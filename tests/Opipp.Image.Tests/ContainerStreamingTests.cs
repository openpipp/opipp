namespace Opipp.Image.Tests;

using System.Security.Cryptography;
using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Xunit;

/// <summary>
/// Container streaming (<c>-avi</c>/<c>-ser</c>): the writers pull frames from a lazy, index-based
/// sequence, so an animation is never materialised. These tests pin the bytes produced by the AVI
/// animation (the safety net for the rewrite), the arithmetic frame count a container header needs,
/// and the deferred, index-based nature of the sequence itself.
/// </summary>
public class ContainerStreamingTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Writes a five-frame mono SER whose pixels vary across both axes and from frame to frame, so
    /// every AVI chunk differs and the pinned hash covers real pixel bytes, not repeated padding.
    /// </summary>
    private static string WriteSyntheticSer(string dir, int frames = 5)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, 24, 18, 1, 8);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(24, 18, 1, 8);
            for (int y = 0; y < 18; y++)
            {
                for (int x = 0; x < 24; x++)
                {
                    frame.Samples[(y * 24) + x] = (ushort)(((x * 3) + (y * 5) + (f * 7)) % 256);
                }
            }

            writer.Write(frame);
        }

        return path;
    }

    private static PipelineSettings Settings(string outputDir, string extension) => new()
    {
        OutputDirectory = outputDir,
        OutputExtension = extension,
        Fps = 25,
        WriteLogFile = false,
    };

    /// <summary>Builds a queue of flat mono frames whose first sample is 10, 20, 30, ...</summary>
    private static List<(RetainedFrame Frame, double Score, AlignmentOffset Offset, string Suffix)> FlatQueue(int count)
    {
        var queue = new List<(RetainedFrame Frame, double Score, AlignmentOffset Offset, string Suffix)>(count);
        for (int i = 0; i < count; i++)
        {
            var frame = new Frame(8, 6, 1, 8);
            Array.Fill(frame.Samples, (ushort)(10 * (i + 1)));
            queue.Add((RetainedFrame.Inline(frame), 0.0, AlignmentOffset.Zero, string.Empty));
        }

        return queue;
    }

    private static List<int> Values(IEnumerable<Frame> frames) => frames.Select(frame => (int)frame.Samples[0]).ToList();

    // ------------------------------------------------------------------ A1: byte baseline

    /// <summary>
    /// A1: the byte-level baseline for the streaming rewrite. <c>-avi -anim_fwd -anim_rev</c> on a
    /// five-frame capture writes 1 2 3 4 5 4 3 2 1 = 9 frames; this hash pins every byte of the file
    /// (headers, chunk order, padding and index), so any change to the sequence, the frame count or
    /// the writer's layout fails here rather than in a comparison tool.
    /// </summary>
    [Fact]
    public void AviAnimation_ForwardReverse_OutputBytesArePinned()
    {
        string dir = TempDir();
        string input = WriteSyntheticSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".avi");
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true };

        new Processor(settings).Run([input]);

        string output = Path.Combine(dir, "out", "capture.avi");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)));
        Assert.Equal("DE5B6EA7AB4EB46EC1D8360C0117526DF3E2ABED35201F901807380BC41598FF", hash);

        using var reader = new AviReader(output);
        int frames = 0;
        while (reader.ReadFrame() is not null)
        {
            frames++;
        }

        Assert.Equal(9, frames);
    }

    /// <summary>
    /// A4: <c>-rgb</c> writes three containers from a single enumeration of the lazy sequence. Each
    /// channel file must carry the full animated sequence (and the same values, since the source is
    /// mono), which fails if a channel restarts or short-circuits the shared loop.
    /// </summary>
    [Fact]
    public void AviAnimation_SplitRgb_WritesEveryChannelWithTheFullSequence()
    {
        string dir = TempDir();
        string input = WriteSyntheticSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".avi");
        settings.SplitRgb = true;
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true };

        new Processor(settings).Run([input]);

        var perChannel = new List<List<int>>();
        foreach (char letter in "rgb")
        {
            string path = Path.Combine(dir, "out", letter + "_capture..avi");
            Assert.True(File.Exists(path), $"missing {path}");
            using var reader = new AviReader(path);
            var values = new List<int>();
            Frame? frame;
            while ((frame = reader.ReadFrame()) is not null)
            {
                values.Add(frame.Samples[0]);
            }

            perChannel.Add(values);
        }

        Assert.All(perChannel, values => Assert.Equal(9, values.Count));
        Assert.Equal(perChannel[0], perChannel[1]);
        Assert.Equal(perChannel[0], perChannel[2]);
    }

    // ------------------------------------------------------------------ A2: arithmetic count

    /// <summary>
    /// The pre-streaming sequencer, kept as the oracle for the rewrite: materialise the plain
    /// frames, build the blocks, then repeat the whole sequence. The lazy sequencer must produce
    /// exactly these frames, in this order, for every option combination.
    /// </summary>
    private static List<Frame> MaterialisedReference(List<Frame> plain, AnimationOptions animation)
    {
        var sequence = new List<Frame>(plain.Count * 3);
        if (animation.Forward)
        {
            sequence.AddRange(plain);
            for (int hold = 0; hold < animation.ForwardLastFrameRepeats; hold++)
            {
                sequence.Add(plain[^1]);
            }
        }

        if (animation.Reverse)
        {
            int start = animation.Forward ? plain.Count - 2 : plain.Count - 1;
            for (int i = start; i >= 0; i--)
            {
                sequence.Add(plain[i]);
            }

            for (int hold = 0; hold < animation.ReverseLastFrameRepeats; hold++)
            {
                sequence.Add(plain[0]);
            }
        }

        if (sequence.Count == 0)
        {
            sequence = plain;
        }

        int plays = Math.Max(1, animation.AllRepeats + 1);
        var repeated = new List<Frame>(sequence.Count * plays);
        for (int play = 0; play < plays; play++)
        {
            repeated.AddRange(sequence);
        }

        return repeated;
    }

    /// <summary>
    /// A2: the count a container header needs is computed arithmetically, so it must equal the
    /// sequence it describes for every option combination. The expected totals are hand-computed for
    /// five selected frames; the final assertion runs the real sequencer, so the ordering rules, the
    /// arithmetic and these numbers cannot drift apart silently.
    /// </summary>
    [Theory]
    [InlineData(false, false, 0, 0, 0, 5)]   // no block: the plain frame order
    [InlineData(true, false, 0, 0, 0, 5)]    // forward only
    [InlineData(false, true, 0, 0, 0, 5)]    // reverse only
    [InlineData(true, true, 0, 0, 0, 9)]     // 1 2 3 4 5 4 3 2 1
    [InlineData(true, false, 2, 0, 0, 7)]    // two extra 5s
    [InlineData(true, true, 2, 0, 0, 11)]    // seven forward frames plus four reverse
    [InlineData(true, true, 0, 3, 0, 12)]    // five forward, four reverse, three extra 1s
    [InlineData(true, true, 1, 1, 0, 11)]    // six forward, four reverse, one extra 1
    [InlineData(true, true, 0, 0, 2, 27)]    // three plays of nine
    [InlineData(true, false, 0, 0, 1, 10)]   // two plays of five
    [InlineData(false, true, 0, 0, 2, 15)]   // three plays of five, backwards
    [InlineData(true, true, 2, 3, 1, 28)]    // (5 + 2 + 4 + 3) x 2
    public void CountAnimationFrames_MatchesTheSequenceItDescribes(
        bool forward, bool reverse, int forwardHolds, int reverseHolds, int allRepeats, int expected)
    {
        var animation = new AnimationOptions
        {
            Forward = forward,
            Reverse = reverse,
            ForwardLastFrameRepeats = forwardHolds,
            ReverseLastFrameRepeats = reverseHolds,
            AllRepeats = allRepeats,
        };

        Assert.Equal(expected, Processor.CountAnimationFrames(5, animation));

        // AnimationOrder is one play; the arithmetic multiplies it by -anim_all_rep + 1.
        var singlePlay = new AnimationOptions
        {
            Forward = forward,
            Reverse = reverse,
            ForwardLastFrameRepeats = forwardHolds,
            ReverseLastFrameRepeats = reverseHolds,
        };
        Assert.Equal(expected / Math.Max(1, allRepeats + 1), Processor.AnimationOrder(5, singlePlay).Count());

        var queue = FlatQueue(5);
        var sequencer = new Processor(new PipelineSettings { Animation = animation });
        List<Frame> streamed = sequencer.BuildAviAnimation(queue, null, null).ToList();
        Assert.Equal(expected, streamed.Count);

        // Frame-for-frame equal to the pre-streaming implementation (the oracle above).
        Assert.Equal(
            Values(MaterialisedReference(queue.Select(entry => entry.Frame.Frame).ToList(), animation)),
            Values(streamed));
    }

    /// <summary>Single-frame captures are the edge that separates a turn-skip from an off-by-one.</summary>
    [Fact]
    public void CountAnimationFrames_HandlesASingleSelectedFrame()
    {
        var animation = new AnimationOptions { Forward = true, Reverse = true, ReverseLastFrameRepeats = 2 };

        // 1, then the reverse block is empty (the turn is the only frame), then two holds of it.
        Assert.Equal(3, Processor.CountAnimationFrames(1, animation));
        Assert.Equal(3, Processor.AnimationOrder(1, animation).Count());
        Assert.Empty(Processor.AnimationOrder(0, animation));
        Assert.Equal(0, Processor.CountAnimationFrames(0, animation));
    }

    // ------------------------------------------------------------------ A3: lazy sequence

    /// <summary>
    /// A3: the sequence is index-based and deferred — creating it does not copy or decode anything.
    /// Mutating a queued frame after creation shows the writer will read the queue when it pulls,
    /// not a snapshot taken up front. The order is still exactly the engine's ping-pong.
    /// </summary>
    [Fact]
    public void AviAnimation_IsDeferredAndReadsTheQueueByIndex()
    {
        var settings = new PipelineSettings { Animation = new AnimationOptions { Forward = true, Reverse = true } };
        var queue = FlatQueue(4);

        IEnumerable<Frame> sequence = new Processor(settings).BuildAviAnimation(queue, null, null);

        // A materialised list would expose a collection; an iterator cannot answer Count without
        // walking, which is what keeps the run's memory flat.
        Assert.False(sequence is ICollection<Frame>);

        // Nothing has been read yet: change frame 3 and the sequence must see the new value.
        queue[2].Frame.Frame.Samples[0] = 99;

        Assert.Equal([10, 20, 99, 40, 99, 20, 10], Values(sequence));
    }

    /// <summary>
    /// The non-animation container path streams too, and writes exactly the queued frames in queue
    /// order — the same sequence the old materialised list held.
    /// </summary>
    [Fact]
    public void PlainContainer_StreamsTheQueueInOrder()
    {
        string dir = TempDir();
        string input = WriteSyntheticSer(dir);

        new Processor(Settings(Path.Combine(dir, "out"), ".ser")).Run([input]);

        using var reader = new SerReader(Path.Combine(dir, "out", "capture.ser"));
        var firstSamples = new List<int>();
        Frame? frame;
        while ((frame = reader.ReadFrame()) is not null)
        {
            firstSamples.Add(frame.Samples[0]);
        }

        // Sample 0 of the synthetic capture is 0, 7, 14, 21, 28; the plain container keeps that order.
        Assert.Equal([0, 7, 14, 21, 28], firstSamples);
    }
}
