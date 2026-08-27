using System;
using System.Collections.Generic;

namespace CCSeasonTracker;

public class ItemMeta
{
    public string key;
    public string type = "misc";
    public string[] seasons = Array.Empty<string>();
    public string location = "";
    public string time = "";
    public string weather = "";
    public string[] tags = Array.Empty<string>();
    public int cropDays;
    public int regrowDays;
}

public class ItemDbFile { public List<ItemMeta> items = new(); }

/// <summary>One raw requirement parsed from the live bundle data.</summary>
public class Requirement
{
    public string Area, BundleKey, BundleName;
    public int BundleId;                              // VERIFIED: global id from the "Area/Id" key (0-36)
    public int SlotIndex;
    public string Token;
    public int Stack, MinQuality;
    public bool IsGoldAmount;
    public bool DonatedBit;
    public int RequiredCount;
}

/// <summary>A missing thing shown in the UI.</summary>
public class Row
{
    public Requirement Src;
    public string DisplayName;
    public ItemMeta Meta;
    public string MetaFallback;
    public int HaveCount = -1;
    public bool NeedsQuality => Src != null && !Src.IsGoldAmount && Src.MinQuality >= 2;
}
