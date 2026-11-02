<h1 align="center">SephPlanner</h1>

<p align="center">
  計算《塞菲莉亞》石板與神器的擺放位置，並顯示在遊戲畫面上的模組
</p>

<p align="center">
  <a href="https://github.com/nyabi-gh/SephPlanner/releases/latest"><img src="https://img.shields.io/github/v/release/nyabi-gh/SephPlanner?style=flat-square&label=%E6%9C%80%E6%96%B0&color=b9a7f5" alt="最新版本"></a>
  <a href="https://github.com/nyabi-gh/SephPlanner/releases"><img src="https://img.shields.io/github/downloads/nyabi-gh/SephPlanner/total?style=flat-square&label=%E4%B8%8B%E8%BC%89&color=77d8b0" alt="下載次數"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/%E6%8E%88%E6%AC%8A-MIT-b9a7f5?style=flat-square" alt="授權"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <a href="README.en.md">English</a> · <a href="README.ja.md">日本語</a> · <a href="README.zh-CN.md">简体中文</a> · <b>繁體中文</b></p>

## 下載

| 平台 | 需求 | 下載 |
| --- | --- | --- |
| **Windows** | Steam 版《塞菲莉亞》 | [最新版本](https://github.com/nyabi-gh/SephPlanner/releases/latest)中的 `SephPlanner-v<版本>.zip` |
| **macOS** | Steam 版《塞菲莉亞》 | [最新版本](https://github.com/nyabi-gh/SephPlanner/releases/latest)中的 `SephPlanner-macos-v<版本>.zip` |

請下載上方的 ZIP，而不是 `Source code`。ZIP 內已附 BepInEx，不必另外下載。

SephPlanner 會自動更新。有新版本時，啟動遊戲就會通知你，按下 `更新` 後重新啟動遊戲即可套用。

> [!NOTE]
> 這是與 TEAM HORAY 無關的非官方工具。使用模組須自行承擔風險，重要的存檔請事先備份。

## 安裝

**Windows**

1. 關閉遊戲，解壓縮 ZIP。
2. 在 Steam 對《塞菲莉亞》按右鍵 → **管理 → 瀏覽本機檔案**，開啟遊戲資料夾。
3. 將 `게임 폴더에 복사`（複製到遊戲資料夾）資料夾**裡面的內容**複製到遊戲資料夾。
4. 啟動遊戲並開始探險，畫面上就會出現 SephPlanner。

**macOS**

1. 關閉遊戲，將 `게임 폴더에 복사`（複製到遊戲資料夾）裡面的內容複製到遊戲資料夾。（放在 `Sephiria.app` 旁邊，而不是裡面。）
2. 在終端機執行下列指令，並把 `<遊戲資料夾>` 換成實際路徑。
   ```sh
   xattr -c "<遊戲資料夾>/libdoorstop.dylib" "<遊戲資料夾>/run_bepinex.sh"
   chmod +x "<遊戲資料夾>/run_bepinex.sh"
   ```
3. 在 Steam 開啟《塞菲莉亞》的 **內容 → 一般 → 啟動選項**，輸入 `"<遊戲資料夾>/run_bepinex.sh" %command%`。
4. 啟動遊戲。詳細說明請見 [macOS 安裝說明](docs/INSTALL-macos.txt)（韓文）。

如果你已經因為其他模組而在使用 BepInEx，只要把 ZIP 中 `BepInEx/plugins` 裡的兩個 DLL（`SephPlanner.Plugin.dll`、`SephPlanner.Core.dll`）放到遊戲資料夾的相同位置即可。手動更新時也是覆蓋這兩個檔案。

## 使用方式

1. **開始探險。** 畫面會顯示目前背包分數與建議配置的分數，會變動的位置以金色外框標示。
2. **確認內容。** 將游標移到格子上，就能看到效果與推薦理由。
3. **按下 `F8`。** 套用建議配置。若想自己移動，照著移動提示由上往下操作即可。

也會推薦在寶箱、商店與賽菲萊特中該選什麼，以及要合成、附魔哪些石板。按 `F1` 可以預覽放入尚未取得的候選物品後的配置。

> [!IMPORTANT]
> 分數是用來比較配置的自訂評估值，並非遊戲的官方數值。請一併考量固有效果與你想要的玩法。

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
| `F8` | 套用建議配置 |
| `F9` | 重新讀取物品資料 |
| `F10` | 儲存狀態以回報錯誤 |

按鍵可在 `F3` 中變更。若要使用手把，請在 `F3` 開啟 `用手把 View 鍵開啟視窗`。

## 多人遊戲

多人遊戲中，自動配置預設為關閉。請先取得一起遊玩的玩家同意，再到 `F3` 的 `多人遊戲自動佈局` 開啟。如果出現伺服器同步警告，請重新進入房間後再使用。

## 常見問題

**看不到面板。** 請確認是否已開始探險、是否用 `F4` 隱藏了面板，以及兩個 DLL 是否位於 `BepInEx/plugins`。

**遊戲更新後辨識異常。** 按 `F9` 重新讀取物品資料。

**計算很久。** 開啟 `F3` 的 `高效能模式（多核心運算）`，會使用更多 CPU 核心加快計算，結果不變。如果遊戲變得卡頓，請再關閉。

**看不到推薦。** 請確認 `F3` 的 `獲得·合成·附魔·移除推薦` 是否已開啟。

**快速鍵沒有反應。** 請在 `F3` 的`快速鍵`分頁確認是否與其他按鍵衝突。

**還是無法解決。** 在問題發生的狀態下按 `F10`，並附上畫面顯示的**回報編號**，透過 [Issues](https://github.com/nyabi-gh/SephPlanner/issues) 或 [nyabi@tb.pro](mailto:nyabi@tb.pro) 告訴我們。儲存的診斷檔案含有遊戲資料，請勿上傳到公開的 Issues。（[回報說明](docs/USAGE.md#제보에-필요한-정보)（韓文））

**想要移除。** 關閉遊戲，刪除 `BepInEx/plugins` 中的 `SephPlanner.Plugin.dll` 與 `SephPlanner.Core.dll`。若連設定也要刪除，請一併刪除 `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg` 與 `%LOCALAPPDATA%\SephPlanner` 資料夾。如果還有使用其他模組，請保留 BepInEx。

## 授權

SephPlanner 採用 [MIT 授權](LICENSE)，可自由使用、修改與散布。

依照開發商的模組政策，本專案以非營利方式經營，沒有廣告、贊助或訂閱。分享時請提供 [發布頁面連結](https://github.com/nyabi-gh/SephPlanner/releases/latest)，而不是檔案本身。

## 參與開發

歡迎回報錯誤與提交 Pull Request。建置與測試方法請見 [開發文件](docs/DEVELOPMENT.md)（韓文）。
