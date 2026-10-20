using System.Globalization;
using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Pipeline;

namespace Opipp.Cli.Tests;

/// <summary>
/// End-to-end tests for final-image integration (<c>-stack=</c>, an OPIPP extension): one output
/// image per input, the coverage policy, the sub-pixel switch and the failure paths, driven through
/// the real entry point on synthetic SER captures.
/// </summary>
/// <remarks>
/// Console redirection is process-wide, so this class joins the non-parallel collection used by the
/// GUI transcript snapshots and the other CLI end-to-end tests.
/// </remarks>
[Collection("gui-stdout")]
public class FinalImageIntegrationTests
{
    private const int Width = 160;
    private const int Height = 120;

    /// <summary>Top-left corner and size of the marked feature and of the AFB box that tracks it.</summary>
    private const int FeatureX = 48;
    private const int FeatureY = 40;
    private const int FeatureSize = 24;

    /// <summary>
    /// The surface-tracking quartet that makes the pipeline measure a registration. The box sits well
    /// away from the frame border, so the aligner's full search window stays inside the frame, and it
    /// covers the marked feature, so every frame's displacement is measured against the first frame.
    /// </summary>
    private static readonly string[] TrackingSwitches =
    [
        "-en_surface_feature_tracking", "-en_ss",
        $"-afb_x={FeatureX}", $"-afb_y={FeatureY}",
        $"-afb_width={FeatureSize}", $"-afb_height={FeatureSize}",
    ];

    // ---------------------------------------------------------------------------------------------
    // One image from the selection
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The mean of two frames whose samples are 100 and 101 is 100.5, which half-away-from-zero
    /// quantisation writes as 101. Exactly one file is produced, named after the input.
    /// </summary>
    [Fact]
    public void Mean_TwoFrames_WritesOneImageRoundedHalfAwayFromZero()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Flat(100), Flat(101));
            string output = Path.Combine(directory, "out");

            RunResult result = Run("-tif", "-stack=mean", "-od=" + output, input);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StdErr);

            string[] files = Directory.GetFiles(output, "*.tif");
            Assert.Single(files);
            Assert.Equal("capture.tif", Path.GetFileName(files[0]));

            Frame integrated = ReadTiff(files[0]);
            Assert.Equal(Width, integrated.Width);
            Assert.Equal(Height, integrated.Height);
            Assert.Equal(101, integrated.Samples[(5 * Width) + 5]);
            // The disc is 200 in both frames, so the mean is 200 wherever it covers.
            Assert.Equal(200, integrated.Samples[(60 * Width) + 80]);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>The median of three frames whose backgrounds are 10, 20 and 30 is the middle sample, 20.</summary>
    [Fact]
    public void Median_ThreeFrames_WritesTheExactMiddleSample()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Flat(10), Flat(20), Flat(30));
            string output = Path.Combine(directory, "out");

            RunResult result = Run("-tif", "-stack=median", "-od=" + output, input);

            Assert.Equal(0, result.ExitCode);
            string[] files = Directory.GetFiles(output, "*.tif");
            Assert.Single(files);

            Frame integrated = ReadTiff(files[0]);
            Assert.Equal(Width, integrated.Width);
            Assert.Equal(Height, integrated.Height);
            Assert.Equal(20, integrated.Samples[(5 * Width) + 5]);
            Assert.Equal(200, integrated.Samples[(60 * Width) + 80]);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Coverage policy
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A feature that moves right by exactly three pixels between the two frames produces a measured
    /// registration of three pixels. The default policy crops to the region both frames can fill, so
    /// the image loses three columns; <c>canvas</c> keeps the first frame's geometry and writes the
    /// columns no frame can supply as the fill sentinel.
    /// </summary>
    [Fact]
    public void Coverage_CanvasKeepsTheFirstCanvas_AndCropShrinksPerTheShifts()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Feature(0), Feature(3));
            string cropOutput = Path.Combine(directory, "crop");
            string canvasOutput = Path.Combine(directory, "canvas");

            RunResult crop = Run(["-tif", "-stack=mean", .. TrackingSwitches, "-od=" + cropOutput, input]);
            RunResult canvas = Run(
                ["-tif", "-stack=mean", "-stack_coverage=canvas", .. TrackingSwitches, "-od=" + canvasOutput, input]);

            Assert.Equal(0, crop.ExitCode);
            Assert.Equal(0, canvas.ExitCode);

            Frame cropped = ReadTiff(Path.Combine(cropOutput, "capture.tif"));
            Frame kept = ReadTiff(Path.Combine(canvasOutput, "capture.tif"));

            // The 3 px registration is integral, so the crop loses exactly three columns. The
            // sub-pixel estimator's tiny vertical residual can cost at most the one border row.
            Assert.Equal(Width - 3, cropped.Width);
            Assert.InRange(cropped.Height, Height - 1, Height);

            Assert.Equal(Width, kept.Width);
            Assert.Equal(Height, kept.Height);
            Assert.True(cropped.Width < kept.Width, "crop must cover less than the first canvas");

            // With a first-frame reference the first frame covers its own canvas completely, so the
            // fill sentinel needs a capture where the first frame is shifted too: -ctr centres every
            // frame, including the first.
            string centredInput = WriteCapture(directory, "centred.ser", Disc(0), Disc(3));
            string centredOutput = Path.Combine(directory, "centred");
            RunResult centred = Run(
                ["-tif", "-stack=mean", "-stack_coverage=canvas", "-ctr", "-od=" + centredOutput, centredInput]);

            Assert.Equal(0, centred.ExitCode);
            Frame shifted = ReadTiff(Path.Combine(centredOutput, "centred.tif"));
            Assert.Equal(Width, shifted.Width);
            Assert.Equal(Height, shifted.Height);

            int uncovered = 0;
            for (int x = 0; x < Width; x++)
            {
                if (shifted.Samples[(10 * Width) + x] == 0)
                {
                    uncovered++;
                }
            }

            // Pixels no frame can supply are the fill sentinel, and they sit together at the edge the
            // first frame moved away from - never a synthetic background.
            Assert.True(uncovered > 0, "a shifted first frame leaves canvas pixels no frame can supply");
            Assert.True(
                (shifted.Samples[10 * Width] == 0) ^ (shifted.Samples[(10 * Width) + (Width - 1)] == 0),
                "the uncovered columns must be contiguous at one edge");
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Sub-pixel registration
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// With <c>-stack_subpixel=off</c> the measured offset is rounded to whole pixels and applied
    /// without resampling, exactly like the pre-0.2 shift. For the 3 px feature the registered pair
    /// coincides, so the integrated image is the first frame cropped by those three columns - no
    /// interpolated sample anywhere.
    /// </summary>
    [Fact]
    public void SubpixelOff_ReproducesWholePixelShifts()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            Frame first = Feature(0);
            string input = WriteCapture(directory, "capture.ser", first, Feature(3));
            string output = Path.Combine(directory, "out");

            RunResult result = Run(
                ["-tif", "-stack=mean", "-stack_subpixel=off", .. TrackingSwitches, "-od=" + output, input]);

            Assert.Equal(0, result.ExitCode);
            Frame integrated = ReadTiff(Path.Combine(output, "capture.tif"));
            Assert.Equal(Width - 3, integrated.Width);
            Assert.Equal(Height, integrated.Height);

            for (int y = 0; y < integrated.Height; y++)
            {
                for (int x = 0; x < integrated.Width; x++)
                {
                    Assert.Equal(first.Samples[(y * Width) + x], integrated.Samples[(y * integrated.Width) + x]);
                }
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// Sub-pixel registration is the default (<c>-stack_subpixel=on</c>), so the same capture is
    /// resampled: the width follows the same integer part, while any fractional part of the measured
    /// offset may cost the border row the bilinear kernel cannot reach. Both runs write one image.
    /// </summary>
    [Fact]
    public void SubpixelOn_IsTheDefaultAndResamplesTheFraction()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Feature(0), Feature(3));
            string onOutput = Path.Combine(directory, "on");
            string offOutput = Path.Combine(directory, "off");

            RunResult on = Run(["-tif", "-stack=mean", .. TrackingSwitches, "-od=" + onOutput, input]);
            RunResult off = Run(
                ["-tif", "-stack=mean", "-stack_subpixel=off", .. TrackingSwitches, "-od=" + offOutput, input]);

            Assert.Equal(0, on.ExitCode);
            Assert.Equal(0, off.ExitCode);
            Assert.Single(Directory.GetFiles(onOutput, "*.tif"));
            Assert.Single(Directory.GetFiles(offOutput, "*.tif"));

            Frame resampled = ReadTiff(Path.Combine(onOutput, "capture.tif"));
            Frame rounded = ReadTiff(Path.Combine(offOutput, "capture.tif"));

            Assert.Equal(rounded.Width, resampled.Width);
            Assert.InRange(resampled.Height, rounded.Height - 1, rounded.Height);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Failure paths
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A joined stream whose inputs declare different CFA patterns cannot be integrated: the
    /// integrator rejects the mixed phases before a sample is combined, the CLI reports it as a
    /// runtime failure (exit 1) and no image is left behind.
    /// </summary>
    [Fact]
    public void MixedCfaJoin_WithIntegration_ExitsOneAndWritesNoImage()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string first = WriteCapture(directory, "first.ser", BayerPattern.RGGB, Flat(100));
            string second = WriteCapture(directory, "second.ser", BayerPattern.BGGR, Flat(100));
            string output = Path.Combine(directory, "out");

            RunResult result = Run("-tif", "-join", "-stack=mean", "-od=" + output, first, second);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Bayer", result.StdErr, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(output, "*.tif"));
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// Cancelling before the integration writes leaves no output file: the image is staged under a
    /// temporary name and only moved into place after the integration completes.
    /// </summary>
    [Fact]
    public void CancelledBeforeIntegration_LeavesNoOutputFile()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Feature(0), Feature(3));
            string output = Path.Combine(directory, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                SurfaceTracking = true,
                EnableStabilisation = true,
                AlignmentBox = new PixelRect(FeatureX, FeatureY, FeatureSize, FeatureSize),
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            };

            using var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress(p =>
            {
                // 100% of the input frames have been read; integration has not written yet.
                if (p.Percent >= 100) cancellation.Cancel();
            });

            Assert.Throws<OperationCanceledException>(
                () => new Processor(settings, progress).Run([input], cancellation.Token));

            Assert.False(File.Exists(Path.Combine(output, "capture.tif")));
            Assert.Empty(Directory.GetFiles(output, "*.tif"));
            Assert.Empty(Directory.GetFiles(output, "*.integrating.tif"));
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Configuration errors and help
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>-stack</c> has no bare form: integration needs a method, so a value-less switch is a
    /// configuration error (exit 2) and nothing is processed.
    /// </summary>
    [Fact]
    public void StackWithoutAValue_IsAConfigurationError()
    {
        RunResult result = Run("-tif", "-stack");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("-stack", result.StdErr, StringComparison.Ordinal);
        Assert.Contains("requires a value", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two qualifiers only shape an integration; on their own they would silently do nothing, so
    /// the command line is rejected with exit 2 rather than accepted as a no-op.
    /// </summary>
    [Theory]
    [InlineData("-stack_coverage=crop")]
    [InlineData("-stack_coverage=canvas")]
    [InlineData("-stack_subpixel=on")]
    [InlineData("-stack_subpixel=off")]
    public void QualifierWithoutStack_IsAConfigurationError(string qualifier)
    {
        RunResult result = Run("-tif", qualifier);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(qualifier.Split('=')[0], result.StdErr, StringComparison.Ordinal);
        Assert.Contains("-stack=mean|sum|median", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>An unknown method is a configuration error that names the offending value.</summary>
    [Fact]
    public void UnknownMethod_ExitsTwoNamingTheValue()
    {
        RunResult result = Run("-tif", "-stack=average");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("'average'", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>An unknown coverage policy and an unknown flag are rejected the same way.</summary>
    [Theory]
    [InlineData("-stack=mean", "-stack_coverage=stretch", "stretch")]
    [InlineData("-stack=mean", "-stack_subpixel=maybe", "maybe")]
    public void UnknownQualifierValue_ExitsTwoNamingTheValue(string method, string qualifier, string value)
    {
        RunResult result = Run("-tif", method, qualifier);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"'{value}'", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>The help has its own section for the extension and lists all three switches.</summary>
    [Fact]
    public void Help_ListsTheFinalImageSectionAndAllThreeSwitches()
    {
        RunResult result = Run("-help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Final image (OPIPP extension)", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("-stack=mean|sum|median", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("-stack_coverage=crop", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("-stack_coverage=canvas", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("-stack_subpixel=on", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("-stack_subpixel=off", result.StdOut, StringComparison.Ordinal);
        // The original engine answers 127 to these switches; the help must say so.
        Assert.Contains("127", result.StdOut, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "pipp-stack-tests", Guid.NewGuid().ToString("N"));

    private static string WriteCapture(string directory, string name, params Frame[] frames) =>
        WriteCapture(directory, name, BayerPattern.None, frames);

    /// <summary>Writes a mono 8-bit SER capture from the given frames.</summary>
    private static string WriteCapture(string directory, string name, BayerPattern bayer, params Frame[] frames)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, Width, Height, 1, 8, frameCount: 0, bayer: bayer);
        foreach (Frame frame in frames)
        {
            writer.Write(frame);
        }

        return path;
    }

    /// <summary>
    /// A flat frame with a small bright disc. The disc gives the detection and quality stages a real
    /// object, while the flat background is what the arithmetic assertions read.
    /// </summary>
    private static Frame Flat(ushort value)
    {
        var frame = new Frame(Width, Height, 1, 8);
        Array.Fill(frame.Samples, value);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double dx = x - 80.5, dy = y - 60.5;
                if ((dx * dx) + (dy * dy) <= 16)
                {
                    frame.Samples[(y * Width) + x] = 200;
                }
            }
        }

        return frame;
    }

    /// <summary>
    /// A flat frame with a bright disc at <c>x = 60 + shiftX</c>, <c>y = 60</c>: the disc moves
    /// between frames so centring gives each frame its own (whole-pixel) offset.
    /// </summary>
    private static Frame Disc(int shiftX)
    {
        var frame = new Frame(Width, Height, 1, 8);
        Array.Fill(frame.Samples, (ushort)40);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double dx = x - (60 + shiftX), dy = y - 60;
                if ((dx * dx) + (dy * dy) <= 25)
                {
                    frame.Samples[(y * Width) + x] = 200;
                }
            }
        }

        return frame;
    }

    /// <summary>
    /// One frame of a capture whose asymmetric 24x24 feature sits at <c>x = 48 + shiftX</c>,
    /// <c>y = 40</c>. An exact whole-pixel translation keeps the alignment measurement integral, so
    /// the registered pair can be compared sample for sample.
    /// </summary>
    private static Frame Feature(int shiftX)
    {
        var frame = new Frame(Width, Height, 1, 8);
        Array.Fill(frame.Samples, (ushort)40);
        for (int ly = 0; ly < FeatureSize; ly++)
        {
            for (int lx = 0; lx < FeatureSize; lx++)
            {
                int x = FeatureX + shiftX + lx, y = FeatureY + ly;
                if (x >= Width || y >= Height)
                {
                    continue;
                }

                ushort value = 60;
                if (lx is >= 3 and < 12 && ly is >= 3 and < 12) value = 220;
                if (ly is >= 18 and < 20) value = 25;
                if (lx is >= 17 and < 20 && ly < 8) value = 150;
                frame.Samples[(y * Width) + x] = value;
            }
        }

        return frame;
    }

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidDataException($"'{path}' holds no frame");
    }

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

    /// <summary>Progress sink that reports inline, so a handler can cancel deterministically.</summary>
    private sealed class InlineProgress(Action<PipelineProgress> handler) : IProgress<PipelineProgress>
    {
        public void Report(PipelineProgress value) => handler(value);
    }
}
