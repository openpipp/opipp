using System.Buffers.Binary;
using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Stacking;
using Xunit;

namespace Opipp.Image.Tests;

/// <summary>
/// Joined inputs are several captures behind one stream, so a stacked master must name the file whose
/// frames were actually selected — not the first input, which only names the join.
/// </summary>
/// <remarks>
/// <para>
/// Regression: with <c>-join</c>, quality selection and stacking, a stack built entirely from the
/// second input wrote the first input's Observer/Telescope/Instrument, and reversing the input order
/// changed the metadata. The pipeline overwrote every frame's origin with the joined unit's name
/// (which is <c>files[0]</c>) before selection ran, so the stacker's documented first-accepted-frame
/// rule faithfully carried the wrong capture. The pixels were right all along.
/// </para>
/// <para>
/// Fixture note: PIPP's default quality metric is a sharpness estimator, and a perfectly flat
/// synthetic frame scores 0 — every flat frame ties, so <c>KeepFrames</c> would fall back to source
/// order and never actually prefer the second input. The captures are therefore 64x64 (the metric's
/// stride sweep needs a box at least two strides wide) and the second input's bright frame carries
/// vertical structure, which makes it the highest-scoring frame deterministically. The assertions
/// then check the output sample-for-sample against that frame: the master must be the frame that was
/// selected, with its file's identity.
/// </para>
/// <para>
/// The output header is read field by field at the SER v3 offsets, not through
/// <see cref="SerReader"/>, so a writer-side identity bug cannot hide behind the reader the production
/// code uses for the same lookup.
/// </para>
/// </remarks>
public class JoinedSourceIdentityTests
{
    private const int Width = 64;
    private const int Height = 64;

    private const string ObserverA = "Observer-A";
    private const string TelescopeA = "Scope-A";
    private const string InstrumentA = "Camera-A";

    private const string ObserverB = "Observer-B";
    private const string TelescopeB = "Scope-B";
    private const string InstrumentB = "Camera-B";

    // SER v3 header offsets (see SerHeader): identity text fields and the payload start.
    private const int LittleEndianOffset = 22;
    private const int ColorIdOffset = 18;
    private const int WidthOffset = 26;
    private const int HeightOffset = 30;
    private const int BitDepthOffset = 34;
    private const int FrameCountOffset = 38;
    private const int ObserverOffset = 42;
    private const int TelescopeOffset = 82;
    private const int InstrumentOffset = 122;
    private const int TextLength = 40;
    private const int HeaderSize = 178;

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-joinid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Frame Uniform(ushort value)
    {
        var frame = new Frame(Width, Height, 1, 8);
        Array.Fill(frame.Samples, value);
        return frame;
    }

    /// <summary>A sharp frame whose bright sample is <paramref name="bright"/> (alternating columns).</summary>
    private static Frame BrightStripes(ushort bright)
    {
        var frame = new Frame(Width, Height, 1, 8);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                frame.Samples[(y * Width) + x] = (x & 1) == 0 ? bright : (ushort)0;
            }
        }

        return frame;
    }

    /// <summary>The values <see cref="BrightStripes"/> produces, for a sample-for-sample assertion.</summary>
    private static ushort[] StripeSamples(ushort bright)
    {
        var samples = new ushort[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                samples[(y * Width) + x] = (x & 1) == 0 ? bright : (ushort)0;
            }
        }

        return samples;
    }

    /// <summary>Writes a mono 8-bit SER capture carrying the given identity and frames.</summary>
    private static string WriteCapture(
        string dir, string fileName, string observer, string telescope, string instrument, params Frame[] frames)
    {
        string path = Path.Combine(dir, fileName);
        SerHeader header = SerHeader.Create(Width, Height, 1, 8, frames.Length);
        header.Observer = observer;
        header.Telescope = telescope;
        header.Instrument = instrument;
        using var writer = new SerWriter(path, header);
        foreach (Frame frame in frames) writer.Write(frame);
        return path;
    }

    /// <summary>
    /// Runs one joined stack and returns the output path. The shape is the defect's own: quality
    /// selection on, <c>MinPixelValue</c> 1, and a 1-byte retention budget when the caller wants every
    /// accepted frame forced through the spill store.
    /// </summary>
    private static string RunJoinedStack(
        string dir,
        string outputName,
        StackMethod method,
        int? keepFrames,
        bool qualityWeighting,
        long retentionBudget,
        params string[] inputs)
    {
        string outputDirectory = Path.Combine(dir, outputName);
        var settings = new PipelineSettings
        {
            OutputDirectory = outputDirectory,
            OutputExtension = ".ser",
            JoinMode = true,
            Stack = method,
            KeepFrames = keepFrames,
            QualityWeighting = qualityWeighting,
            MinPixelValue = 1,
            RetentionBudgetBytes = retentionBudget,
        };

        ProcessingReport report = new Processor(settings).Run(inputs);

        Assert.Empty(report.FailedFiles);
        Assert.Equal(1, report.OutputFrames);
        string output = Assert.Single(report.OutputFiles);
        Assert.Equal(
            Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputs[0]) + ".ser"),
            output);
        return output;
    }

    /// <summary>The output SER as an independent header read plus the first frame's samples.</summary>
    private sealed record RawSer(
        int ColorId, int Width, int Height, int BitDepth, int FrameCount, bool LittleEndian,
        string Observer, string Telescope, string Instrument, ushort[] FirstFrame);

    private static RawSer ReadRawSer(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= HeaderSize, $"{path} is shorter than the SER header");
        Assert.Equal(SerHeader.FileId, Encoding.ASCII.GetString(bytes, 0, 14));

        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(WidthOffset));
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(HeightOffset));
        int bitDepth = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(BitDepthOffset));
        int frameCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(FrameCountOffset));
        bool littleEndian = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(LittleEndianOffset)) != 0;

        int bytesPerSample = bitDepth <= 8 ? 1 : 2;
        int samples = width * height;
        Assert.True(bytes.Length >= HeaderSize + (samples * bytesPerSample), $"{path} has no complete frame");

        var first = new ushort[samples];
        for (int i = 0; i < samples; i++)
        {
            int at = HeaderSize + (i * bytesPerSample);
            first[i] = bytesPerSample == 1
                ? bytes[at]
                : littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at))
                    : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at));
        }

        return new RawSer(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(ColorIdOffset)),
            width, height, bitDepth, frameCount, littleEndian,
            Text(bytes, ObserverOffset), Text(bytes, TelescopeOffset), Text(bytes, InstrumentOffset),
            first);
    }

    private static string Text(byte[] bytes, int offset)
    {
        int length = 0;
        while (length < TextLength && bytes[offset + length] != 0) length++;
        return Encoding.ASCII.GetString(bytes, offset, length).TrimEnd(' ');
    }

    private static void AssertIdentity(RawSer header, string observer, string telescope, string instrument)
    {
        Assert.Equal(observer, header.Observer);
        Assert.Equal(telescope, header.Telescope);
        Assert.Equal(instrument, header.Instrument);
    }

    [Theory]
    [InlineData(StackMethod.Mean, false)]
    [InlineData(StackMethod.Sum, false)]
    [InlineData(StackMethod.Median, false)]
    [InlineData(StackMethod.Mean, true)]
    [InlineData(StackMethod.Sum, true)]
    [InlineData(StackMethod.Median, true)]
    public void OnlyTheSecondInputContributes_ReportsTheSecondInputsIdentity(StackMethod method, bool secondInputFirst)
    {
        string dir = TempDir();
        try
        {
            // A's frames are flat (score 0). B has a flat 80 frame and a sharp 160 frame, so keeping
            // one frame keeps B's 160 whichever order the two files are joined in. A used to win the
            // header anyway, because the joined unit is named after A.
            string a = WriteCapture(dir, "a.ser", ObserverA, TelescopeA, InstrumentA, Uniform(10), Uniform(20));
            string b = WriteCapture(dir, "b.ser", ObserverB, TelescopeB, InstrumentB, Uniform(80), BrightStripes(160));
            string[] inputs = secondInputFirst ? [b, a] : [a, b];

            foreach (long budget in new[] { long.MaxValue, 1L }) // in memory, and forced through the spill store
            {
                string output = RunJoinedStack(
                    dir,
                    $"out-{method}-{(secondInputFirst ? "ba" : "ab")}-{budget}",
                    method,
                    keepFrames: 1,
                    qualityWeighting: true,
                    retentionBudget: budget,
                    inputs);

                RawSer header = ReadRawSer(output);
                Assert.Equal(1, header.FrameCount);
                Assert.Equal(Width, header.Width);
                Assert.Equal(Height, header.Height);
                Assert.Equal(8, header.BitDepth);
                Assert.Equal(0, header.ColorId); // mono payload

                // One frame was kept, so the master is B's 160 frame sample for sample — not a
                // combination, and certainly not anything A contributed.
                Assert.Equal(StripeSamples(160), header.FirstFrame);
                Assert.Equal(160, header.FirstFrame.Max());

                AssertIdentity(header, ObserverB, TelescopeB, InstrumentB);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FramesFromBothInputs_ReportTheFirstDeliveredFramesIdentity(bool secondInputFirst)
    {
        string dir = TempDir();
        try
        {
            string a = WriteCapture(dir, "a.ser", ObserverA, TelescopeA, InstrumentA, BrightStripes(10), BrightStripes(20));
            string b = WriteCapture(dir, "b.ser", ObserverB, TelescopeB, InstrumentB, BrightStripes(80), BrightStripes(160));
            string[] inputs = secondInputFirst ? [b, a] : [a, b];

            // Keep three of four. Every frame is structured so the scores are strictly ordered by
            // brightness (10 < 20 < 80 < 160): B's two frames and A's 20 are selected, for either
            // input order. One output header cannot describe two captures, so the documented
            // first-wins rule decides, and "first" is delivery order: quality ranking delivers B's
            // 160 first, so the master reports B even though A contributed pixels.
            string output = RunJoinedStack(
                dir,
                $"out-both-{(secondInputFirst ? "ba" : "ab")}",
                StackMethod.Mean,
                keepFrames: 3,
                qualityWeighting: false,
                retentionBudget: long.MaxValue,
                inputs);

            RawSer header = ReadRawSer(output);
            Assert.Equal(1, header.FrameCount);

            // Mean of [160 stripes, 80 stripes, 20 stripes] per pixel: (160+80+20)/3 = 86 in the
            // bright columns, and every stripe frame's dark columns are 0, so those stay 0. The
            // fractions truncate.
            var expected = new ushort[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    expected[(y * Width) + x] = (x & 1) == 0 ? (ushort)86 : (ushort)0;
                }
            }

            Assert.Equal(expected, header.FirstFrame);
            AssertIdentity(header, ObserverB, TelescopeB, InstrumentB);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(StackMethod.Mean, 20)]
    [InlineData(StackMethod.Sum, 60)]
    [InlineData(StackMethod.Median, 20)]
    public void SingleInput_KeepsNamingItsOwnFile(StackMethod method, int expectedSample)
    {
        string dir = TempDir();
        try
        {
            // Not a join in effect (one input), so attribution must stay exactly as it was: the
            // output names the capture it was made from, whether the frames stayed in memory or
            // went through the spill store.
            string capture = WriteCapture(
                dir, "a.ser", ObserverA, TelescopeA, InstrumentA, Uniform(10), Uniform(20), Uniform(30));

            foreach (long budget in new[] { long.MaxValue, 1L })
            {
                string output = RunJoinedStack(
                    dir,
                    $"out-single-{method}-{budget}",
                    method,
                    keepFrames: null,
                    qualityWeighting: false,
                    retentionBudget: budget,
                    capture);

                RawSer header = ReadRawSer(output);
                Assert.Equal(1, header.FrameCount);
                Assert.All(header.FirstFrame, sample => Assert.Equal(expectedSample, sample));
                AssertIdentity(header, ObserverA, TelescopeA, InstrumentA);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
