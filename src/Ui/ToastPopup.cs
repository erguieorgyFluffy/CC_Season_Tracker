using System;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CCSeasonTracker.Ui;

/// <summary>
/// Notification front-end. Since round 9 we delegate entirely to the game's own
/// corner textbox HUD messages (vanilla look, vanilla wrapping + queueing).
/// The old custom-drawn boxes suffered SpriteText wrapping quirks (empty boxes).
/// Update/Draw are kept as no-ops so existing call sites stay untouched.
/// </summary>
public class ToastPopup
{
    public void ResetDailyFlags() { /* vanilla queues its own messages */ }

    public void Add(string message, int seconds)
    {
        if (string.IsNullOrEmpty(message)) return;
        try { Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message)); }
        catch { /* never let a notification crash the mod */ }
    }

    public void Update() { /* no-op: vanilla updates its own HUD messages */ }

    public void Draw(SpriteBatch b) { /* no-op: vanilla draws its own HUD messages */ }
}
