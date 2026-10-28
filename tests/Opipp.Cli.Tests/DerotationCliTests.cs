using System.Globalization;
using System.Text.RegularExpressions;
using Opipp.Core.Alignment;
using Opipp.Core.Cli;
using Opipp.Core.Diagnostics;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Pipeline;

namespace Opipp.Cli.Tests;

/// <summary>
/// The field-rotation switches (<c>-derotate=on|off</c> and <c>-derotate_search=&lt;degrees&gt;</c>,
/// OPIPP extensions): parsing, the <c>-stack</c> dependency, the help text, and what a derotated
/// integration actually does to a rotating capture.
/// </summary>
/// <remarks>
/// <para>
/// The end-to-end cases run the real entry point on synthetic SER captures, like the other CLI
/// integration tests, and compare the images the pipeline wrote. Console redirection is
/// process-wide, so this class joins the non-parallel <c>gui-stdout</c> collection.
/// </para>
/// <para>
/// The sharpness case is the one that would fail if the estimator were a no-op: a sequence whose
/// field rotates is integrated twice with the same command line, once with <c>-derotate=on</c> and
/// once without, and the second derivative of the edges must be at least three times stronger in
/// the derotated image.
/// </para>
/// </remarks>
[Collection("gui-stdout")]
public class DerotationCliTests
{
    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("ON", true)]
    [InlineData("Off", false)]
    public void Derotate_IsParsedCaseInsensitively(string value, bool expected)
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", $"-derotate={value}"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(expected, result.Options.Derotate);
    }

    [Theory]
    [InlineData("0.1", 0.1)]
    [InlineData("5", 5.0)]
    [InlineData("12.5", 12.5)]
    [InlineData("45", 45.0)]
    public void DerotateSearch_IsParsedAcrossItsRange(string value, double expected)
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", $"-derotate_search={value}"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(expected, result.Options.DerotateSearchDegrees);
    }

    /// <summary>The default is off and 5 degrees: an absent switch leaves both at "not requested".</summary>
    [Fact]
    public void AbsentSwitches_LeaveDerotationUnrequested()
    {
        ParseResult result = CommandLineParser.Parse(["-tif", "capture.ser"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Null(result.Options.Derotate);
        Assert.Null(result.Options.DerotateSearchDegrees);
        Assert.Null(result.Options.StackMethod);
    }

    /// <summary>A qualifier may precede -stack: the cross-switch check runs after the whole line.</summary>
    [Fact]
    public void DerotateBeforeStack_IsAccepted()
    {
        ParseResult result = CommandLineParser.Parse(["-derotate=on", "-derotate_search=8", "-stack=sum"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(true, result.Options.Derotate);
        Assert.Equal(8.0, result.Options.DerotateSearchDegrees);
        Assert.Equal(PippStackMethod.Sum, result.Options.StackMethod);
    }

    [Theory]
    [InlineData("-derotate=on")]
    [InlineData("-derotate=off")]
    [InlineData("-derotate_search=5")]
    public void WithoutStack_IsAConfigurationError(string qualifier)
    {
        ParseResult result = CommandLineParser.Parse(["-tif", qualifier]);

        Assert.False(result.Success);
        Assert.Contains(
            result.Errors,
            error => error.Contains(qualifier.Split('=')[0], StringComparison.Ordinal) &&
                     error.Contains("-stack=mean|sum|median", StringComparison.Ordinal));
    }

    [Fact]
    public void DerotateWithoutAValue_IsReported()
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", "-derotate"]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("-derotate", StringComparison.Ordinal));
        Assert.Null(result.Options.Derotate);
    }

    [Theory]
    [InlineData("-derotate=maybe", "maybe")]
    [InlineData("-derotate_search=0", "0")]
    [InlineData("-derotate_search=0.05", "0.05")]
    [InlineData("-derotate_search=-3", "-3")]
    [InlineData("-derotate_search=46", "46")]
    [InlineData("-derotate_search=abc", "abc")]
    [InlineData("-derotate_search=", "")]
    public void BadValues_AreReportedWithTheValue(string token, string value)
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", token]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains($"'{value}'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("Unknown switch", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    // Help
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Help_ListsTheDerotationSwitchesUnderTheFinalImageSection()
    {
        RunResult help = Run("-help");

        Assert.Equal(0, help.ExitCode);
        int finalImage = help.StdOut.IndexOf("Final image (OPIPP extension)", StringComparison.Ordinal);
        int derotate = help.StdOut.IndexOf("-derotate=on", StringComparison.Ordinal);
        int search = help.StdOut.IndexOf("-derotate_search=<deg>", StringComparison.Ordinal);

        Assert.True(finalImage >= 0, "the help no longer has a Final image section");
        Assert.True(derotate > finalImage, "-derotate is not documented under the Final image section");
        Assert.True(search > derotate, "-derotate_search is not documented after -derotate");

        // The explanation has to say why the stage exists: translation-only alignment, the edges a
        // rotating field smears, and the shared crop rectangle.
        Assert.Contains("translation-only", help.StdOut, StringComparison.Ordinal);
        Assert.Contains("rotated about the image centre", help.StdOut, StringComparison.Ordinal);
        Assert.Contains("cropped to the one rectangle", help.StdOut, StringComparison.Ordinal);
        Assert.Contains("Off by default", help.StdOut, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Command-line contract
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("-derotate=on")]
    [InlineData("-derotate_search=5")]
    public void RunWithoutStack_ExitsTwoNamingTheSwitch(string qualifier)
    {
        RunResult result = Run("-tif", qualifier);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(qualifier.Split('=')[0], result.StdErr, StringComparison.Ordinal);
        Assert.Contains("-stack=mean|sum|median", result.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-derotate_search=0", "0")]
    [InlineData("-derotate_search=46", "46")]
    [InlineData("-derotate_search=notanumber", "notanumber")]
    [InlineData("-derotate=sometimes", "sometimes")]
    public void BadValues_ExitTwoNamingTheValue(string token, string value)
    {
        RunResult result = Run("-tif", "-stack=mean", token);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"'{value}'", result.StdErr, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Sharpness: the reason the switch exists
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A 20-frame capture whose field rotates 0.05 degrees per frame (0.95 degrees end to end) and
    /// drifts a fraction of a pixel per frame is integrated twice with the same command line, once
    /// with <c>-derotate=on</c> and once without. The translation-only run smears every feature
    /// along an arc and leaves the drift where it falls; the derotated run rotates each frame back
    /// onto frame 0, crops them all to one rectangle and measures each frame's translation, so the
    /// variance of the edge Laplacian must be at least three times larger.
    /// </summary>
    /// <remarks>
    /// The comparison is deliberately one command line with and without the switch, because that is
    /// what an operator changes. Without <c>-derotate</c> the run registers nothing: PIPP's
    /// translation alignment only runs when <c>-ctr</c> or the surface-tracking switches ask for it,
    /// so the small drift is left in the mean as well as the rotation.
    /// </remarks>
    [Fact]
    public void RotatingCapture_DerotatedIntegration_KeepsEdgesSharp()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "rotating.ser", RotatingFrames(rotating: true));
            string plainOutput = Path.Combine(directory, "plain");
            string derotatedOutput = Path.Combine(directory, "derotated");

            RunResult plain = Run("-ser", "-stack=mean", "-od=" + plainOutput, input);
            RunResult derotated = Run(
                "-ser", "-stack=mean", "-derotate=on", "-od=" + derotatedOutput, input);

            Assert.Equal(0, plain.ExitCode);
            Assert.Equal(string.Empty, plain.StdErr);
            Assert.Equal(0, derotated.ExitCode);
            Assert.Equal(string.Empty, derotated.StdErr);

            Frame plainFrame = ReadSingleFrame(plainOutput);
            Frame sharpFrame = ReadSingleFrame(derotatedOutput);

            // The rotated frames are cropped to the rectangle every rotation can fill, so the
            // derotated image is smaller. Both measurements cover the same physical region: the
            // centred rectangle of the smaller image, in each frame's own coordinates.
            Assert.True(
                sharpFrame.Width <= plainFrame.Width - 3 && sharpFrame.Height <= plainFrame.Height - 3,
                $"a rotating capture was not cropped by the estimated rotation " +
                $"({plainFrame.Width}x{plainFrame.Height} -> {sharpFrame.Width}x{sharpFrame.Height})");

            const int inset = 8;
            int windowWidth = sharpFrame.Width - (2 * inset);
            int windowHeight = sharpFrame.Height - (2 * inset);
            int offsetX = (plainFrame.Width - sharpFrame.Width) / 2;
            int offsetY = (plainFrame.Height - sharpFrame.Height) / 2;

            double sharpVariance = LaplacianVariance(sharpFrame, inset, inset, windowWidth, windowHeight);
            double plainVariance = LaplacianVariance(
                plainFrame, offsetX + inset, offsetY + inset, windowWidth, windowHeight);

            Assert.True(
                sharpVariance >= 3.0 * plainVariance,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"edge Laplacian variance {sharpVariance:F1} for the derotated image is not 3x " +
                    $"the translation-only {plainVariance:F1}"));
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// A capture that does not rotate must still integrate with <c>-derotate=on</c>: the stage is
    /// never silently switched off, the angles are simply near zero, the crop keeps the whole frame,
    /// and the image matches the translation-only one within resampling tolerance.
    /// </summary>
    [Fact]
    public void StillCapture_StillSucceedsAndMatchesThePlainImage()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "still.ser", RotatingFrames(rotating: false));
            string plainOutput = Path.Combine(directory, "plain");
            string derotatedOutput = Path.Combine(directory, "derotated");

            RunResult plain = Run("-ser", "-stack=mean", "-od=" + plainOutput, input);
            RunResult derotated = Run(
                "-ser", "-stack=mean", "-derotate=on", "-od=" + derotatedOutput, input);

            Assert.Equal(0, plain.ExitCode);
            Assert.Equal(0, derotated.ExitCode);
            Assert.Equal(string.Empty, derotated.StdErr);

            Frame plainFrame = ReadSingleFrame(plainOutput);
            Frame derotatedFrame = ReadSingleFrame(derotatedOutput);

            // Zero rotation leaves the shared crop at (very nearly) the full canvas, so both images
            // are the same size and can be compared sample for sample.
            Assert.True(
                Math.Abs(plainFrame.Width - derotatedFrame.Width) <= 4 &&
                Math.Abs(plainFrame.Height - derotatedFrame.Height) <= 4,
                $"a still capture was cropped by -derotate ({plainFrame.Width}x{plainFrame.Height} -> " +
                $"{derotatedFrame.Width}x{derotatedFrame.Height})");

            double difference = MeanAbsoluteDifference(plainFrame, derotatedFrame);
            Assert.True(
                difference <= 1.0,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"a still capture integrated differently with -derotate=on (mean |difference| {difference:F2})"));
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// <c>-derotate=off</c> is an explicit "no", so the integration must be byte-for-byte the run
    /// that never spelled the switch: no crop, no resampling, nothing that could shift a pixel.
    /// </summary>
    [Fact]
    public void DerotateOff_WritesTheSameBytesAsNoSwitchAtAll()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "rotating.ser", RotatingFrames(rotating: true));
            string plainOutput = Path.Combine(directory, "plain");
            string offOutput = Path.Combine(directory, "off");

            RunResult plain = Run("-ser", "-stack=mean", "-od=" + plainOutput, input);
            RunResult off = Run("-ser", "-stack=mean", "-derotate=off", "-od=" + offOutput, input);

            Assert.Equal(0, plain.ExitCode);
            Assert.Equal(0, off.ExitCode);

            byte[] plainBytes = File.ReadAllBytes(Directory.GetFiles(plainOutput, "*.ser").Single());
            byte[] offBytes = File.ReadAllBytes(Directory.GetFiles(offOutput, "*.ser").Single());
            Assert.Equal(plainBytes, offBytes);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Raw Bayer
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A raw mosaic cannot be rotated: resampling across the 2x2 colour lattice would invent
    /// colours. The run refuses the combination, names <c>-db</c>, and writes nothing.
    /// </summary>
    [Fact]
    public void RawBayer_WithDerotation_FailsNamingDbAndWritesNothing()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(
                directory, "raw.ser", BayerPattern.RGGB, RotatingFrames(rotating: true));
            string output = Path.Combine(directory, "out");

            RunResult result = Run("-tif", "-stack=mean", "-derotate=on", "-od=" + output, input);

            Assert.True(
                result.ExitCode is 1 or 2,
                $"raw Bayer with -derotate exited {result.ExitCode}; expected a controlled failure");
            Assert.Contains("-db", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("-derotate", result.StdErr, StringComparison.Ordinal);
            Assert.True(
                !Directory.Exists(output) || Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length == 0,
                "a refused derotated run left output behind");
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// <c>-check</c> reports the same problem before processing starts, so a validation-only run
    /// exits 2 naming <c>-db</c> instead of silently passing a command line that cannot work.
    /// </summary>
    [Fact]
    public void RawBayer_WithDerotation_FailsCheckNamingDb()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(
                directory, "raw.ser", BayerPattern.RGGB, RotatingFrames(rotating: true));

            RunResult result = Run("-tif", "-stack=mean", "-derotate=on", "-check", input);

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("Command line check: FAILED", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("-db", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The protocol line and the interpolated curve
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The run reports what it measured in one protocol line: the number of sampled keyframes, the
    /// largest angle and the crop every frame was reduced to.
    /// </summary>
    [Fact]
    public void DerotatedRun_WritesOneDerotationProtocolLine()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "rotating.ser", RotatingFrames(rotating: true));
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(directory, "out"),
                OutputExtension = ".ser",
                WriteLogFile = false,
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
                Derotation = new DerotationOptions { Enabled = true, Keyframes = 6 },
            };

            var protocolText = new StringWriter(CultureInfo.InvariantCulture);
            var processor = new Processor(settings, null, new EngineProtocolWriter(protocolText));
            ProcessingReport report = processor.Run([input]);

            Assert.Equal(1, report.OutputFrames);
            string protocol = protocolText.ToString();
            string[] lines = protocol.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.StartsWith("Derotation:", StringComparison.Ordinal))
                .ToArray();

            Assert.Single(lines);
            var match = Regex.Match(
                lines[0],
                @"^Derotation: (?<keyframes>\d+) keyframes, max \|angle\| (?<angle>\d+\.\d{2}) deg, " +
                @"crop (?<width>\d+)x(?<height>\d+)$");
            Assert.True(match.Success, $"unexpected derotation line: '{lines[0]}'");

            // Six keyframes over 20 frames is what the run asked for, and the angle has to be the
            // capture's real sweep (0.95 degrees), not a placeholder zero. A six-sample curve is
            // coarse, so this asserts the order of magnitude the estimator can honestly claim here.
            Assert.Equal(6, int.Parse(match.Groups["keyframes"].Value, CultureInfo.InvariantCulture));
            double maxAngle = double.Parse(match.Groups["angle"].Value, CultureInfo.InvariantCulture);
            Assert.InRange(maxAngle, 0.5, 2.5);

            int cropWidth = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
            int cropHeight = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
            Assert.True(
                cropWidth < Width && cropHeight < Height,
                $"the reported crop {cropWidth}x{cropHeight} was not reduced for a rotating capture");
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>Keyframes are spread evenly and always include the first frame and the last.</summary>
    [Fact]
    public void Keyframes_AreEvenlySpacedAndIncludeBothEnds()
    {
        int[] indices = Processor.DerotationKeyframes(20, 12);

        Assert.Equal(12, indices.Length);
        Assert.Equal(0, indices[0]);
        Assert.Equal(19, indices[^1]);
        for (int i = 1; i < indices.Length; i++)
        {
            Assert.True(indices[i] > indices[i - 1], "keyframe indices are not strictly increasing");
        }

        // A selection smaller than the keyframe budget samples every frame exactly once.
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Processor.DerotationKeyframes(5, 12));
        Assert.Equal(new[] { 0 }, Processor.DerotationKeyframes(1, 12));
    }

    /// <summary>The angle curve between samples is the straight line joining them.</summary>
    [Fact]
    public void Interpolation_IsPiecewiseLinearBetweenSamples()
    {
        int[] samples = [0, 10, 20];
        double[] measured = [0.0, 1.0, 3.0];

        double[] angles = Processor.DerotationInterpolate(samples, measured, 21);

        Assert.Equal(0.0, angles[0], 9);
        Assert.Equal(0.5, angles[5], 9);
        Assert.Equal(1.0, angles[10], 9);
        Assert.Equal(2.0, angles[15], 9);
        Assert.Equal(3.0, angles[20], 9);
        // Every frame gets an angle, including the ones between samples.
        Assert.Equal(21, angles.Length);
    }

    /// <summary>A sampled curve is reproduced exactly when every frame is a sample.</summary>
    [Fact]
    public void Interpolation_WithEveryFrameSampled_IsTheIdentity()
    {
        double[] measured = [0.0, -0.25, 0.5, 1.25];

        double[] angles = Processor.DerotationInterpolate([0, 1, 2, 3], measured, 4);

        Assert.Equal(measured, angles);
    }

    // ---------------------------------------------------------------------------------------------
    // Synthetic capture
    // ---------------------------------------------------------------------------------------------

    private const int Width = 320;
    private const int Height = 240;
    private const double CentreX = (Width - 1) / 2.0;
    private const double CentreY = (Height - 1) / 2.0;

    /// <summary>Disc radius as a fraction of the shorter side: leaves sky all round the limb.</summary>
    private const double RadiusFraction = 0.42;

    private static double DiscRadius => Height * RadiusFraction;

    /// <summary>Degrees of field rotation added per frame; 20 frames sweep just under one degree.</summary>
    private const double DegreesPerFrame = 0.05;

    /// <summary>
    /// Drift per frame, in pixels. Small for a real capture (a few pixels over the whole sequence),
    /// but enough that a run which registers nothing but the rotation is visibly blurred.
    /// </summary>
    private const double DriftPerFrameX = 0.7;
    private const double DriftPerFrameY = 0.35;

    /// <summary>Width of every edge, in pixels: a plausible PSF rather than a one-pixel step.</summary>
    private const double EdgeSoftness = 2.0;

    private const int FrameCount = 20;

    /// <summary>
    /// Craters on the synthetic disc, as (u, v, radius, depth) in units of the disc radius. They are
    /// spread over the whole disc and none of them repeats another, so a rotation has one
    /// unambiguous answer, and the outer ones are the sharpest test of rotation because the arc a
    /// rotation sweeps grows with radius.
    /// </summary>
    private static readonly (double U, double V, double Radius, double Depth)[] Craters =
    [
        (0.55, -0.38, 0.26, 0.55),
        (-0.62, 0.42, 0.20, 0.48),
        (0.12, 0.72, 0.16, 0.42),
        (-0.28, -0.68, 0.13, 0.38),
        (0.80, 0.46, 0.11, 0.34),
        (-0.84, -0.12, 0.09, 0.30),
        (-0.10, 0.18, 0.07, 0.26),
        (0.32, 0.28, 0.37, 0.18),
    ];

    /// <summary>
    /// One frame of the capture: the cratered disc, sampled as it appears after the field rotated by
    /// <paramref name="angleDegrees"/> and drifted by (<paramref name="driftX"/>,
    /// <paramref name="driftY"/>). Each frame is generated analytically from the same scene, so the
    /// only differences between frames are the rotation and the drift: no source image is
    /// interpolated on the way in, which would blur what is being measured.
    /// </summary>
    private static Frame RotatedFrame(double angleDegrees, double driftX, double driftY)
    {
        var frame = new Frame(Width, Height, 1, 8);
        double theta = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(theta);
        double sin = Math.Sin(theta);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                // The content was rotated and moved, so the scene coordinate is the inverse map of
                // the output pixel: un-translate, then un-rotate about the image centre.
                double dx = x - CentreX - driftX;
                double dy = y - CentreY - driftY;
                double u = (dx * cos) + (dy * sin);
                double v = (-dx * sin) + (dy * cos);
                frame.Samples[(y * Width) + x] = (ushort)Math.Round(Scene(u, v));
            }
        }

        return frame;
    }

    /// <summary>The cratered disc: a soft-edged limb, eight dark craters with one-pixel bright rims and a speck.</summary>
    private static double Scene(double u, double v)
    {
        double radius = DiscRadius;
        double distance = Math.Sqrt((u * u) + (v * v));
        double coverage = Ramp(radius - distance);
        if (coverage <= 0) return 20;

        double value = 20 + (150 * coverage);

        // Limb brightening on the inside of the edge.
        value += 55 * Ramp(distance - (radius - (2.5 * EdgeSoftness))) * coverage;

        foreach ((double craterU, double craterV, double craterRadius, double depth) in Craters)
        {
            double cx = craterU * radius;
            double cy = craterV * radius;
            double size = craterRadius * radius;
            double d = Math.Sqrt(((u - cx) * (u - cx)) + ((v - cy) * (v - cy)));
            double cover = Ramp(size - d);
            if (cover <= 0) continue;

            value -= depth * 150 * cover;
            // A bright rim: the compact high-frequency feature a rotation smears along an arc.
            value += 30 * Ramp((1.6 * EdgeSoftness) - Math.Abs(d - size));
        }

        double speckX = 0.72 * radius;
        double speckY = -0.80 * radius;
        double speckRadius = Math.Max(1.5, 0.05 * radius);
        double speckDistance = Math.Sqrt(((u - speckX) * (u - speckX)) + ((v - speckY) * (v - speckY)));
        value += 40 * Ramp(speckRadius - speckDistance);

        return Math.Clamp(value, 0, 255);
    }

    /// <summary>A soft edge of width <see cref="EdgeSoftness"/>: 0 outside it, 1 inside.</summary>
    private static double Ramp(double distanceInside) =>
        Math.Clamp(0.5 + (distanceInside / EdgeSoftness), 0.0, 1.0);

    /// <summary>
    /// The 20-frame capture: the rotating one turns a twentieth of a degree per frame and drifts a
    /// fraction of a pixel per frame; the still capture does neither.
    /// </summary>
    private static Frame[] RotatingFrames(bool rotating)
    {
        var frames = new Frame[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            double angle = rotating ? i * DegreesPerFrame : 0.0;
            double driftX = rotating ? i * DriftPerFrameX : 0.0;
            double driftY = rotating ? i * DriftPerFrameY : 0.0;
            frames[i] = RotatedFrame(angle, driftX, driftY);
            frames[i].Index = i;
        }

        return frames;
    }

    // ---------------------------------------------------------------------------------------------
    // Image measurement
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Variance of the 4-neighbour Laplacian over a window: the second derivative of the image,
    /// which is large wherever an edge is sharp and collapses when the same edge is smeared.
    /// </summary>
    private static double LaplacianVariance(Frame frame, int left, int top, int width, int height)
    {
        double sum = 0;
        double sumOfSquares = 0;
        int count = 0;
        for (int y = top; y < top + height; y++)
        {
            for (int x = left; x < left + width; x++)
            {
                int index = (y * frame.Width) + x;
                double centre = frame.Samples[index];
                double laplacian = (4 * centre)
                    - frame.Samples[index - 1]
                    - frame.Samples[index + 1]
                    - frame.Samples[index - frame.Width]
                    - frame.Samples[index + frame.Width];
                sum += laplacian;
                sumOfSquares += laplacian * laplacian;
                count++;
            }
        }

        double mean = sum / count;
        return (sumOfSquares / count) - (mean * mean);
    }

    /// <summary>Mean absolute difference over the region the two frames share.</summary>
    private static double MeanAbsoluteDifference(Frame left, Frame right)
    {
        int width = Math.Min(left.Width, right.Width);
        int height = Math.Min(left.Height, right.Height);
        long total = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                total += Math.Abs(left.Samples[(y * left.Width) + x] - right.Samples[(y * right.Width) + x]);
            }
        }

        return (double)total / (width * height);
    }

    // ---------------------------------------------------------------------------------------------
    // Files
    // ---------------------------------------------------------------------------------------------

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "pipp-derotate-tests", Guid.NewGuid().ToString("N"));

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

    /// <summary>Reads the single integrated image from an output directory.</summary>
    private static Frame ReadSingleFrame(string outputDirectory)
    {
        string[] files = Directory.GetFiles(outputDirectory, "*.ser");
        Assert.Single(files);
        using var reader = new SerReader(files[0]);
        return reader.ReadFrame() ?? throw new InvalidDataException($"'{files[0]}' holds no frame");
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
