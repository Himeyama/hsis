using System.Text.Json;
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
            string payload = Path.Combine(work, "Payload.bin");
            await CreatePayloadAsync(plan, payload, log, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(work, "Package.json"), JsonSerializer.Serialize(definition), cancellationToken).ConfigureAwait(false);
            string project = Path.Combine(work, "Source");
            CopyDirectory(template, project);
            string applicationIcon = Path.Combine(work, "Application.ico");
            if (File.Exists(applicationIcon))
                File.Copy(applicationIcon, Path.Combine(project, "src", "Hsis.App", "Hsis.ico"), true);
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
            InstallPlan uninstallPlan = plan with { Files = [], Directories = [], Install = [] };
            await CreatePayloadAsync(uninstallPlan, Path.Combine(work, "UninstallerPayload.bin"), log, cancellationToken).ConfigureAwait(false);
            string staging = Path.Combine(work, "Published");
            log("共通のセットアップ画面とランタイムを生成しています…");
            // One UI/runtime serves both modes. Keep the compiled WinUI filename intact.
            await ProcessRunner.RunAsync("dotnet", ["publish", projectFile, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", staging,
                "-p:PublishSingleFile=false", $"-p:HsisPackageDirectory={work}", "-p:HsisOutputName=Hsis.Setup"],
                Path.GetDirectoryName(projectFile)!, log, cancellationToken).ConfigureAwait(false);
            string runtime = Path.Combine(work, "Runtime.7z");
            log("共通ランタイムを 7z / LZMA2（高圧縮）で圧縮しています…");
            await SevenZip.CreateAsync(staging, runtime, log, cancellationToken).ConfigureAwait(false);
            string launcher = await NativeLauncher.BuildAsync(project, work, log, cancellationToken).ConfigureAwait(false);
            string destination = Path.Combine(Path.GetFullPath(output), $"Setup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(destination);
            string setup = Path.Combine(destination, outputName);
            File.Copy(launcher, setup);
            int depth = (Path.GetDirectoryName(definition.Uninstaller) ?? "").Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
            await SetupArchive.AppendNativeAsync(setup, runtime, payload, depth, cancellationToken).ConfigureAwait(false);
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
        string staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "files"));
        string compressed = staging + ".7z";
        try
        {
            List<PayloadFile> files = [];
            for (int index = 0; index < plan.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InstallFile file = plan.Files[index];
                string entry = $"files/{index}";
                await using (FileStream input = File.OpenRead(file.Source))
                await using (FileStream target = File.Create(Path.Combine(staging, entry)))
                    await input.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                files.Add(new(entry, file.Destination));
                log($"追加: {file.Source}");
            }
            byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new PayloadManifest(files, plan.Directories)
                { Initialize = plan.Initialize, Install = plan.Install, Uninstall = plan.Uninstall });
            if (files.Count > 0)
            {
                log("7z / LZMA2（高圧縮）で圧縮しています…");
                await SevenZip.CreateAsync(staging, compressed, log, cancellationToken).ConfigureAwait(false);
            }
            await using FileStream stream = File.Create(output);
            await stream.WriteAsync(PayloadArchive.Magic.ToArray(), cancellationToken).ConfigureAwait(false);
            byte[] length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, metadata.Length);
            await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(metadata, cancellationToken).ConfigureAwait(false);
            if (files.Count > 0)
            {
                await using FileStream input = File.OpenRead(compressed);
                await input.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { Directory.Delete(staging, true); if (File.Exists(compressed)) File.Delete(compressed); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
