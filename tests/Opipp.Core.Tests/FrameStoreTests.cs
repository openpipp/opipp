using Opipp.Core.Frames;

namespace Opipp.Core.Tests;

public class FrameStoreTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opipp-store-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Frame MonoFrame(int width, int height, ushort seed, BayerPattern bayer = BayerPattern.None)
    {
        var frame = new Frame(width, height, 1, 16) { Bayer = bayer };
        for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = (ushort)(seed + i);
        return frame;
    }

    [Fact]
    public void Append_ThenRead_RoundTripsPixelsAndMetadata()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "store.bin");
            using var store = new FrameStore(path);

            Frame a = MonoFrame(4, 3, 100, BayerPattern.RGGB);
            var colour = new Frame(2, 2, 3, 8);
            Array.Fill(colour.Samples, (ushort)7);

            Assert.Equal(0, store.Append(a));
            Assert.Equal(1, store.Append(colour));
            Assert.Equal(2, store.Count);
            Assert.Equal((a.Samples.Length + colour.Samples.Length) * sizeof(ushort), store.StoredBytes);

            Frame readA = store.Read(0);
            Assert.Equal(a.Width, readA.Width);
            Assert.Equal(a.Height, readA.Height);
            Assert.Equal(a.Channels, readA.Channels);
            Assert.Equal(a.BitDepth, readA.BitDepth);
            Assert.Equal(BayerPattern.RGGB, readA.Bayer);
            Assert.Equal(a.Samples, readA.Samples);

            Frame readB = store.Read(1);
            Assert.Equal(3, readB.Channels);
            Assert.Equal(8, readB.BitDepth);
            Assert.Equal(colour.Samples, readB.Samples);

            // Reading twice must be independent: the caller may mutate what it gets back.
            readA.Samples[0] = 0;
            Assert.NotEqual(0, store.Read(0).Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_RemovesTheScratchFile()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "store.bin");
            var store = new FrameStore(path);
            store.Append(MonoFrame(8, 8, 1));
            Assert.True(File.Exists(path));

            store.Dispose();

            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_OutOfRange_Throws()
    {
        string dir = TempDir();
        try
        {
            using var store = new FrameStore(Path.Combine(dir, "store.bin"));
            store.Append(MonoFrame(2, 2, 1));

            Assert.Throws<ArgumentOutOfRangeException>(() => store.Read(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Read(-1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RetainedFrame_MaterialisesFromTheStore()
    {
        string dir = TempDir();
        try
        {
            using var store = new FrameStore(Path.Combine(dir, "store.bin"));
            Frame original = MonoFrame(3, 2, 42);
            int index = store.Append(original);

            var retained = RetainedFrame.Stored(store, index);

            Assert.True(retained.IsValid);
            Assert.Equal(index, retained.Index);
            Assert.Equal(original.Samples, retained.Frame.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RetainedFrame_Uninitialised_Throws()
    {
        RetainedFrame empty = default;
        Assert.False(empty.IsValid);
        Assert.Throws<InvalidOperationException>(() => empty.Frame);
    }
}
