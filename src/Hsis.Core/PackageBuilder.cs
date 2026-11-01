using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;

namespace Hsis.Core;

public sealed class PackageBuilder
{
    public async Task<string> BuildAsync(string script, string output, string template, PackageDefinition? definition, Action<string> log, CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? defines = null)
    {
        if (!File.Exists(script)) throw new FileNotFoundException("定義ファイルが見つかりません。", script);
        log("定義ファイルを解析しています…");
        InstallPlan plan = new DefinitionCompiler().Compile(script, defines);
        definition ??= plan.Definition;
        string outputName = Path.GetFileName(plan.OutputFile);
        if (!outputName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("OutFile には .exe を指定してください。");
        if (!File.Exists(Path.Combine(template, "src", "Hsis.App", "Hsis.App.csproj")))
            throw new DirectoryNotFoundException("ビルド用テンプレートがありません。publish 出力から起動してください。");
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new InvalidOperationException("アプリ名を入力してください。");
        if (string.IsNullOrWhiteSpace(definition.InstallDirectory) || !Path.IsPathFullyQualified(InstallPaths.Expand(definition.InstallDirectory)))
            throw new InvalidOperationException("インストール先には絶対パスを指定してください。");
        string work = Path.Combine(Path.GetTempPath(), "Hsis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string icon = Path.Combine(work, "AppIcon.png");
            ApplicationIcon.TryWrite(plan, definition, icon, log, script);
            plan = PreparePlan(plan with { Definition = definition }, work);
            definition = plan.Definition;
            string payload = Path.Combine(work, "Payload.zip");
            await CreatePayloadAsync(plan, payload, log, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(work, "Package.json"), JsonSerializer.Serialize(definition), cancellationToken).ConfigureAwait(false);
            string project = Path.Combine(work, "Source");
            CopyDirectory(template, project);
            string manifestPath = Path.Combine(project, "src", "Hsis.App", "app.manifest");
            XDocument manifest = XDocument.Load(manifestPath);
            XElement requestedLevel = manifest.Descendants(XName.Get("requestedExecutionLevel", "urn:schemas-microsoft-com:asm.v3")).Single();
            requestedLevel.SetAttributeValue("level", definition.RequiresAdministrator ? "requireAdministrator" : "asInvoker");
            manifest.Save(manifestPath);
            string projectFile = Path.Combine(project, "src", "Hsis.App", "Hsis.App.csproj");
            XDocument projectXml = XDocument.Load(projectFile);
            XElement properties = new("PropertyGroup", new XElement("FileVersion", definition.ProductVersion), new XElement("AssemblyVersion", definition.ProductVersion),
                new XElement("Product", definition.VersionKeys.GetValueOrDefault("ProductName", definition.Name)),
                new XElement("Description", definition.VersionKeys.GetValueOrDefault("FileDescription", definition.Name)),
                new XElement("Copyright", definition.VersionKeys.GetValueOrDefault("LegalCopyright", "")));
            projectXml.Root!.Add(properties);
            projectXml.Save(projectFile);
            bool hasUninstaller = definition.Uninstaller.Length > 0;
            if (hasUninstaller)
            {
                InstallPlan uninstallPlan = plan with { Files = [], Directories = [], Install = [] };
                await CreatePayloadAsync(uninstallPlan, Path.Combine(work, "UninstallerPayload.zip"), log, cancellationToken).ConfigureAwait(false);
                string uninstallName = Path.GetFileNameWithoutExtension(definition.Uninstaller);
                string uninstallSource = Path.Combine(work, "UninstallerSource");
                CopyDirectory(project, uninstallSource);
                string uninstallStaging = Path.Combine(work, "UninstallerPublished");
                log("アンインストーラーを生成しています…");
                await PublishAsync(Path.Combine(uninstallSource, "src", "Hsis.App", "Hsis.App.csproj"), uninstallStaging, uninstallName, true, false).ConfigureAwait(false);
                File.Copy(Path.Combine(uninstallStaging, uninstallName + ".exe"), Path.Combine(work, "Uninstaller.exe"));
            }
            string staging = Path.Combine(work, "Published");
            log("セットアップを生成しています。初回は依存ファイルの取得に時間がかかります…");
            await PublishAsync(projectFile, staging, Path.GetFileNameWithoutExtension(outputName), false, hasUninstaller).ConfigureAwait(false);
            async Task PublishAsync(string sourceProject, string publishDirectory, string name, bool uninstall, bool embedUninstaller)
            {
                // The final executable name must be used at compilation. Renaming a WinUI
                // single-file executable afterwards breaks its resource-map lookup at startup.
                await ProcessRunner.RunAsync("dotnet", ["publish", sourceProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", publishDirectory,
                    "-p:PublishSingleFile=true", $"-p:HsisPackageDirectory={work}", $"-p:HsisOutputName={name}", $"-p:HsisUninstaller={uninstall}", $"-p:HsisHasUninstaller={embedUninstaller}"],
                    Path.GetDirectoryName(sourceProject)!, log, cancellationToken).ConfigureAwait(false);
            }
            string destination = Path.Combine(Path.GetFullPath(output), $"Setup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(destination);
            CopyDirectory(staging, destination);
            string setup = Path.Combine(destination, outputName);
            if (!File.Exists(setup)) throw new InvalidOperationException($"{outputName} が生成されませんでした。");
            await SetupArchive.AppendAsync(setup, payload, hasUninstaller ? Path.Combine(work, "Uninstaller.exe") : null, cancellationToken).ConfigureAwait(false);
            log($"作成完了: {setup}");
            return setup;
        }
        finally
        {
            try { Directory.Delete(work, true); }
            catch (IOException) { log($"一時ファイルを削除できませんでした: {work}"); }
            catch (UnauthorizedAccessException) { log($"一時ファイルを削除できませんでした: {work}"); }
        }
    }

    public static InstallPlan PreparePlan(InstallPlan plan, string work)
    {
        List<InstallFile> files = [.. plan.Files];
        List<ScriptOperation> install = [.. plan.Install];
        List<ScriptOperation> remove = [.. plan.Uninstall];
        PackageDefinition definition = plan.Definition;
        string png = Path.Combine(work, "AppIcon.png");
        string? executable = ApplicationIcon.FindExecutable(plan, definition);
        InstallFile? application = files.FirstOrDefault(file => file.Source == executable);
        if (application is not null && File.Exists(png))
        {
            string relative = @".hsis\Application.ico";
            if (files.Any(file => file.Destination.Equals(relative, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(".hsis\\Application.ico は HSIS が使用します。");
            string ico = Path.Combine(work, "Application.ico");
            byte[] image = File.ReadAllBytes(png);
            using (BinaryWriter writer = new(File.Create(ico)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(image.Length); writer.Write(22); writer.Write(image);
            }
            files.Add(new(ico, relative));
            install = install.Select(operation => operation.Command == "createshortcut"
                && operation.Arguments[1].Equals(@"$INSTDIR\" + application.Destination, StringComparison.OrdinalIgnoreCase)
                ? new ScriptOperation("createshortcut", [.. operation.Arguments.Take(2), @"$INSTDIR\" + relative]) : operation).ToList();
            // Apps & features reads DisplayIcon independently of shortcut icons.
            IEnumerable<ScriptOperation> registrations = install.Where(operation => operation.Command == "writeregstr"
                && operation.Arguments[1].StartsWith(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase)
                && operation.Arguments[2].Equals("DisplayName", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (ScriptOperation registration in registrations)
                install.Add(new("writeregstr", [registration.Arguments[0], registration.Arguments[1], "DisplayIcon", @"$INSTDIR\" + relative]));
            remove.Add(new("delete", [@"$INSTDIR\" + relative]));
            remove.Add(new("rmdir", [@"$INSTDIR\.hsis"]));
        }
        if (definition.Uninstaller.Length == 0)
        {
            string name = "Uninstall.exe";
            if (files.Any(file => file.Destination.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Uninstall.exe はアンインストーラーが使用します。WriteUninstaller で別の名前を指定してください。");
            definition = definition with { Uninstaller = name };
            install.Add(new("writeuninstaller", [@"$INSTDIR\" + name]));
            if (plan.Uninstall.Count == 0)
            {
                foreach (InstallFile file in files) remove.Add(new("delete", [@"$INSTDIR\" + file.Destination]));
                foreach (ScriptOperation shortcut in install.Where(operation => operation.Command == "createshortcut"))
                    remove.Add(new("delete", [shortcut.Arguments[0]]));
                IEnumerable<string> directories = files.Select(file => Path.GetDirectoryName(file.Destination) ?? "")
                    .Concat(plan.Directories);
                HashSet<string> parents = new(StringComparer.OrdinalIgnoreCase);
                foreach (string directory in directories)
                    for (string? parent = directory; !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent)) parents.Add(parent);
                foreach (string directory in parents.OrderByDescending(path => path.Length)) remove.Add(new("rmdir", [@"$INSTDIR\" + directory]));
            }
            remove.Add(new("delete", [@"$INSTDIR\" + name]));
            remove.Add(new("rmdir", ["$INSTDIR"]));
        }
        return plan with { Definition = definition, Files = files, Install = install, Uninstall = remove };
    }

    public static async Task CreatePayloadAsync(InstallPlan plan, string output, Action<string> log, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.Create(output);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create, true);
        List<PayloadFile> files = [];
        for (int index = 0; index < plan.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallFile file = plan.Files[index];
            string entryName = $"files/{index}";
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
            await using (Stream entryStream = entry.Open())
            await using (FileStream input = File.OpenRead(file.Source))
                await input.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            files.Add(new(entryName, file.Destination));
            log($"追加: {file.Source}");
        }
        ZipArchiveEntry metadata = archive.CreateEntry("manifest.json");
        await using Stream metadataStream = metadata.Open();
        await JsonSerializer.SerializeAsync(metadataStream, new PayloadManifest(files, plan.Directories) { Initialize = plan.Initialize, Install = plan.Install, Uninstall = plan.Uninstall }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
