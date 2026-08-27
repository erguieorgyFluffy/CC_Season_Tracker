using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;

namespace CCSeasonTracker.Core;

/// <summary>Implements alert rules R1-R10 from the design doc.</summary>
public class AlertEngine
{
    private readonly ModEntry Mod;
    private bool _wasRaining;
    private bool _rainedToastShownToday;

    public AlertEngine(ModEntry mod) => Mod = mod;

    public void ResetDailyFlags() { _wasRaining = false; _rainedToastShownToday = false; }

    /// <summary>Fires once per rainy day, including when a Rain Totem starts rain mid-day.</summary>
    public void CheckRainTransition()
    {
        if (!Context.IsWorldReady) { ResetDailyFlags(); return; }
        bool raining = Mod.Tracker.IsRaining;

        if (raining && !_wasRaining)
        {
            if (Mod.Config.RainToastEnabled && !_rainedToastShownToday)
            {
                var fish = Mod.Tracker.RainFishAvailableNow();
                if (fish.Count > 0)
                    Mod.Toast.Add(T("alert.rainToday",
                        new { items = string.Join(", ", fish.Select(r => r.DisplayName).Take(5)) }), Mod.Config.RainSeconds);
                _rainedToastShownToday = true;
            }
        }
        if (!raining) _rainedToastShownToday = false;
        _wasRaining = raining;
    }

    /// <summary>Evening forecast ping (6:30pm).</summary>
    public string GetForecastAlert()
    {
        if (!Mod.Config.ForecastToastEnabled || !Context.IsWorldReady) return null;
        return IsRainExpectedTomorrow() ? T("alert.rainTomorrow") : null;
    }

    /// <summary>
    /// FIX: Game1.isRainingTomorrow was removed in SDV 1.6. We probe the new
    /// per-location-context weather API via reflection and fall back to the old
    /// field. Degrades gracefully (returns false) if neither exists.
    /// </summary>
    public static bool IsRainExpectedTomorrow()
    {
        try
        {
            var nws = Game1.netWorldState.Value;
            var m = nws.GetType().GetMethod("GetWeatherForLocation", new[] { typeof(int) });
            if (m != null)
            {
                var w = m.Invoke(nws, new object[] { 0 }); // 0 = default location context
                if (w != null)
                    return Flag(w, "IsRain") || Flag(w, "IsStorm") || Flag(w, "IsLightning");
            }
        }
        catch { /* fall through */ }
        try
        {
            var f = typeof(Game1).GetField("isRainingTomorrow", BindingFlags.Public | BindingFlags.Static);
            return f != null && f.GetValue(null) is true;
        }
        catch { return false; }
    }

    private static bool Flag(object obj, string name)
    {
        try
        {
            MemberInfo mi = (MemberInfo)obj.GetType().GetProperty(name)
                            ?? obj.GetType().GetField(name);
            if (mi == null) return false;
            object v = mi is PropertyInfo pi ? pi.GetValue(obj) : ((FieldInfo)mi).GetValue(obj);
            if (v == null) return false;
            var valProp = v.GetType().GetProperty("Value");   // unwrap NetBool if needed
            if (valProp != null && valProp.PropertyType == typeof(bool))
                v = valProp.GetValue(v);
            return v is true;
        }
        catch { return false; }
    }

    /// <summary>Morning digest: season-end sweep, planting deadlines, cart day, Y1 cabbage, infrastructure.</summary>
    public IEnumerable<string> GetDayStartedAlerts()
    {
        if (!Context.IsWorldReady) yield break;
        var tr = Mod.Tracker;
        var cfg = Mod.Config;
        string season = tr.CurrentSeason();
        int daysLeft = tr.DaysLeftInSeason();

        // R3 - season-exclusive sweep
        var exclusive = tr.ForSeason(season).Where(r => r.Meta.seasons.Length == 1).ToList();
        if (exclusive.Count > 0 && daysLeft <= 7)
            yield return T("alert.seasonEnd", new
            {
                days = daysLeft,
                season,
                items = string.Join(", ", exclusive.Take(3).Select(r => r.DisplayName)) +
                        (exclusive.Count > 3 ? "..." : "")
            });

        // R1 - planting deadlines
        if (cfg.DeadlineReminders)
        {
            var crops = tr.ForSeason(season)
                          .Where(r => r.Meta.cropDays > 0)
                          .OrderBy(r => r.Meta.cropDays).ToList();
            foreach (var c in crops.Where(c => c.Meta.regrowDays <= 0))
            {
                if (c.Meta.cropDays == daysLeft)
                    yield return T("alert.lastPlantDay", new { item = c.DisplayName });
                else if (c.Meta.cropDays > daysLeft)
                    yield return T("alert.tooLate", new { item = c.DisplayName, days = c.Meta.cropDays });
            }
        }

        // R9/R10 - Year-1 Red Cabbage guarantee countdown
        int? visits = tr.Y1CabbageVisitsLeft();
        if (visits.HasValue)
        {
            if (visits == 0 && tr.IsCartDayToday())
                yield return T("alert.cabbageToday");
            else if (Mod.Config.CartDayReminder && visits <= 2)
                yield return T("alert.cabbageSoon", new { visits });
        }

        // R6 - Traveling Cart regular days (Friday = dow 4, Sunday = dow 6)
        if (cfg.CartDayReminder)
        {
            int dow = (Game1.dayOfMonth - 1) % 7; // 0=Mon .. 6=Sun
            if ((dow == 4 || dow == 6) && !(visits == 0))
            {
                var cart = tr.CartCandidates().Take(4).Select(r => r.DisplayName).ToList();
                if (cart.Count > 0)
                    yield return T("alert.cartDay", new { items = string.Join(", ", cart) });
            }
        }

        // R7 - pig prerequisite (FIX: owning a Pig implies a Deluxe Barn exists,
        // so we no longer touch the 1.6-removed Building.upgradeLevel API)
        if (cfg.InfrastructureReminder && NeedTruffle(tr) && !HasPig())
            yield return T("alert.pig");

        // R2 - quality-crop status (informational)
        if (Mod.Config.GoldQualityReminders)
        {
            foreach (var r in tr.ForSeason(season).Where(r => r.NeedsQuality && r.HaveCount < r.Src.Stack).Take(2))
                yield return $"{r.DisplayName} (gold): {Math.Min(r.HaveCount, r.Src.Stack)}/{r.Src.Stack}";
        }
    }

    private static bool NeedTruffle(TrackerService tr) =>
        tr.AllMissing.Any(r => string.Equals(r.DisplayName, "Truffle", StringComparison.OrdinalIgnoreCase));

    private static bool HasPig()
    {
        try { return Game1.getFarm()?.getAllFarmAnimals().Any(a => a.type.Value == "Pig") == true; }
        catch { return false; }
    }

    /// <summary>Safe translation lookup: falls back to the key name instead of crashing.</summary>
    private string T(string key, object tokens = null)
    {
        string result = Mod.Helper.Translation.Get(key, tokens);
        return string.IsNullOrWhiteSpace(result) ? key : result;
    }
}
