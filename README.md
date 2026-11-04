# HSIS
インストーラー作成アプリ

<img width="600" src="https://github.com/user-attachments/assets/6e9dd7e8-513f-4cfa-87d7-1fb8c2e9608d" />

運用ルールは `AGENTS.md`、変更履歴は `CHANGELOG.md` を参照してください。

`WriteUninstaller` がない定義にも `Uninstall.exe` を自動生成します。削除定義がない場合、配置ファイルとショートカットを削除し、ユーザーが追加したファイルは残します。レジストリなどの削除は `Section Uninstall` で指定してください。取得した対象アプリのアイコンは `.hsis/Application.ico` に配置し、アプリのショートカットから参照します。

Windows アプリ用インストーラーの作成ツールです。HSIS 自身が `.nsi` 定義を解析し、ファイルを圧縮して WPF 標準 Fluent テーマのセットアップを生成します。インストール処理も HSIS が実行します。

## 起動

生成・実行の対象は Windows 11、x64 です。標準の `tar.exe` が 7z／LZMA2 の作成・展開に対応している環境が必要です。生成には .NET 9 SDK が必要です。C++ ビルドツールは不要です。生成時には NuGet に接続します。画面は参考サンプルと同じ .NET 9 の WPF 標準 Fluent テーマ（`ThemeMode="System"`）を使用します。NSIS や 7-Zip のインストールは不要です。

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
# CLI テンプレートに元のアイコンを補います。
Copy-Item src/Hsis.App/Hsis.ico artifacts/cli/Template/src/Hsis.App/Hsis.ico
.\artifacts\cli\hsis.exe build .\samples\HelloApp\installer.nsi
```

## インストール時の注意

- 旧版が Program Files などにインストールされている場合、ユーザー単位の新しいインストールとは保存先やアプリ一覧の登録先が異なり、両方が残ることがあります。移行前に旧版のアンインストール方法と保存データを確認してください。
- インストール後は、設定の「アプリ → インストールされているアプリ」とスタートメニューの両方を確認してください。ユーザー単位の登録はそのユーザーのアカウントに作成されます。
- 古い登録だけが残った場合は、まず旧版のアンインストーラーを使用してください。レジストリを手動で変更する場合は、対象の保存先と登録内容を確認し、必要に応じてバックアップしてください。管理者権限が必要な場合があります。
- 自動起動タスクなど外部コマンドによる処理は HSIS の対応範囲外です。既存のインストーラーから移行する際は、登録と削除の方法を別途確認してください。

## Windows のアプリ一覧に表示する .nsi の例

`Uninstall.exe` の自動生成だけでは、Windows の「設定 → アプリ → インストールされているアプリ」には登録されません。次のように `WriteRegStr` で登録し、`Section "Uninstall"` で登録を解除します。明示的な削除セクションを書く場合は、配置ファイルとショートカットの削除も指定してください。

次を `installer/MyApp.nsi` として保存します。`SOURCE_DIR` には発行済みアプリのフォルダーを指定し、`MyApp.exe` は実際の実行ファイル名に置き換えます。

```nsi
Unicode true
!include "MUI2.nsh"
Name "MyApp"
OutFile "MyApp-${APP_VERSION}-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\MyApp"
RequestExecutionLevel user
VIProductVersion "${APP_VERSION}.0"
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\MyApp.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Japanese"

Section "MyApp"
  SetOutPath "$INSTDIR\app"
  File /r "${SOURCE_DIR}\*"
  CreateShortcut "$SMPROGRAMS\MyApp.lnk" "$INSTDIR\app\MyApp.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "DisplayName" "MyApp"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "DisplayIcon" "$INSTDIR\app\MyApp.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\MyApp.lnk"
  RMDir /r "$INSTDIR\app"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyApp"
SectionEnd
```

```powershell
.\hsis.cmd build .\installer\MyApp.nsi '-DAPP_VERSION=0.1.0' '-DSOURCE_DIR=C:\path\to\publish'
```

この例は実行ユーザー用に `HKCU` へ登録します。空白を含むインストール先でも削除できるよう、アンインストールコマンドの EXE パスを引用符で囲んでいます。`RMDir /r` は指定フォルダー内をすべて削除するため、ユーザーデータは `app` フォルダーの外に保存してください。HSIS 自身の定義は [installer/HSIS.nsi](installer/HSIS.nsi) を参照してください。

## HSIS 自身のインストーラーをダウンロードする

バージョンタグ (`vMAJOR.MINOR.PATCH`) を push すると、HSIS 自身でインストーラーをビルドし、GitHub Release にセットアップ EXE と `.sha256` を公開します。このリポジトリの **Releases** からダウンロードしてください。

手動でビルドする場合は **Actions → Build HSIS installer → Run workflow** から実行します。完了後、実行結果の **Artifacts → HSIS-バージョン-win-x64**、または Summary のダウンロードリンクから ZIP を取得してください。成果物の保存期間は30日です。手動実行では GitHub Release を作成しません。

Releases から取得した `HSIS-バージョン-win-x64-Setup.exe` を起動します。Actions の成果物を取得した場合は、先に ZIP を展開してください。併せて取得した `.sha256` で SHA-256 を確認できます。セットアップのファイル名は変更しないでください。既定ではユーザーの `LocalAppData\Programs\HSIS` に Studio、CLI (`hsis.exe`)、生成に必要な `Template` を配置し、スタートメニューに HSIS を追加します。アンインストールは配置先の `Uninstall.exe` から実行できます。

タグと `Directory.Build.props` のバージョンが一致しない場合は失敗します。バージョンは入力値で上書きせず、同ファイルを使用します。タグ起動でも Actions の成果物に EXE と `.sha256` を保存します。

ローカルでも、前述の生成環境で次を実行できます。

```powershell
./build-installer.ps1
```

Studio と CLI を発行し、その CLI が `installer/HSIS.nsi` を処理して **HSIS で HSIS 自身のインストーラーを生成**します。完成した EXE と SHA-256 は `artifacts/distribution/` に保存します。インストール後の Studio / CLI で別のインストーラーを生成する場合も、.NET 9 SDK、対応する Windows 標準 `tar.exe`、NuGet 接続が必要です。

標準 `tar.exe` が LZMA2 に対応しない環境では、`$env:HSIS_7ZIP_PATH = 'C:\Program Files\7-Zip\7z.exe'` を設定すると、圧縮と展開に 7-Zip を使用します。このリポジトリの workflow もこの設定でビルドします。生成したセットアップのアプリ本体の展開処理は Windows 標準 `tar.exe` を使用するため、配布対象には LZMA2 の展開に対応した Windows が必要です。

## GitHub Actions で使用する

HSIS を使用する例です。アプリと HSIS を別々のフォルダーへチェックアウトし、CLI をビルドして既存の `.nsi` からインストーラーを生成します。NSIS のインストールは不要です。

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
          dotnet-version: |
            8.0.x
            9.0.x
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

`APP_VERSION`、`SOURCE_DIR`、`OUTPUT_FILE` は、この例の `.nsi` が使用するビルド定数です。HSIS 固有の必須定数ではありません。アプリのバージョンは入力値から設定し、使用する HSIS のバージョンは `ref: v*.*.*` で固定します。

CLI の `Template` に `Hsis.ico` が含まれないため、同じタグの元アセットをコピーしています。`hsis.exe` と `Template` は一緒に保持してください。生成物は `artifacts/installer/Setup-日時-識別子/` に保存されます。生成した EXE の名前は変更せず、出力フォルダーごと配布してください。

この例は手動実行で Artifacts に保存します。GitHub Release へ公開する場合は、タグ起動の条件と公開ステップを追加し、そのジョブに `contents: write` を設定してください。

## 開発時のバージョン更新

バージョンはコミットごとに更新します。変更を `git add` でステージしてから、次の1コマンドを実行してください。

```powershell
./version.ps1 -Part patch -Message 'fix: 修正内容' -Summary '変更履歴に記録する内容'
```

`Directory.Build.props` のアプリ内部バージョンと `CHANGELOG.md` を更新し、ステージ済みの変更をコミットして、そのコミットに同じ番号の `vX.Y.Z` タグを付けます。機能追加・機能変更は `-Part minor`、開発者が明示指定した大幅な更新は `-Part major` を使います。方針やドキュメント、開発用スクリプトだけの変更は `patch` です。`-WhatIf` で更新予定を確認できます。push は行いません。

コミット失敗時は更新済みファイルを残すため、原因を解消して同じバージョンのままコミットし、タグを作成してください。タグ作成だけ失敗した場合は表示される復旧コマンドを使い、スクリプトを再実行して重複更新しないでください。

## GUI

定義ファイルを選ぶと表示情報と規約を読み込みます。必要な項目を変更して「インストーラーを作成」を押してください。

セットアップは、ようこそ → 利用規約への同意 → インストール先 → インストール → 完了の順に進みます。規約が設定されている場合は同意が必須です。管理者指定は生成物の実行マニフェストに反映します。

セットアップ上部にはインストール対象のアプリのアイコンを表示します。完了後の起動ファイル、ショートカットや DisplayIcon の対象、アプリ名に一致する EXE の順に選び、生成時に取得して同梱します。EXE にアイコンがない場合は、同梱する同名の ICO / PNG、定義付近の同名 C# プロジェクトの `ApplicationIcon` を参照します。Windows の汎用 EXE アイコンは使いません。複数の EXE から判別できない場合は、完了後の起動ファイルを指定してください。取得できない場合はセットアップの標準アイコンを表示します。

出力先の `Setup-日時-識別子` フォルダーに実行ファイルを生成します。ファイル名は定義の `OutFile` から取得します。`OutFile` のディレクトリ部分は使わず、出力先は GUI / CLI の指定に従います。ランタイムを同梱するため、インストール先では SDK は不要です。付随ファイルがある場合は出力フォルダーごと配布してください。

Studio、セットアップ、アンインストーラーは WPF の標準 Fluent テーマを使用し、Windows のライト／ダーク設定に追従します。WinUI / Windows App SDK は使用しません。

セットアップは .NET を同梱した単一 EXE として発行し、以前のネイティブ準備画面と、起動前に共通ランタイム全体を 7z から展開する処理を廃止しました。起動するとセットアップ画面を直接開きます。ただし単一 EXE の仕様上、初回起動時などには .NET が必要なネイティブ DLL を内部キャッシュへ展開します。初期ディスク書き込みが完全になくなるわけではありません。

インストール対象のファイルは 7z／LZMA2・圧縮レベル9で圧縮し、セットアップ EXE 末尾に格納します。インストール開始までは展開しません。配置中には進行表示と待ち時間の案内を表示します。

セットアップとアンインストーラーは同じ .NET 同梱単一 EXE を共用し、配布ファイル内には一度だけ格納します。`HSISPKG3` 形式では配置時にその EXE からアプリ本体のデータを除き、削除モードを付けてアンインストーラーを生成します。削除用定義とアイコンは共通 EXE 内に含めるため、アンインストーラーはセットアップを残さなくても単独で動作します。起動時に自分自身を一時フォルダーへコピーし、元のプロセスの終了を待ってから削除画面を開くため、インストール先の EXE も削除できます。`/S` による無人削除も利用できます。一時コピーと .NET のキャッシュは残る場合があります。旧 `HSISPKG1` / `HSISPKG2` の読み取りも維持します。

セットアップとアンインストーラーには対象アプリのアイコンを設定し、取得できない場合は HSIS のアイコンを使用します。旧 ZIP、`HSIS7Z01` / `HSIS7Z02`、ネイティブ起動形式の読み取りは維持します。新規生成では起動前の共有ランタイム展開・共通ファイルへの重複排除を使用しません。

Studio / CLI の `Template` フォルダーは生成時に必要です。実行ファイルと一緒に保持してください。

## 定義の対応範囲

既存の `.nsi` を読み込めますが、自前コンパイラーの対応は以下の範囲です。**完全な構文互換は未実装です。** 未対応の命令はファイル名・行番号付きでエラーにし、黙って省略しません。

- UTF-8、コメント、引用符、行継続
- `Name`、`OutFile`、`InstallDir`、`LicenseData`、`Unicode true`
- `RequestExecutionLevel user` / `admin`、`InstallDirRegKey`
- `VIProductVersion`、`VIAddVersionKey`（ProductName / FileDescription / FileVersion / LegalCopyright）
- `SetCompressor /SOLID lzma` は HSIS の 7z／LZMA2・圧縮レベル9に対応付けます。指定がない場合も同じ方式を使います。
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

WPF 画面の検証（保存先チェック・利用規約の同意・削除モード、描画画像は `artifacts/wpf-checks`）:

```powershell
dotnet build tests/Hsis.WpfChecks -c Release -m:1
./tests/Hsis.WpfChecks/bin/Release/net9.0-windows/win-x64/Hsis.WpfChecks.exe
```

workflow では `--ci` を付け、ウィンドウを表示せずに画面遷移・同意・保存先・中止確認・削除モードをライト／ダーク両方で検証します。画面撮影と実際のキーボードフォーカスの確認はローカル実行時に行います。

```powershell
./tests/Hsis.WpfChecks/bin/Release/net9.0-windows/win-x64/Hsis.WpfChecks.exe --ci
./tests/Hsis.WpfChecks/bin/Release/net9.0-windows/win-x64/Hsis.WpfChecks.exe --ci --dark
```

単一 EXE の初回起動確認:

```powershell
./tests/Check-SetupStartup.ps1 -SetupPath <生成したSetup.exe> -ExpectedTitle 'Hello App セットアップ' -ColdStart
./tests/Check-UninstallerStartup.ps1 -UninstallerPath <生成したUninstall.exe>
```

`Check-PreparedStartup.ps1` は旧ネイティブ形式の準備画面を確認する互換検証専用です。
