namespace Lex.V3.Artifacts;

internal static class BoundedStreamReader
{
    public static async ValueTask<BoundedReadResult> ReadAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        var buffer = GC.AllocateUninitializedArray<byte>(maximumBytes);
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count == maximumBytes)
        {
            var overflowProbe = new byte[1];
            if (await stream.ReadAsync(overflowProbe, cancellationToken).ConfigureAwait(false) != 0)
            {
                return BoundedReadResult.TooLarge();
            }

            return BoundedReadResult.FromBytes(buffer);
        }

        return BoundedReadResult.FromBytes(buffer.AsMemory(0, count).ToArray());
    }
}

internal sealed record BoundedReadResult(bool ExceededLimit, byte[] Bytes)
{
    public static BoundedReadResult TooLarge() => new(true, Array.Empty<byte>());

    public static BoundedReadResult FromBytes(byte[] bytes) => new(false, bytes);
}
