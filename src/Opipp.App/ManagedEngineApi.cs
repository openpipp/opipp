using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Pipeline;

namespace PIPP_Application;

/// <summary>Metadata the source file list needs from one AVI file.</summary>
internal sealed class VideoProbe
{
	public int Width;
	public int Height;
	public int FpsRate;
	public int FpsScale;
	public string Codec;
	public int FrameCount;
	/// <summary>Bayer mosaic index, or -1 when the header alone cannot tell (always -1 here).</summary>
	public int BayerPattern = -1;
}

/// <summary>Metadata the source file list needs from one SER file.</summary>
internal sealed class SerFileProbe
{
	public int Width;
	public int Height;
	public int FpsRate;
	public int FpsScale;
	public string ColourId;
	public int FrameCount;
}

/// <summary>Metadata the source file list needs from one FITS file.</summary>
internal sealed class FitsFileProbe
{
	public int Width;
	public int Height;
	public int Channels;
	public int FrameCount;
	/// <summary>True when the header promises more image data than the file holds.</summary>
	public bool IsTruncated;
}

/// <summary>
/// Managed replacement for the helper entry points the ported WinForms UI used to reach through
/// the former native engine DLL (<c>video_check</c>, <c>ser_check</c>, <c>ser_info</c>/<c>avi_info</c>/<c>fits_info</c>,
/// <c>date_time_check</c> and the version strings). Everything here is implemented on top of
/// <c>Opipp.Core</c>; no native library is loaded or probed.
/// </summary>
/// <remarks>
/// Two legacy helper features have no managed equivalent and are deliberately reported as
/// unsupported instead of being faked:
/// <list type="bullet">
/// <item>RAW decoding: <c>raw_decode_check</c> asked the embedded dcraw whether an unknown extension
/// was a camera RAW file. Opipp.Core contains no dcraw/LibRaw decoder, so unknown extensions are
/// rejected; the engine would not be able to read such a file anyway.</item>
/// <item>Video containers other than AVI: the native helper probed them through FFmpeg. The managed
/// reader implements AVI only, so MOV/MP4/MKV/... are rejected by <see cref="TryProbeVideo"/>.</item>
/// </list>
/// </remarks>
internal static class ManagedEngineApi
{
	/// <summary>Version banner shown in the processing output pane.</summary>
	public static readonly string OpippVersion = "OPIPP " + VersionInfo.Display;

	/// <summary>
	/// Probes the first video stream of an AVI file. Returns false for every other container (the
	/// managed reader has no FFmpeg demuxers) and for files the RIFF walker cannot parse.
	/// </summary>
	public static bool TryProbeVideo(string path, out VideoProbe probe)
	{
		probe = null;
		if (!string.Equals(Path.GetExtension(path), ".avi", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		try
		{
			AviInfo info = AviProbe.GetInfo(path);
			probe = new VideoProbe
			{
				Width = info.Width,
				Height = info.Height,
				FpsRate = info.FpsRate,
				FpsScale = info.FpsScale,
				Codec = info.FourCC,
				FrameCount = info.FrameCount,
			};
			return true;
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
		{
			return false;
		}
	}

	/// <summary>
	/// Probes a FITS file for the number of images it actually contains.
	/// </summary>
	/// <remarks>
	/// One FITS file is not one frame: the primary HDU and every image extension are separate
	/// images, and the engine's reader exposes each of them. The file list used to add one per file,
	/// so a master built from two two-image files was capped at two frames and both of them came
	/// from the first file. The probe opens the real reader (which scans headers only, never the
	/// pixel data), reads its frame count and disposes it before the next file is touched.
	/// </remarks>
	public static bool TryProbeFits(string path, out FitsFileProbe probe)
	{
		probe = null;
		try
		{
			FitsProbeResult info = FitsProbe.Probe(path);
			probe = new FitsFileProbe
			{
				Width = info.Width,
				Height = info.Height,
				Channels = info.Channels,
				FrameCount = info.FrameCount,
				IsTruncated = info.IsTruncated,
			};
			return probe.FrameCount > 0 && probe.Width > 0 && probe.Height > 0;
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FitsFormatException)
		{
			return false;
		}
	}

	/// <summary>
	/// Probes a SER header. Reports the number of frames actually present (the header's count clamped
	/// to the file size) and, when the v3 timestamp trailer is present, the frame rate derived from it.
	/// </summary>
	public static bool TryProbeSer(string path, out SerFileProbe probe)
	{
		probe = null;
		try
		{
			SerInfo info = SerProbe.Probe(path);
			SerHeader header = info.Header;

			int frames = header.FrameCount;
			if (header.FrameSize > 0 && info.FileSizeBytes is long size)
			{
				long payload = Math.Max(0, size - SerHeader.Size);
				long present = payload / header.FrameSize;
				if (present < frames)
				{
					frames = (int)Math.Min(present, int.MaxValue);
				}
			}

			GetSerFrameRate(path, info, out int fpsRate, out int fpsScale);

			probe = new SerFileProbe
			{
				Width = header.Width,
				Height = header.Height,
				FpsRate = fpsRate,
				FpsScale = fpsScale,
				ColourId = DescribeSerColour(header),
				FrameCount = Math.Max(0, frames),
			};

			return probe.FrameCount > 0 && probe.Width > 0 && probe.Height > 0;
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
		{
			return false;
		}
	}

	/// <summary>Formats the metadata of a SER, AVI or FITS file for the File Details window.</summary>
	public static string DescribeFile(string path)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();
		return extension switch
		{
			".ser" => DescribeSer(path),
			".avi" => DescribeAvi(path),
			_ => DescribeFits(path),
		};
	}

	/// <summary>
	/// Parses one WinJUPOS timestamp out of a file name using the UI's date/time mask. This is the
	/// managed equivalent of <c>date_time_check</c>: it returns false when the name does not match.
	/// </summary>
	public static bool TryExtractDateTime(string fileName, string mask, out string extracted)
	{
		extracted = string.Empty;
		if (string.IsNullOrEmpty(mask) || !Processor.TryParseWinJuposTimestamp(fileName, mask, out DateTime value))
		{
			return false;
		}

		extracted = value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		return true;
	}

	private static void GetSerFrameRate(string path, SerInfo info, out int rate, out int scale)
	{
		rate = 0;
		scale = 1;

		SerHeader header = info.Header;
		if (header.FrameCount < 2 || header.FrameSize <= 0 || info.FileSizeBytes is not long size)
		{
			return;
		}

		long imageBytes = (long)header.FrameCount * header.FrameSize;
		long trailerBytes = size - SerHeader.Size - imageBytes;
		if (trailerBytes != header.FrameCount * 8L)
		{
			return; // no v3 per-frame timestamp trailer
		}

		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			stream.Seek(size - trailerBytes, SeekOrigin.Begin);

			var buffer = new byte[8];
			long first = 0;
			long last = 0;
			for (int i = 0; i < header.FrameCount; i++)
			{
				stream.ReadExactly(buffer, 0, buffer.Length);
				long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer);
				if (i == 0)
				{
					first = ticks;
				}
				else if (ticks <= last)
				{
					// The native helper reported "Unordered TS" for non-increasing timestamps.
					rate = -1;
					scale = 1;
					return;
				}

				last = ticks;
			}

			double averageTicks = (last - first) / (double)(header.FrameCount - 1);
			if (averageTicks <= 0)
			{
				rate = -1;
				scale = 1;
				return;
			}

			double fps = TimeSpan.TicksPerSecond / averageTicks;
			double milliFps = Math.Round(fps * 1000.0);
			if (milliFps is > 0 and <= int.MaxValue)
			{
				rate = (int)milliFps;
				scale = 1000;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			rate = 0;
			scale = 1;
		}
	}

	private static string DescribeSerColour(SerHeader header)
	{
		if (header.Bayer != Opipp.Core.Frames.BayerPattern.None)
		{
			return header.Bayer.ToString();
		}

		if (header.IsColour)
		{
			return header.Color == SerColorId.Bgr ? "BGR" : "RGB";
		}

		return "MONO";
	}

	private static string DescribeSer(string path)
	{
		SerInfo info = SerProbe.Probe(path);
		SerHeader header = info.Header;
		var text = new StringBuilder();

		text.AppendLine("PIPP file information (managed engine)");
		text.AppendLine("=====================================");
		AppendFileHeader(text, path, "SER v3");
		text.AppendLine("Image size:        " + header.Width + " x " + header.Height);
		text.AppendLine("Colour:            " + DescribeSerColour(header) + " (ColorID " + header.ColorId + ")");
		text.AppendLine("Bit depth:         " + header.BitDepth);
		text.AppendLine("Frames (header):   " + header.FrameCount);
		if (info.FileSizeBytes is long size)
		{
			long present = header.FrameSize > 0 ? Math.Max(0, size - SerHeader.Size) / header.FrameSize : 0;
			text.AppendLine("Frames (on disk):  " + present);
		}
		text.AppendLine("Byte order:        " + (header.LittleEndian ? "little-endian" : "big-endian"));
		text.AppendLine("Observer:          " + header.Observer);
		text.AppendLine("Telescope:         " + header.Telescope);
		text.AppendLine("Instrument:        " + header.Instrument);
		text.AppendLine("Local time:        " + FormatTime(header.LocalTime));
		text.AppendLine("UTC time:          " + FormatTime(header.UtcTime));
		if (info.HasTimestamps)
		{
			text.AppendLine("UTC offset:        " + header.LocalTimeUtcOffsetMinutes + " minutes");
		}
		text.AppendLine("Truncated:         " + (info.IsTruncated ? "yes" : "no"));
		text.AppendLine("Extra bytes:       " + info.ExtraBytes);
		return text.ToString();
	}

	private static string DescribeAvi(string path)
	{
		AviInfo info = AviProbe.GetInfo(path);
		var text = new StringBuilder();

		text.AppendLine("PIPP file information (managed engine)");
		text.AppendLine("=====================================");
		AppendFileHeader(text, path, "AVI");
		text.AppendLine("Image size:        " + info.Width + " x " + info.Height);
		text.AppendLine("Bits per pixel:    " + info.BitCount);
		text.AppendLine("Codec:             " + info.FourCC);
		text.AppendLine("Frame rate:        " + info.Fps.ToString("0.###", CultureInfo.InvariantCulture) +
		                  " fps (" + info.FpsRate + "/" + info.FpsScale + ")");
		text.AppendLine("Frames:            " + info.FrameCount);
		return text.ToString();
	}

	private static string DescribeFits(string path)
	{
		FitsProbeResult info = FitsProbe.Probe(path);
		var text = new StringBuilder();

		text.AppendLine("PIPP file information (managed engine)");
		text.AppendLine("=====================================");
		AppendFileHeader(text, path, "FITS");
		text.AppendLine("Image size:        " + info.Width + " x " + info.Height);
		text.AppendLine("Channels:          " + info.Channels);
		text.AppendLine("Bit depth:         " + info.BitDepth + " (BITPIX " + info.BitPix + ", NAXIS " + info.Naxis + ")");
		text.AppendLine("Frames:            " + info.FrameCount);
		text.AppendLine("Bayer pattern:     " + (info.Bayer == Opipp.Core.Frames.BayerPattern.None
			? "none"
			: info.Bayer + " (offset " + info.BayerOffsetX + ", " + info.BayerOffsetY + ")"));
		text.AppendLine("BZERO / BSCALE:    " + info.BZero.ToString("G", CultureInfo.InvariantCulture) + " / " +
		                  info.BScale.ToString("G", CultureInfo.InvariantCulture));
		text.AppendLine("Date:              " + (info.Date ?? "(none)"));
		text.AppendLine("Truncated:         " + (info.IsTruncated ? "yes - " + info.TruncationMessage : "no"));
		text.AppendLine();
		text.AppendLine("Header:");
		text.AppendLine(info.Header.ToText());
		return text.ToString();
	}

	private static void AppendFileHeader(StringBuilder text, string path, string format)
	{
		var info = new FileInfo(path);
		text.AppendLine("File:              " + info.Name);
		text.AppendLine("Directory:         " + info.DirectoryName);
		text.AppendLine("Size:              " + info.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
		text.AppendLine("Format:            " + format);
		text.AppendLine();
	}

	private static string FormatTime(DateTime? value) =>
		value is DateTime time ? time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "(none)";
}
