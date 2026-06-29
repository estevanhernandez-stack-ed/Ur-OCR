// Ipc/FrameCodec.cs
using System.Buffers.Binary;
using System.IO;
namespace RoRoRo.UrOcr.Ipc;

internal static class FrameCodec
{
    public const int MaxFrameBytes = 64 * 1024;

    public static async Task WriteFrameAsync(Stream s, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > MaxFrameBytes) throw new InvalidDataException($"Frame too large: {payload.Length}.");
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, payload.Length);
        await s.WriteAsync(len, ct).ConfigureAwait(false);
        await s.WriteAsync(payload, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var lenBuf = await ReadExactAsync(s, 4, ct).ConfigureAwait(false);
        if (lenBuf is null) return null;
        int len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (len < 0 || len > MaxFrameBytes) throw new InvalidDataException($"Bad frame length: {len}.");
        var payload = await ReadExactAsync(s, len, ct).ConfigureAwait(false);
        if (payload is null) throw new EndOfStreamException("Truncated frame.");
        return payload;
    }

    private static async Task<byte[]?> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        if (count == 0) return Array.Empty<byte>();
        var buf = new byte[count]; int read = 0;
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0) return read == 0 ? null : throw new EndOfStreamException();
            read += n;
        }
        return buf;
    }
}
