using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using PIPP_Application;

// These tests drive the real WinForms form and the real engine process. Constructing two forms at
// the same time on two STA threads breaks inside the designer code (ToolStripMenuItem layout is not
// thread-safe), so the assembly runs its tests one at a time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Opipp.App.Tests;

/// <summary>Shared fixtures for the GUI handoff and form-state tests.</summary>
internal static class AppTestSupport
{
    /// <summary>
    /// Runs before the first helper call: the form plays the Windows notification sound when a run
    /// completes, and this suite completes many runs on the developer's machine.
    /// </summary>
    static AppTestSupport()
    {
        Form1.SuppressSounds = true;
    }

    /// <summary>Creates a unique temporary directory.</summary>
    public static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "opipp-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Deletes a temporary directory, ignoring a file that is still closing.</summary>
    public static void DeleteQuietly(string directory)
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

    /// <summary>Writes an 8x8 mono 8-bit SER whose left half is <paramref name="left"/> and right half is <paramref name="right"/>.</summary>
    public static string WriteSplitSer(string directory, string name, ushort left, ushort right, int size = 8)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, size, size, 1, 8);
        var frame = new Frame(size, size, 1, 8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                frame[x, y] = x < size / 2 ? left : right;
            }
        }

        writer.Write(frame);
        return path;
    }

    /// <summary>Writes a uniform mono 8-bit SER.</summary>
    public static string WriteUniformSer(string directory, string name, ushort value, int size = 8)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, size, size, 1, 8);
        var frame = new Frame(size, size, 1, 8);
        Array.Fill(frame.Samples, value);
        writer.Write(frame);
        return path;
    }

    /// <summary>
    /// Writes a mono 8-bit FITS file with one image HDU per entry in <paramref name="frames"/>.
    /// Each entry is the (left, right) value pair of one 8x8 image, so a multi-image file can be
    /// told apart from a single-image one by the master it produces.
    /// </summary>
    public static string WriteSplitFits(string directory, string name, params (ushort Left, ushort Right)[] frames)
    {
        string path = Path.Combine(directory, name);
        using var writer = new FitsWriter(path);
        foreach ((ushort left, ushort right) in frames)
        {
            var frame = new Frame(8, 8, 1, 8);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    frame[x, y] = x < 4 ? left : right;
                }
            }

            writer.Write(frame);
        }

        return path;
    }

    /// <summary>Writes a FITS file holding <paramref name="frameCount"/> identical uniform 8x8 images.</summary>
    public static string WriteUniformFits(string directory, string name, ushort value, int frameCount)
    {
        var frames = new (ushort, ushort)[frameCount];
        Array.Fill(frames, (value, value));
        return WriteSplitFits(directory, name, frames);
    }

    /// <summary>
    /// Writes a 32x32 mono SER whose frames hold a bright square on a dark field, so object
    /// detection (used by the planetary-animation preset) keeps every frame.
    /// </summary>
    public static string WritePlanetSer(string directory, string name, int frames = 3)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, 32, 32, 1, 8);
        var frame = new Frame(32, 32, 1, 8);
        for (int i = 0; i < frames; i++)
        {
            Array.Fill(frame.Samples, (ushort)0);
            for (int y = 11; y < 21; y++)
            {
                for (int x = 11; x < 21; x++)
                {
                    frame[x, y] = 200;
                }
            }

            writer.Write(frame);
        }

        return path;
    }

    /// <summary>Reads the one TIFF a run produced, wherever the per-input subdirectory put it.</summary>
    public static Frame ReadSingleTiff(string outputDirectory)
    {
        string[] files = Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories);
        Assert.Single(files);
        using var reader = new TiffReader(files[0]);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {files[0]}");
    }

    /// <summary>Reads the one science TIFF a run produced, ignoring the saved masters under <c>logs/</c>.</summary>
    public static Frame ReadSingleScienceTiff(string outputDirectory)
    {
        string logs = Path.Combine(outputDirectory, "logs") + Path.DirectorySeparatorChar;
        string[] files = Directory
            .GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(logs, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Single(files);
        using var reader = new TiffReader(files[0]);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {files[0]}");
    }

    /// <summary>Runs the managed engine that the front-end launches, exactly as a process.</summary>
    public static (int ExitCode, string StdOut, string StdErr) RunEngine(IEnumerable<string> arguments)
    {
        string engine = EngineLocator.FindEngineExecutable();
        Assert.True(File.Exists(engine), "the managed engine was not found at " + engine);

        var start = new ProcessStartInfo(engine)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("the engine did not start");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "the engine did not exit within two minutes");
        return (process.ExitCode, stdout, stderr);
    }

    /// <summary>Runs <paramref name="action"/> on a dedicated STA thread and rethrows its failure.</summary>
    public static void RunSta(Action action, int timeoutSeconds = 120)
    {
        Form1.SuppressSounds = true;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(timeoutSeconds)), $"the STA test hung for {timeoutSeconds} seconds");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Pumps the message loop for a fixed interval (drains late process callbacks).</summary>
    public static void PumpFor(TimeSpan duration)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < duration)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Waits until the form has written the run's final transcript verdict.
    /// </summary>
    /// <remarks>
    /// The exit handler sets the status as soon as the process exits, but the verdict also waits for
    /// the engine's stdout to reach end of stream, which arrives through a marshalled callback. A
    /// test that reads the transcript right after the status can therefore see it one pump early.
    /// </remarks>
    public static void PumpUntilRunVerdict(Form1 form) =>
        PumpUntil(() => form.RunVerdictWrittenForTest, "the run's transcript verdict");

    /// <summary>
    /// Pumps the WinForms message loop until <paramref name="condition"/> holds. The form's engine
    /// callbacks are marshalled to this thread through its SynchronizingObject, so a plain sleep
    /// would never see them.
    /// </summary>
    public static void PumpUntil(Func<bool> condition, string because, int timeoutSeconds = 90)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
            {
                throw new TimeoutException("timed out waiting until " + because);
            }

            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
