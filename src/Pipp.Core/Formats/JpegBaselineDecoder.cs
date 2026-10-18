using System.Buffers.Binary;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Decodes baseline (SOF0) Huffman-coded JPEG images into a single <see cref="Frame"/>.
/// </summary>
/// <remarks>
/// <para>
/// The implementation covers the JFIF subset that motion-JPEG streams use: 8-bit samples, one or
/// three components, 4:4:4, 4:2:2 and 4:2:0 sampling, 8- and 16-bit quantisation tables, restart
/// markers (DRI plus RSTn) and a single interleaved scan. Entropy-coded data is Huffman decoded,
/// dequantised and passed through a float inverse DCT. Chroma is upsampled with bilinear (triangle)
/// interpolation and converted to RGB with the JFIF full-range YCbCr coefficients.
/// </para>
/// <para>
/// Decoded frames are always 8-bit, three-channel RGB stored as interleaved <c>R,G,B</c> samples, so
/// single-component (greyscale) images are replicated across the three channels. Progressive (SOF2),
/// extended sequential (SOF1), arithmetic-coded, lossless, hierarchical and CMYK JPEGs are rejected
/// with <see cref="NotSupportedException"/>; structural damage and truncation raise
/// <see cref="InvalidDataException"/>.
/// </para>
/// <para>
/// This type performs no I/O after construction: a file or stream is read completely up front and the
/// marker segments are parsed eagerly, while the entropy-coded scan is decoded on the first
/// <see cref="ReadFrame"/> call.
/// </para>
/// </remarks>
public sealed class JpegBaselineDecoder : IFrameSource
{
    /// <summary>Maps a coefficient's zig-zag position to its natural (row-major) position.</summary>
    private static readonly byte[] ZigZag =
    {
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };

    /// <summary>Cosine basis scaled by the JPEG normalisation factors; index <c>x * 8 + u</c>.</summary>
    private static readonly float[] Cosine = BuildCosineTable();

    private static readonly float[] CrToRed = BuildScaleTable(1.402f);
    private static readonly float[] CbToGreen = BuildScaleTable(-0.344136f);
    private static readonly float[] CrToGreen = BuildScaleTable(-0.714136f);
    private static readonly float[] CbToBlue = BuildScaleTable(1.772f);

    private readonly byte[] _data;
    private readonly string? _sourcePath;
    private readonly Dictionary<int, ushort[]> _quantisationTables = new();
    private readonly Dictionary<int, HuffmanTable> _huffmanTables = new();

    private JpegHeader? _header;
    private JpegScan? _scan;
    private int _restartInterval;
    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens and parses a baseline JPEG file.</summary>
    /// <param name="path">Path to the JPEG.</param>
    /// <exception cref="InvalidDataException">The file is not a valid baseline JPEG.</exception>
    /// <exception cref="NotSupportedException">The file uses an unsupported JPEG coding process.</exception>
    public JpegBaselineDecoder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _data = File.ReadAllBytes(path);
        _sourcePath = path;
        Parse();
    }

    /// <summary>Reads and parses a baseline JPEG from a stream.</summary>
    /// <param name="stream">Stream positioned at the JPEG's first byte; it is read to the end.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    /// <exception cref="InvalidDataException">The data is not a valid baseline JPEG.</exception>
    /// <exception cref="NotSupportedException">The data uses an unsupported JPEG coding process.</exception>
    public JpegBaselineDecoder(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            _data = buffer.ToArray();
        }
        finally
        {
            if (!leaveOpen)
                stream.Dispose();
        }

        _sourcePath = null;
        Parse();
    }

    private JpegBaselineDecoder(byte[] data, string? sourcePath)
    {
        _data = data;
        _sourcePath = sourcePath;
        Parse();
    }

    /// <inheritdoc />
    public int Width => Header.Width;

    /// <inheritdoc />
    public int Height => Header.Height;

    /// <inheritdoc />
    public int Channels => 3;

    /// <inheritdoc />
    public int BitDepth => 8;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => BayerPattern.None;

    /// <summary>Component sampling factors as stored in SOF0, in frame-component order.</summary>
    /// <remarks>
    /// For colour images the first entry is luma and the rest are chroma; a component with both
    /// factors equal to one is unsubsampled. Greyscale images report a single 1x1 component.
    /// </remarks>
    public IReadOnlyList<(int Horizontal, int Vertical)> SamplingFactors
    {
        get
        {
            JpegHeader header = Header;
            var factors = new (int, int)[header.Components.Length];
            for (int i = 0; i < factors.Length; i++)
                factors[i] = (header.Components[i].H, header.Components[i].V);
            return factors;
        }
    }

    /// <summary>Number of components in the JPEG frame header: 1 for greyscale, 3 for YCbCr.</summary>
    public int ComponentCount => Header.Components.Length;

    /// <summary>Path this reader was opened from, or null for stream and span construction.</summary>
    public string? SourcePath => _sourcePath;

    private JpegHeader Header =>
        _header ?? throw new InvalidDataException("JPEG has no SOF0 frame header.");

    /// <summary>Decodes a complete baseline JPEG held in memory.</summary>
    /// <param name="jpeg">Bytes of a standalone JPEG, starting at the SOI marker.</param>
    /// <returns>A new 8-bit RGB frame.</returns>
    /// <exception cref="InvalidDataException">The data is truncated, corrupt or not a JPEG.</exception>
    /// <exception cref="NotSupportedException">The data uses an unsupported JPEG coding process.</exception>
    public static Frame Decode(ReadOnlySpan<byte> jpeg)
    {
        var decoder = new JpegBaselineDecoder(jpeg.ToArray(), sourcePath: null);
        return decoder.ReadFrame() ?? throw new InvalidDataException("JPEG produced no frame.");
    }

    /// <summary>Parses a JPEG held in memory up to EOI and reports the SOF0 geometry without decoding pixels.</summary>
    /// <param name="jpeg">Bytes of a standalone JPEG, starting at the SOI marker.</param>
    /// <returns>Geometry and sample format of the frame <see cref="Decode"/> would produce.</returns>
    /// <exception cref="InvalidDataException">The data is truncated, corrupt or not a JPEG.</exception>
    /// <exception cref="NotSupportedException">The data uses an unsupported JPEG coding process.</exception>
    public static ImageInfo Probe(ReadOnlySpan<byte> jpeg)
    {
        var decoder = new JpegBaselineDecoder(jpeg.ToArray(), sourcePath: null);
        return new ImageInfo(decoder.Width, decoder.Height, 8, 3);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The entropy-coded data is corrupt or truncated.</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= 1)
            return null;

        Frame frame = DecodeFrame();
        _nextIndex++;
        return frame;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    private Frame DecodeFrame()
    {
        JpegHeader header = Header;
        JpegScan scan = _scan ?? throw new InvalidDataException("JPEG has no SOS scan header.");

        int maxH = header.MaxH;
        int maxV = header.MaxV;
        int mcuColumns = (header.Width + (8 * maxH) - 1) / (8 * maxH);
        int mcuRows = (header.Height + (8 * maxV) - 1) / (8 * maxV);

        int componentCount = header.Components.Length;
        var planes = new byte[componentCount][];
        var planeWidths = new int[componentCount];

        for (int i = 0; i < componentCount; i++)
        {
            JpegComponent component = header.Components[i];
            planeWidths[i] = mcuColumns * component.H * 8;
            int planeHeight = mcuRows * component.V * 8;
            planes[i] = new byte[planeWidths[i] * planeHeight];
        }

        var reader = new BitReader(scan.Entropy);
        var dcPrediction = new int[componentCount];
        var coefficients = new float[64];
        var intermediate = new float[64];
        var pixels = new float[64];

        int mcuIndex = 0;
        for (int mcuRow = 0; mcuRow < mcuRows; mcuRow++)
        {
            for (int mcuColumn = 0; mcuColumn < mcuColumns; mcuColumn++)
            {
                if (_restartInterval > 0 && mcuIndex > 0 && mcuIndex % _restartInterval == 0)
                {
                    reader.AlignToByte();
                    Array.Clear(dcPrediction);
                }

                for (int s = 0; s < scan.Components.Length; s++)
                {
                    ScanComponent scanned = scan.Components[s];
                    JpegComponent component = header.Components[scanned.ComponentIndex];
                    ushort[] quantisation = GetQuantisationTable(component.QuantisationTable);
                    HuffmanTable dcTable = GetHuffmanTable(isAc: false, scanned.DcTable);
                    HuffmanTable acTable = GetHuffmanTable(isAc: true, scanned.AcTable);

                    byte[] plane = planes[scanned.ComponentIndex];
                    int planeWidth = planeWidths[scanned.ComponentIndex];

                    for (int vertical = 0; vertical < component.V; vertical++)
                    {
                        for (int horizontal = 0; horizontal < component.H; horizontal++)
                        {
                            DecodeBlock(
                                reader,
                                dcTable,
                                acTable,
                                quantisation,
                                coefficients,
                                ref dcPrediction[scanned.ComponentIndex]);

                            InverseDct(coefficients, intermediate, pixels);

                            int blockX = (mcuColumn * component.H) + horizontal;
                            int blockY = (mcuRow * component.V) + vertical;
                            WriteBlock(plane, planeWidth, blockX * 8, blockY * 8, pixels);
                        }
                    }
                }

                mcuIndex++;
            }
        }

        return ComposeFrame(header, planes, planeWidths);
    }

    private void Parse()
    {
        byte[] data = _data;
        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("Not a JPEG file: missing SOI marker (FF D8).");

        int position = 2;
        bool sawEndOfImage = false;

        while (!sawEndOfImage)
        {
            if (position >= data.Length)
                throw new InvalidDataException("JPEG file is truncated: missing EOI marker.");
            if (data[position] != 0xFF)
            {
                throw new InvalidDataException(
                    $"JPEG marker expected at offset {position}, found 0x{data[position]:X2}.");
            }

            while (position < data.Length && data[position] == 0xFF)
                position++;
            if (position >= data.Length)
                throw new InvalidDataException("JPEG file is truncated inside a marker.");

            byte marker = data[position++];

            if (marker == 0x00)
            {
                throw new InvalidDataException(
                    "JPEG contains a stuffed byte (FF 00) outside entropy-coded data.");
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue; // TEM and stray restart markers carry no payload

            if (marker == 0xD9)
            {
                sawEndOfImage = true;
                break;
            }

            if (position + 2 > data.Length)
            {
                throw new InvalidDataException(
                    $"JPEG file is truncated inside the length field of marker FF{marker:X2}.");
            }

            int length = (data[position] << 8) | data[position + 1];
            if (length < 2)
            {
                throw new InvalidDataException(
                    $"JPEG marker FF{marker:X2} declares an invalid segment length {length}.");
            }

            int payloadStart = position + 2;
            int payloadEnd = payloadStart + length - 2;
            if (payloadEnd > data.Length)
                throw new InvalidDataException($"JPEG file is truncated inside marker FF{marker:X2}.");

            ReadOnlySpan<byte> payload = data.AsSpan(payloadStart, length - 2);

            switch (marker)
            {
                case 0xC0:
                    if (_header is not null)
                        throw new InvalidDataException("JPEG contains more than one SOF0 frame header.");
                    _header = JpegHeader.Parse(payload);
                    break;

                case 0xC2:
                    throw new NotSupportedException(
                        "Progressive JPEG (SOF2) is not supported; only baseline SOF0 is decoded.");

                case 0xC1:
                    throw new NotSupportedException(
                        "Extended sequential JPEG (SOF1) is not supported; only baseline SOF0 is decoded.");

                case 0xC3:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xC9:
                case 0xCA:
                case 0xCB:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    throw new NotSupportedException(
                        $"JPEG coding process SOF{marker - 0xC0} is not supported; only baseline SOF0 is decoded.");

                case 0xC4:
                    ParseHuffmanTables(payload);
                    break;

                case 0xDB:
                    ParseQuantisationTables(payload);
                    break;

                case 0xDD:
                    if (length != 4)
                    {
                        throw new InvalidDataException(
                            $"JPEG DRI segment is {length} bytes; expected 4.");
                    }

                    _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(payload);
                    break;

                case 0xDA:
                    if (_scan is not null)
                        throw new NotSupportedException("JPEG with more than one scan is not supported.");
                    if (_header is null)
                        throw new InvalidDataException("JPEG SOS marker appears before the SOF0 frame header.");

                    JpegScan scan = JpegScan.Parse(payload, _header);
                    scan.Entropy = ExtractEntropy(data, payloadEnd, out int nextMarker);
                    _scan = scan;
                    position = nextMarker;
                    continue;

                case 0xDC:
                    break; // DNL: frame height is always present, so this carries nothing to apply

                case 0xCC:
                    throw new NotSupportedException("Arithmetic-coded JPEG (DAC) is not supported.");

                case 0xFE:
                    break; // COM

                case >= 0xE0 and <= 0xEF:
                    break; // APPn: JFIF/Exif/Adobe metadata is not needed for decoding

                default:
                    throw new NotSupportedException($"JPEG marker FF{marker:X2} is not supported.");
            }

            position = payloadEnd;
        }

        if (_header is null)
            throw new InvalidDataException("JPEG has no SOF0 frame header.");
        if (_scan is null)
            throw new InvalidDataException("JPEG has no SOS scan header.");
    }

    private void ParseQuantisationTables(ReadOnlySpan<byte> payload)
    {
        int position = 0;
        while (position < payload.Length)
        {
            int descriptor = payload[position++];
            int precision = descriptor >> 4;
            int identifier = descriptor & 0x0F;

            if (precision > 1)
            {
                throw new InvalidDataException(
                    $"JPEG quantisation table has invalid precision {precision}.");
            }

            if (identifier > 3)
            {
                throw new InvalidDataException(
                    $"JPEG quantisation table identifier {identifier} is out of range.");
            }

            int bytesPerEntry = precision == 0 ? 1 : 2;
            if (position + (64 * bytesPerEntry) > payload.Length)
                throw new InvalidDataException("JPEG DQT segment is truncated.");

            var table = new ushort[64];
            for (int zigZagIndex = 0; zigZagIndex < 64; zigZagIndex++)
            {
                int value;
                if (bytesPerEntry == 1)
                {
                    value = payload[position];
                }
                else
                {
                    value = BinaryPrimitives.ReadUInt16BigEndian(payload[position..]);
                }

                position += bytesPerEntry;
                table[ZigZag[zigZagIndex]] = (ushort)value;
            }

            _quantisationTables[identifier] = table;
        }
    }

    private void ParseHuffmanTables(ReadOnlySpan<byte> payload)
    {
        int position = 0;
        while (position < payload.Length)
        {
            int descriptor = payload[position++];
            int tableClass = descriptor >> 4;
            int identifier = descriptor & 0x0F;

            if (tableClass > 1)
            {
                throw new InvalidDataException(
                    $"JPEG Huffman table has invalid class {tableClass}; expected 0 (DC) or 1 (AC).");
            }

            if (identifier > 3)
            {
                throw new InvalidDataException(
                    $"JPEG Huffman table identifier {identifier} is out of range.");
            }

            if (position + 16 > payload.Length)
                throw new InvalidDataException("JPEG DHT segment is truncated inside the code counts.");

            ReadOnlySpan<byte> counts = payload.Slice(position, 16);
            position += 16;

            int symbolCount = 0;
            foreach (byte count in counts)
                symbolCount += count;

            if (symbolCount > 256)
            {
                throw new InvalidDataException(
                    $"JPEG Huffman table declares {symbolCount} symbols; at most 256 are possible.");
            }

            if (position + symbolCount > payload.Length)
                throw new InvalidDataException("JPEG DHT segment is truncated inside its symbol list.");

            ReadOnlySpan<byte> symbols = payload.Slice(position, symbolCount);
            position += symbolCount;

            _huffmanTables[(tableClass << 8) | identifier] = new HuffmanTable(counts, symbols);
        }
    }

    private static byte[] ExtractEntropy(byte[] data, int start, out int nextMarker)
    {
        using var entropy = new MemoryStream();
        int position = start;

        while (position < data.Length)
        {
            byte current = data[position];
            if (current != 0xFF)
            {
                entropy.WriteByte(current);
                position++;
                continue;
            }

            int runEnd = position + 1;
            while (runEnd < data.Length && data[runEnd] == 0xFF)
                runEnd++;

            if (runEnd >= data.Length)
                throw new InvalidDataException("JPEG file is truncated inside entropy-coded data.");

            byte following = data[runEnd];
            if (following == 0x00)
            {
                // One or more fill bytes followed by the stuffed 0xFF data byte.
                for (int i = position; i < runEnd; i++)
                    entropy.WriteByte(0xFF);
                position = runEnd + 1;
                continue;
            }

            if (following >= 0xD0 && following <= 0xD7)
            {
                // Restart marker: the chunks are concatenated and the decoder re-aligns at the
                // restart interval, so the marker itself carries no payload.
                position = runEnd + 1;
                continue;
            }

            nextMarker = position;
            return entropy.ToArray();
        }

        throw new InvalidDataException(
            "JPEG file is truncated: entropy-coded data has no terminating marker.");
    }

    private static void DecodeBlock(
        BitReader reader,
        HuffmanTable dcTable,
        HuffmanTable acTable,
        ushort[] quantisation,
        float[] coefficients,
        ref int dcPrediction)
    {
        Array.Clear(coefficients);

        int dcBits = dcTable.Decode(reader);
        if (dcBits > 15)
            throw new InvalidDataException($"JPEG DC coefficient category {dcBits} is invalid.");

        int difference = dcBits == 0 ? 0 : ReceiveExtend(reader, dcBits);
        dcPrediction += difference;
        coefficients[0] = dcPrediction * (float)quantisation[0];

        int coefficient = 1;
        while (coefficient < 64)
        {
            int runAndSize = acTable.Decode(reader);
            int run = runAndSize >> 4;
            int size = runAndSize & 0x0F;

            if (size == 0)
            {
                if (run == 15)
                {
                    coefficient += 16;
                    continue;
                }

                if (run != 0)
                    throw new InvalidDataException($"JPEG AC symbol 0x{runAndSize:X2} is invalid.");

                break; // end of block
            }

            coefficient += run;
            if (coefficient > 63)
                throw new InvalidDataException("JPEG AC run extends past the end of the block.");

            int value = ReceiveExtend(reader, size);
            int natural = ZigZag[coefficient];
            coefficients[natural] = value * (float)quantisation[natural];
            coefficient++;
        }
    }

    private static int ReceiveExtend(BitReader reader, int bitCount)
    {
        int value = reader.ReadBits(bitCount);
        if (bitCount > 0 && value < (1 << (bitCount - 1)))
            value -= (1 << bitCount) - 1;
        return value;
    }

    private static void InverseDct(float[] coefficients, float[] intermediate, float[] output)
    {
        float dc = coefficients[0];
        bool hasAc = false;
        for (int i = 1; i < 64; i++)
        {
            if (coefficients[i] != 0f)
            {
                hasAc = true;
                break;
            }
        }

        if (!hasAc)
        {
            float level = (dc * 0.125f) + 128f;
            for (int i = 0; i < 64; i++)
                output[i] = level;
            return;
        }

        // Pass 1: inverse transform each frequency row along the horizontal axis.
        for (int v = 0; v < 8; v++)
        {
            int row = v * 8;
            bool rowNonZero = false;
            for (int u = 0; u < 8; u++)
            {
                if (coefficients[row + u] != 0f)
                {
                    rowNonZero = true;
                    break;
                }
            }

            if (!rowNonZero)
            {
                Array.Clear(intermediate, row, 8);
                continue;
            }

            for (int x = 0; x < 8; x++)
            {
                float sum = 0f;
                for (int u = 0; u < 8; u++)
                    sum += Cosine[(x * 8) + u] * coefficients[row + u];
                intermediate[row + x] = sum;
            }
        }

        // Pass 2: inverse transform each column along the vertical axis, level-shift and round.
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                float sum = 0f;
                for (int v = 0; v < 8; v++)
                    sum += Cosine[(y * 8) + v] * intermediate[(v * 8) + x];

                output[(y * 8) + x] = (sum * 0.25f) + 128f;
            }
        }
    }

    private static void WriteBlock(byte[] plane, int planeWidth, int x, int y, float[] pixels)
    {
        for (int row = 0; row < 8; row++)
        {
            int destination = ((y + row) * planeWidth) + x;
            for (int column = 0; column < 8; column++)
                plane[destination + column] = ClampToByte(pixels[(row * 8) + column]);
        }
    }

    private Frame ComposeFrame(JpegHeader header, byte[][] planes, int[] planeWidths)
    {
        int width = header.Width;
        int height = header.Height;
        var frame = new Frame(width, height, 3, 8)
        {
            Index = 0,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        ushort[] samples = frame.Samples;
        int sample = 0;

        if (header.Components.Length == 1)
        {
            byte[] luma = planes[0];
            int lumaWidth = planeWidths[0];
            for (int y = 0; y < height; y++)
            {
                int row = y * lumaWidth;
                for (int x = 0; x < width; x++)
                {
                    ushort value = luma[row + x];
                    samples[sample++] = value;
                    samples[sample++] = value;
                    samples[sample++] = value;
                }
            }

            return frame;
        }

        JpegComponent lumaComponent = header.Components[0];
        JpegComponent blueComponent = header.Components[1];
        JpegComponent redComponent = header.Components[2];

        byte[] lumaPlane = planes[0];
        byte[] bluePlane = planes[1];
        byte[] redPlane = planes[2];

        int maxH = header.MaxH;
        int maxV = header.MaxV;

        var blueX = BuildAxisMap(width, ComponentWidth(header, blueComponent), blueComponent.H, maxH);
        var redX = BuildAxisMap(width, ComponentWidth(header, redComponent), redComponent.H, maxH);
        var blueY = BuildAxisMap(height, ComponentHeight(header, blueComponent), blueComponent.V, maxV);
        var redY = BuildAxisMap(height, ComponentHeight(header, redComponent), redComponent.V, maxV);

        for (int y = 0; y < height; y++)
        {
            int lumaRow = (y * lumaComponent.V / maxV) * planeWidths[0];
            int blueRowTop = blueY.Lower[y] * planeWidths[1];
            int blueRowBottom = blueY.Upper[y] * planeWidths[1];
            int redRowTop = redY.Lower[y] * planeWidths[2];
            int redRowBottom = redY.Upper[y] * planeWidths[2];
            float blueVertical = blueY.Weight[y];
            float redVertical = redY.Weight[y];

            for (int x = 0; x < width; x++)
            {
                int luma = lumaPlane[lumaRow + (x * lumaComponent.H / maxH)];

                float blueTop = Lerp(
                    bluePlane[blueRowTop + blueX.Lower[x]],
                    bluePlane[blueRowTop + blueX.Upper[x]],
                    blueX.Weight[x]);
                float blueBottom = Lerp(
                    bluePlane[blueRowBottom + blueX.Lower[x]],
                    bluePlane[blueRowBottom + blueX.Upper[x]],
                    blueX.Weight[x]);
                float blue = Lerp(blueTop, blueBottom, blueVertical);

                float redTop = Lerp(
                    redPlane[redRowTop + redX.Lower[x]],
                    redPlane[redRowTop + redX.Upper[x]],
                    redX.Weight[x]);
                float redBottom = Lerp(
                    redPlane[redRowBottom + redX.Lower[x]],
                    redPlane[redRowBottom + redX.Upper[x]],
                    redX.Weight[x]);
                float red = Lerp(redTop, redBottom, redVertical);

                int cb = (int)MathF.Round(blue);
                int cr = (int)MathF.Round(red);
                if (cb < 0)
                    cb = 0;
                else if (cb > 255)
                    cb = 255;
                if (cr < 0)
                    cr = 0;
                else if (cr > 255)
                    cr = 255;

                samples[sample++] = (ushort)ClampToByte(luma + CrToRed[cr]);
                samples[sample++] = (ushort)ClampToByte(luma + CbToGreen[cb] + CrToGreen[cr]);
                samples[sample++] = (ushort)ClampToByte(luma + CbToBlue[cb]);
            }
        }

        return frame;
    }

    private static int ComponentWidth(JpegHeader header, JpegComponent component) =>
        ((header.Width * component.H) + header.MaxH - 1) / header.MaxH;

    private static int ComponentHeight(JpegHeader header, JpegComponent component) =>
        ((header.Height * component.V) + header.MaxV - 1) / header.MaxV;

    private readonly record struct AxisMap(int[] Lower, int[] Upper, float[] Weight);

    private static AxisMap BuildAxisMap(int outputSize, int componentSize, int factor, int maxFactor)
    {
        var lower = new int[outputSize];
        var upper = new int[outputSize];
        var weight = new float[outputSize];

        for (int i = 0; i < outputSize; i++)
        {
            double position = ((((i + 0.5) * factor) / maxFactor) - 0.5);
            int floor = (int)Math.Floor(position);
            double fraction = position - floor;
            if (fraction < 0.0)
                fraction = 0.0;

            lower[i] = Math.Clamp(floor, 0, componentSize - 1);
            upper[i] = Math.Clamp(floor + 1, 0, componentSize - 1);
            weight[i] = (float)fraction;
        }

        return new AxisMap(lower, upper, weight);
    }

    private static float Lerp(float from, float to, float weight) => from + ((to - from) * weight);

    private ushort[] GetQuantisationTable(int identifier)
    {
        if (_quantisationTables.TryGetValue(identifier, out ushort[]? table))
            return table;
        throw new InvalidDataException($"JPEG references undefined quantisation table {identifier}.");
    }

    private HuffmanTable GetHuffmanTable(bool isAc, int identifier)
    {
        int key = ((isAc ? 1 : 0) << 8) | identifier;
        if (_huffmanTables.TryGetValue(key, out HuffmanTable? table))
            return table;
        throw new InvalidDataException(
            $"JPEG references undefined {(isAc ? "AC" : "DC")} Huffman table {identifier}.");
    }

    private static float[] BuildCosineTable()
    {
        var table = new float[64];
        for (int x = 0; x < 8; x++)
        {
            for (int u = 0; u < 8; u++)
            {
                double value = Math.Cos(((2 * x) + 1) * u * Math.PI / 16.0);
                if (u == 0)
                    value /= Math.Sqrt(2.0);
                table[(x * 8) + u] = (float)value;
            }
        }

        return table;
    }

    private static float[] BuildScaleTable(float scale)
    {
        var table = new float[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = scale * (i - 128);
        return table;
    }

    private static byte ClampToByte(float value)
    {
        int rounded = (int)MathF.Round(value);
        if (rounded < 0)
            return 0;
        return rounded > 255 ? (byte)255 : (byte)rounded;
    }

    /// <summary>Validated SOF0 fields.</summary>
    private sealed class JpegHeader
    {
        private JpegHeader(int width, int height, int maxH, int maxV, JpegComponent[] components)
        {
            Width = width;
            Height = height;
            MaxH = maxH;
            MaxV = maxV;
            Components = components;
        }

        public int Width { get; }

        public int Height { get; }

        public int MaxH { get; }

        public int MaxV { get; }

        public JpegComponent[] Components { get; }

        public static JpegHeader Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 6)
                throw new InvalidDataException("JPEG SOF0 segment is truncated.");

            int precision = payload[0];
            if (precision != 8)
            {
                throw new NotSupportedException(
                    $"JPEG sample precision {precision} is not supported; only 8-bit baseline.");
            }

            int height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
            int width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
            if (width == 0 || height == 0)
                throw new InvalidDataException($"JPEG has invalid dimensions {width}x{height}.");

            int componentCount = payload[5];
            if (componentCount is not (1 or 3))
            {
                throw new NotSupportedException(
                    $"JPEG with {componentCount} components is not supported; only 1 (greyscale) and 3 (YCbCr).");
            }

            if (payload.Length < 6 + (componentCount * 3))
                throw new InvalidDataException("JPEG SOF0 segment is truncated inside its component list.");

            var components = new JpegComponent[componentCount];
            int maxH = 1;
            int maxV = 1;

            for (int i = 0; i < componentCount; i++)
            {
                int identifier = payload[6 + (i * 3)];
                int factors = payload[7 + (i * 3)];
                int quantisation = payload[8 + (i * 3)];
                int horizontal = factors >> 4;
                int vertical = factors & 0x0F;

                if (horizontal is < 1 or > 4 || vertical is < 1 or > 4)
                {
                    throw new InvalidDataException(
                        $"JPEG component {identifier} has invalid sampling factors {horizontal}x{vertical}.");
                }

                if (quantisation > 3)
                {
                    throw new InvalidDataException(
                        $"JPEG component {identifier} references quantisation table {quantisation}.");
                }

                for (int previous = 0; previous < i; previous++)
                {
                    if (components[previous].Id == identifier)
                    {
                        throw new InvalidDataException(
                            $"JPEG SOF0 lists component identifier {identifier} twice.");
                    }
                }

                components[i] = new JpegComponent(identifier, horizontal, vertical, quantisation);
                maxH = Math.Max(maxH, horizontal);
                maxV = Math.Max(maxV, vertical);
            }

            int blocksPerMcu = 0;
            foreach (JpegComponent component in components)
                blocksPerMcu += component.H * component.V;

            if (blocksPerMcu > 10)
            {
                throw new InvalidDataException(
                    $"JPEG sampling factors put {blocksPerMcu} blocks in one MCU; at most 10 are allowed.");
            }

            if (componentCount == 1 && (components[0].H != 1 || components[0].V != 1))
            {
                throw new NotSupportedException(
                    "Single-component JPEG with sampling factors other than 1x1 is not supported.");
            }

            return new JpegHeader(width, height, maxH, maxV, components);
        }

        public int IndexOf(int componentId)
        {
            for (int i = 0; i < Components.Length; i++)
            {
                if (Components[i].Id == componentId)
                    return i;
            }

            return -1;
        }
    }

    /// <summary>One SOF0 component description.</summary>
    private sealed class JpegComponent
    {
        public JpegComponent(int id, int h, int v, int quantisationTable)
        {
            Id = id;
            H = h;
            V = v;
            QuantisationTable = quantisationTable;
        }

        public int Id { get; }

        public int H { get; }

        public int V { get; }

        public int QuantisationTable { get; }
    }

    /// <summary>One SOS component selector.</summary>
    private sealed class ScanComponent
    {
        public ScanComponent(int componentIndex, int dcTable, int acTable)
        {
            ComponentIndex = componentIndex;
            DcTable = dcTable;
            AcTable = acTable;
        }

        public int ComponentIndex { get; }

        public int DcTable { get; }

        public int AcTable { get; }
    }

    /// <summary>Validated SOS fields plus the extracted entropy-coded bytes.</summary>
    private sealed class JpegScan
    {
        private JpegScan(ScanComponent[] components)
        {
            Components = components;
        }

        public ScanComponent[] Components { get; }

        public byte[] Entropy { get; set; } = Array.Empty<byte>();

        public static JpegScan Parse(ReadOnlySpan<byte> payload, JpegHeader header)
        {
            if (payload.Length < 1)
                throw new InvalidDataException("JPEG SOS segment is truncated.");

            int componentCount = payload[0];
            if (componentCount < 1 || componentCount > 4)
            {
                throw new InvalidDataException(
                    $"JPEG SOS declares {componentCount} scan components.");
            }

            if (componentCount != header.Components.Length)
            {
                throw new NotSupportedException(
                    $"JPEG scan contains {componentCount} of {header.Components.Length} components; " +
                    "only a single interleaved scan is supported.");
            }

            if (payload.Length < 1 + (componentCount * 2) + 3)
                throw new InvalidDataException("JPEG SOS segment is truncated inside its component list.");

            var components = new ScanComponent[componentCount];
            var seen = new bool[header.Components.Length];

            for (int i = 0; i < componentCount; i++)
            {
                int identifier = payload[1 + (i * 2)];
                int tables = payload[2 + (i * 2)];
                int index = header.IndexOf(identifier);
                if (index < 0)
                {
                    throw new InvalidDataException(
                        $"JPEG scan references component {identifier}, which SOF0 does not declare.");
                }

                if (seen[index])
                    throw new InvalidDataException($"JPEG scan lists component {identifier} twice.");
                seen[index] = true;

                components[i] = new ScanComponent(index, tables >> 4, tables & 0x0F);
            }

            int spectralStart = payload[1 + (componentCount * 2)];
            int spectralEnd = payload[2 + (componentCount * 2)];
            int approximation = payload[3 + (componentCount * 2)];

            if (spectralStart != 0 || spectralEnd != 63 || approximation != 0)
            {
                throw new NotSupportedException(
                    "Sequential JPEG with spectral selection or successive approximation is not supported; " +
                    "only a baseline single scan is decoded.");
            }

            return new JpegScan(components);
        }
    }

    /// <summary>Canonical JPEG Huffman decoding table (ITU T.81 Annex C).</summary>
    private sealed class HuffmanTable
    {
        private readonly int[] _minimumCode = new int[17];
        private readonly int[] _maximumCode = new int[17];
        private readonly int[] _valueOffset = new int[17];
        private readonly byte[] _symbols;

        public HuffmanTable(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
        {
            if (counts.Length != 16)
                throw new InvalidDataException("JPEG Huffman table must have 16 code-length counts.");

            int total = 0;
            foreach (byte count in counts)
                total += count;

            if (total == 0)
                throw new InvalidDataException("JPEG Huffman table contains no symbols.");
            if (total != symbols.Length)
            {
                throw new InvalidDataException(
                    $"JPEG Huffman table declares {total} symbols but lists {symbols.Length}.");
            }

            _symbols = symbols.ToArray();

            int code = 0;
            int valueIndex = 0;
            for (int length = 1; length <= 16; length++)
            {
                int count = counts[length - 1];
                _minimumCode[length] = code;
                _valueOffset[length] = valueIndex;
                _maximumCode[length] = count == 0 ? -1 : code + count - 1;

                code += count;
                if (code > (1 << length))
                {
                    throw new InvalidDataException(
                        $"JPEG Huffman table is over-subscribed at code length {length}.");
                }

                code <<= 1;
                valueIndex += count;
            }
        }

        public int Decode(BitReader reader)
        {
            int code = 0;
            for (int length = 1; length <= 16; length++)
            {
                code = (code << 1) | reader.ReadBit();
                int maximum = _maximumCode[length];
                if (maximum >= 0 && code <= maximum)
                    return _symbols[_valueOffset[length] + code - _minimumCode[length]];
            }

            throw new InvalidDataException(
                "JPEG entropy-coded data contains a Huffman code longer than 16 bits.");
        }
    }

    /// <summary>MSB-first bit reader over unstuffed entropy-coded bytes.</summary>
    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _position;
        private ulong _buffer;
        private int _available;

        public BitReader(byte[] data)
        {
            _data = data;
        }

        public int ReadBit()
        {
            if (_available == 0)
            {
                Fill();
                if (_available == 0)
                    throw Truncated();
            }

            _available--;
            return (int)((_buffer >> _available) & 1UL);
        }

        public int ReadBits(int count)
        {
            if (count == 0)
                return 0;
            if (count is < 0 or > 16)
                throw new ArgumentOutOfRangeException(nameof(count));

            while (_available < count)
            {
                int before = _available;
                Fill();
                if (_available == before)
                    throw Truncated();
            }

            _available -= count;
            return (int)((_buffer >> _available) & ((1UL << count) - 1));
        }

        /// <summary>Discards the padding bits up to the next byte boundary.</summary>
        public void AlignToByte()
        {
            _available -= _available & 7;
        }

        private void Fill()
        {
            while (_available <= 56 && _position < _data.Length)
            {
                _buffer = (_buffer << 8) | _data[_position++];
                _available += 8;
            }
        }

        private static InvalidDataException Truncated() =>
            new("JPEG entropy-coded data ended before the image was complete.");
    }
}
