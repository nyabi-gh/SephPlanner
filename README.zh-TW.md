<h1 align="center">SephPlanner</h1>

<p align="center">
  計算《塞菲莉亞》石板與神器的擺放位置，並顯示在遊戲畫面上的模組
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/%E6%8E%88%E6%AC%8A-MIT-b9a7f5?style=flat-square" alt="授權"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <a href="README.en.md">English</a> · <a href="README.ja.md">日本語</a> · <a href="README.zh-CN.md">简体中文</a> · <b>繁體中文</b></p>

## 下載

在 [Steam 工作坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)訂閱即可。支援遊戲 1.1 以上。

內含 BepInEx 的 ZIP 版已在 0.6.6 停止發布。如果你正在使用 ZIP 版，請依照下方的安裝說明改用工作坊版。

> [!NOTE]
> 這是由個人製作、與 TEAM HORAY 無關的模組。使用模組須自行承擔風險，重要的存檔請事先備份。如果遊戲出現異常，請先關閉 SephPlanner。關閉後問題仍然存在，請向 TEAM HORAY 回報；關閉後問題消失，請向這裡回報。

## 安裝

1. 在 [工作坊頁面](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)按下**訂閱**。
2. 啟動遊戲，在標題畫面的工作坊視窗確認 SephPlanner 已啟用。
3. 開始探險後，畫面上就會出現 SephPlanner。

更新會由 Steam 自動下載。

**如果你之前使用 BepInEx 版（ZIP）**，必須刪除遊戲資料夾 `BepInEx/plugins` 中的 `SephPlanner.Plugin.dll` 與 `SephPlanner.Core.dll`，工作坊版才會啟用。若不刪除，只會執行 BepInEx 版。原本的設定會在第一次啟動工作坊版時匯入。

## 使用方式

1. **開始探險。** 畫面角落會顯示目前背包分數與建議佈局的分數。
2. **按 `F7` 展開。** 會變動的位置以金色外框標示，將游標移到格子上就能看到效果與推薦理由。開啟寶箱、商店、賽菲萊特、組合器或附魔視窗時會自動展開。
3. **按下 `F8`。** 套用建議佈局。若想自己移動，照著移動提示由上往下操作即可。

也會推薦在寶箱、商店與賽菲萊特中該選什麼、要合成哪些石板，以及要附魔在哪裡。按 `F1` 可以預覽放入尚未取得的候選物品後的佈局。

> [!IMPORTANT]
> 分數是用來比較佈局的自訂評估值，並非遊戲的官方數值。請一併考量固有效果與你想要的玩法。

指定構築、各物品規則等內容請見 [詳細使用說明](docs/USAGE.md)（韓文）。

## 快速鍵

| 按鍵 | 功能 |
| --- | --- |
| `F1` | 候選預覽 |
| `F2` | 構築設定 |
| `F3` | 設定 |
| `F4` | 隱藏面板 |
| `F5` | 面板不透明度 |
| `F6` | 移動面板 |
| `F7` | 收合/展開面板 |
| `F8` | 套用建議佈局 |
| `F9` | 重新讀取物品資料 |
| `F10` | 儲存狀態以回報錯誤 |

按鍵可在 `F3` 中變更。使用手把時，在背包、寶箱等遊戲視窗開啟期間按 `View` 鍵即可開啟視窗。

## 多人遊戲

SephPlanner 只會作用在你自己的畫面上，所以一起遊玩的玩家不需要安裝。

多人遊戲中，自動佈局預設為關閉。請先取得一起遊玩的玩家同意，再到 `F3` 的 `多人遊戲自動佈局` 開啟。如果出現伺服器同步警告，請重新進入房間後再使用。

## 常見問題

**看不到面板。** 請確認是否已開始探險、是否用 `F4` 隱藏了面板，以及 `F3` 的 `顯示規劃面板` 是否已開啟。如果是工作坊版，請檢查標題畫面的工作坊視窗中是否已啟用，以及 `BepInEx/plugins` 是否還留有 SephPlanner 的 DLL。

**遊戲更新後辨識異常。** 按 `F9` 重新讀取物品資料。

**計算很久。** 開啟 `F3` 的 `高效能模式（多核心運算）`，會使用更多 CPU 核心加快計算，結果不變。如果遊戲變得卡頓，請再關閉。

**看不到推薦。** 請確認 `F3` 的 `獲得·合成·附魔·移除推薦` 是否已開啟。

**快速鍵沒有反應。** 按住快速鍵組合以外的 Ctrl、Alt、Shift 或 Cmd 時，快速鍵不會有反應。請先放開該鍵再按。按住移動鍵沒有關係。也請在 `F3` 的`快速鍵`分頁確認是否與其他按鍵衝突。關閉 `顯示規劃面板` 後，`F1`、`F4`~`F8` 也會停止運作。

**還是無法解決。** 在問題發生的狀態下按 `F10`，並附上畫面顯示的**回報編號**，透過 [Issues](https://github.com/nyabi-gh/SephPlanner/issues)、工作坊頁面的留言或 [nyabi@tb.pro](mailto:nyabi@tb.pro) 告訴我們。儲存的診斷檔案含有遊戲資料，請勿上傳到公開的 Issues。（[回報說明](docs/USAGE.md#제보에-필요한-정보)（韓文））

**想要移除。** 工作坊版請在工作坊頁面取消訂閱。ZIP 版請關閉遊戲，刪除 `BepInEx/plugins` 中的 `SephPlanner.Plugin.dll` 與 `SephPlanner.Core.dll`。若連設定也要刪除，請一併刪除 `%LOCALAPPDATA%\SephPlanner` 資料夾與 `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg`。如果還有使用其他模組，請保留 BepInEx。

## 授權

SephPlanner 採用 [MIT 授權](LICENSE)，可自由使用、修改與散布。

依照開發商的模組政策，本專案以非營利方式經營，沒有廣告、贊助或訂閱。分享時請提供 [工作坊頁面](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)的連結，而不是檔案本身。

## 參與開發

歡迎回報錯誤與提交 Pull Request。建置與測試方法請見 [開發文件](docs/DEVELOPMENT.md)（韓文）。
