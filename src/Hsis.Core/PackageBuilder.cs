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
        if (!File.Exists(Path.Combine(template, "src", "Hsis.App", "Hsis.App.csproj")))
            throw new DirectoryNotFoundException("ビルド用テンプレートがありません。publish 出力から起動してください。");
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new InvalidOperationException("アプリ名を入力してください。");
        if (string.IsNullOrWhiteSpace(definition.InstallDirectory) || !Path.IsPathFullyQualified(InstallPaths.Expand(definition.InstallDirectory)))
            throw new InvalidOperationException("インストール先には絶対パスを指定してください。");
        string work = Path.Combine(Path.GetTempPath(), "Hsis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
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
            string staging = Path.Combine(work, "Published");
            log("セットアップを生成しています。初回は依存ファイルの取得に時間がかかります…");
            await ProcessRunner.RunAsync("dotnet", ["publish", Path.Combine(project, "src", "Hsis.App", "Hsis.App.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", staging, "-p:PublishSingleFile=true", $"-p:HsisPackageDirectory={work}"], project, log, cancellationToken).ConfigureAwait(false);
            string destination = Path.Combine(Path.GetFullPath(output), $"Setup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(destination);
            CopyDirectory(staging, destination);
            string setup = Path.Combine(destination, Path.GetFileName(plan.OutputFile));
            if (!setup.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("OutFile には .exe を指定してください。");
            string compiledSetup = Path.Combine(destination, "Setup.exe");
            if (!File.Exists(compiledSetup)) throw new InvalidOperationException("Setup.exe が生成されませんでした。");
            if (!string.Equals(compiledSetup, setup, StringComparison.OrdinalIgnoreCase)) File.Move(compiledSetup, setup);
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
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
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
