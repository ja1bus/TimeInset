# TimeInset

TimeInset は、Windows 11 向けの常駐アプリです。画面に時計・日付・経過時間（カウントアップのタイマー）を重ねて表示し、スクリーンショットや操作説明の動画に、撮影した時刻や作業の経過時間を写し込めます。表示は日本語のみです。

このリポジトリは、一般公開している TimeInset のソースコードです。

## 主な機能

- 時計（`HH:mm:ss`）・日付（`yyyy/MM/dd`）・タイマー（`00:00:00`）を個別に表示。縦並び・横一列と並び順を選べます
- 縦並びでは値を右端で揃え、日付を時刻と同じ幅に自動で合わせます
- 文字の大きさ、文字色（白・グレー・黒・黄・緑・青）、縁取り（色は文字色ごとに固定）、背景色（黒・白・グレー・緑・青）と不透明度、アイコン、最前面
- タイマーの開始・一時停止・リセット。一時停止中は赤字で表示。ショートカットキー（既定は未設定）で開始・一時停止
- 指定した日時から進む時計（起動するたびに現在日時に戻ります）
- 表示はドラッグで移動、右クリックで操作。タスクトレイに常駐し、二重起動しません

タイマーの値は終了すると保存しません。設定はこの PC の中だけに保存し、TimeInset がインターネットへ送信することはありません（詳しくは下記のプライバシーポリシー）。

## 対応 OS と実行環境

- Windows 11（64 ビット、x64）
- Microsoft Store 版は .NET を内包しているため、追加のインストールは不要です。
- このリポジトリのソースからビルドして実行する場合は、[.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0) が必要です。

## インストール

正式な配布先は Microsoft Store です。

- Microsoft Store: https://apps.microsoft.com/detail/9PG52HVDZGMD

この GitHub リポジトリでは実行ファイル（バイナリ）を配布していません。

## 基本的な使い方

1. 起動すると画面の左上に時計と日付が表示され、タスクトレイに常駐します。
2. 表示をドラッグして好きな位置へ動かします。写したくないときは、映らない場所へ動かします。
3. 表示またはトレイアイコンを右クリックすると、時計・日付・タイマーの表示の切り替え、タイマーの開始・一時停止・リセット、最前面、設定、終了を選べます。タイマーの行をダブルクリックしても開始できます。
4. トレイアイコンをクリックすると設定画面が開きます。［表示］タブの変更はすぐに表示へ反映され、［OK］か［適用］で保存、［キャンセル］で元に戻ります。
5. 終了はメニューの［終了］です。

詳しい使い方は取扱説明書をご覧ください。

- 取扱説明書: https://www.ja1bus.com/timeinset/manual/

## ビルド方法

### 必要な環境

- Windows 11 x64
- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)（確認済み: 10.0.402。ルートの `global.json` で 10.0 系の SDK を選びます）
- `dotnet` CLI だけでビルドできます（Visual Studio は不要）。
- 本体は NuGet パッケージに依存しません。テストだけが NuGet の `Microsoft.NET.Test.Sdk`・`xunit`・`xunit.runner.visualstudio` を使います（初回のビルド時に nuget.org から取得します）。

### コマンド

リポジトリのルートで実行します。

```powershell
dotnet build TimeInset.csproj
dotnet test Tests/TimeInset.Tests.csproj
dotnet run --project TimeInset.csproj
```

`dotnet run` で起動するとタスクトレイに常駐します。終了はメニューの［終了］です。設定は `%LOCALAPPDATA%\TimeInset\` に保存されます。

ビルドの出力には `LICENSE` と `THIRD-PARTY-NOTICES.md` も入ります（設定画面の［バージョン情報］から表示します）。

### 構成

| パス | 内容 |
|---|---|
| `TimeInset.csproj` | 本体（WPF、`net10.0-windows`）。タスクトレイだけ WinForms の `NotifyIcon` を使用 |
| `Core/` | 画面・Win32 に依存しないロジック（時計・タイマー・設定と保存・位置計算）と、画面の文字列（`Localization/Strings.resx`、日本語） |
| `Services/`・`Views/` | 単一起動・ショートカット・保存先などの処理と、表示・設定画面 |
| `Tests/` | 単体テスト（xUnit。表示の寸法・画素やショートカットの登録は Windows 上で実行） |
| `icon.png`・`Resources/icon.ico` | アプリアイコン（`pwsh -File tools/make-icon.ps1` で `icon.png` から `icon.ico` を作成） |

Microsoft Store 版のパッケージ（MSIX）を作る仕組みは、このリポジトリに含めていません。

ソースコード中のコメントは日本語と英語が混在しています。

## ライセンス

[MIT License](LICENSE) で公開しています。

Copyright (c) 2026 ja1bus（© ja1bus）

第三者のソフトウェアについては [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

## 無保証

本ソフトウェアは「現状のまま」提供され、明示・黙示を問わず、いかなる保証もありません。本ソフトウェアの使用によって生じたいかなる損害についても、作者は責任を負いません（詳細は [LICENSE](LICENSE)）。

## 関連リンク

- Web ページ: https://www.ja1bus.com/timeinset/
- 取扱説明書: https://www.ja1bus.com/timeinset/manual/
- プライバシーポリシー: https://www.ja1bus.com/timeinset/privacy/
- Microsoft Store: https://apps.microsoft.com/detail/9PG52HVDZGMD
- お問い合わせ: contact@ja1bus.com
