# HSIS

現在のバージョン: **0.1.0**。運用ルールは `AGENTS.md`、変更履歴は `CHANGELOG.md` を参照してください。

Windows アプリ用インストーラーの作成ツールです。HSIS 自身が `.nsi` 定義を解析し、ファイルを圧縮して WinUI 3 のセットアップを生成します。インストール処理も HSIS が実行します。

## 起動

Windows 10 version 2004 以降 / Windows 11、x64、.NET 8 以降の SDK が必要です。生成時には NuGet に接続します。外部のインストーラーコンパイラーは不要です。

```powershell
# 毎回ビルドして Studio を起動
.\run.ps1

# ビルドのみ
.\run.ps1 -NoLaunch

# 生成済みサンプルを起動
.\run.ps1 sample
```

`run.cmd` も同じ処理を呼び出します。以前の `-Build` は引き続き指定できますが、指定の有無にかかわらず Studio をビルドします。起動中の Studio は先に閉じてください。

## CLI

```powershell
.\hsis.cmd build .\samples\HelloApp\installer.nsi
.\hsis.cmd --help
```

ビルド時の定数は `-D名前=値` で渡します。GUI では「ビルド定数」に `名前=値` を1行ずつ入力して「定義を読み込む」を押します。

```powershell
.\hsis.cmd build C:\Users\hikari\sources\VideoEditor\installer\VideoEditor.nsi '-DAPP_VERSION=0.1.0' '-DSOURCE_DIR=C:\Users\hikari\sources\VideoEditor\artifacts\publish-startup-final' '-DOUTPUT_FILE=VideoEditor-Setup.exe'
```

`Name`、`InstallDir`、`LicenseData` / `MUI_PAGE_LICENSE`、`RequestExecutionLevel` を定義から読み込みます。オプションで上書きできます。

```powershell
.\hsis.cmd build .\samples\HelloApp\installer.nsi --name "Hello App" --install-dir '$LOCALAPPDATA\Programs\Hello App' --output .\artifacts\checks
```

`--license`、`--version`、`--launch`、`--admin` も指定できます。生成後に `SETUP=生成先` を表示します。終了コードは成功 `0`、生成失敗 `1`、引数エラー `2`、キャンセル `130`。Ctrl+C で生成を中止します。セットアップは自動起動しません。

独立した CLI の発行:

```powershell
dotnet publish src/Hsis.Cli/Hsis.Cli.csproj -c Release -r win-x64 --self-contained true -o artifacts/cli
.\artifacts\cli\hsis.exe build .\samples\HelloApp\installer.nsi
```

## GUI

定義ファイルを選ぶと表示情報と規約を読み込みます。必要な項目を変更して「インストーラーを作成」を押してください。

セットアップは、ようこそ → 利用規約への同意 → インストール先 → インストール → 完了の順に進みます。規約が設定されている場合は同意が必須です。管理者指定は生成物の実行マニフェストに反映します。

出力先の `Setup-日時-識別子` フォルダーに実行ファイルを生成します。ファイル名は定義の `OutFile` から取得します。`OutFile` のディレクトリ部分は使わず、出力先は GUI / CLI の指定に従います。ランタイムを同梱するため、インストール先では SDK は不要です。付随ファイルがある場合は出力フォルダーごと配布してください。

Studio / CLI の `Template` フォルダーは生成時に必要です。実行ファイルと一緒に保持してください。

## 定義の対応範囲

既存の `.nsi` を読み込めますが、自前コンパイラーの対応は以下の範囲です。**完全な構文互換は未実装です。** 未対応の命令はファイル名・行番号付きでエラーにし、黙って省略しません。

- UTF-8、コメント、引用符、行継続
- `Name`、`OutFile`、`InstallDir`、`LicenseData`、`Unicode true`
- `RequestExecutionLevel user` / `admin`、`InstallDirRegKey`
- `VIProductVersion`、`VIAddVersionKey`（ProductName / FileDescription / FileVersion / LegalCopyright）
- `SetCompressor /SOLID lzma` は HSIS の ZIP 最適圧縮に対応付けます。LZMA 形式の生成ではありません。
- 通常の必須 `Section` / `SectionEnd`
- `SetOutPath`、`CreateDirectory`（`$INSTDIR` 以下）
- `File`、ワイルドカード、`/r`、単一ファイルの `/oname=`
- `SetShellVarContext current`、`SetOverwrite on`
- `WriteUninstaller`、`Section Uninstall`、`Delete`、`RMDir [/r]`
- `CreateShortcut`（リンク先と対象の2引数）、`$SMPROGRAMS` 以下のフォルダー作成・削除
- `WriteRegStr`、`WriteRegDWORD`（10進数）、`DeleteRegKey`（HKCU / HKLM）
- ローカルの `!include`、単純な `!define` / `!undef`、`${定数}`、`${__FILEDIR__}`、`${__FILE__}`、`!ifdef` / `!ifndef` / `!else` / `!endif`
- `Page license` / `directory` / `instfiles`
- `MUI2.nsh` の参照と標準の `MUI_PAGE_WELCOME` / `LICENSE` / `DIRECTORY` / `INSTFILES` / `FINISH`、`MUI_UNPAGE_CONFIRM` / `INSTFILES` を HSIS の標準画面に対応付けます。
- `LogicLib.nsh` / `x64.nsh` / `WinVer.nsh` は HSIS 内蔵の互換機能で扱うため、ヘッダーファイルは不要です。`.onInit` 内の `${If}` / `${IfNot}` / `${Else}` / `${EndIf}`、`${RunningX64}`、`${AtLeastBuild}`、`MessageBox MB_ICONSTOP` と `Abort` に対応します。
- `MUI_FINISHPAGE_RUN` を完了後の起動ファイルとして読み込みます。`MUI_ABORTWARNING` の指定は受理しますが確認ダイアログは未実装です。`MUI_LANGUAGE English` / `Japanese` の指定を受理し、現在の画面は日本語で表示します。

上記以外のマクロ・関数・ジャンプ・変数操作・プラグイン・独自ページ・任意プロセス実行は未対応です。自動ロールバックも未実装です。アンインストーラーは同じ WinUI ランタイムを一時コピーして起動し、元のアンインストーラー自身も削除します。`/S` はアンインストールの無人実行に対応します。一時コピーは一時フォルダーに残ります。

既定の保存先では `$LOCALAPPDATA`、`$APPDATA`、`$PROGRAMFILES`、`$PROGRAMFILES64` を実行ユーザーの環境で展開します。完了後に起動するファイルはインストール先からの相対パスを指定します。

## 検証

```powershell
dotnet run --project tests/Hsis.Checks -- .
```

解析、インクルード、条件分岐、再帰取り込み、名前変更、圧縮・配置、日本語の保存先、未対応命令と範囲外パスの拒否を確認します。配置先は `artifacts/native-check` 内です。
