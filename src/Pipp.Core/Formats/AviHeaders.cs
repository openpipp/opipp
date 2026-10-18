using System.Buffers.Binary;

namespace Pipp.Core.Formats;

/// <summary>The 56-byte AVI main header (<c>avih</c> chunk data).</summary>
internal struct AviMainHeader
{
    /// <summary>Serialized size of the header, in bytes.</summary>
    public const int Size = 56;

    public uint MicroSecPerFrame;
    public uint MaxBytesPerSec;
    public uint PaddingGranularity;
    public uint Flags;
    public uint TotalFrames;
    public uint InitialFrames;
    public uint Streams;
    public uint SuggestedBufferSize;
    public uint Width;
    public uint Height;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
    public uint Reserved3;

    /// <summary>Parses an <c>avih</c> payload; <paramref name="data"/> must be at least <see cref="Size"/> bytes.</summary>
    public static AviMainHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"AVI main header needs {Size} bytes, got {data.Length}.");

        return new AviMainHeader
        {
            MicroSecPerFrame = BinaryPrimitives.ReadUInt32LittleEndian(data),
            MaxBytesPerSec = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            PaddingGranularity = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
            TotalFrames = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]),
            InitialFrames = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
            Streams = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
            SuggestedBufferSize = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]),
            Width = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]),
            Height = BinaryPrimitives.ReadUInt32LittleEndian(data[36..]),
            Reserved0 = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]),
            Reserved1 = BinaryPrimitives.ReadUInt32LittleEndian(data[44..]),
            Reserved2 = BinaryPrimitives.ReadUInt32LittleEndian(data[48..]),
            Reserved3 = BinaryPrimitives.ReadUInt32LittleEndian(data[52..]),
        };
    }

    /// <summary>Serializes the header; <paramref name="destination"/> must be at least <see cref="Size"/> bytes.</summary>
    public readonly void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"AVI main header needs {Size} bytes, got {destination.Length}.", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination, MicroSecPerFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], MaxBytesPerSec);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], PaddingGranularity);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], TotalFrames);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], InitialFrames);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], Streams);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[28..], SuggestedBufferSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[32..], Width);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[36..], Height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[40..], Reserved0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[44..], Reserved1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[48..], Reserved2);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[52..], Reserved3);
    }
}

/// <summary>The 56-byte AVI stream header (<c>strh</c> chunk data) for a video stream.</summary>
internal struct AviStreamHeader
{
    /// <summary>Serialized size of the header, in bytes.</summary>
    public const int Size = 56;

    public uint Type;
    public uint Handler;
    public uint Flags;
    public ushort Priority;
    public ushort Language;
    public uint InitialFrames;
    public uint Scale;
    public uint Rate;
    public uint Start;
    public uint Length;
    public uint SuggestedBufferSize;
    public uint Quality;
    public uint SampleSize;
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;

    /// <summary>Parses a <c>strh</c> payload; <paramref name="data"/> must be at least <see cref="Size"/> bytes.</summary>
    public static AviStreamHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"AVI stream header needs {Size} bytes, got {data.Length}.");

        return new AviStreamHeader
        {
            Type = BinaryPrimitives.ReadUInt32LittleEndian(data),
            Handler = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            Priority = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            Language = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            InitialFrames = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]),
            Scale = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
            Rate = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
            Start = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]),
            Length = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]),
            SuggestedBufferSize = BinaryPrimitives.ReadUInt32LittleEndian(data[36..]),
            Quality = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]),
            SampleSize = BinaryPrimitives.ReadUInt32LittleEndian(data[44..]),
            Left = BinaryPrimitives.ReadInt16LittleEndian(data[48..]),
            Top = BinaryPrimitives.ReadInt16LittleEndian(data[50..]),
            Right = BinaryPrimitives.ReadInt16LittleEndian(data[52..]),
            Bottom = BinaryPrimitives.ReadInt16LittleEndian(data[54..]),
        };
    }

    /// <summary>Serializes the header; <paramref name="destination"/> must be at least <see cref="Size"/> bytes.</summary>
    public readonly void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"AVI stream header needs {Size} bytes, got {destination.Length}.", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Type);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Handler);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[12..], Priority);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..], Language);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], InitialFrames);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], Scale);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], Rate);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[28..], Start);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[32..], Length);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[36..], SuggestedBufferSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[40..], Quality);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[44..], SampleSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination[48..], Left);
        BinaryPrimitives.WriteInt16LittleEndian(destination[50..], Top);
        BinaryPrimitives.WriteInt16LittleEndian(destination[52..], Right);
        BinaryPrimitives.WriteInt16LittleEndian(destination[54..], Bottom);
    }
}

/// <summary>
/// The 40-byte <c>BITMAPINFOHEADER</c> stored in a video stream's <c>strf</c> chunk. For 8bpp DIB
/// streams a 256-entry palette may follow the header inside the same chunk.
/// </summary>
internal struct BitmapInfoHeader
{
    /// <summary>Serialized size of the structure, in bytes (excluding any palette).</summary>
    public const int Size = 40;

    /// <summary>The <c>BI_RGB</c> compression value: uncompressed DIB samples.</summary>
    public const uint BiRgb = 0;

    public uint HeaderSize;
    public int Width;

    /// <summary>Positive for bottom-up DIB rows, negative for top-down rows.</summary>
    public int Height;

    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;

    /// <summary>Parses a <c>strf</c> payload; <paramref name="data"/> must be at least <see cref="Size"/> bytes.</summary>
    public static BitmapInfoHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"AVI bitmap header needs {Size} bytes, got {data.Length}.");

        return new BitmapInfoHeader
        {
            HeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(data),
            Width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]),
            Height = BinaryPrimitives.ReadInt32LittleEndian(data[8..]),
            Planes = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            BitCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            Compression = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]),
            SizeImage = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
            XPelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(data[24..]),
            YPelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(data[28..]),
            ClrUsed = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]),
            ClrImportant = BinaryPrimitives.ReadUInt32LittleEndian(data[36..]),
        };
    }

    /// <summary>Serializes the header; <paramref name="destination"/> must be at least <see cref="Size"/> bytes.</summary>
    public readonly void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"AVI bitmap header needs {Size} bytes, got {destination.Length}.", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], Height);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[12..], Planes);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..], BitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], Compression);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], SizeImage);
        BinaryPrimitives.WriteInt32LittleEndian(destination[24..], XPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(destination[28..], YPelsPerMeter);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[32..], ClrUsed);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[36..], ClrImportant);
    }
}
