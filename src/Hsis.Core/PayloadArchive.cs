using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Hsis.Core;

internal sealed class PayloadArchive : IDisposable
{
    internal static ReadOnlySpan<byte> Magic => "HSIS7Z01"u8;
    internal static ReadOnlySpan<byte> SharedMagic => "HSIS7Z02"u8;
    private readonly Stream _source;
    private readonly ZipArchive? _zip;
    private string? _temporary;
    private readonly string _runtimeDirectory;
    public PayloadManifest Manifest { get; }

    private PayloadArchive(Stream source, PayloadManifest manifest, ZipArchive? zip, string? runtimeDirectory = null)
    { _source = source; Manifest = manifest; _zip = zip; _runtimeDirectory = Path.GetFullPath(runtimeDirectory ?? AppContext.BaseDirectory); }

    public static async Task<PayloadArchive> OpenAsync(Stream source, CancellationToken cancellationToken = default, string? runtimeDirectory = null)
    {
        byte[] magic = new byte[8];
        await source.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        bool shared = magic.AsSpan().SequenceEqual(SharedMagic);
        if (shared || magic.AsSpan().SequenceEqual(Magic))
        {
            byte[] size = new byte[4];
            await source.ReadExactlyAsync(size, cancellationToken).ConfigureAwait(false);
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(size);
            if (length <= 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("配置情報の長さが不正です。");
            byte[] metadata = new byte[length];
            await source.ReadExactlyAsync(metadata, cancellationToken).ConfigureAwait(false);
            PayloadManifest manifest = JsonSerializer.Deserialize<PayloadManifest>(metadata) ?? throw new InvalidDataException("配置情報が不正です。");
            for (int index = 0; index < manifest.Files.Count; index++)
            {
                if (manifest.Files[index].Entry != $"files/{index}") throw new InvalidDataException("圧縮データのファイル名が不正です。");
                PayloadFile file = manifest.Files[index];
                if ((file.RuntimePath is not null || file.Sha256 is not null) && (!shared || file.RuntimePath is null
                    || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)))
                    throw new InvalidDataException("共有ランタイムの参照が不正です。");
            }
            return new(source, manifest, null, runtimeDirectory);
        }
        source.Position -= magic.Length;
        ZipArchive zip = new(source, ZipArchiveMode.Read, true);
        try
        {
            await using Stream metadata = (zip.GetEntry("manifest.json") ?? throw new InvalidDataException("配置情報がありません。")).Open();
            PayloadManifest manifest = await JsonSerializer.DeserializeAsync<PayloadManifest>(metadata, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("配置情報が不正です。");
            foreach (PayloadFile file in manifest.Files)
            {
                if (file.RuntimePath is not null || file.Sha256 is not null) throw new InvalidDataException("ZIP 形式では共有ランタイムを参照できません。");
                if (zip.GetEntry(file.Entry) is null) throw new InvalidDataException($"ファイルがありません: {file.Entry}");
            }
            return new(source, manifest, zip);
        }
        catch { zip.Dispose(); throw; }
    }

    public async Task ExtractAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (_zip is not null || Manifest.Files.Count == 0) return;
        foreach (PayloadFile file in Manifest.Files.Where(file => file.RuntimePath is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Stream verified = OpenRuntimeFile(file);
        }
        PayloadFile[] stored = Manifest.Files.Where(file => file.RuntimePath is null).ToArray();
        if (stored.Length == 0) return;
        progress?.Report("圧縮データを展開しています。時間がかかる場合があります。そのままお待ちください。");
        _temporary = Path.Combine(Path.GetTempPath(), "Hsis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporary);
        string archive = Path.Combine(_temporary, "Payload.7z");
        await using (FileStream output = File.Create(archive)) await _source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        string files = Path.Combine(_temporary, "selected.txt");
        await File.WriteAllLinesAsync(files, stored.Select(file => file.Entry), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await SevenZip.ExtractAsync(archive, _temporary, files, _ => { }, cancellationToken).ConfigureAwait(false);
        foreach (PayloadFile file in stored)
        {
            string path = Path.Combine(_temporary, file.Entry);
            InstallationEngine.RejectLinks(path);
            if (!File.Exists(path)) throw new InvalidDataException($"ファイルがありません: {file.Entry}");
        }
    }

    public Stream OpenFile(string entry)
    {
        PayloadFile? shared = Manifest.Files.FirstOrDefault(file => file.Entry == entry && file.RuntimePath is not null);
        if (shared is not null) return OpenRuntimeFile(shared);
        return _zip is not null
        ? (_zip.GetEntry(entry) ?? throw new InvalidDataException($"ファイルがありません: {entry}")).Open()
        : File.OpenRead(Path.Combine(_temporary ?? throw new InvalidOperationException("展開されていません。"), entry));
    }

    private Stream OpenRuntimeFile(PayloadFile file)
    {
        string relative = file.RuntimePath!;
        string path = Path.GetFullPath(Path.Combine(_runtimeDirectory, relative));
        if (Path.IsPathRooted(relative) || relative.Contains(':')
            || !path.StartsWith(Path.TrimEndingDirectorySeparator(_runtimeDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("共有ランタイムの参照先が範囲外です。");
        InstallationEngine.RejectLinks(path);
        FileStream input = File.OpenRead(path);
        try
        {
            string hash = Convert.ToHexString(SHA256.HashData(input));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"共有ランタイムの内容が一致しません: {relative}");
            input.Position = 0;
            return input;
        }
        catch { input.Dispose(); throw; }
    }

    public void Dispose()
    {
        _zip?.Dispose();
        if (_temporary is null) return;
        try { Directory.Delete(_temporary, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }
}
