using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using CCSeasonTracker.Core;

namespace CCSeasonTracker;

/// <summary>
/// Supplies the live "CC Tracker" section to ToDew's overlay.
/// Items are computed from game state, so marking one done simply
/// dismisses it until the next day (it returns on its own if the
/// state changes, e.g. rain starts or the season rolls over).
/// </summary>
public class TodewSource : ToDew.IToDewOverlayDataSource
{
    private readonly ModEntry Mod;
    private readonly HashSet<string> Dismissed = new();
    private string DayKey = "";

    public TodewSource(ModEntry mod)
    {
        Mod = mod;
    }

    public string GetSectionTitle()
    {
        return "CC Tracker";
    }

    public List<(string text, bool isBold, Action onDone)> GetItems(int limit)
    {
        var list = new List<(string, bool, Action)>();
        if (!Context.IsWorldReady) return list;
        if (!Mod.Config.TodewSectionEnabled) return list;
        if (Mod.Tracker.IsJojaTakeover()) return list;
        try
        {
            var tr = Mod.Tracker;
            string dayKey = Game1.year + "-" + tr.CurrentSeason() + "-" + Game1.dayOfMonth;
            if (dayKey != DayKey)
            {
                DayKey = dayKey;
                Dismissed.Clear();
            }
            void Add(string key, string text, bool bold)
            {
                if (list.Count >= limit) return;
                if (Dismissed.Contains(key)) return;
                list.Add((text, bold, () =>
                {
                    Dismissed.Add(key);
                    try { Mod.Todew.RefreshOverlay(); } catch { }
                }));
            }
            foreach (var r in (Mod.Config.TodewShowLastChance ? tr.LastChanceRows() : new List<Row>()))
            {
                Add("lc" + r.DisplayName + r.Src.BundleKey,
                    "LAST CHANCE: " + r.DisplayName + " (" + tr.DaysLeftInSeason() + "d)", true);
            }
            if (tr.IsRaining)
            {
                foreach (var r in (Mod.Config.TodewShowRainFish ? tr.RainFishAvailableNow() : new List<Row>()))
                {
                    Add("rf" + r.DisplayName + r.Src.BundleKey,
                        "Fish NOW: " + r.DisplayName, true);
                }
            }
            foreach (var r in (Mod.Config.TodewShowDonateReady ? tr.ReadyToDonate() : new List<Row>()))
            {
                Add("rd" + r.DisplayName + r.Src.BundleKey,
                    "Donate: " + r.DisplayName + " (have it)", false);
            }
            foreach (var r in (Mod.Config.TodewShowPlant ? tr.ForSeason(tr.CurrentSeason()) : new List<Row>()))
            {
                if (r.Meta != null && r.Meta.type == "crop" && r.Meta.cropDays > 0
                    && !TrackerService.IsReady(r))
                {
                    Add("pt" + r.DisplayName + r.Src.BundleKey,
                        "Plant: " + r.DisplayName + " (" + r.Meta.cropDays + "d)", false);
                }
            }
        }
        catch
        {
            // never let the overlay integration break anything
        }
        return list;
    }
}
