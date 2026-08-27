using StardewModdingAPI;

namespace CCSeasonTracker;

public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public class ModConfig
{
    // --- Panel ---
    public bool PanelBlocksMovement { get; set; } = false;   // false = walkable overlay (default)

    // --- HUD ---
    public bool HudVisible { get; set; } = false;
    public ScreenCorner HudCorner { get; set; } = ScreenCorner.TopLeft;
    public int HudOffsetX { get; set; } = 16;
    public int HudOffsetY { get; set; } = 16;
    public bool HudShowSummaryAlways { get; set; } = true;
    public bool HudShowMissing { get; set; } = true;
    public bool HudShowPercent { get; set; } = true;
    public bool HudShowRain { get; set; } = true;
    public bool HudShowGold { get; set; } = true;

    // --- Hotkeys ---
    public SButton TogglePanelKey { get; set; } = SButton.F6;
    public SButton ToggleHudKey { get; set; } = SButton.V;

    // --- Alerts ---
    public bool RainToastEnabled { get; set; } = true;
    public bool ForecastToastEnabled { get; set; } = true;
    public bool DeadlineReminders { get; set; } = true;
    public bool CartDayReminder { get; set; } = true;
    public bool InfrastructureReminder { get; set; } = true;
    public int ToastSeconds { get; set; } = 8;

    // --- ToDew overlay section ---
    public bool TodewSectionEnabled { get; set; } = true;
    public bool TodewShowLastChance { get; set; } = true;
    public bool TodewShowRainFish { get; set; } = true;
    public bool TodewShowDonateReady { get; set; } = true;
    public bool TodewShowPlant { get; set; } = true;
    public bool LastChanceEnabled { get; set; } = true;
    public bool GoldQualityReminders { get; set; } = true;
    public int RainSeconds { get; set; } = 8;
    public int ForecastSeconds { get; set; } = 8;
    public int LastChanceSeconds { get; set; } = 8;
    public int CartSeconds { get; set; } = 8;
    public int DeadlineSeconds { get; set; } = 8;
}
