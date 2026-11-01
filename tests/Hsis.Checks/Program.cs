using Hsis.Core;
using System.Text;
using System.Reflection;
using System.Collections.Concurrent;

if (args is ["--emit-unicode-log"])
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.WriteLine("標準出力：日本語のビルドログ ✓");
    Console.Error.WriteLine("標準エラー：日本語の診断ログ ✓");
    return;
}
if (args is ["--extract-uninstaller", string setupExecutable, string destinationPath])
{
    using Stream resource = SetupArchive.OpenUninstaller(setupExecutable) ?? throw new InvalidDataException("専用アンインストーラーがありません。");
    await using FileStream destinationFile = File.Create(destinationPath);
    await resource.CopyToAsync(destinationFile);
    return;
}
if (args is ["--exercise-generated-setup", string generatedSetup])
{
    string installRoot = Path.Combine(Path.GetFullPath("."), "artifacts", "sevenzip-install-check", Guid.NewGuid().ToString("N"));
    using (Stream data = SetupArchive.OpenPayload(generatedSetup))
    using (Stream? uninstaller = SetupArchive.OpenUninstaller(generatedSetup))
        await InstallationEngine.InstallAsync(data, installRoot, new InlineProgress(Console.WriteLine), uninstaller: uninstaller);
    Check(File.Exists(Path.Combine(installRoot, "hello.txt")), "生成済み 7z EXE からサンプルを配置");
    System.Diagnostics.ProcessStartInfo start = new(Path.Combine(installRoot, "Uninstall.exe")) { UseShellExecute = false, WorkingDirectory = installRoot };
    start.ArgumentList.Add("/S");
    using System.Diagnostics.Process launcher = System.Diagnostics.Process.Start(start)!;
    await launcher.WaitForExitAsync();
    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
    while (Directory.Exists(installRoot) && watch.Elapsed.TotalSeconds < 60) await Task.Delay(100);
    Check(!Directory.Exists(installRoot), "ネイティブ起動から無人アンインストールし自身も削除");
    return;
}
if (args is ["--extract-icon", string executablePath, string scriptPath, string iconOutput])
{
    PackageDefinition definition = new() { Name = Path.GetFileNameWithoutExtension(executablePath), LaunchExecutable = Path.GetFileName(executablePath) };
    InstallPlan plan = new(definition, "Setup.exe", [new(executablePath, definition.LaunchExecutable)], []);
    if (!ApplicationIcon.TryWrite(plan, definition, iconOutput, Console.WriteLine, scriptPath)) throw new Exception("アプリアイコンを取得できません。");
    return;
}

string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string sandbox = Path.Combine(root, "artifacts", "native-check", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding originalEncoding = Console.OutputEncoding;
ConcurrentQueue<string> childLog = new();
try
{
    Console.OutputEncoding = Encoding.GetEncoding(932);
    await ProcessRunner.RunAsync("dotnet", [Assembly.GetExecutingAssembly().Location, "--emit-unicode-log"], sandbox, childLog.Enqueue);
}
finally { Console.OutputEncoding = originalEncoding; }
Check(childLog.Contains("標準出力：日本語のビルドログ ✓") && childLog.Contains("標準エラー：日本語の診断ログ ✓"), "親プロセスが Shift-JIS でも子プロセスの UTF-8 ログを正しく読み取る");
DefinitionCompiler compiler = new();
InstallPlan sample = compiler.Compile(Path.Combine(root, "samples", "HelloApp", "installer.nsi"));
Check(sample.Definition.Name == "Hello App" && sample.Definition.License.Contains("サンプル利用規約"), "定義から表示情報・規約を読み取る");
Check(sample.Files.Count == 2 && !sample.Definition.RequiresAdministrator, "File と権限を読み取る");
InstallPlan automatic = PackageBuilder.PreparePlan(sample, sandbox);
Check(automatic.Definition.Uninstaller == "Uninstall.exe" && automatic.Install.Any(operation => operation.Command == "writeuninstaller"), "指定がなくてもアンインストーラーを生成する");
string automaticPayload = Path.Combine(sandbox, "Automatic.zip");
await PackageBuilder.CreatePayloadAsync(automatic, automaticPayload, Console.WriteLine);
string automaticRoot = Path.Combine(sandbox, "automatic-install");
using (MemoryStream uninstaller = new("test uninstaller"u8.ToArray()))
await using (FileStream source = File.OpenRead(automaticPayload))
    await InstallationEngine.InstallAsync(source, automaticRoot, uninstaller: uninstaller);
await File.WriteAllTextAsync(Path.Combine(automaticRoot, "user-data.txt"), "keep");
await using (FileStream source = File.OpenRead(automaticPayload))
    await InstallationEngine.UninstallAsync(source, automaticRoot);
Check(!File.Exists(Path.Combine(automaticRoot, "hello.txt")) && !File.Exists(Path.Combine(automaticRoot, "Uninstall.exe"))
    && File.Exists(Path.Combine(automaticRoot, "user-data.txt")), "自動アンインストールは配置ファイルを削除しユーザーデータを残す");
string payload = Path.Combine(sandbox, "Payload.zip");
await PackageBuilder.CreatePayloadAsync(sample, payload, Console.WriteLine);
byte[] payloadHeader = new byte[8];
await using (FileStream source = File.OpenRead(payload)) await source.ReadExactlyAsync(payloadHeader);
Check(payloadHeader.AsSpan().SequenceEqual("HSIS7Z01"u8), "新規パッケージは 7z / LZMA2 用の形式");
List<string> extractionMessages = [];
await using (FileStream source = File.OpenRead(payload))
    await InstallationEngine.InstallAsync(source, Path.Combine(sandbox, "sevenzip-progress"), new InlineProgress(extractionMessages.Add));
Check(extractionMessages.Any(message => message.Contains("展開しています")), "展開待ちの案内を通知");
string legacyPayload = Path.Combine(sandbox, "Legacy.zip");
using (System.IO.Compression.ZipArchive legacy = new(File.Create(legacyPayload), System.IO.Compression.ZipArchiveMode.Create))
{
    using StreamWriter manifestWriter = new(legacy.CreateEntry("manifest.json").Open());
    manifestWriter.Write(System.Text.Json.JsonSerializer.Serialize(new PayloadManifest([], []) { Uninstall = [] }));
}
await using (FileStream source = File.OpenRead(legacyPayload))
    await InstallationEngine.InstallAsync(source, Path.Combine(sandbox, "legacy-install"));
Check(true, "従来の ZIP パッケージも読み込める");
string nativeOverlay = Path.Combine(sandbox, "NativeOverlay.exe");
string runtimeArchive = Path.Combine(sandbox, "Runtime.7z");
await File.WriteAllBytesAsync(nativeOverlay, "native prefix"u8.ToArray());
await File.WriteAllBytesAsync(runtimeArchive, "shared runtime"u8.ToArray());
await SetupArchive.AppendNativeAsync(nativeOverlay, runtimeArchive, payload, 1);
await using (Stream generated = SetupArchive.OpenUninstaller(nativeOverlay)!)
{
    string generatedPath = Path.Combine(sandbox, "NativeUninstall.exe");
    await using (FileStream target = File.Create(generatedPath)) await generated.CopyToAsync(target);
    byte[] generatedBytes = await File.ReadAllBytesAsync(generatedPath);
    Check(generatedBytes.AsSpan(0, 27).SequenceEqual("native prefixshared runtime"u8)
        && generatedBytes.Length == 27 + 64 && SetupArchive.OpenUninstaller(generatedPath) is null,
        "削除 EXE は共有ランタイムから生成しアプリ本体を含めない");
}
if (OperatingSystem.IsWindows())
{
    string busyRoot = Path.Combine(sandbox, "busy-install");
    Directory.CreateDirectory(busyRoot);
    string busyExecutable = Path.Combine(busyRoot, "BusyApp.exe");
    File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), busyExecutable);
    string sentinel = Path.Combine(busyRoot, "keep.txt");
    await File.WriteAllTextAsync(sentinel, "unchanged");
    string existingFile = Path.Combine(busyRoot, "hello.txt");
    await File.WriteAllTextAsync(existingFile, "original");
    System.Diagnostics.ProcessStartInfo start = new(busyExecutable)
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
    };
    using System.Diagnostics.Process busy = System.Diagnostics.Process.Start(start)!;
    try
    {
        Check(!busy.WaitForExit(300), "起動中アプリのテストプロセスを維持");
        foreach (bool uninstall in new[] { false, true })
        {
            bool blocked = false;
            await using FileStream data = File.OpenRead(automaticPayload);
            try
            {
                if (uninstall) await InstallationEngine.UninstallAsync(data, busyRoot);
                else await InstallationEngine.InstallAsync(data, busyRoot);
            }
            catch (ApplicationsInUseException ex)
            {
                blocked = ex.Message.Contains("BusyApp.exe") && ex.Message.Contains("終了してから");
            }
            Check(blocked && File.Exists(sentinel) && await File.ReadAllTextAsync(existingFile) == "original",
                uninstall ? "起動中は削除前に終了を案内" : "起動中は配置前に終了を案内");
        }
    }
    finally { if (!busy.HasExited) { busy.Kill(); busy.WaitForExit(); } }
    RunningApplications.EnsureClosed(busyRoot);
    Check(File.Exists(sentinel), "終了後は再実行できる");
}
string overlayExecutable = Path.Combine(sandbox, "OverlaySetup.exe");
string uninstallData = Path.Combine(sandbox, "UninstallerData.exe");
byte[] hostPrefix = "HSIS test executable prefix"u8.ToArray();
byte[] uninstallBytes = "HSIS independent uninstaller bytes"u8.ToArray();
await File.WriteAllBytesAsync(overlayExecutable, hostPrefix);
await File.WriteAllBytesAsync(uninstallData, uninstallBytes);
await SetupArchive.AppendAsync(overlayExecutable, payload, uninstallData);
await using (Stream embeddedUninstaller = SetupArchive.OpenUninstaller(overlayExecutable)!)
{
    using MemoryStream buffer = new();
    await embeddedUninstaller.CopyToAsync(buffer);
    Check(buffer.ToArray().SequenceEqual(uninstallBytes) && embeddedUninstaller.ReadByte() == -1, "EXE 末尾のアンインストーラーを他データと混在させず読み取る");
    embeddedUninstaller.Seek(-1, SeekOrigin.End);
    Check(embeddedUninstaller.ReadByte() == uninstallBytes[^1] && embeddedUninstaller.ReadByte() == -1, "末尾データを範囲内でシークできる");
}
string overlayDestination = Path.Combine(sandbox, "overlay-install");
await using (Stream embeddedPayload = SetupArchive.OpenPayload(overlayExecutable))
    await InstallationEngine.InstallAsync(embeddedPayload, overlayDestination);
Check(File.Exists(Path.Combine(overlayDestination, "hello.txt")), "EXE の末尾から直接インストールデータを配置できる");
Check((await File.ReadAllBytesAsync(overlayExecutable)).AsSpan(0, hostPrefix.Length).SequenceEqual(hostPrefix), "EXE 本体のバイト列を保持");
string noUninstaller = Path.Combine(sandbox, "NoUninstaller.exe");
await File.WriteAllBytesAsync(noUninstaller, hostPrefix);
await SetupArchive.AppendAsync(noUninstaller, payload, null);
Check(SetupArchive.OpenUninstaller(noUninstaller) is null, "アンインストーラーなしのセットアップにも対応");
string brokenOverlay = Path.Combine(sandbox, "BrokenOverlay.exe");
File.Copy(overlayExecutable, brokenOverlay);
using (FileStream file = new(brokenOverlay, FileMode.Open, FileAccess.Write))
using (BinaryWriter writer = new(file)) { file.Position = file.Length - 32; writer.Write(-1L); }
ExpectInvalid(() => SetupArchive.OpenPayload(brokenOverlay), "データ範囲", "不正な末尾データ範囲を拒否");
string destination = Path.Combine(sandbox, "日本語 App");
await using (FileStream source = File.OpenRead(payload)) await InstallationEngine.InstallAsync(source, destination);
Check(await File.ReadAllTextAsync(Path.Combine(destination, "hello.txt")) == await File.ReadAllTextAsync(Path.Combine(root, "samples", "HelloApp", "hello.txt")), "日本語・空白を含む配置先へのコピー");

Directory.CreateDirectory(Path.Combine(sandbox, "assets", "child"));
await File.WriteAllTextAsync(Path.Combine(sandbox, "assets", "child", "file.txt"), "recursive");
await File.WriteAllTextAsync(Path.Combine(sandbox, "shared.nsh"), "!define APP_NAME \"Nested App\"\n");
string script = Path.Combine(sandbox, "recursive.nsi");
await File.WriteAllTextAsync(script, """
    !include "shared.nsh"
    !ifdef APP_NAME
    Name "${APP_NAME}"
    !else
    UnsupportedCommand
    !endif
    RequestExecutionLevel admin
    Section
      SetOutPath "$INSTDIR\data"
      File /r "assets\*"
      File /oname=renamed.txt "assets\child\file.txt"
      CreateDirectory "$INSTDIR\empty"
    SectionEnd
    """);
InstallPlan recursive = compiler.Compile(script);
Check(recursive.Definition.Name == "Nested App" && recursive.Definition.RequiresAdministrator, "インクルード・定数・条件分岐・管理者指定");
Check(recursive.Files.Count == 2 && recursive.Files[0].Destination == @"data\child\file.txt", "再帰取り込み");
string recursivePayload = Path.Combine(sandbox, "recursive.zip");
await PackageBuilder.CreatePayloadAsync(recursive, recursivePayload, Console.WriteLine);
await using (FileStream source = File.OpenRead(recursivePayload)) await InstallationEngine.InstallAsync(source, destination);
Check(File.Exists(Path.Combine(destination, "data", "renamed.txt")) && Directory.Exists(Path.Combine(destination, "empty")), "名前変更とフォルダー作成");

string literalDirectory = Path.Combine(sandbox, "assets", "artifacts", "installer-startup-fix", "extracted", "$PLUGINSDIR");
Directory.CreateDirectory(literalDirectory);
await File.WriteAllTextAsync(Path.Combine(literalDirectory, "modern-wizard.bmp"), "literal dollar directory");
await File.WriteAllTextAsync(Path.Combine(sandbox, "assets", "$name.txt"), "literal dollar file");
InstallPlan literalFiles = compiler.Compile(script);
Check(literalFiles.Files.Any(file => file.Destination == @"data\artifacts\installer-startup-fix\extracted\$PLUGINSDIR\modern-wizard.bmp"), "再帰取り込み元の $PLUGINSDIR は実在するフォルダー名として保持");
string literalPayload = Path.Combine(sandbox, "literal.zip");
await PackageBuilder.CreatePayloadAsync(literalFiles, literalPayload, Console.WriteLine);
await using (FileStream source = File.OpenRead(literalPayload)) await InstallationEngine.InstallAsync(source, destination);
Check(await File.ReadAllTextAsync(Path.Combine(destination, "data", "artifacts", "installer-startup-fix", "extracted", "$PLUGINSDIR", "modern-wizard.bmp")) == "literal dollar directory"
    && await File.ReadAllTextAsync(Path.Combine(destination, "data", "$name.txt")) == "literal dollar file", "$ を含むフォルダー・ファイルをそのまま配置");
await File.WriteAllTextAsync(script, "Section\nSetOutPath \"$INSTDIR\\$PLUGINSDIR\"\nSectionEnd");
ExpectInvalid(() => compiler.Compile(script), "無効な配置先", "定義内の未対応変数は引き続き拒否");

await File.WriteAllTextAsync(script, "Name \"App\"\nSection\nExecWait something.exe\nSectionEnd");
ExpectInvalid(() => compiler.Compile(script), "(3):", "未対応命令を行番号付きで拒否");
await File.WriteAllTextAsync(script, "Section\nSetOutPath \"$INSTDIR\\..\\outside\"\nSectionEnd");
ExpectInvalid(() => compiler.Compile(script), "無効な配置先", "範囲外の配置を拒否");
await File.WriteAllTextAsync(script, "!include \"recursive.nsi\"");
ExpectInvalid(() => compiler.Compile(script), "循環", "循環インクルードを拒否");
Console.WriteLine("PASS: HSIS の定義解析・圧縮・インストール実行（外部コンパイラー不使用）");

string videoScript = args.Length > 1 ? args[1] : Path.Combine(root, "tests", "Fixtures", "VideoEditor.nsi");
if (File.Exists(videoScript))
{
    foreach (string newline in new[] { "\r", "\n", "\r\n" })
    {
        string input = string.Join(newline, "APP_VERSION=0.1.0", "OUTPUT_FILE=", "SOURCE_DIR=");
        Dictionary<string, string> parsedInput = DefinitionConstants.ParseValues(input);
        DefinitionInspection partial = compiler.Inspect(videoScript, parsedInput);
        Check(parsedInput["APP_VERSION"] == "0.1.0" && partial.Definition.ProductVersion == "0.1.0.0" && partial.MissingConstants.SequenceEqual(new[] { "OUTPUT_FILE", "SOURCE_DIR" }),
            "WinUI と各改行形式で APP_VERSION のみ入力して再解析できる");
        Check(DefinitionConstants.AddMissing(input, partial.MissingConstants) == input, "各改行形式で空欄の定数を重複追加しない");
    }
    DefinitionInspection initial = compiler.Inspect(videoScript);
    Check(initial.Definition.Name == "VideoEditor" && initial.MissingConstants.SequenceEqual(new[] { "APP_VERSION", "OUTPUT_FILE", "SOURCE_DIR" }), "ビルド定数未入力でも定義を読み込み、必要な定数を列挙");
    string blankConstants = DefinitionConstants.AddMissing("", initial.MissingConstants);
    Dictionary<string, string> blankValues = DefinitionConstants.Parse(blankConstants.Split('\n'));
    Check(blankValues.Count == 3 && blankValues.Values.All(value => value.Length == 0), "必要な定数を空欄で補完");
    DefinitionInspection blanks = compiler.Inspect(videoScript, DefinitionConstants.ParseValues(blankConstants));
    Check(blanks.MissingConstants.SequenceEqual(initial.MissingConstants), "補完した空欄を未指定として読み込み、バージョン解析を失敗させない");
    string preserved = DefinitionConstants.AddMissing("APP_VERSION=2.3.4", initial.MissingConstants);
    Check(DefinitionConstants.Parse(preserved.Split('\n'))["APP_VERSION"] == "2.3.4" && DefinitionConstants.AddMissing(preserved, initial.MissingConstants) == preserved, "入力済みの値を保持して定数を重複させない");
    ExpectInvalid(() => compiler.Compile(videoScript), "APP_VERSION is required", "定義の読み込み後もビルド時は定数を必須にする");
    Dictionary<string, string> unavailableSource = DefinitionConstants.Parse([
        "APP_VERSION=1.2.3", "OUTPUT_FILE=Setup.exe", $"SOURCE_DIR=\"{Path.Combine(sandbox, "not-published")}\""
    ]);
    DefinitionInspection withoutFiles = compiler.Inspect(videoScript, unavailableSource);
    Check(withoutFiles.MissingConstants.Count == 0 && withoutFiles.Definition.Version == "1.2.3", "取り込み元未準備でも表示情報を読み込める");
    Check(unavailableSource["SOURCE_DIR"] == Path.Combine(sandbox, "not-published"), "定数値の外側の引用符を除去");
    ExpectInvalid(() => compiler.Compile(videoScript, unavailableSource), "not-published", "ビルド時には取り込み元を検証する");
    Dictionary<string, string> defines = new() { ["APP_VERSION"] = "1.2.3", ["SOURCE_DIR"] = Path.Combine(sandbox, "assets"), ["OUTPUT_FILE"] = "VideoEditor-Test.exe" };
    InstallPlan video = compiler.Compile(videoScript, defines);
    Check(video.Definition.Name == "VideoEditor" && video.Definition.Version == "1.2.3", "実際の VideoEditor 定義と外部定数");
    Check(video.Definition.LaunchExecutable == @"app\VideoEditor.exe" && video.Definition.Uninstaller == "Uninstall.exe", "起動ファイルとアンインストーラー");
    Check(video.Install.Any(op => op.Command == "createshortcut") && video.Install.Count(op => op.Command.StartsWith("writereg")) == 8, "ショートカットとレジストリのコンパイル");
    Check(video.Initialize.Count == 9 && video.Uninstall.Count == 7, "OS 条件とアンインストールのコンパイル");
    RuntimeOperations.CheckRequirements(video.Initialize);
    Console.WriteLine("PASS: VideoEditor.nsi の全行を解析（実アプリのインストールは実行しない）");
}

if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
{
    string applicationExecutable = Path.Combine(sandbox, "Main.exe");
    File.Copy(Environment.ProcessPath!, applicationExecutable);
    PackageDefinition iconDefinition = new() { Name = "Main", LaunchExecutable = @"app\Main.exe" };
    InstallPlan iconPlan = new(iconDefinition, "Setup.exe", [new("missing-helper.exe", @"app\Helper.exe"), new(applicationExecutable, @"app\Main.exe")], []);
    Check(ApplicationIcon.FindExecutable(iconPlan, iconDefinition) == applicationExecutable, "補助 EXE ではなくインストール対象アプリのアイコンを選ぶ");
    string iconProject = Path.Combine(sandbox, "IconProject");
    Directory.CreateDirectory(Path.Combine(iconProject, "Assets"));
    Directory.CreateDirectory(Path.Combine(iconProject, "installer"));
    string projectIcon = Path.Combine(iconProject, "Assets", "Main.ico");
    List<(int Size, byte[] Png)> iconFrames = [];
    foreach (int frameSize in new[] { 48, 256 })
    {
        using System.Drawing.Bitmap bitmap = new(frameSize, frameSize);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(frameSize == 256 ? System.Drawing.Color.Lime : System.Drawing.Color.Red);
        using MemoryStream imageBytes = new();
        bitmap.Save(imageBytes, System.Drawing.Imaging.ImageFormat.Png);
        iconFrames.Add((frameSize, imageBytes.ToArray()));
    }
    using (FileStream file = File.Create(projectIcon))
    using (BinaryWriter writer = new(file))
    {
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)iconFrames.Count);
        int imageOffset = 6 + 16 * iconFrames.Count;
        foreach ((int size, byte[] bytes) in iconFrames)
        {
            writer.Write(size == 256 ? (byte)0 : (byte)size); writer.Write(size == 256 ? (byte)0 : (byte)size);
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(bytes.Length); writer.Write(imageOffset); imageOffset += bytes.Length;
        }
        foreach ((int _, byte[] bytes) in iconFrames) writer.Write(bytes);
    }
    await File.WriteAllTextAsync(Path.Combine(iconProject, "Main.csproj"), "<Project><PropertyGroup><ApplicationIcon>Assets/Main.ico</ApplicationIcon></PropertyGroup></Project>");
    string iconScript = Path.Combine(iconProject, "installer", "Main.nsi");
    await File.WriteAllTextAsync(iconScript, "Name Main");
    Check(ApplicationIcon.FindIconFile(iconPlan, applicationExecutable, iconScript) == projectIcon, "対象プロジェクトが指定するアプリアイコンを選ぶ");
    string apphostExecutable = Path.Combine(sandbox, "apphost.exe");
    File.Copy(applicationExecutable, apphostExecutable);
    InstallPlan apphostPlan = iconPlan with { Files = [new(apphostExecutable, @"app\apphost.exe"), new("missing-helper.exe", @"app\Helper.exe")] };
    string apphostWork = Path.Combine(sandbox, "apphost-icon");
    Directory.CreateDirectory(apphostWork);
    Check(ApplicationIcon.FindExecutable(apphostPlan, iconDefinition) is null, "apphost と補助 EXE を対象アプリと誤認しない");
    Check(ApplicationIcon.TryWrite(apphostPlan, iconDefinition, Path.Combine(apphostWork, "AppIcon.png"), Console.WriteLine, iconScript),
        "対象 EXE がなくても起動ファイル名からプロジェクトのアイコンを取得");
    InstallPlan apphostPrepared = PackageBuilder.PreparePlan(apphostPlan, apphostWork);
    Check(File.Exists(Path.Combine(apphostWork, "Application.ico")) && apphostPrepared.Files.Any(file => file.Destination == @".hsis\Application.ico"),
        "対象 EXE がなくてもセットアップとタスクバー用 ICO を生成して同梱");
    string pngPath = Path.Combine(sandbox, "AppIcon.png");
    List<string> iconLog = [];
    Check(ApplicationIcon.TryWrite(iconPlan, iconDefinition, pngPath, iconLog.Add, iconScript) && iconLog.Contains($"アプリアイコン: {projectIcon}"), "独自アイコンのない EXE には汎用アイコンではなくプロジェクトのアイコンを使う");
    byte[] png = await File.ReadAllBytesAsync(pngPath);
    Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "アイコンを表示用の PNG として保存");
    using (System.Drawing.Bitmap decodedIcon = new(pngPath))
        Check(decodedIcon.Width == 256 && decodedIcon.Height == 256 && decodedIcon.GetPixel(128, 128).G == 255 && decodedIcon.GetPixel(128, 128).R == 0,
            "48px の画像の拡大ではなく 256px の高解像度フレームを取得");
    InstallPlan shortcutPlan = PackageBuilder.PreparePlan(iconPlan with
    {
        Install = [new("createshortcut", [@"$INSTDIR\Main.lnk", @"$INSTDIR\app\Main.exe"]),
            new("writeregstr", ["HKCU", @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS.IconTest", "DisplayName", "Main"]),
            new("writeregstr", ["HKCU", @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS.IconTest", "DisplayIcon", @"$INSTDIR\app\Main.exe"])]
    }, sandbox);
    ScriptOperation displayIcon = shortcutPlan.Install.Last(operation => operation.Command == "writeregstr" && operation.Arguments[2] == "DisplayIcon");
    Check(displayIcon.Arguments[3] == @"$INSTDIR\.hsis\Application.ico", "アプリ一覧のアイコンはアイコンのない EXE ではなく同梱 ICO を参照する");
    string shortcutRoot = Path.Combine(sandbox, "shortcut-icon");
    Directory.CreateDirectory(shortcutRoot);
    foreach (InstallFile file in shortcutPlan.Files.Where(file => file.Destination.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)))
    {
        string target = Path.Combine(shortcutRoot, file.Destination);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file.Source, target);
    }
    RuntimeOperations.Execute(shortcutPlan.Install.Where(operation => operation.Command == "createshortcut"), shortcutRoot, null, null);
    Type shortcutShellType = Type.GetTypeFromProgID("WScript.Shell")!;
    object shortcutShell = Activator.CreateInstance(shortcutShellType)!;
    object? savedShortcut = null;
    try
    {
        dynamic shell = shortcutShell;
        savedShortcut = shell.CreateShortcut(Path.Combine(shortcutRoot, "Main.lnk"));
        dynamic link = savedShortcut;
        string location = link.IconLocation;
        Check(location == Path.Combine(shortcutRoot, @".hsis\Application.ico") + ",0", "保存したショートカットは同梱アプリアイコンを参照する");
        using System.Drawing.Icon installedIcon = new(Path.Combine(shortcutRoot, @".hsis\Application.ico"), 256, 256);
        Check(installedIcon.Width == 256, "ショートカット用 ICO を Windows が読み取れる");
    }
    finally
    {
        if (savedShortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(savedShortcut);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcutShell);
    }
    string registryPath = @"Software\HSIS.Tests\" + Guid.NewGuid().ToString("N");
    string operationRoot = Path.Combine(sandbox, "operations");
    string fakeSetup = Path.Combine(sandbox, "FakeSetup.exe");
    await File.WriteAllTextAsync(fakeSetup, "test-only, not executable");
    try
    {
        using FileStream uninstaller = File.OpenRead(fakeSetup);
        RuntimeOperations.Execute([
            new("createdirectory", ["$INSTDIR"]),
            new("writeuninstaller", [@"$INSTDIR\Uninstall.exe"]),
            new("createshortcut", [@"$INSTDIR\Test.lnk", @"$INSTDIR\Uninstall.exe"]),
            new("writeregstr", ["HKCU", registryPath, "InstallDir", "$INSTDIR"]),
            new("writeregdword", ["HKCU", registryPath, "NoModify", "1"])
        ], operationRoot, uninstaller, null);
        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath)!)
            Check((string?)key.GetValue("InstallDir") == operationRoot && (int?)key.GetValue("NoModify") == 1, "レジストリの文字列・DWORD 登録");
        Check(File.Exists(Path.Combine(operationRoot, "Test.lnk")) && await File.ReadAllTextAsync(Path.Combine(operationRoot, "Uninstall.exe")) == "test-only, not executable", "ショートカットとアンインストーラー生成");
        RuntimeOperations.Execute([
            new("delete", [@"$INSTDIR\Test.lnk"]), new("delete", [@"$INSTDIR\Uninstall.exe"]),
            new("rmdir", ["$INSTDIR"]), new("deleteregkey", ["HKCU", registryPath])
        ], operationRoot, null, null);
        using Microsoft.Win32.RegistryKey? deletedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath);
        Check(!Directory.Exists(operationRoot) && deletedKey is null, "アンインストールによるファイルとレジストリ削除");
    }
    finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine($"PASS: {label}");
}

static void ExpectInvalid(Action action, string fragment, string label)
{
    try { action(); }
    catch (InvalidDataException ex) when (ex.Message.Contains(fragment)) { Console.WriteLine($"PASS: {label}"); return; }
    throw new Exception(label);
}

sealed class InlineProgress(Action<string> report) : IProgress<string>
{
    public void Report(string value) => report(value);
}
