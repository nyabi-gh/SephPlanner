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

| Platform | Requirements | Download |
| --- | --- | --- |
| **Windows** | Sephiria on Steam | `SephPlanner-v<version>.zip` from the [latest release](https://github.com/nyabi-gh/SephPlanner/releases/latest) |
| **macOS** | Sephiria on Steam | `SephPlanner-macos-v<version>.zip` from the [latest release](https://github.com/nyabi-gh/SephPlanner/releases/latest) |

Download the ZIP above, not `Source code`. BepInEx is included in the ZIP, so there is nothing else to download.

SephPlanner updates itself. When a new version is out, it tells you when you start the game; press `Update` and restart the game to apply it.

> [!NOTE]
> This is an unofficial tool, not affiliated with TEAM HORAY. You use mods at your own risk, so back up any saves you care about first.

## Installation

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

If you already use BepInEx for other mods, just put the two DLLs from the ZIP's `BepInEx/plugins` (`SephPlanner.Plugin.dll`, `SephPlanner.Core.dll`) in the same place in your game folder. To update manually, overwrite these two files.

## How to use

1. **Start a run.** Your current bag score and the suggested layout's score are shown, and slots that would change are outlined in gold.
2. **Check it.** Hover over a slot to see its effects and why it is suggested.
3. **Press `F8`.** The suggested layout is applied. If you'd rather move things yourself, follow the move instructions from top to bottom.

It also suggests what to pick from chests, shops and Sephirite, and which Tablets to mix and enchant. Press `F1` to preview the layout with a candidate you haven't obtained yet.

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

In multiplayer, auto-place is off by default. Get the other players' consent first, then turn it on with `Auto-place in multiplayer` in `F3`. If a server sync warning appears, rejoin the room before using it.

## FAQ

**I can't see the panel.** Check that you are in a run, that you didn't hide it with `F4`, and that both DLLs are in `BepInEx/plugins`.

**Items are recognized wrongly after a game patch.** Press `F9` to reload item data.

**Calculation takes a long time.** Turn on `High-performance mode (multi-core)` in `F3` to use more CPU cores and calculate faster. The results are the same. If the game stutters, turn it back off.

**I don't see any suggestions.** Check that `Acquire/mix/enchant/remove suggestions` is on in `F3`.

**Hotkeys don't work.** Check the `Shortcuts` tab in `F3` for conflicts with other keys.

**Still not solved.** Press `F10` while the problem is happening, then let us know through [Issues](https://github.com/nyabi-gh/SephPlanner/issues) or [nyabi@tb.pro](mailto:nyabi@tb.pro) along with the **report number** shown on screen. The saved diagnostic file contains game data, so do not post it to public Issues. ([Reporting guide](docs/USAGE.md#제보에-필요한-정보) (Korean))

**I want to uninstall it.** Close the game and delete `SephPlanner.Plugin.dll` and `SephPlanner.Core.dll` from `BepInEx/plugins`. To remove the settings too, also delete `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg` and the `%LOCALAPPDATA%\SephPlanner` folder. If you use other mods, leave BepInEx in place.

## License

SephPlanner is released under the [MIT License](LICENSE), so you are free to use, modify and distribute it.

In line with the developer's modding policy, it is run non-commercially, with no ads, donations or subscriptions. When sharing it, please pass along the [release link](https://github.com/nyabi-gh/SephPlanner/releases/latest) instead of the file.

## Contributing

Bug reports and pull requests are welcome. How to build and test is described in the [development docs](docs/DEVELOPMENT.md) (Korean).
