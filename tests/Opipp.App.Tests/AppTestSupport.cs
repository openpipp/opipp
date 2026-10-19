using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>Shared fixtures for the GUI handoff and form-state tests.</summary>
internal static class AppTestSupport
{
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
