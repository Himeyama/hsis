using System.Text;

namespace Hsis.Core;

/// <summary>Segments appended to a native launcher, with compatibility for the earlier .NET bundle layout.</summary>
public static class SetupArchive
{
    private const string Magic = "HSISPKG1";
    private const int FooterSize = 40;
    private const int NativeFooterSize = 64;
    private const int SharedFooterSize = 32;
    private sealed record SharedFooter(long BundleLength, long PayloadLength, long Mode);
    private sealed record NativeFooter(long NativeLength, long RuntimeOffset, long RuntimeLength, long PayloadOffset, long PayloadLength, long Mode, long Depth);

    public static async Task AppendSharedAsync(string executable, string payload, CancellationToken cancellationToken = default)
    {
        await using FileStream output = new(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        long bundleLength = output.Length;
        output.Position = bundleLength;
        await using (FileStream input = File.OpenRead(payload)) await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        WriteSharedFooter(output, new(bundleLength, output.Position - bundleLength, 0));
    }

    private static void WriteSharedFooter(Stream output, SharedFooter footer)
    {
        using BinaryWriter writer = new(output, Encoding.UTF8, true);
        writer.Write("HSISPKG3"u8);
        writer.Write(footer.BundleLength); writer.Write(footer.PayloadLength); writer.Write(footer.Mode);
    }

    private static SharedFooter? ReadSharedFooter(FileStream file)
    {
        if (file.Length < SharedFooterSize) return null;
        file.Position = file.Length - SharedFooterSize;
        using BinaryReader reader = new(file, Encoding.UTF8, true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("HSISPKG3"u8)) return null;
        SharedFooter footer = new(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
        long end = file.Length - SharedFooterSize;
        if (footer.BundleLength <= 0 || footer.PayloadLength < 0 || footer.BundleLength > end
            || footer.PayloadLength != end - footer.BundleLength || footer.Mode is not (0 or 1)
            || (footer.Mode == 1 && footer.PayloadLength != 0) || (footer.Mode == 0 && footer.PayloadLength == 0))
            throw new InvalidDataException("セットアップのデータ範囲が不正です。");
        return footer;
    }

    public static bool IsUninstaller(string executable)
    {
        using FileStream file = new(executable, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return ReadSharedFooter(file)?.Mode == 1;
    }

    public static async Task AppendNativeAsync(string executable, string runtime, string payload, int depth, CancellationToken cancellationToken = default)
    {
        await using FileStream output = new(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        long nativeLength = output.Length;
        output.Position = nativeLength;
        await using (FileStream input = File.OpenRead(runtime)) await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        long payloadOffset = output.Position;
        await using (FileStream input = File.OpenRead(payload)) await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        WriteNativeFooter(output, new(nativeLength, nativeLength, payloadOffset - nativeLength, payloadOffset, output.Position - payloadOffset, 0, depth));
    }

    private static void WriteNativeFooter(Stream output, NativeFooter footer)
    {
        using BinaryWriter writer = new(output, Encoding.UTF8, true);
        writer.Write("HSISPKG2"u8);
        writer.Write(footer.NativeLength); writer.Write(footer.RuntimeOffset); writer.Write(footer.RuntimeLength);
        writer.Write(footer.PayloadOffset); writer.Write(footer.PayloadLength); writer.Write(footer.Mode); writer.Write(footer.Depth);
    }

    private static NativeFooter? ReadNativeFooter(FileStream file)
    {
        if (file.Length < NativeFooterSize) return null;
        file.Position = file.Length - NativeFooterSize;
        using BinaryReader reader = new(file, Encoding.UTF8, true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("HSISPKG2"u8)) return null;
        NativeFooter footer = new(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
        long end = file.Length - NativeFooterSize;
        if (footer.NativeLength <= 0 || footer.RuntimeOffset != footer.NativeLength || footer.RuntimeLength <= 0
            || footer.RuntimeOffset > end - footer.RuntimeLength || footer.PayloadOffset != footer.RuntimeOffset + footer.RuntimeLength
            || footer.PayloadLength < 0 || footer.PayloadOffset > end - footer.PayloadLength || footer.PayloadOffset + footer.PayloadLength != end
            || footer.Mode is not (0 or 1) || footer.Depth < 0 || footer.Depth > 100)
            throw new InvalidDataException("セットアップのデータ範囲が不正です。");
        return footer;
    }

    public static async Task AppendAsync(string executable, string payload, string? uninstaller, CancellationToken cancellationToken = default)
    {
        await using FileStream output = new(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        output.Position = output.Length;
        long payloadOffset = output.Position;
        await using (FileStream input = File.OpenRead(payload)) await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        long payloadLength = output.Position - payloadOffset;
        long uninstallOffset = output.Position;
        if (uninstaller is not null)
        {
            await using FileStream input = File.OpenRead(uninstaller);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        long uninstallLength = output.Position - uninstallOffset;
        using BinaryWriter footer = new(output, Encoding.UTF8, true);
        footer.Write(Encoding.ASCII.GetBytes(Magic));
        footer.Write(payloadOffset); footer.Write(payloadLength);
        footer.Write(uninstallOffset); footer.Write(uninstallLength);
    }

    public static Stream OpenPayload(string executable) => Open(executable, false) ?? throw new InvalidDataException("インストールデータがありません。");
    public static Stream? OpenUninstaller(string executable) => Open(executable, true);

    private static Stream? Open(string executable, bool uninstaller)
    {
        FileStream file = new(executable, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            if (ReadSharedFooter(file) is SharedFooter shared)
            {
                if (!uninstaller)
                {
                    if (shared.Mode == 1) throw new InvalidDataException("インストールデータがありません。");
                    return new SegmentStream(file, shared.BundleLength, shared.PayloadLength);
                }
                if (shared.Mode == 1) { file.Dispose(); return null; }
                string temporary = Path.Combine(Path.GetTempPath(), "HsisUninstall-" + Guid.NewGuid().ToString("N"));
                FileStream generated = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.DeleteOnClose);
                try
                {
                    using (SegmentStream bundle = new(file, 0, shared.BundleLength)) bundle.CopyTo(generated);
                    WriteSharedFooter(generated, shared with { PayloadLength = 0, Mode = 1 });
                    generated.Position = 0;
                    return generated;
                }
                catch { generated.Dispose(); throw; }
            }
            if (ReadNativeFooter(file) is NativeFooter native)
            {
                if (!uninstaller) return new SegmentStream(file, native.PayloadOffset, native.PayloadLength);
                if (native.Mode == 1) { file.Dispose(); return null; }
                string temporary = Path.Combine(Path.GetTempPath(), "HsisUninstall-" + Guid.NewGuid().ToString("N"));
                FileStream generated = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.DeleteOnClose);
                try
                {
                    using (SegmentStream content = new(file, 0, native.PayloadOffset)) content.CopyTo(generated);
                    WriteNativeFooter(generated, native with { PayloadLength = 0, Mode = 1 });
                    generated.Position = 0;
                    return generated;
                }
                catch { generated.Dispose(); throw; }
            }
            if (file.Length < FooterSize) throw new InvalidDataException("セットアップのデータ情報がありません。");
            file.Position = file.Length - FooterSize;
            using BinaryReader reader = new(file, Encoding.UTF8, true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != Magic) throw new InvalidDataException("セットアップのデータ形式が不正です。");
            long payloadOffset = reader.ReadInt64();
            long payloadLength = reader.ReadInt64();
            long uninstallOffset = reader.ReadInt64();
            long uninstallLength = reader.ReadInt64();
            long dataEnd = file.Length - FooterSize;
            if (payloadOffset < 0 || payloadLength <= 0 || payloadOffset > dataEnd - payloadLength || uninstallOffset != payloadOffset + payloadLength
                || uninstallLength < 0 || uninstallOffset > dataEnd - uninstallLength || uninstallOffset + uninstallLength != dataEnd)
                throw new InvalidDataException("セットアップのデータ範囲が不正です。");
            if (uninstaller && uninstallLength == 0) { file.Dispose(); return null; }
            return new SegmentStream(file, uninstaller ? uninstallOffset : payloadOffset, uninstaller ? uninstallLength : payloadLength);
        }
        catch { file.Dispose(); throw; }
    }

    private sealed class SegmentStream(FileStream file, long start, long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            file.Position = start + _position;
            int read = file.Read(buffer[..(int)Math.Min(buffer.Length, length - _position)]);
            _position += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            file.Position = start + _position;
            int read = await file.ReadAsync(buffer[..(int)Math.Min(buffer.Length, length - _position)], cancellationToken).ConfigureAwait(false);
            _position += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(_position + offset), SeekOrigin.End => checked(length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (position < 0 || position > length) throw new IOException("データ範囲外への移動です。");
            _position = position;
            return position;
        }
        protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
