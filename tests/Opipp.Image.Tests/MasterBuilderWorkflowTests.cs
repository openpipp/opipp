namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;

/// <summary>
/// Contract tests for the calibration-master workflow: reading a <c>.tmp</c> master as a subtraction
/// reference, the explicit <see cref="MasterBuildResult"/> contract, staged publication, cancellation,
/// and channel/depth preservation for mono, raw Bayer and already-colour captures.
/// </summary>
public class MasterBuilderWorkflowTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-masterflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes one or two single-channel frames whose sample at (x,y) is <paramref name="sample"/> + frameIndex * 2.</summary>
    private static string WriteMono(string dir, string name, int width, int height, int bitDepth,
        BayerPattern bayer, Func<int, int, int> sample, int frames = 2)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, width, height, 1, bitDepth, 0, bayer);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, bitDepth);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    frame[x, y] = (ushort)(sample(x, y) + f * 2);
            writer.Write(frame);
        }

        return path;
    }

    private static string WriteColour(string dir, string name, int width, int height, int bitDepth,
        Func<int, int, int, int> sample, int frames = 2)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, width, height, 3, bitDepth);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 3, bitDepth);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < 3; c++)
                        frame[x, y, c] = (ushort)(sample(x, y, c) + f * 2);
            writer.Write(frame);
        }

        return path;
    }

    // ---------------------------------------------------- .tmp through the factory

    [Fact]
    public void TmpMaster_OpensThroughTheFrameFactoryAsASingleFrame()
    {
        string dir = TempDir();
        try
        {
            var master = new Frame(3, 2, 3, 16);
            for (int i = 0; i < master.Samples.Length; i++) master.Samples[i] = (ushort)(i * 11 + 3);
            string path = Path.Combine(dir, "pipp_master_flat_dark.tmp");
            MasterTmpFile.Write(master, path);

            // A master is a handoff artifact, not a user-selectable capture input.
            Assert.False(FrameSourceFactory.IsSupported(path));

            using var source = FrameSourceFactory.Open(path);
            Assert.Equal(3, source.Width);
            Assert.Equal(2, source.Height);
            Assert.Equal(3, source.Channels);
            Assert.Equal(16, source.BitDepth);
            Assert.Equal(1, source.FrameCount);
            // The container has no CFA field, so nothing is invented.
            Assert.Equal(BayerPattern.None, source.Bayer);

            Frame? frame = source.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(master.Samples, frame!.Samples);
            Assert.Null(source.ReadFrame());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FlatDarkMaster_CanBeBuiltFromATmpSubtractionReference()
    {
        string dir = TempDir();
        try
        {
            // This is the sequence the GUI performs: build the flat dark, then build the corrected
            // flat using the .tmp it just wrote as the subtraction reference.
            string flatDarkFrame = WriteMono(dir, "flat_dark.ser", 4, 4, 16, BayerPattern.None, (_, _) => 5000);
            string flatDarkTmp = Path.Combine(dir, "pipp_master_flat_dark.tmp");
            MasterBuildResult flatDark = MasterFrameBuilder.Build(
                [flatDarkFrame], null, flatDarkTmp, null, 0);
            Assert.True(flatDark.Success, flatDark.Error);

            string flatFrame = WriteMono(dir, "flat.ser", 4, 4, 16, BayerPattern.None, (_, _) => 20000);
            string flatTmp = Path.Combine(dir, "pipp_master_flat.tmp");
            MasterBuildResult flat = MasterFrameBuilder.Build(
                [flatFrame], flatDarkTmp, flatTmp, null, 0);

            Assert.True(flat.Success, flat.Error);
            Assert.Equal(2, flat.FrameCount);

            Frame corrected = MasterTmpFile.Read(flatTmp);
            Assert.All(corrected.Samples, v => Assert.Equal(15000, v));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------- explicit failures

    [Fact]
    public void MissingInputs_AreExplicitFailuresAndPublishNothing()
    {
        string dir = TempDir();
        try
        {
            string masterPath = Path.Combine(dir, "master.tmp");
            string pgmPath = Path.Combine(dir, "master.pgm");

            MasterBuildResult missing = MasterFrameBuilder.Build(
                [Path.Combine(dir, "absent.ser")], null, masterPath, pgmPath, 0);
            Assert.False(missing.Success);
            Assert.Contains("not found", missing.Error, StringComparison.Ordinal);
            Assert.Contains("absent.ser", missing.Error, StringComparison.Ordinal);

            MasterBuildResult blank = MasterFrameBuilder.Build(["", "  "], null, masterPath, pgmPath, 0);
            Assert.False(blank.Success);
            Assert.Contains("empty path", blank.Error, StringComparison.Ordinal);

            MasterBuildResult none = MasterFrameBuilder.Build([], null, masterPath, pgmPath, 0);
            Assert.False(none.Success);
            Assert.Contains("No calibration input files", none.Error, StringComparison.Ordinal);

            Assert.False(File.Exists(masterPath));
            Assert.False(File.Exists(pgmPath));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingSubtractionMaster_IsAnExplicitFailure()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMono(dir, "flat.ser", 4, 4, 8, BayerPattern.None, (_, _) => 100);
            string masterPath = Path.Combine(dir, "master.tmp");

            MasterBuildResult result = MasterFrameBuilder.Build(
                [input], Path.Combine(dir, "absent_flat_dark.tmp"), masterPath, null, 0);

            Assert.False(result.Success);
            Assert.Contains("Subtraction master not found", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(masterPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SubtractionMasterWithWrongGeometry_IsReportedNotSwallowed()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMono(dir, "flat.ser", 4, 4, 16, BayerPattern.None, (_, _) => 100);
            string wrong = WriteMono(dir, "flat_dark.ser", 8, 8, 16, BayerPattern.None, (_, _) => 10);
            string masterPath = Path.Combine(dir, "master.tmp");

            MasterBuildResult result = MasterFrameBuilder.Build([input], wrong, masterPath, null, 0);

            Assert.False(result.Success);
            Assert.Contains("does not match the calibration frames", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(masterPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FailedPgmWrite_DoesNotPublishTheMaster()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMono(dir, "dark.ser", 4, 4, 8, BayerPattern.None, (_, _) => 40);
            string masterPath = Path.Combine(dir, "pipp_master_dark.tmp");
            // An existing directory at the PGM path makes that write fail after the .tmp was staged.
            string pgmPath = Path.Combine(dir, "pgm-target");
            Directory.CreateDirectory(pgmPath);

            MasterBuildResult result = MasterFrameBuilder.Build([input], null, masterPath, pgmPath, 0);

            Assert.False(result.Success);
            Assert.False(File.Exists(masterPath), "a half-written master set was published");
            Assert.Empty(Directory.GetFiles(dir, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FailedBuild_ReportsTheFrameCountThatWasActuallyStacked()
    {
        string dir = TempDir();
        try
        {
            // The frame limit asks for 5 but the source only carries 2.
            string input = WriteMono(dir, "dark.ser", 4, 4, 8, BayerPattern.None, (_, _) => 40, frames: 2);
            string masterPath = Path.Combine(dir, "pipp_master_dark.tmp");

            MasterBuildResult result = MasterFrameBuilder.Build([input], null, masterPath, null, 5);

            Assert.True(result.Success, result.Error);
            Assert.Equal(2, result.FrameCount);
            Assert.Equal(Path.GetFullPath(masterPath), result.MasterPath);
            Assert.Null(result.PgmPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -------------------------------------------------------------- cancellation

    [Fact]
    public void CancelledBeforeStarting_ThrowsAndWritesNothing()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMono(dir, "dark.ser", 4, 4, 8, BayerPattern.None, (_, _) => 40);
            string masterPath = Path.Combine(dir, "pipp_master_dark.tmp");
            string pgmPath = Path.Combine(dir, "pipp_master_dark.pgm");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => MasterFrameBuilder.Build(
                [input], null, masterPath, pgmPath, 0, null, cancellation.Token));

            Assert.False(File.Exists(masterPath));
            Assert.False(File.Exists(pgmPath));
            Assert.Empty(Directory.GetFiles(dir, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CancelledWhileStacking_LeavesNoMasterThatCouldBeReused()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMono(dir, "dark.ser", 4, 4, 8, BayerPattern.None, (_, _) => 40, frames: 3);
            string masterPath = Path.Combine(dir, "pipp_master_dark.tmp");
            string pgmPath = Path.Combine(dir, "pipp_master_dark.pgm");
            using var cancellation = new CancellationTokenSource();

            Assert.Throws<OperationCanceledException>(() => MasterFrameBuilder.Build(
                [input], null, masterPath, pgmPath, 3,
                _ => cancellation.Cancel(),
                cancellation.Token));

            Assert.True(cancellation.IsCancellationRequested);
            Assert.False(File.Exists(masterPath));
            Assert.False(File.Exists(pgmPath));
            Assert.Empty(Directory.GetFiles(dir, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------- channel / depth preservation

    [Theory]
    [InlineData(1, 8, BayerPattern.None)]   // mono 8-bit
    [InlineData(1, 16, BayerPattern.None)]  // mono 16-bit
    [InlineData(1, 8, BayerPattern.BGGR)]   // raw Bayer mosaic, 8-bit
    [InlineData(1, 16, BayerPattern.BGGR)]  // raw Bayer mosaic, 16-bit
    [InlineData(3, 8, BayerPattern.None)]   // already-colour RGB, 8-bit
    [InlineData(3, 16, BayerPattern.None)]  // already-colour RGB, 16-bit
    public void MasterKeepsSourceChannelsDepthAndSamples(int channels, int bitDepth, BayerPattern bayer)
    {
        string dir = TempDir();
        try
        {
            const int width = 4, height = 3;
            int max = bitDepth == 8 ? 255 : 65535;
            int Base(int x, int y, int c) => (20 + x * 3 + y * 5 + c * 7) % (max / 4);

            string input = channels == 1
                ? WriteMono(dir, "capture.ser", width, height, bitDepth, bayer, (x, y) => Base(x, y, 0))
                : WriteColour(dir, "capture.ser", width, height, bitDepth, Base);

            string masterPath = Path.Combine(dir, "pipp_master_flat.tmp");
            string pgmPath = Path.Combine(dir, "pipp_master_flat.pgm");

            MasterBuildResult result = MasterFrameBuilder.Build([input], null, masterPath, pgmPath, 0);

            Assert.True(result.Success, result.Error);
            Assert.Equal(2, result.FrameCount);

            // The .tmp is the authoritative handoff: same channel count, same sample depth, and the
            // two frames (v and v+2) average to v+1 exactly.
            Frame master = MasterTmpFile.Read(masterPath);
            Assert.Equal(channels, master.Channels);
            Assert.Equal(bitDepth, master.BitDepth);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        Assert.Equal(Base(x, y, c) + 1, master[x, y, c]);
                    }
                }
            }

            // The PGM is the one-channel legacy fallback: identical for mono, luma for colour.
            Frame pgm = PgmFile.Read(pgmPath);
            Assert.Equal(1, pgm.Channels);
            Assert.Equal(bitDepth, pgm.BitDepth);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort expected = channels == 1
                        ? master[x, y]
                        : (ushort)((299 * master[x, y, 0] + 587 * master[x, y, 1] + 114 * master[x, y, 2] + 500) / 1000);
                    Assert.Equal(expected, pgm[x, y]);
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
