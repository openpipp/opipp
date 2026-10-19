using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The master rebuild-vs-reuse and handoff-switch decisions, driven through the toggle sequence the
/// operator performs rather than through hand-written engine arguments.
/// </summary>
public sealed class MasterHandoffPlanTests
{
    /// <summary>Builds requests for one temp directory and carries the cache records like Form1 does.</summary>
    private sealed class CacheRig : IDisposable
    {
        public CacheRig()
        {
            Root = AppTestSupport.TempDirectory();
            FlatFile = Path.Combine(Root, "flat.ser");
            FlatDarkFile = Path.Combine(Root, "flat_dark.ser");
            DarkFile = Path.Combine(Root, "dark.ser");
        }

        public string Root { get; }

        public string FlatFile { get; }

        public string FlatDarkFile { get; }

        public string DarkFile { get; }

        public string DarkTmp => Path.Combine(Root, "pipp_master_dark.tmp");

        public string DarkPgm => Path.Combine(Root, "pipp_master_dark.pgm");

        public string FlatDarkTmp => Path.Combine(Root, "pipp_master_flat_dark.tmp");

        public string FlatDarkPgm => Path.Combine(Root, "pipp_master_flat_dark.pgm");

        public string FlatTmp => Path.Combine(Root, "pipp_master_flat.tmp");

        public CachedMaster? Dark { get; set; }

        public CachedMaster? FlatDark { get; set; }

        public CachedMaster? Flat { get; set; }

        public MasterHandoffRequest Request(
            bool flatCalibration,
            bool flatDarkSubtraction,
            bool darkSubtraction = true,
            bool saveFlatDark = false,
            string? flatFile = null)
        {
            return new MasterHandoffRequest
            {
                TestRun = false,
                SaveFlatDark = saveFlatDark,
                Dark = new MasterStageRequest(MasterKind.Dark)
                {
                    Enabled = darkSubtraction,
                    Files = [DarkFile],
                    FrameCount = 1,
                    MasterPath = DarkTmp,
                    PgmPath = DarkPgm,
                },
                FlatDark = new MasterStageRequest(MasterKind.FlatDark)
                {
                    Enabled = flatDarkSubtraction,
                    Files = [FlatDarkFile],
                    FrameCount = 1,
                    MasterPath = FlatDarkTmp,
                    PgmPath = FlatDarkPgm,
                },
                Flat = new MasterStageRequest(MasterKind.Flat)
                {
                    Enabled = flatCalibration,
                    Files = [flatFile ?? FlatFile],
                    FrameCount = 1,
                    MasterPath = FlatTmp,
                },
                DarkCache = Dark,
                FlatDarkCache = FlatDark,
                FlatCache = Flat,
            };
        }

        /// <summary>Stands in for Form1's stage execution: publish the artifact and record its identity.</summary>
        public void Execute(MasterHandoffPlan plan)
        {
            if (plan.Dark.Enabled && plan.Dark.Rebuild)
            {
                Publish(plan.Dark);
                Dark = plan.Dark.CreateEntry(1);
            }

            if (plan.FlatDark.Enabled && plan.FlatDark.Rebuild)
            {
                Publish(plan.FlatDark);
                FlatDark = plan.FlatDark.CreateEntry(1);
            }

            if (plan.Flat.Enabled && plan.Flat.Rebuild)
            {
                Publish(plan.Flat);
                Flat = plan.Flat.CreateEntry(1);
            }
        }

        private static void Publish(MasterStagePlan stage)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stage.MasterPath)!);
            File.WriteAllBytes(stage.MasterPath, [0]);
        }

        public void Dispose() => AppTestSupport.DeleteQuietly(Root);
    }

    private static bool HasFlatDarkSwitch(MasterHandoffPlan plan) =>
        plan.Arguments.Any(argument => argument.StartsWith("-flat_dark_tmp=", StringComparison.Ordinal));

    private static bool HasFlatSwitch(MasterHandoffPlan plan) =>
        plan.Arguments.Any(argument => argument.StartsWith("-flat_tmp=", StringComparison.Ordinal));

    [Fact]
    public void FlatCalibrationOff_KeepsTheCorrectedFlat_AndDoesNotHandTheFlatDarkOver()
    {
        using var rig = new CacheRig();

        // Run 1: flat calibration and flat-dark subtraction are both on. The front-end prepares
        // the flat (builds it with the flat dark already removed) and must not pass the flat dark
        // to the engine as well.
        MasterHandoffPlan first = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        Assert.True(first.FlatDark.Enabled);
        Assert.True(first.FlatDark.Rebuild);
        Assert.True(first.Flat.Enabled);
        Assert.True(first.Flat.Rebuild);
        Assert.True(first.Flat.Corrected);
        Assert.Equal(rig.FlatDarkTmp, first.Flat.SubtractPath);
        Assert.True(HasFlatSwitch(first));
        Assert.False(HasFlatDarkSwitch(first), "a prepared flat must not also hand over the flat dark");
        rig.Execute(first);
        Assert.NotNull(rig.Flat);
        Assert.True(rig.Flat!.Corrected);

        // Run 2: flat calibration is switched off. Nothing in the caches may be invalidated — the
        // corrected master on disk still describes the same inputs.
        MasterHandoffPlan middle = MasterHandoffPlanner.Create(rig.Request(flatCalibration: false, flatDarkSubtraction: true));
        Assert.False(middle.Flat.Enabled);
        Assert.False(middle.FlatDark.Enabled);
        Assert.False(HasFlatSwitch(middle));
        rig.Execute(middle);
        Assert.NotNull(rig.Flat);
        Assert.True(rig.Flat!.Corrected);

        // Run 3: flat calibration is switched back on. The cached corrected flat is reused and the
        // engine still must not receive -flat_dark_tmp; the old bug did exactly that and subtracted
        // the flat dark a second time.
        MasterHandoffPlan again = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        Assert.True(again.Flat.Enabled);
        Assert.False(again.Flat.Rebuild, "the unchanged, still-present corrected flat should be reused");
        Assert.False(again.FlatDark.Rebuild, "the unchanged, still-present flat dark should be reused");
        Assert.True(again.Flat.Corrected);
        Assert.True(HasFlatSwitch(again));
        Assert.False(HasFlatDarkSwitch(again));
        Assert.Equal(rig.FlatDarkTmp, again.FlatDark.MasterPath);
    }

    [Fact]
    public void FlatDarkSubtractionOff_RebuildsTheFlatWithoutTheCorrection()
    {
        using var rig = new CacheRig();

        MasterHandoffPlan first = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        rig.Execute(first);
        Assert.True(rig.Flat!.Corrected);

        // The operator no longer wants the flat dark applied. A corrected master would still carry
        // it, so the flat has to be rebuilt from the raw flat frames.
        MasterHandoffPlan withoutFlatDark = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: false));
        Assert.False(withoutFlatDark.FlatDark.Enabled);
        Assert.True(withoutFlatDark.Flat.Rebuild);
        Assert.False(withoutFlatDark.Flat.Corrected);
        Assert.Null(withoutFlatDark.Flat.SubtractPath);
        Assert.False(HasFlatDarkSwitch(withoutFlatDark));
    }

    [Fact]
    public void CachedCorrectedFlatFromDifferentFlatDarkInputs_IsRebuilt()
    {
        using var rig = new CacheRig();

        MasterHandoffPlan first = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        rig.Execute(first);

        // Same flat master file, but the flat-dark set it was corrected with changed: the stored
        // correction identity no longer matches, so the flat cannot be reused.
        MasterHandoffRequest changed = rig.Request(flatCalibration: true, flatDarkSubtraction: true);
        changed.FlatDark.Files = [Path.Combine(rig.Root, "other_flat_dark.ser")];

        MasterHandoffPlan plan = MasterHandoffPlanner.Create(changed);
        Assert.True(plan.Flat.Rebuild);
        Assert.True(plan.FlatDark.Rebuild);
    }

    [Fact]
    public void MissingCachedArtifact_IsRebuiltEvenWhenTheCountMatches()
    {
        using var rig = new CacheRig();
        MasterHandoffPlan first = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        rig.Execute(first);

        File.Delete(rig.FlatTmp);

        MasterHandoffPlan plan = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        Assert.True(plan.Flat.Rebuild, "a deleted cached master must be rebuilt, not handed to the engine");

        // The flat dark is still present and keeps its identity.
        Assert.False(plan.FlatDark.Rebuild);
    }

    [Fact]
    public void UncorrectedCachedFlat_WithFlatDarkOn_IsReusedAndTheEngineSubtractsOnce()
    {
        using var rig = new CacheRig();

        // Built with flat-dark subtraction off: the master is the raw flat.
        MasterHandoffPlan plain = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: false));
        Assert.True(plain.Flat.Rebuild);
        Assert.False(plain.Flat.Corrected);
        rig.Execute(plain);

        // Enabled afterwards: the raw flat is still valid, and the engine gets exactly one flat-dark
        // switch to subtract it per frame.
        MasterHandoffPlan withFlatDark = MasterHandoffPlanner.Create(rig.Request(flatCalibration: true, flatDarkSubtraction: true));
        Assert.False(withFlatDark.Flat.Rebuild);
        Assert.False(withFlatDark.Flat.Corrected);
        Assert.True(HasFlatDarkSwitch(withFlatDark));
        Assert.Contains("-flat_dark_tmp=" + rig.FlatDarkTmp, withFlatDark.Arguments);
    }

    [Fact]
    public void DarkStage_EmitsBothHandoffSwitches_AndNoneWhenDisabled()
    {
        using var rig = new CacheRig();

        MasterHandoffPlan enabled = MasterHandoffPlanner.Create(
            rig.Request(flatCalibration: true, flatDarkSubtraction: false, darkSubtraction: true));
        Assert.Contains("-dark_tmp=" + rig.DarkTmp, enabled.Arguments);
        Assert.Contains("-dark_pgm=" + rig.DarkPgm, enabled.Arguments);

        MasterHandoffPlan disabled = MasterHandoffPlanner.Create(
            rig.Request(flatCalibration: true, flatDarkSubtraction: false, darkSubtraction: false));
        Assert.DoesNotContain(disabled.Arguments, argument =>
            argument.StartsWith("-dark_tmp=", StringComparison.Ordinal) ||
            argument.StartsWith("-dark_pgm=", StringComparison.Ordinal));
    }

    [Fact]
    public void SaveFlatDark_IsOfferedOnlyWhenTheFlatDarkStageRuns()
    {
        using var rig = new CacheRig();

        MasterHandoffPlan saving = MasterHandoffPlanner.Create(
            rig.Request(flatCalibration: true, flatDarkSubtraction: true, saveFlatDark: true));
        Assert.Equal(rig.FlatDarkTmp, saving.FlatDarkMasterToSave);

        MasterHandoffPlan notSaving = MasterHandoffPlanner.Create(
            rig.Request(flatCalibration: true, flatDarkSubtraction: false, saveFlatDark: true));
        Assert.Null(notSaving.FlatDarkMasterToSave);

        MasterHandoffPlan notRequested = MasterHandoffPlanner.Create(
            rig.Request(flatCalibration: true, flatDarkSubtraction: true, saveFlatDark: false));
        Assert.Null(notRequested.FlatDarkMasterToSave);
    }

    [Fact]
    public void TestRun_ReusesAFullBuildMaster_InsteadOfRebuildingIt()
    {
        using var rig = new CacheRig();
        File.WriteAllBytes(rig.FlatTmp, [0]);

        // A full build records "no limit" as its identity. A later test run must not shrink that
        // master to ten frames (which would change the result), so it keeps the unlimited identity
        // and simply reuses the file.
        rig.Flat = new CachedMaster(
            rig.FlatTmp,
            null,
            40,
            MasterHandoffPlanner.Signature([rig.FlatFile], 0),
            corrected: false,
            correctionSignature: null);

        MasterHandoffRequest request = rig.Request(flatCalibration: true, flatDarkSubtraction: false);
        request.TestRun = true;
        request.Flat.FrameCount = 40;
        request.FlatCache = rig.Flat;

        MasterHandoffPlan plan = MasterHandoffPlanner.Create(request);
        Assert.Equal(0, plan.Flat.FrameLimit);
        Assert.False(plan.Flat.Rebuild);
    }

    [Fact]
    public void TestRun_CapsTheMasterAtTenFrames_AndReusesTheCappedMaster()
    {
        using var rig = new CacheRig();
        MasterHandoffRequest request = rig.Request(flatCalibration: true, flatDarkSubtraction: false);
        request.TestRun = true;
        request.Flat.FrameCount = 40;

        MasterHandoffPlan capped = MasterHandoffPlanner.Create(request);
        Assert.Equal(10, capped.Flat.FrameLimit);
        rig.Execute(capped);
        Assert.Equal(1, rig.Flat!.FrameCount);

        // The next test run sees a cache that already holds fewer than ten frames, so the same
        // ten-frame limit (and therefore the same identity) applies and the master is reused.
        request.FlatCache = rig.Flat;
        MasterHandoffPlan reuse = MasterHandoffPlanner.Create(request);
        Assert.Equal(10, reuse.Flat.FrameLimit);
        Assert.False(reuse.Flat.Rebuild);
    }
}
