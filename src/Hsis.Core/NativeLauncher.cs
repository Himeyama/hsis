using System.Diagnostics;

namespace Hsis.Core;

internal static class NativeLauncher
{
    public static async Task<string> BuildAsync(string project, string work, Action<string> log, CancellationToken cancellationToken)
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) throw new FileNotFoundException("Visual Studio の C++ ビルドツールが必要です。", vswhere);
        ProcessStartInfo start = new(vswhere) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-find", @"VC\Auxiliary\Build\vcvars64.bat" }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("C++ ビルド環境を確認できません。");
        string environment = (await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (!File.Exists(environment) || environment.Contains('"') || environment.Contains('\n')) throw new InvalidOperationException("Visual Studio の「C++ によるデスクトップ開発」が必要です。");
        string directory = Path.Combine(work, "Native");
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(project, "src", "Hsis.Bootstrap", "Bootstrap.cpp"), Path.Combine(directory, "Bootstrap.cpp"));
        File.Copy(Path.Combine(project, "src", "Hsis.Bootstrap", "Bootstrap.rc"), Path.Combine(directory, "Bootstrap.rc"));
        Directory.CreateDirectory(Path.Combine(directory, "Fonts"));
        foreach (string font in Directory.GetFiles(Path.Combine(project, "src", "Hsis.Bootstrap", "Fonts")))
            File.Copy(font, Path.Combine(directory, "Fonts", Path.GetFileName(font)));
        File.Copy(Path.Combine(project, "src", "Hsis.App", "Hsis.ico"), Path.Combine(directory, "Application.ico"));
        File.Copy(Path.Combine(project, "src", "Hsis.App", "app.manifest"), Path.Combine(directory, "app.manifest"));
        log("起動直後の案内画面を生成しています…");
        string command = $"@echo off\r\nchcp 65001 >nul\r\ncall \"{environment}\" >nul\r\nif errorlevel 1 exit /b 1\r\nrc /nologo Bootstrap.rc\r\nif errorlevel 1 exit /b 1\r\ncl /nologo /utf-8 /std:c++17 /O2 /MT /EHsc /DUNICODE /D_UNICODE Bootstrap.cpp Bootstrap.res /link /SUBSYSTEM:WINDOWS /OUT:Bootstrap.exe user32.lib shell32.lib gdi32.lib dwmapi.lib\r\n";
        await File.WriteAllTextAsync(Path.Combine(directory, "Build.cmd"), command, new System.Text.UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await ProcessRunner.RunAsync("cmd.exe", ["/d", "/c", "Build.cmd"], directory, log, cancellationToken).ConfigureAwait(false);
        return Path.Combine(directory, "Bootstrap.exe");
    }
}
