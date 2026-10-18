namespace Pipp.Core.Formats;

/// <summary>
/// CRC-32 as used by PNG chunks (the standard IEEE 802.3 polynomial 0xEDB88320, initial value
/// 0xFFFFFFFF, final XOR 0xFFFFFFFF). The table is built once so chunk writes and reads do not
/// allocate per byte.
/// </summary>
public static class PngCrc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>Computes the CRC of a complete buffer.</summary>
    /// <param name="data">Bytes to checksum.</param>
    /// <returns>The PNG CRC-32 value.</returns>
    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Initial, data));

    /// <summary>Computes the CRC of two consecutive buffers without concatenating them.</summary>
    /// <param name="first">First byte run (typically the chunk type).</param>
    /// <param name="second">Second byte run (typically the chunk payload).</param>
    /// <returns>The PNG CRC-32 value.</returns>
    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        => Finish(Update(Update(Initial, first), second));

    /// <summary>Initial CRC register value.</summary>
    public const uint Initial = 0xFFFFFFFFu;

    /// <summary>XORs the CRC register with 0xFFFFFFFF to produce the wire value.</summary>
    /// <param name="crc">Running register from <see cref="Update"/>.</param>
    /// <returns>The final CRC.</returns>
    public static uint Finish(uint crc) => ~crc;

    /// <summary>Folds more bytes into a running CRC register.</summary>
    /// <param name="crc">Current register (start with <see cref="Initial"/>).</param>
    /// <param name="data">Bytes to fold in.</param>
    /// <returns>The updated register, before <see cref="Finish"/>.</returns>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
