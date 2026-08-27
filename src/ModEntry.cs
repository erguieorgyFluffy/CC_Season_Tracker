using System;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using CCSeasonTracker.Core;
using CCSeasonTracker.Ui;

namespace CCSeasonTracker;

public class ModEntry : Mod
{
    internal static ModEntry Instance;
    internal bool LeftHeld;

    internal ModConfig Config;
    internal ItemDatabase Db;
    internal TrackerService Tracker = new();
    internal AlertEngine Alerts;
    internal ToastPopup Toast = new();
    internal HudOverlay Hud;
    internal ToDew.IToDewApi Todew;

    private bool _panelWasDown;
    private bool _hudWasDown;

    internal string RoomsPngPath;
    internal bool OverlayOpen;
    internal PanelMenu OverlayPanel;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        Db = new ItemDatabase(helper);
        Alerts = new AlertEngine(this);
        Hud = new HudOverlay(this);
        RoomsPngPath = System.IO.Path.Combine(helper.DirectoryPath, "assets", "rooms.png");

        helper.Events.GameLoop.SaveLoaded += (s, e) => { Refresh(); OverlayOpen = false; };

        helper.Events.GameLoop.DayStarted += (s, e) =>
        {
            Refresh();
            Toast.ResetDailyFlags();
            NotifyLastChance();
        };

        helper.Events.Player.InventoryChanged += (s, e) => Refresh();

        helper.Events.GameLoop.TimeChanged += (s, e) =>
        {
            Refresh();
            OnTimeChanged(s, e);
        };

        helper.Events.GameLoop.OneSecondUpdateTicked += (s, e) =>
        {
            Alerts.CheckRainTransition();
            Toast.Update();
        };

        helper.Events.Display.RenderedHud += (s, e) =>
        {
            Hud.Draw(e.SpriteBatch);
            Toast.Draw(e.SpriteBatch);
            if (OverlayOpen && Game1.activeClickableMenu == null && OverlayPanel != null)
                OverlayPanel.draw(e.SpriteBatch);
        };

        helper.Events.Input.ButtonPressed += (s, e) =>
        {
            if (e.Button == SButton.MouseLeft) LeftHeld = true;
            if (!OverlayOpen || Game1.activeClickableMenu != null || OverlayPanel == null) return;
            var (mx, my) = UiMouse();

            if (e.Button == SButton.MouseLeft && OverlayPanel.Contains(mx, my))
            {
                OverlayPanel.receiveLeftClick(mx, my);
                Helper.Input.Suppress(SButton.MouseLeft);
                if (OverlayPanel.WantsClose) OverlayOpen = false;
                return;
            }
            if (e.Button == SButton.Escape)
            {
                OverlayOpen = false;
                Helper.Input.Suppress(SButton.Escape);
            }
        };

        helper.Events.Input.MouseWheelScrolled += (s, e) =>
        {
            if (!OverlayOpen || Game1.activeClickableMenu != null || OverlayPanel == null) return;
            var (mx, my) = UiMouse();
            if (OverlayPanel.Contains(mx, my))
            {
                OverlayPanel.receiveScrollWheelAction(Math.Sign(e.Delta));
                Helper.Input.SuppressScrollWheel();
            }
        };

        helper.Events.Input.ButtonReleased += (s, e) =>
        {
            if (e.Button == SButton.MouseLeft)
            {
                LeftHeld = false;
            }
            if (e.Button == SButton.MouseLeft && OverlayOpen && OverlayPanel != null)
                OverlayPanel.EndDrag();   // guaranteed drag end, independent of mouse-state filtering
        };

        helper.Events.GameLoop.UpdateTicked += (s, e) =>

        {
            if (!Context.IsWorldReady) { _panelWasDown = _hudWasDown = false; return; }

            if (OverlayOpen && Game1.activeClickableMenu != null)
                OverlayOpen = false;   // yield to real menus/dialogs

            if (OverlayOpen && OverlayPanel != null)
            {
                var (hx, hy) = UiMouse();
                OverlayPanel.performHoverAction(hx, hy);

                // FIX v2: SMAPI suppression hides MouseLeft from Game1.input AND IsDown(),
                // so we read the raw OS mouse state - it cannot be filtered.
                bool leftHeld = LeftHeld || Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton == ButtonState.Pressed;
                OverlayPanel.UpdateDrag(leftHeld, hy);
            }

            bool panelDown = helper.Input.IsDown(Config.TogglePanelKey);
            bool hudDown = helper.Input.IsDown(Config.ToggleHudKey);

            if (panelDown && !_panelWasDown && (Context.IsPlayerFree || OverlayOpen))
                TogglePanel();

            if (hudDown && !_hudWasDown && Context.IsPlayerFree)
            {
                Config.HudVisible = !Config.HudVisible;
                helper.WriteConfig(Config);
                Toast.Add(Config.HudVisible ? "HUD on" : "HUD off", 2);
            }

            _panelWasDown = panelDown;
            _hudWasDown = hudDown;
        };

        helper.Events.GameLoop.GameLaunched += (s, e) =>
        {
            SetupGmcm();
            SetupTodew();
        };

        Monitor.Log("CC Season Tracker loaded. Press F6 in-game.", LogLevel.Info);
    }

    internal (int, int) UiMouse()
    {
        var pos = Helper.Input.GetCursorPosition();
        var ui = Utility.ModifyCoordinatesForUIScale(pos.ScreenPixels);
        return ((int)ui.X, (int)ui.Y);
    }

    internal void NotifyLastChance()
    {
        if (!Context.IsWorldReady) return;
        if (!PanelMenu.CcWizardDone()) return;
        if (Tracker.IsJojaTakeover()) return;
        var rows = Tracker.LastChanceRows();
        if (rows.Count == 0) return;
        string names = "";
        int shown = 0;
        foreach (var r in rows)
        {
            if (shown >= 2) break;
            if (names.Length > 0)
            {
                names += ", ";
            }
            names += r.DisplayName;
            shown++;
        }
        int extra = rows.Count - shown;
        if (extra > 0)
        {
            names += " + " + extra + " more";
        }
        int days = Tracker.DaysLeftInSeason();
        string dayWord = days == 1 ? "day" : "days";
        Toast.Add("LAST CHANCE: " + names + " - " + days + " " + dayWord + " left!", Config.LastChanceSeconds);
    }

    internal void Refresh()
    {
        Tracker.Rebuild(Db);
        if (Todew != null)
        {
            try
            {
                Todew.RefreshOverlay();
            }
            catch
            {
            }
        }
    }

    internal void TogglePanel()
    {
        if (OverlayOpen) { OverlayOpen = false; return; }
        if (Context.IsPlayerFree)
        {
            Refresh();
            OverlayPanel = new PanelMenu(this);
            OverlayOpen = true;
        }
    }

    private void OnTimeChanged(object sender, TimeChangedEventArgs e)
    {
        if (e.NewTime == 1830)
        {
            var msg = Alerts.GetForecastAlert();
            if (msg != null) Toast.Add(msg, Config.ForecastSeconds);
        }
    }

    private void SetupTodew()
    {
        try
        {
            var api = Helper.ModRegistry.GetApi<ToDew.IToDewApi>("jltaylor-us.ToDew")
                ?? Helper.ModRegistry.GetApi<ToDew.IToDewApi>("jltaylor-us.StardewToDew");
            if (api == null)
            {
                Monitor.Log("ToDew not found - the CC Tracker overlay section is disabled.", LogLevel.Info);
                return;
            }
            Todew = api;
            api.AddOverlayDataSource(new TodewSource(this));
            Monitor.Log("ToDew found - CC Tracker overlay section registered.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Monitor.Log("ToDew integration failed: " + ex.Message, LogLevel.Warn);
        }
    }

    private void SetupGmcm()
    {
        var api = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null) return;

        api.Register(ModManifest, reset: () => Config = new ModConfig(), save: () => Helper.WriteConfig(Config));

        api.AddSectionTitle(ModManifest, () => "Panel");
        api.AddKeybind(ModManifest, () => Config.TogglePanelKey, v => Config.TogglePanelKey = v, () => "Toggle panel key");
        api.AddKeybind(ModManifest, () => Config.ToggleHudKey, v => Config.ToggleHudKey = v, () => "Toggle HUD key");

        api.AddSectionTitle(ModManifest, () => "HUD");
        api.AddBoolOption(ModManifest, () => Config.HudVisible, v => Config.HudVisible = v, () => "Show HUD");
        api.AddNumberOption(ModManifest, () => Config.HudOffsetX, v => Config.HudOffsetX = v, () => "HUD offset X", min: 0, max: 500);
        api.AddNumberOption(ModManifest, () => Config.HudOffsetY, v => Config.HudOffsetY = v, () => "HUD offset Y", min: 0, max: 500);

        api.AddBoolOption(ModManifest, () => Config.HudShowMissing, v => Config.HudShowMissing = v, () => "HUD: show missing count");
        api.AddBoolOption(ModManifest, () => Config.HudShowPercent, v => Config.HudShowPercent = v, () => "HUD: show completion percent");
        api.AddBoolOption(ModManifest, () => Config.HudShowRain, v => Config.HudShowRain = v, () => "HUD: show rain fish line");
        api.AddBoolOption(ModManifest, () => Config.LastChanceEnabled, v => Config.LastChanceEnabled = v, () => "LAST CHANCE morning toast");
        api.AddBoolOption(ModManifest, () => Config.GoldQualityReminders, v => Config.GoldQualityReminders = v, () => "Gold-quality reminders (panel lines)");
        api.AddSectionTitle(ModManifest, () => "Toast durations (seconds)");
        api.AddNumberOption(ModManifest, () => Config.RainSeconds, v => Config.RainSeconds = v, () => "Rain toast duration", min: 2, max: 30);
        api.AddNumberOption(ModManifest, () => Config.ForecastSeconds, v => Config.ForecastSeconds = v, () => "Forecast toast duration", min: 2, max: 30);
        api.AddNumberOption(ModManifest, () => Config.LastChanceSeconds, v => Config.LastChanceSeconds = v, () => "Last chance toast duration", min: 2, max: 30);
        api.AddNumberOption(ModManifest, () => Config.CartSeconds, v => Config.CartSeconds = v, () => "Cart day toast duration", min: 2, max: 30);
        api.AddNumberOption(ModManifest, () => Config.DeadlineSeconds, v => Config.DeadlineSeconds = v, () => "Deadline toast duration", min: 2, max: 30);
        api.AddSectionTitle(ModManifest, () => "HUD position");
        api.AddNumberOption(ModManifest, () => (int)Config.HudCorner, v => Config.HudCorner = (ScreenCorner)v, () => "HUD corner", () => "0 = TopLeft, 1 = TopRight, 2 = BottomLeft, 3 = BottomRight", min: 0, max: 3, formatValue: v => new string[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }[v]);
        api.AddSectionTitle(ModManifest, () => "Alerts");
        api.AddBoolOption(ModManifest, () => Config.RainToastEnabled, v => Config.RainToastEnabled = v, () => "Rain toast (when rain starts)");
        api.AddBoolOption(ModManifest, () => Config.ForecastToastEnabled, v => Config.ForecastToastEnabled = v, () => "Rain forecast toast (6:30pm)");
        api.AddNumberOption(ModManifest, () => Config.ToastSeconds, v => Config.ToastSeconds = v, () => "Toast duration (seconds)", min: 2, max: 30);

        api.AddSectionTitle(ModManifest, () => "ToDew overlay");
        api.AddBoolOption(ModManifest, () => Config.TodewSectionEnabled, v => Config.TodewSectionEnabled = v, () => "Show CC Tracker section in ToDew overlay");
        api.AddBoolOption(ModManifest, () => Config.TodewShowLastChance, v => Config.TodewShowLastChance = v, () => "ToDew: show last-chance items");
        api.AddBoolOption(ModManifest, () => Config.TodewShowRainFish, v => Config.TodewShowRainFish = v, () => "ToDew: show rain fish");
        api.AddBoolOption(ModManifest, () => Config.TodewShowDonateReady, v => Config.TodewShowDonateReady = v, () => "ToDew: show ready-to-donate items");
        api.AddBoolOption(ModManifest, () => Config.TodewShowPlant, v => Config.TodewShowPlant = v, () => "ToDew: show crops to plant");
    }
}
