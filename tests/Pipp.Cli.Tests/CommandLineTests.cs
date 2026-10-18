using System.Globalization;
using Pipp.Core.Cli;

namespace Pipp.Cli.Tests;

/// <summary>
/// Tests for the typed PIPP command-line model: defaults, parsing of every switch in the engine
/// vocabulary, error reporting, numeric edges and full <c>PippOptions -> Build -> Parse</c> round-trips.
/// </summary>
public class CommandLineTests
{
    /// <summary>
    /// Every switch spelling in the <c>pipp_core.exe</c> vocabulary, each paired with a sample value.
    /// This is the authoritative list used by the parse/help/builder coverage tests.
    /// </summary>
    private static readonly string[] AllSwitches =
    [
        // Output formats.
        "-bmp", "-tif", "-png", "-fits", "-ser", "-avi", "-gif",

        // Output naming.
        @"-od=C:\out", "-op=pre", "-os=suf", "-osubdir=run", "-drop=5", "-nr", "-nlz", "-incqv", "-rgb",
        "-keepimgname",

        // AVI.
        "-avi_codec=1", "-avi_quality=85", "-avi_old", "-avi_old4", "-avi_safe_width",
        "-auto_fps", "-fps_rate=30", "-fps_scale=1",

        // GIF.
        "-gif_quant_type=1", "-gif_border_tol=5", "-gif_trns_pxl_tol=5", "-gif_col_tbl_depth=7",
        "-gif_lossy_comp=5",

        // Animation.
        "-anim_fwd", "-anim_rev", "-anim_all_rep=2", "-anim_fwd_lf_rep=3", "-anim_rev_lf_rep=3",
        "-no_anim_rev_last_rep", "-anim_all_rep_always",

        // Frame range.
        "-sf=10", "-f=100", "-ifd=2", "-ctrf",

        // Input.
        "-mono", "-colour", "-db", "-pb", "-db_type=1", "-db=2", "-bin=2", "-bin_sum", "-bin_average",
        "-ser_bpp=8", "-ser_bpp=9", "-ser_bpp=10", "-ser_bpp=11", "-ser_bpp=12", "-ser_bpp=13",
        "-ser_bpp=14", "-ser_bpp=15", "-ser_bpp=16",
        "-raw_hpf", "-raw_db=1", "-raw_ocs=2", "-raw_hl=1", "-y8", "-wjpos_dtmask=YYYYMMDD_hhmmss",
        "-wjpos_lt2ut", "-wjpos_ipfps=60", "-wjpos_time=0",

        // Processing.
        "-median", "-convmono=6", "-input_gain=1.5", "-gamma=2.2", "-hs=80", "-hs_bp", "-hsrgb", "-oe=20",
        "-flip_x", "-flip_y", "-rotate90", "-rotate180", "-rotate270",
        "-chk", "-ms=15", "-mp=40", "-ctr", "-centre", "-shdw=3", "-cx=450", "-cy=450", "-ox=-12", "-oy=7",
        "-en_surface_feature_tracking", "-en_ss", "-afb_x=150", "-afb_y=150", "-afb_width=128",
        "-afb_height=128", "-freject",
        "-aoi", "-aoi_x=10", "-aoi_y=10", "-aoi_width=300", "-aoi_height=300", "-aoi_reject", "-aoi_crop",
        "-resize_en", "-resize_width=500", "-resize_height=-1",

        // Quality.
        "-q=100", "-qpcnt=25", "-q", "-qold", "-qhisto", "-qtotal", "-qw", "-qmin=3", "-qmax=5", "-qinc=1",

        // Calibration.
        "-dark", "-save_dark", "-hot_pixel=90", "-dark_bayer", "-flat", "-save_flat", "-flat_bayer",
        "-flat_dark", "-save_flat_dark",
        @"-dark_tmp=C:\t\dark.tmp", @"-dark_pgm=C:\t\dark.pgm", @"-flat_tmp=C:\t\flat.tmp",
        @"-flat_dark_tmp=C:\t\fd.tmp",

        // Misc.
        "-join", "-gui", "-test_run", "-sdp", "-sr", "-no_ram_buff", @"-temp_dir=C:\t", "-debugavi",
        "-check", "-help", "-median",
    ];

    private static PippOptions KitchenSink() => new()
    {
        Gui = true,
        TestRun = true,
        ShowDetectedPixels = true,
        SaveRejectedFrames = true,
        NoRamBuffers = true,
        CheckOnly = true,
        DebugAvi = true,
        Join = true,
        DarkSubtraction = true,
        SaveMasterDark = true,
        HotPixelThreshold = 90,
        DarkHasBayer = true,
        FlatCalibration = true,
        SaveMasterFlat = true,
        FlatHasBayer = true,
        FlatDarkSubtraction = true,
        SaveMasterFlatDark = true,
        RawHotPixelFilter = true,
        RawDebayerAlgorithm = PippRawDebayerAlgorithm.Vng,
        RawColourSpace = PippRawColourSpace.AdobeRgb1998,
        RawHighlightRecovery = 5,
        TrueY800 = true,
        WinJuposDateTimeMask = "YYYYMMDD_hhmmss",
        WinJuposLocalToUtc = true,
        WinJuposInputFps = 30,
        SerBitDepth = 12,
        StartFrame = 10,
        FrameCount = 101,
        FramesFromCentre = true,
        InputFrameDrop = 2,
        ColourMode = PippColourMode.Colour,
        Debayer = true,
        ProtectBayer = true,
        DebayerAlgorithm = PippDebayerAlgorithm.HqLinear,
        DebayerPattern = PippDebayerPattern.Rggb,
        Binning = 2,
        BinningCalculation = PippBinningCalculation.Sum,
        InputGain = 1.5,
        Gamma = 2.2,
        MedianFilter = true,
        ConvertToMono = 6,
        HistogramStretch = 80,
        HistogramStretchNoBlackPoint = true,
        HistogramStretchRgb = true,
        OverexposureThreshold = 20,
        FlipX = true,
        FlipY = true,
        Rotation = PippRotation.Rotate270,
        CheckForPlanet = true,
        MinPlanetSize = 15,
        MinPixelValue = 40,
        Centre = true,
        ShadowEdge = 3,
        CropWidth = 450,
        CropHeight = 450,
        OffsetX = -12,
        OffsetY = 7,
        SurfaceFeatureTracking = true,
        EnableStabilisation = true,
        AnchorFeatureX = 150,
        AnchorFeatureY = 150,
        AnchorFeatureWidth = 128,
        AnchorFeatureHeight = 128,
        AnchorFeatureReject = true,
        AreaOfInterest = true,
        AreaOfInterestX = 10,
        AreaOfInterestY = 10,
        AreaOfInterestWidth = 300,
        AreaOfInterestHeight = 300,
        AreaOfInterestReject = true,
        AreaOfInterestCrop = true,
        ResizeFrames = true,
        ResizeWidth = 500,
        ResizeHeight = -1,
        AnimateForward = true,
        RepeatForwardLast = 3,
        AnimateReverse = true,
        RepeatReverseLast = 3,
        RepeatAll = 2,
        NoReverseInLastRepeat = true,
        RepeatAllForever = true,
        QualityFrameCount = 100,
        QualityAlgorithm = PippQualityAlgorithm.Histogram,
        QualityMinDownsample = 2,
        QualityMaxDownsample = 6,
        QualityIncrement = 2,
        QualityWeighting = true,
        NoRenumber = true,
        OutputSubdirectory = "run1",
        OutputDirectory = @"C:\out",
        OutputPrefix = "p_",
        OutputSuffix = "_s",
        Formats = PippOutputFormats.Avi | PippOutputFormats.Ser | PippOutputFormats.Bmp |
                  PippOutputFormats.Tiff | PippOutputFormats.Png | PippOutputFormats.Fits |
                  PippOutputFormats.Gif,
        AviCodec = PippAviCodec.Fmp4,
        AviQuality = 85,
        AviOldFormat = PippAviOldFormat.Old4,
        AviSafeWidth = true,
        AutoFps = true,
        FpsRate = 30,
        FpsScale = 1,
        GifQuantisation = PippGifQuantisation.MedianCut,
        GifBorderTolerance = -1,
        GifTransparentPixelTolerance = 5,
        GifColorTableDepth = 7,
        GifLossyCompression = 5,
        KeepImageFilenames = true,
        NoLeadingZeros = true,
        IncludeQualityValue = true,
        SplitRgb = true,
        OutputFrameDrop = 10,
        WinJuposTime = PippWinJuposTimeMode.Earliest,
        TempDirectory = @"C:\tmp",
        MasterDarkTmpFile = @"C:\tmp\dark.tmp",
        MasterDarkPgmFile = @"C:\tmp\dark.pgm",
        MasterFlatTmpFile = @"C:\tmp\flat.tmp",
        MasterFlatDarkTmpFile = @"C:\tmp\fd.tmp",
    };

    /// <summary>
    /// Variants that reach the mutually exclusive branches of the vocabulary (format alternatives,
    /// rotation, quality algorithm and frame-selection mode).
    /// </summary>
    private static PippOptions[] Variants() =>
    [
        KitchenSink(),
        KitchenSink() with
        {
            ColourMode = PippColourMode.Monochrome,
            AviOldFormat = PippAviOldFormat.Old,
            BinningCalculation = PippBinningCalculation.Average,
            QualityFrameCount = null,
            QualityAllFrames = true,
        },
        KitchenSink() with
        {
            QualityFrameCount = null,
            QualityAllFrames = false,
            QualityPercent = 25,
        },
        KitchenSink() with { QualityAlgorithm = PippQualityAlgorithm.Ninox },
        KitchenSink() with { QualityAlgorithm = PippQualityAlgorithm.TotalPixel },
        KitchenSink() with { Rotation = PippRotation.Rotate90 },
        KitchenSink() with { Rotation = PippRotation.Rotate180 },
    ];

    // ---------------------------------------------------------------------------------------------
    // Defaults
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Defaults_MatchDocumentedGuiDefaults()
    {
        var options = new PippOptions();

        Assert.False(options.Gui);
        Assert.False(options.TestRun);
        Assert.Equal(PippColourMode.Auto, options.ColourMode);
        Assert.Equal(PippRotation.None, options.Rotation);
        Assert.Equal(PippQualityAlgorithm.Default, options.QualityAlgorithm);
        Assert.Equal(1.0, options.InputGain);
        Assert.Equal(1.0, options.Gamma);
        Assert.Equal(1, options.RawHighlightRecovery);
        Assert.Equal(PippOutputFormats.Avi, options.Formats);
        Assert.Equal(PippAviCodec.Dib, options.AviCodec);
        Assert.Equal(10, options.AviQuality);
        Assert.Equal(PippAviOldFormat.None, options.AviOldFormat);
        Assert.Equal(60, options.FpsRate);
        Assert.Equal(1, options.FpsScale);
        Assert.Equal(PippGifQuantisation.NeuralNet, options.GifQuantisation);
        Assert.Equal(5, options.GifBorderTolerance);
        Assert.Equal(5, options.GifTransparentPixelTolerance);
        Assert.Equal(8, options.GifColorTableDepth);
        Assert.Equal(0, options.GifLossyCompression);
        Assert.Equal(string.Empty, options.OutputSubdirectory);
        Assert.Null(options.OutputDirectory);
        Assert.False(options.NoRenumber);
        Assert.Null(options.SerBitDepth);
        Assert.Null(options.QualityFrameCount);
        Assert.Null(options.QualityPercent);
    }

    [Fact]
    public void Build_OfDefaultOptions_EmitsTheGuiDefaultCommandLine()
    {
        var built = CommandLineBuilder.Build(new PippOptions());

        Assert.Equal(
            ["-raw_hl=1", "-osubdir=", "-avi", "-avi_codec=0", "-avi_quality=10", "-fps_rate=60", "-fps_scale=1"],
            built);
    }

    [Fact]
    public void DefaultOptions_RoundTrip()
    {
        var original = new PippOptions();

        var result = CommandLineParser.Parse(CommandLineBuilder.Build(original));

        Assert.True(result.Success);
        Assert.Equal(original, result.Options);
    }

    // ---------------------------------------------------------------------------------------------
    // Coverage: every switch parses, appears in help, and is emitted by the builder
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EverySwitch_ParsesWithoutError()
    {
        var failures = new List<string>();

        foreach (var token in AllSwitches.Distinct(StringComparer.Ordinal))
        {
            var result = CommandLineParser.Parse([token]);
            if (!result.Success)
            {
                failures.Add($"{token}: {string.Join(" | ", result.Errors)}");
            }

            if (token == "-help" && !result.WantsHelp)
            {
                failures.Add("-help: WantsHelp was not set");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EverySwitch_IsDocumentedInTheHelpText()
    {
        var missing = new List<string>();

        foreach (var token in AllSwitches.Distinct(StringComparer.Ordinal))
        {
            var name = token.Split('=')[0];
            var needle = token.Contains('=') ? name + "=" : name;
            if (!PippHelpText.Text.Contains(needle, StringComparison.Ordinal))
            {
                missing.Add(needle);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EverySwitch_IsEmittedByTheBuilder()
    {
        // Some switches are alternative spellings or mutually exclusive states, so several option
        // sets are needed to exercise the whole vocabulary.
        var variants = Variants();

        var built = variants.SelectMany(CommandLineBuilder.Build).ToList();
        var missing = new List<string>();

        foreach (var token in AllSwitches.Distinct(StringComparer.Ordinal))
        {
            // -help is a parser directive, not an option, so the builder never emits it.
            if (token == "-help")
            {
                continue;
            }

            // -centre is documented as an alias of -ctr, which is the GUI's canonical spelling.
            var expected = token == "-centre" ? "-ctr" : token;
            var name = expected.Split('=')[0];
            var found = expected.Contains('=')
                ? built.Any(t => t.StartsWith(name + "=", StringComparison.Ordinal))
                : built.Contains(expected, StringComparer.Ordinal);

            if (!found)
            {
                missing.Add(expected);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void SerBitDepth_AllNineValues_MapToTheSameProperty()
    {
        for (var bits = 8; bits <= 16; bits++)
        {
            var result = CommandLineParser.Parse([$"-ser_bpp={bits}"]);

            Assert.True(result.Success);
            Assert.Equal(bits, result.Options.SerBitDepth);
            Assert.Contains($"-ser_bpp={bits}", CommandLineBuilder.Build(result.Options));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Round-trip
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void KitchenSink_RoundTripsThroughBuildAndParse()
    {
        var original = KitchenSink();

        var built = CommandLineBuilder.Build(original);
        var result = CommandLineParser.Parse(built);

        Assert.True(result.Success);
        Assert.Equal(original, result.Options);

        // Building the parsed options must reproduce the command line byte for byte.
        Assert.Equal(built, CommandLineBuilder.Build(result.Options));
    }

    [Fact]
    public void EveryVariant_RoundTripsThroughBuildAndParse()
    {
        foreach (var variant in Variants())
        {
            var built = CommandLineBuilder.Build(variant);
            var parsed = CommandLineParser.Parse(built);

            Assert.True(parsed.Success, string.Join(" | ", parsed.Errors));
            Assert.Equal(variant, parsed.Options);
            Assert.Equal(built, CommandLineBuilder.Build(parsed.Options));
        }
    }

    [Fact]
    public void AviTuningWithoutAviOutput_StillRoundTrips()
    {
        var original = new PippOptions
        {
            Formats = PippOutputFormats.Png,
            AviCodec = PippAviCodec.Fmp4,
            AviQuality = 85,
            AviOldFormat = PippAviOldFormat.Old,
            AviSafeWidth = true,
        };

        var built = CommandLineBuilder.Build(original);
        var parsed = CommandLineParser.Parse(built);

        Assert.True(parsed.Success);
        Assert.Equal(original, parsed.Options);
    }

    [Fact]
    public void BuiltCommandLine_ContainsNoDuplicateSwitches()
    {
        var built = CommandLineBuilder.Build(KitchenSink());

        Assert.Equal(built.Length, built.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void KitchenSink_EmitsSwitchesInGuiOrder()
    {
        var built = CommandLineBuilder.Build(KitchenSink());

        var index = (string token) => Array.IndexOf(built, token);

        Assert.True(index("-gui") < index("-test_run"));
        Assert.True(index("-test_run") < index("-sdp"));
        Assert.True(index("-sdp") < index("-join"));
        Assert.True(index("-join") < index("-dark"));
        Assert.True(index("-dark") < index("-flat"));
        Assert.True(index("-flat") < index("-flat_dark"));
        Assert.True(index("-flat_dark") < index("-raw_hl=5"));
        Assert.True(index("-raw_hl=5") < index("-sf=10"));
        Assert.Equal(index("-sf=10") + 1, index("-ctrf"));
        Assert.Equal(index("-ctrf") + 1, index("-f=101"));
        Assert.True(index("-raw_hl=5") < index("-bin=2"));
        Assert.True(index("-bin=2") < index("-input_gain=1.5"));
        Assert.True(index("-input_gain=1.5") < index("-median"));
        Assert.True(index("-rotate270") < index("-chk"));
        Assert.True(index("-chk") < index("-cx=450"));
        Assert.True(index("-cx=450") < index("-en_surface_feature_tracking"));
        Assert.True(index("-en_surface_feature_tracking") < index("-resize_en"));
        Assert.True(index("-resize_en") < index("-anim_fwd"));
        Assert.True(index("-anim_fwd") < index("-q=100"));
        Assert.True(index("-q=100") < index("-qhisto"));
        Assert.True(index("-qhisto") < index("-qmin=2"));
        Assert.True(index("-qmin=2") < index("-osubdir=run1"));
        Assert.True(index("-osubdir=run1") < index("-od=C:\\out"));
        Assert.True(index("-od=C:\\out") < index("-avi"));
        Assert.True(index("-gif") < index("-gif_quant_type=1"));
        Assert.True(index("-gif_quant_type=1") < index("-keepimgname"));
        Assert.True(index("-keepimgname") < index("-nr"));
        Assert.True(index("-nr") < index("-drop=10"));
        Assert.True(index("-drop=10") < index("-wjpos_time=1"));
        Assert.True(index("-wjpos_time=1") < index("-sr"));
        Assert.True(index("-sr") < index("-no_ram_buff"));
        Assert.True(index("-no_ram_buff") < index("-temp_dir=C:\\tmp"));
        Assert.True(index("-temp_dir=C:\\tmp") < index("-debugavi"));
        Assert.True(index("-debugavi") < index("-check"));
    }

    // ---------------------------------------------------------------------------------------------
    // Bare flags versus valued switches
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BareQ_SortsAllFrames_AndValuedQ_KeepsTheBestN()
    {
        var all = CommandLineParser.Parse(["-q"]);
        Assert.True(all.Options.QualityAllFrames);
        Assert.Null(all.Options.QualityFrameCount);
        Assert.Contains("-q", CommandLineBuilder.Build(all.Options));

        var best = CommandLineParser.Parse(["-q=250"]);
        Assert.False(best.Options.QualityAllFrames);
        Assert.Equal(250, best.Options.QualityFrameCount);
        Assert.Contains("-q=250", CommandLineBuilder.Build(best.Options));
    }

    [Fact]
    public void BareDb_EnablesDebayering_AndValuedDb_SelectsThePattern()
    {
        var bare = CommandLineParser.Parse(["-db"]);
        Assert.True(bare.Options.Debayer);
        Assert.Null(bare.Options.DebayerPattern);

        var pattern = CommandLineParser.Parse(["-db=2"]);
        Assert.False(pattern.Options.Debayer);
        Assert.Equal(PippDebayerPattern.Rggb, pattern.Options.DebayerPattern);

        var both = CommandLineParser.Parse(["-db", "-db=3"]);
        Assert.True(both.Options.Debayer);
        Assert.Equal(PippDebayerPattern.Grbg, both.Options.DebayerPattern);
        Assert.Equal(["-db", "-db=3"], CommandLineBuilder.Build(both.Options).Where(t => t.StartsWith("-db", StringComparison.Ordinal)));
    }

    [Fact]
    public void CentreAlias_MapsToTheCanonicalCtrSwitch()
    {
        var result = CommandLineParser.Parse(["-centre"]);

        Assert.True(result.Success);
        Assert.True(result.Options.Centre);
        Assert.Contains("-ctr", CommandLineBuilder.Build(result.Options));
        Assert.DoesNotContain("-centre", CommandLineBuilder.Build(result.Options));
    }

    [Fact]
    public void HistogramStretchPercentSpelling_MapsToTheSameFieldAsTheIntegerForm()
    {
        // The engine's parser converts -hs=xx% (and -hs=xxp) to xx*255/100 in the same option field,
        // so all three spellings below end up as the same setting.
        foreach (var token in new[] { "-hs=80%", "-hs=80p", "-hs=204" })
        {
            var result = CommandLineParser.Parse([token]);

            Assert.True(result.Success, token);
            Assert.Equal(204, result.Options.HistogramStretch);
        }

        Assert.False(CommandLineParser.Parse(["-hs=101%"]).Success);
        Assert.False(CommandLineParser.Parse(["-hs=0%"]).Success);
        Assert.False(CommandLineParser.Parse(["-hs=%"]).Success);
    }

    [Fact]
    public void FormatSwitches_ReplaceTheDefaultFormat_AndCombine()
    {
        var single = CommandLineParser.Parse(["-png"]);
        Assert.Equal(PippOutputFormats.Png, single.Options.Formats);

        var combined = CommandLineParser.Parse(["-png", "-fits"]);
        Assert.Equal(PippOutputFormats.Png | PippOutputFormats.Fits, combined.Options.Formats);
    }

    // ---------------------------------------------------------------------------------------------
    // Errors
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void UnknownSwitch_IsReportedAsAnError_NotThrown()
    {
        var result = CommandLineParser.Parse(["-bogus=1", "-png"]);

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Unknown switch", error);
        Assert.Contains("-bogus", error);

        // The rest of the command line is still applied.
        Assert.Equal(PippOutputFormats.Png, result.Options.Formats);
    }

    [Fact]
    public void NonSwitchArgument_IsTreatedAsAnInputFile()
    {
        // PIPP's usage is "pipp [options] input_file(s)", so positionals are inputs, not errors.
        var result = CommandLineParser.Parse(["frames.ser", "-tif"]);

        Assert.True(result.Success);
        Assert.Equal(["frames.ser"], result.Inputs);
        Assert.Equal(PippOutputFormats.Tiff, result.Options.Formats);
    }

    [Theory]
    [InlineData("-od")]
    [InlineData("-avi_quality")]
    [InlineData("-bin")]
    [InlineData("-q=")]
    public void ValuedSwitchWithoutValue_IsReported(string token)
    {
        var result = CommandLineParser.Parse([token]);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("-median=1")]
    [InlineData("-flip_x=yes")]
    [InlineData("-aoi_crop=1")]
    public void FlagWithValue_IsReported(string token)
    {
        var result = CommandLineParser.Parse([token]);

        Assert.False(result.Success);
        Assert.Contains("does not take a value", Assert.Single(result.Errors));
        Assert.False(result.Options.MedianFilter);
        Assert.False(result.Options.FlipX);
        Assert.False(result.Options.AreaOfInterestCrop);
    }

    [Theory]
    [InlineData("-bin=0")]
    [InlineData("-bin=9")]
    [InlineData("-ser_bpp=7")]
    [InlineData("-ser_bpp=17")]
    [InlineData("-convmono=7")]
    [InlineData("-avi_quality=0")]
    [InlineData("-avi_quality=101")]
    [InlineData("-qpcnt=101")]
    [InlineData("-gif_quant_type=2")]
    [InlineData("-raw_ocs=7")]
    [InlineData("-raw_db=4")]
    [InlineData("-shdw=9")]
    [InlineData("-fps_scale=0")]
    [InlineData("-gif_col_tbl_depth=9")]
    public void OutOfRangeValue_IsReported(string token)
    {
        var result = CommandLineParser.Parse([token]);

        Assert.False(result.Success);
        Assert.Contains("outside the allowed range", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("-bin=abc")]
    [InlineData("-q=best")]
    [InlineData("-input_gain=lots")]
    [InlineData("-gamma=")]
    public void NonNumericValue_IsReported(string token)
    {
        var result = CommandLineParser.Parse([token]);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("-input_gain=0")]
    [InlineData("-gamma=-1")]
    [InlineData("-input_gain=NaN")]
    public void NonPositiveGainOrGamma_IsReported(string token)
    {
        var result = CommandLineParser.Parse([token]);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    // ---------------------------------------------------------------------------------------------
    // Numeric edge values
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("-bin=1")]
    [InlineData("-bin=8")]
    [InlineData("-ser_bpp=8")]
    [InlineData("-ser_bpp=16")]
    [InlineData("-convmono=0")]
    [InlineData("-convmono=6")]
    [InlineData("-gif_border_tol=-1")]
    [InlineData("-gif_col_tbl_depth=6")]
    [InlineData("-gif_lossy_comp=1")]
    [InlineData("-resize_height=-1")]
    [InlineData("-ox=-5000")]
    [InlineData("-oy=5000")]
    [InlineData("-q=0")]
    [InlineData("-f=0")]
    [InlineData("-hot_pixel=0")]
    [InlineData("-avi_quality=1")]
    [InlineData("-avi_quality=100")]
    [InlineData("-input_gain=0.5")]
    [InlineData("-gamma=2.2")]
    public void EdgeValues_AreAcceptedAndRoundTrip(string token)
    {
        var result = CommandLineParser.Parse([token]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Contains(token, CommandLineBuilder.Build(result.Options));
    }

    [Fact]
    public void NegativeOffsets_RoundTrip()
    {
        var result = CommandLineParser.Parse(["-ox=-12", "-oy=-7"]);

        Assert.True(result.Success);
        Assert.Equal(-12, result.Options.OffsetX);
        Assert.Equal(-7, result.Options.OffsetY);
        Assert.Contains("-ox=-12", CommandLineBuilder.Build(result.Options));
        Assert.Contains("-oy=-7", CommandLineBuilder.Build(result.Options));
    }

    [Fact]
    public void ResizeHeightMinusOne_MeansKeepAspectRatio()
    {
        var result = CommandLineParser.Parse(["-resize_en", "-resize_width=500", "-resize_height=-1"]);

        Assert.True(result.Success);
        Assert.True(result.Options.ResizeFrames);
        Assert.Equal(500, result.Options.ResizeWidth);
        Assert.Equal(-1, result.Options.ResizeHeight);
        Assert.Equal(result.Options, CommandLineParser.Parse(CommandLineBuilder.Build(result.Options)).Options);
    }

    // ---------------------------------------------------------------------------------------------
    // Parser mechanics
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RepeatedSwitch_LastValueWins()
    {
        var result = CommandLineParser.Parse(["-ser_bpp=8", "-ser_bpp=16"]);

        Assert.True(result.Success);
        Assert.Equal(16, result.Options.SerBitDepth);
    }

    [Fact]
    public void RepeatedFlag_IsIdempotent()
    {
        var result = CommandLineParser.Parse(["-nr", "-nr"]);

        Assert.True(result.Success);
        Assert.True(result.Options.NoRenumber);
        Assert.Single(CommandLineBuilder.Build(result.Options), t => t == "-nr");
    }

    [Fact]
    public void SwitchNames_AreCaseInsensitive()
    {
        var result = CommandLineParser.Parse(["-BIN=2", "-MEDIAN"]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Options.Binning);
        Assert.True(result.Options.MedianFilter);
    }

    [Fact]
    public void EmptyAndWhitespaceArguments_AreIgnored()
    {
        var result = CommandLineParser.Parse(["", "   ", "-png"]);

        Assert.True(result.Success);
        Assert.Equal(PippOutputFormats.Png, result.Options.Formats);
    }

    [Fact]
    public void EmptyOutputSubdirectory_IsPreserved()
    {
        var result = CommandLineParser.Parse(["-osubdir="]);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Options.OutputSubdirectory);
        Assert.Contains("-osubdir=", CommandLineBuilder.Build(result.Options));
    }

    [Fact]
    public void HelpSwitch_RequestsHelpWithoutErrors()
    {
        var result = CommandLineParser.Parse(["-help"]);

        Assert.True(result.Success);
        Assert.True(result.WantsHelp);
        Assert.DoesNotContain("-help", CommandLineBuilder.Build(result.Options));
    }

    [Fact]
    public void NumbersAreParsedWithTheInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = CommandLineParser.Parse(["-input_gain=1.5", "-gamma=2.25"]);

            Assert.True(result.Success);
            Assert.Equal(1.5, result.Options.InputGain);
            Assert.Equal(2.25, result.Options.Gamma);

            var built = CommandLineBuilder.Build(result.Options);
            Assert.Contains("-input_gain=1.5", built);
            Assert.Contains("-gamma=2.25", built);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => CommandLineParser.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => CommandLineBuilder.Build(null!));
    }

    [Fact]
    public void ParseResult_DefaultsAreUsable()
    {
        var result = CommandLineParser.Parse([]);

        Assert.True(result.Success);
        Assert.False(result.WantsHelp);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Options);
    }
}
