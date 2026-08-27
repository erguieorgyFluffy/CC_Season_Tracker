# CC Season Tracker

A Stardew Valley 1.6 mod (SMAPI 4.x) that tracks Community Center bundles:
a clickable room map, per-season planning, LAST CHANCE alerts, and a live
section in the To-Dew overlay.

  <td><img width="320" alt="map" src="https://github.com/user-attachments/assets/1cb2d3ff-56bf-4775-8807-6308c8d839da" /></td>
  <td><img width="320" alt="next" src="https://github.com/user-attachments/assets/dda7b92d-1dce-4e49-a918-f52415757574" /></td>
  <td><img width="320" alt="toast" src="https://github.com/user-attachments/assets/4c312dc0-9aec-4af0-ace3-9a61aca24f68" /></td>

## Features

- **F6 planner panel** - walkable overlay, never blocks movement
  - Next: a prioritized DO NEXT action list
  - Bundles: the Community Center map with clickable rooms; each room
    opens its vanilla bundle page directly
  - Now, Fish, Spring, Summer, Fall, Winter, Anytime, Info tabs
- **LAST CHANCE system** - red badge on season-exclusive rows and a
  morning toast when 3 or fewer days remain
- **Rain intelligence** - toast when rain starts (rain-only fish listed),
  6:30pm forecast toast, catchable-now filtering
- **Planting deadlines** - "plant today" and "too late" alerts, planted-crop
  awareness, Red Cabbage Year-1 Traveling Cart countdown
- **Donation detection** - scans inventory, shipping bin and every chest
- **Joja-aware** - member notice, and a calm takeover screen instead of a
  wall of impossible bundles
- **To-Dew integration (optional)** - a live "CC Tracker" section in the
  To-Dew overlay; checkmark dismisses an item until tomorrow
- **Unlock tutorial** - until the CC is open, the panel explains the
  unlock steps, then disappears on its own

## Install

1. Install [SMAPI](https://smapi.io/)
2. Unzip the mod into your Mods folder
3. Optional but recommended: [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)
   and [To-Dew](https://www.nexusmods.com/stardewvalley/mods/10705)

## Configuration

Everything is configurable in-game via Generic Mod Config Menu:

- Keybinds (panel F6, HUD V by default)
- Per-category notification toggles and toast durations
- HUD corner position and per-line content toggles
- ToDew section: master switch plus per-category toggles

## Customization

- assets/rooms.json - the room hotspot polygons on the Bundles map,
  as fractions of rooms.png (edit, then reopen the panel)
- assets/items.json - the strategy database (seasons, locations, times)

## Building from source

Requires the .NET 6 SDK. From the project folder, with the game CLOSED:

    dotnet build

The build self-checks assets/rooms.png (must be a standard 8-bit PNG) and
auto-deploys to your game Mods folder via ModBuildConfig.

## Compatibility

- Stardew Valley 1.6.15, SMAPI 4.x
- Single-player; co-op not tested
- Works on saves that never opened the CC, and on the Joja route

## Credits

- [To-Dew](https://github.com/jltaylor-us/StardewToDew) by jltaylor-us -
  overlay API used for the integration (bundled interface, MIT)
- [Generic Mod Config Menu](https://github.com/spacechase0/StardewValleyMods) by spacechase0
- [SMAPI](https://smapi.io/) by Pathoschild

## License

MIT - see LICENSE. ToDewApi.cs retains its own license header.
