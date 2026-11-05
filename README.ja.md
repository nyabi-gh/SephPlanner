<h1 align="center">SephPlanner</h1>

<p align="center">
  セフィリアの石版・アーティファクトの配置を計算してゲーム画面に表示するMOD
</p>

<p align="center">
  <a href="https://github.com/nyabi-gh/SephPlanner/releases/latest"><img src="https://img.shields.io/github/v/release/nyabi-gh/SephPlanner?style=flat-square&label=%E6%9C%80%E6%96%B0&color=b9a7f5" alt="最新リリース"></a>
  <a href="https://github.com/nyabi-gh/SephPlanner/releases"><img src="https://img.shields.io/github/downloads/nyabi-gh/SephPlanner/total?style=flat-square&label=%E3%83%80%E3%82%A6%E3%83%B3%E3%83%AD%E3%83%BC%E3%83%89&color=77d8b0" alt="ダウンロード"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/%E3%83%A9%E3%82%A4%E3%82%BB%E3%83%B3%E3%82%B9-MIT-b9a7f5?style=flat-square" alt="ライセンス"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <a href="README.en.md">English</a> · <b>日本語</b> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.zh-TW.md">繁體中文</a></p>

## ダウンロード

ゲームのブランチによってダウンロード先が異なります。

| ゲーム | ダウンロード |
| --- | --- |
| **1.1 ベータ**（`workshop-beta` ブランチ） | [Steamワークショップ](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)でサブスクライブ |
| **デフォルトブランチ**、Windows | [最新リリース](https://github.com/nyabi-gh/SephPlanner/releases/latest)の `SephPlanner-v<バージョン>.zip` |
| **デフォルトブランチ**、macOS | [最新リリース](https://github.com/nyabi-gh/SephPlanner/releases/latest)の `SephPlanner-macos-v<バージョン>.zip` |

ZIPはBepInExが入った版で、1.1 ベータでも動きます。`Source code` ではなく、上のZIPをダウンロードしてください。

> [!NOTE]
> TEAM HORAYとは関係のない個人が作ったMODです。MODの使用は自己責任でお願いします。大切なセーブデータは事前にバックアップしておいてください。

## インストール

### ワークショップ（ゲーム 1.1 ベータ）

1. Steamでセフィリアを右クリック → **プロパティ → ゲームバージョンとベータ** で `workshop-beta` を選びます。
2. [ワークショップのページ](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)で**サブスクライブ**を押します。
3. ゲームを起動し、タイトル画面のワークショップウィンドウでSephPlannerが有効になっているか確認します。
4. 探検を始めると、画面にSephPlannerが表示されます。

アップデートはSteamが自動でダウンロードします。

**BepInEx版（ZIP）を使っていた方は**、ゲームフォルダの `BepInEx/plugins` から `SephPlanner.Plugin.dll` と `SephPlanner.Core.dll` を削除しないと、ワークショップ版が有効になりません。そのままにするとBepInEx版だけが動きます。使っていた設定は、ワークショップ版を初めて起動したときに引き継がれます。

### ZIP（BepInEx版）

**Windows**

1. ゲームを終了し、ZIPを展開します。
2. Steamでセフィリアを右クリック → **管理 → ローカルファイルを閲覧** でゲームフォルダを開きます。
3. `게임 폴더에 복사`（ゲームフォルダにコピー）フォルダの**中身**をゲームフォルダにコピーします。
4. ゲームを起動して探検を始めると、画面にSephPlannerが表示されます。

**macOS**

1. ゲームを終了し、`게임 폴더에 복사`（ゲームフォルダにコピー）の中身をゲームフォルダにコピーします。（`Sephiria.app` の中ではなく、その隣に置きます。）
2. ターミナルで次のコマンドを実行します。`<ゲームフォルダ>` は実際のパスに置き換えてください。
   ```sh
   xattr -c "<ゲームフォルダ>/libdoorstop.dylib" "<ゲームフォルダ>/run_bepinex.sh"
   chmod +x "<ゲームフォルダ>/run_bepinex.sh"
   ```
3. Steamでセフィリアの **プロパティ → 一般 → 起動オプション** に `"<ゲームフォルダ>/run_bepinex.sh" %command%` と入力します。
4. ゲームを起動します。詳しくは [macOSインストールガイド](docs/INSTALL-macos.txt)（韓国語）をご覧ください。

ZIP版は自動でアップデートします。新しいバージョンが出るとゲーム起動時にお知らせが表示され、`アップデート` を押してゲームを再起動すると適用されます。

すでに他のMODでBepInExを使っている場合は、ZIPの `BepInEx/plugins` にあるDLL 2つ（`SephPlanner.Plugin.dll`、`SephPlanner.Core.dll`）だけをゲームフォルダの同じ場所に入れれば大丈夫です。手動でアップデートするときも、この2つのファイルを上書きします。

## 使い方

1. **探検を始めます。** 画面の隅に、現在のバッグのスコアと提案配置のスコアが表示されます。
2. **`F7` で展開します。** 変わる場所が金色の枠で示され、枠にカーソルを合わせると効果とおすすめの理由が表示されます。宝箱・ショップ・セフィライト・合成機・エンチャントの画面を開くと自動で展開されます。
3. **`F8` を押します。** 提案配置が適用されます。自分で動かしたい場合は、移動の案内を上から順にたどってください。

宝箱・ショップ・セフィライトで何を選ぶか、どの石版を合成し、どこにエンチャントするかもおすすめします。`F1` で、まだ手に入れていない候補を入れたときの配置をプレビューできます。

> [!IMPORTANT]
> スコアは配置を比べるための独自の評価値で、ゲームの公式な数値ではありません。固有効果や好みのプレイスタイルもあわせて考えてください。

ビルドの指定やアイテムごとのルールなどは [詳しい使い方](docs/USAGE.md)（韓国語）にあります。

## ショートカットキー

| キー | 機能 |
| --- | --- |
| `F1` | 候補プレビュー |
| `F2` | ビルド設定 |
| `F3` | 設定 |
| `F4` | パネルを隠す |
| `F5` | パネルの不透明度 |
| `F6` | パネルを移動 |
| `F7` | パネルの折りたたみ・展開 |
| `F8` | 提案配置を適用 |
| `F9` | アイテムデータを再読み込み |
| `F10` | 不具合報告用に状態を保存 |

キーは `F3` で変更できます。ゲームパッドでは、バッグや宝箱などのゲーム画面が開いている間に `View` ボタンでウィンドウを開きます。

## マルチプレイ

SephPlannerは自分の画面にだけ反映されるので、一緒に遊ぶ人はインストールしなくても大丈夫です。

マルチプレイでは自動配置は初期状態でオフになっています。一緒に遊ぶ人の同意を得てから、`F3` の `マルチプレイで自動配置` でオンにしてください。サーバー反映の警告が出たら、ロビーに入り直してから使います。

## よくある質問

**パネルが表示されません。** 探検中か、`F4` で隠していないか、`F3` の `プランナー画面を表示` がオンになっているかを確認してください。ワークショップ版なら、タイトル画面のワークショップウィンドウで有効になっているか、`BepInEx/plugins` にSephPlannerのDLLが残っていないかを見てください。ZIP版なら、DLL 2つが `BepInEx/plugins` にあるかを見てください。

**ゲームのアップデート後、認識がおかしくなりました。** `F9` でアイテムデータを再読み込みしてください。

**計算に時間がかかります。** `F3` の `高性能モード（複数コアで計算）` をオンにすると、CPUコアを多く使って速く計算します。結果は変わりません。ゲームがカクつく場合はオフに戻してください。

**おすすめが表示されません。** `F3` の `獲得・合成・エンチャント・除去のおすすめ` がオンになっているか確認してください。

**ショートカットキーが効きません。** 他のキーを押している間はショートカットキーが効きません。歩きながら押さず、移動キーから手を離してから押してください。`F3` の`ショートカット`タブで、他のキーと重なっていないかも確認してください。`プランナー画面を表示` をオフにすると、`F1`・`F4`～`F8` も止まります。

**それでも解決しません。** 問題が起きている状態で `F10` を押し、画面に表示された**報告番号**を添えて [Issues](https://github.com/nyabi-gh/SephPlanner/issues)、ワークショップのページのコメント、または [nyabi@tb.pro](mailto:nyabi@tb.pro) までお知らせください。保存された診断ファイルにはゲームデータが含まれているため、公開のIssuesには投稿しないでください。（[報告の案内](docs/USAGE.md#제보에-필요한-정보)（韓国語））

**アンインストールしたいです。** ワークショップ版は、ワークショップのページでサブスクライブを解除します。ZIP版は、ゲームを終了し、`BepInEx/plugins` から `SephPlanner.Plugin.dll` と `SephPlanner.Core.dll` を削除します。設定も消す場合は、`%LOCALAPPDATA%\SephPlanner` フォルダと `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg` も削除してください。他のMODを使っている場合、BepInExはそのまま残してください。

## ライセンス

SephPlannerは [MITライセンス](LICENSE) のもとで、自由に使用・改変・配布できます。

開発元のMODポリシーに従い、広告・寄付・サブスクリプションなしの非営利で運営しています。共有するときは、ファイルではなく [リリース](https://github.com/nyabi-gh/SephPlanner/releases/latest) か [ワークショップのページ](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875) のリンクを伝えてください。

## 開発への参加

不具合報告やプルリクエストを歓迎します。ビルドとテストの方法は [開発ドキュメント](docs/DEVELOPMENT.md)（韓国語）にあります。
