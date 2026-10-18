using System.Buffers.Binary;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>
/// Reader and writer for the engine's private master-frame container: the <c>pipp_master_*.tmp</c>
/// files the GUI passes as <c>-dark_tmp</c>, <c>-flat_tmp</c> and <c>-flat_dark_tmp</c>.
/// </summary>
/// <remarks>
/// The format has no magic number. It is a 40-byte little-endian header followed by a raw raster:
/// <code>
/// offset size field
///      0    4 width  (pixels)
///      4    4 height (pixels)
///      8    4 colour flag: 0 = single plane, 1 = three channels
///     12    4 bytes per sample: 1 = 8-bit, 2 = 16-bit little-endian
///     16    4 maximum sample value (255 or 65535)
///     20   20 reserved, zero
/// </code>
/// The raster is stored the way the engine keeps frames in memory: rows bottom-up (the first row in
/// the file is the bottom row of the image) and, for colour, in B,G,R order. Only
/// <c>pipp_dll!stack_frames</c> (FUN_1400b3370 is the matching loader in <c>pipp_core.exe</c>) writes
/// it; the loader reads the header, allocates the image and <c>fread</c>s the raster straight into the
/// frame buffer, ignoring <c>maxval</c> and the reserved words.
/// </remarks>
public static class MasterTmpFile
{
    /// <summary>Size of the fixed header that precedes the raster, in bytes.</summary>
    public const int HeaderSize = 40;

    /// <summary>Value of the colour-flag field for a three-channel (B,G,R) master.</summary>
    private const uint ColourFlag = 1;

    /// <summary>Upper bound applied to the declared geometry, to reject a mis-typed header.</summary>
    private const int MaxDimension = 1 << 17;

    /// <summary>
    /// Reads a <c>.tmp</c> master into a frame, undoing the engine's storage layout: rows are flipped
    /// back to top-down order and colour samples are reordered from B,G,R to the frame's R,G,B.
    /// </summary>
    /// <param name="path">Master file produced by <c>stack_frames</c> or <see cref="Write"/>.</param>
    /// <returns>The master as a top-down frame at the depth recorded in the header.</returns>
    /// <exception cref="InvalidDataException">The header or raster is not a valid master.</exception>
    public static Frame Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllBytes(path));
    }

    /// <summary>Reads a <c>.tmp</c> master from memory; see <see cref="Read(string)"/>.</summary>
    /// <exception cref="InvalidDataException">The header or raster is not a valid master.</exception>
    public static Frame Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"master header is {data.Length} bytes, expected {HeaderSize}");

        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        uint colour = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        uint bytesPerSample = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);

        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
            throw new InvalidDataException($"master has invalid dimensions {width}x{height}");
        if (colour is not (0 or ColourFlag))
            throw new InvalidDataException($"master has an unknown colour flag {colour}");
        if (bytesPerSample is not (1 or 2))
            throw new InvalidDataException($"master has an unknown sample size {bytesPerSample}");

        int channels = colour == ColourFlag ? 3 : 1;
        int bitDepth = bytesPerSample == 2 ? 16 : 8;
        long required = (long)width * height * channels * bytesPerSample;
        if (data.Length - HeaderSize < required)
            throw new InvalidDataException(
                $"master raster is truncated: {data.Length - HeaderSize} bytes present, {required} expected");

        var samples = new ushort[checked(width * height * channels)];
        int source = HeaderSize;

        for (int fileRow = 0; fileRow < height; fileRow++)
        {
            // The engine stores row 0 as the bottom of the image.
            int destination = (height - 1 - fileRow) * width * channels;

            for (int x = 0; x < width; x++)
            {
                if (channels == 1)
                {
                    samples[destination++] = bytesPerSample == 1
                        ? data[source]
                        : BinaryPrimitives.ReadUInt16LittleEndian(data[source..]);
                    source += (int)bytesPerSample;
                }
                else
                {
                    // Stored B,G,R; a frame is interleaved R,G,B.
                    ushort blue = Sample(data, source, bytesPerSample);
                    ushort green = Sample(data, source + (int)bytesPerSample, bytesPerSample);
                    ushort red = Sample(data, source + 2 * (int)bytesPerSample, bytesPerSample);
                    samples[destination++] = red;
                    samples[destination++] = green;
                    samples[destination++] = blue;
                    source += 3 * (int)bytesPerSample;
                }
            }
        }

        return new Frame(width, height, channels, bitDepth, samples);
    }

    /// <summary>
    /// Writes a frame in the engine's master layout, mirroring <c>pipp_dll!stack_frames</c>: the
    /// header records the geometry, channel count and sample size, and the raster is emitted
    /// bottom-up with colour samples in B,G,R order.
    /// </summary>
    /// <param name="frame">Master frame; any bit depth or channel count the frame class allows.</param>
    /// <param name="path">Destination path; parent directories are created when missing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static void Write(Frame frame, string path)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        Write(frame, stream, leaveOpen: true);
    }

    /// <summary>Writes a frame in the engine's master layout to a stream.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="stream"/> is null.</exception>
    public static void Write(Frame frame, Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(stream);

        int bytesPerSample = frame.BitDepth == 16 ? 2 : 1;
        int channels = frame.Channels;

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)frame.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)frame.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), channels == 3 ? ColourFlag : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)bytesPerSample);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), bytesPerSample == 2 ? 65535u : 255u);
        // bytes 20..39 stay zero, matching the engine's reserved words.

        stream.Write(header);

        // Emit one file row at a time (bottom row first) to keep the working buffer small.
        var row = new byte[frame.Width * channels * bytesPerSample];
        for (int y = frame.Height - 1; y >= 0; y--)
        {
            int source = y * frame.Width * channels;
            int destination = 0;
            for (int x = 0; x < frame.Width; x++)
            {
                if (channels == 1)
                {
                    PutSample(row, destination, frame.Samples[source++], bytesPerSample);
                    destination += bytesPerSample;
                }
                else
                {
                    ushort red = frame.Samples[source++];
                    ushort green = frame.Samples[source++];
                    ushort blue = frame.Samples[source++];
                    PutSample(row, destination, blue, bytesPerSample);
                    PutSample(row, destination + bytesPerSample, green, bytesPerSample);
                    PutSample(row, destination + 2 * bytesPerSample, red, bytesPerSample);
                    destination += 3 * bytesPerSample;
                }
            }

            stream.Write(row, 0, row.Length);
        }

        if (!leaveOpen) stream.Dispose();
    }

    private static ushort Sample(ReadOnlySpan<byte> data, int offset, uint bytesPerSample) =>
        bytesPerSample == 1 ? data[offset] : BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static void PutSample(byte[] buffer, int offset, ushort value, int bytesPerSample)
    {
        if (bytesPerSample == 1)
        {
            buffer[offset] = (byte)Math.Min(value, (ushort)255);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);
        }
    }
}
