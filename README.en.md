<h1 align="center">SephPlanner</h1>

<p align="center">
  A Sephiria mod that works out where to place your Tablets and Artifacts and shows it on the game screen
</p>

<p align="center">
  <a href="https://github.com/nyabi-gh/SephPlanner/releases/latest"><img src="https://img.shields.io/github/v/release/nyabi-gh/SephPlanner?style=flat-square&label=Latest&color=b9a7f5" alt="Latest release"></a>
  <a href="https://github.com/nyabi-gh/SephPlanner/releases"><img src="https://img.shields.io/github/downloads/nyabi-gh/SephPlanner/total?style=flat-square&label=Downloads&color=77d8b0" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-b9a7f5?style=flat-square" alt="License"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <b>English</b> · <a href="README.ja.md">日本語</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.zh-TW.md">繁體中文</a></p>

## Download

Where to download it depends on your game branch.

| Game | Download |
| --- | --- |
| **1.1 beta** (`workshop-beta` branch) | Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875) |
| **Default branch**, Windows | `SephPlanner-v<version>.zip` from the [latest release](https://github.com/nyabi-gh/SephPlanner/releases/latest) |
| **Default branch**, macOS | `SephPlanner-macos-v<version>.zip` from the [latest release](https://github.com/nyabi-gh/SephPlanner/releases/latest) |

The ZIP is the version with BepInEx included, and it also runs on the 1.1 beta. Download the ZIP above, not `Source code`.

> [!NOTE]
> This is a mod made by an individual, not affiliated with TEAM HORAY. You use mods at your own risk, so back up any saves you care about first.

## Installation

### Workshop (game 1.1 beta)

1. In Steam, right-click Sephiria → **Properties → Game Versions & Betas** and choose `workshop-beta`.
2. Press **Subscribe** on the [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875).
3. Launch the game and check that SephPlanner is enabled in the Workshop window on the title screen.
4. Start a run; SephPlanner appears on screen.

Steam downloads updates automatically.

**If you used the BepInEx version (ZIP),** delete `SephPlanner.Plugin.dll` and `SephPlanner.Core.dll` from `BepInEx/plugins` in the game folder so that the Workshop version can turn on. If you leave them, only the BepInEx version runs. Your settings are carried over the first time you launch the Workshop version.

### ZIP (BepInEx version)

**Windows**

1. Close the game and extract the ZIP.
2. In Steam, right-click Sephiria → **Manage → Browse local files** to open the game folder.
3. Copy **the contents of** the `게임 폴더에 복사` (Copy to game folder) folder into the game folder.
4. Launch the game and start a run; SephPlanner appears on screen.

**macOS**

1. Close the game and copy the contents of `게임 폴더에 복사` (Copy to game folder) into the game folder. (Put them next to `Sephiria.app`, not inside it.)
2. Run the following in Terminal, replacing `<game folder>` with the actual path.
   ```sh
   xattr -c "<game folder>/libdoorstop.dylib" "<game folder>/run_bepinex.sh"
   chmod +x "<game folder>/run_bepinex.sh"
   ```
3. In Steam, open Sephiria's **Properties → General → Launch Options** and enter `"<game folder>/run_bepinex.sh" %command%`.
4. Launch the game. See the [macOS installation guide](docs/INSTALL-macos.txt) (Korean) for details.

The ZIP version updates itself. When a new version is out, it tells you when you start the game; press `Update` and restart the game to apply it.

If you already use BepInEx for other mods, just put the two DLLs from the ZIP's `BepInEx/plugins` (`SephPlanner.Plugin.dll`, `SephPlanner.Core.dll`) in the same place in your game folder. To update manually, overwrite these two files.

## How to use

1. **Start a run.** Your current inventory score and the suggested layout's score appear in a corner of the screen.
2. **Expand it with `F7`.** Slots that would change are outlined in gold, and hovering over a slot shows its effects and why it is suggested. It expands on its own when you open a chest, shop, Sephirite, combiner or enchant window.
3. **Press `F8`.** The suggested layout is applied. If you'd rather move things yourself, follow the move instructions from top to bottom.

It also suggests what to pick from chests, shops and Sephirite, which Tablets to combine and where to enchant. Press `F1` to preview the layout with a candidate you haven't obtained yet.

> [!IMPORTANT]
> The score is SephPlanner's own rating for comparing layouts, not an official in-game number. Please also weigh unique effects and the way you like to play.

Setting a build, per-item rules and more are covered in the [detailed usage guide](docs/USAGE.md) (Korean).

## Hotkeys

| Key | Function |
| --- | --- |
| `F1` | Candidate preview |
| `F2` | Build settings |
| `F3` | Settings |
| `F4` | Hide panel |
| `F5` | Panel opacity |
| `F6` | Move panel |
| `F7` | Collapse/expand panel |
| `F8` | Apply suggested layout |
| `F9` | Reload item data |
| `F10` | Save state for a bug report |

Keys can be changed in `F3`. With a gamepad, press `View` while a game window such as the inventory or a chest is open.

## Multiplayer

SephPlanner only works on your own screen, so the other players don't need to install it.

In multiplayer, auto-place is off by default. Get the other players' consent first, then turn it on with `Auto-place in multiplayer` in `F3`. If a server sync warning appears, rejoin the lobby before using it.

## FAQ

**I can't see the panel.** Check that you are in a run, that you didn't hide it with `F4`, and that `Show planner` is on in `F3`. For the Workshop version, check that it is enabled in the Workshop window on the title screen and that no SephPlanner DLLs are left in `BepInEx/plugins`. For the ZIP version, check that both DLLs are in `BepInEx/plugins`.

**Items are recognized wrongly after a game patch.** Press `F9` to reload item data.

**Calculation takes a long time.** Turn on `High-performance mode (multi-core)` in `F3` to use more CPU cores and calculate faster. The results are the same. If the game stutters, turn it back off.

**I don't see any suggestions.** Check that `Acquire/combine/enchant/remove suggestions` is on in `F3`.

**Hotkeys don't work.** Hotkeys don't respond while you hold Ctrl, Alt, Shift or Cmd that isn't part of the hotkey. Let go of that key first. Holding movement keys is fine. Also check the `Shortcuts` tab in `F3` for conflicts with other keys. Turning off `Show planner` also stops `F1` and `F4`–`F8`.

**Still not solved.** Press `F10` while the problem is happening, then let us know through [Issues](https://github.com/nyabi-gh/SephPlanner/issues), a comment on the Workshop page or [nyabi@tb.pro](mailto:nyabi@tb.pro) along with the **report number** shown on screen. The saved diagnostic file contains game data, so do not post it to public Issues. ([Reporting guide](docs/USAGE.md#제보에-필요한-정보) (Korean))

**I want to uninstall it.** For the Workshop version, unsubscribe on the Workshop page. For the ZIP version, close the game and delete `SephPlanner.Plugin.dll` and `SephPlanner.Core.dll` from `BepInEx/plugins`. To remove the settings too, also delete the `%LOCALAPPDATA%\SephPlanner` folder and `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg`. If you use other mods, leave BepInEx in place.

## License

SephPlanner is released under the [MIT License](LICENSE), so you are free to use, modify and distribute it.

In line with the developer's modding policy, it is run non-commercially, with no ads, donations or subscriptions. When sharing it, please pass along a link to the [release](https://github.com/nyabi-gh/SephPlanner/releases/latest) or the [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875) instead of the file.

## Contributing

Bug reports and pull requests are welcome. How to build and test is described in the [development docs](docs/DEVELOPMENT.md) (Korean).
