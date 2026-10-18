using System.Globalization;
using System.Text;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Minimal PGM (portable graymap) reader/writer, the intermediate handoff format PIPP's GUI and
/// engine share for master calibration frames (<c>-dark_pgm</c>, <c>pipp_master_dark.pgm</c>).
/// Supports binary (<c>P5</c>) and ASCII (<c>P2</c>) with maxval up to 65535.
/// </summary>
public static class PgmFile
{
    /// <summary>Reads a PGM as a single-channel frame (16-bit when maxval &gt; 255).</summary>
    /// <exception cref="InvalidDataException">The file is not a PGM, or is truncated.</exception>
    public static Frame Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        int pos = 0;

        string Magic() => NextToken(bytes, ref pos);
        int Number() => int.Parse(NextToken(bytes, ref pos), CultureInfo.InvariantCulture);

        string magic = Magic();
        if (magic is not ("P5" or "P2")) throw new InvalidDataException($"'{path}' is not a PGM (magic '{magic}')");
        int width = Number();
        int height = Number();
        int maxval = Number();
        if (width <= 0 || height <= 0) throw new InvalidDataException("PGM has invalid dimensions");
        if (maxval is <= 0 or > 65535) throw new InvalidDataException($"PGM maxval {maxval} is not supported");

        var samples = new ushort[width * height];
        bool twoBytes = maxval > 255;

        if (magic == "P5")
        {
            // exactly one whitespace byte separates the header from the raster
            if (pos >= bytes.Length) throw new InvalidDataException("PGM header is truncated");
            pos++;
            int need = width * height * (twoBytes ? 2 : 1);
            if (bytes.Length - pos < need) throw new InvalidDataException("PGM raster is truncated");
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = twoBytes
                    ? (ushort)((bytes[pos + i * 2] << 8) | bytes[pos + i * 2 + 1])
                    : bytes[pos + i];
            }
        }
        else
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)Number();
            }
        }

        return new Frame(width, height, 1, twoBytes ? 16 : 8, samples);
    }

    /// <summary>Writes a single-channel frame as a binary P5 PGM.</summary>
    public static void Write(string path, Frame frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Channels != 1) throw new ArgumentException("PGM is single-channel", nameof(frame));

        int maxval = frame.BitDepth == 8 ? 255 : 65535;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var header = Encoding.ASCII.GetBytes($"P5\n{frame.Width} {frame.Height}\n{maxval}\n");
        stream.Write(header);

        if (frame.BitDepth == 8)
        {
            var row = new byte[frame.Width];
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++) row[x] = (byte)Math.Clamp((int)frame.Samples[y * frame.Width + x], 0, 255);
                stream.Write(row);
            }
        }
        else
        {
            var row = new byte[frame.Width * 2];
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    ushort v = frame.Samples[y * frame.Width + x];
                    row[x * 2] = (byte)(v >> 8);
                    row[x * 2 + 1] = (byte)v;
                }
                stream.Write(row);
            }
        }
    }

    private static string NextToken(byte[] data, ref int pos)
    {
        while (pos < data.Length)
        {
            byte b = data[pos];
            if (b == (byte)'#')
            {
                while (pos < data.Length && data[pos] != (byte)'\n') pos++;
                continue;
            }
            if (!char.IsWhiteSpace((char)b)) break;
            pos++;
        }

        int start = pos;
        while (pos < data.Length && !char.IsWhiteSpace((char)data[pos])) pos++;
        if (start == pos) throw new InvalidDataException("PGM header is truncated");
        return Encoding.ASCII.GetString(data, start, pos - start);
    }
}
