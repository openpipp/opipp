namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Stacking;
using Xunit;

/// <summary>
/// <c>-qw</c> is a sequence-duplication policy: the extra copies are how the master is weighted.
/// The copy count therefore belongs to a frame's quality rank, not to the position it occupies in
/// the delivered sequence. <c>-nr</c> and <c>-no_ram_buff</c> reorder that sequence (source order
/// instead of score order), and spilling changes where its pixels are stored; none of them may move
/// weight from one frame to another.
/// </summary>
/// <remarks>
/// The fixture separates the two roles a frame plays. One bright sample at (0,0) carries the value
/// that reaches the master, while a checkerboard everywhere else carries the sharpness score that
/// decides the quality rank. That makes score order the exact reverse of source order, so a policy
/// that attaches copies by queue position visibly changes the master.
/// </remarks>
public class QualityWeightOrderingTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-qworder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// The review's seven-frame capture. Source bright values are 10, 20, 80, 160 (with increasing
    /// checkerboard texture) followed by three untextured frames. With <c>-q=4</c> quality keeps the
    /// four textured frames; their quality order is 160, 80, 20, 10 and their source order is the
    /// reverse. <c>-qw</c> on four frames copies the top quarter three times and the next quarter
    /// twice, so the weighted master sample is (160*3 + 80*2 + 20 + 10) / 7 = 95. Applying the same
    /// positional weights to the source order instead gives (10*3 + 20*2 + 80 + 160) / 7 = 44.
    /// </summary>
    private static string WriteReviewCapture(string dir)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, 16, 12, 1, 8);
        ushort[] bright = [10, 20, 80, 160, 5, 5, 5];
        int[] texture = [10, 20, 30, 40, 0, 0, 0];
        for (int f = 0; f < bright.Length; f++)
        {
            var frame = new Frame(16, 12, 1, 8);
            for (int y = 0; y < 12; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    if (x == 0 && y == 0) continue;
                    frame.Samples[(y * 16) + x] = (ushort)(((x + y) & 1) == 0 ? texture[f] : 0);
                }
            }

            frame.Samples[0] = bright[f];
            writer.Write(frame);
        }

        return path;
    }

    private static PipelineSettings Settings(string outputDirectory, bool renumber, bool noRamBuffers, long budget) => new()
    {
        OutputDirectory = outputDirectory,
        OutputExtension = ".tif",
        KeepFrames = 4,
        QualityWeighting = true,
        QualityMinDownsample = 1,
        QualityMaxDownsample = 1,
        MinPixelValue = 1,
        Stack = StackMethod.Mean,
        RenumberFrames = renumber,
        NoRamBuffers = noRamBuffers,
        RetentionBudgetBytes = budget,
    };

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {path}");
    }

    [Fact]
    public void QualityWeighting_MasterIsInvariantUnderSourceOrderAndSpill()
    {
        string dir = TempDir();
        try
        {
            string input = WriteReviewCapture(dir);

            (string Label, bool Renumber, bool NoRamBuffers, long Budget)[] runs =
            [
                ("default", true, false, long.MaxValue),
                ("-nr", false, false, long.MaxValue),
                ("-no_ram_buff", true, true, long.MaxValue),
                ("-nr -no_ram_buff", false, true, long.MaxValue),
                ("spilled", true, false, 1L),
            ];

            byte[]? baseline = null;
            foreach (var run in runs)
            {
                string outputDirectory = Path.Combine(dir, run.Label.Replace(' ', '_').Replace("-", string.Empty));
                var report = new Processor(Settings(outputDirectory, run.Renumber, run.NoRamBuffers, run.Budget)).Run([input]);

                Assert.Empty(report.FailedFiles);
                Assert.Equal(1, report.OutputFrames);

                string masterPath = Path.Combine(outputDirectory, "capture.tif");
                Assert.True(File.Exists(masterPath), $"{run.Label}: missing {masterPath}");

                // The analytic case: quality rank gets the weight, so every delivery order gives 95.
                // A positional policy applied after the -nr/-no_ram_buff resort would give 44.
                Assert.Equal((ushort)95, ReadTiff(masterPath).Samples[0]);

                byte[] master = File.ReadAllBytes(masterPath);
                baseline ??= master;
                Assert.True(master.AsSpan().SequenceEqual(baseline),
                    $"{run.Label}: master is not byte-identical to the default run's master");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void QualityWeighting_SourceOrderDelivery_KeepsCopiesOnTheirQualityRank()
    {
        // Same capture without stacking: the output sequence shows which frame received the copies.
        // Quality rank is 160, 80, 20, 10; -nr delivers the selected frames in source order
        // 10, 20, 80, 160. The copies stay with the ranked frames (160 gets a and b, 80 gets a), so
        // the names are 0, 1, 2, 2a, 3, 3a, 3b - not the 0,0a,0b,1,1a,2,3 a positional policy would
        // produce by treating the source-ordered queue as the ranking.
        string dir = TempDir();
        try
        {
            string input = WriteReviewCapture(dir);
            string outputDirectory = Path.Combine(dir, "out");
            var settings = Settings(outputDirectory, renumber: false, noRamBuffers: false, budget: long.MaxValue);
            settings.Stack = null;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(7, report.OutputFrames);
            string[] names = Directory.GetFiles(Path.Combine(outputDirectory, "capture"), "*.tif")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()!;
            Assert.Equal(["0", "1", "2", "2a", "3", "3a", "3b"], names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
