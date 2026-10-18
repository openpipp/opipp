namespace Pipp.Image.Tests;

using System.Buffers.Binary;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Pipp.Core.Pipeline;
using Xunit;

/// <summary>
/// Animation sequencing (<c>-anim_*</c>), WinJUPOS-friendly output names (<c>-wjpos_*</c>) and the
/// legacy AVI layout (<c>-avi_old</c>). Every expected value here was produced by
/// <c>bin/pipp_core.exe</c> on the same fixtures — the oracle commands are quoted on the tests.
/// </summary>
public class AnimationAndNamingTests
{
    // ------------------------------------------------------------------ fixtures

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-anim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Four-frame mono SER whose frames are flat 40, 80, 120 and 160.</summary>
    private static string WriteFlatSer(string dir, string name = "capture.ser", int frames = 4)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, 16, 12, 1, 8);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(16, 12, 1, 8);
            Array.Fill(frame.Samples, (ushort)(40 * (f + 1)));
            writer.Write(frame);
        }

        return path;
    }

    /// <summary>Appends a SER v3 per-frame timestamp trailer (8 bytes per frame, .NET ticks).</summary>
    private static void AppendSerTrailer(string path, params DateTime[] stamps)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        Span<byte> buffer = stackalloc byte[8];
        foreach (DateTime stamp in stamps)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer, stamp.Ticks);
            stream.Write(buffer);
        }
    }

    private static PipelineSettings Settings(string outputDir, string extension) => new()
    {
        OutputDirectory = outputDir,
        OutputExtension = extension,
        Fps = 25,
        WriteLogFile = false,
    };

    private static List<int> AviValues(string path)
    {
        var values = new List<int>();
        using var reader = new AviReader(path);
        Frame? frame;
        while ((frame = reader.ReadFrame()) is not null)
        {
            values.Add(frame.Samples[0]);
        }

        return values;
    }

    /// <summary>Counts image descriptors in a GIF and returns the NETSCAPE loop count, if present.</summary>
    private static (int Images, int? Loop) ReadGif(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(data, 0, 6));
        int pos = 13;
        if ((data[10] & 0x80) != 0)
        {
            pos += 3 * (1 << ((data[10] & 0x07) + 1));
        }

        int images = 0;
        int? loop = null;
        while (pos < data.Length)
        {
            byte block = data[pos];
            if (block == 0x21)
            {
                byte label = data[pos + 1];
                pos += 2;
                var payload = new List<byte>();
                while (data[pos] != 0)
                {
                    int size = data[pos];
                    payload.AddRange(data.AsSpan(pos + 1, size).ToArray());
                    pos += 1 + size;
                }

                pos++;
                if (label == 0xFF && payload.Count >= 14 &&
                    System.Text.Encoding.ASCII.GetString(payload.ToArray(), 0, 11) == "NETSCAPE2.0")
                {
                    loop = payload[12] | (payload[13] << 8);
                }
            }
            else if (block == 0x2C)
            {
                images++;
                int flags = data[pos + 9];
                pos += 10;
                if ((flags & 0x80) != 0)
                {
                    pos += 3 * (1 << ((flags & 0x07) + 1));
                }

                pos++; // LZW minimum code size
                while (data[pos] != 0)
                {
                    pos += 1 + data[pos];
                }

                pos++;
            }
            else
            {
                break;
            }
        }

        return (images, loop);
    }

    // ------------------------------------------------------------------ AVI animation

    [Fact]
    public void AviAnimation_ForwardReverse_WritesPingPongWithoutRepeatingTheTurnFrame()
    {
        // pipp_core: -avi -anim_fwd -anim_rev writes 1 2 3 4 3 2 1 (seven chunks).
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".avi");
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true };

        new Processor(settings).Run([input]);

        Assert.Equal([40, 80, 120, 160, 120, 80, 40],
            AviValues(Path.Combine(dir, "out", "capture.avi")));
    }

    [Fact]
    public void AviAnimation_AllRepeats_PlaysTheWholeSequenceNPlusOneTimes()
    {
        // pipp_core: -anim_all_rep=2 writes three copies of the seven-frame sequence (21 frames).
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".avi");
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true, AllRepeats = 2 };

        new Processor(settings).Run([input]);

        var values = AviValues(Path.Combine(dir, "out", "capture.avi"));
        Assert.Equal(21, values.Count);
        Assert.Equal([40, 80, 120, 160, 120, 80, 40], values.Take(7));
        Assert.Equal(values.Take(7), values.Skip(14));
    }

    [Fact]
    public void AviAnimation_LastFrameHolds_AppendCopiesAtTheEndOfEachBlock()
    {
        // pipp_core: -anim_fwd -anim_rev -anim_fwd_lf_rep=2 -anim_rev_lf_rep=3 writes
        // 1 2 3 4 4 4 3 2 1 1 1 1 (twelve frames).
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".avi");
        settings.Animation = new AnimationOptions
        {
            Forward = true,
            Reverse = true,
            ForwardLastFrameRepeats = 2,
            ReverseLastFrameRepeats = 3,
        };

        new Processor(settings).Run([input]);

        Assert.Equal([40, 80, 120, 160, 160, 160, 120, 80, 40, 40, 40, 40],
            AviValues(Path.Combine(dir, "out", "capture.avi")));
    }

    [Fact]
    public void AviAnimation_ReverseOnly_WritesTheFramesBackwards()
    {
        // pipp_core: -avi -anim_rev alone writes 4 3 2 1; -anim_fwd is not implied.
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".avi");
        settings.Animation = new AnimationOptions { Reverse = true };

        new Processor(settings).Run([input]);

        Assert.Equal([160, 120, 80, 40], AviValues(Path.Combine(dir, "out", "capture.avi")));
    }

    [Fact]
    public void AviAnimation_NoSwitches_LeavesThePlainFrameOrder()
    {
        string dir = TempDir();
        string input = WriteFlatSer(dir);

        new Processor(Settings(Path.Combine(dir, "out"), ".avi")).Run([input]);

        Assert.Equal([40, 80, 120, 160], AviValues(Path.Combine(dir, "out", "capture.avi")));
    }

    [Fact]
    public void AnimationSwitches_LeaveSerOutputAsAPlainFrameStream()
    {
        // With -ser and -anim_* the engine dumps per-frame .tmp files, writes no SER at all, reports
        // "Total output frames: 0" and exits 127 ("All frames have been discarded"). That is a
        // defect, not a layout; here SER keeps the plain frame order.
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".ser");
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true };

        new Processor(settings).Run([input]);

        using var reader = new SerReader(Path.Combine(dir, "out", "capture.ser"));
        var values = new List<int>();
        Frame? frame;
        while ((frame = reader.ReadFrame()) is not null)
        {
            values.Add(frame.Samples[0]);
        }

        Assert.Equal([40, 80, 120, 160], values);
    }

    // ------------------------------------------------------------------ GIF animation

    [Fact]
    public void GifAnimation_CollapsesHoldsAndDropsTheLoopSeam()
    {
        // pipp_core: -gif -anim_fwd -anim_rev writes 6 images for this four-frame capture (the
        // closing frame repeats the first image), and -anim_fwd -anim_fwd_lf_rep=2 still writes 4.
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".gif");
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true };

        new Processor(settings).Run([input]);

        Assert.Equal(6, ReadGif(Path.Combine(dir, "out", "capture.gif")).Images);

        string dir2 = TempDir();
        string input2 = WriteFlatSer(dir2);
        var holdSettings = Settings(Path.Combine(dir2, "out"), ".gif");
        holdSettings.Animation = new AnimationOptions { Forward = true, ForwardLastFrameRepeats = 2 };

        new Processor(holdSettings).Run([input2]);

        Assert.Equal(4, ReadGif(Path.Combine(dir2, "out", "capture.gif")).Images);
    }

    [Fact]
    public void GifAnimation_UsesRepeatSwitchesAsTheLoopCount()
    {
        // pipp_core: -anim_all_rep=2 writes loop=2 and -anim_all_rep_always writes loop=0; neither
        // duplicates frames (the image count stays 6).
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(Path.Combine(dir, "out"), ".gif");
        settings.Animation = new AnimationOptions { Forward = true, Reverse = true, AllRepeats = 2 };

        new Processor(settings).Run([input]);

        (int images, int? loop) = ReadGif(Path.Combine(dir, "out", "capture.gif"));
        Assert.Equal(6, images);
        Assert.Equal(2, loop);

        string dir2 = TempDir();
        string input2 = WriteFlatSer(dir2);
        var alwaysSettings = Settings(Path.Combine(dir2, "out"), ".gif");
        alwaysSettings.Animation = new AnimationOptions { Forward = true, Reverse = true, RepeatForever = true };

        new Processor(alwaysSettings).Run([input2]);

        (images, loop) = ReadGif(Path.Combine(dir2, "out", "capture.gif"));
        Assert.Equal(6, images);
        Assert.Equal(0, loop);
    }

    // ------------------------------------------------------------------ WinJUPOS names

    [Theory]
    [InlineData(0, "2022-10-25-0055_0")]
    [InlineData(2, "2022-10-25-0055_0")]
    [InlineData(3, "2022-10-25-0055_1")]
    [InlineData(8, "2022-10-25-0055_1")]
    [InlineData(9, "2022-10-25-0055_2")]
    [InlineData(33, "2022-10-25-0055_6")]
    [InlineData(56, "2022-10-25-0055_9")]
    [InlineData(57, "2022-10-25-0056_0")]
    public void WinJuposPrefix_RoundsToTheNearestTenthOfAMinute(int seconds, string expected)
    {
        // pipp_core rounds the WinJUPOS timestamp to 6-second tenths, halves up, and carries into the
        // next minute (all eight points measured on one-frame SER fixtures).
        var stamp = new DateTime(2022, 10, 25, 0, 55, 0).AddSeconds(seconds);

        Assert.Equal(expected, Processor.FormatWinJuposPrefix(stamp));
    }

    [Fact]
    public void WinJuposMask_ParsesTheEngineDateFormat()
    {
        // date_time_check("2022-10-25-0055_2.ser", "YYYY-MM-DD-hhmm_t") reports "25-Oct-2022 00:55 12".
        Assert.True(Processor.TryParseWinJuposTimestamp(
            "2022-10-25-0055_2.ser", "YYYY-MM-DD-hhmm_t", out DateTime value));
        Assert.Equal(new DateTime(2022, 10, 25, 0, 55, 12), value);

        Assert.False(Processor.TryParseWinJuposTimestamp(
            "capture.ser", "YYYY-MM-DD-hhmm_t", out _));
    }

    [Fact]
    public void WinJuposNaming_UsesTheSerTrailerTimestamps()
    {
        // pipp_core with -ser -wjpos_time=0 over frame times 00:55:00, :20, 01:50 and 03:05 names the
        // file 2022-10-25-0056_5-... (midpoint 00:56:32.5, rounded to 00:56:30).
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var baseTime = new DateTime(2022, 10, 25, 0, 55, 0);
        AppendSerTrailer(input, baseTime, baseTime.AddSeconds(20), baseTime.AddSeconds(110),
            baseTime.AddSeconds(185));

        string outDir = Path.Combine(dir, "out");
        var settings = Settings(outDir, ".ser");
        settings.WinJuposTimeMode = 0;
        new Processor(settings).Run([input]);

        Assert.Contains("2022-10-25-0056_5-capture.ser",
            Directory.GetFiles(outDir, "*.ser").Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(1, "2022-10-25-0055_0-2022-10-25-0055_0.ser")]
    [InlineData(2, "2022-10-25-0055_0-2022-10-25-0055_0.ser")]
    public void WinJuposNaming_FallsBackToTheFileNameMask(int mode, string expected)
    {
        // No trailer: the timestamp is read from the name with the mask and stepped by -wjpos_ipfps.
        // pipp_core picks the earliest for mode 1 and the first frame for mode 2; both are 00:55:00.
        string dir = TempDir();
        string input = WriteFlatSer(dir, "2022-10-25-0055_0.ser");

        string outDir = Path.Combine(dir, "out");
        var settings = Settings(outDir, ".ser");
        settings.WinJuposTimeMode = mode;
        settings.WinJuposDateTimeMask = "YYYY-MM-DD-hhmm_t";
        settings.WinJuposInputFps = 10;
        new Processor(settings).Run([input]);

        Assert.Contains(expected, Directory.GetFiles(outDir, "*.ser").Select(Path.GetFileName));
    }

    [Fact]
    public void WinJuposNaming_WritesMinValueWhenNoTimestampSourceExists()
    {
        // pipp_core's observed fallback for a file whose name does not match the mask and which has
        // no trailer is the literal 0001-01-01-0000_0 prefix.
        string dir = TempDir();
        string input = WriteFlatSer(dir);

        string outDir = Path.Combine(dir, "out");
        var settings = Settings(outDir, ".ser");
        settings.WinJuposTimeMode = 1;
        new Processor(settings).Run([input]);

        Assert.Contains("0001-01-01-0000_0-capture.ser",
            Directory.GetFiles(outDir, "*.ser").Select(Path.GetFileName));
    }

    [Fact]
    public void WinJuposNaming_PrefixesEveryRgbChannelContainer()
    {
        // pipp_core -ser -rgb writes <prefix>-r_<stem>..ser, <prefix>-g_..., <prefix>-b_...
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var baseTime = new DateTime(2022, 10, 25, 0, 55, 0);
        AppendSerTrailer(input, baseTime, baseTime.AddSeconds(20), baseTime.AddSeconds(110),
            baseTime.AddSeconds(185));

        string outDir = Path.Combine(dir, "out");
        var settings = Settings(outDir, ".ser");
        settings.WinJuposTimeMode = 0;
        settings.SplitRgb = true;
        new Processor(settings).Run([input]);

        var names = Directory.GetFiles(outDir, "*.ser").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(3, names.Count);
        Assert.Equal(["2022-10-25-0056_5-b_capture..ser", "2022-10-25-0056_5-g_capture..ser",
            "2022-10-25-0056_5-r_capture..ser"], names);
    }

    [Fact]
    public void WinJuposNaming_IsOffWithoutWjposTime()
    {
        // -wjpos_dtmask and -wjpos_ipfps alone never enable naming; only -wjpos_time does.
        string dir = TempDir();
        string input = WriteFlatSer(dir);

        string outDir = Path.Combine(dir, "out");
        var settings = Settings(outDir, ".ser");
        settings.WinJuposDateTimeMask = "YYYY-MM-DD-hhmm_t";
        settings.WinJuposInputFps = 10;
        new Processor(settings).Run([input]);

        Assert.Contains("capture.ser", Directory.GetFiles(outDir, "*.ser").Select(Path.GetFileName));
        Assert.DoesNotContain(Directory.GetFiles(outDir, "*.ser").Select(Path.GetFileName),
            name => name!.Contains("0001-01-01"));
    }

    // ------------------------------------------------------------------ legacy AVI layout

    [Fact]
    public void AviLegacyLayout_WritesAJunkChunkAndMoviAt8192()
    {
        // pipp_core -avi -avi_old: no indx/odml/ix00, and a zero-filled JUNK chunk puts the movi
        // LIST at absolute offset 8192 (same file as -avi_old4 for anything under 2 GiB).
        string dir = TempDir();
        string input = WriteFlatSer(dir);
        var settings = Settings(dir, ".avi");
        settings.AviLegacyLayout = true;

        new Processor(settings).Run([input]);

        string path = Path.Combine(dir, "capture.avi");
        byte[] data = File.ReadAllBytes(path);
        var chunks = new List<(string Id, int Offset, int Size)>();
        void Walk(int start, int end)
        {
            int q = start;
            while (q + 8 <= end)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, q, 4);
                int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(q + 4, 4));
                chunks.Add((id, q, size));
                if (id is "RIFF" or "LIST")
                {
                    Walk(q + 12, q + 8 + size);
                }

                q += 8 + size + (size & 1);
            }
        }

        Walk(0, data.Length);

        Assert.Contains(chunks, c => c.Id == "JUNK" && c.Offset == 1236 && c.Size == 6948);
        Assert.Contains(chunks, c => c.Id == "LIST" && c.Offset == 8192);
        Assert.DoesNotContain(chunks, c => c.Id is "indx" or "odml" or "dmlh" or "ix00");

        // The filler is all zeroes and the frame payload still starts right after the movi header.
        Assert.All(data.AsSpan(1244, 6948).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal("00db", System.Text.Encoding.ASCII.GetString(data, 8204, 4));
    }
}
