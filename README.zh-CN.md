<h1 align="center">SephPlanner</h1>

<p align="center">
  计算《塞菲莉亚》石板与神器的摆放位置，并显示在游戏画面上的模组
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/%E8%AE%B8%E5%8F%AF%E8%AF%81-MIT-b9a7f5?style=flat-square" alt="许可证"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <a href="README.en.md">English</a> · <a href="README.ja.md">日本語</a> · <b>简体中文</b> · <a href="README.zh-TW.md">繁體中文</a></p>

## 下载

在 [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)订阅即可。支持游戏 1.1 及以上。

内含 BepInEx 的 ZIP 版已在 0.6.6 停止发布。如果你正在使用 ZIP 版，请按照下方的安装说明改用创意工坊版。

> [!NOTE]
> 本模组由个人制作，与 TEAM HORAY 无关。使用模组的风险由你自行承担，重要的存档请提前备份。如果游戏出现异常，请先关闭 SephPlanner。关闭后问题仍然存在，请向 TEAM HORAY 反馈；关闭后问题消失，请向这里反馈。

## 安装

1. 在 [创意工坊页面](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)点击**订阅**。
2. 启动游戏，在标题画面的创意工坊窗口中确认 SephPlanner 已启用。
3. 开始探险后，SephPlanner 就会出现在画面上。

更新由 Steam 自动下载。

**如果你之前用的是 BepInEx 版（ZIP）**，需要删除游戏文件夹 `BepInEx/plugins` 中的 `SephPlanner.Plugin.dll` 和 `SephPlanner.Core.dll`，创意工坊版才会启用。如果不删除，只会运行 BepInEx 版。之前的设置会在第一次启动创意工坊版时导入。

## 使用方法

1. **开始探险。** 画面角落会显示当前背包分数和建议布局的分数。
2. **按 `F7` 展开。** 需要变动的位置会以金色边框标出，将光标移到格子上可以看到效果和推荐理由。打开宝箱、商店、赛菲莱特、组合器或附魔窗口时会自动展开。
3. **按下 `F8`。** 建议布局会被应用。如果想自己移动，按照移动提示从上往下操作即可。

它还会推荐在宝箱、商店和赛菲莱特中选什么、合成哪些石板，以及附魔到哪里。按 `F1` 可以预览放入尚未获得的候选物品后的布局。

> [!IMPORTANT]
> 分数是用于比较布局的自定评价值，并非游戏的官方数值。请同时考虑固有效果和你想要的玩法。

指定构筑、按物品设置规则等内容见 [详细使用说明](docs/USAGE.md)（韩语）。

## 快捷键

| 按键 | 功能 |
| --- | --- |
| `F1` | 候选预览 |
| `F2` | 构筑设置 |
| `F3` | 设置 |
| `F4` | 隐藏面板 |
| `F5` | 面板不透明度 |
| `F6` | 移动面板 |
| `F7` | 折叠/展开面板 |
| `F8` | 应用建议布局 |
| `F9` | 重新读取物品数据 |
| `F10` | 保存状态用于反馈问题 |

按键可以在 `F3` 中更改。使用手柄时，在背包、宝箱等游戏窗口打开期间按 `View` 键即可打开窗口。

## 多人游戏

SephPlanner 只作用于你自己的画面，所以一起游玩的玩家不需要安装。

多人游戏中，自动布局默认关闭。请先征得一起游玩的玩家同意，再在 `F3` 的 `多人游戏自动布局` 中开启。如果出现服务器同步警告，请重新进入房间后再使用。

## 常见问题

**看不到面板。** 请确认是否已开始探险、是否用 `F4` 隐藏了面板，以及 `F3` 中的 `显示规划面板` 是否已开启。如果是创意工坊版，请检查标题画面的创意工坊窗口中是否已启用，以及 `BepInEx/plugins` 中是否还留有 SephPlanner 的 DLL。

**游戏更新后识别不正常。** 按 `F9` 重新读取物品数据。

**计算很慢。** 开启 `F3` 中的 `高性能模式（多核计算）`，会使用更多 CPU 核心来加快计算，结果不变。如果游戏卡顿，请再关掉。

**看不到推荐。** 请确认 `F3` 中的 `获得·合成·附魔·移除推荐` 已开启。

**快捷键没反应。** 按住快捷键组合之外的 Ctrl、Alt、Shift 或 Cmd 时快捷键不会响应。请先松开该键再按。按住移动键没有关系。也请在 `F3` 的`快捷键`页中确认是否与其他按键冲突。关闭 `显示规划面板` 后，`F1`、`F4`~`F8` 也会停用。

**还是没解决。** 在问题出现时按下 `F10`，并附上画面上显示的**报告编号**，通过 [Issues](https://github.com/nyabi-gh/SephPlanner/issues)、创意工坊页面的留言或 [nyabi@tb.pro](mailto:nyabi@tb.pro) 告诉我们。保存的诊断文件包含游戏数据，请不要上传到公开的 Issues。（[反馈说明](docs/USAGE.md#제보에-필요한-정보)（韩语））

**想要卸载。** 创意工坊版请在创意工坊页面取消订阅。ZIP 版请关闭游戏，删除 `BepInEx/plugins` 中的 `SephPlanner.Plugin.dll` 和 `SephPlanner.Core.dll`。如果连设置也要删除，请一并删除 `%LOCALAPPDATA%\SephPlanner` 文件夹和 `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg`。如果还在使用其他模组，请保留 BepInEx。

## 许可证

SephPlanner 采用 [MIT 许可证](LICENSE)，你可以自由使用、修改和分发。

根据开发商的模组政策，本项目以非营利方式运营，不设广告、赞助或订阅。分享时请发送 [创意工坊页面](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)的链接，而不是文件本身。

## 参与开发

欢迎反馈问题和提交拉取请求。构建与测试方法见 [开发文档](docs/DEVELOPMENT.md)（韩语）。
