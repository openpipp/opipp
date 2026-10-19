using System.Globalization;
using Opipp.Core.Formats;

namespace Opipp.Cli;

/// <summary>
/// Structural validation of every HDU in a FITS file.
/// </summary>
/// <remarks>
/// <para>
/// The frame reader decodes one HDU per <c>ReadFrame</c> call, so reading "the first frame" cannot
/// tell a valid one-HDU file from a file whose later HDU is truncated. This audit walks the whole
/// header chain instead: it parses every 2880-byte header block, recomputes the data offset and
/// exact payload length from the header keywords, and verifies the payload is present. No pixel
/// sample is decoded, so the cost is proportional to the number of HDUs, not to the file's pixel count.
/// </para>
/// <para>
/// Which IMAGE headers are acceptable is not decided here: every image HDU is checked with
/// <see cref="FitsImageConstraints.Validate"/>, the same code <see cref="FitsReader"/> uses, so
/// <c>-check</c> cannot pass a header that a processing run rejects (and vice versa).
/// </para>
/// <para>
/// A short all-zero tail is legal FITS padding and is ignored. A short non-zero tail is reported:
/// it is either an incomplete header or a data block that the file ends inside, and a reader that
/// stops at the first malformed extension would otherwise call the file valid.
/// </para>
/// </remarks>
internal static class FitsHduAudit
{
    /// <summary>Outcome of one file audit.</summary>
    /// <param name="HduCount">Number of HDUs whose header was walked.</param>
    /// <param name="ImageHduCount">Number of those HDUs that declare an image.</param>
    /// <param name="Errors">Structural problems, most severe first; empty when the file is sound.</param>
    internal sealed record Result(int HduCount, int ImageHduCount, IReadOnlyList<string> Errors);

    /// <summary>Walks every HDU in <paramref name="path"/> and reports structural problems.</summary>
    /// <exception cref="IOException">The file cannot be opened or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be opened.</exception>
    internal static Result Inspect(string path)
    {
        var errors = new List<string>();
        int hduCount = 0;
        int imageCount = 0;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        long position = 0;

        while (position < length)
        {
            long remaining = length - position;
            if (remaining < FitsCard.BlockSize)
            {
                // FITS pads the final data block to 2880 bytes; the file may also end exactly after
                // the payload. Only a short NON-zero tail means something was cut off.
                if (!IsZeroPadding(stream, position, length))
                {
                    errors.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"HDU {hduCount + 1} at offset {position} is truncated: only {remaining} of the "
                        + $"{FitsCard.BlockSize} bytes of a header block are present."));
                }

                break;
            }

            stream.Position = position;
            FitsHeader header;
            int headerBytes;
            try
            {
                header = FitsHeader.Read(stream, out headerBytes);
            }
            catch (FitsFormatException ex)
            {
                // Trailing zero padding after the last HDU parses as neither a card nor an END.
                if (hduCount > 0 && IsZeroPadding(stream, position, length))
                {
                    break;
                }

                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"HDU {hduCount + 1} header at offset {position} cannot be parsed: {ex.Message}"));
                break;
            }

            hduCount++;
            long dataStart = position + headerBytes;

            long dataBytes;
            try
            {
                dataBytes = FitsImageConstraints.DeclaredDataBytes(header);
            }
            catch (FitsFormatException ex)
            {
                errors.Add($"HDU {hduCount}: {ex.Message}");
                break;
            }
            catch (OverflowException)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"HDU {hduCount}: the header's axis sizes overflow a 64-bit byte count."));
                break;
            }

            long available = Math.Max(0, length - dataStart);
            if (dataBytes > available)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"HDU {hduCount} data block is truncated: {dataBytes} bytes are declared at offset "
                    + $"{dataStart} but only {available} are present."));
                break; // no later HDU offset can be trusted
            }

            bool image;
            try
            {
                image = FitsImageConstraints.IsImageHdu(header);
            }
            catch (FitsFormatException ex)
            {
                errors.Add($"HDU {hduCount}: {ex.Message}");
                break;
            }

            if (image)
            {
                imageCount++;
                try
                {
                    errors.AddRange(ValidateImageHeader(header, dataBytes, hduCount));
                }
                catch (FitsFormatException ex)
                {
                    errors.Add($"HDU {hduCount}: {ex.Message}");
                    break;
                }
            }

            long next;
            try
            {
                next = checked(dataStart + RoundUp(dataBytes, FitsCard.BlockSize));
            }
            catch (OverflowException)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"HDU {hduCount}: the data block end offset overflows a 64-bit file position."));
                break;
            }

            if (next <= position)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"HDU {hduCount} does not advance the file position; the header chain is malformed."));
                break;
            }

            position = next;
        }

        return new Result(hduCount, imageCount, errors);
    }

    /// <summary>
    /// Reports every reason this image header cannot be decoded. The rules themselves live in
    /// <see cref="FitsImageConstraints.Validate"/> — the same call the frame reader makes — so
    /// <c>-check</c> and a processing run can never disagree about a header.
    /// </summary>
    private static IEnumerable<string> ValidateImageHeader(FitsHeader header, long dataBytes, int hdu)
    {
        IReadOnlyList<string> problems = FitsImageConstraints.Validate(header, dataBytes);
        foreach (string problem in problems)
        {
            yield return $"HDU {hdu}: {problem}";
        }
    }

    private static long RoundUp(long bytes, long block)
    {
        if (bytes <= 0)
        {
            return 0;
        }

        long remainder = bytes % block;
        return remainder == 0 ? bytes : checked(bytes + block - remainder);
    }

    /// <summary>True when every byte in <c>[start, end)</c> is zero.</summary>
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
                return true; // the file ended; nothing non-zero was stored
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
}
