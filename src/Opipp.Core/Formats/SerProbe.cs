using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>Metadata extracted from a SER file without decoding its frames.</summary>
public sealed class SerInfo
{
    internal SerInfo(string? sourcePath, SerHeader header, long? fileSizeBytes)
    {
        SourcePath = sourcePath;
        Header = header;
        FileSizeBytes = fileSizeBytes;
        ExpectedFileSizeBytes = header.ExpectedFileSize;
        if (fileSizeBytes is long size)
        {
            IsTruncated = size < ExpectedFileSizeBytes;
            ExtraBytes = Math.Max(0, size - ExpectedFileSizeBytes);
        }
    }

    /// <summary>File path, when probed from a path; otherwise null.</summary>
    public string? SourcePath { get; }

    /// <summary>The complete parsed header.</summary>
    public SerHeader Header { get; }

    /// <summary>File size in bytes, or null for non-seekable streams.</summary>
    public long? FileSizeBytes { get; }

    /// <summary>Minimum file size implied by the header (178 + frames).</summary>
    public long ExpectedFileSizeBytes { get; }

    /// <summary>True when the file is shorter than its header's declared frame data.</summary>
    public bool IsTruncated { get; }

    /// <summary>Bytes after the last frame (for example an SER v4 timestamp trailer).</summary>
    public long ExtraBytes { get; }

    /// <summary>Frame width in pixels.</summary>
    public int Width => Header.Width;

    /// <summary>Frame height in pixels.</summary>
    public int Height => Header.Height;

    /// <summary>Interleaved channel count (1 for mono/Bayer, 3 for RGB/BGR).</summary>
    public int Channels => Header.Channels;

    /// <summary>Bits per sample from the header.</summary>
    public int BitDepth => Header.BitDepth;

    /// <summary>Number of frames declared by the header.</summary>
    public int FrameCount => Header.FrameCount;

    /// <summary>Raw SER ColorID.</summary>
    public int ColorId => Header.ColorId;

    /// <summary>Parsed SER ColorID.</summary>
    public SerColorId Color => Header.Color;

    /// <summary>Bayer mosaic pattern, or <see cref="BayerPattern.None"/>.</summary>
    public BayerPattern BayerPattern => Header.Bayer;

    /// <summary>True for three-channel RGB/BGR files.</summary>
    public bool IsColour => Header.IsColour;

    /// <summary>True when ColorID, dimensions, depth and frame count are all supported.</summary>
    public bool IsSupported => Header.IsStructurallyValid;

    /// <summary>Local timestamp of the first frame, or null when absent.</summary>
    public DateTime? LocalTime => Header.LocalTime;

    /// <summary>UTC timestamp of the first frame, or null when absent.</summary>
    public DateTime? UtcTime => Header.UtcTime;

    /// <summary>Derived local-minus-UTC offset in minutes (0 when timestamps are absent).</summary>
    public int LocalTimeUtcOffsetMinutes => Header.LocalTimeUtcOffsetMinutes;

    /// <summary>True when either header timestamp is present.</summary>
    public bool HasTimestamps => Header.LocalTimeTicks != 0 || Header.UtcTicks != 0;

    /// <summary>Observer metadata (may be empty).</summary>
    public string Observer => Header.Observer;

    /// <summary>Telescope metadata (may be empty).</summary>
    public string Telescope => Header.Telescope;

    /// <summary>Instrument metadata (may be empty).</summary>
    public string Instrument => Header.Instrument;

    /// <summary>Camera id from the header.</summary>
    public int LuId => Header.LuId;

    /// <summary>True when the frame samples are stored little-endian.</summary>
    public bool LittleEndian => Header.LittleEndian;
}

/// <summary>Reads SER metadata without decoding frame pixels.</summary>
public static class SerProbe
{
    /// <summary>Probes a SER file on disk.</summary>
    /// <param name="path">Path to the SER file.</param>
    /// <returns>Header metadata, including truncation status and timestamps.</returns>
    /// <exception cref="InvalidDataException">The file is not a SER file or is shorter than the 178-byte header.</exception>
    public static SerInfo Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Probe(stream, path);
    }

    /// <summary>
    /// Probes a SER stream. The stream is restored to its original position when seekable; otherwise it is
    /// left immediately after the header.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of a SER header.</param>
    /// <param name="sourcePath">Optional path recorded in the result.</param>
    public static SerInfo Probe(Stream stream, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long start = stream.CanSeek ? stream.Position : 0;
        SerHeader header = SerHeader.Read(stream);
        long? size = stream.CanSeek ? stream.Length - start : null;

        if (stream.CanSeek)
            stream.Position = start;

        return new SerInfo(sourcePath, header, size);
    }
}
