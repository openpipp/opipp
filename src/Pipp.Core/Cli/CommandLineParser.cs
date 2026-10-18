using System.Globalization;

namespace Pipp.Core.Cli;

/// <summary>Result of parsing a PIPP command line.</summary>
public sealed class ParseResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="options">The parsed options; never <see langword="null"/>.</param>
    /// <param name="errors">Human-readable problems found while parsing.</param>
    /// <param name="wantsHelp">Whether <c>-help</c> was present.</param>
    public ParseResult(PippOptions options, IReadOnlyList<string> errors, bool wantsHelp, IReadOnlyList<string>? inputs = null)
    {
        Options = options;
        Errors = errors;
        WantsHelp = wantsHelp;
        Inputs = inputs ?? Array.Empty<string>();
    }

    /// <summary>
    /// Positional input files: the <c>input_file(s)</c> in <c>pipp [options] input_file(s)</c>.
    /// They are not part of <see cref="Options"/> because they are not switches.
    /// </summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>The parsed options. Switches that failed to parse are left at their default value.</summary>
    public PippOptions Options { get; }

    /// <summary>Problems found while parsing, one entry per bad token. Empty when parsing succeeded.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary><see langword="true"/> when the command line contained <c>-help</c>.</summary>
    public bool WantsHelp { get; }

    /// <summary><see langword="true"/> when no errors were reported.</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Parses the <c>pipp_core.exe</c> switch vocabulary into a <see cref="PippOptions"/> instance.
/// </summary>
/// <remarks>
/// <para>
/// Both <c>-name=value</c> and bare <c>-name</c> flags are accepted. Switch names are matched
/// case-insensitively; numbers are parsed with <see cref="CultureInfo.InvariantCulture"/> so a
/// command line behaves identically on every locale. A switch that appears more than once simply
/// overwrites the same property, so the last value wins.
/// </para>
/// <para>
/// Malformed input never throws: unknown switches, missing values, values supplied to flags and
/// out-of-range numbers are collected in <see cref="ParseResult.Errors"/>.
/// </para>
/// <para>
/// Two switches are usable with and without a value: <c>-q</c> (sort every frame) versus
/// <c>-q=&lt;n&gt;</c> (keep the best n), and <c>-db</c> (debayer the frames) versus
/// <c>-db=&lt;pattern&gt;</c> (Bayer pattern index).
/// </para>
/// </remarks>
public static class CommandLineParser
{
    /// <summary>Parses <paramref name="args"/> into a <see cref="PippOptions"/> instance.</summary>
    /// <param name="args">The command-line tokens, without the executable name.</param>
    /// <returns>A <see cref="ParseResult"/> describing the parsed options and any errors.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = new PippOptions();
        var errors = new List<string>();
        var inputs = new List<string>();
        var wantsHelp = false;
        var formatsSeen = false;

        foreach (var rawToken in args)
        {
            // The GUI writes its switch list one token per line and can leave trailing empties behind.
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                continue;
            }

            var token = rawToken.Trim();
            if (token[0] != '-')
            {
                // Positional arguments are the input files: "pipp [options] input_file(s)".
                inputs.Add(token);
                continue;
            }

            var separator = token.IndexOf('=');
            var name = separator < 0 ? token : token[..separator];
            var value = separator < 0 ? null : token[(separator + 1)..];
            var key = name.ToLowerInvariant();

            switch (key)
            {
                // ---------------------------------------------------------------------------------
                // Global / mode
                // ---------------------------------------------------------------------------------
                case "-help":
                    wantsHelp = true;
                    break;

                case "-gui":
                    if (Flag(token, value)) options.Gui = true;
                    break;

                case "-test_run":
                    if (Flag(token, value)) options.TestRun = true;
                    break;

                case "-sdp":
                    if (Flag(token, value)) options.ShowDetectedPixels = true;
                    break;

                case "-sr":
                    if (Flag(token, value)) options.SaveRejectedFrames = true;
                    break;

                case "-no_ram_buff":
                    if (Flag(token, value)) options.NoRamBuffers = true;
                    break;

                case "-check":
                    if (Flag(token, value)) options.CheckOnly = true;
                    break;

                case "-debugavi":
                    if (Flag(token, value)) options.DebugAvi = true;
                    break;

                case "-join":
                    if (Flag(token, value)) options.Join = true;
                    break;

                // ---------------------------------------------------------------------------------
                // Calibration frames
                // ---------------------------------------------------------------------------------
                case "-dark":
                    if (Flag(token, value)) options.DarkSubtraction = true;
                    break;

                case "-save_dark":
                    if (Flag(token, value)) options.SaveMasterDark = true;
                    break;

                case "-hot_pixel":
                    if (TryInt(token, value, 0, 65535, out var hotPixel)) options.HotPixelThreshold = hotPixel;
                    break;

                case "-dark_bayer":
                    if (Flag(token, value)) options.DarkHasBayer = true;
                    break;

                case "-flat":
                    if (Flag(token, value)) options.FlatCalibration = true;
                    break;

                case "-save_flat":
                    if (Flag(token, value)) options.SaveMasterFlat = true;
                    break;

                case "-flat_bayer":
                    if (Flag(token, value)) options.FlatHasBayer = true;
                    break;

                case "-flat_dark":
                    if (Flag(token, value)) options.FlatDarkSubtraction = true;
                    break;

                case "-save_flat_dark":
                    if (Flag(token, value)) options.SaveMasterFlatDark = true;
                    break;

                // ---------------------------------------------------------------------------------
                // Input options
                // ---------------------------------------------------------------------------------
                case "-raw_hpf":
                    if (Flag(token, value)) options.RawHotPixelFilter = true;
                    break;

                case "-raw_db":
                    if (TryEnum(token, value, 0, 3, out int rawDb)) options.RawDebayerAlgorithm = (PippRawDebayerAlgorithm)rawDb;
                    break;

                case "-raw_ocs":
                    if (TryEnum(token, value, 0, 6, out int rawOcs)) options.RawColourSpace = (PippRawColourSpace)rawOcs;
                    break;

                case "-raw_hl":
                    if (TryEnum(token, value, 0, 9, out int rawHl)) options.RawHighlightRecovery = rawHl;
                    break;

                case "-y8":
                    if (Flag(token, value)) options.TrueY800 = true;
                    break;

                case "-wjpos_dtmask":
                    if (HasValue(token, value, out var dateMask)) options.WinJuposDateTimeMask = dateMask;
                    break;

                case "-wjpos_lt2ut":
                    if (Flag(token, value)) options.WinJuposLocalToUtc = true;
                    break;

                case "-wjpos_ipfps":
                    if (TryInt(token, value, 1, int.MaxValue, out var ipFps)) options.WinJuposInputFps = ipFps;
                    break;

                case "-ser_bpp":
                    if (TryInt(token, value, 8, 16, out var serBitDepth)) options.SerBitDepth = serBitDepth;
                    break;

                case "-sf":
                    if (TryInt(token, value, 0, int.MaxValue, out var startFrame)) options.StartFrame = startFrame;
                    break;

                case "-f":
                    if (TryInt(token, value, 0, int.MaxValue, out var frameCount)) options.FrameCount = frameCount;
                    break;

                case "-ctrf":
                    if (Flag(token, value)) options.FramesFromCentre = true;
                    break;

                case "-ifd":
                    if (TryInt(token, value, 0, int.MaxValue, out var inputFrameDrop)) options.InputFrameDrop = inputFrameDrop;
                    break;

                case "-mono":
                    if (Flag(token, value)) options.ColourMode = PippColourMode.Monochrome;
                    break;

                case "-colour":
                    if (Flag(token, value)) options.ColourMode = PippColourMode.Colour;
                    break;

                case "-db":
                    if (value is null)
                    {
                        options.Debayer = true;
                    }
                    else if (TryEnum(token, value, 0, 3, out int pattern))
                    {
                        options.DebayerPattern = (PippDebayerPattern)pattern;
                    }

                    break;

                case "-pb":
                    if (Flag(token, value)) options.ProtectBayer = true;
                    break;

                case "-db_type":
                    if (TryEnum(token, value, 0, 2, out int debayerType)) options.DebayerAlgorithm = (PippDebayerAlgorithm)debayerType;
                    break;

                case "-bin":
                    if (TryInt(token, value, 1, 8, out var binning)) options.Binning = binning;
                    break;

                case "-bin_sum":
                    if (Flag(token, value)) options.BinningCalculation = PippBinningCalculation.Sum;
                    break;

                case "-bin_average":
                    if (Flag(token, value)) options.BinningCalculation = PippBinningCalculation.Average;
                    break;

                // ---------------------------------------------------------------------------------
                // Processing options
                // ---------------------------------------------------------------------------------
                case "-input_gain":
                    if (TryPositiveDouble(token, value, out var gain)) options.InputGain = gain;
                    break;

                case "-gamma":
                    if (TryPositiveDouble(token, value, out var gamma)) options.Gamma = gamma;
                    break;

                case "-median":
                    if (Flag(token, value)) options.MedianFilter = true;
                    break;

                case "-convmono":
                    if (TryInt(token, value, 0, 6, out var convMono)) options.ConvertToMono = convMono;
                    break;

                case "-hs":
                    if (TryInt(token, value, 1, 255, out var stretch)) options.HistogramStretch = stretch;
                    break;

                case "-hs_bp":
                    if (Flag(token, value)) options.HistogramStretchNoBlackPoint = true;
                    break;

                case "-hsrgb":
                    if (Flag(token, value)) options.HistogramStretchRgb = true;
                    break;

                case "-oe":
                    if (TryInt(token, value, 0, int.MaxValue, out var overexposed)) options.OverexposureThreshold = overexposed;
                    break;

                case "-flip_x":
                    if (Flag(token, value)) options.FlipX = true;
                    break;

                case "-flip_y":
                    if (Flag(token, value)) options.FlipY = true;
                    break;

                case "-rotate90":
                    if (Flag(token, value)) options.Rotation = PippRotation.Rotate90;
                    break;

                case "-rotate180":
                    if (Flag(token, value)) options.Rotation = PippRotation.Rotate180;
                    break;

                case "-rotate270":
                    if (Flag(token, value)) options.Rotation = PippRotation.Rotate270;
                    break;

                case "-chk":
                    if (Flag(token, value)) options.CheckForPlanet = true;
                    break;

                case "-ms":
                    if (TryInt(token, value, 0, 100000, out var minSize)) options.MinPlanetSize = minSize;
                    break;

                case "-mp":
                    if (TryInt(token, value, 0, 65535, out var minPixel)) options.MinPixelValue = minPixel;
                    break;

                case "-ctr":
                case "-centre":
                    if (Flag(token, value)) options.Centre = true;
                    break;

                case "-shdw":
                    if (TryEnum(token, value, 0, 8, out int shadow)) options.ShadowEdge = shadow;
                    break;

                case "-cx":
                    if (TryInt(token, value, 0, 100000, out var cropWidth)) options.CropWidth = cropWidth;
                    break;

                case "-cy":
                    if (TryInt(token, value, 0, 100000, out var cropHeight)) options.CropHeight = cropHeight;
                    break;

                case "-ox":
                    if (TryInt(token, value, -100000, 100000, out var offsetX)) options.OffsetX = offsetX;
                    break;

                case "-oy":
                    if (TryInt(token, value, -100000, 100000, out var offsetY)) options.OffsetY = offsetY;
                    break;

                case "-en_surface_feature_tracking":
                    if (Flag(token, value)) options.SurfaceFeatureTracking = true;
                    break;

                case "-en_ss":
                    if (Flag(token, value)) options.EnableStabilisation = true;
                    break;

                case "-afb_x":
                    if (TryInt(token, value, 0, 100000, out var afbX)) options.AnchorFeatureX = afbX;
                    break;

                case "-afb_y":
                    if (TryInt(token, value, 0, 100000, out var afbY)) options.AnchorFeatureY = afbY;
                    break;

                case "-afb_width":
                    if (TryInt(token, value, 0, 100000, out var afbWidth)) options.AnchorFeatureWidth = afbWidth;
                    break;

                case "-afb_height":
                    if (TryInt(token, value, 0, 100000, out var afbHeight)) options.AnchorFeatureHeight = afbHeight;
                    break;

                case "-freject":
                    if (Flag(token, value)) options.AnchorFeatureReject = true;
                    break;

                case "-aoi":
                    if (Flag(token, value)) options.AreaOfInterest = true;
                    break;

                case "-aoi_x":
                    if (TryInt(token, value, 0, 100000, out var aoiX)) options.AreaOfInterestX = aoiX;
                    break;

                case "-aoi_y":
                    if (TryInt(token, value, 0, 100000, out var aoiY)) options.AreaOfInterestY = aoiY;
                    break;

                case "-aoi_width":
                    if (TryInt(token, value, 0, 100000, out var aoiWidth)) options.AreaOfInterestWidth = aoiWidth;
                    break;

                case "-aoi_height":
                    if (TryInt(token, value, 0, 100000, out var aoiHeight)) options.AreaOfInterestHeight = aoiHeight;
                    break;

                case "-aoi_reject":
                    if (Flag(token, value)) options.AreaOfInterestReject = true;
                    break;

                case "-aoi_crop":
                    if (Flag(token, value)) options.AreaOfInterestCrop = true;
                    break;

                case "-resize_en":
                    if (Flag(token, value)) options.ResizeFrames = true;
                    break;

                case "-resize_width":
                    if (TryInt(token, value, 1, 100000, out var resizeWidth)) options.ResizeWidth = resizeWidth;
                    break;

                case "-resize_height":
                    // -1 keeps the source aspect ratio.
                    if (TryInt(token, value, -1, 100000, out var resizeHeight)) options.ResizeHeight = resizeHeight;
                    break;

                // ---------------------------------------------------------------------------------
                // Animation options
                // ---------------------------------------------------------------------------------
                case "-anim_fwd":
                    if (Flag(token, value)) options.AnimateForward = true;
                    break;

                case "-anim_fwd_lf_rep":
                    if (TryInt(token, value, 0, int.MaxValue, out var fwdRepeat)) options.RepeatForwardLast = fwdRepeat;
                    break;

                case "-anim_rev":
                    if (Flag(token, value)) options.AnimateReverse = true;
                    break;

                case "-anim_rev_lf_rep":
                    if (TryInt(token, value, 0, int.MaxValue, out var revRepeat)) options.RepeatReverseLast = revRepeat;
                    break;

                case "-anim_all_rep":
                    if (TryInt(token, value, 0, int.MaxValue, out var allRepeat)) options.RepeatAll = allRepeat;
                    break;

                case "-no_anim_rev_last_rep":
                    if (Flag(token, value)) options.NoReverseInLastRepeat = true;
                    break;

                case "-anim_all_rep_always":
                    if (Flag(token, value)) options.RepeatAllForever = true;
                    break;

                // ---------------------------------------------------------------------------------
                // Quality options
                // ---------------------------------------------------------------------------------
                case "-q":
                    if (value is null)
                    {
                        options.QualityAllFrames = true;
                    }
                    else if (TryInt(token, value, 0, int.MaxValue, out var qualityFrames))
                    {
                        options.QualityFrameCount = qualityFrames;
                    }

                    break;

                case "-qpcnt":
                    if (TryInt(token, value, 0, 100, out var qualityPercent)) options.QualityPercent = qualityPercent;
                    break;

                case "-qold":
                    if (Flag(token, value)) options.QualityAlgorithm = PippQualityAlgorithm.Ninox;
                    break;

                case "-qhisto":
                    if (Flag(token, value)) options.QualityAlgorithm = PippQualityAlgorithm.Histogram;
                    break;

                case "-qtotal":
                    if (Flag(token, value)) options.QualityAlgorithm = PippQualityAlgorithm.TotalPixel;
                    break;

                case "-qmin":
                    if (TryInt(token, value, 0, 1000, out var qMin)) options.QualityMinDownsample = qMin;
                    break;

                case "-qmax":
                    if (TryInt(token, value, 0, 1000, out var qMax)) options.QualityMaxDownsample = qMax;
                    break;

                case "-qinc":
                    if (TryInt(token, value, 0, 1000, out var qInc)) options.QualityIncrement = qInc;
                    break;

                case "-qw":
                    if (Flag(token, value)) options.QualityWeighting = true;
                    break;

                case "-nr":
                    if (Flag(token, value)) options.NoRenumber = true;
                    break;

                // ---------------------------------------------------------------------------------
                // Output options
                // ---------------------------------------------------------------------------------
                case "-osubdir":
                    if (HasValue(token, value, out var subdirectory)) options.OutputSubdirectory = subdirectory;
                    break;

                case "-od":
                    if (HasValue(token, value, out var outputDirectory)) options.OutputDirectory = outputDirectory;
                    break;

                case "-op":
                    if (HasValue(token, value, out var prefix)) options.OutputPrefix = prefix;
                    break;

                case "-os":
                    if (HasValue(token, value, out var suffix)) options.OutputSuffix = suffix;
                    break;

                case "-avi":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Avi);
                    break;

                case "-ser":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Ser);
                    break;

                case "-bmp":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Bmp);
                    break;

                case "-tif":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Tiff);
                    break;

                case "-png":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Png);
                    break;

                case "-fits":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Fits);
                    break;

                case "-gif":
                    if (Flag(token, value)) SetFormat(PippOutputFormats.Gif);
                    break;

                case "-avi_codec":
                    if (TryEnum(token, value, 0, 2, out int aviCodec)) options.AviCodec = (PippAviCodec)aviCodec;
                    break;

                case "-avi_quality":
                    if (TryInt(token, value, 1, 100, out var aviQuality)) options.AviQuality = aviQuality;
                    break;

                case "-avi_old":
                    if (Flag(token, value)) options.AviOldFormat = PippAviOldFormat.Old;
                    break;

                case "-avi_old4":
                    if (Flag(token, value)) options.AviOldFormat = PippAviOldFormat.Old4;
                    break;

                case "-avi_safe_width":
                    if (Flag(token, value)) options.AviSafeWidth = true;
                    break;

                case "-auto_fps":
                    if (Flag(token, value)) options.AutoFps = true;
                    break;

                case "-fps_rate":
                    if (TryInt(token, value, 1, int.MaxValue, out var fpsRate)) options.FpsRate = fpsRate;
                    break;

                case "-fps_scale":
                    if (TryInt(token, value, 1, int.MaxValue, out var fpsScale)) options.FpsScale = fpsScale;
                    break;

                case "-gif_quant_type":
                    if (TryEnum(token, value, 0, 1, out int gifQuant)) options.GifQuantisation = (PippGifQuantisation)gifQuant;
                    break;

                case "-gif_border_tol":
                    if (TryInt(token, value, -1, 100, out var gifBorder)) options.GifBorderTolerance = gifBorder;
                    break;

                case "-gif_trns_pxl_tol":
                    if (TryInt(token, value, -1, 100, out var gifTransparent)) options.GifTransparentPixelTolerance = gifTransparent;
                    break;

                case "-gif_col_tbl_depth":
                    if (TryInt(token, value, 1, 8, out var gifDepth)) options.GifColorTableDepth = gifDepth;
                    break;

                case "-gif_lossy_comp":
                    if (TryInt(token, value, 0, 100, out var gifLossy)) options.GifLossyCompression = gifLossy;
                    break;

                case "-keepimgname":
                    if (Flag(token, value)) options.KeepImageFilenames = true;
                    break;

                case "-nlz":
                    if (Flag(token, value)) options.NoLeadingZeros = true;
                    break;

                case "-incqv":
                    if (Flag(token, value)) options.IncludeQualityValue = true;
                    break;

                case "-rgb":
                    if (Flag(token, value)) options.SplitRgb = true;
                    break;

                case "-drop":
                    if (TryInt(token, value, 0, int.MaxValue, out var frameDrop)) options.OutputFrameDrop = frameDrop;
                    break;

                case "-wjpos_time":
                    if (TryEnum(token, value, 0, 2, out int wjposTime)) options.WinJuposTime = (PippWinJuposTimeMode)wjposTime;
                    break;

                // ---------------------------------------------------------------------------------
                // Engine paths
                // ---------------------------------------------------------------------------------
                case "-temp_dir":
                    if (HasValue(token, value, out var tempDirectory)) options.TempDirectory = tempDirectory;
                    break;

                case "-dark_tmp":
                    if (HasValue(token, value, out var darkTmp)) options.MasterDarkTmpFile = darkTmp;
                    break;

                case "-dark_pgm":
                    if (HasValue(token, value, out var darkPgm)) options.MasterDarkPgmFile = darkPgm;
                    break;

                case "-flat_tmp":
                    if (HasValue(token, value, out var flatTmp)) options.MasterFlatTmpFile = flatTmp;
                    break;

                case "-flat_dark_tmp":
                    if (HasValue(token, value, out var flatDarkTmp)) options.MasterFlatDarkTmpFile = flatDarkTmp;
                    break;

                default:
                    errors.Add($"Unknown switch '{name}'.");
                    break;
            }
        }

        return new ParseResult(options, errors, wantsHelp, inputs);

        // ---- local helpers ---------------------------------------------------------------------
        void SetFormat(PippOutputFormats format)
        {
            // The first format switch replaces the GUI's default AVI output; later ones add to it.
            if (!formatsSeen)
            {
                options.Formats = format;
                formatsSeen = true;
            }
            else
            {
                options.Formats |= format;
            }
        }

        bool HasValue(string switchToken, string? switchValue, out string text)
        {
            if (switchValue is null)
            {
                errors.Add($"Switch '{switchToken}' requires a value ('{switchToken}=<value>').");
                text = string.Empty;
                return false;
            }

            text = switchValue;
            return true;
        }

        bool Flag(string switchToken, string? switchValue)
        {
            if (switchValue is null)
            {
                return true;
            }

            errors.Add($"Switch '{switchToken}' does not take a value.");
            return false;
        }

        bool TryInt(string switchToken, string? switchValue, int minimum, int maximum, out int number)
        {
            number = 0;
            if (!HasValue(switchToken, switchValue, out var text))
            {
                return false;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                errors.Add($"Value '{text}' for switch '{switchToken}' is not a valid integer.");
                return false;
            }

            if (number < minimum || number > maximum)
            {
                errors.Add($"Value {number} for switch '{switchToken}' is outside the allowed range {minimum}..{maximum}.");
                number = 0;
                return false;
            }

            return true;
        }

        bool TryEnum(string switchToken, string? switchValue, int minimum, int maximum, out int index)
        {
            return TryInt(switchToken, switchValue, minimum, maximum, out index);
        }

        bool TryPositiveDouble(string switchToken, string? switchValue, out double number)
        {
            number = 0;
            if (!HasValue(switchToken, switchValue, out var text))
            {
                return false;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || double.IsNaN(number)
                || double.IsInfinity(number)
                || number <= 0)
            {
                errors.Add($"Value '{text}' for switch '{switchToken}' is not a positive decimal number.");
                number = 0;
                return false;
            }

            return true;
        }
    }
}
