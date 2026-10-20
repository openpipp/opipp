using System.Globalization;
using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Cli.Tests;

/// <summary>
/// End-to-end tests for the area of interest combined with final-image integration
/// (<c>-aoi_crop</c> with <c>-stack=</c>): the request is a rectangle on the delivered canvas, so the
/// integrator registers the whole frame and the crop is applied to the integrated image afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Cropping each contributor before registration — the original order — threw away the pixels the
/// alignment moves into the box from outside it: the review measured a 10x10 request coming out 7x9,
/// and <see cref="AoiCrop_WithStack_KeepsTheRequestedTenByTenBox"/> pins the same loss with a
/// whole-pixel offset.
/// </para>
/// <para>
/// Console redirection is process-wide, so this class joins the non-parallel collection used by the
/// GUI transcript snapshots and the other CLI end-to-end tests.
/// </para>
/// </remarks>
[Collection("gui-stdout")]
public class AoiIntegrationTests
{
    /// <summary>
    /// The review's reproduction shape: a 40x30 capture with a 10x10 request at x 15..24. The
    /// registered frames must still fill the whole box, whatever the measured offset is.
    /// </summary>
    /// <remarks>
    /// A frame this small cannot hold a wide tracking search, so the second frame carries a one-pixel
    /// translation — the motion the fine stage can still measure exactly. One pixel is enough to show
    /// the defect: cropping first leaves 9x10, while cropping the registered image keeps 10x10.
    /// </remarks>
    [Fact]
    public void AoiCrop_WithStack_KeepsTheRequestedTenByTenBox()
    {
        const int width = 40, height = 30;
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(
                directory,
                "capture.ser",
                width,
                height,
                Box(width, height, 15, 10, 0),
                Box(width, height, 15, 10, -1));
            string output = Path.Combine(directory, "out");

            RunResult result = Run(
                "-tif", "-stack=mean", "-stack_subpixel=off",
                "-en_surface_feature_tracking", "-en_ss",
                "-afb_x=15", "-afb_y=10", "-afb_width=16", "-afb_height=16",
                "-aoi_crop", "-aoi_x=15", "-aoi_y=10", "-aoi_width=10", "-aoi_height=10",
                "-od=" + output, input);

            Assert.True(result.ExitCode == 0, result.StdErr);
            Frame integrated = ReadTiff(Path.Combine(output, "capture.tif"));

            // The request survives registration: 10 columns and 10 rows, not the 9x10 the
            // pre-registration crop left behind.
            Assert.Equal(10, integrated.Width);
            Assert.Equal(10, integrated.Height);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// A capture whose feature is an exact whole-pixel translation: the aligned mean inside the box
    /// is the reference frame's box, sample for sample, and the box is exactly as requested.
    /// </summary>
    [Fact]
    public void AoiCrop_WithMean_MatchesTheReferenceFrameInsideTheBox()
    {
        const int width = 160, height = 120;
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            Frame reference = Box(width, height, 48, 40, 0);
            string input = WriteCapture(
                directory, "capture.ser", width, height, reference, Box(width, height, 48, 40, 3));
            string output = Path.Combine(directory, "out");

            RunResult result = Run(
                "-tif", "-stack=mean", "-stack_subpixel=off",
                "-en_surface_feature_tracking", "-en_ss",
                "-afb_x=48", "-afb_y=40", "-afb_width=24", "-afb_height=24",
                "-aoi_crop", "-aoi_x=48", "-aoi_y=40", "-aoi_width=24", "-aoi_height=24",
                "-od=" + output, input);

            Assert.True(result.ExitCode == 0, result.StdErr);
            Frame integrated = ReadTiff(Path.Combine(output, "capture.tif"));

            Assert.Equal(24, integrated.Width);
            Assert.Equal(24, integrated.Height);
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 24; x++)
                {
                    Assert.Equal(
                        reference.Samples[((40 + y) * width) + 48 + x],
                        integrated.Samples[(y * 24) + x]);
                }
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// The same request under canvas coverage: the image keeps the requested rectangle and the median
    /// of the registered frames, so the crop is independent of the coverage policy.
    /// </summary>
    [Fact]
    public void AoiCrop_WithMedianAndCanvas_KeepsTheRequestedRectangle()
    {
        const int width = 160, height = 120;
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            Frame reference = Box(width, height, 48, 40, 0);
            string input = WriteCapture(
                directory,
                "capture.ser",
                width,
                height,
                reference,
                Box(width, height, 48, 40, 3),
                Box(width, height, 48, 40, -1));
            string output = Path.Combine(directory, "out");

            RunResult result = Run(
                "-tif", "-stack=median", "-stack_coverage=canvas", "-stack_subpixel=off",
                "-en_surface_feature_tracking", "-en_ss",
                "-afb_x=48", "-afb_y=40", "-afb_width=24", "-afb_height=24",
                "-aoi_crop", "-aoi_x=48", "-aoi_y=40", "-aoi_width=24", "-aoi_height=24",
                "-od=" + output, input);

            Assert.True(result.ExitCode == 0, result.StdErr);
            Frame integrated = ReadTiff(Path.Combine(output, "capture.tif"));

            Assert.Equal(24, integrated.Width);
            Assert.Equal(24, integrated.Height);
            // Three registered copies of one block median to that block, so the canvas policy costs
            // nothing inside the requested box.
            Assert.Equal(reference.Samples[((40 + 5) * width) + 48 + 5], integrated.Samples[(5 * 24) + 5]);
            Assert.Equal(reference.Samples[((40 + 4) * width) + 48 + 5], integrated.Samples[(4 * 24) + 5]);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "opipp-aoi-tests", Guid.NewGuid().ToString("N"));

    private static string WriteCapture(string directory, string name, int width, int height, params Frame[] frames)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, width, height, 1, 8, frameCount: 0);
        foreach (Frame frame in frames)
        {
            writer.Write(frame);
        }

        return path;
    }

    /// <summary>
    /// A flat frame with an asymmetric 24x24 (or smaller, when the frame is) feature whose top-left
    /// corner sits at <c>(x + shiftX, y)</c>. The internal pattern is off-centre, so a correlation
    /// sees one unambiguous translation.
    /// </summary>
    private static Frame Box(int width, int height, int x, int y, int shiftX)
    {
        int size = Math.Min(24, Math.Min(width - x, height - y));
        var frame = new Frame(width, height, 1, 8);
        Array.Fill(frame.Samples, (ushort)40);
        for (int ly = 0; ly < size; ly++)
        {
            for (int lx = 0; lx < size; lx++)
            {
                int px = x + shiftX + lx, py = y + ly;
                if (px < 0 || px >= width || py < 0 || py >= height)
                {
                    continue;
                }

                ushort value = 60;
                if (lx is >= 3 and < 12 && ly is >= 3 and < 12) value = 220;
                if (ly is >= 18 and < 20) value = 25;
                if (lx is >= 17 and < 20 && ly < 8) value = 150;
                frame.Samples[(py * width) + px] = value;
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
}
