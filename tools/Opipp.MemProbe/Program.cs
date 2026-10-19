using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Opipp.MemProbe;

/// <summary>
/// Measures the peak working set of a real CLI run, so the retention budget in docs/memory.md is a
/// measured claim rather than a policy statement.
///
/// Generates a synthetic 16-bit mono SER capture, runs Opipp.Cli over it with a chosen
/// OPIPP_RETENTION_BUDGET_MB, and reports the child's PeakWorkingSet64. Two budgets can be compared
/// in one invocation.
///
/// Usage:
///   dotnet run --project tools/Opipp.MemProbe -- --cli &lt;Opipp.Cli.exe|dll&gt; \
///       [--frames 1200] [--width 1024] [--height 768] [--budget-mb 32,4096] [--keep]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null || options.Help)
        {
            Console.WriteLine("usage: Opipp.MemProbe --cli <path> [--frames N] [--width W] [--height H] " +
                              "[--budget-mb 32,4096] [--keep]");
            return options is null ? 2 : 0;
        }

        string work = options.WorkDirectory ?? Path.Combine(Path.GetTempPath(), "opipp-memprobe");
        Directory.CreateDirectory(work);
        string capture = Path.Combine(work, "capture.ser");

        long bytes = (long)options.Frames * options.Width * options.Height * sizeof(ushort);
        Console.WriteLine($"capture : {options.Frames} frames, {options.Width}x{options.Height} mono 16-bit " +
                          $"({bytes / 1024.0 / 1024.0:F0} MB)");

        if (!File.Exists(capture) || new FileInfo(capture).Length != 178 + bytes)
        {
            Console.Write("writing : ");
            SyntheticSer.Write(capture, options.Frames, options.Width, options.Height);
            Console.WriteLine("done");
        }

        Console.WriteLine();
        Console.WriteLine($"{"budget",10} {"peak WS",12} {"elapsed",10} {"outputs",8}  exit");
        foreach (int budget in options.Budgets)
        {
            string outDir = Path.Combine(work, "out-" + budget.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);

            var psi = new ProcessStartInfo(options.CliPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            psi.ArgumentList.Add("-tif");
            psi.ArgumentList.Add("-q=10");
            psi.ArgumentList.Add("-od=" + outDir);
            psi.ArgumentList.Add(capture);
            psi.Environment["OPIPP_RETENTION_BUDGET_MB"] = budget.ToString(CultureInfo.InvariantCulture);

            var clock = Stopwatch.StartNew();
            using var process = Process.Start(psi)!;
            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // PeakWorkingSet64 throws once the process object has exited, so sample it while it runs.
            long peak = 0;
            int samples = 0;
            while (!process.HasExited)
            {
                try
                {
                    process.Refresh();                       // Process caches its snapshot; without this
                    peak = Math.Max(peak, process.PeakWorkingSet64); // every read returns the start value.
                    samples++;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                Thread.Sleep(25);
            }

            process.WaitForExit();
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            clock.Stop();
            int outputs = Directory.Exists(outDir)
                ? Directory.GetFiles(outDir, "*.tif", SearchOption.AllDirectories).Length
                : 0;

            Console.WriteLine($"{budget + " MB",10} {peak / 1024.0 / 1024.0,9:F0} MB {clock.Elapsed.TotalSeconds,8:F1}s " +
                              $"{outputs,8}  {process.ExitCode}  ({samples} samples)");

            if (process.ExitCode != 0)
            {
                Console.WriteLine("  stderr: " + stderr.Trim().Split('\n')[0]);
                Console.WriteLine("  stdout: " + stdout.Trim().Split('\n').LastOrDefault());
            }
        }

        if (!options.Keep)
        {
            try
            {
                File.Delete(capture);
                foreach (string dir in Directory.GetDirectories(work, "out-*")) Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                // Leftovers are in temp; harmless.
            }
        }

        return 0;
    }

    private sealed record Options(
        string CliPath, int Frames, int Width, int Height, int[] Budgets, string? WorkDirectory, bool Keep, bool Help)
    {
        public static Options? Parse(string[] args)
        {
            string? cli = null, work = null;
            int frames = 400, width = 800, height = 600;
            int[] budgets = [32, 4096];
            bool keep = false, help = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + a);
                switch (a)
                {
                    case "--cli": cli = Next(); break;
                    case "--frames": frames = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--width": width = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--height": height = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--work": work = Next(); break;
                    case "--keep": keep = true; break;
                    case "-h" or "--help": help = true; break;
                    case "--budget-mb":
                        budgets = Next().Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                        break;
                    default: return null;
                }
            }

            if (!help && string.IsNullOrWhiteSpace(cli)) return null;
            return new Options(cli ?? string.Empty, frames, width, height, budgets, work, keep, help);
        }
    }
}

/// <summary>Minimal SER writer: 178-byte header, mono, 16-bit, little-endian samples.</summary>
internal static class SyntheticSer
{
    public static void Write(string path, int frames, int width, int height)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("LUCAM-RECORDER"));   // 14
        writer.Write(0);                                            // LuID
        writer.Write(0);                                            // colour id: mono
        writer.Write(0);                                            // little endian
        writer.Write(width);                                        // 30
        writer.Write(height);
        writer.Write(16);                                           // bit depth
        writer.Write(frames);                                       // 42
        writer.Write(new byte[40 + 40 + 40]);                       // observer, instrument, telescope
        writer.Write(0L);                                           // date time
        writer.Write(0L);                                           // date time UTC -> 178 bytes

        var row = new byte[width * 2];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)((x * 7 + y * 3 + frame * 5) & 0xFFFF);
                    row[x * 2] = (byte)value;
                    row[x * 2 + 1] = (byte)(value >> 8);
                }
                writer.Write(row, 0, row.Length);
            }
        }
    }
}
