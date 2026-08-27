using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;   // FIX: SpriteText
using StardewValley.Menus;             // FIX: IClickableMenu
using CCSeasonTracker.Core;

namespace CCSeasonTracker.Ui;

/// <summary>Compact overlay; hidden unless enabled (default OFF), corner + offsets configurable.</summary>
public class HudOverlay
{
    private readonly ModEntry Mod;
    public HudOverlay(ModEntry mod) => Mod = mod;

    public void Draw(SpriteBatch b)
    {
        if (!Mod.Config.HudVisible || !StardewModdingAPI.Context.IsWorldReady) return;
        var tr = Mod.Tracker;
        if (tr.AllMissing.Count == 0 && !tr.Groups.Any(g => !g.Complete)) return;

        string season = tr.CurrentSeason();
        int missing = tr.AllMissing.Count;

        int pct = 0;
        if (Mod.Tracker.SlotsRequired > 0)
        {
            pct = Mod.Tracker.SlotsDonated * 100 / Mod.Tracker.SlotsRequired;
            if (pct > 100) pct = 100;
        }
        string line1 = Mod.Config.HudShowMissing
            ? "CC [" + season + "]: " + missing + " missing" + (Mod.Config.HudShowPercent ? " (" + pct + "%)" : "")
            : null;
        string line2 = null, line3 = null;

        if (tr.IsRaining && Mod.Config.HudShowRain)
        {
            var rain = tr.RainFishAvailableNow().Select(r => r.DisplayName).Take(3).ToList();
            if (rain.Count > 0) line2 = "RAIN NOW: " + string.Join(", ", rain);
        }
        var gold = tr.ForSeason(season).FirstOrDefault(r => r.NeedsQuality && r.HaveCount >= 0 && r.HaveCount < r.Src.Stack);
        if (Mod.Config.HudShowGold && Mod.Config.GoldQualityReminders && gold != null) line3 = $"{gold.DisplayName} gold: {gold.HaveCount}/{gold.Src.Stack}";

        var lines = (new[] { line1, line2, line3 }).Where(l => l != null).ToArray();
        if (lines.Length == 0) return;
        if (!Mod.Config.HudShowSummaryAlways && lines.Length == 1) return;

        int w = lines.Max(l => SpriteText.getWidthOfString(l)) + 28;
        int h = lines.Sum(l => SpriteText.getHeightOfString(l)) + 20;

        var vp = Game1.uiViewport;
        var cornerEnum = (ScreenCorner)System.Math.Max(0, System.Math.Min(3, (int)Mod.Config.HudCorner));
        bool leftSide = cornerEnum == ScreenCorner.TopLeft || cornerEnum == ScreenCorner.BottomLeft;
        bool topSide  = cornerEnum == ScreenCorner.TopLeft || cornerEnum == ScreenCorner.TopRight;

        int x = leftSide ? Mod.Config.HudOffsetX : vp.Width - w - Mod.Config.HudOffsetX;
        int y = topSide  ? Mod.Config.HudOffsetY : vp.Height - h - Mod.Config.HudOffsetY;

        IClickableMenu.drawTextureBox(b, x, y, w, h, Color.White * 0.95f);
        int ty = y + 10;
        foreach (var l in lines)
        {
            SpriteText.drawString(b, l, x + 14, ty);
            ty += SpriteText.getHeightOfString(l) + 2;
        }
    }
}
