using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;

namespace CCSeasonTracker.Core;

public class ItemDatabase
{
    private readonly Dictionary<string, ItemMeta> ByName =
        new(StringComparer.OrdinalIgnoreCase);

    public ItemDatabase(IModHelper helper)
    {
        var db = helper.Data.ReadJsonFile<ItemDbFile>("assets/items.json") ?? new ItemDbFile();
        foreach (var m in db.items)
            if (!string.IsNullOrEmpty(m.key))
                ByName[m.key] = m;
    }

    public ItemMeta Get(string displayName) =>
        displayName != null && ByName.TryGetValue(displayName, out var m) ? m : null;

    /// <summary>Resolve an item token like "(O)24" to its localized display name at runtime.</summary>
    public static string ResolveName(string token)
    {
        try { return ItemRegistry.GetDataOrErrorItem(token).DisplayName; }
        catch { return token; }
    }
}
