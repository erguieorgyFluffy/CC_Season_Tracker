using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace CCSeasonTracker.Core;

/// <summary>
/// Parses Game1.netWorldState.Value.BundleData live.
/// VERIFIED: keys are "Area/Id" where Id is the GLOBAL bundle number; donation state
/// lives in netWorldState.Value.Bundles, keyed by the same Id, values are bool[] per slot.
/// IMPORTANT: we use the dictionary's OWN ContainsKey/indexer (proven to exist by our
/// own earlier compile logs) - the previous IDictionary cast compiled but threw at
/// runtime, and the silent catch made every bundle read as "nothing donated".
/// </summary>
public static class BundleParser
{
    private static readonly HashSet<string> LoggedOnce = new();

    private static void LogOnce(string msg)
    {
        if (LoggedOnce.Add(msg))
            ModEntry.Instance?.Monitor.Log("[CCST] " + msg, LogLevel.Warn);
    }

    public static List<Requirement> Parse()
    {
        var result = new List<Requirement>();
        if (!Context.IsWorldReady) return result;

        try
        {
            var bundleData = Game1.netWorldState.Value.BundleData;

            foreach (var kv in bundleData)
            {
                string[] keyParts = kv.Key.Split('/');
                string area = keyParts[0];
                int bundleId = int.Parse(keyParts[1]);

                string[] f = kv.Value.Split('/');
                string name = f.Length > 0 ? f[0] : kv.Key;
                string requirements = f.Length > 2 ? f[2] : "";

                int requiredCount = -1;
                if (f.Length > 4 && int.TryParse(f[4].Trim(), out int rc) && rc > 0)
                    requiredCount = rc;

                var tokens = requirements.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int slot = 0;
                for (int i = 0; i + 2 < tokens.Length; i += 3)
                {
                    string id = StripQualifier(tokens[i]);
                    int.TryParse(tokens[i + 1], out int stack);
                    int.TryParse(tokens[i + 2], out int quality);
                    bool gold = id == "-1";

                    result.Add(new Requirement
                    {
                        Area = area,
                        BundleKey = kv.Key,
                        BundleName = name,
                        BundleId = bundleId,
                        SlotIndex = slot,
                        Token = gold ? "-1" : "(O)" + id,
                        Stack = stack,
                        MinQuality = quality,
                        IsGoldAmount = gold,
                        DonatedBit = ReadDonationBit(bundleId, slot),
                        RequiredCount = requiredCount
                    });
                    slot++;
                }
            }
        }
        catch (Exception ex)
        {
            ModEntry.Instance?.Monitor.Log("Bundle parse failed: " + ex, LogLevel.Warn);
        }
        return result;
    }

    private static bool ReadDonationBit(int bundleId, int slot)
    {
        try
        {
            var bundles = Game1.netWorldState.Value.Bundles;
            if (bundles != null && bundles.ContainsKey(bundleId))
            {
                bool[] flags = bundles[bundleId];   // native indexer -> bool[]
                if (flags != null && slot >= 0 && slot < flags.Length)
                    return flags[slot];
            }
        }
        catch (Exception ex)
        {
            LogOnce($"donation read failed for bundle {bundleId} slot {slot}: {ex.Message}");
        }
        return false;
    }

    /// <summary>One-time diagnostic: proves what the storage looks like on this save.</summary>
    public static void LogStorageSummary()
    {
        try
        {
            var bundles = Game1.netWorldState.Value.Bundles;
            bool has0 = bundles.ContainsKey(0);
            int len0 = -1, donated = -1;
            if (has0)
            {
                bool[] a = bundles[0];
                len0 = a?.Length ?? -1;
                donated = a?.Count(b => b) ?? -1;
            }
            ModEntry.Instance?.Monitor.Log(
                $"[CCST] Storage: type={bundles.GetType().Name}, bundle0 present={has0}, bundle0 slots={len0}, bundle0 donated={donated}",
                LogLevel.Info);
        }
        catch (Exception ex)
        {
            ModEntry.Instance?.Monitor.Log("[CCST] Storage probe failed: " + ex.Message, LogLevel.Warn);
        }
    }

    private static string StripQualifier(string token)
    {
        if (token.StartsWith("("))
        {
            int close = token.IndexOf(')');
            if (close >= 0) return token.Substring(close + 1);
        }
        return token;
    }
}
