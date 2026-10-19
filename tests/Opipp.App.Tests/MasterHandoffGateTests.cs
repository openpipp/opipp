using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The numerical end-to-end gate for the calibration handoff: the plan decides what to build and
/// which switches to pass, and the real managed engine processes the fixture.
/// </summary>
/// <remarks>
/// Fixture (the repository's documented one): flat [200,100], flat dark [50,0], science [125,125].
/// One flat-dark subtraction gives [104,156]; subtracting it twice gives [125,125]. The middle run
/// disables flat calibration entirely, so its correct output is the untouched science frame.
/// </remarks>
public sealed class MasterHandoffGateTests
{
    private sealed class GateRig : IDisposable
    {
        public GateRig()
        {
            Directory = AppTestSupport.TempDirectory();
            SciencePath = AppTestSupport.WriteUniformSer(Directory, "science.ser", 125);
            FlatPath = AppTestSupport.WriteSplitSer(Directory, "flat.ser", 200, 100);
            FlatDarkPath = AppTestSupport.WriteSplitSer(Directory, "flat_dark.ser", 50, 0);
        }

        public string Directory { get; }

        public string SciencePath { get; }

        public string FlatPath { get; }

        public string FlatDarkPath { get; }

        public string FlatTmp => Path.Combine(Directory, "pipp_master_flat.tmp");

        public string FlatDarkTmp => Path.Combine(Directory, "pipp_master_flat_dark.tmp");

        public CachedMaster? Flat { get; private set; }

        public CachedMaster? FlatDark { get; private set; }

        /// <summary>
        /// Builds (or reuses) the masters exactly as <c>Form1.do_processing_thread_core</c> does,
        /// starting from the plan the GUI would compute.
        /// </summary>
        public MasterHandoffPlan Execute(bool flatCalibration, bool flatDarkSubtraction, bool saveFlatDark = false)
        {
            var request = new MasterHandoffRequest
            {
                SaveFlatDark = saveFlatDark,
                FlatDark = new MasterStageRequest(MasterKind.FlatDark)
                {
                    Enabled = flatDarkSubtraction,
                    Files = [FlatDarkPath],
                    FrameCount = 1,
                    MasterPath = FlatDarkTmp,
                    PgmPath = Path.Combine(Directory, "pipp_master_flat_dark.pgm"),
                },
                Flat = new MasterStageRequest(MasterKind.Flat)
                {
                    Enabled = flatCalibration,
                    Files = [FlatPath],
                    FrameCount = 1,
                    MasterPath = FlatTmp,
                },
                FlatDarkCache = FlatDark,
                FlatCache = Flat,
            };

            MasterHandoffPlan plan = MasterHandoffPlanner.Create(request);
            if (plan.FlatDark.Enabled && plan.FlatDark.Rebuild)
            {
                MasterBuildResult result = MasterFrameBuilder.Build(
                    plan.FlatDark.Files, null, plan.FlatDark.MasterPath, plan.FlatDark.PgmPath, plan.FlatDark.FrameLimit);
                Assert.True(result.Success, result.Error);
                FlatDark = plan.FlatDark.CreateEntry(result.FrameCount);
            }

            if (plan.Flat.Enabled && plan.Flat.Rebuild)
            {
                MasterBuildResult result = MasterFrameBuilder.Build(
                    plan.Flat.Files, plan.Flat.SubtractPath, plan.Flat.MasterPath, null, plan.Flat.FrameLimit);
                Assert.True(result.Success, result.Error);
                Flat = plan.Flat.CreateEntry(result.FrameCount);
            }

            return plan;
        }

        /// <summary>Runs the engine with the switches the GUI would generate for this plan.</summary>
        public (int ExitCode, string StdOut, string StdErr) Run(
            MasterHandoffPlan plan,
            bool flatCalibration,
            bool flatDarkSubtraction,
            string outputDirectory)
        {
            var arguments = new List<string> { "-tif", "-od=" + outputDirectory };
            if (flatCalibration)
            {
                arguments.Add("-flat");
            }

            if (flatDarkSubtraction)
            {
                arguments.Add("-flat_dark");
            }

            arguments.AddRange(plan.Arguments);
            arguments.Add(SciencePath);
            return AppTestSupport.RunEngine(arguments);
        }

        public void Dispose() => AppTestSupport.DeleteQuietly(Directory);
    }

    [Fact]
    public void Calibrated_ThenFlatCalibrationOff_ThenCalibratedAgain_StaysCorrectedOnce()
    {
        using var rig = new GateRig();

        // 1. Flat calibration with flat-dark subtraction: the GUI prepares the flat and passes no
        //    -flat_dark_tmp. Science 125 with flat 150/100 -> 104/156.
        MasterHandoffPlan first = rig.Execute(flatCalibration: true, flatDarkSubtraction: true);
        Assert.True(first.Flat.Corrected);
        Assert.DoesNotContain(first.Arguments, a => a.StartsWith("-flat_dark_tmp=", StringComparison.Ordinal));

        string out1 = Path.Combine(rig.Directory, "out1");
        (int exit1, _, string error1) = rig.Run(first, true, true, out1);
        Assert.Equal(0, exit1);
        Assert.Equal(string.Empty, error1);
        Frame calibrated = AppTestSupport.ReadSingleTiff(out1);
        Assert.Equal(104, calibrated[0, 0]);
        Assert.Equal(156, calibrated[7, 0]);

        // 2. Flat calibration disabled: the science frame is written untouched (no correction at
        //    all), and the corrected master must survive this run untouched.
        MasterHandoffPlan middle = rig.Execute(flatCalibration: false, flatDarkSubtraction: true);
        Assert.False(middle.Flat.Enabled);
        Assert.False(middle.FlatDark.Enabled);

        string out2 = Path.Combine(rig.Directory, "out2");
        (int exit2, _, string error2) = rig.Run(middle, false, true, out2);
        Assert.Equal(0, exit2);
        Assert.Equal(string.Empty, error2);
        Frame uncorrected = AppTestSupport.ReadSingleTiff(out2);
        Assert.Equal(125, uncorrected[0, 0]);
        Assert.Equal(125, uncorrected[7, 0]);

        // 3. Flat calibration back on with the same inputs: the cached corrected flat is reused,
        //    and the engine still receives no flat dark. A stale "not corrected" flag used to add
        //    -flat_dark_tmp here, producing 125/125.
        MasterHandoffPlan again = rig.Execute(flatCalibration: true, flatDarkSubtraction: true);
        Assert.False(again.Flat.Rebuild);
        Assert.False(again.FlatDark.Rebuild);
        Assert.True(again.Flat.Corrected);
        Assert.DoesNotContain(again.Arguments, a => a.StartsWith("-flat_dark_tmp=", StringComparison.Ordinal));

        string out3 = Path.Combine(rig.Directory, "out3");
        (int exit3, _, string error3) = rig.Run(again, true, true, out3);
        Assert.Equal(0, exit3);
        Assert.Equal(string.Empty, error3);
        Frame recalibrated = AppTestSupport.ReadSingleTiff(out3);
        Assert.Equal(104, recalibrated[0, 0]);
        Assert.Equal(156, recalibrated[7, 0]);
    }

    [Fact]
    public void DoubleSubtractionControl_StillReads125And125()
    {
        // The negative control for the gate: hand the engine a prepared flat AND the flat dark, and
        // the same fixture reads 125/125. This proves 104/156 above is one subtraction.
        using var rig = new GateRig();

        // The prepared flat (flat dark already removed), handed over together with the flat dark:
        // this is the handoff the old GUI produced after the cached "corrected" flag was lost.
        string flatDarkTmp = Path.Combine(rig.Directory, "control_flat_dark.tmp");
        Assert.True(MasterFrameBuilder.Build([rig.FlatDarkPath], null, flatDarkTmp, null, 0).Success);

        string flatTmp = Path.Combine(rig.Directory, "control_flat.tmp");
        Assert.True(MasterFrameBuilder.Build([rig.FlatPath], flatDarkTmp, flatTmp, null, 0).Success);

        string output = Path.Combine(rig.Directory, "out-doubled");
        (int exitCode, _, string error) = AppTestSupport.RunEngine([
            "-tif",
            "-flat",
            "-flat_dark",
            "-flat_tmp=" + flatTmp,
            "-flat_dark_tmp=" + flatDarkTmp,
            "-od=" + output,
            rig.SciencePath,
        ]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Frame doubled = AppTestSupport.ReadSingleTiff(output);
        Assert.Equal(125, doubled[0, 0]);
        Assert.Equal(125, doubled[7, 0]);
    }

    [Fact]
    public void SavedFlatDarkTiff_MatchesTheBuiltMaster_AndTheScienceHandoffIsStillCorrected()
    {
        using var rig = new GateRig();

        MasterHandoffPlan plan = rig.Execute(flatCalibration: true, flatDarkSubtraction: true, saveFlatDark: true);
        Assert.NotNull(rig.FlatDark);
        Assert.Equal(rig.FlatDarkTmp, plan.FlatDarkMasterToSave);
        Assert.DoesNotContain(plan.Arguments, a => a.StartsWith("-flat_dark_tmp=", StringComparison.Ordinal));

        string savedDirectory = Path.Combine(rig.Directory, "saved");
        string saved = MasterHandoffArtifacts.SaveFlatDarkMaster(plan.FlatDarkMasterToSave!, savedDirectory);

        Assert.Equal(Path.Combine(savedDirectory, "logs", "pipp_master_flat_dark.tif"), saved);
        Assert.True(File.Exists(saved));

        // The saved TIFF is the master the GUI built: same geometry, depth and samples.
        Frame built = MasterTmpFile.Read(rig.FlatDarkTmp);
        using (var reader = new TiffReader(saved))
        {
            Frame written = reader.ReadFrame() ?? throw new InvalidOperationException("the saved TIFF has no frame");
            Assert.Equal(built.Width, written.Width);
            Assert.Equal(built.Height, written.Height);
            Assert.Equal(built.Channels, written.Channels);
            Assert.Equal(built.BitDepth, written.BitDepth);
            Assert.Equal(built.Samples, written.Samples);
        }

        // Saving the master did not restore the double subtraction: the same handoff still yields
        // the single-subtraction values.
        string runDirectory = Path.Combine(rig.Directory, "out-saved");
        (int exitCode, _, string error) = rig.Run(plan, true, true, runDirectory);
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Frame calibrated = AppTestSupport.ReadSingleTiff(runDirectory);
        Assert.Equal(104, calibrated[0, 0]);
        Assert.Equal(156, calibrated[7, 0]);
    }

    [Fact]
    public void PreparedFlatMasterOnDisk_HasTheFlatDarkRemoved()
    {
        using var rig = new GateRig();

        MasterHandoffPlan plan = rig.Execute(flatCalibration: true, flatDarkSubtraction: true);
        Assert.True(plan.Flat.Corrected);
        Assert.Equal(rig.FlatDarkTmp, plan.Flat.SubtractPath);

        Frame prepared = MasterTmpFile.Read(rig.FlatTmp);
        Assert.Equal(150, prepared[0, 0]);
        Assert.Equal(100, prepared[7, 0]);
    }
}
