using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Hsis.Core;

internal sealed class PayloadArchive : IDisposable
{
    internal static ReadOnlySpan<byte> Magic => "HSIS7Z01"u8;
    private readonly Stream _source;
    private readonly ZipArchive? _zip;
    private string? _temporary;
    public PayloadManifest Manifest { get; }

    private PayloadArchive(Stream source, PayloadManifest manifest, ZipArchive? zip) { _source = source; Manifest = manifest; _zip = zip; }

    public static async Task<PayloadArchive> OpenAsync(Stream source, CancellationToken cancellationToken = default)
    {
        byte[] magic = new byte[8];
        await source.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        if (magic.AsSpan().SequenceEqual(Magic))
        {
            byte[] size = new byte[4];
            await source.ReadExactlyAsync(size, cancellationToken).ConfigureAwait(false);
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(size);
            if (length <= 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("配置情報の長さが不正です。");
            byte[] metadata = new byte[length];
            await source.ReadExactlyAsync(metadata, cancellationToken).ConfigureAwait(false);
            PayloadManifest manifest = JsonSerializer.Deserialize<PayloadManifest>(metadata) ?? throw new InvalidDataException("配置情報が不正です。");
            for (int index = 0; index < manifest.Files.Count; index++)
                if (manifest.Files[index].Entry != $"files/{index}") throw new InvalidDataException("圧縮データのファイル名が不正です。");
            return new(source, manifest, null);
        }
        source.Position -= magic.Length;
        ZipArchive zip = new(source, ZipArchiveMode.Read, true);
        try
        {
            await using Stream metadata = (zip.GetEntry("manifest.json") ?? throw new InvalidDataException("配置情報がありません。")).Open();
            PayloadManifest manifest = await JsonSerializer.DeserializeAsync<PayloadManifest>(metadata, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("配置情報が不正です。");
            foreach (PayloadFile file in manifest.Files)
                if (zip.GetEntry(file.Entry) is null) throw new InvalidDataException($"ファイルがありません: {file.Entry}");
            return new(source, manifest, zip);
        }
        catch { zip.Dispose(); throw; }
    }

    public async Task ExtractAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (_zip is not null || Manifest.Files.Count == 0) return;
        progress?.Report("圧縮データを展開しています。時間がかかる場合があります。そのままお待ちください。");
        _temporary = Path.Combine(Path.GetTempPath(), "Hsis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporary);
        string archive = Path.Combine(_temporary, "Payload.7z");
        await using (FileStream output = File.Create(archive)) await _source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        string files = Path.Combine(_temporary, "selected.txt");
        await File.WriteAllLinesAsync(files, Manifest.Files.Select(file => file.Entry), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await SevenZip.ExtractAsync(archive, _temporary, files, _ => { }, cancellationToken).ConfigureAwait(false);
        foreach (PayloadFile file in Manifest.Files)
        {
            string path = Path.Combine(_temporary, file.Entry);
            InstallationEngine.RejectLinks(path);
            if (!File.Exists(path)) throw new InvalidDataException($"ファイルがありません: {file.Entry}");
        }
    }

    public Stream OpenFile(string entry) => _zip is not null
        ? (_zip.GetEntry(entry) ?? throw new InvalidDataException($"ファイルがありません: {entry}")).Open()
        : File.OpenRead(Path.Combine(_temporary ?? throw new InvalidOperationException("展開されていません。"), entry));

    public void Dispose()
    {
        _zip?.Dispose();
        if (_temporary is null) return;
        try { Directory.Delete(_temporary, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }
}
