using System.Globalization;

namespace Pipp.Core.Cli;

/// <summary>
/// Serialises a <see cref="PippOptions"/> instance back into the <c>pipp_core.exe</c> switch list.
/// </summary>
/// <remarks>
/// <para>
/// The order of the emitted switches follows the GUI's <c>Form1.generate_cmdline_options</c> pipeline:
/// global switches, source/calibration groups, input, processing, animation, quality, output, and
/// finally the menu-strip and engine-path switches the GUI appends right before handing the list to
/// the engine.
/// </para>
/// <para>
/// Format-specific switches (AVI codec, GIF quantisation, frame rate) are emitted when the matching
/// format is selected, exactly like the GUI; they are also emitted when their value differs from the
/// GUI default so that a parsed command line always round-trips, whatever format it names. The frame
/// naming switches (<c>-keepimgname</c>, <c>-nr</c>, <c>-nlz</c>, <c>-incqv</c>, <c>-rgb</c>) are emitted
/// whenever they are set, because the engine ignores them for formats that do not use per-frame names.
/// </para>
/// <para>
/// <see cref="PippOutputFormats.None"/> cannot be expressed on a command line: with no format switch the
/// engine falls back to its own default, which parses back as AVI.
/// </para>
/// <para>
/// Two switches are shared between GUI tabs and are emitted only once: <c>-nr</c> (frame renumbering
/// and quality reordering) in the output group, and <c>-auto_fps</c> / <c>-fps_rate</c> /
/// <c>-fps_scale</c> in the AVI frame-rate position even for GIF-only output.
/// </para>
/// </remarks>
public static class CommandLineBuilder
{
    private static readonly PippOptions Defaults = new();

    /// <summary>Builds the command-line tokens for <paramref name="options"/>.</summary>
    /// <param name="options">The options to serialise.</param>
    /// <returns>The switch tokens, in GUI emission order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static string[] Build(PippOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string>(160);
        var avi = (options.Formats & PippOutputFormats.Avi) != 0;
        var gif = (options.Formats & PippOutputFormats.Gif) != 0;

        // ---- global / mode ---------------------------------------------------------------------
        if (options.Gui)
        {
            args.Add("-gui");
        }

        if (options.TestRun)
        {
            args.Add("-test_run");
        }

        if (options.ShowDetectedPixels)
        {
            args.Add("-sdp");
        }

        if (options.Join)
        {
            args.Add("-join");
        }

        // ---- dark frames -----------------------------------------------------------------------
        if (options.DarkSubtraction)
        {
            args.Add("-dark");
        }

        if (options.SaveMasterDark)
        {
            args.Add("-save_dark");
        }

        AddInt(args, "-hot_pixel", options.HotPixelThreshold);

        if (options.DarkHasBayer)
        {
            args.Add("-dark_bayer");
        }

        // ---- flat frames -----------------------------------------------------------------------
        if (options.FlatCalibration)
        {
            args.Add("-flat");
        }

        if (options.SaveMasterFlat)
        {
            args.Add("-save_flat");
        }

        if (options.FlatHasBayer)
        {
            args.Add("-flat_bayer");
        }

        // ---- flat dark frames ------------------------------------------------------------------
        if (options.FlatDarkSubtraction)
        {
            args.Add("-flat_dark");
        }

        if (options.SaveMasterFlatDark)
        {
            args.Add("-save_flat_dark");
        }

        // ---- input options ---------------------------------------------------------------------
        if (options.RawHotPixelFilter)
        {
            args.Add("-raw_hpf");
        }

        AddEnum(args, "-raw_db", options.RawDebayerAlgorithm);
        AddEnum(args, "-raw_ocs", options.RawColourSpace);

        // The GUI emits -raw_hl unconditionally.
        AddInt(args, "-raw_hl", options.RawHighlightRecovery);

        if (options.TrueY800)
        {
            args.Add("-y8");
        }

        if (options.WinJuposDateTimeMask is not null)
        {
            args.Add("-wjpos_dtmask=" + options.WinJuposDateTimeMask);
        }

        if (options.WinJuposLocalToUtc)
        {
            args.Add("-wjpos_lt2ut");
        }

        AddInt(args, "-wjpos_ipfps", options.WinJuposInputFps);
        AddInt(args, "-ser_bpp", options.SerBitDepth);
        AddInt(args, "-sf", options.StartFrame);

        if (options.FramesFromCentre)
        {
            args.Add("-ctrf");
        }

        AddInt(args, "-f", options.FrameCount);

        if (options.InputFrameDrop > 0)
        {
            args.Add(Int("-ifd", options.InputFrameDrop));
        }

        if (options.ColourMode == PippColourMode.Monochrome)
        {
            args.Add("-mono");
        }
        else if (options.ColourMode == PippColourMode.Colour)
        {
            args.Add("-colour");
        }

        if (options.Debayer)
        {
            args.Add("-db");
        }

        if (options.ProtectBayer)
        {
            args.Add("-pb");
        }

        AddEnum(args, "-db_type", options.DebayerAlgorithm);
        AddEnum(args, "-db", options.DebayerPattern);
        AddInt(args, "-bin", options.Binning);

        if (options.BinningCalculation == PippBinningCalculation.Sum)
        {
            args.Add("-bin_sum");
        }
        else if (options.BinningCalculation == PippBinningCalculation.Average)
        {
            args.Add("-bin_average");
        }

        // ---- processing options ----------------------------------------------------------------
        if (options.InputGain != Defaults.InputGain)
        {
            args.Add(Double("-input_gain", options.InputGain));
        }

        if (options.Gamma != Defaults.Gamma)
        {
            args.Add(Double("-gamma", options.Gamma));
        }

        if (options.MedianFilter)
        {
            args.Add("-median");
        }

        AddInt(args, "-convmono", options.ConvertToMono);
        AddInt(args, "-hs", options.HistogramStretch);

        if (options.HistogramStretchNoBlackPoint)
        {
            args.Add("-hs_bp");
        }

        if (options.HistogramStretchRgb)
        {
            args.Add("-hsrgb");
        }

        AddInt(args, "-oe", options.OverexposureThreshold);

        if (options.FlipX)
        {
            args.Add("-flip_x");
        }

        if (options.FlipY)
        {
            args.Add("-flip_y");
        }

        switch (options.Rotation)
        {
            case PippRotation.Rotate90:
                args.Add("-rotate90");
                break;
            case PippRotation.Rotate270:
                args.Add("-rotate270");
                break;
            case PippRotation.Rotate180:
                args.Add("-rotate180");
                break;
        }

        if (options.CheckForPlanet)
        {
            args.Add("-chk");
        }

        AddInt(args, "-ms", options.MinPlanetSize);
        AddInt(args, "-mp", options.MinPixelValue);

        if (options.Centre)
        {
            args.Add("-ctr");
        }

        AddInt(args, "-shdw", options.ShadowEdge);
        AddInt(args, "-cx", options.CropWidth);
        AddInt(args, "-cy", options.CropHeight);
        AddInt(args, "-ox", options.OffsetX);
        AddInt(args, "-oy", options.OffsetY);

        if (options.SurfaceFeatureTracking)
        {
            args.Add("-en_surface_feature_tracking");
        }

        if (options.EnableStabilisation)
        {
            args.Add("-en_ss");
        }

        AddInt(args, "-afb_x", options.AnchorFeatureX);
        AddInt(args, "-afb_y", options.AnchorFeatureY);
        AddInt(args, "-afb_width", options.AnchorFeatureWidth);
        AddInt(args, "-afb_height", options.AnchorFeatureHeight);

        if (options.AnchorFeatureReject)
        {
            args.Add("-freject");
        }

        if (options.AreaOfInterest)
        {
            args.Add("-aoi");
        }

        AddInt(args, "-aoi_x", options.AreaOfInterestX);
        AddInt(args, "-aoi_y", options.AreaOfInterestY);
        AddInt(args, "-aoi_width", options.AreaOfInterestWidth);
        AddInt(args, "-aoi_height", options.AreaOfInterestHeight);

        if (options.AreaOfInterestReject)
        {
            args.Add("-aoi_reject");
        }

        if (options.AreaOfInterestCrop)
        {
            args.Add("-aoi_crop");
        }

        if (options.ResizeFrames)
        {
            args.Add("-resize_en");
        }

        AddInt(args, "-resize_width", options.ResizeWidth);
        AddInt(args, "-resize_height", options.ResizeHeight);

        // ---- animation options -----------------------------------------------------------------
        if (options.AnimateForward)
        {
            args.Add("-anim_fwd");
        }

        AddInt(args, "-anim_fwd_lf_rep", options.RepeatForwardLast);

        if (options.AnimateReverse)
        {
            args.Add("-anim_rev");
        }

        AddInt(args, "-anim_rev_lf_rep", options.RepeatReverseLast);
        AddInt(args, "-anim_all_rep", options.RepeatAll);

        if (options.NoReverseInLastRepeat)
        {
            args.Add("-no_anim_rev_last_rep");
        }

        // ---- quality options -------------------------------------------------------------------
        if (options.QualityFrameCount.HasValue)
        {
            AddInt(args, "-q", options.QualityFrameCount);
        }
        else if (options.QualityPercent.HasValue)
        {
            AddInt(args, "-qpcnt", options.QualityPercent);
        }
        else if (options.QualityAllFrames)
        {
            args.Add("-q");
        }

        switch (options.QualityAlgorithm)
        {
            case PippQualityAlgorithm.Ninox:
                args.Add("-qold");
                break;
            case PippQualityAlgorithm.Histogram:
                args.Add("-qhisto");
                break;
            case PippQualityAlgorithm.TotalPixel:
                args.Add("-qtotal");
                break;
        }

        AddInt(args, "-qmin", options.QualityMinDownsample);
        AddInt(args, "-qmax", options.QualityMaxDownsample);
        AddInt(args, "-qinc", options.QualityIncrement);

        if (options.QualityWeighting)
        {
            args.Add("-qw");
        }

        // ---- output options --------------------------------------------------------------------
        args.Add("-osubdir=" + options.OutputSubdirectory);

        if (options.OutputDirectory is not null)
        {
            args.Add("-od=" + options.OutputDirectory);
        }

        if (options.OutputPrefix is not null)
        {
            args.Add("-op=" + options.OutputPrefix);
        }

        if (options.OutputSuffix is not null)
        {
            args.Add("-os=" + options.OutputSuffix);
        }

        if ((options.Formats & PippOutputFormats.Avi) != 0)
        {
            args.Add("-avi");
        }

        if ((options.Formats & PippOutputFormats.Ser) != 0)
        {
            args.Add("-ser");
        }

        if ((options.Formats & PippOutputFormats.Bmp) != 0)
        {
            args.Add("-bmp");
        }

        if ((options.Formats & PippOutputFormats.Tiff) != 0)
        {
            args.Add("-tif");
        }

        if ((options.Formats & PippOutputFormats.Png) != 0)
        {
            args.Add("-png");
        }

        if ((options.Formats & PippOutputFormats.Fits) != 0)
        {
            args.Add("-fits");
        }

        if ((options.Formats & PippOutputFormats.Gif) != 0)
        {
            args.Add("-gif");
        }

        if (avi || options.AviCodec != Defaults.AviCodec)
        {
            AddInt(args, "-avi_codec", (int)options.AviCodec);
        }

        if (avi || options.AviQuality != Defaults.AviQuality)
        {
            AddInt(args, "-avi_quality", options.AviQuality);
        }

        if (options.AviOldFormat == PippAviOldFormat.Old4)
        {
            args.Add("-avi_old4");
        }
        else if (options.AviOldFormat == PippAviOldFormat.Old)
        {
            args.Add("-avi_old");
        }

        if (options.AviSafeWidth)
        {
            args.Add("-avi_safe_width");
        }

        if (gif && options.RepeatAllForever)
        {
            args.Add("-anim_all_rep_always");
        }
        else if (!gif && options.RepeatAllForever)
        {
            args.Add("-anim_all_rep_always");
        }

        if (options.AutoFps)
        {
            args.Add("-auto_fps");
        }

        if (avi || gif || options.FpsRate != Defaults.FpsRate)
        {
            AddInt(args, "-fps_rate", options.FpsRate);
        }

        if (avi || gif || options.FpsScale != Defaults.FpsScale)
        {
            AddInt(args, "-fps_scale", options.FpsScale);
        }

        if (gif || options.GifQuantisation != Defaults.GifQuantisation)
        {
            AddInt(args, "-gif_quant_type", (int)options.GifQuantisation);
        }

        if (gif || options.GifBorderTolerance != Defaults.GifBorderTolerance)
        {
            AddInt(args, "-gif_border_tol", options.GifBorderTolerance);
        }

        if (gif || options.GifTransparentPixelTolerance != Defaults.GifTransparentPixelTolerance)
        {
            AddInt(args, "-gif_trns_pxl_tol", options.GifTransparentPixelTolerance);
        }

        if (gif || options.GifColorTableDepth != Defaults.GifColorTableDepth)
        {
            AddInt(args, "-gif_col_tbl_depth", options.GifColorTableDepth);
        }

        if (gif || options.GifLossyCompression != Defaults.GifLossyCompression)
        {
            AddInt(args, "-gif_lossy_comp", options.GifLossyCompression);
        }

        if (options.KeepImageFilenames)
        {
            args.Add("-keepimgname");
        }

        if (options.NoRenumber)
        {
            args.Add("-nr");
        }

        if (options.NoLeadingZeros)
        {
            args.Add("-nlz");
        }

        if (options.IncludeQualityValue)
        {
            args.Add("-incqv");
        }

        if (options.SplitRgb)
        {
            args.Add("-rgb");
        }

        if (options.OutputFrameDrop > 0)
        {
            args.Add(Int("-drop", options.OutputFrameDrop));
        }

        AddEnum(args, "-wjpos_time", options.WinJuposTime);

        // ---- menu strip ------------------------------------------------------------------------
        if (options.SaveRejectedFrames)
        {
            args.Add("-sr");
        }

        if (options.NoRamBuffers)
        {
            args.Add("-no_ram_buff");
        }

        // ---- engine paths / debug (appended by the GUI just before the command line is sent) ----
        if (options.TempDirectory is not null)
        {
            args.Add("-temp_dir=" + options.TempDirectory);
        }

        if (options.MasterDarkTmpFile is not null)
        {
            args.Add("-dark_tmp=" + options.MasterDarkTmpFile);
        }

        if (options.MasterDarkPgmFile is not null)
        {
            args.Add("-dark_pgm=" + options.MasterDarkPgmFile);
        }

        if (options.MasterFlatDarkTmpFile is not null)
        {
            args.Add("-flat_dark_tmp=" + options.MasterFlatDarkTmpFile);
        }

        if (options.MasterFlatTmpFile is not null)
        {
            args.Add("-flat_tmp=" + options.MasterFlatTmpFile);
        }

        if (options.DebugAvi)
        {
            args.Add("-debugavi");
        }

        if (options.CheckOnly)
        {
            args.Add("-check");
        }

        return args.ToArray();
    }

    private static string Int(string name, int value) => name + "=" + value.ToString(CultureInfo.InvariantCulture);

    private static string Double(string name, double value) => name + "=" + value.ToString("R", CultureInfo.InvariantCulture);

    private static void AddInt(List<string> args, string name, int? value)
    {
        if (value.HasValue)
        {
            args.Add(Int(name, value.Value));
        }
    }

    private static void AddInt(List<string> args, string name, int value)
    {
        args.Add(Int(name, value));
    }

    private static void AddEnum<TEnum>(List<string> args, string name, TEnum? value)
        where TEnum : struct, Enum
    {
        if (value.HasValue)
        {
            args.Add(Int(name, Convert.ToInt32(value.Value, CultureInfo.InvariantCulture)));
        }
    }

    private static void AddEnum<TEnum>(List<string> args, string name, TEnum value)
        where TEnum : struct, Enum
    {
        args.Add(Int(name, Convert.ToInt32(value, CultureInfo.InvariantCulture)));
    }
}
