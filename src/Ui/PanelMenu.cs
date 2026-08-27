using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using CCSeasonTracker.Core;

namespace CCSeasonTracker.Ui;

/// <summary>Tabbed planner panel, drawn as a walkable HUD overlay (movement stays free).</summary>
public class PanelMenu : IClickableMenu
{
    private const int Pad = 16, TitleH = 50, LineSpacing = 24, DescSpacing = 18;

    private static readonly string[] Tabs = { "Next", "Bundles", "Now", "Fish", "Spring", "Summer", "Fall", "Winter", "Anytime", "Info" };
    private readonly Rectangle[] TabRects = new Rectangle[Tabs.Length];

    // action-priority accent colors (muted, matches the cream/ink theme)
    private static readonly Color AccBlue   = new Color(70, 110, 180);
    private static readonly Color AccGreen  = new Color(60, 120, 60);
    private static readonly Color AccRed    = new Color(178, 48, 38);
    private static readonly Color AccPurple = new Color(150, 80, 160);
    private static readonly Color AccOlive  = new Color(120, 140, 60);
    private static readonly Color AccGold   = new Color(198, 148, 42);

    private readonly ModEntry Mod;
    private int _activeTab;
    private int _hoverTab = -1;
    private int _scroll;
    private int _tabsHeight = 44;
    private int _rowParity;
    private List<object[]> _lines = new();
    private List<int> _heights = new();

    // row hover -> tooltip
    private int _hoverRowIdx = -1;
    private Row _hoverRowRow;


    private static readonly Color Cream = new Color(255, 246, 214);
    private static readonly Color Ink = new Color(90, 45, 15);
    // vanilla scrollbar sprites - the canonical cursor-atlas rects
    // that every stock SDV scrolling menu uses (scale 4x)
    private static readonly Rectangle SrcUpArrow   = new Rectangle(421, 459, 11, 12);
    private static readonly Rectangle SrcDownArrow = new Rectangle(421, 472, 11, 12);
    private static readonly Rectangle SrcTrack     = new Rectangle(435, 463, 6, 10);
    private const float ScrollSpriteScale = 4f;    // vanilla pixel scale
    private const int ThumbW = 24;                 // 6px thumb sprite x 4
    private const int ThumbH = 40;                 // 10px thumb sprite x 4
    private readonly ClickableTextureComponent UpArrowBtn;
    private readonly ClickableTextureComponent DownArrowBtn;
    private readonly ClickableTextureComponent ThumbBtn;
    private int _lastHoverX;
    private static ClickableTextureComponent _thumbProto;

    public bool WantsClose { get; private set; }

    public PanelMenu(ModEntry mod)
    {
        Mod = mod;
        Recenter();
        BuildTabs();
        initializeUpperRightCloseButton();
        UpArrowBtn = new ClickableTextureComponent(new Rectangle(0, 0, 44, 48), Game1.mouseCursors, SrcUpArrow, ScrollSpriteScale);
        DownArrowBtn = new ClickableTextureComponent(new Rectangle(0, 0, 44, 48), Game1.mouseCursors, SrcDownArrow, ScrollSpriteScale);
        ThumbBtn = MakeThumbComponent();
        RebuildLines();
    }

    public bool Contains(int x, int y) =>
        new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height).Contains(x, y);

    // The thumb and runner sprites are read from the game itself:
    // the 1.6 atlas moved them, so hardcoded rects showed wrong art.
    private static ClickableTextureComponent MakeThumbComponent()
    {
        if (_thumbProto != null) return _thumbProto;
        Rectangle src = SrcTrack;
        Texture2D tex = Game1.mouseCursors;
        float scale = ScrollSpriteScale;
        try
        {
            var page = new OptionsPage(-9999, -9999, 400, 400);
            ClickableTextureComponent found = null;
            var flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            var fld = typeof(OptionsPage).GetField("scrollBar", flags);
            if (fld != null)
            {
                found = fld.GetValue(page) as ClickableTextureComponent;
            }
            if (found == null)
            {
                foreach (var f2 in typeof(OptionsPage).GetFields(flags))
                {
                    var c2 = f2.GetValue(page) as ClickableTextureComponent;
                    if (c2 != null && c2.texture != null
                        && c2.sourceRect.Width <= 8
                        && c2.sourceRect.Height >= 6
                        && c2.sourceRect.Height <= 12)
                    {
                        found = c2;
                        break;
                    }
                }
            }
            if (found != null && found.texture != null)
            {
                src = found.sourceRect;
                tex = found.texture;
                scale = found.baseScale;
            }
        }
        catch
        {
        }
        _thumbProto = new ClickableTextureComponent(new Rectangle(0, 0, ThumbW, ThumbH), tex, src, scale);
        return _thumbProto;
    }

    private void Recenter()
    {
        var vp = Game1.uiViewport;
        width = MathHelper.Clamp((int)(vp.Width * 0.62f), 640, 980);
        height = MathHelper.Clamp((int)(vp.Height * 0.76f), 480, 1000);
        xPositionOnScreen = vp.Width / 2 - width / 2;
        yPositionOnScreen = vp.Height / 2 - height / 2;
    }

    private int ContentTop => yPositionOnScreen + Pad + TitleH + _tabsHeight + 6;
    private int ContentBottom => yPositionOnScreen + height - 62;
    private int TotalHeight => _heights.Sum();
    private int MaxScroll => Math.Max(0, TotalHeight - (ContentBottom - ContentTop));

    private int ScrollCenter => xPositionOnScreen + width - 30;
    private Rectangle ScrollTrack => new Rectangle(ScrollCenter - 12, UpArrow.Bottom + 2, 24, DownArrow.Y - UpArrow.Bottom - 6);
    private Rectangle UpArrow => new Rectangle(ScrollCenter - 22, ContentTop + 4, 44, 48);
    private Rectangle DownArrow => new Rectangle(ScrollCenter - 22, ContentBottom - 52, 44, 48);
    private Rectangle ScrollColumn => new Rectangle(ScrollCenter - 22, UpArrow.Bottom + 2, 44, DownArrow.Y - UpArrow.Bottom - 4);

    private void BuildTabs()
    {
        if (!CcWizardDone())
        {
            _tabsHeight = 0;
            for (int i = 0; i < TabRects.Length; i++) TabRects[i] = Rectangle.Empty;
            return;
        }
        int tx = xPositionOnScreen + Pad;
        int ty = yPositionOnScreen + Pad + TitleH;
        const int tabH = 32, gap = 2;
        for (int i = 0; i < Tabs.Length; i++)
        {
            int w = (int)Game1.smallFont.MeasureString(Tabs[i]).X + 12;
            TabRects[i] = new Rectangle(tx, ty, w, tabH);
            tx += w + gap;
        }
        _tabsHeight = tabH + 12;
    }

    // ---------- bundles hub tab ----------

    private Texture2D _mapTex;
    private bool _mapTried;
    private int _mapW, _mapH;
    private Rectangle _mapDest = Rectangle.Empty;
    private int _hoverRoom = -1;

    private static readonly string[] RoomAreas =
        { "Crafts Room", "Pantry", "Fish Tank", "Bulletin Board", "Vault", "Boiler Room" };

    private static float[][] _roomPolys;   // room hotspots, from rooms.json or defaults

    // hotspot polygons tracing each room's walls (fractions of the image)
    // defaults = the user-calibrated values (rooms.json overrides at runtime)
    private static float[][] DefaultPolys() =>
        new float[][]
        {
            new float[] { 0.083f, 0.006f, 0.250f, 0.006f, 0.252f, 0.261f, 0.083f, 0.261f },
            new float[] { 0.250f, 0.994f, 0.252f, 0.496f, 0.005f, 0.489f, 0.007f, 0.992f },
            new float[] { 0.526f, 0.405f, 0.524f, 0.222f, 0.414f, 0.222f, 0.413f, 0.403f },
            new float[] { 0.659f, 0.470f, 0.662f, 0.350f, 0.547f, 0.347f, 0.544f, 0.477f },
            new float[] { 0.863f, 0.324f, 0.861f, 0.036f, 0.575f, 0.034f, 0.575f, 0.322f },
            new float[] { 0.823f, 0.339f, 0.992f, 0.339f, 0.992f, 0.742f, 0.823f, 0.742f }
        };

    private sealed class MapLine : Line { }

    private Texture2D LoadMapTex()
    {
        if (_mapTried) return _mapTex;
        _mapTried = true;
        Mod.Monitor.Log("[CCST] rooms.png probe: " + Mod.RoomsPngPath, StardewModdingAPI.LogLevel.Info);
        try
        {
            if (!System.IO.File.Exists(Mod.RoomsPngPath))
            {
                Mod.Monitor.Log("[CCST] rooms.png NOT FOUND at that path", StardewModdingAPI.LogLevel.Warn);
                return null;
            }
            _mapTex = Texture2D.FromFile(Game1.graphics.GraphicsDevice, Mod.RoomsPngPath);
            Mod.Monitor.Log("[CCST] rooms.png loaded OK", StardewModdingAPI.LogLevel.Info);
        }
        catch (Exception ex)
        {
            Mod.Monitor.Log("[CCST] rooms.png FOUND but load FAILED: " + ex.Message, StardewModdingAPI.LogLevel.Warn);
            _mapTex = null;
        }
        return _mapTex;
    }

    private static int AreaIndexOf(string area) => area switch
    {
        "Crafts Room" => StardewValley.Locations.CommunityCenter.AREA_CraftsRoom,
        "Pantry" => StardewValley.Locations.CommunityCenter.AREA_Pantry,
        "Fish Tank" => StardewValley.Locations.CommunityCenter.AREA_FishTank,
        "Bulletin Board" => StardewValley.Locations.CommunityCenter.AREA_Bulletin,
        "Vault" => StardewValley.Locations.CommunityCenter.AREA_Vault,
        "Boiler Room" => StardewValley.Locations.CommunityCenter.AREA_BoilerRoom,
        _ => StardewValley.Locations.CommunityCenter.AREA_CraftsRoom
    };

    private (int done, int total, bool complete) RoomProgress(string area)
    {
        int done = 0, total = 0;
        foreach (var g in Mod.Tracker.Groups)
        {
            if (g.IsMissingBundle || g.Area != area) continue;
            total += g.RequiredCount;
            done += Math.Min(g.DonatedSlots, g.RequiredCount);
        }
        return (done, total, total > 0 && done >= total);
    }

    private void ReloadPolys()
    {
        _roomPolys = DefaultPolys();
        try
        {
            var loaded = Mod.Helper.Data.ReadJsonFile<float[][]>("assets/rooms.json");
            if (loaded != null && loaded.Length == RoomAreas.Length)
            {
                _roomPolys = loaded;
            }
        }
        catch
        {
        }
    }

    private List<Vector2> RoomScreenPoly(int i)
    {
        var f = _roomPolys[i];
        var pts = new List<Vector2>();
        for (int k = 0; k < f.Length; k += 2)
        {
            pts.Add(new Vector2(
                _mapDest.X + f[k] * _mapDest.Width,
                _mapDest.Y + f[k + 1] * _mapDest.Height));
        }
        return pts;
    }

    private int RoomAt(int x, int y)
    {
        if (_roomPolys == null) return -1;
        for (int i = RoomAreas.Length - 1; i >= 0; i--)
        {
            if (PolyContains(RoomScreenPoly(i), x, y)) return i;
        }
        return -1;
    }

    private static bool PolyContains(List<Vector2> pts, int px, int py)
    {
        bool inside = false;
        int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            bool cross = (pts[i].Y > py) != (pts[j].Y > py);
            if (cross)
            {
                float xInt = (pts[j].X - pts[i].X)
                    * (py - pts[i].Y) / (pts[j].Y - pts[i].Y)
                    + pts[i].X;
                if (px < xInt) inside = !inside;
            }
        }
        return inside;
    }

    private static void DrawPolyLine(SpriteBatch b, List<Vector2> pts, Color c)
    {
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            Vector2 p1 = pts[j];
            Vector2 p2 = pts[i];
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            int steps = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps < 1) steps = 1;
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps;
                b.Draw(Game1.staminaRect,
                    new Rectangle((int)Math.Round(p1.X + dx * t) - 1,
                        (int)Math.Round(p1.Y + dy * t) - 1, 2, 2), c);
            }
        }
    }

    private static void FillPolyFaint(SpriteBatch b, List<Vector2> pts, Color c)
    {
        float ymin = float.MaxValue, ymax = float.MinValue;
        foreach (var p in pts)
        {
            if (p.Y < ymin) ymin = p.Y;
            if (p.Y > ymax) ymax = p.Y;
        }
        for (float yy = ymin + 2f; yy < ymax - 2f; yy += 4f)
        {
            var xs = new List<float>();
            int n = pts.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((pts[i].Y > yy) != (pts[j].Y > yy))
                {
                    xs.Add((pts[j].X - pts[i].X)
                        * (yy - pts[i].Y) / (pts[j].Y - pts[i].Y)
                        + pts[i].X);
                }
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int x0 = (int)xs[k];
                int x1 = (int)xs[k + 1];
                if (x1 > x0)
                {
                    b.Draw(Game1.staminaRect,
                        new Rectangle(x0, (int)yy - 2, x1 - x0, 4), c);
                }
            }
        }
    }

    private void BuildBundlesTab()
    {
        ReloadPolys();

        _mapTex = LoadMapTex();
        if (_mapTex == null)
        {
            PushText("Map image missing - check the SMAPI console for the exact path the mod checked.");
            PushText("Put rooms.png at that path (and in the project assets folder for builds), then reopen with F6.");
            foreach (var area in RoomAreas)
            {
                var (done, total, complete) = RoomProgress(area);
                PushHeader(area + (complete ? "  COMPLETE" : ""));
                PushText(done + " of " + total + " slots donated");
            }
            return;
        }
        _mapW = Math.Min(TextMaxW, 1000);
        _mapH = (int)(_mapW * (float)_mapTex.Height / _mapTex.Width);
        int avail = ContentBottom - ContentTop - 34;
        if (_mapH > avail)
        {
            _mapH = avail;
            _mapW = (int)(_mapH * (float)_mapTex.Width / _mapTex.Height);
        }
        _lines.Add(new object[] { "m", new MapLine() });
        _heights.Add(_mapH + 30);
    }

    private void DrawMap(SpriteBatch b, int yTop)
    {
        int destX = xPositionOnScreen + Pad + (TextMaxW - _mapW) / 2;
        _mapDest = new Rectangle(destX, yTop, _mapW, _mapH);
        b.Draw(Game1.staminaRect,
            new Rectangle(_mapDest.X - 2, _mapDest.Y - 2, _mapDest.Width + 4, _mapDest.Height + 4),
            Ink * 0.6f);
        b.Draw(_mapTex, _mapDest, Color.White);

        var (mx, my) = Mod.UiMouse();
        _hoverRoom = _mapDest.Contains(mx, my) ? RoomAt(mx, my) : -1;

        for (int i = 0; i < RoomAreas.Length; i++)
        {
            var poly = RoomScreenPoly(i);
            var prog = RoomProgress(RoomAreas[i]);
            if (prog.complete)
            {
                FillPolyFaint(b, poly, new Color(80, 200, 90) * 0.30f);
                DrawPolyLine(b, poly, new Color(60, 160, 70) * 0.95f);
            }
            else if (i == _hoverRoom)
            {
                FillPolyFaint(b, poly, Color.White * 0.25f);
                DrawPolyLine(b, poly, Color.White * 0.9f);
            }
        }

        string info = "Click a room for the vanilla bundle view - hold Shift to calibrate";
        if (_hoverRoom >= 0)
        {
            var p = RoomProgress(RoomAreas[_hoverRoom]);
            info = RoomAreas[_hoverRoom] + " - " + p.done + " of " + p.total
                + " slots - click for the vanilla bundle view";
        }
        var kb = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        if (kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)
            || kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift))
        {
            info = "calibrate: px " + (mx - _mapDest.X) + " , " + (my - _mapDest.Y)
                + "   fr " + ((mx - _mapDest.X) / (float)_mapDest.Width).ToString("0.000")
                + " , " + ((my - _mapDest.Y) / (float)_mapDest.Height).ToString("0.000")
                + "   (room " + (_hoverRoom + 1) + ")";
        }
        b.DrawString(Game1.smallFont, info, new Vector2(_mapDest.X, _mapDest.Bottom + 8), Ink);
    }

    private static bool MailHas(string flag)
    {
        try
        {
            var p = Game1.MasterPlayer ?? Game1.player;
            return p.mailReceived.Contains(flag);
        }
        catch
        {
            return false;
        }
    }

    private static bool CcIntroSeen()
    {
        try
        {
            return (Game1.MasterPlayer ?? Game1.player).eventsSeen.Contains("60867");
        }
        catch
        {
            return false;
        }
    }

    internal static bool CcWizardDone()
    {
        if (MailHas("canReadJunimoText"))
        {
            return true;
        }
        if (MailHas("ccIsFinished"))
        {
            return true;
        }
        try
        {
            var bundles = Game1.netWorldState.Value.Bundles;
            for (int id = 0; id <= 36; id++)
            {
                if (!bundles.ContainsKey(id)) continue;
                bool[] flags = bundles[id];
                if (flags == null) continue;
                foreach (bool b in flags)
                {
                    if (b) return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private void BuildUnlockTab()
    {
        PushHeader("COMMUNITY CENTER - HOW TO UNLOCK");
        PushText("The tracker activates once the Community Center is open. Here is the path:");
        PushText("1. From Spring 5 onward: enter Pelican Town FROM THE BUS STOP, 8:00am to 1:00pm, on a sunny (not rainy) day.");
        PushText("Mayor Lewis unlocks the Center and the Rat Problem quest starts.");
        PushText("2. Enter the CC and interact with the Golden Scroll in the lower-left room (Crafts Room).");
        PushText("3. The NEXT morning: check your mailbox for the Wizard's letter.");
        PushText("4. Visit the Wizard's Tower in Cindersap Forest - his potion lets you read Junimo writing.");
        PushText("5. Return to the CC - all bundles readable. Press F6: the tracker is live!");
        PushText("Rainy days do not count, and entering town from any other side will not trigger it.");
        PushText("This page hides itself once the CC is unlocked.");
    }

    // ---------- line model ----------

    private abstract class Line { }
    private sealed class HeaderLine : Line { public string Text; }
    private sealed class RowLine : Line { public Row Row; public List<string> DescLines; }
    private sealed class TextLine : Line { public List<string> Lines; }
    private sealed class ActionLine : Line { public List<string> Lines; public Color Accent; }

    private int TextMaxW => width - Pad * 2 - 40;

    private (List<string>, bool) Wrap(string s, float maxW, int maxLines)
    {
        var lines = new List<string>();
        bool truncated = false;
        if (string.IsNullOrEmpty(s)) { lines.Add(""); return (lines, false); }
        var words = s.Split(' ');
        string cur = "";
        foreach (var w in words)
        {
            string test = cur.Length == 0 ? w : cur + " " + w;
            if (Game1.smallFont.MeasureString(test).X <= maxW || cur.Length == 0)
            {
                cur = test;
                continue;
            }
            if (lines.Count == maxLines - 1) { truncated = true; break; }
            lines.Add(cur);
            cur = w;
        }
        if (cur.Length > 0 && lines.Count < maxLines) lines.Add(cur);
        else if (cur.Length > 0) truncated = true;
        if (truncated && lines.Count > 0)
        {
            string last = lines[lines.Count - 1];
            while (last.Length > 1 && Game1.smallFont.MeasureString(last + "...").X > maxW)
                last = last.Substring(0, last.Length - 1);
            lines[lines.Count - 1] = last + "...";
        }
        return (lines, truncated);
    }

    private void PushHeader(string text)
    {
        int maxW = TextMaxW - 20;
        while (text.Length > 1 && SpriteText.getWidthOfString(text) > maxW)
            text = text.Substring(0, text.Length - 1);
        _lines.Add(new object[] { "h", new HeaderLine { Text = text } });
        _heights.Add(66);
        _rowParity = 0;
    }

    // FIX r21: rows use TWO stacked zones (name line + wrapped description line),
    // so long names like "Parsnip x5 (gold)" never fight a middle column again.
    private void PushRow(Row row)
    {
        bool ready = TrackerService.IsReady(row);
        string desc = ready ? "have it - donate at the CC" : Describe(row);
        float descAvail = TextMaxW - 64;   // icon column width
        var (lines, _) = Wrap(desc, descAvail, 2);

        int nameH = SpriteText.getHeightOfString(RowName(row));

        int descTop = 8 + nameH + 8;
        _lines.Add(new object[] { "r", new RowLine { Row = row, DescLines = lines } });
        _heights.Add(descTop + (lines.Count - 1) * DescSpacing + 24);
        _rowParity++;
    }

    private void PushText(string text)
    {
        var (lines, _) = Wrap(text, TextMaxW, 2);
        _lines.Add(new object[] { "t", new TextLine { Lines = lines } });
        _heights.Add(lines.Count > 1 ? 32 + LineSpacing : 32);
        _rowParity = 0;
    }

    private void PushAction(string text, Color accent)
    {
        var (lines, _) = Wrap(text, TextMaxW - 14, 2);
        _lines.Add(new object[] { "a", new ActionLine { Lines = lines, Accent = accent } });
        _heights.Add(lines.Count > 1 ? 36 + LineSpacing : 36);
        _rowParity = 0;
    }

    // ---------- helpers ----------

    /// <summary>" (location)" or "" - never an empty/broken parenthesis.</summary>
    private static string Loc(Row r)
    {
        var l = r.Meta?.location;
        if (string.IsNullOrWhiteSpace(l) || l == "-") return "";
        return " (" + l + ")";
    }

    private static int TokenItemId(string t) =>
        t != null && t.StartsWith("(O)") && int.TryParse(t.Substring(3), out var v) ? v : -1;

    private bool CatchableNow(Row r)
    {
        var w = r.Meta?.weather;
        if (w == "rain-only") return Mod.Tracker.IsRaining;
        if (w == "sunny-only" || w == "sunwind") return !Mod.Tracker.IsRaining;
        return true;
    }

    private static string GroupOfType(string t) => t switch
    {
        "fish" or "shellfish" => "FISH",
        "crop" => "CROPS",
        "tree" or "forage" => "FORAGE & TREES",
        "animal" or "artisan" => "ANIMALS & ARTISAN",
        _ => "MINES & OTHER"
    };

    // fish LAST - crops/forage are more accessible (user preference)
    private static readonly string[] GroupOrder =
        { "CROPS", "FORAGE & TREES", "ANIMALS & ARTISAN", "MINES & OTHER", "FISH" };

    private void AddTypeGroups(Func<Row, bool> filter)
    {
        var tr = Mod.Tracker;
        var map = new Dictionary<string, List<Row>>();
        foreach (var r in tr.AllMissing.Where(r => r.Meta != null && !TrackerService.IsReady(r) && filter(r)))
        {
            string g = GroupOfType(r.Meta.type);
            if (!map.TryGetValue(g, out var list)) map[g] = list = new List<Row>();
            list.Add(r);
        }
        foreach (var g in GroupOrder)
            if (map.TryGetValue(g, out var rows) && rows.Count > 0)
            {
                PushHeader($"{g}  ({rows.Count})");
                rows.ForEach(PushRow);
            }
    }

    /// <summary>The prioritized action list (own tab).</summary>
    private void BuildNextTab()
    {
        var tr = Mod.Tracker;
        string season = tr.CurrentSeason();
        bool NotReady(Row r) => !TrackerService.IsReady(r);

        PushText($"It is {season}, day {Game1.dayOfMonth} - {tr.DaysLeftInSeason()} days left.");

        var actions = new List<(string Text, Color Accent)>();
        void Add(string s, Color c) => actions.Add((s, c));

        // live scan of what is already planted on the farm
        var planted = TrackerService.PlantedCropsByItemId();

        // Red Cabbage: own seeds? remind to plant them while summer allows (9-day crop)
        var cabRows = tr.AllMissing.Where(x => string.Equals(x.DisplayName, "Red Cabbage", StringComparison.OrdinalIgnoreCase)).ToList();
        int cabSeeds = TrackerService.CountAnywhere("(O)485", 0);
        string packs = cabSeeds == 1 ? "pack" : "packs";
        if (cabRows.Count > 0 && cabSeeds > 0 && !cabRows.Any(x => TrackerService.IsReady(x))
            && season.Equals("summer", StringComparison.OrdinalIgnoreCase)
            && tr.DaysLeftInSeason() >= 9)
            Add($"Plant your Red Cabbage seeds ({cabSeeds} {packs} stored) - {tr.DaysLeftInSeason()}d left, crop takes 9d", AccGreen);

        foreach (var r in tr.RainFishAvailableNow().Where(NotReady))
        {
            string loc = Loc(r);
            if (tr.IsRaining)
                Add($"Fish NOW: {r.DisplayName}{loc} - rain only", AccBlue);
            else if (Core.AlertEngine.IsRainExpectedTomorrow())
                Add($"Rain tomorrow: fish {r.DisplayName}{loc}", AccBlue);
            else
                Add($"Next rain: {r.DisplayName}{loc} - rain-only fish", AccBlue);
        }

        // FIX r21: planting actions are aware of crops ALREADY in the ground
        foreach (var r in tr.ForSeason(season).Where(r =>
                     r.Meta?.type == "crop" && r.Meta.cropDays > 0 && NotReady(r)))
        {
            int id = TokenItemId(r.Src.Token);
            int growing = 0, soon = -1;
            if (id > 0 && planted.TryGetValue(id, out var lst) && lst.Count > 0)
            {
                growing = lst.Count;
                soon = lst.Min();
            }
            int need = r.Src.Stack;
            if (growing >= need && need == 1)
                Add($"{r.DisplayName} planted - about {soon}d to harvest", AccGreen);
            else if (growing >= need)
                Add($"All {growing} {r.DisplayName} planted - about {soon}d to harvest", AccGreen);
            else if (growing > 0)
                Add($"Plant {need - growing} more {r.DisplayName} ({growing} growing, ~{soon}d to harvest)", AccGreen);
            else
                Add($"Plant today: {r.DisplayName} (ready in {r.Meta.cropDays}d)", AccGreen);
        }

        if (tr.DaysLeftInSeason() <= 5)
        {
            var excl = tr.ForSeason(season).Where(r => r.Meta?.seasons.Length == 1 && NotReady(r)).ToList();
            if (excl.Count > 0)
                Add($"Season ends in {tr.DaysLeftInSeason()}d - still need: {string.Join(", ", excl.Take(2).Select(x => x.DisplayName))}", AccRed);
        }

        if (tr.IsCartDayToday())
        {
            var cart = tr.CartCandidates().Where(NotReady).ToList();
            if (cart.Count > 0) Add($"Traveling Cart today: look for {cart[0].DisplayName}", AccPurple);
        }

        var forage = tr.ForSeason(season).Where(r => r.Meta?.type is "forage" && NotReady(r)).ToList();
        foreach (var r in forage) Add($"Forage: {r.DisplayName}{Loc(r)}", AccOlive);

        if (Mod.Config.GoldQualityReminders)
        {
            foreach (var r in tr.ForSeason(season).Where(r => r.NeedsQuality && NotReady(r)))
                Add($"{RowName(r)}: {Math.Max(0, r.HaveCount)}/{r.Src.Stack} collected (" + QualityWord(r) + ")", AccGold);
        }

        if (tr.IsJojaMember())
        {
            Add("Joja member: buying the Joja Membership converts the CC on your next visit - bundles close for good. Donate what you can first!", AccOlive);
        }

        if (tr.AllMissing.Any(r => string.Equals(r.DisplayName, "Truffle", StringComparison.OrdinalIgnoreCase)) &&
            !Game1.getFarm().getAllFarmAnimals().Any(a => a.type.Value == "Pig"))
            Add("Buy a Deluxe Barn + Pig (you still need a Truffle)", Ink);

        var (have, goal) = tr.VaultProgress();
        if (goal > 0 && have < goal) Add($"Vault: pay {goal - have:N0}g more ({have:N0}/{goal:N0}g)", Ink);

        try { if (Core.AlertEngine.IsRainExpectedTomorrow()) Add("Rain forecast tomorrow - prep your fishing rod!", AccBlue); } catch { }

        if (actions.Count > 0)
        {
            PushHeader("DO NEXT");
            foreach (var a in actions) PushAction(a.Text, a.Accent);
        }
        else
        {
            PushText("Nothing urgent right now. Check the season tabs for the full list.");
        }
    }

    private void RebuildLines()
    {
        _lines = new List<object[]>();
        _heights = new List<int>();
        _rowParity = 0;
        if (!CcWizardDone())
        {
            BuildUnlockTab();
            _scroll = Math.Max(0, Math.Min(_scroll, MaxScroll));
            return;
        }
        var tr = Mod.Tracker;
        if (tr.IsJojaTakeover())
        {
            PushHeader("COMMUNITY CENTER - JOJA TAKEOVER");
            PushText("The Community Center is now a Joja Movie Theater. The bundles are gone for good.");
            PushText("There is nothing left to track. Thanks for using CC Season Tracker!");
            _scroll = Math.Max(0, Math.Min(_scroll, MaxScroll));
            return;
        }

        string season = tr.CurrentSeason();
        var cmp = StringComparer.OrdinalIgnoreCase;
        bool NotReady(Row r) => !TrackerService.IsReady(r);

        switch (_activeTab)
        {
            case 0: BuildNextTab(); break;
            case 1: BuildBundlesTab(); break;

            case 2: // NOW
            {
                PushText($"Season: {season} - {tr.DaysLeftInSeason()} days left");
                PushText($"Community Center: {tr.BundlesDone}/{tr.BundlesTotal} bundles complete");

                var ready = tr.ReadyToDonate();
                if (ready.Count > 0)
                {
                    PushHeader($"READY TO DONATE  ({ready.Count})");
                    PushText("You already have these - take them to the CC:");
                    ready.ForEach(PushRow);
                }

                var crops = tr.ForSeason(season).Where(r => r.Meta?.type == "crop" && !r.NeedsQuality && NotReady(r)).ToList();
                if (crops.Count > 0) { PushHeader($"CROPS TO PLANT  ({crops.Count})"); crops.ForEach(PushRow); }

                var forage = tr.ForSeason(season).Where(r => r.Meta?.type is "forage" or "tree" && NotReady(r)).ToList();
                if (forage.Count > 0) { PushHeader($"FORAGE & TREES  ({forage.Count})"); forage.ForEach(PushRow); }

                var gold = tr.ForSeason(season).Where(r => r.NeedsQuality && NotReady(r)).ToList();
                if (Mod.Config.GoldQualityReminders && gold.Count > 0) { PushHeader($"QUALITY CROPS  ({gold.Count})"); gold.ForEach(PushRow); }

                string nxt = season.ToLower() switch
                {
                    "spring" => "summer", "summer" => "fall", "fall" => "winter", _ => "spring"
                };
                var nextCrops = tr.AllMissing
                    .Where(r => r.Meta?.type == "crop" && r.Meta.seasons.Contains(nxt, cmp) && NotReady(r)).Take(4).ToList();
                if (nextCrops.Count > 0)
                {
                    PushHeader($"PLAN AHEAD - plant day 1 of {char.ToUpper(nxt[0]) + nxt.Substring(1)}");
                    nextCrops.ForEach(PushRow);
                }

                // only fish actually catchable in CURRENT weather
                if (tr.IsRaining)
                {
                    var rain = tr.RainFishAvailableNow().Where(NotReady).ToList();
                    if (rain.Count > 0)
                    {
                        PushHeader($"RAIN RIGHT NOW - FISH TODAY  ({rain.Count})");
                        rain.ForEach(PushRow);
                    }
                }
                var fish = tr.ForSeason(season)
                             .Where(r => r.Meta?.type is "fish" or "shellfish" && NotReady(r) && CatchableNow(r))
                             .ToList();
                if (fish.Count > 0) { PushHeader($"FISH CATCHABLE NOW  ({fish.Count})"); fish.ForEach(PushRow); }
                if (!tr.IsRaining && tr.RainFishAvailableNow().Any(NotReady))
                    PushText("Rain-only fish (Catfish, Eel...) are hidden until it rains. See Fish tab.");

                var (have, goal) = tr.VaultProgress();
                if (goal > 0) PushText($"Vault: {have:N0}g / {goal:N0}g ({(int)(100.0 * have / goal)}%)");
                if (tr.Unknown().Count > 0) PushText(tr.Unknown().Count + " items lack data - see Info tab.");
                break;
            }
            case 3: // FISH planner
            {
                PushText("Every fish you still need, by season. RAIN = only catchable in rain.");
                foreach (var s in new[] { "spring", "summer", "fall", "winter" })
                {
                    var rows = tr.AllMissing.Where(r => r.Meta?.type is "fish" or "shellfish"
                                && r.Meta.seasons.Contains(s, cmp) && NotReady(r)).ToList();
                    if (rows.Count > 0)
                    {
                        PushHeader(char.ToUpper(s[0]) + s.Substring(1).ToUpper());
                        rows.ForEach(PushRow);
                    }
                }
                var any = tr.AllMissing.Where(r => r.Meta?.type is "fish" or "shellfish"
                          && (r.Meta.seasons.Length == 0 || r.Meta.seasons.Length >= 4) && NotReady(r)).ToList();
                if (any.Count > 0) { PushHeader("ANY SEASON"); any.ForEach(PushRow); }
                break;
            }
            case 4: AddTypeGroups(r => r.Meta.seasons.Contains("spring", cmp)); break;
            case 5: AddTypeGroups(r => r.Meta.seasons.Contains("summer", cmp)); break;
            case 6: AddTypeGroups(r => r.Meta.seasons.Contains("fall", cmp)); break;
            case 7: AddTypeGroups(r => r.Meta.seasons.Contains("winter", cmp)); break;
            case 8: // ANYTIME
            {
                PushText("Obtainable in ANY season: mines, artisan goods, animals, all-season fish.");
                AddTypeGroups(r => r.Meta.seasons.Length == 0 || r.Meta.seasons.Length >= 4);
                break;
            }
            case 9: // INFO
            {
                var missingBundle = tr.Groups.Where(g => g.IsMissingBundle).ToList();
                if (missingBundle.Count > 0)
                {
                    PushHeader("MISSING BUNDLE (pick any 5 of 6)");
                    PushText("Abandoned JojaMart - unlocks the Movie Theater");
                    foreach (var grp in missingBundle) grp.Rows.ForEach(PushRow);
                }
                var unknown = tr.Unknown();
                if (unknown.Count > 0)
                {
                    PushHeader($"NO STRATEGY DATA  ({unknown.Count})");
                    unknown.ForEach(PushRow);
                }
                if (missingBundle.Count == 0 && unknown.Count == 0)
                    PushText("Nothing extra. All items have strategy data.");
                break;
            }
        }
        if (_lines.Count == 0) PushText("Nothing to show.");

        _scroll = Math.Max(0, Math.Min(_scroll, MaxScroll));
    }

    // ---------- input ----------

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton != null && upperRightCloseButton.bounds.Contains(x, y))
        {
            WantsClose = true;
            return;
        }
        if (MaxScroll > 0)
        {
            if (UpArrow.Contains(x, y))
            {
                _scroll = Math.Max(0, _scroll - 64);
                Game1.playSound("smallSelect");
                return;
            }
            if (DownArrow.Contains(x, y))
            {
                _scroll = Math.Min(MaxScroll, _scroll + 64);
                Game1.playSound("smallSelect");
                return;
            }
            if (ScrollColumn.Contains(x, y))
            {
                SetScrollFromY(y);
                Game1.playSound("smallSelect");
                return;
            }
        }
        if (_activeTab == 1 && _mapTex != null && _mapDest.Contains(x, y))
        {
            if (RoomAt(x, y) >= 0)
            {
                Game1.playSound("bigSelect");
                try
                {
                    Game1.activeClickableMenu = new JunimoNoteMenu(false, AreaIndexOf(RoomAreas[RoomAt(x, y)]));
                }
                catch
                {
                    Mod.Monitor.Log("[CCST] bundle menu open failed, falling back to overview mode", StardewModdingAPI.LogLevel.Warn);
                    Game1.activeClickableMenu = new JunimoNoteMenu(true, AreaIndexOf(RoomAreas[RoomAt(x, y)]));
                }
                return;
            }
        }

        for (int i = 0; i < TabRects.Length; i++)
            if (TabRects[i].Contains(x, y) && _activeTab != i)
            {
                _activeTab = i; _scroll = 0; RebuildLines();
                Game1.playSound("smallSelect");
                return;
            }
    }

    public void UpdateDrag(bool mouseDown, int uiMouseY)
    {
        bool held = mouseDown
            || Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        if (held
            && MaxScroll > 0
            && ScrollColumn.Contains(_lastHoverX, uiMouseY))
        {
            SetScrollFromY(uiMouseY);
        }
    }
    public void EndDrag()
    {
    }

    private void SetScrollFromY(int uiMouseY)
    {
        int visH = ContentBottom - ContentTop;
        int totalH = TotalHeight;
        if (totalH <= visH) { _scroll = 0; return; }
        var track = ScrollTrack;
        int knobH = ThumbH;
        int rel = uiMouseY - track.Y - knobH / 2;
        _scroll = Math.Max(0, Math.Min(MaxScroll, rel * MaxScroll / Math.Max(1, track.Height - knobH)));
    }

    public override void performHoverAction(int x, int y)
    {
        _hoverTab = -1;
        for (int i = 0; i < TabRects.Length; i++) if (TabRects[i].Contains(x, y)) _hoverTab = i;
        upperRightCloseButton?.tryHover(x, y, 0.5f);   // vanilla hover grow animation
        if (MaxScroll > 0)
        {
            UpArrowBtn.tryHover(x, y, 0.4f);
            DownArrowBtn.tryHover(x, y, 0.4f);
            ThumbBtn.tryHover(x, y, 0.4f);
        }
        _lastHoverX = x;
    }

    public override void receiveScrollWheelAction(int direction)
    {
        _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(direction) * 64));
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        Recenter();
        BuildTabs();
        initializeUpperRightCloseButton();
    }

    // ---------- drawing ----------

    public override void draw(SpriteBatch b)
    {
        IClickableMenu.drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        int ccPct = 0;
        if (Mod.Tracker.SlotsRequired > 0)
        {
            ccPct = Math.Min(100, Mod.Tracker.SlotsDonated * 100 / Mod.Tracker.SlotsRequired);
        }
        SpriteText.drawString(b, "Community Center Tracker - " + ccPct + "%", xPositionOnScreen + Pad, yPositionOnScreen + Pad + 6);
        upperRightCloseButton?.draw(b);

        b.Draw(Game1.staminaRect,
            new Rectangle(xPositionOnScreen + Pad, yPositionOnScreen + Pad + TitleH - 4, width - Pad * 2, 2),
            new Color(60, 30, 10) * 0.35f);

        bool tabsVisible = CcWizardDone();
        for (int i = 0; i < TabRects.Length && tabsVisible; i++)
        {
            var r = TabRects[i];
            if (i == _activeTab)
            {
                int th = (int)Game1.smallFont.MeasureString(Tabs[i]).Y;

                var hl = new Rectangle(

                    r.X, r.Y + 2,

                    r.Width, th + 8);

                b.Draw(Game1.staminaRect, hl, new Color(166, 100, 40) * 0.45f);
                b.Draw(Game1.staminaRect, new Rectangle(hl.X, hl.Bottom - 2, hl.Width, 2), Color.Gold * 0.95f);
            }
            else if (i == _hoverTab)
            {
                int thh = (int)Game1.smallFont.MeasureString(Tabs[i]).Y;
                var hb = new Rectangle(
                    r.X, r.Y + 2, r.Width, thh + 8);
                b.Draw(Game1.staminaRect, hb, Color.Black * 0.08f);
            }
            b.DrawString(Game1.smallFont, Tabs[i], new Vector2(r.X + 9, r.Y + 6),
                i == _activeTab ? new Color(40, 20, 5) : Ink);
        }

        b.Draw(Game1.staminaRect,
            new Rectangle(xPositionOnScreen + Pad, ContentTop - 8, width - Pad * 2, 2),
            new Color(60, 30, 10) * 0.35f);

        // uniform content well: covers the box texture tiled shading so the list area is clean
        b.Draw(Game1.staminaRect,
            new Rectangle(xPositionOnScreen + 10, ContentTop - 2, width - 20, ContentBottom - ContentTop + 6),
            new Color(255, 226, 163));

        int top = ContentTop, bottom = ContentBottom;
        int cx = xPositionOnScreen + Pad;

        var (mx, my) = Mod.UiMouse();     // UI-space cursor (same math as click handling)
        _hoverRowIdx = -1;

        int y = top - _scroll;
        for (int i = 0; i < _lines.Count; i++)
        {
            int h = _heights[i];
            if (y >= top && y + h <= bottom)
            {
                DrawLine(b, _lines[i], y, cx, h);
                if (_lines[i][0] is "r" && Contains(mx, my) && my >= y && my < y + h)
                {
                    _hoverRowIdx = i;
                    _hoverRowRow = ((RowLine)_lines[i][1]).Row;
                }
            }
            y += h;
        }

        if (TotalHeight > bottom - top)
        {
            // vanilla scrollbar: real game sprites via stock components
            b.Draw(Game1.staminaRect, new Rectangle(ScrollCenter - 6, ScrollTrack.Y, 12, ScrollTrack.Height), new Color(233, 203, 152));
            int knobY = ScrollTrack.Y
                + (ScrollTrack.Height - ThumbH) * _scroll
                / Math.Max(1, MaxScroll);
            UpArrowBtn.bounds = UpArrow;
            DownArrowBtn.bounds = DownArrow;
            ThumbBtn.bounds = new Rectangle(ScrollCenter - 12, knobY, ThumbW, ThumbH);
            UpArrowBtn.draw(b);
            DownArrowBtn.draw(b);
            ThumbBtn.draw(b);
        }

        string foot = "F6/Esc: close  |  wheel, arrows or drag: scroll";
        foot += "  |  hover items for details";
        b.DrawString(Game1.smallFont, foot,
            new Vector2(xPositionOnScreen + Pad, yPositionOnScreen + height - 46), Cream * 0.9f);

        if (_hoverRowIdx >= 0 && _hoverRowRow != null)
            DrawTooltip(b, _hoverRowRow, mx, my);
    }

    private void DrawLine(SpriteBatch b, object[] line, int y, int cx, int h)
    {
        int innerW = width - Pad * 2;

        if (line[0] is "m")
        {
            DrawMap(b, y);
            return;
        }

        if (line[0] is "h")
        {
            var hl = (HeaderLine)line[1];
            int hh = SpriteText.getHeightOfString(hl.Text);
            b.Draw(Game1.staminaRect,
                new Rectangle(cx - 4, y + 30, 8, hh),
                Color.Gold * 0.9f);
            SpriteText.drawString(b, hl.Text, cx + 12, y + 30);
            return;
        }
        if (line[0] is "a")
        {
            // DO NEXT entry: priority-colored bar + indented text
            var al = (ActionLine)line[1];
            int lh = (int)Game1.smallFont.MeasureString("Ag").Y;
            int txtH = (al.Lines.Count - 1) * LineSpacing + lh;
            b.Draw(Game1.staminaRect,
                new Rectangle(cx + 1, y + 9, 8, Math.Min(txtH, h - 16)), al.Accent);
            for (int i = 0; i < al.Lines.Count; i++)
                b.DrawString(Game1.smallFont, al.Lines[i], new Vector2(cx + 18, y + 9 + i * LineSpacing), Ink);
            return;
        }
        if (line[0] is "t")
        {
            var tl = (TextLine)line[1];
            for (int i = 0; i < tl.Lines.Count; i++)
                b.DrawString(Game1.smallFont, tl.Lines[i], new Vector2(cx + 6, y + 8 + i * LineSpacing), Ink);
            return;
        }

        // ---- item row: icon | NAME LINE (big font, wide) / DESC LINE (small font) ----
        var row = ((RowLine)line[1]).Row;
        int starQuality = 0;
        bool ready = TrackerService.IsReady(row);

        if (row.Src.IsGoldAmount)
        {
            b.Draw(Game1.staminaRect, new Rectangle(cx + 12, y + 12, 24, 24), Color.Gold);
        }
        else
        {
            try
            {
                Item icon = ItemRegistry.Create(row.Src.Token);
                if (icon is StardewValley.Object obj && row.Src.MinQuality > 0)
                {
                    starQuality = Math.Min(4, row.Src.MinQuality);
                    obj.Quality = 0;
                }
                icon.drawInMenu(b, new Vector2(cx + 6, y + 6), 0.5f, 1f, 0.9f,
                    StackDrawType.Hide, Color.White, false);
            }
            catch { /* unknown id: skip icon */ }
            if (starQuality > 0)
            {
                Rectangle starSrc = starQuality switch
                {
                    1 => new Rectangle(338, 400, 8, 8),
                    2 => new Rectangle(346, 400, 8, 8),
                    4 => new Rectangle(346, 392, 8, 8),
                    _ => Rectangle.Empty
                };
                if (!starSrc.IsEmpty)
                {
                    b.Draw(Game1.mouseCursors,
                        new Rectangle(cx + 40, y + 40, 12, 12),
                        starSrc, Color.White);
                }
            }
        }

        int tx = cx + 64;
        int rightEdge = xPositionOnScreen + width - Pad - 44;

        // counter reserves space on the NAME line (right-aligned)
        float counterW = 0f;
        string counter = null;
        if (row.HaveCount >= 0)
        {
            counter = $"{Math.Min(row.HaveCount, row.Src.Stack)}/{row.Src.Stack}";
            counterW = Game1.smallFont.MeasureString(counter).X;
        }

        // name: nearly the whole row is available, so "(gold)"/"(silver)" survive
        string name = RowName(row);
        int nameMaxW = rightEdge - (counterW > 0 ? (int)counterW + 14 : 0) - tx;
        if (SpriteText.getWidthOfString(name) > nameMaxW)
        {
            while (name.Length > 1 && SpriteText.getWidthOfString(name + "...") > nameMaxW)
                name = name.Substring(0, name.Length - 1);
            name += "...";
        }
        SpriteText.drawString(b, name, tx, y + 8);

        if (counter != null)
            b.DrawString(Game1.smallFont, counter, new Vector2(rightEdge - counterW, y + 15),
                ready ? Color.ForestGreen : Ink);

        if (Mod.Tracker.IsLastChance(row))
        {
            string tag = "LAST CHANCE";
            Vector2 tagSize = Game1.smallFont.MeasureString(tag);
            b.DrawString(Game1.smallFont, tag,
                new Vector2(rightEdge - counterW - 14f - tagSize.X, y + 15),
                new Color(178, 48, 38));
        }
            b.DrawString(Game1.smallFont, counter, new Vector2(rightEdge - counterW, y + 15),
                ready ? Color.ForestGreen : Ink);

        // description on its own line(s): ALWAYS measured against real width (no bleed)
        var descLines = ((RowLine)line[1]).DescLines;

        int descTop = 8

            + SpriteText.getHeightOfString(RowName(row)) + 8;
        for (int i = 0; i < descLines.Count; i++)
            b.DrawString(Game1.smallFont, descLines[i], new Vector2(tx, y + descTop + i * DescSpacing), Ink);
    }

    // ---------- tooltip (mini lookup; complements Lookup Anything) ----------


    private void DrawTooltip(SpriteBatch b, Row row, int mx, int my)
    {
        bool ready = TrackerService.IsReady(row);

        var body = new List<string>();
        body.Add("Needed for: " + row.Src.Area
            + " - " + row.Src.BundleName);
        if (row.Src.IsGoldAmount)
        {
            body.Add("Pay this amount at the Vault");
        }
        if (row.Src.MinQuality > 0)
        {
            body.Add("Quality needed: "
                + QualityWord(row) + " or better");
        }
        if (ready)
        {
            body.Add("READY - take it to the Community Center");
        }
        else if (row.HaveCount >= 0)
        {
            body.Add("You have " + Math.Max(0, row.HaveCount)
                + " of " + row.Src.Stack
                + " at the needed quality");
        }
        body.Add("id: " + row.Src.Token);

        int lineH = (int)Game1.smallFont.MeasureString("Ag").Y;
        int pitch = lineH + 8;
        int titleH = SpriteText.getHeightOfString(RowName(row));

        int boxW = 280;
        foreach (var s in body)
        {
            int w2 = (int)Game1.smallFont.MeasureString(s).X + 28;
            if (w2 > boxW) boxW = w2;
        }
        int titleW = SpriteText.getWidthOfString(RowName(row)) + 56;
        if (titleW > boxW) boxW = titleW;

        const int titlePad = 12;
        const int dividerH = 2;
        int hgt = titlePad + titleH + titlePad
            + dividerH + 10
            + body.Count * pitch
            + 12;

        int tx = mx + 18;
        int ty = my + 22;
        int maxX = Game1.uiViewport.Width - boxW - 8;
        int maxY = Game1.uiViewport.Height - hgt - 8;
        if (tx > maxX) tx = maxX;
        if (ty > maxY) ty = maxY;
        if (tx < 8) tx = 8;
        if (ty < 8) ty = 8;

        IClickableMenu.drawTextureBox(b, tx, ty, boxW, hgt, Color.White);

        b.Draw(Game1.staminaRect,
            new Rectangle(tx + 10, ty + 10, boxW - 20, hgt - 20),
            new Color(244, 206, 138));

        SpriteText.drawString(b, RowName(row), tx + 20, ty + titlePad);

        int dividerY = ty + titlePad + titleH + titlePad;
        b.Draw(Game1.staminaRect,
            new Rectangle(tx + 10, dividerY, boxW - 20, dividerH),
            new Color(90, 45, 15) * 0.5f);

        int cy = dividerY + dividerH + 10;
        foreach (var s in body)
        {
            Color col = Ink;
            if (s.StartsWith("READY"))
            {
                col = Color.ForestGreen;
            }
            else if (s.StartsWith("id:"))
            {
                col = Ink * 0.55f;
            }
            b.DrawString(Game1.smallFont, s,
                new Vector2(tx + 16, cy), col);
            cy += pitch;
        }
    }

    private static string QualityWord(Row r)
    {
        if (r.Src.MinQuality == 4) return "iridium";
        if (r.Src.MinQuality == 2) return "gold";
        if (r.Src.MinQuality == 1) return "silver";
        return "any";
    }

    private static string RowName(Row r)
    {
        string name = r.DisplayName;
        if (r.Src.Stack > 1) name += $" x{r.Src.Stack}";
        return name;
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    private static string Describe(Row r)
    {
        if (r.Src.IsGoldAmount) return "one-time Vault payment";
        var m = r.Meta;
        if (m == null) return "needed for: " + r.Src.BundleName;

        var parts = new List<string>();
        switch (m.type)
        {
            case "crop":
                parts.Add(m.regrowDays > 0
                    ? $"farm, {m.cropDays}d (regrows {m.regrowDays}d)"
                    : $"farm, {m.cropDays}d");
                break;
            case "tree":
                parts.Add("fruit tree, 28 days to mature");
                break;
            default:
                if (!string.IsNullOrEmpty(m.location) && m.location != "-") parts.Add(m.location);
                break;
        }
        if (!string.IsNullOrEmpty(m.time) && m.time != "-" && m.time != "any") parts.Add(m.time);
        if (m.weather == "rain-only") parts.Add("RAIN only");
        else if (m.weather == "sunny-only") parts.Add("sunny only");
        else if (m.weather == "sunwind") parts.Add("sun/wind only");

        if (m.seasons.Length == 1) parts.Add(Cap(m.seasons[0]) + " only");
        else if (m.seasons.Length >= 2 && m.seasons.Length <= 3)
            parts.Add(string.Join("/", m.seasons.Select(Cap)));

        return string.Join(" - ", parts);
    }
}
