using Opipp.Core.Frames;
using Opipp.Core.Formats;

namespace Opipp.Parity.FakeEngine;

/// <summary>
/// Stand-in engine used by the parity-harness tests. It accepts the same argument shape as a real
/// engine (<c>...switches -od=&lt;dir&gt; input</c>) and writes deterministic TIFFs, so a test can
/// make one side misbehave without touching the original binary or the managed CLI.
/// </summary>
/// <remarks>
/// The scenario arrives as <c>-fake=name</c>, which the harness passes both on argv and on stdin;
/// this fixture never reads stdin. Frames are 8x6 mono, with a distinct constant payload per frame.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? outputDir = args.FirstOrDefault(a => a.StartsWith("-od=", StringComparison.Ordinal))?[4..];
        string scenario = args.FirstOrDefault(a => a.StartsWith("-fake=", StringComparison.Ordinal))?["-fake=".Length..] ?? "match";
        if (outputDir is null)
        {
            Console.Error.WriteLine("fake engine: missing -od=");
            return 2;
        }

        Directory.CreateDirectory(outputDir);
        switch (scenario)
        {
            case "match":
                WriteFrames(outputDir, [0, 1, 2]);
                return 0;

            case "reorder":
                // The same three frames as "match", assigned to different frame numbers.
                WriteFrames(outputDir, [1, 2, 0]);
                return 0;

            case "exit-nonzero":
                // Produces perfectly good, matching output and still fails: the verdict must not
                // be fooled by artifacts from an engine that reported failure.
                WriteFrames(outputDir, [0, 1, 2]);
                Console.Out.WriteLine("fake engine: output written, failing deliberately");
                return 3;

            case "empty-ser":
                // A valid header over zero frames: both sides agree on name, geometry, layout and
                // log, yet no image sample exists. The verdict must not call that a match.
                WriteEmptySer(outputDir);
                return 0;

            case "malformed":
                WriteFrame(Path.Combine(outputDir, "1.tif"), 0);
                File.WriteAllText(Path.Combine(outputDir, "2.tif"), "this is not a TIFF file");
                WriteFrame(Path.Combine(outputDir, "3.tif"), 2);
                return 0;

            case "hang":
                Thread.Sleep(Timeout.Infinite);
                return 0;

            case "pipe-block":
                // Fill stderr, then stdout, then hang. A harness that reads the streams in the wrong
                // order (or only after WaitForExit) deadlocks here instead of reaching its timeout.
                string chunk = new('E', 64 * 1024);
                for (int i = 0; i < 8; i++) { Console.Error.Write(chunk); Console.Error.Flush(); }
                for (int i = 0; i < 8; i++) { Console.Out.Write(chunk); Console.Out.Flush(); }
                Thread.Sleep(Timeout.Infinite);
                return 0;

            default:
                Console.Error.WriteLine($"fake engine: unknown scenario '{scenario}'");
                return 2;
        }
    }

    private static void WriteFrames(string dir, int[] payloads)
    {
        for (int i = 0; i < payloads.Length; i++)
            WriteFrame(Path.Combine(dir, $"{i + 1}.tif"), payloads[i]);
    }

    /// <summary>
    /// Writes a zero-frame SER with valid geometry plus the log a real engine would leave, so the
    /// two sides differ in nothing observable except the absence of image data.
    /// </summary>
    private static void WriteEmptySer(string dir)
    {
        using (var writer = new SerWriter(Path.Combine(dir, "1.ser"), 8, 6, 1, 8, frameCount: 0))
        {
            // deliberately no frames
        }

        string logs = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "input_pipp_log.txt"), "fake engine: empty SER (0 frames)\n");
    }

    private static void WriteFrame(string path, int payload)
    {
        var frame = new Frame(8, 6, 1, 8);
        for (int i = 0; i < frame.Samples.Length; i++)
            frame.Samples[i] = (ushort)(16 + payload * 32 + i % 7);

        using var writer = new TiffWriter(path, 8, 6, 1, 8);
        writer.Write(frame);
    }
}
