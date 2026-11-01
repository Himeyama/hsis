using System.ComponentModel;
using System.Diagnostics;

namespace Hsis.Core;

public static class RunningApplications
{
    public static void EnsureClosed(string directory)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        List<string> applications = [];
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    string? executable = process.MainModule?.FileName;
                    if (executable is not null && executable.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        applications.Add(Path.GetFileName(executable));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Processes may exit during enumeration or belong to another user.
                }
            }
        }
        if (applications.Count > 0) throw new ApplicationsInUseException(string.Join("、", applications.Distinct(StringComparer.OrdinalIgnoreCase)));
    }
}
