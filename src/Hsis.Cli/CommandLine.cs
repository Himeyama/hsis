using Hsis.Core;

namespace Hsis.Cli;

public static class CommandLine
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"] or ["build", "--help"])
        {
            await output.WriteLineAsync(Help);
            return 0;
        }
        if (args[0] != "build")
        {
            await error.WriteLineAsync("コマンドが不明です。hsis --help を参照してください。");
            return 2;
        }
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        List<string> defineArguments = [];
        HashSet<string> valueOptions = ["--output", "--name", "--version", "--install-dir", "--license", "--launch", "--template"];
        string? script = null;
        bool administrator = false;
        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.StartsWith("-D", StringComparison.Ordinal) && argument.Length > 2) { defineArguments.Add(argument[2..]); continue; }
            if (argument == "--admin") { administrator = true; continue; }
            if (valueOptions.Contains(argument))
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(argument, args[index + 1]))
                {
                    await error.WriteLineAsync($"{argument} の値がないか、重複しています。");
                    return 2;
                }
                index++;
            }
            else if (!argument.StartsWith('-') && script is null) script = argument;
            else { await error.WriteLineAsync($"不明な引数: {argument}"); return 2; }
        }
        if (script is null) { await error.WriteLineAsync(".nsi 定義ファイルを指定してください。"); return 2; }

        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            Dictionary<string, string> defines = DefinitionConstants.Parse(defineArguments);
            InstallPlan plan = new DefinitionCompiler().Compile(script, defines);
            string name = options.GetValueOrDefault("--name", plan.Definition.Name);
            string license = options.TryGetValue("--license", out string? licensePath)
                ? await File.ReadAllTextAsync(licensePath, cancellation.Token) : plan.Definition.License;
            PackageDefinition definition = plan.Definition with
            {
                Name = name, Version = options.GetValueOrDefault("--version", plan.Definition.Version),
                InstallDirectory = options.GetValueOrDefault("--install-dir", plan.Definition.InstallDirectory),
                License = license, LaunchExecutable = options.GetValueOrDefault("--launch", plan.Definition.LaunchExecutable), RequiresAdministrator = administrator || plan.Definition.RequiresAdministrator
            };
            string setup = await new PackageBuilder().BuildAsync(script,
                options.GetValueOrDefault("--output", Path.Combine(Environment.CurrentDirectory, "artifacts", "installers")),
                options.GetValueOrDefault("--template", Path.Combine(AppContext.BaseDirectory, "Template")),
                definition, line => { lock (output) output.WriteLine(line); }, cancellation.Token, defines);
            await output.WriteLineAsync($"SETUP={setup}");
            return 0;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("生成をキャンセルしました。"); return 130; }
        catch (Exception ex) { await error.WriteLineAsync(ex.Message); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private const string Help = """
        HSIS — インストーラーの生成

        hsis build <script.nsi> [options]

        -D名前=値            ビルド定数（複数指定可能）
        --output <directory>  出力先（省略時: ./artifacts/installers）
        --name <name>         表示名を上書き
        --version <version>   表示バージョン（省略時: 1.0.0）
        --install-dir <path>  保存先を上書き
        --license <file>      同意画面に表示する UTF-8 テキスト
        --launch <file>       完了後の起動ファイル（保存先からの相対パス）
        --admin               インストール時に管理者の承認を求める
        --template <dir>      ビルド用テンプレートの場所（通常は指定不要）
        --help                このヘルプを表示

        定義を HSIS が直接処理します。生成後のセットアップは自動起動しません。
        終了コード: 0 成功 / 1 生成失敗 / 2 引数エラー / 130 キャンセル
        """;
}
