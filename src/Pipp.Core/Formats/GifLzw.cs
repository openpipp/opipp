using System.Text;

namespace Pipp.Core.Formats;

/// <summary>
/// GIF variant of LZW compression: variable-width codes packed least-significant-bit first, with a
/// clear code when the dictionary fills and an end-of-information code at the end.
/// </summary>
/// <remarks>
/// The stream is returned already framed as GIF data sub-blocks (length-prefixed chunks of at most
/// 255 bytes) and terminated with a zero-length block, so callers only need to write the minimum
/// code size byte followed by this buffer.
/// </remarks>
public static class GifLzw
{
    /// <summary>Largest code width the format allows.</summary>
    public const int MaxCodeWidth = 12;

    /// <summary>Dictionary size at which the encoder emits a clear code.</summary>
    public const int ResetThreshold = 1 << MaxCodeWidth;

    /// <summary>
    /// Compresses <paramref name="indices"/> into GIF image data sub-blocks.
    /// </summary>
    /// <param name="indices">Palette indices, one byte per pixel, row-major.</param>
    /// <param name="minCodeSize">Minimum code width; 2..8 for GIF (8 for a 256-entry palette).</param>
    public static byte[] Encode(ReadOnlySpan<byte> indices, int minCodeSize)
    {
        if (minCodeSize is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(minCodeSize), minCodeSize, "GIF minimum code size must be between 2 and 8.");
        }

        var writer = new BitWriter(indices.Length / 2 + 64);
        var dictionary = new Dictionary<int, int>(4096);

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int nextCode = endCode + 1;
        int codeWidth = minCodeSize + 1;

        writer.Write(clearCode, codeWidth);

        if (indices.Length == 0)
        {
            writer.Write(endCode, codeWidth);
            return Frame(writer.Finish());
        }

        dictionary.Clear();
        int prefix = indices[0];

        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = (prefix << 8) | k;
            if (dictionary.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            writer.Write(prefix, codeWidth);
            dictionary[key] = nextCode++;

            if (nextCode > (1 << codeWidth) && codeWidth < MaxCodeWidth)
            {
                codeWidth++;
            }
            else if (nextCode >= ResetThreshold)
            {
                writer.Write(clearCode, codeWidth);
                dictionary.Clear();
                nextCode = endCode + 1;
                codeWidth = minCodeSize + 1;
            }

            prefix = k;
        }

        writer.Write(prefix, codeWidth);
        writer.Write(endCode, codeWidth);
        return Frame(writer.Finish());
    }

    /// <summary>Frames the packed stream as length-prefixed sub-blocks plus the terminating zero block.</summary>
    private static byte[] Frame(byte[] packed)
    {
        var output = new List<byte>(packed.Length + (packed.Length / 255) + 2);
        int offset = 0;
        while (offset < packed.Length)
        {
            int count = Math.Min(255, packed.Length - offset);
            output.Add((byte)count);
            output.AddRange(packed.AsSpan(offset, count).ToArray());
            offset += count;
        }

        output.Add(0);
        return output.ToArray();
    }

    /// <summary>Accumulates codes of varying width into a byte stream, least significant bit first.</summary>
    private sealed class BitWriter(int capacity)
    {
        private readonly List<byte> _bytes = new(capacity);
        private int _buffer;
        private int _bits;

        public void Write(int code, int width)
        {
            _buffer |= code << _bits;
            _bits += width;

            while (_bits >= 8)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer >>= 8;
                _bits -= 8;
            }
        }

        public byte[] Finish()
        {
            if (_bits > 0)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer = 0;
                _bits = 0;
            }

            return _bytes.ToArray();
        }
    }
}
