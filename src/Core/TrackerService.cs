using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace CCSeasonTracker.Core;

/// <summary>Caches the full "what is missing" model; rebuilt on load/inventory-change/day-start/panel-open.</summary>
public class TrackerService
{
    public List<BundleGroup> Groups = new();
    public List<Row> AllMissing = new();

    public int BundlesTotal, BundlesDone, SlotsRequired, SlotsDonated;

    private static readonly HashSet<string> LoggedUnknown = new();
    private static bool _loggedStorage;

    public void Rebuild(ItemDatabase db)
    {
        Groups.Clear();
        AllMissing.Clear();
        BundlesTotal = BundlesDone = SlotsRequired = SlotsDonated = 0;
        if (!Context.IsWorldReady) return;

        if (!_loggedStorage)
        {
            _loggedStorage = true;
            BundleParser.LogStorageSummary();
        }

        var reqs = BundleParser.Parse();

        foreach (var g in reqs.GroupBy(r => r.BundleKey))
        {
            var first = g.First();
            var grp = new BundleGroup
            {
                Area = first.Area,
                BundleKey = first.BundleKey,
                BundleName = first.BundleName,
                TotalSlots = g.Count(),
                RequiredCount = first.RequiredCount > 0 ? first.RequiredCount : g.Count()
            };
            grp.DonatedSlots = g.Count(r => r.DonatedBit);
            grp.Complete = grp.DonatedSlots >= grp.RequiredCount;
            grp.RoomComplete = IsRoomDone(grp.Area);
            grp.IsMissingBundle = first.Area == "Abandoned Joja Mart";

            if (!grp.IsMissingBundle)
            {
                BundlesTotal++;
                SlotsRequired += grp.RequiredCount;
                SlotsDonated += Math.Min(grp.DonatedSlots, grp.RequiredCount);
                if (grp.Complete || grp.RoomComplete) BundlesDone++;
            }

            if (grp.Complete || grp.RoomComplete) continue;

            foreach (var r in g.Where(r => !r.DonatedBit))
            {
                var row = new Row
                {
                    Src = r,
                    DisplayName = r.IsGoldAmount ? r.Stack.ToString("N0") + "g" : ItemDatabase.ResolveName(r.Token),
                };
                row.Meta = r.IsGoldAmount ? null : db.Get(row.DisplayName);

                if (!r.IsGoldAmount && row.DisplayName.Equals("Red Cabbage", StringComparison.OrdinalIgnoreCase))
                {
                    var m = new ItemMeta { type = "crop", seasons = Array.Empty<string>(), cropDays = 9,
                                           location = "", tags = new[] { "cart" } };
                    int? v = Y1CabbageVisitsLeft();
                    if (v.HasValue)                          m.location = $"Traveling Cart - GUARANTEED within {v} cart visit(s)";
                    else if (!v.HasValue && Game1.year >= 2) m.location = "Pierre's sells them in Year 2 / cart / Seed Maker";
                    else                                     m.location = "Year 1: buy seeds at the Traveling Cart (Fri/Sun), or rare Skull Cavern drop";
                    row.Meta = m;
                }

                if (!r.IsGoldAmount && row.Meta == null)
                {
                    row.MetaFallback = "(no data)";
                    if (LoggedUnknown.Add(row.DisplayName))
                        ModEntry.Instance?.Monitor.Log(
                            $"[CCST] No strategy data for '{row.DisplayName}' ({r.Area} - {r.BundleName})",
                            LogLevel.Info);
                }

                // scan EVERYTHING (inventory + bin + all chests) for every missing row
                if (!r.IsGoldAmount)
                    row.HaveCount = CountAnywhere(r.Token, r.MinQuality);

                grp.Rows.Add(row);
                AllMissing.Add(row);
            }
            Groups.Add(grp);
        }
    }

    public static bool IsReady(Row r) => !r.Src.IsGoldAmount && r.HaveCount >= r.Src.Stack;

    public bool IsLastChance(Row r)
    {
        var m = r.Meta;
        if (m == null || m.seasons.Length != 1) return false;
        if (!m.seasons[0].Equals(CurrentSeason(), StringComparison.OrdinalIgnoreCase)) return false;
        return DaysLeftInSeason() <= 3 && !IsReady(r);
    }

    public List<Row> LastChanceRows() => AllMissing.Where(IsLastChance).ToList();

    public List<Row> ReadyToDonate() => AllMissing.Where(IsReady).ToList();

    public bool IsJojaMember()
    {
        try { return (Game1.MasterPlayer ?? Game1.player).mailReceived.Contains("JojaMember"); }
        catch { return false; }
    }

    public bool IsJojaTakeover()
    {
        try
        {
            var mail = (Game1.MasterPlayer ?? Game1.player).mailReceived;
            return mail.Contains("JojaMember")
                && (mail.Contains("movieTheaterJoja") || mail.Contains("ccMovieTheater"));
        }
        catch { return false; }
    }

    private static bool IsRoomDone(string area)
    {
        try
        {
            if (Game1.getLocationFromName("CommunityCenter") is CommunityCenter cc)
            {
                return area switch
                {
                    "Fish Tank"         => cc.areasComplete[CommunityCenter.AREA_FishTank],
                    "Pantry"            => cc.areasComplete[CommunityCenter.AREA_Pantry],
                    "Crafts Room"       => cc.areasComplete[CommunityCenter.AREA_CraftsRoom],
                    "Boiler Room"       => cc.areasComplete[CommunityCenter.AREA_BoilerRoom],
                    "Vault"             => cc.areasComplete[CommunityCenter.AREA_Vault],
                    "Bulletin Board"    => cc.areasComplete[CommunityCenter.AREA_Bulletin],
                    "Abandoned Joja Mart" => (Game1.MasterPlayer ?? Game1.player).mailReceived.Contains("movieTheaterJoja") ||
                                                (Game1.MasterPlayer ?? Game1.player).mailReceived.Contains("ccMovieTheater"),
                    _ => false
                };
            }
        }
        catch { /* fall back to mail flags */ }

        try
        {
            var mail = Game1.MasterPlayer?.mailReceived ?? Game1.player.mailReceived;
            return area switch
            {
                "Fish Tank" => mail.Contains("ccFishTank"),
                "Pantry" => mail.Contains("ccPantry"),
                "Crafts Room" => mail.Contains("ccCraftsRoom"),
                "Boiler Room" => mail.Contains("ccBoilerRoom"),
                "Vault" => mail.Contains("ccVault"),
                "Bulletin Board" => mail.Contains("ccBulletin"),
                _ => false
            };
        }
        catch { return false; }
    }

    /// <summary>Counts matching items across inventory, shipping bin and every chest we can reach.</summary>
    public static int CountAnywhere(string token, int minQuality)
    {
        int count = 0;
        void Scan(IEnumerable<Item> items)
        {
            foreach (var it in items)
                if (it is StardewValley.Object o && o.QualifiedItemId == token && o.Quality >= minQuality)
                    count += o.Stack;
        }
        void ScanChests(GameLocation loc)
        {
            if (loc == null) return;
            foreach (var obj in loc.Objects.Values)
                if (obj is StardewValley.Objects.Chest c) Scan(c.Items.Where(i => i != null));
        }
        try
        {
            Scan(Game1.player.Items.Where(i => i != null));

            var farm = Game1.getFarm();
            var bin = farm?.getShippingBin(Game1.MasterPlayer ?? Game1.player);
            if (bin != null) Scan(bin.Where(i => i != null));

            if (farm != null)
            {
                ScanChests(farm);
                ScanChests(Game1.getLocationFromName("FarmHouse"));
                ScanChests(Game1.getLocationFromName("Greenhouse"));
                foreach (var b in farm.buildings)
                    if (b.indoors.Value != null)
                        ScanChests(b.indoors.Value);
            }
        }
        catch { /* best effort */ }
        return count;
    }

    /// <summary>
    /// Scans tilled farm soil: harvest-item-id -> estimated days-until-harvest per living crop.
    /// VERIFIED against 1.6 decompiled Crop: currentPhase/dayOfCurrentPhase (NetInt),
    /// phaseDays (NetIntList, trailing 99999 sentinel = mature marker), fullyGrown/dead (NetBool).
    /// Covers outdoor farm plots only (not greenhouse/garden pots). Best effort.
    /// </summary>
    public static Dictionary<int, List<int>> PlantedCropsByItemId()
    {
        var map = new Dictionary<int, List<int>>();
        try
        {
            var farm = Game1.getFarm();
            if (farm == null) return map;
            foreach (var tf in farm.terrainFeatures.Values)
            {
                if (tf is not StardewValley.TerrainFeatures.HoeDirt dirt || dirt.crop == null) continue;
                StardewValley.Crop crop = dirt.crop;
                if (crop.dead.Value) continue;
                string hs = crop.indexOfHarvest.Value; if (string.IsNullOrEmpty(hs)) continue; if (hs.StartsWith("(")) { int cl = hs.IndexOf((char)41); if (cl >= 0) hs = hs.Substring(cl + 1); } int id; if (!int.TryParse(hs, out id) || id <= 0) continue;
                if (id <= 0) continue;
                if (!map.TryGetValue(id, out var list)) map[id] = list = new List<int>();
                list.Add(EstimateDaysToHarvest(crop));
            }
        }
        catch { /* best effort */ }
        return map;
    }

    private static int EstimateDaysToHarvest(StardewValley.Crop crop)
    {
        try
        {
            int total = crop.phaseDays.Count;
            if (total <= 0) return -1;
            if (crop.fullyGrown.Value)          // harvested once; dayOfCurrentPhase counts regrow down
                return Math.Max(0, crop.dayOfCurrentPhase.Value);
            int days = 0;                        // growing phases exclude the trailing 99999 sentinel
            for (int i = Math.Max(0, crop.currentPhase.Value); i < total - 1; i++)
                days += crop.phaseDays[i];
            return Math.Max(0, days - crop.dayOfCurrentPhase.Value);
        }
        catch { return -1; }
    }
    // ---------- convenience queries ----------

    public string CurrentSeason() => Game1.season.ToString();
    public int DaysLeftInSeason() => Math.Max(0, 28 - Game1.dayOfMonth);
    public bool IsRaining => Game1.isRaining || Game1.isLightning;

    public bool IsCartDayToday()
    {
        int dow = (Game1.dayOfMonth - 1) % 7;
        if (dow == 4 || dow == 6) return true;
        return Game1.dayOfMonth is >= 15 and <= 17 && Game1.season == Season.Winter;
    }

    public int? Y1CabbageVisitsLeft()
    {
        try
        {
            int v = Game1.netWorldState.Value.VisitsUntilY1Guarantee;
            return v >= 0 ? v : null;
        }
        catch { return null; }
    }

    public List<Row> ForSeason(string season) =>
        AllMissing.Where(r => r.Meta?.seasons?
            .Contains(season, StringComparer.OrdinalIgnoreCase) == true).ToList();

    public List<Row> Unknown() =>
        AllMissing.Where(r => r.Meta == null && r.Src is { IsGoldAmount: false }).ToList();

    public List<Row> RainFishAvailableNow() =>
        ForSeason(CurrentSeason()).Where(r => r.Meta?.weather == "rain-only").ToList();

    public List<Row> CartCandidates() =>
        AllMissing.Where(r => r.Meta?.tags?.Contains("cart") == true).ToList();

    public (long have, long goal) VaultProgress()
    {
        long have = 0, goal = 0;
        foreach (var grp in Groups.Where(g => g.Area == "Vault"))
            foreach (var row in grp.Rows)
            {
                long amt = ParseGold(row.DisplayName);
                goal += amt;
                if (grp.DonatedSlots > 0 && row.Src.SlotIndex < grp.DonatedSlots) have += amt;
            }
        return (have, goal);
    }

    private static long ParseGold(string s)
    {
        var t = (s ?? "").Replace(",", "").Replace("g", "").Replace("G", "").Trim();
        return long.TryParse(t, out long v) ? v : 0;
    }
}

public class BundleGroup
{
    public string Area, BundleKey, BundleName;
    public int TotalSlots, RequiredCount, DonatedSlots;
    public bool Complete, RoomComplete, IsMissingBundle;
    public List<Row> Rows = new();
    public string ProgressLabel => Complete
        ? "complete"
        : $"{DonatedSlots}/{RequiredCount}" + (TotalSlots > RequiredCount ? $" (of {TotalSlots} options)" : "");
}
