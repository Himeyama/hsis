using System.Text;

namespace Hsis.Core;

/// <summary>Installation data stored after the .NET bundle, outside startup extraction.</summary>
public static class SetupArchive
{
    private const string Magic = "HSISPKG1";
    private const int FooterSize = 40;

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
