using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Xunit;

namespace Opipp.Core.Tests;

/// <summary>
/// A long joined sequence must not hold every input reader at once.
/// </summary>
/// <remarks>
/// The original implementation opened all inputs in the constructor. A joined PNG sequence therefore
/// retained every compressed IDAT payload before frame retention could help, so memory grew with the
/// inputs rather than with the retention budget. These tests pin the sequential contract.
/// </remarks>
public sealed class JoinedSourceTests
{
    [Fact]
    public void JoinedSource_KeepsAtMostOneReaderOpen()
    {
        string directory = CreateDirectory();
        try
        {
            string[] files = Enumerable.Range(0, 6)
                .Select(i => WriteSer(directory, $"part{i}.ser", frames: 2, seed: i))
                .ToArray();

            int live = 0;
            int maxLive = 0;
            JoinedFrameSource.OpenForTests = (path, depth) =>
            {
                IFrameSource inner = FrameSourceFactory.Open(path, depth);
                int now = Interlocked.Increment(ref live);
                if (now > maxLive) maxLive = now;
                return new CountingSource(inner, () => Interlocked.Decrement(ref live));
            };

            try
            {
                using var joined = new JoinedFrameSource(files);

                Assert.Equal(12, joined.FrameCount);

                int read = 0;
                while (joined.ReadFrame() is not null)
                {
                    read++;
                }

                Assert.Equal(12, read);
                Assert.Equal(1, maxLive);
            }
            finally
            {
                JoinedFrameSource.OpenForTests = null;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JoinedSource_ReadsEveryFrameAcrossFileBoundaries()
    {
        string directory = CreateDirectory();
        try
        {
            string[] files =
            [
                WriteSer(directory, "a.ser", frames: 3, seed: 10),
                WriteSer(directory, "b.ser", frames: 1, seed: 20),
                WriteSer(directory, "c.ser", frames: 2, seed: 30),
            ];

            using var joined = new JoinedFrameSource(files);

            Assert.Equal(6, joined.FrameCount);

            var values = new List<ushort>();
            while (joined.ReadFrame() is { } frame)
            {
                values.Add(frame.Samples[0]);
            }

            Assert.Equal(6, values.Count);
            Assert.Equal(new[] { 10, 11, 12, 20, 30, 31 }, values.Select(v => (int)v).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "opipp-join-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes a 4x1 mono 16-bit SER whose first sample is the seed plus the frame index.</summary>
    private static string WriteSer(string directory, string name, int frames, int seed)
    {
        string path = Path.Combine(directory, name);
        SerHeader header = SerHeader.Create(width: 4, height: 1, channels: 1, bitDepth: 16, frameCount: frames);

        var written = new List<Frame>(frames);
        for (int i = 0; i < frames; i++)
        {
            var frame = new Frame(4, 1, 1, 16) { Index = i };
            frame.Samples[0] = (ushort)(seed + i);
            frame.Samples[1] = 1;
            frame.Samples[2] = 2;
            frame.Samples[3] = 3;
            written.Add(frame);
        }

        using (var writer = new SerWriter(path, header))
        {
            foreach (Frame frame in written) writer.Write(frame);
        }

        return path;
    }

    private sealed class CountingSource(IFrameSource inner, Action onDispose) : IFrameSource
    {
        public int Width => inner.Width;
        public int Height => inner.Height;
        public int Channels => inner.Channels;
        public int BitDepth => inner.BitDepth;
        public int FrameCount => inner.FrameCount;
        public BayerPattern Bayer => inner.Bayer;
        public Frame? ReadFrame() => inner.ReadFrame();

        public void Dispose()
        {
            inner.Dispose();
            onDispose();
        }
    }
}
