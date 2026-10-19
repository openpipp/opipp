using System.Globalization;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;

namespace Opipp.Cli.Tests;

/// <summary>
/// The calibration-master handoff, end to end: the GUI builds the masters, writes them to disk, and
/// launches the engine with the switches it would actually pass. The numerical fixture is the
/// documented one — flat [200,100], flat dark [50,0], science [125,125] — whose single-subtraction
/// result is [104,156]. A double subtraction yields [125,125], so the negative control below proves
/// the handoff does not subtract the flat dark twice.
/// </summary>
/// <remarks>
/// Console redirection is process-wide, so this class joins the non-parallel collection used by the
/// other CLI transcript tests.
/// </remarks>
[Collection("gui-stdout")]
public class CalibrationMasterHandoffTests
{
    private static readonly object ConsoleGate = new();

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-masterhandoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>8x8 mono 8-bit SER whose left half is <paramref name="left"/> and right half <paramref name="right"/>.</summary>
    private static string WriteSplit(string dir, string name, ushort left, ushort right)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, 8, 8, 1, 8);
        var frame = new Frame(8, 8, 1, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                frame[x, y] = x < 4 ? left : right;
        writer.Write(frame);
        return path;
    }

    private static string WriteUniform(string dir, string name, ushort value)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, 8, 8, 1, 8);
        var frame = new Frame(8, 8, 1, 8);
        Array.Fill(frame.Samples, value);
        writer.Write(frame);
        return path;
    }

    /// <summary>Reads the single TIFF the run produced, wherever the unit subdirectory put it.</summary>
    private static Frame ReadSingleOutput(string outputDirectory)
    {
        string[] files = Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories);
        Assert.Single(files);
        using var reader = new TiffReader(files[0]);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {files[0]}");
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string[] arguments)
    {
        lock (ConsoleGate)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            var stdout = new StringWriter(CultureInfo.InvariantCulture);
            var stderr = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Program.Main(arguments);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void FlatDarkFixture_CalibratesExactlyOnceTo104And156()
    {
        string dir = TempDir();
        try
        {
            string science = WriteUniform(dir, "science.ser", 125);
            string flat = WriteSplit(dir, "flat.ser", 200, 100);
            string flatDark = WriteSplit(dir, "flat_dark.ser", 50, 0);

            // 1. Master creation, exactly as Form1 does it: the flat dark first, then the corrected
            //    flat using that .tmp as the subtraction reference.
            string flatDarkTmp = Path.Combine(dir, "pipp_master_flat_dark.tmp");
            MasterBuildResult flatDarkMaster = MasterFrameBuilder.Build([flatDark], null, flatDarkTmp, null, 0);
            Assert.True(flatDarkMaster.Success, flatDarkMaster.Error);

            string flatTmp = Path.Combine(dir, "pipp_master_flat.tmp");
            MasterBuildResult flatMaster = MasterFrameBuilder.Build([flat], flatDarkTmp, flatTmp, null, 0);
            Assert.True(flatMaster.Success, flatMaster.Error);

            // The prepared master is the corrected flat: 200-50, 100-0.
            Frame prepared = MasterTmpFile.Read(flatTmp);
            Assert.Equal(150, prepared[0, 0]);
            Assert.Equal(100, prepared[7, 0]);

            // 2. The exact switches the GUI now passes: -flat_tmp only, no -flat_dark_tmp.
            string outputDirectory = Path.Combine(dir, "out");
            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-flat",
                "-flat_tmp=" + flatTmp,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);

            Frame calibrated = ReadSingleOutput(outputDirectory);
            Assert.Equal(1, calibrated.Channels);
            Assert.Equal(104, calibrated[0, 0]);
            Assert.Equal(156, calibrated[7, 0]);

            // 3. Negative control: the old handoff handed the flat dark to the engine as well, which
            //    subtracts it a second time. Same files, one extra switch, a visibly different result.
            string doubledDirectory = Path.Combine(dir, "out-doubled");
            (int doubledExit, _, _) = Run([
                "-tif",
                "-flat",
                "-flat_dark",
                "-flat_tmp=" + flatTmp,
                "-flat_dark_tmp=" + flatDarkTmp,
                "-od=" + doubledDirectory,
                science,
            ]);

            Assert.Equal(0, doubledExit);
            Frame doubled = ReadSingleOutput(doubledDirectory);
            Assert.Equal(125, doubled[0, 0]);
            Assert.Equal(125, doubled[7, 0]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void UnpreparedFlat_WithFlatDark_IsSubtractedOnceByTheEngine()
    {
        // The CLI path for a user-supplied flat plus a flat dark: the flat has NOT been corrected,
        // so Processor must subtract the flat dark itself. This keeps the switch meaningful after
        // the GUI stopped passing it for its own prepared master.
        string dir = TempDir();
        try
        {
            string science = WriteUniform(dir, "science.ser", 125);
            string unpreparedFlat = WriteSplit(dir, "flat.ser", 200, 100);
            string flatDark = WriteSplit(dir, "flat_dark.ser", 50, 0);

            // A flat master is always handed over as a .tmp; build it without subtraction.
            string flatTmp = Path.Combine(dir, "pipp_master_flat.tmp");
            Assert.True(MasterFrameBuilder.Build([unpreparedFlat], null, flatTmp, null, 0).Success);

            string flatDarkTmp = Path.Combine(dir, "pipp_master_flat_dark.tmp");
            Assert.True(MasterFrameBuilder.Build([flatDark], null, flatDarkTmp, null, 0).Success);

            string outputDirectory = Path.Combine(dir, "out");
            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-flat",
                "-flat_dark",
                "-flat_tmp=" + flatTmp,
                "-flat_dark_tmp=" + flatDarkTmp,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);

            Frame calibrated = ReadSingleOutput(outputDirectory);
            // 125 * 125 / 150 = 104, 125 * 125 / 100 = 156: one subtraction, done by the engine.
            Assert.Equal(104, calibrated[0, 0]);
            Assert.Equal(156, calibrated[7, 0]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ColourDarkMaster_IsTakenFromTheTmpEvenWhenALumaPgmIsAlsoPassed()
    {
        // Defect: the GUI hands both -dark_tmp (3-channel) and -dark_pgm (1-channel luma). Loading the
        // PGM first made every colour capture fail the channel check. The .tmp must win.
        string dir = TempDir();
        try
        {
            string sciencePath = Path.Combine(dir, "colour.ser");
            using (var writer = new SerWriter(sciencePath, 8, 8, 3, 8))
            {
                var frame = new Frame(8, 8, 3, 8);
                for (int i = 0; i < frame.Samples.Length; i += 3)
                {
                    frame.Samples[i] = 100;
                    frame.Samples[i + 1] = 150;
                    frame.Samples[i + 2] = 200;
                }
                writer.Write(frame);
            }

            var dark = new Frame(8, 8, 3, 8);
            for (int i = 0; i < dark.Samples.Length; i += 3)
            {
                dark.Samples[i] = 10;
                dark.Samples[i + 1] = 20;
                dark.Samples[i + 2] = 30;
            }
            string tmp = Path.Combine(dir, "pipp_master_dark.tmp");
            MasterTmpFile.Write(dark, tmp);

            var luma = new Frame(8, 8, 1, 8);
            Array.Fill(luma.Samples, (ushort)99);
            string pgm = Path.Combine(dir, "pipp_master_dark.pgm");
            PgmFile.Write(pgm, luma);

            string outputDirectory = Path.Combine(dir, "out");
            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-dark",
                "-dark_tmp=" + tmp,
                "-dark_pgm=" + pgm,
                "-od=" + outputDirectory,
                sciencePath,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);

            Frame calibrated = ReadSingleOutput(outputDirectory);
            Assert.Equal(3, calibrated.Channels);
            Assert.Equal(90, calibrated[0, 0, 0]);
            Assert.Equal(130, calibrated[0, 0, 1]);
            Assert.Equal(170, calibrated[0, 0, 2]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void LoadMaster_PrefersTheFullFidelityTmpOverTheLumaPgm()
    {
        string dir = TempDir();
        try
        {
            // A colour dark master: the .tmp carries three channels and 16 bits, the PGM the engine's
            // legacy handoff would carry is one-channel luma. Loading the PGM fails the channel check.
            var colour = new Frame(4, 4, 3, 16);
            for (int i = 0; i < colour.Samples.Length; i++) colour.Samples[i] = (ushort)(1000 + i);
            string tmp = Path.Combine(dir, "pipp_master_dark.tmp");
            MasterTmpFile.Write(colour, tmp);

            var luma = new Frame(4, 4, 1, 16);
            Array.Fill(luma.Samples, (ushort)1234);
            string pgm = Path.Combine(dir, "pipp_master_dark.pgm");
            PgmFile.Write(pgm, luma);

            Frame? master = Program.LoadMaster(tmp, pgm, "dark", enabled: true);

            Assert.NotNull(master);
            Assert.Equal(3, master!.Channels);
            Assert.Equal(16, master.BitDepth);
            Assert.Equal(colour.Samples, master.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadMaster_UsesThePgmOnlyWhenTheTmpIsAbsent()
    {
        string dir = TempDir();
        try
        {
            var luma = new Frame(4, 4, 1, 16);
            Array.Fill(luma.Samples, (ushort)777);
            string pgm = Path.Combine(dir, "pipp_master_dark.pgm");
            PgmFile.Write(pgm, luma);

            Frame? master = Program.LoadMaster(
                Path.Combine(dir, "absent.tmp"), pgm, "dark", enabled: true);

            Assert.NotNull(master);
            Assert.Equal(1, master!.Channels);
            Assert.All(master.Samples, v => Assert.Equal(777, v));

            // Disabled calibration loads nothing even when both files exist.
            Assert.Null(Program.LoadMaster(Path.Combine(dir, "absent.tmp"), pgm, "dark", enabled: false));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadMaster_UnreadableTmp_IsAnErrorNotASilentSkip()
    {
        string dir = TempDir();
        try
        {
            string tmp = Path.Combine(dir, "pipp_master_dark.tmp");
            File.WriteAllText(tmp, "this is not a master");

            string pgm = Path.Combine(dir, "pipp_master_dark.pgm");
            PgmFile.Write(pgm, new Frame(4, 4, 1, 8));

            // No fallback to the lossy PGM: a corrupt authoritative master must stop the run.
            Assert.Throws<InvalidDataException>(() => Program.LoadMaster(tmp, pgm, "dark", enabled: true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
