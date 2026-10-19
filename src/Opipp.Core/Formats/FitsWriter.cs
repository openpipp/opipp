using System.Globalization;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>
/// Writes 8-bit and 16-bit FITS images. Samples are stored as signed 16-bit integers with
/// <c>BZERO = 32768</c> and <c>BSCALE = 1</c>, so the full unsigned 0..65535 range round-trips;
/// 8-bit frames keep <c>BITPIX = 8</c> and one byte per sample.
/// Mono frames produce NAXIS = 2; colour frames produce NAXIS = 3 with NAXIS3 = 3 and the R, G and B
/// planes written sequentially (plane-major). Headers and data are padded to 2880-byte boundaries,
/// with the data pad computed from the bytes actually written so every image unit is a multiple of
/// 2880 for both sample depths.
/// </summary>
/// <remarks>
/// Every call to <see cref="Write"/> appends one image HDU; the primary header declares
/// <c>EXTEND = T</c> so a multi-frame sequence is a valid FITS file with one image extension per
/// additional frame. Most PIPP outputs are one frame per file.
/// </remarks>
public sealed class FitsWriter : IFrameSink
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private int _framesWritten;
    private int _width;
    private int _height;
    private int _channels;
    private bool _disposed;

    /// <summary>Creates a writer that truncates/creates the file at <paramref name="path"/>.</summary>
    /// <param name="path">Destination FITS file path.</param>
    public FitsWriter(string path)
        : this(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
            leaveOpen: false)
    {
    }

    /// <summary>Creates a writer over an existing stream.</summary>
    /// <param name="stream">Writable stream; the first byte is written on the first <see cref="Write"/>.</param>
    /// <param name="leaveOpen">When true, disposing the writer leaves the stream open.</param>
    public FitsWriter(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The FITS stream must be writable.", nameof(stream));
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <summary>ORIGIN card value; null or empty omits the card.</summary>
    public string? Origin { get; set; } = "PIPP-RE";

    /// <summary>SOFTWARE card value; null or empty omits the card.</summary>
    public string? Software { get; set; } = "PIPP-RE FITS Writer";

    /// <summary>
    /// Timestamp for the DATE card. Defaults to UTC now at the time the frame is written.
    /// The value is written in ISO-8601 form and labelled UTC.
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>Total frames written so far.</summary>
    public int FramesWritten => _framesWritten;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The frame mixes a Bayer pattern with colour channels, or a later frame changes shape.
    /// </exception>
    public void Write(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfDisposed();

        if (frame.Channels == 3 && frame.Bayer != BayerPattern.None)
        {
            throw new ArgumentException("Colour frames cannot carry a Bayer mosaic pattern.", nameof(frame));
        }

        if (_framesWritten == 0)
        {
            _width = frame.Width;
            _height = frame.Height;
            _channels = frame.Channels;
        }
        else if (frame.Width != _width || frame.Height != _height || frame.Channels != _channels)
        {
            throw new ArgumentException(
                $"All frames in one FITS file must share a shape; expected {_width}x{_height}x{_channels}, "
                + $"got {frame.Width}x{frame.Height}x{frame.Channels}.",
                nameof(frame));
        }

        FitsHeader header = BuildHeader(frame, primary: _framesWritten == 0);
        _stream.Write(header.ToBytes());
        int dataBytes = WritePixels(frame);

        // Pad from the bytes actually written: BITPIX 8 stores one byte per sample, BITPIX 16 two.
        // Deriving the pad from SampleCount * 2 adds 2 * (samples mod 1440) bytes too many for 8-bit
        // images and pushes every later extension header off its 2880-byte boundary.
        WritePadding(dataBytes);
        _stream.Flush();
        _framesWritten++;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _stream.Flush();
        }
        finally
        {
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }
    }

    private FitsHeader BuildHeader(Frame frame, bool primary)
    {
        var header = new FitsHeader();
        if (primary)
        {
            header.Add("SIMPLE", true, "conforms to FITS standard");
        }
        else
        {
            header.Add("XTENSION", "IMAGE", "image extension");
        }

        // PIPP preserves the sample depth: 8-bit frames stay BITPIX=8 (verified against the
        // original binary), 16-bit frames are BITPIX=16 with BZERO so the unsigned range survives.
        int bitPix = frame.BitDepth == 8 ? 8 : 16;
        header.Add("BITPIX", bitPix, bitPix == 16 ? "unsigned samples stored with BZERO" : "8-bit samples");
        header.Add("NAXIS", frame.Channels == 3 ? 3 : 2);
        header.Add("NAXIS1", frame.Width);
        header.Add("NAXIS2", frame.Height);
        if (frame.Channels == 3)
        {
            header.Add("NAXIS3", 3, "RGB planes");
        }

        if (primary)
        {
            header.Add("EXTEND", true, "image extensions may follow");
        }
        else
        {
            header.Add("PCOUNT", 0, "no random groups");
            header.Add("GCOUNT", 1, "one group");
        }

        if (bitPix == 16)
        {
            header.Add("BZERO", 32768.0, "unsigned 16-bit offset");
        }
        else
        {
            header.Add("BZERO", 0.0);
        }
        header.Add("BSCALE", 1.0);

        // The original writes the data range as DATAMIN/DATAMAX; consumers use it for display scaling.
        int dataMin = frame.Samples.Min();
        int dataMax = frame.Samples.Max();
        header.Add("DATAMIN", (double)dataMin);
        header.Add("DATAMAX", (double)dataMax);

        if (frame.Channels == 1 && frame.Bayer != BayerPattern.None)
        {
            header.Add("BAYERPAT", frame.Bayer.ToString(), "Bayer mosaic pattern");
        }

        DateTime timestamp = Date ?? DateTime.UtcNow;
        header.Add(
            "DATE",
            timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            "file creation time (UTC)");

        if (!string.IsNullOrWhiteSpace(Origin))
        {
            header.Add("ORIGIN", Origin, "origin of the file");
        }

        if (!string.IsNullOrWhiteSpace(Software))
        {
            header.Add("SOFTWARE", Software);
        }

        header.AddComment("Written by PIPP-RE FITS writer");
        return header;
    }

    /// <summary>Writes the pixel payload and returns the number of bytes written.</summary>
    /// <returns>Bytes actually appended to the stream: one per sample for BITPIX 8, two for BITPIX 16.</returns>
    private int WritePixels(Frame frame)
    {
        int pixels = checked(frame.Width * frame.Height);
        int channels = frame.Channels;
        bool eightBit = frame.BitDepth == 8;
        int bytesPerSample = eightBit ? 1 : 2;
        int length = checked(pixels * channels * bytesPerSample);
        var data = new byte[length];
        int index = 0;

        // FITS stores colour planes sequentially (plane-major); Frame is interleaved. 8-bit FITS is
        // unsigned by definition; 16-bit is stored signed and offset by BZERO.
        // PIPP writes FITS rows bottom-up (its internal buffers are bottom-up and this path never
        // flips them); matching that is what makes the files byte-identical to the original.
        for (int channel = 0; channel < channels; channel++)
        {
            for (int row = frame.Height - 1; row >= 0; row--)
            {
                int sampleIndex = (row * frame.Width * channels) + channel;
                for (int col = 0; col < frame.Width; col++)
                {
                    ushort value = frame.Samples[sampleIndex];
                    sampleIndex += channels;
                    if (eightBit)
                    {
                        data[index++] = (byte)value;
                    }
                    else
                    {
                        ushort stored = (ushort)(value - 32768);
                        data[index++] = (byte)(stored >> 8);
                        data[index++] = (byte)stored;
                    }
                }
            }
        }

        _stream.Write(data);
        return data.Length;
    }

    private void WritePadding(int dataLength)
    {
        int remainder = dataLength % FitsCard.BlockSize;
        if (remainder == 0)
        {
            return;
        }

        int padding = FitsCard.BlockSize - remainder;
        _stream.Write(new byte[padding]);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
