using Hsis.Core;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string sandbox = Path.Combine(root, "artifacts", "native-check", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
DefinitionCompiler compiler = new();
InstallPlan sample = compiler.Compile(Path.Combine(root, "samples", "HelloApp", "installer.nsi"));
Check(sample.Definition.Name == "Hello App" && sample.Definition.License.Contains("サンプル利用規約"), "定義から表示情報・規約を読み取る");
Check(sample.Files.Count == 2 && !sample.Definition.RequiresAdministrator, "File と権限を読み取る");
string payload = Path.Combine(sandbox, "Payload.zip");
await PackageBuilder.CreatePayloadAsync(sample, payload, Console.WriteLine);
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
    Dictionary<string, string> defines = new() { ["APP_VERSION"] = "1.2.3", ["SOURCE_DIR"] = Path.Combine(sandbox, "assets"), ["OUTPUT_FILE"] = "VideoEditor-Test.exe" };
    InstallPlan video = compiler.Compile(videoScript, defines);
    Check(video.Definition.Name == "VideoEditor" && video.Definition.Version == "1.2.3", "実際の VideoEditor 定義と外部定数");
    Check(video.Definition.LaunchExecutable == @"app\VideoEditor.exe" && video.Definition.Uninstaller == "Uninstall.exe", "起動ファイルとアンインストーラー");
    Check(video.Install.Any(op => op.Command == "createshortcut") && video.Install.Count(op => op.Command.StartsWith("writereg")) == 8, "ショートカットとレジストリのコンパイル");
    Check(video.Initialize.Count == 9 && video.Uninstall.Count == 7, "OS 条件とアンインストールのコンパイル");
    RuntimeOperations.CheckRequirements(video.Initialize);
    Console.WriteLine("PASS: VideoEditor.nsi の全行を解析（実アプリのインストールは実行しない）");
}

if (OperatingSystem.IsWindows())
{
    string registryPath = @"Software\HSIS.Tests\" + Guid.NewGuid().ToString("N");
    string operationRoot = Path.Combine(sandbox, "operations");
    string fakeSetup = Path.Combine(sandbox, "FakeSetup.exe");
    await File.WriteAllTextAsync(fakeSetup, "test-only, not executable");
    try
    {
        RuntimeOperations.Execute([
            new("createdirectory", ["$INSTDIR"]),
            new("writeuninstaller", [@"$INSTDIR\Uninstall.exe"]),
            new("createshortcut", [@"$INSTDIR\Test.lnk", @"$INSTDIR\Uninstall.exe"]),
            new("writeregstr", ["HKCU", registryPath, "InstallDir", "$INSTDIR"]),
            new("writeregdword", ["HKCU", registryPath, "NoModify", "1"])
        ], operationRoot, fakeSetup, null);
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
