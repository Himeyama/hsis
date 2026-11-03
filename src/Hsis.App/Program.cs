using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Hsis.Core;

namespace Hsis.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Wait for the installed process to exit before initializing WPF,
        // so the temporary copy can remove the original executable.
        int rootArgument = Array.IndexOf(args, "--uninstall");
        if (rootArgument < 0 && Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute.Key == "Hsis.Uninstaller" && attribute.Value == "true"))
        {
            using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hsis.Package.json")
                ?? throw new InvalidDataException("セットアップ情報がありません。");
            PackageDefinition definition = JsonSerializer.Deserialize<PackageDefinition>(resource)
                ?? throw new InvalidDataException("セットアップ情報が不正です。");
            string source = Environment.ProcessPath!;
            string installRoot = Path.GetDirectoryName(source)!;
            string? parentDirectory = Path.GetDirectoryName(definition.Uninstaller);
            if (!string.IsNullOrEmpty(parentDirectory))
                foreach (string _ in parentDirectory.Split(Path.DirectorySeparatorChar))
                    installRoot = Path.GetDirectoryName(installRoot) ?? throw new InvalidDataException("インストール先を取得できません。");
            string temporary = Path.Combine(Path.GetTempPath(), "Hsis", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            string executable = Path.Combine(temporary, Path.GetFileName(source));
            File.Copy(source, executable);
            ProcessStartInfo start = new(executable) { UseShellExecute = false, WorkingDirectory = temporary };
            start.ArgumentList.Add("--uninstall"); start.ArgumentList.Add(installRoot);
            start.ArgumentList.Add("--wait-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (args.Contains("/S", StringComparer.OrdinalIgnoreCase)) start.ArgumentList.Add("--quiet");
            using Process? child = Process.Start(start);
            return;
        }
        int waitArgument = Array.IndexOf(args, "--wait-pid");
        if (waitArgument >= 0 && waitArgument + 1 < args.Length && int.TryParse(args[waitArgument + 1], out int parentId))
        {
            try { using Process parent = Process.GetProcessById(parentId); parent.WaitForExit(); }
            catch (ArgumentException) { }
        }
        if (rootArgument >= 0 && rootArgument + 1 < args.Length && args.Contains("--quiet"))
        {
            try
            {
                using Stream payload = InstallerResources.OpenPayload();
                InstallationEngine.UninstallAsync(payload, Path.GetFullPath(args[rootArgument + 1])).GetAwaiter().GetResult();
            }
            catch { Environment.ExitCode = 1; }
            return;
        }
        StartApplication();
    }

    private static void StartApplication()
    {
        App application = new();
        application.InitializeComponent();
        application.Run();
    }
}
