using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Opipp.Core.Cli;
using Opipp.Core.Formats;

namespace Opipp.Cli.Tests;

/// <summary>
/// Regression on the real Saturn capture: a raw CFA sequence whose centring produces odd shifts
/// cannot be integrated directly, and debayering first is the documented workaround. Both halves
/// are asserted through the real entry point, and the outputs are inspected with an independent
/// <see cref="SerHeader"/> read rather than the engine's own <c>SerReader</c>.
/// </summary>
/// <remarks>
/// <c>samples/saturn30.ser</c> is third-party evaluation data kept out of version control, so every
/// case skips loudly when the fixture is absent. Console redirection is process-wide, so this class
/// joins the non-parallel collection the other CLI end-to-end and transcript tests use.
/// Measured against this tree: the raw run exits 1 and writes nothing; the debayered run exits 0 and
/// writes one 619x598 three-plane SER frame in about one second.
/// </remarks>
[Collection("gui-stdout")]
public class RealCaptureIntegrationTests
{
    /// <summary>30 frames of 640x640 8-bit RGGB, tagged ColorID 8.</summary>
    private const string FixtureRelativePath = "samples/saturn30.ser";

    /// <summary>Detected object box threshold the recording run used; the planet needs it to be found.</summary>
    private const string MinimumPixel = "-mp=40";

    /// <summary>
    /// The crop width and height the debayered integration produced from the 640x640 input: cropping
    /// to the region every contributor can fill removes 21 columns and 42 rows for this capture's
    /// measured shifts. Pinned so a registration or coverage regression changes the number here.
    /// </summary>
    private const int IntegratedWidth = 619;
    private const int IntegratedHeight = 598;

    private static readonly string FixturePath =
        Path.Combine(FindRepoRoot(), FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));

    // ---------------------------------------------------------------------------------------------
    // (a) raw integration is refused
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Centring the raw capture produces shifts such as (-20, 39). An odd shift moves the 2x2 CFA
    /// tile to another phase, so the run must fail instead of averaging different colour sites: exit
    /// 1, the phase conflict named on stderr, and no SER written (not even a staging file).
    /// </summary>
    [SkippableFact]
    public void RawCapture_WithCentring_ExitsOneAndWritesNoSer()
    {
        Skip.If(!File.Exists(FixturePath), $"requires fixture not stored in the repository: {FixturePath}");

        string output = NewDirectory();
        Directory.CreateDirectory(output);
        try
        {
            RunResult result = Run("-ser", "-stack=mean", "-ctr", MinimumPixel, "-od=" + output, FixturePath);

            Assert.Equal(1, result.ExitCode);

            // The message must name the conflict (Bayer phase / effective pattern / colour sites) and
            // the documented workaround, so a user is not left with an unexplained refusal.
            Assert.Contains("Bayer", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("effective pattern", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("colour sites", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("-db", result.StdErr, StringComparison.Ordinal);

            Assert.Empty(Directory.GetFiles(output, "*.ser"));
            Assert.Empty(Directory.GetFiles(output, "*.integrating*"));
        }
        finally
        {
            Delete(output);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // (b) debayer first, then integrate
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The documented workaround: <c>-db</c> converts the mosaics to colour, the colour frames
    /// register with sub-pixel resampling, and the run writes exactly one three-plane SER image at the
    /// cropped overlap. Every sample must lie inside the input's value range because a mean cannot
    /// leave it, and the run must finish well inside a generous limit rather than hang.
    /// </summary>
    [SkippableFact]
    public void DebayeredCapture_WithCentring_WritesOneColourImageInRange()
    {
        Skip.If(!File.Exists(FixturePath), $"requires fixture not stored in the repository: {FixturePath}");

        SerHeader source = ReadHeader(FixturePath);
        Assert.Equal(30, source.FrameCount);
        Assert.Equal(SerColorId.BayerRggb, source.Color);
        Assert.Equal(1, source.BytesPerSample);
        (int sourceMin, int sourceMax) = ReadSampleRange(FixturePath, source);

        string output = NewDirectory();
        Directory.CreateDirectory(output);
        try
        {
            long started = Stopwatch.GetTimestamp();
            RunResult result = Run("-ser", "-db", "-stack=mean", "-ctr", MinimumPixel, "-od=" + output, FixturePath);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

            Assert.Equal(0, result.ExitCode);
            Assert.True(
                elapsed < TimeSpan.FromSeconds(120),
                $"the integration took {elapsed.TotalSeconds:F1} s; the limit is 120 s");

            string[] files = Directory.GetFiles(output, "*.ser");
            Assert.Single(files);
            Assert.Equal("saturn30.ser", Path.GetFileName(files[0]));

            SerHeader header = ReadHeader(files[0]);
            Assert.Equal(1, header.FrameCount);
            Assert.Equal(IntegratedWidth, header.Width);
            Assert.Equal(IntegratedHeight, header.Height);
            Assert.Equal(3, header.Channels);
            Assert.Equal(SerColorId.Bgr, header.Color);
            Assert.Equal(8, header.BitDepth);

            // The file length must match the header exactly: one 619x598 three-plane frame.
            Assert.Equal(
                SerHeader.Size + ((long)header.FrameCount * header.Width * header.Height * header.Channels),
                new FileInfo(files[0]).Length);

            (int outputMin, int outputMax) = ReadSampleRange(files[0], header);
            Assert.True(
                outputMin >= sourceMin && outputMax <= sourceMax,
                $"integrated samples span {outputMin}..{outputMax}, outside the input range {sourceMin}..{sourceMax}");
        }
        finally
        {
            Delete(output);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // (c) control: the fixture really holds 30 frames
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Without <c>-stack=</c> the same capture converts normally: one SER container holding all 30
    /// frames at the input geometry. This proves the failures above are the integration's, not a
    /// damaged fixture, and pins that a plain conversion still writes every frame.
    /// </summary>
    [SkippableFact]
    public void CaptureWithoutIntegration_StillWritesThirtyFrames()
    {
        Skip.If(!File.Exists(FixturePath), $"requires fixture not stored in the repository: {FixturePath}");

        string output = NewDirectory();
        Directory.CreateDirectory(output);
        try
        {
            RunResult result = Run("-ser", MinimumPixel, "-od=" + output, FixturePath);

            Assert.Equal(0, result.ExitCode);

            string[] files = Directory.GetFiles(output, "*.ser");
            Assert.Single(files);

            SerHeader header = ReadHeader(files[0]);
            Assert.Equal(30, header.FrameCount);
            Assert.Equal(640, header.Width);
            Assert.Equal(640, header.Height);
            Assert.Equal(SerColorId.BayerRggb, header.Color);
            Assert.Equal(
                SerHeader.Size + ((long)header.FrameCount * header.Width * header.Height * header.BytesPerSample),
                new FileInfo(files[0]).Length);

            // The frames are the input's own mosaics: the run must not retag or rescale them.
            (int min, int max) = ReadSampleRange(files[0], header);
            (int sourceMin, int sourceMax) = ReadSampleRange(FixturePath, ReadHeader(FixturePath));
            Assert.Equal(sourceMin, min);
            Assert.Equal(sourceMax, max);
        }
        finally
        {
            Delete(output);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // (d) the help documents the workaround the message names
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The extension section must carry the same story the failure message tells: a raw CFA capture
    /// moved by an odd number of pixels cannot be integrated directly, and <c>-db</c> is the way out.
    /// Without this, the guidance can silently disappear from the user-facing text.
    /// </summary>
    [Fact]
    public void Help_DocumentsTheDebayerWorkaroundForRawCfaIntegration()
    {
        int section = OpippHelpText.Text.IndexOf("Final image (OPIPP extension)", StringComparison.Ordinal);
        Assert.True(section >= 0, "the final-image help section is missing");

        // Collapse the help's hard wrapping so the assertions read the text a user sees, not its
        // line breaks: the GUI control's name happens to fall across two lines.
        string finalImage = Regex.Replace(OpippHelpText.Text[section..], @"\s+", " ");

        Assert.Contains("odd number of pixels", finalImage, StringComparison.Ordinal);
        Assert.Contains("-db", finalImage, StringComparison.Ordinal);
        Assert.Contains("-db=<pattern>", finalImage, StringComparison.Ordinal);
        Assert.Contains("Debayer Monochrome Frames", finalImage, StringComparison.Ordinal);
        Assert.Contains("-stack_subpixel=off", finalImage, StringComparison.Ordinal);
        Assert.Contains("even whole number of pixels", finalImage, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Independent SER inspection
    // ---------------------------------------------------------------------------------------------

    private static SerHeader ReadHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SerHeader.Read(stream);
    }

    /// <summary>
    /// The extreme sample values of the payload, read straight from the file's bytes after the
    /// header. Only 8-bit payloads are used here, where one byte is one sample and the SER
    /// endianness flag cannot change the value; anything else is refused rather than misread.
    /// </summary>
    private static (int Min, int Max) ReadSampleRange(string path, SerHeader header)
    {
        Assert.Equal(1, header.BytesPerSample);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(SerHeader.Size, SeekOrigin.Begin);

        long expected = (long)header.FrameCount * header.Width * header.Height * header.Channels;
        var buffer = new byte[64 * 1024];
        long remaining = expected;
        int min = byte.MaxValue;
        int max = byte.MinValue;
        while (remaining > 0)
        {
            int want = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, 0, want);
            Assert.True(read > 0, $"'{path}' ended {remaining} byte(s) before its header said it would");
            for (int i = 0; i < read; i++)
            {
                int value = buffer[i];
                if (value < min) min = value;
                if (value > max) max = value;
            }

            remaining -= read;
        }

        Assert.Equal(-1, stream.ReadByte());
        return (min, max);
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "pipp-real-capture-tests", Guid.NewGuid().ToString("N"));

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Walks up from the test output until a directory holding <c>src/Opipp.Core</c> next to a
    /// solution file. Accepting any solution name keeps a rename from turning into a wall of skips.
    /// </summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !(Directory.Exists(Path.Combine(directory.FullName, "src", "Opipp.Core")) &&
                 Directory.EnumerateFiles(directory.FullName, "*.slnx").Any()))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("repository root (src/Opipp.Core next to a .slnx) not found above the test output");
    }

    // ---------------------------------------------------------------------------------------------
    // CLI driver
    // ---------------------------------------------------------------------------------------------

    private readonly record struct RunResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>Runs the real entry point with the console captured, like the other CLI end-to-end tests.</summary>
    private static RunResult Run(params string[] arguments)
    {
        lock (ConsoleGate)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            var stdout = new StringWriter(CultureInfo.InvariantCulture);
            var stderr = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Program.Main(arguments);
                return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    private static readonly object ConsoleGate = new();
}
