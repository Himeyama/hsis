# HSIS

現在のバージョン: **0.4.4**。運用ルールは `AGENTS.md`、変更履歴は `CHANGELOG.md` を参照してください。

`WriteUninstaller` がない定義にも `Uninstall.exe` を自動生成します。削除定義がない場合、配置ファイルとショートカットを削除し、ユーザーが追加したファイルは残します。レジストリなどの削除は `Section Uninstall` で指定してください。取得した対象アプリのアイコンは `.hsis/Application.ico` に配置し、アプリのショートカットから参照します。

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

起動用ビルドでは NuGet のオンライン脆弱性監査を省きます。パッケージの初回取得や更新には引き続き接続が必要です。監査する場合は、NuGet に接続できる環境で次を実行してください。通常の `dotnet build` / `dotnet restore` でも監査は有効です。

```powershell
dotnet restore src/Hsis.App/Hsis.App.csproj -p:Platform=x64 -p:NuGetAudit=true --force
```

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
# v0.4.4 の CLI テンプレートに元のアイコンを補います。
Copy-Item src/Hsis.App/Hsis.ico artifacts/cli/Template/src/Hsis.App/Hsis.ico
.\artifacts\cli\hsis.exe build .\samples\HelloApp\installer.nsi
```

## GitHub Actions で使用する

VideoEditor で確認した、HSIS **v0.4.4** を使用する例です。アプリと HSIS を別々のフォルダーへチェックアウトし、CLI をビルドして既存の `.nsi` からインストーラーを生成します。NSIS のインストールは不要です。

次の例を対象アプリの `.github/workflows/installer.yml` に保存します。アプリ側に `build.ps1` と `installer/VideoEditor.nsi` があることを前提としています。別のアプリでは、アプリの発行コマンド、定義ファイル名、出力ファイル名を変更してください。`build.ps1` は VideoEditor の発行スクリプトで、HSIS に含まれるものではありません。

```yaml
name: Build installer with HSIS

on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Application version (MAJOR.MINOR.PATCH)'
        required: true
        default: '0.5.1'

permissions:
  contents: read

jobs:
  build:
    runs-on: windows-2022
    defaults:
      run:
        shell: pwsh
        working-directory: app
    steps:
      - uses: actions/checkout@v4
        with:
          path: app
      - name: Check out HSIS v0.4.4
        uses: actions/checkout@v4
        with:
          repository: Himeyama/hsis
          ref: v0.4.4
          path: hsis
          persist-credentials: false
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Validate application version
        env:
          APP_VERSION: ${{ inputs.version }}
        run: |
          if ($env:APP_VERSION -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Expected MAJOR.MINOR.PATCH' }
          if (($env:APP_VERSION.Split('.') | Where-Object { [long]$_ -gt 65535 }).Count) { throw 'Version components must be at most 65535' }
      - name: Publish application
        env:
          APP_VERSION: ${{ inputs.version }}
        run: |
          ./build.ps1 -Configuration Release -Publish -Version $env:APP_VERSION -OutputDirectory "$env:RUNNER_TEMP/payload" -SkipLaunch
          if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }
      - name: Build HSIS CLI
        run: |
          $toolPath = Join-Path $env:RUNNER_TEMP 'hsis-v0.4.4'
          dotnet publish ../hsis/src/Hsis.Cli/Hsis.Cli.csproj -c Release -r win-x64 --self-contained true -o $toolPath
          if ($LASTEXITCODE -ne 0) { throw 'HSIS build failed' }
          # v0.4.4 omits the icon from the CLI template; copy the original asset.
          Copy-Item ../hsis/src/Hsis.App/Hsis.ico "$toolPath/Template/src/Hsis.App/Hsis.ico"
          "HSIS_PATH=$(Join-Path $toolPath 'hsis.exe')" >> $env:GITHUB_ENV
      - name: Build installer and checksum
        env:
          APP_VERSION: ${{ inputs.version }}
        run: |
          & $env:HSIS_PATH build ./installer/VideoEditor.nsi --output ./artifacts/installer "-DAPP_VERSION=$env:APP_VERSION" "-DSOURCE_DIR=$env:RUNNER_TEMP/payload" "-DOUTPUT_FILE=VideoEditor-$env:APP_VERSION-win-x64-Setup.exe"
          if ($LASTEXITCODE -ne 0) { throw 'HSIS failed' }
          $installers = @(Get-ChildItem ./artifacts/installer/Setup-*/*-Setup.exe)
          if ($installers.Count -ne 1) { throw 'Expected one installer' }
          $installer = $installers[0]
          $hash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
          "$hash  $($installer.Name)" | Set-Content -LiteralPath "$($installer.FullName).sha256" -Encoding ascii
      - uses: actions/upload-artifact@v4
        with:
          name: VideoEditor-${{ inputs.version }}-win-x64
          path: app/artifacts/installer/
          if-no-files-found: error
```

`APP_VERSION`、`SOURCE_DIR`、`OUTPUT_FILE` は、この例の `.nsi` が使用するビルド定数です。HSIS 固有の必須定数ではありません。アプリのバージョンは入力値から設定し、使用する HSIS のバージョンは `ref: v0.4.4` で固定します。

v0.4.4 では CLI の `Template` に `Hsis.ico` が含まれないため、同じタグの元アセットをコピーしています。`hsis.exe` と `Template` は一緒に保持してください。生成物は `artifacts/installer/Setup-日時-識別子/` に保存されます。生成した EXE の名前は変更せず、出力フォルダーごと配布してください。

この例は手動実行で Artifacts に保存します。GitHub Release へ公開する場合は、タグ起動の条件と公開ステップを追加し、そのジョブに `contents: write` を設定してください。

## GUI

定義ファイルを選ぶと表示情報と規約を読み込みます。必要な項目を変更して「インストーラーを作成」を押してください。

セットアップは、ようこそ → 利用規約への同意 → インストール先 → インストール → 完了の順に進みます。規約が設定されている場合は同意が必須です。管理者指定は生成物の実行マニフェストに反映します。

セットアップ上部にはインストール対象のアプリのアイコンを表示します。完了後の起動ファイル、ショートカットや DisplayIcon の対象、アプリ名に一致する EXE の順に選び、生成時に取得して同梱します。EXE にアイコンがない場合は、同梱する同名の ICO / PNG、定義付近の同名 C# プロジェクトの `ApplicationIcon` を参照します。Windows の汎用 EXE アイコンは使いません。複数の EXE から判別できない場合は、完了後の起動ファイルを指定してください。取得できない場合はセットアップの標準アイコンを表示します。

出力先の `Setup-日時-識別子` フォルダーに実行ファイルを生成します。ファイル名は定義の `OutFile` から取得します。`OutFile` のディレクトリ部分は使わず、出力先は GUI / CLI の指定に従います。ランタイムを同梱するため、インストール先では SDK は不要です。付随ファイルがある場合は出力フォルダーごと配布してください。

アプリ本体の圧縮データとアンインストーラーは EXE の末尾に同梱し、画面起動時の .NET 一時展開から外しています。インストールを開始した時点で必要なデータだけを読み取ります。アンインストーラーにはインストール用のアプリ本体を重複して含めません。

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
- `MUI_FINISHPAGE_RUN` を完了後の起動ファイルとして読み込みます。キャンセルや × ボタンで閉じる場合は確認ダイアログを表示します。`MUI_ABORTWARNING` の指定も受理します。`MUI_LANGUAGE English` / `Japanese` の指定を受理し、現在の画面は日本語で表示します。

上記以外のマクロ・関数・ジャンプ・変数操作・プラグイン・独自ページ・任意プロセス実行は未対応です。自動ロールバックも未実装です。アンインストーラーは指定された名前で別途ビルドしてセットアップに同梱し、実行時には同じファイル名で一時コピーして起動します。`/S` はアンインストールの無人実行に対応します。一時コピーは一時フォルダーに残ります。

生成された EXE の名前は変更しないでください。変更する場合は `OutFile` またはビルド定数を変更して生成し直してください。

既定の保存先では `$LOCALAPPDATA`、`$APPDATA`、`$PROGRAMFILES`、`$PROGRAMFILES64` を実行ユーザーの環境で展開します。完了後に起動するファイルはインストール先からの相対パスを指定します。

## 検証

```powershell
dotnet run --project tests/Hsis.Checks -- .
```

解析、インクルード、条件分岐、再帰取り込み、名前変更、圧縮・配置、日本語の保存先、未対応命令と範囲外パスの拒否を確認します。配置先は `artifacts/native-check` 内です。
