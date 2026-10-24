namespace Opipp.IntegrationProbe;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Integration;

/// <summary>
/// Measures what final-image integration costs a real process, so the numbers published for the
/// retention budget and the median routes are process peaks rather than estimates of live objects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a child process per configuration.</b> <see cref="Process.PeakWorkingSet64"/> is a
/// high-water mark for the life of the process: it never falls, so measuring two configurations in
/// one process would report the first one's peak for the second. The probe therefore acts as a
/// driver, launching itself once per row and sampling the child's <c>PeakWorkingSet64</c> every
/// <c>--sample-ms</c> milliseconds while it runs (reading it after exit throws; without
/// <see cref="Process.Refresh"/> every read returns the start snapshot). The child reports its own
/// exact peak as well, and the table shows the larger of the two.
/// </para>
/// <para>
/// <b>What the child does.</b> It writes nothing: the driver generates a synthetic SER fixture once
/// per geometry, and the child reads it back through <see cref="SerReader"/>, retains the frames
/// exactly as <c>Processor</c> does — inline while a byte budget holds, then a
/// <see cref="FrameStore"/> with the already-inline frames migrated — and integrates them through
/// <see cref="FinalImageIntegrator"/>. Which overload it calls is the case's shape: <c>streaming</c>
/// is the one <c>Processor</c> uses, where the integrator pulls one frame at a time from the retained
/// handles, and <c>list</c> materialises the selection first, which is what makes a median choose
/// between its in-memory and tiled routes.
/// </para>
/// <para>
/// <b>What is measured.</b> <c>peak_mb</c> is the process peak: the retention pass, the integration
/// and the runtime together. <c>retain_peak_mb</c> and <c>integrate_peak_mb</c> divide that history
/// at a forced full collection taken between the two phases, so it is visible which phase owns the
/// peak. <c>retained_mb</c> is the pixel data still in managed memory when integration starts,
/// <c>scratch_mb</c> is what the run wrote to disk — the retention store's pixel bytes plus, for the
/// tiled median route, the one translated copy per frame that route spills itself.
/// <c>output_mb</c> is the integrated frame plus its coverage map.
/// </para>
/// <para>
/// Usage, from the repository root:
/// <code>
/// dotnet run -c Release --project tools/Opipp.IntegrationProbe -- --suite memory
/// dotnet run -c Release --project tools/Opipp.IntegrationProbe -- --case hd,median:list,120,1920,1080,rgb16,512
/// </code>
/// A case is <c>name,method[:shape],frames,width,height,format,retentionMB[,medianMaxFrames
/// [,medianMemMB[,medianScratchMB[,bandRows[,shiftPixels]]]]]</c>; <c>retentionMB</c> 0 means "no
/// retention budget" (nothing spills), a zero median value leaves that
/// <see cref="IntegrationOptions"/> default in place, and the shape defaults to streaming.
/// </para>
/// </remarks>
internal static class Program
{
    private const string ChildSwitch = "--child";

    private const int DefaultSampleMilliseconds = 20;

    private static int Main(string[] args)
    {
        try
        {
            return args.Length > 0 && args[0] == ChildSwitch ? RunChild(args) : RunSuite(args);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    // ------------------------------------------------------------------ driver

    private static int RunSuite(string[] args)
    {
        string? work = null;
        string? outPath = null;
        bool keep = false;
        bool csv = false;
        int sampleMs = DefaultSampleMilliseconds;
        var cases = new List<ProbeCase>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {a}");
            switch (a)
            {
                case "--suite": cases.AddRange(Suites.Named(Next())); break;
                case "--case": cases.Add(ProbeCase.Parse(Next())); break;
                case "--work": work = Next(); break;
                case "--out": outPath = Next(); break;
                case "--sample-ms": sampleMs = PositiveInt(Next(), a); break;
                case "--csv": csv = true; break;
                case "--keep": keep = true; break;
                case "-h" or "--help": PrintUsage(); return 0;
                default: throw new ArgumentException($"unknown argument '{a}'");
            }
        }

        if (cases.Count == 0) cases.AddRange(Suites.Memory);

        bool ownsDirectory = work is null;
        string workDirectory = work is null
            ? Path.Combine(Path.GetTempPath(), "opipp-integration-probe")
            : Path.GetFullPath(work);
        Directory.CreateDirectory(workDirectory);

        Console.WriteLine(EnvironmentHeader());
        Console.WriteLine($"work      : {workDirectory}");
        Console.WriteLine($"sampling  : every {sampleMs} ms (PeakWorkingSet64 of the child process)");
        Console.WriteLine();

        var results = new List<Measurement>();
        StreamWriter? csvFile = null;
        try
        {
            if (outPath is not null)
            {
                csvFile = new StreamWriter(outPath, append: false, Encoding.UTF8);
                csvFile.WriteLine(CsvHeader());
            }

            if (csv) Console.WriteLine(CsvHeader());
            else Console.WriteLine(TableHeader());

            foreach (ProbeCase probe in cases)
            {
                string fixture = Path.Combine(workDirectory, probe.FixtureName);
                EnsureFixture(fixture, probe);

                Measurement measurement = Measure(probe, fixture, sampleMs);
                results.Add(measurement);
                if (csvFile is not null) csvFile.WriteLine(CsvRow(measurement));
                Console.WriteLine(csv ? CsvRow(measurement) : TableRow(measurement));
                if (measurement.ExitCode != 0) Console.WriteLine("  error   : " + measurement.Error);
                Console.Out.Flush();
            }
        }
        finally
        {
            csvFile?.Dispose();
            if (!keep) Cleanup(workDirectory, ownsDirectory);
        }

        int failures = results.Count(r => r.ExitCode != 0);
        Console.WriteLine();
        Console.WriteLine($"{results.Count} configuration(s), {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static string EnvironmentHeader()
    {
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        return string.Create(CultureInfo.InvariantCulture,
            $"host      : {Environment.MachineName}, {Environment.OSVersion.VersionString}, " +
            $"{Environment.ProcessorCount} logical CPUs, {memory.TotalAvailableMemoryBytes / 1073741824.0:F1} GiB RAM, " +
            $".NET {Environment.Version}, workstation GC");
    }

    private static void EnsureFixture(string path, ProbeCase probe)
    {
        long expected = SerHeader.Size + probe.FileBytes;
        if (File.Exists(path) && new FileInfo(path).Length == expected) return;

        Console.WriteLine($"fixture   : {probe.Frames} x {probe.Width}x{probe.Height} {FormatName(probe.Format)} " +
                          $"({probe.FileBytes / 1048576.0:F0} MB on disk, {probe.CaptureBytes / 1048576.0:F0} MB retained) " +
                          $"-> {Path.GetFileName(path)}");
        var clock = Stopwatch.StartNew();
        WriteFixture(path, probe);
        long actual = new FileInfo(path).Length;
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"fixture '{path}' is {actual} bytes, {expected} expected from its header");
        }
        Console.WriteLine($"            written in {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Writes a synthetic capture with a deterministic, non-uniform ramp per frame.</summary>
    private static void WriteFixture(string path, ProbeCase probe)
    {
        using var writer = new SerWriter(
            path, probe.Width, probe.Height, probe.Channels, probe.BitDepth, probe.Frames);
        var frame = new Frame(probe.Width, probe.Height, probe.Channels, probe.BitDepth);
        ushort[] samples = frame.Samples;

        for (int f = 0; f < probe.Frames; f++)
        {
            int i = 0;
            if (probe.BitDepth == 8)
            {
                for (int y = 0; y < probe.Height; y++)
                {
                    for (int x = 0; x < probe.Width; x++)
                        samples[i++] = (ushort)((x * 7 + y * 3 + f * 5) & 0xFF);
                }
            }
            else
            {
                for (int y = 0; y < probe.Height; y++)
                {
                    for (int x = 0; x < probe.Width; x++)
                    {
                        for (int c = 0; c < probe.Channels; c++)
                            samples[i++] = (ushort)((x * 13 + y * 5 + f * 11 + c * 4096) & 0xFFFF);
                    }
                }
            }
            writer.Write(frame);
        }
    }

    private static Measurement Measure(ProbeCase probe, string fixture, int sampleMs)
    {
        (string fileName, List<string> prefix) = ChildInvocation();
        string retention = probe.RetentionMb.ToString(CultureInfo.InvariantCulture);
        string frames = probe.Frames.ToString(CultureInfo.InvariantCulture);

        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(fixture))!,
        };
        foreach (string arg in prefix) psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add(ChildSwitch);
        psi.ArgumentList.Add("--ser"); psi.ArgumentList.Add(fixture);
        psi.ArgumentList.Add("--name"); psi.ArgumentList.Add(probe.Name);
        psi.ArgumentList.Add("--method"); psi.ArgumentList.Add(MethodName(probe.Method));
        psi.ArgumentList.Add("--shape"); psi.ArgumentList.Add(ShapeName(probe.Shape));
        psi.ArgumentList.Add("--expect-frames"); psi.ArgumentList.Add(frames);
        psi.ArgumentList.Add("--retention-mb"); psi.ArgumentList.Add(retention);
        psi.ArgumentList.Add("--sample-ms"); psi.ArgumentList.Add(sampleMs.ToString(CultureInfo.InvariantCulture));
        if (probe.MedianMaxFrames > 0)
        {
            psi.ArgumentList.Add("--median-max-frames");
            psi.ArgumentList.Add(probe.MedianMaxFrames.ToString(CultureInfo.InvariantCulture));
        }
        if (probe.MedianMemoryMb > 0)
        {
            psi.ArgumentList.Add("--median-memory-mb");
            psi.ArgumentList.Add(probe.MedianMemoryMb.ToString(CultureInfo.InvariantCulture));
        }
        if (probe.MedianScratchMb > 0)
        {
            psi.ArgumentList.Add("--median-scratch-mb");
            psi.ArgumentList.Add(probe.MedianScratchMb.ToString(CultureInfo.InvariantCulture));
        }
        if (probe.MedianBandRows > 0)
        {
            psi.ArgumentList.Add("--median-band-rows");
            psi.ArgumentList.Add(probe.MedianBandRows.ToString(CultureInfo.InvariantCulture));
        }
        if (probe.ShiftPixels > 0)
        {
            psi.ArgumentList.Add("--shift");
            psi.ArgumentList.Add(probe.ShiftPixels.ToString(CultureInfo.InvariantCulture));
        }

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"could not start '{fileName}' for case '{probe.Name}'");
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // The child must be measured while it is alive: PeakWorkingSet64 throws once the process
        // object has exited, and without Refresh() every read returns the snapshot taken at start.
        long sampledPeak = 0;
        int samples = 0;
        while (!process.HasExited)
        {
            try
            {
                process.Refresh();
                sampledPeak = Math.Max(sampledPeak, process.PeakWorkingSet64);
                samples++;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            Thread.Sleep(sampleMs);
        }

        process.WaitForExit();
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        clock.Stop();

        Dictionary<string, string>? fields = ParseResult(stdout);
        if (process.ExitCode != 0 || fields is null)
        {
            string detail = FirstLine(stderr);
            if (detail.Length == 0) detail = FirstLine(stdout);
            if (detail.Length == 0) detail = $"child exited {process.ExitCode} without a result line";
            return Measurement.Failed(probe, process.ExitCode, detail, clock.Elapsed.TotalSeconds, samples);
        }

        double childPeak = Number(fields, "peak_mb");
        return new Measurement
        {
            Case = probe,
            ExitCode = 0,
            Storage = fields.GetValueOrDefault("storage", "?"),
            Route = fields.GetValueOrDefault("route", "?"),
            PeakMb = Math.Max(childPeak, sampledPeak / 1048576.0),
            RetainPeakMb = Number(fields, "retain_peak_mb"),
            IntegratePeakMb = Number(fields, "integrate_peak_mb"),
            WallSeconds = Number(fields, "wall_s"),
            RetainSeconds = Number(fields, "retain_s"),
            RetainedMb = Number(fields, "retained_mb"),
            ScratchMb = Number(fields, "scratch_mb"),
            OutputMb = Number(fields, "output_mb"),
            MedianEstimateMb = Number(fields, "median_est_mb"),
            BandRows = (int)Number(fields, "band_rows"),
            Samples = Math.Max(samples, (int)Number(fields, "samples")),
        };
    }

    /// <summary>Locates the executable to re-launch: the apphost when there is one, else <c>dotnet</c>.</summary>
    private static (string FileName, List<string> Prefix) ChildInvocation()
    {
        string? processPath = Environment.ProcessPath;
        if (processPath is not null &&
            Path.GetFileNameWithoutExtension(processPath)
                .Equals("Opipp.IntegrationProbe", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, new List<string>());
        }

        string? assembly = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(assembly))
        {
            return ("dotnet", new List<string> { assembly });
        }

        throw new InvalidOperationException("cannot locate the probe assembly to run a child measurement");
    }

    private static Dictionary<string, string>? ParseResult(string stdout)
    {
        foreach (string line in stdout.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("RESULT ", StringComparison.Ordinal)) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string token in trimmed[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = token.IndexOf('=');
                if (equals > 0) fields[token[..equals]] = token[(equals + 1)..];
            }
            return fields;
        }
        return null;
    }

    private static double Number(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;

    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length != 0) return trimmed;
        }
        return string.Empty;
    }

    private static void Cleanup(string directory, bool removeDirectory)
    {
        foreach (string pattern in new[] { "fixture-*.ser", "retention-*.bin", "opipp-median-*.bin" })
        {
            foreach (string file in Directory.GetFiles(directory, pattern))
            {
                try { File.Delete(file); }
                catch (IOException) { /* temp leftovers are harmless */ }
            }
        }

        if (!removeDirectory) return;
        try { Directory.Delete(directory); }
        catch (IOException) { /* something else is using it */ }
    }

    private static int PositiveInt(string text, string option)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new ArgumentException($"{option} needs a positive integer, got '{text}'");
        }
        return value;
    }

    // ------------------------------------------------------------------ child

    private static int RunChild(string[] args)
    {
        ChildOptions options;
        try
        {
            options = ChildOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var self = Process.GetCurrentProcess();
        try
        {
            using var reader = new SerReader(options.SerPath);
            if (options.ExpectedFrames > 0 && reader.FrameCount != options.ExpectedFrames)
            {
                throw new InvalidOperationException(
                    $"fixture '{options.SerPath}' holds {reader.FrameCount} frame(s), {options.ExpectedFrames} expected");
            }

            long frameBytes = (long)reader.Width * reader.Height * reader.Channels * sizeof(ushort);
            string scratch = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(options.SerPath))!,
                "retention-" + Guid.NewGuid().ToString("n") + ".bin");

            using var sampler = new PhaseSampler(options.SampleMs);
            using var retention = new RetentionSession(options.RetentionBytes, scratch);

            var retainClock = Stopwatch.StartNew();
            while (reader.ReadFrame() is { } frame) retention.Add(frame);
            retainClock.Stop();

            var shifts = new AlignmentOffset[retention.Count];
            for (int i = 0; i < shifts.Length; i++)
            {
                shifts[i] = options.ShiftPixels == 0
                    ? AlignmentOffset.Zero
                    : new AlignmentOffset((i % 2 == 0 ? 1 : -1) * options.ShiftPixels, 0);
            }

            var integration = new IntegrationOptions { Method = options.Method };
            if (options.MedianMaxFrames > 0) integration.MaxRetainedMedianFrames = options.MedianMaxFrames;
            if (options.MedianMemoryMb > 0) integration.MedianInMemoryBudgetBytes = options.MedianMemoryMb * 1048576L;
            if (options.MedianScratchMb > 0) integration.MedianScratchBudgetBytes = options.MedianScratchMb * 1048576L;
            if (options.MedianBandRows > 0) integration.MedianBandRows = options.MedianBandRows;

            // A list-shaped caller holds the whole selection before it calls, so it is materialised here
            // rather than inside the timer: the bytes are a property of the shape, the wall time of the
            // integration. They stay on the integration side of the split because that is when they exist.
            var selection = new List<Frame>(retention.Count);
            if (options.Shape == ProbeShape.List)
            {
                for (int i = 0; i < retention.Count; i++) selection.Add(retention.FrameAt(i));
            }

            long listBytes = options.Shape == ProbeShape.List ? (long)retention.StoredCount * frameBytes : 0;

            // Retention allocated a frame per read and most of them are garbage by now. A full
            // compacting collection here means the integration phase is measured from a settled heap
            // instead of reporting how far behind the collector happened to be. Whatever the collection
            // itself costs stays on the retention side of the split, where the garbage was made.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            sampler.BeginIntegration();
            var clock = Stopwatch.StartNew();
            IntegrationResult result = options.Shape == ProbeShape.List
                ? FinalImageIntegrator.Integrate(selection, shifts, weights: null, integration, CancellationToken.None)
                : FinalImageIntegrator.Integrate(
                    retention.FrameAt, retention.Count, shifts, weights: null, integration, CancellationToken.None);
            clock.Stop();

            sampler.EndIntegration();

            if (result.Frame.Width != reader.Width || result.Frame.Height != reader.Height)
            {
                throw new InvalidOperationException(
                    $"integrated frame is {result.Frame.Width}x{result.Frame.Height}, " +
                    $"{reader.Width}x{reader.Height} expected");
            }

            if (DistinctSamples(result.Frame, 4096) < 2)
            {
                throw new InvalidOperationException("integrated frame is uniform; the fixture did not reach the output");
            }

            long outputBytes = (long)result.Frame.SampleCount * sizeof(ushort) +
                               (long)result.Coverage.Length * sizeof(int);
            long medianScratch = result.MedianRoute == MedianRoute.Tiled ? frameBytes * retention.Count : 0;

            self.Refresh();
            long peak = Math.Max(sampler.PeakBytes, self.PeakWorkingSet64);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"RESULT name={options.Name} method={MethodName(options.Method)} " +
                $"shape={ShapeName(options.Shape)} " +
                $"route={RouteName(result.MedianRoute)} frames={retention.Count} " +
                $"band_rows={integration.MedianBandRows} " +
                $"storage={(retention.Spilled ? "spilled" : "inline")} " +
                $"peak_mb={peak / 1048576.0:F2} " +
                $"retain_peak_mb={sampler.RetainPeakBytes / 1048576.0:F2} " +
                $"integrate_peak_mb={sampler.IntegratePeakBytes / 1048576.0:F2} " +
                $"wall_s={clock.Elapsed.TotalSeconds:F2} retain_s={retainClock.Elapsed.TotalSeconds:F2} " +
                $"retained_mb={(retention.ResidentBytes + listBytes) / 1048576.0:F2} " +
                $"scratch_mb={(retention.StoredBytes + medianScratch) / 1048576.0:F2} " +
                $"output_mb={outputBytes / 1048576.0:F2} " +
                $"median_est_mb={result.MedianEstimatedBytes / 1048576.0:F2} " +
                $"samples={sampler.Samples}"));

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Counts distinct sample values, stopping once <paramref name="limit"/> is reached.</summary>
    private static int DistinctSamples(Frame frame, int limit)
    {
        var seen = new HashSet<ushort>();
        ushort[] samples = frame.Samples;
        for (int i = 0; i < samples.Length && seen.Count < limit; i++) seen.Add(samples[i]);
        return seen.Count;
    }

    private static string MethodName(IntegrationMethod method) => method switch
    {
        IntegrationMethod.Mean => "mean",
        IntegrationMethod.Sum => "sum",
        IntegrationMethod.Median => "median",
        _ => method.ToString(),
    };

    private static string RouteName(MedianRoute route) =>
        route == MedianRoute.NotApplicable ? "-" : route.ToString();

    private static string FormatName(FixtureFormat format) =>
        format == FixtureFormat.Mono8 ? "mono8" : "rgb16";

    private static string ShapeName(ProbeShape shape) =>
        shape == ProbeShape.List ? "list" : "streaming";

    // ------------------------------------------------------------------ reporting

    private static string TableHeader() =>
        $"{"case",-30} {"method",-6} {"shape",-9} {"frames",6} {"geometry",10} {"budget",10} {"storage",8} " +
        $"{"route",9} {"peak WS",10} {"wall",7} {"retained",9} {"scratch",8} {"output",7} {"samples",7}";

    private static string TableRow(Measurement m)
    {
        string geometry = $"{m.Case.Width}x{m.Case.Height}";
        string budget = m.Case.RetentionMb == 0 ? "none" : m.Case.RetentionMb + " MB";
        if (m.ExitCode != 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{m.Case.Name,-30} {MethodName(m.Case.Method),-6} {ShapeName(m.Case.Shape),-9} " +
                $"{m.Case.Frames,6} {geometry,10} {budget,10} FAILED");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"{m.Case.Name,-30} {MethodName(m.Case.Method),-6} {ShapeName(m.Case.Shape),-9} {m.Case.Frames,6} " +
            $"{geometry,10} {budget,10} {m.Storage,8} {m.Route,9} {m.PeakMb,8:F1} MB {m.WallSeconds,6:F1}s " +
            $"{m.RetainedMb,7:F1} MB {m.ScratchMb,6:F1} MB {m.OutputMb,5:F1} MB {m.Samples,7}");
    }

    private static string CsvHeader() =>
        "name,method,shape,frames,width,height,format,retention_mb,storage,median_max_frames,median_memory_mb," +
        "median_scratch_mb,median_band_rows,route,peak_mb,retain_phase_peak_mb,integrate_phase_peak_mb,wall_s," +
        "retain_s,retained_mb,scratch_mb,output_mb,median_estimate_mb,samples,exit_code";

    private static string CsvRow(Measurement m) => string.Join(',', new[]
    {
        m.Case.Name,
        MethodName(m.Case.Method),
        ShapeName(m.Case.Shape),
        m.Case.Frames.ToString(CultureInfo.InvariantCulture),
        m.Case.Width.ToString(CultureInfo.InvariantCulture),
        m.Case.Height.ToString(CultureInfo.InvariantCulture),
        FormatName(m.Case.Format),
        m.Case.RetentionMb.ToString(CultureInfo.InvariantCulture),
        m.Storage,
        m.Case.MedianMaxFrames.ToString(CultureInfo.InvariantCulture),
        m.Case.MedianMemoryMb.ToString(CultureInfo.InvariantCulture),
        m.Case.MedianScratchMb.ToString(CultureInfo.InvariantCulture),
        m.BandRows.ToString(CultureInfo.InvariantCulture),
        m.Route,
        F(m.PeakMb),
        F(m.RetainPeakMb),
        F(m.IntegratePeakMb),
        F(m.WallSeconds),
        F(m.RetainSeconds),
        F(m.RetainedMb),
        F(m.ScratchMb),
        F(m.OutputMb),
        F(m.MedianEstimateMb),
        m.Samples.ToString(CultureInfo.InvariantCulture),
        m.ExitCode.ToString(CultureInfo.InvariantCulture),
    });


    private static string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Opipp.IntegrationProbe - process memory of final-image integration.

              --suite memory              the published matrix (default when no --case is given)
              --case <spec>               add a configuration; repeatable. Spec:
                                          name,method[:shape],frames,width,height,format,retentionMB
                                          [,medianMaxFrames[,medianMemMB[,medianScratchMB[,bandRows
                                          [,shiftPixels]]]]]
                                          method: mean | sum | median; format: mono8 | rgb16
                                          shape: streaming (default, the pipeline's path) | list
                                          retentionMB 0 = no retention budget (nothing spills)
              --work <dir>                fixture directory (default %TEMP%/opipp-integration-probe)
              --out <file.csv>            also write the measurements as CSV
              --csv                       print CSV instead of the aligned table
              --sample-ms <n>             working-set sampling interval, default 20
              --keep                      keep the generated fixtures
            """);
    }

    // ------------------------------------------------------------------ types

    private enum FixtureFormat
    {
        Mono8,
        Rgb16,
    }

    /// <summary>
    /// Which <see cref="FinalImageIntegrator"/> overload the child calls.
    /// </summary>
    /// <remarks>
    /// <see cref="Streaming"/> is the path <c>Processor</c> uses: the integrator pulls one frame at a
    /// time from the retained handles, so nothing beyond the retention layer is resident and a median
    /// reports <see cref="MedianRoute.Streamed"/>. <see cref="List"/> is the shape every other caller
    /// (and the route tests) use: the selection is materialised first, which is what makes the median
    /// router choose between <see cref="MedianRoute.InMemory"/> and <see cref="MedianRoute.Tiled"/>.
    /// Both are worth measuring because they answer different questions.
    /// </remarks>
    private enum ProbeShape
    {
        Streaming,
        List,
    }

    /// <summary>One configuration: what to generate, how to retain it and how to integrate it.</summary>
    private sealed record ProbeCase(
        string Name,
        IntegrationMethod Method,
        int Frames,
        int Width,
        int Height,
        FixtureFormat Format,
        int RetentionMb = 0,
        int MedianMaxFrames = 0,
        int MedianMemoryMb = 0,
        int MedianScratchMb = 0,
        int MedianBandRows = 0,
        int ShiftPixels = 0,
        ProbeShape Shape = ProbeShape.Streaming)
    {
        public int Channels => Format == FixtureFormat.Mono8 ? 1 : 3;

        public int BitDepth => Format == FixtureFormat.Mono8 ? 8 : 16;

        /// <summary>Bytes one frame occupies once retained: 16-bit samples whatever the file depth.</summary>
        public long CaptureBytes => (long)Frames * Width * Height * Channels * sizeof(ushort);

        /// <summary>Bytes the SER file occupies: a depth-8 source stores one byte per sample.</summary>
        public long FileBytes =>
            (long)Frames * Width * Height * Channels * (BitDepth <= 8 ? 1 : sizeof(ushort));

        public string FixtureName =>
            $"fixture-{Frames}x{Width}x{Height}-{FormatName(Format)}.ser";

        public static ProbeCase Parse(string text)
        {
            string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length is < 7 or > 12)
            {
                throw new ArgumentException($"case '{text}' needs 7 to 12 comma-separated fields");
            }

            string[] methodParts = parts[1].Split(':', StringSplitOptions.TrimEntries);
            IntegrationMethod method = methodParts[0].ToLowerInvariant() switch
            {
                "mean" => IntegrationMethod.Mean,
                "sum" => IntegrationMethod.Sum,
                "median" => IntegrationMethod.Median,
                _ => throw new ArgumentException($"case '{text}': unknown method '{methodParts[0]}'"),
            };

            ProbeShape shape = methodParts.Length > 1
                ? methodParts[1].ToLowerInvariant() switch
                {
                    "streaming" => ProbeShape.Streaming,
                    "list" => ProbeShape.List,
                    _ => throw new ArgumentException($"case '{text}': unknown shape '{methodParts[1]}' (streaming or list)"),
                }
                : ProbeShape.Streaming;
            if (methodParts.Length > 2)
            {
                throw new ArgumentException($"case '{text}': method takes at most one ':shape' suffix");
            }

            FixtureFormat format = parts[5].ToLowerInvariant() switch
            {
                "mono8" => FixtureFormat.Mono8,
                "rgb16" => FixtureFormat.Rgb16,
                _ => throw new ArgumentException($"case '{text}': unknown format '{parts[5]}' (mono8 or rgb16)"),
            };

            int Field(int index) => index < parts.Length && parts[index].Length != 0
                ? int.Parse(parts[index], CultureInfo.InvariantCulture)
                : 0;

            return new ProbeCase(
                parts[0],
                method,
                Positive(Field(2), text, "frames"),
                Positive(Field(3), text, "width"),
                Positive(Field(4), text, "height"),
                format,
                Math.Max(0, Field(6)),
                Math.Max(0, Field(7)),
                Math.Max(0, Field(8)),
                Math.Max(0, Field(9)),
                Math.Max(0, Field(10)),
                Math.Max(0, Field(11)),
                shape);
        }

        private static int Positive(int value, string text, string field) => value > 0
            ? value
            : throw new ArgumentException($"case '{text}': {field} must be positive");
    }

    private static class Suites
    {
        /// <summary>
        /// The matrix published in docs/memory.md: a small capture integrated both ways with and without
        /// spilling, the in-memory median route at Full HD RGB (which retains a copy per frame), the
        /// tiled route at a size that exercises its scratch pass, and a Full-HD mean as the control for
        /// the accumulator's own cost.
        /// </summary>
        public static readonly ProbeCase[] Memory =
        [
            new("small-mean-spilled", IntegrationMethod.Mean, 300, 512, 256, FixtureFormat.Mono8, RetentionMb: 1),
            new("small-mean-inline", IntegrationMethod.Mean, 300, 512, 256, FixtureFormat.Mono8),
            new("small-median-spilled", IntegrationMethod.Median, 300, 512, 256, FixtureFormat.Mono8, RetentionMb: 1),
            new("small-median-tiled", IntegrationMethod.Median, 300, 512, 256, FixtureFormat.Mono8,
                RetentionMb: 1, Shape: ProbeShape.List),
            new("hd-median-inmemory", IntegrationMethod.Median, 100, 1920, 1080, FixtureFormat.Rgb16,
                RetentionMb: 512, MedianMaxFrames: 128, MedianMemoryMb: 2048, Shape: ProbeShape.List),
            new("hd-median-inmemory-inline", IntegrationMethod.Median, 100, 1920, 1080, FixtureFormat.Rgb16,
                MedianMaxFrames: 128, MedianMemoryMb: 2048, Shape: ProbeShape.List),
            new("hd-median-tiled", IntegrationMethod.Median, 200, 1920, 1080, FixtureFormat.Rgb16,
                RetentionMb: 512, MedianScratchMb: 4096, Shape: ProbeShape.List),
            new("hd-mean-inline", IntegrationMethod.Mean, 100, 1920, 1080, FixtureFormat.Rgb16),
        ];

        public static ProbeCase[] Named(string name) => name.ToLowerInvariant() switch
        {
            "memory" => Memory,
            _ => throw new ArgumentException($"unknown suite '{name}' (known: memory)"),
        };
    }

    /// <summary>One row of the table: what the child did and what it cost.</summary>
    private sealed record Measurement
    {
        public required ProbeCase Case { get; init; }

        public required int ExitCode { get; init; }

        public string? Error { get; init; }

        public string Storage { get; init; } = "?";

        public string Route { get; init; } = "?";

        public double PeakMb { get; init; }

        public double RetainPeakMb { get; init; }

        public double IntegratePeakMb { get; init; }

        public double WallSeconds { get; init; }

        public double RetainSeconds { get; init; }

        public double RetainedMb { get; init; }

        public double ScratchMb { get; init; }

        public double OutputMb { get; init; }

        public double MedianEstimateMb { get; init; }

        public int BandRows { get; init; }

        public int Samples { get; init; }

        public static Measurement Failed(ProbeCase probe, int exitCode, string error, double wallSeconds, int samples) =>
            new()
            {
                Case = probe,
                ExitCode = exitCode == 0 ? 1 : exitCode,
                Error = error,
                WallSeconds = wallSeconds,
                Samples = samples,
            };
    }

    /// <summary>Options for one child run; parsed from the driver so the case grammar lives in one place.</summary>
    private sealed record ChildOptions(
        string SerPath,
        string Name,
        IntegrationMethod Method,
        ProbeShape Shape,
        int ExpectedFrames,
        long RetentionBytes,
        int MedianMaxFrames,
        int MedianMemoryMb,
        int MedianScratchMb,
        int MedianBandRows,
        int ShiftPixels,
        int SampleMs)
    {
        public static ChildOptions Parse(string[] args)
        {
            string? ser = null;
            string name = "case";
            IntegrationMethod method = IntegrationMethod.Mean;
            ProbeShape shape = ProbeShape.Streaming;
            int expectedFrames = 0;
            int retentionMb = 0;
            int medianMax = 0, medianMemory = 0, medianScratch = 0, bandRows = 0, shift = 0;
            int sampleMs = DefaultSampleMilliseconds;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {a}");
                switch (a)
                {
                    case ChildSwitch: break;
                    case "--ser": ser = Next(); break;
                    case "--name": name = Next(); break;
                    case "--expect-frames": expectedFrames = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--retention-mb": retentionMb = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--median-max-frames": medianMax = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--median-memory-mb": medianMemory = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--median-scratch-mb": medianScratch = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--median-band-rows": bandRows = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--shift": shift = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--sample-ms": sampleMs = PositiveInt(Next(), a); break;
                    case "--shape":
                        shape = Next().ToLowerInvariant() switch
                        {
                            "streaming" => ProbeShape.Streaming,
                            "list" => ProbeShape.List,
                            var other => throw new ArgumentException($"unknown shape '{other}'"),
                        };
                        break;
                    case "--method":
                        method = Next().ToLowerInvariant() switch
                        {
                            "mean" => IntegrationMethod.Mean,
                            "sum" => IntegrationMethod.Sum,
                            "median" => IntegrationMethod.Median,
                            var other => throw new ArgumentException($"unknown method '{other}'"),
                        };
                        break;
                    default: throw new ArgumentException($"unknown child argument '{a}'");
                }
            }

            if (string.IsNullOrWhiteSpace(ser)) throw new ArgumentException("child needs --ser <path>");
            return new ChildOptions(
                ser, name, method, shape, expectedFrames,
                retentionMb <= 0 ? long.MaxValue : retentionMb * 1048576L,
                Math.Max(0, medianMax), Math.Max(0, medianMemory), Math.Max(0, medianScratch),
                Math.Max(0, bandRows), Math.Max(0, shift), sampleMs);
        }
    }

    /// <summary>
    /// Keeps the frames the way <c>Processor.Retain</c> does: inline while the byte budget holds, then a
    /// single <see cref="FrameStore"/> into which the already-inline frames are migrated. Reproduced
    /// rather than approximated — a probe that retained differently would measure a different program.
    /// </summary>
    private sealed class RetentionSession : IDisposable
    {
        private readonly List<RetainedFrame> _handles = new();
        private readonly long _budgetBytes;
        private readonly string _storePath;
        private long _residentBytes;
        private FrameStore? _store;

        public RetentionSession(long budgetBytes, string storePath)
        {
            _budgetBytes = budgetBytes;
            _storePath = storePath;
        }

        public int Count => _handles.Count;

        /// <summary>Pixel bytes this session still holds in managed memory.</summary>
        public long ResidentBytes => _residentBytes;

        /// <summary>Pixel bytes this session wrote to its scratch file.</summary>
        public long StoredBytes => _store?.StoredBytes ?? 0;

        public bool Spilled => _store is not null;

        /// <summary>Handles whose pixels were materialised from the store — one fresh copy per read.</summary>
        public int StoredCount
        {
            get
            {
                int stored = 0;
                foreach (RetainedFrame handle in _handles)
                {
                    if (handle.IsStored) stored++;
                }
                return stored;
            }
        }

        public Frame FrameAt(int index) => _handles[index].Frame;

        public void Add(Frame frame)
        {
            int bytes = frame.Samples.Length * sizeof(ushort);
            if (_store is null && _residentBytes + bytes <= _budgetBytes)
            {
                _residentBytes += bytes;
                _handles.Add(RetainedFrame.Inline(frame));
                return;
            }

            FrameStore store = _store ??= new FrameStore(_storePath);

            // First overflow: the frames accepted so far move to the store, so a unit never has two
            // homes for its pixels.
            if (_residentBytes > 0)
            {
                for (int i = 0; i < _handles.Count; i++)
                {
                    RetainedFrame handle = _handles[i];
                    if (handle.IsStored) continue;
                    _handles[i] = RetainedFrame.Stored(store, store.Append(handle.Frame), handle.SourceName);
                }
                _residentBytes = 0;
            }

            _handles.Add(RetainedFrame.Stored(store, store.Append(frame), frame.SourceName));
        }

        public void Dispose() => _store?.Dispose();
    }

    /// <summary>
    /// Samples the current process's working set on a background thread and splits the history at
    /// <see cref="BeginIntegration"/>, so a peak can be attributed to retention or integration.
    /// </summary>
    private sealed class PhaseSampler : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly int _intervalMs;
        private readonly Thread _thread;
        private volatile bool _stop;
        private volatile int _phase;
        private long _retainPeak;
        private long _integratePeak;
        private int _samples;

        public PhaseSampler(int intervalMs)
        {
            _intervalMs = Math.Max(1, intervalMs);
            _thread = new Thread(Loop) { IsBackground = true, Name = "opipp-ws-sampler" };
            _thread.Start();
        }

        public long RetainPeakBytes => Interlocked.Read(ref _retainPeak);

        public long IntegratePeakBytes => Interlocked.Read(ref _integratePeak);

        public long PeakBytes => Math.Max(RetainPeakBytes, IntegratePeakBytes);

        public int Samples => Volatile.Read(ref _samples);

        public void BeginIntegration() => _phase = 1;

        public void EndIntegration() => _stop = true;

        public void Dispose()
        {
            _stop = true;
            _thread.Join(2000);
        }

        private void Loop()
        {
            while (!_stop)
            {
                try
                {
                    _process.Refresh();
                    long workingSet = _process.WorkingSet64;
                    if (_phase == 0) Raise(ref _retainPeak, workingSet);
                    else Raise(ref _integratePeak, workingSet);
                    Interlocked.Increment(ref _samples);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    return;
                }

                Thread.Sleep(_intervalMs);
            }
        }

        private static void Raise(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current) return;
                current = seen;
            }
        }
    }
}
