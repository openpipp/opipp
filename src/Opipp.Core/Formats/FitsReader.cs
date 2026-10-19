using System.Buffers.Binary;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>Controls what <see cref="FitsReader.ReadFrame"/> does when an image payload is short.</summary>
public enum FitsTruncationMode
{
    /// <summary>Throw <see cref="FitsTruncationException"/> when the pixel payload is incomplete.</summary>
    Throw = 0,

    /// <summary>Return the available samples and zero-fill the missing tail.</summary>
    ZeroFill = 1,
}

/// <summary>Geometry and sample mapping of one supported FITS image HDU.</summary>
/// <param name="Width">NAXIS1.</param>
/// <param name="Height">NAXIS2.</param>
/// <param name="Channels">1 for a single plane, 3 for an RGB cube.</param>
/// <param name="BitPix">FITS BITPIX value (8, 16, 32, 64, -32 or -64).</param>
/// <param name="BZero">BZERO (0 when absent).</param>
/// <param name="BScale">BSCALE (1 when absent).</param>
public sealed record FitsImageSpec(
    int Width,
    int Height,
    int Channels,
    int BitPix,
    double BZero,
    double BScale);

/// <summary>
/// The single decision point for which FITS IMAGE header this engine can decode.
/// </summary>
/// <remarks>
/// <para>
/// A supported image is NAXIS 2 or 3 with a supported BITPIX, positive dimensions that fit one
/// frame, a single plane or three RGB planes, a finite BZERO and a finite non-zero BSCALE, and
/// PCOUNT = 0 / GCOUNT = 1 so the pixel payload is exactly the NAXIS product. The declared data
/// section length must match that product.
/// </para>
/// <para>
/// <see cref="FitsReader"/> and the CLI's whole-file <c>FitsHduAudit</c> both call
/// <see cref="Validate"/>/<see cref="Require"/> and <see cref="DeclaredDataBytes"/>, so a header a
/// processing run rejects can never be reported as valid by <c>-check</c>, and neither can drift
/// from the other as keywords are added.
/// </para>
/// </remarks>
public static class FitsImageConstraints
{
    /// <summary>True when the header declares an image: a SIMPLE primary or IMAGE extension with NAXIS 2+.</summary>
    /// <param name="header">Parsed header.</param>
    public static bool IsImageHdu(FitsHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        bool simple = header.GetLogical("SIMPLE") == true;
        string? extension = header.GetString("XTENSION")?.Trim();
        bool imageExtension = extension is not null
            && extension.Equals("IMAGE", StringComparison.OrdinalIgnoreCase);

        return (simple || imageExtension) && header.GetInteger("NAXIS") >= 2;
    }

    /// <summary>
    /// Exact payload length the header declares: the product of every NAXISn axis, plus the
    /// random-groups PCOUNT, times GCOUNT and bytes per value. One implementation so the reader and
    /// the audit agree on where the next HDU starts.
    /// </summary>
    /// <param name="header">Parsed header.</param>
    /// <exception cref="FitsFormatException">NAXIS is impossible or an axis is negative.</exception>
    /// <exception cref="OverflowException">The byte count does not fit a 64-bit integer.</exception>
    public static long DeclaredDataBytes(FitsHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        int naxis = header.GetInt32("NAXIS", 0);
        if (naxis <= 0)
        {
            return 0;
        }

        if (naxis > 999)
        {
            throw new FitsFormatException($"Invalid FITS NAXIS value {naxis}.");
        }

        long count = 1;
        for (int axis = 1; axis <= naxis; axis++)
        {
            long length = header.GetInteger(
                "NAXIS" + axis.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? 0;
            if (length < 0)
            {
                throw new FitsFormatException(
                    $"Negative FITS NAXIS{axis.ToString(System.Globalization.CultureInfo.InvariantCulture)} value {length}.");
            }

            count = checked(count * length);
        }

        long pcount = header.GetInteger("PCOUNT") ?? 0;
        long gcount = header.GetInteger("GCOUNT") ?? 1;
        if (pcount < 0)
        {
            pcount = 0;
        }

        if (gcount <= 0)
        {
            gcount = 1;
        }

        long bitPix = header.GetInteger("BITPIX") ?? 8;
        long bytesPerValue = bitPix == 0 ? 1 : Math.Abs(bitPix) / 8;
        return checked((count + pcount) * gcount * bytesPerValue);
    }

    /// <summary>
    /// Every reason <paramref name="header"/> cannot be decoded as an image, in a stable order;
    /// empty when the header is acceptable. Messages do not name the HDU, so the caller can prefix
    /// its own numbering.
    /// </summary>
    /// <param name="header">Parsed image header.</param>
    /// <param name="declaredDataBytes">Value returned by <see cref="DeclaredDataBytes"/> for this header.</param>
    public static IReadOnlyList<string> Validate(FitsHeader header, long declaredDataBytes)
    {
        ArgumentNullException.ThrowIfNull(header);
        var errors = new List<string>();

        int naxis = header.GetInt32("NAXIS", 0);
        if (naxis is < 2 or > 3)
        {
            errors.Add($"image NAXIS={naxis} is not supported; only 2-D and 3-D images are accepted.");
            return errors;
        }

        long bitPix = header.GetInteger("BITPIX") ?? 0;
        bool bitPixSupported = bitPix is 8 or 16 or 32 or 64 or -32 or -64;
        if (!bitPixSupported)
        {
            errors.Add($"unsupported image BITPIX value {bitPix}.");
        }

        long width = header.GetInteger("NAXIS1") ?? 0;
        long height = header.GetInteger("NAXIS2") ?? 0;
        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            errors.Add($"invalid image dimensions {width} x {height}.");
            return errors;
        }

        long depth = header.GetInteger("NAXIS3") ?? 1;
        if (depth is not (1 or 3))
        {
            errors.Add($"NAXIS3={depth} is not supported; only 1 plane or 3 RGB planes are accepted.");
            return errors;
        }

        int channels = depth == 3 ? 3 : 1;
        long sampleCount;
        try
        {
            sampleCount = checked(width * height * channels);
        }
        catch (OverflowException)
        {
            errors.Add($"image {width} x {height} with {channels} channel(s) is too large for a single frame.");
            return errors;
        }

        if (sampleCount > int.MaxValue)
        {
            errors.Add($"image is too large for a single frame ({sampleCount} samples).");
            return errors;
        }

        double bZero = header.GetReal("BZERO") ?? 0.0;
        if (!double.IsFinite(bZero))
        {
            errors.Add($"BZERO={bZero} is not a finite number.");
        }

        double bScale = header.GetReal("BSCALE") ?? 1.0;
        if (!double.IsFinite(bScale))
        {
            errors.Add($"BSCALE={bScale} is not a finite number.");
        }
        else if (bScale == 0.0)
        {
            errors.Add("BSCALE cannot be zero.");
        }

        long pcount = header.GetInteger("PCOUNT") ?? 0;
        long gcount = header.GetInteger("GCOUNT") ?? 1;
        if (pcount != 0)
        {
            errors.Add(
                $"PCOUNT={pcount} is not supported for an image HDU; the payload must be exactly the NAXIS pixels.");
        }

        if (gcount != 1)
        {
            errors.Add($"GCOUNT={gcount} is not supported for an image HDU; one group per HDU is expected.");
        }

        if (errors.Count == 0 && bitPixSupported)
        {
            long expected;
            try
            {
                expected = checked(sampleCount * (Math.Abs(bitPix) / 8));
            }
            catch (OverflowException)
            {
                errors.Add("the image payload size overflows a 64-bit byte count.");
                return errors;
            }

            if (declaredDataBytes != expected)
            {
                errors.Add(
                    $"the data section is {declaredDataBytes} bytes but the NAXIS/BITPIX geometry needs {expected}.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Returns the validated geometry of an image header, or throws with every violation joined
    /// into one message.
    /// </summary>
    /// <param name="header">Parsed image header.</param>
    /// <param name="declaredDataBytes">Value returned by <see cref="DeclaredDataBytes"/> for this header.</param>
    /// <exception cref="FitsFormatException">The header is not a supported image.</exception>
    public static FitsImageSpec Require(FitsHeader header, long declaredDataBytes)
    {
        IReadOnlyList<string> errors = Validate(header, declaredDataBytes);
        if (errors.Count > 0)
        {
            throw new FitsFormatException(string.Join(" ", errors));
        }

        return new FitsImageSpec(
            (int)(header.GetInteger("NAXIS1") ?? 0),
            (int)(header.GetInteger("NAXIS2") ?? 0),
            (header.GetInteger("NAXIS3") ?? 1) == 3 ? 3 : 1,
            (int)(header.GetInteger("BITPIX") ?? 0),
            (header.GetReal("BZERO") ?? 0.0) + 0.0,
            header.GetReal("BSCALE") ?? 1.0);
    }
}

/// <summary>
/// Reads the first image HDU (and any following image extensions of the same shape) from a FITS file.
/// </summary>
/// <remarks>
/// <para>
/// BITPIX 8, 16, 32, 64, -32 and -64 are accepted. Samples are mapped into PIPP's unsigned 16-bit
/// <see cref="Frame"/> model as follows:
/// </para>
/// <list type="bullet">
/// <item><description>BITPIX = 8 maps to <see cref="Frame.BitDepth"/> 8: the physical value
/// (<c>BZERO + BSCALE * raw</c>) is rounded and clamped to 0..255. When both BZERO and BSCALE are
/// absent the bytes are treated as unsigned (the common convention for 8-bit imaging FITS files);
/// otherwise they are treated as signed per the FITS standard.</description></item>
/// <item><description>BITPIX = 16 maps to bit depth 16, rounding/clamping to 0..65535. With
/// <c>BZERO = 32768</c> (the unsigned 16-bit convention) the values pass through unchanged, so
/// 0..65535 round-trips. Without BZERO the raw 16-bit pattern is reinterpreted as unsigned, matching
/// the way 16-bit imaging data is normally written.</description></item>
/// <item><description>BITPIX = 32 and 64 (and floating point -32/-64) are clamped to 0..65535;
/// no automatic contrast stretch is applied, so 32-bit data above 65535 saturates. 32/64-bit data
/// is treated as signed.</description></item>
/// </list>
/// <para>
/// Colour data (NAXIS = 3 with NAXIS3 = 3) is stored plane-major in the file and is returned as
/// interleaved RGB in <see cref="Frame.Samples"/>. Non-image HDUs before the first image HDU are
/// skipped; an incomplete final 2880-byte data pad is tolerated, while a short pixel payload is reported through
/// <see cref="IsTruncated"/> and, by default, throws on read. A later IMAGE header the reader cannot
/// decode is a hard error naming its HDU: silently stopping there dropped the valid frames after it
/// while the run still reported success.
/// BAYOFFX/BAYOFFY are parsed and exposed on
/// the reader, but <see cref="Frame"/> has no offset field, so they do not alter the sample buffer.
/// </para>
/// </remarks>
public sealed class FitsReader : IFrameSource
{
    private sealed class ImageHdu
    {
        public required FitsHeader Header { get; init; }

        public required long DataOffset { get; init; }

        public required long DataBytes { get; init; }

        public required int Width { get; init; }

        public required int Height { get; init; }

        public required int Channels { get; init; }

        public required int BitPix { get; init; }

        public required double BZero { get; init; }

        public required double BScale { get; init; }

        public required BayerPattern Bayer { get; init; }

        public required int BayerOffsetX { get; init; }

        public required int BayerOffsetY { get; init; }

        public required bool IsTruncated { get; init; }

        public required string? TruncationMessage { get; init; }
    }

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly List<ImageHdu> _images = new();
    private readonly string? _sourceName;
    private int _nextFrame;
    private bool _disposed;

    /// <summary>Opens a FITS file and scans its headers without reading pixel data.</summary>
    /// <param name="path">Path to the FITS file.</param>
    /// <exception cref="FitsFormatException">No supported image HDU was found.</exception>
    public FitsReader(string path)
        : this(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read),
            leaveOpen: false,
            Path.GetFileName(path))
    {
    }

    /// <summary>Reads FITS headers from a seekable stream.</summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere.</param>
    /// <param name="leaveOpen">When true, disposing the reader leaves the stream open.</param>
    /// <exception cref="FitsFormatException">No supported image HDU was found.</exception>
    public FitsReader(Stream stream, bool leaveOpen = false)
        : this(stream, leaveOpen, null)
    {
    }

    private FitsReader(Stream stream, bool leaveOpen, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The FITS stream must be readable.", nameof(stream));
        }

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The FITS stream must be seekable.", nameof(stream));
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
        _sourceName = sourceName;
        try
        {
            Scan(stream.Length);
        }
        catch
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public int Width => First.Width;

    /// <inheritdoc />
    public int Height => First.Height;

    /// <inheritdoc />
    public int Channels => First.Channels;

    /// <inheritdoc />
    public int BitDepth => First.BitPix == 8 ? 8 : 16;

    /// <summary>Number of image HDUs with the same width, height and channel count as the first.</summary>
    public int FrameCount => _images.Count;

    /// <inheritdoc />
    public BayerPattern Bayer => First.Bayer;

    /// <summary>Original FITS BITPIX of the first image HDU (8, 16, 32, 64, -32 or -64).</summary>
    public int BitPix => First.BitPix;

    /// <summary>NAXIS from the first image HDU (2 for mono, 3 for colour).</summary>
    public int Naxis => NaxisOf(First.Header);

    /// <summary>BZERO from the first image HDU (0 when absent).</summary>
    public double BZero => First.BZero;

    /// <summary>BSCALE from the first image HDU (1 when absent).</summary>
    public double BScale => First.BScale;

    /// <summary>BAYOFFX from the first image HDU, or -1 when absent. Not representable on <see cref="Frame"/>.</summary>
    public int BayerOffsetX => First.BayerOffsetX;

    /// <summary>BAYOFFY from the first image HDU, or -1 when absent. Not representable on <see cref="Frame"/>.</summary>
    public int BayerOffsetY => First.BayerOffsetY;

    /// <summary>The full parsed header of the first image HDU.</summary>
    public FitsHeader Header => First.Header;

    /// <summary>True when any selected image HDU declares more pixel bytes than the file contains.</summary>
    public bool IsTruncated => _images.Any(image => image.IsTruncated);

    /// <summary>Description of the first truncation found, or null when the pixel data is complete.</summary>
    public string? TruncationMessage =>
        _images.FirstOrDefault(image => image.IsTruncated)?.TruncationMessage;

    /// <summary>Behavior for short pixel payloads; defaults to <see cref="FitsTruncationMode.Throw"/>.</summary>
    public FitsTruncationMode TruncationMode { get; set; } = FitsTruncationMode.Throw;

    private ImageHdu First => _images[0];

    /// <inheritdoc />
    /// <exception cref="FitsTruncationException">
    /// The current image payload is short and <see cref="TruncationMode"/> is
    /// <see cref="FitsTruncationMode.Throw"/>.
    /// </exception>
    public Frame? ReadFrame()
    {
        ThrowIfDisposed();
        if (_nextFrame >= _images.Count)
        {
            return null;
        }

        ImageHdu image = _images[_nextFrame];
        if (image.IsTruncated && TruncationMode == FitsTruncationMode.Throw)
        {
            throw new FitsTruncationException(
                image.TruncationMessage ?? "The FITS image payload is truncated.",
                image.DataBytes,
                Math.Max(0, _stream.Length - image.DataOffset));
        }

        int sampleCount = checked(image.Width * image.Height * image.Channels);
        int bytesPerSample = Math.Abs(image.BitPix) / 8;
        int wanted = checked(sampleCount * bytesPerSample);
        var buffer = new byte[wanted];

        _stream.Position = image.DataOffset;
        int available = ReadFully(_stream, buffer, wanted);
        if (available < wanted && TruncationMode == FitsTruncationMode.Throw)
        {
            throw new FitsTruncationException(
                image.TruncationMessage ?? "The FITS image payload is truncated.",
                image.DataBytes,
                Math.Max(0, _stream.Length - image.DataOffset));
        }

        ushort[] samples = ConvertSamples(buffer, available, image, sampleCount);
        var frame = new Frame(image.Width, image.Height, image.Channels, image.BitPix == 8 ? 8 : 16, samples)
        {
            Bayer = image.Bayer,
            Index = _nextFrame,
            SourceName = _sourceName,
        };

        _nextFrame++;
        return frame;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private static int NaxisOf(FitsHeader header) => header.GetInt32("NAXIS", 0);

    private void Scan(long length)
    {
        long position = 0;
        int hdu = 0;

        while (position + FitsCard.CardSize <= length)
        {
            int number = hdu + 1;
            _stream.Position = position;
            FitsHeader header;
            int headerBytes;
            try
            {
                header = FitsHeader.Read(_stream, out headerBytes);
            }
            catch (FitsFormatException ex)
            {
                // A zero-filled tail is FITS padding after the last HDU. Anything else is a header
                // the reader cannot walk, and stopping there would silently drop every frame after
                // it, so name the HDU and fail the open instead.
                if (number > 1 && IsZeroPadding(_stream, position, length))
                {
                    break;
                }

                throw new FitsFormatException(
                    $"HDU {number} at offset {position} cannot be parsed: {ex.Message}", ex);
            }

            hdu = number;
            long dataStart = position + headerBytes;
            long dataBytes;
            ImageHdu? image = null;
            try
            {
                dataBytes = FitsImageConstraints.DeclaredDataBytes(header);
                if (FitsImageConstraints.IsImageHdu(header))
                {
                    FitsImageSpec spec = FitsImageConstraints.Require(header, dataBytes);
                    image = CreateImageHdu(header, spec, dataStart, length);
                }
            }
            catch (FitsFormatException ex)
            {
                // Never swallow this for a later HDU: a rejected second IMAGE header used to stop
                // the scan and leave a valid third frame unread while the CLI reported SUCCESS.
                throw new FitsFormatException($"HDU {number}: {ex.Message}", ex);
            }
            catch (OverflowException ex)
            {
                throw new FitsFormatException(
                    $"HDU {number}: the header's data section overflows a 64-bit byte count.", ex);
            }

            if (image is not null)
            {
                bool first = _images.Count == 0;
                if (first || (image.Width == First.Width && image.Height == First.Height
                    && image.Channels == First.Channels))
                {
                    _images.Add(image);
                }
            }

            long padded;
            try
            {
                padded = RoundUp(dataBytes, FitsCard.BlockSize);
            }
            catch (OverflowException ex)
            {
                throw new FitsFormatException(
                    $"HDU {number}: the data block end offset overflows a 64-bit file position.", ex);
            }

            long next = dataStart + padded;
            if (next <= position)
            {
                break;
            }

            position = next;
        }

        if (_images.Count == 0)
        {
            throw new FitsFormatException("No supported FITS image HDU was found.");
        }
    }

    private static ImageHdu CreateImageHdu(
        FitsHeader header,
        FitsImageSpec spec,
        long dataStart,
        long fileLength)
    {
        long dataBytesExact = checked((long)spec.Width * spec.Height * spec.Channels * (Math.Abs(spec.BitPix) / 8));

        bool truncated = dataStart + dataBytesExact > fileLength;
        string? truncationMessage = truncated
            ? $"FITS image payload is truncated: {dataBytesExact} bytes expected at offset {dataStart}, "
              + $"but only {Math.Max(0, fileLength - dataStart)} are available."
            : null;

        return new ImageHdu
        {
            Header = header,
            DataOffset = dataStart,
            DataBytes = dataBytesExact,
            Width = spec.Width,
            Height = spec.Height,
            Channels = spec.Channels,
            BitPix = spec.BitPix,
            BZero = spec.BZero,
            BScale = spec.BScale,
            Bayer = ParseBayer(header),
            BayerOffsetX = (int)(header.GetInteger("BAYOFFX") ?? -1),
            BayerOffsetY = (int)(header.GetInteger("BAYOFFY") ?? -1),
            IsTruncated = truncated,
            TruncationMessage = truncationMessage,
        };
    }

    private static BayerPattern ParseBayer(FitsHeader header)
    {
        string? value = header.GetString("BAYERPAT");
        if (string.IsNullOrWhiteSpace(value))
        {
            return BayerPattern.None;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "RGGB" => BayerPattern.RGGB,
            "GRBG" => BayerPattern.GRBG,
            "GBRG" => BayerPattern.GBRG,
            "BGGR" => BayerPattern.BGGR,
            _ => BayerPattern.None,
        };
    }

    /// <summary>True when every byte in <c>[start, end)</c> is zero (FITS block padding).</summary>
    private static bool IsZeroPadding(Stream stream, long start, long end)
    {
        var buffer = new byte[512];
        stream.Position = start;
        long remaining = end - start;
        while (remaining > 0)
        {
            int wanted = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, 0, wanted);
            if (read <= 0)
            {
                return true;
            }

            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != 0)
                {
                    return false;
                }
            }

            remaining -= read;
        }

        return true;
    }

    private static ushort[] ConvertSamples(byte[] buffer, int availableBytes, ImageHdu image, int sampleCount)
    {
        var samples = new ushort[sampleCount];
        int bytesPerSample = Math.Abs(image.BitPix) / 8;
        double bZero = image.BZero;
        double bScale = image.BScale;
        int maximum = image.BitPix == 8 ? 255 : 65535;

        bool rawUnsigned8 = image.BitPix == 8 && bZero == 0.0 && bScale == 1.0;
        bool rawUnsigned16 = image.BitPix == 16 && bZero == 0.0 && bScale == 1.0;
        bool identity16 = image.BitPix == 16 && bScale == 1.0;
        int pixelCount = sampleCount / image.Channels;

        // PIPP stores FITS rows bottom-up and flips every FITS it reads (verified: feeding the
        // original binary a top-down file returns an upside-down frame). Mirroring that keeps files
        // byte-identical and keeps PIPP-written files upright.
        int width = image.Width;
        int height = image.Height;

        for (int i = 0; i < sampleCount; i++)
        {
            int planePixel = i % pixelCount;
            int flippedPixel = ((height - 1) - (planePixel / width)) * width + (planePixel % width);

            // FITS colour planes are stored consecutively; Frame is RGB-interleaved.
            int target = image.Channels == 1
                ? flippedPixel
                : (flippedPixel * image.Channels) + (i / pixelCount);

            int offset = i * bytesPerSample;
            if (offset + bytesPerSample > availableBytes)
            {
                samples[target] = 0;
                continue;
            }

            double physical;
            switch (image.BitPix)
            {
                case 8:
                {
                    byte raw = buffer[offset];
                    physical = rawUnsigned8 ? raw : bZero + (bScale * (sbyte)raw);
                    break;
                }

                case 16:
                {
                    short raw = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(offset, 2));
                    if (rawUnsigned16)
                    {
                        physical = (ushort)raw;
                    }
                    else if (identity16 && bZero == 32768.0)
                    {
                        physical = (ushort)(raw + 32768);
                    }
                    else
                    {
                        physical = bZero + (bScale * raw);
                    }

                    break;
                }

                case 32:
                {
                    int raw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset, 4));
                    physical = bZero + (bScale * raw);
                    break;
                }

                case 64:
                {
                    long raw = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(offset, 8));
                    physical = bZero + (bScale * raw);
                    break;
                }

                case -32:
                {
                    int bits = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset, 4));
                    float raw = BitConverter.Int32BitsToSingle(bits);
                    physical = bZero + (bScale * raw);
                    break;
                }

                case -64:
                {
                    long bits = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(offset, 8));
                    double raw = BitConverter.Int64BitsToDouble(bits);
                    physical = bZero + (bScale * raw);
                    break;
                }

                default:
                    throw new FitsFormatException($"Unsupported FITS BITPIX value {image.BitPix}.");
            }

            samples[target] = ToSample(physical, maximum);
        }

        return samples;
    }

    private static ushort ToSample(double physical, int maximum)
    {
        if (double.IsNaN(physical) || physical <= 0.0)
        {
            return 0;
        }

        if (physical >= maximum)
        {
            return (ushort)maximum;
        }

        return (ushort)Math.Round(physical, MidpointRounding.AwayFromZero);
    }

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static long RoundUp(long value, int block)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        long remainder = value % block;
        return remainder == 0 ? value : checked(value + block - remainder);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
