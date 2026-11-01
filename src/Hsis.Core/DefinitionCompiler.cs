using System.Text;
using System.Text.RegularExpressions;

namespace Hsis.Core;

/// <summary>Compiles the supported .nsi language directly into an HSIS install plan.</summary>
public sealed class DefinitionCompiler
{
    private readonly Dictionary<string, string> _defines = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeIncludes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InstallFile> _files = [];
    private readonly List<string> _directories = [];
    private PackageDefinition _definition = new();
    private string _outputFile = "Setup.exe";
    private string _outPath = "$INSTDIR";
    private bool _inSection;
    private bool _uninstall;
    private bool _inFunction;
    private readonly List<ScriptOperation> _initialize = [];
    private readonly List<ScriptOperation> _install = [];
    private readonly List<ScriptOperation> _remove = [];
    private readonly HashSet<string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingConstants = new(StringComparer.Ordinal);
    private bool _inspecting;

    public InstallPlan Compile(string script, IReadOnlyDictionary<string, string>? defines = null)
        => CompileCore(script, defines, false);

    public DefinitionInspection Inspect(string script, IReadOnlyDictionary<string, string>? defines = null)
    {
        InstallPlan plan = CompileCore(script, defines, true);
        return new(plan.Definition, _missingConstants.Order(StringComparer.Ordinal).ToList());
    }

    private InstallPlan CompileCore(string script, IReadOnlyDictionary<string, string>? defines, bool inspecting)
    {
        _defines.Clear(); _activeIncludes.Clear(); _files.Clear(); _directories.Clear();
        _definition = new(); _outputFile = "Setup.exe"; _outPath = "$INSTDIR"; _inSection = false;
        _uninstall = false; _inFunction = false; _initialize.Clear(); _install.Clear(); _remove.Clear(); _headers.Clear();
        _inspecting = inspecting; _missingConstants.Clear();
        if (defines is not null) foreach (KeyValuePair<string, string> define in defines) _defines.Add(define.Key, define.Value);
        ReadFile(Path.GetFullPath(script));
        if (_inSection) throw new InvalidDataException("SectionEnd がありません。");
        if (_inFunction) throw new InvalidDataException("FunctionEnd がありません。");
        ValidateConditionStructure();
        if (_defines.TryGetValue("MUI_FINISHPAGE_RUN", out string? launch))
            _definition = _definition with { LaunchExecutable = NormalizeDestination(launch) };
        if (string.IsNullOrWhiteSpace(_definition.InstallDirectory))
            _definition = _definition with { InstallDirectory = @"$LOCALAPPDATA\Programs\" + _definition.Name };
        return new(_definition, _outputFile, [.. _files], [.. _directories]) { Initialize = [.. _initialize], Install = [.. _install], Uninstall = [.. _remove] };
    }

    private void ReadFile(string path)
    {
        if (!_activeIncludes.Add(path)) throw new InvalidDataException($"インクルードが循環しています: {path}");
        string[] lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
        Stack<bool> conditions = new();
        Stack<string?> requiredConstants = new();
        bool active = true;
        string pending = "";
        try
        {
            for (int index = 0; index < lines.Length; index++)
            {
                string line = pending + lines[index].Trim();
                if (line.EndsWith('\\')) { pending = line[..^1] + " "; continue; }
                pending = "";
                try
                {
                    List<string> raw = Tokenize(line);
                    if (raw.Count == 0) continue;
                    string command = raw[0].ToLowerInvariant();
                    if (command is "!ifdef" or "!ifndef")
                    {
                        Require(raw, 2); conditions.Push(active);
                        requiredConstants.Push(command == "!ifndef" && !_defines.ContainsKey(raw[1]) ? raw[1] : null);
                        active = active && (_defines.ContainsKey(raw[1]) == (command == "!ifdef"));
                        continue;
                    }
                    if (command == "!else")
                    {
                        Require(raw, 1);
                        if (conditions.Count == 0) throw new InvalidDataException("対応する !ifdef がありません。");
                        requiredConstants.Pop(); requiredConstants.Push(null);
                        active = conditions.Peek() && !active; continue;
                    }
                    if (command == "!endif")
                    {
                        Require(raw, 1);
                        if (conditions.Count == 0) throw new InvalidDataException("対応する !ifdef がありません。");
                        requiredConstants.Pop();
                        active = conditions.Pop(); continue;
                    }
                    if (!active) continue;
                    if (_inspecting && command == "!error" && requiredConstants.FirstOrDefault(name => name is not null) is string required)
                    {
                        _missingConstants.Add(required);
                        continue;
                    }
                    List<string> tokens = raw.Select(token => Expand(token, path)).ToList();
                    if (_inspecting && tokens.Any(token => Regex.Matches(token, @"\$\{([^}]+)\}").Any(match => _missingConstants.Contains(match.Groups[1].Value)))) continue;
                    Parse(tokens, path);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
                { throw new InvalidDataException($"{path}({index + 1}): {ex.Message}", ex); }
            }
            if (pending.Length != 0) throw new InvalidDataException($"{path}: 行継続が完結していません。");
            if (conditions.Count != 0) throw new InvalidDataException($"{path}: !endif がありません。");
        }
        finally { _activeIncludes.Remove(path); }
    }

    private void Parse(List<string> tokens, string source)
    {
        string command = tokens[0].ToLowerInvariant();
        string directory = Path.GetDirectoryName(source)!;
        if (_inFunction) { ParseInitialize(tokens); return; }
        switch (command)
        {
            case "!define":
                if (tokens.Count is < 2 or > 3 || tokens[1].StartsWith('/')) throw new InvalidDataException("!define の形式は !define 名前 [値] です。");
                _defines.Add(tokens[1], tokens.Count == 3 ? tokens[2] : ""); break;
            case "!undef": Require(tokens, 2); _defines.Remove(tokens[1]); break;
            case "!include":
                Require(tokens, 2);
                if (new[] { "MUI2.nsh", "LogicLib.nsh", "x64.nsh", "WinVer.nsh" }.Contains(tokens[1], StringComparer.OrdinalIgnoreCase)) _headers.Add(tokens[1]);
                else ReadFile(Path.GetFullPath(Path.Combine(directory, tokens[1])));
                break;
            case "!error": Require(tokens, 2); throw new InvalidDataException(tokens[1] + "（ビルド定数は -D名前=値 で指定してください）");
            case "!insertmacro": ParseUiMacro(tokens, directory); break;
            case "name": Require(tokens, 2); _definition = _definition with { Name = tokens[1] }; break;
            case "outfile": Require(tokens, 2); _outputFile = tokens[1]; break;
            case "installdir": Require(tokens, 2); _definition = _definition with { InstallDirectory = tokens[1] }; break;
            case "installdirregkey":
                Require(tokens, 4); ValidateRegistry(tokens[1]);
                _definition = _definition with { InstallDirectoryRegistry = tokens.Skip(1).ToArray() }; break;
            case "setcompressor":
                if (tokens.Count != 3 || tokens[1] != "/SOLID" || tokens[2] != "lzma") throw new InvalidDataException("SetCompressor /SOLID lzma の形式のみ対応しています。");
                // Compression directives are mapped to the HSIS package's optimal ZIP compression.
                break;
            case "viproductversion": Require(tokens, 2); if (!Version.TryParse(tokens[1], out Version? _)) throw new InvalidDataException("製品バージョンが不正です。"); _definition = _definition with { ProductVersion = tokens[1], Version = tokens[1] }; break;
            case "viaddversionkey":
                Require(tokens, 4); if (!tokens[1].StartsWith("/LANG=")) throw new InvalidDataException("VIAddVersionKey /LANG=... キー 値 を指定してください。");
                if (tokens[2] is not ("ProductName" or "FileDescription" or "FileVersion" or "LegalCopyright")) throw new InvalidDataException($"未対応のバージョンキー: {tokens[2]}");
                _definition.VersionKeys[tokens[2]] = tokens[3];
                if (tokens[2] == "FileVersion") _definition = _definition with { Version = tokens[3] };
                break;
            case "licensedata": Require(tokens, 2); SetLicense(directory, tokens[1]); break;
            case "requestexecutionlevel":
                Require(tokens, 2);
                if (tokens[1] is not ("user" or "admin")) throw new InvalidDataException("RequestExecutionLevel は user / admin に対応しています。");
                _definition = _definition with { RequiresAdministrator = tokens[1] == "admin" }; break;
            case "unicode":
                Require(tokens, 2);
                if (!tokens[1].Equals("true", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unicode true を指定してください。");
                break;
            case "section":
                if (_inSection || tokens.Count > 2 || (tokens.Count == 2 && tokens[1].StartsWith('/')))
                    throw new InvalidDataException("通常の必須 Section のみ対応しています。");
                _uninstall = tokens.Count == 2 && tokens[1].Equals("Uninstall", StringComparison.OrdinalIgnoreCase);
                _inSection = true; break;
            case "sectionend": Require(tokens, 1); if (!_inSection) throw new InvalidDataException("対応する Section がありません。"); _inSection = false; _uninstall = false; break;
            case "function":
                Require(tokens, 2); if (_inSection || tokens[1] != ".onInit") throw new InvalidDataException("Function .onInit のみ対応しています。");
                _inFunction = true; break;
            case "setoutpath": RequireSection(); Require(tokens, 2); string relativePath = NormalizeDestination(tokens[1]); _outPath = "$INSTDIR\\" + relativePath; _directories.Add(relativePath); break;
            case "createdirectory": RequireSection(); Require(tokens, 2); ValidateRuntimePath(tokens[1]); AddOperation(command, tokens); break;
            case "file": RequireSection(); AddFiles(tokens, directory); break;
            case "setshellvarcontext": RequireSection(); Require(tokens, 2); if (tokens[1] != "current") throw new InvalidDataException("SetShellVarContext current のみ対応しています。"); break;
            case "setoverwrite": RequireSection(); Require(tokens, 2); if (tokens[1] != "on") throw new InvalidDataException("SetOverwrite on のみ対応しています。"); break;
            case "writeuninstaller":
                RequireSection(); Require(tokens, 2); if (_uninstall) throw new InvalidDataException("WriteUninstaller はインストール用です。");
                _definition = _definition with { Uninstaller = NormalizeDestination(tokens[1]) }; AddOperation(command, tokens); break;
            case "createshortcut":
                RequireSection(); Require(tokens, 3); ValidateRuntimePath(tokens[1]); ValidateRuntimePath(tokens[2]); AddOperation(command, tokens); break;
            case "writeregstr": case "writeregdword":
                RequireSection(); Require(tokens, 5); ValidateRegistry(tokens[1]);
                if (command == "writeregdword" && !uint.TryParse(tokens[4], out uint _)) throw new InvalidDataException("DWORD の値が不正です。");
                AddOperation(command, tokens); break;
            case "deleteregkey": RequireSection(); Require(tokens, 3); ValidateRegistry(tokens[1]); RequireUninstall(); AddOperation(command, tokens); break;
            case "delete": RequireSection(); Require(tokens, 2); RequireUninstall(); ValidateRuntimePath(tokens[1]); AddOperation(command, tokens); break;
            case "rmdir":
                RequireSection(); RequireUninstall();
                if (tokens.Count == 3 && tokens[1] == "/r") ValidateRuntimePath(tokens[2]);
                else { Require(tokens, 2); ValidateRuntimePath(tokens[1]); }
                AddOperation(command, tokens); break;
            case "page":
                Require(tokens, 2);
                if (tokens[1] is not ("license" or "directory" or "instfiles")) throw new InvalidDataException($"未対応のページ: {tokens[1]}");
                break;
            default: throw new InvalidDataException($"未対応の命令: {tokens[0]}");
        }
    }

    private void ParseUiMacro(List<string> tokens, string directory)
    {
        if (tokens.Count < 2) throw new InvalidDataException("マクロ名がありません。");
        switch (tokens[1])
        {
            case "MUI_PAGE_WELCOME": case "MUI_PAGE_DIRECTORY": case "MUI_PAGE_INSTFILES": case "MUI_PAGE_FINISH": case "MUI_UNPAGE_CONFIRM": case "MUI_UNPAGE_INSTFILES": Require(tokens, 2); break;
            case "MUI_PAGE_LICENSE": Require(tokens, 3); SetLicense(directory, tokens[2]); break;
            case "MUI_LANGUAGE":
                Require(tokens, 3);
                if (tokens[2] is not ("Japanese" or "English")) throw new InvalidDataException("Japanese / English のみ指定できます。現在の表示は日本語です。");
                break;
            default: throw new InvalidDataException($"未対応のマクロ: {tokens[1]}");
        }
        // These macros describe the standard wizard, which is rendered by the HSIS runtime.
    }

    private void AddFiles(List<string> tokens, string directory)
    {
        bool recursive = false;
        string? rename = null;
        int index = 1;
        for (; index < tokens.Count && tokens[index].StartsWith('/'); index++)
        {
            string option = tokens[index];
            if (option.Equals("/r", StringComparison.OrdinalIgnoreCase)) recursive = true;
            else if (option.StartsWith("/oname=", StringComparison.OrdinalIgnoreCase)) rename = option[7..];
            else throw new InvalidDataException($"未対応の File オプション: {option}");
        }
        if (index != tokens.Count - 1) throw new InvalidDataException("File [/r] [/oname=名前] ソース を指定してください。");
        if (recursive && rename is not null) throw new InvalidDataException("/r と /oname は併用できません。");
        if (_inspecting) return;
        string pattern = Path.GetFullPath(Path.Combine(directory, tokens[index]));
        string parent = Path.GetDirectoryName(pattern)!;
        string filter = Path.GetFileName(pattern);
        List<string> paths;
        string relativeRoot = parent;
        if (Directory.Exists(pattern))
        {
            if (!recursive) throw new InvalidDataException("フォルダーの取り込みには File /r を指定してください。");
            paths = Directory.GetFiles(pattern, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        else paths = Directory.GetFiles(parent, filter, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) throw new InvalidDataException($"File の対象がありません: {tokens[index]}");
        if (rename is not null && paths.Count != 1) throw new InvalidDataException("/oname は単一ファイルに指定してください。");
        foreach (string path in paths)
        {
            string relative = rename ?? (recursive ? Path.GetRelativePath(relativeRoot, path) : Path.GetFileName(path));
            // Source file names are literal, even when they contain NSIS-like dollar names.
            string destination = NormalizeDestination(_outPath + "\\" + relative, rename is null);
            _files.Add(new(path, destination));
        }
    }

    private string Expand(string value, string source)
    {
        return Regex.Replace(value, @"\$\{([^}]+)\}", match => match.Groups[1].Value switch
        {
            "__FILEDIR__" => Path.GetDirectoryName(source)!,
            "__FILE__" => Path.GetFileName(source),
            "If" or "IfNot" or "Else" or "EndIf" when _headers.Contains("LogicLib.nsh") => match.Value,
            "RunningX64" when _headers.Contains("x64.nsh") => match.Value,
            "AtLeastBuild" when _headers.Contains("WinVer.nsh") => match.Value,
            string key when _defines.TryGetValue(key, out string? replacement) => replacement,
            string key when _inspecting => MissingConstant(key, match.Value),
            _ => throw new InvalidDataException($"未定義の定数: {match.Value}")
        });
    }

    private string MissingConstant(string key, string placeholder) { _missingConstants.Add(key); return placeholder; }

    private void ParseInitialize(List<string> tokens)
    {
        string command = tokens[0].ToLowerInvariant();
        switch (command)
        {
            case "functionend": Require(tokens, 1); _inFunction = false; break;
            case "${if}": case "${ifnot}":
                if (tokens.Count == 2 && tokens[1] == "${RunningX64}") { }
                else if (tokens.Count == 3 && tokens[1] == "${AtLeastBuild}" && int.TryParse(tokens[2], out int _)) { }
                else throw new InvalidDataException("未対応の実行時条件です。");
                _initialize.Add(new(command, tokens.Skip(1).ToArray())); break;
            case "${else}": case "${endif}": case "abort": Require(tokens, 1); _initialize.Add(new(command, [])); break;
            case "messagebox":
                Require(tokens, 3); if (tokens[1] != "MB_ICONSTOP") throw new InvalidDataException("MessageBox MB_ICONSTOP のみ対応しています。");
                _initialize.Add(new(command, tokens.Skip(1).ToArray())); break;
            default: throw new InvalidDataException($".onInit 内の未対応命令: {tokens[0]}");
        }
    }

    private void ValidateConditionStructure()
    {
        Stack<bool> blocks = new();
        foreach (ScriptOperation operation in _initialize)
        {
            if (operation.Command is "${if}" or "${ifnot}") blocks.Push(false);
            else if (operation.Command == "${else}")
            {
                if (blocks.Count == 0 || blocks.Pop()) throw new InvalidDataException("Else に対応する If がありません。");
                blocks.Push(true);
            }
            else if (operation.Command == "${endif}")
            {
                if (blocks.Count == 0) throw new InvalidDataException("EndIf に対応する If がありません。");
                blocks.Pop();
            }
        }
        if (blocks.Count != 0) throw new InvalidDataException("EndIf がありません。");
    }

    private void AddOperation(string command, List<string> tokens) => (_uninstall ? _remove : _install).Add(new(command, tokens.Skip(1).ToArray()));
    private void RequireUninstall() { if (!_uninstall) throw new InvalidDataException("この命令は Uninstall セクションのみ対応しています。"); }
    private static void ValidateRegistry(string root) { if (root is not ("HKCU" or "HKLM")) throw new InvalidDataException("レジストリルートは HKCU / HKLM に対応しています。"); }
    private static void ValidateRuntimePath(string path)
    {
        if (path.StartsWith("$SMPROGRAMS\\", StringComparison.OrdinalIgnoreCase)) NormalizeDestination("$INSTDIR\\" + path[12..]);
        else NormalizeDestination(path);
    }

    public static string NormalizeDestination(string path)
        => NormalizeDestination(path, false);

    private static string NormalizeDestination(string path, bool allowLiteralDollar)
    {
        string normalized = path.Replace('/', '\\');
        if (!normalized.Equals("$INSTDIR", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("$INSTDIR\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("配置先には $INSTDIR 以下を指定してください。");
        string relative = normalized[8..].TrimStart('\\');
        if (relative.Split('\\').Any(part => part is "." or ".." || part.Contains(':') || (!allowLiteralDollar && part.Contains('$'))) || Path.IsPathRooted(relative))
            throw new InvalidDataException($"無効な配置先: {path}");
        return relative;
    }

    private void SetLicense(string directory, string path) => _definition = _definition with { License = File.ReadAllText(Path.Combine(directory, path), new UTF8Encoding(false, true)) };
    private void RequireSection() { if (!_inSection) throw new InvalidDataException("命令は Section 内に記述してください。"); }
    private static void Require(List<string> tokens, int count) { if (tokens.Count != count) throw new InvalidDataException($"{tokens[0]} の引数の数が違います。"); }

    private static List<string> Tokenize(string line)
    {
        List<string> tokens = [];
        StringBuilder token = new();
        char quote = '\0';
        bool started = false;
        for (int index = 0; index < line.Length; index++)
        {
            char ch = line[index];
            if (ch == '$' && index + 1 < line.Length && line[index + 1] == '$') { token.Append('$'); started = true; index++; continue; }
            if (ch == '$' && index + 2 < line.Length && line[index + 1] == '\\')
            {
                char escaped = line[index + 2];
                token.Append(escaped switch { 'r' => '\r', 'n' => '\n', 't' => '\t', _ => escaped }); index += 2; started = true; continue;
            }
            if (quote != '\0') { if (ch == quote) quote = '\0'; else token.Append(ch); started = true; continue; }
            if (ch is ';' or '#') break;
            if (ch is '"' or '\'' or '`') { quote = ch; started = true; continue; }
            if (char.IsWhiteSpace(ch)) { if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; } }
            else { token.Append(ch); started = true; }
        }
        if (quote != '\0') throw new InvalidDataException("引用符が閉じていません。");
        if (started) tokens.Add(token.ToString());
        return tokens;
    }
}
