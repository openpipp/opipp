namespace Pipp.Image.Tests;

using Pipp.Core.Alignment;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Pipp.Core.Pipeline;

/// <summary>
/// Pipeline-level tests for surface-feature tracking: the Processor must measure each accepted frame
/// against the first accepted frame inside the AFB, apply the <em>negated</em> measurement (the shift
/// that cancels the motion), and zero-fill the vacated border like the engine. Fixtures are synthetic
/// translations, so the expected offset is known exactly.
/// </summary>
public class SurfaceTrackingPipelineTests
{
    private const int Width = 64;
    private const int Height = 64;

    /// <summary>Deterministic noise: a unique, non-repeating correlation peak.</summary>
    private static Frame Texture(int seed)
    {
        var frame = new Frame(Width, Height, 1, 8);
        var random = new Random(seed);
        for (int i = 0; i < frame.Samples.Length; i++)
        {
            frame.Samples[i] = (ushort)random.Next(20, 256);
        }

        return frame;
    }

    private static Frame Translate(Frame source, int dx, int dy)
    {
        var frame = new Frame(Width, Height, 1, 8);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int sx = x - dx, sy = y - dy;
                frame.Samples[y * Width + x] = sx >= 0 && sx < Width && sy >= 0 && sy < Height
                    ? source.Samples[sy * Width + sx]
                    : (ushort)0;
            }
        }

        return frame;
    }

    private static (string Input, Frame[] Frames) WriteCapture(string directory, int frames)
    {
        var source = Texture(1234);
        var built = new Frame[frames];
        built[0] = source;
        for (int f = 1; f < frames; f++)
        {
            // A different displacement per frame, all well inside the AFB and the search radius.
            built[f] = Translate(source, 3 * f, -2 * f);
        }

        string path = Path.Combine(directory, "capture.ser");
        using var writer = new SerWriter(path, Width, Height, 1, 8);
        foreach (var frame in built) writer.Write(frame);
        return (path, built);
    }

    private static Frame[] ReadAll(string path)
    {
        using var reader = new SerReader(path);
        var frames = new List<Frame>();
        while (reader.ReadFrame() is { } frame) frames.Add(frame);
        return frames.ToArray();
    }

    [Fact]
    public void Tracking_AppliesTheNegatedMeasurement_AndZeroFills()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pipp-st-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, 4);
            string outputDirectory = Path.Combine(directory, "out");
            Directory.CreateDirectory(outputDirectory);
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                SurfaceTracking = true,
                AlignmentBox = new PixelRect(16, 16, 32, 32),
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(4, report.OutputFrames);
            var output = ReadAll(report.OutputFiles.Single());
            Assert.Equal(4, output.Length);

            for (int f = 0; f < output.Length; f++)
            {
                // The content moved by (+3f, -2f); the pipeline must shift it back by the negation,
                // and the vacated border must be zero rather than the frame median.
                var expected = FrameAligner.ShiftWithBackground(
                    source[f], new AlignmentOffset(-3.0 * f, 2.0 * f), fill: 0);

                Assert.Equal(expected.Samples, output[f].Samples);
            }

            // Frames 1..3 really are translated: they must not match the reference.
            Assert.NotEqual(source[0].Samples, source[1].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tracking_WithoutAlignmentBox_LeavesFramesUntouched()
    {
        // The engine emits -en_surface_feature_tracking without -afb_* as a no-op; saturn30 output
        // is byte-identical to a plain run in that case.
        string directory = Path.Combine(Path.GetTempPath(), "pipp-st-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, 3);
            string outputDirectory = Path.Combine(directory, "out");
            Directory.CreateDirectory(outputDirectory);
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                SurfaceTracking = true,
            };

            var report = new Processor(settings).Run([input]);
            var output = ReadAll(report.OutputFiles.Single());

            for (int f = 0; f < output.Length; f++)
            {
                Assert.Equal(source[f].Samples, output[f].Samples);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AlignerFallback_FlatBox_ReturnsNull()
    {
        // Cosine correlation is scale-free, so a constant box scores 1.0 at every offset; the guard
        // must reject it rather than report the first candidate in the search window.
        var flat = new Frame(32, 32, 1, 8);
        Array.Fill(flat.Samples, (ushort)40);

        Assert.Null(FrameAligner.EstimateTranslation(flat, flat, 8, new PixelRect(8, 8, 8, 8)));
    }

    [Fact]
    public void Tracking_UntrackableBox_LeavesFramesUntouched()
    {
        // A flat box has no feature: both the ZNCC tracker and the aligner fallback reject it, and
        // the pipeline must fall back to a zero offset rather than inventing one.
        string directory = Path.Combine(Path.GetTempPath(), "pipp-st-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "flat.ser");
            using (var writer = new SerWriter(input, Width, Height, 1, 8))
            {
                for (int f = 0; f < 2; f++)
                {
                    var flat = new Frame(Width, Height, 1, 8);
                    Array.Fill(flat.Samples, (ushort)40);
                    writer.Write(flat);
                }
            }

            string outputDirectory = Path.Combine(directory, "out");
            Directory.CreateDirectory(outputDirectory);
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                SurfaceTracking = true,
                AlignmentBox = new PixelRect(8, 8, 16, 16),
            };

            var report = new Processor(settings).Run([input]);
            var output = ReadAll(report.OutputFiles.Single());

            Assert.All(output, frame => Assert.All(frame.Samples, sample => Assert.Equal(40, sample)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
