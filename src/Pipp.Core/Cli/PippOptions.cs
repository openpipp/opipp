namespace Pipp.Core.Cli;

/// <summary>
/// The output container formats the engine can write. Several formats may be selected at once;
/// the GUI exposes them as radio buttons but the engine accepts any combination.
/// </summary>
[Flags]
public enum PippOutputFormats
{
    /// <summary>No output format selected.</summary>
    None = 0,

    /// <summary>Uncompressed/compressed AVI video (<c>-avi</c>).</summary>
    Avi = 1 << 0,

    /// <summary>LuCam recorder SER stream (<c>-ser</c>).</summary>
    Ser = 1 << 1,

    /// <summary>8-bit BMP still frames (<c>-bmp</c>).</summary>
    Bmp = 1 << 2,

    /// <summary>8/16-bit TIFF still frames (<c>-tif</c>).</summary>
    Tiff = 1 << 3,

    /// <summary>8/16-bit PNG still frames (<c>-png</c>).</summary>
    Png = 1 << 4,

    /// <summary>16-bit FITS still frames (<c>-fits</c>).</summary>
    Fits = 1 << 5,

    /// <summary>Animated GIF (<c>-gif</c>).</summary>
    Gif = 1 << 6,
}

/// <summary>How PIPP decides whether the input frames are colour or monochrome (<c>-mono</c>, <c>-colour</c>).</summary>
public enum PippColourMode
{
    /// <summary>Automatic detection (the default; no switch is emitted).</summary>
    Auto = 0,

    /// <summary>Force monochrome input (<c>-mono</c>).</summary>
    Monochrome = 1,

    /// <summary>Force colour input (<c>-colour</c>).</summary>
    Colour = 2,
}

/// <summary>Bayer pattern used when debayering (<c>-db=&lt;pattern&gt;</c>). Values match the GUI combo box order.</summary>
public enum PippDebayerPattern
{
    /// <summary>GBRG (0).</summary>
    Gbrg = 0,

    /// <summary>BGGR (1).</summary>
    Bggr = 1,

    /// <summary>RGGB (2).</summary>
    Rggb = 2,

    /// <summary>GRBG (3).</summary>
    Grbg = 3,
}

/// <summary>Debayer algorithm (<c>-db_type=&lt;index&gt;</c>). Values match the GUI combo box order.</summary>
public enum PippDebayerAlgorithm
{
    /// <summary>Bi-linear, the fastest algorithm (0).</summary>
    Bilinear = 0,

    /// <summary>HQ linear (1).</summary>
    HqLinear = 1,

    /// <summary>Smooth hue (2).</summary>
    SmoothHue = 2,
}

/// <summary>Camera rotation applied to every frame. Values match the GUI combo box order.</summary>
public enum PippRotation
{
    /// <summary>No rotation (the default; no switch is emitted).</summary>
    None = 0,

    /// <summary>Rotate 90 degrees clockwise (<c>-rotate90</c>).</summary>
    Rotate90 = 1,

    /// <summary>Rotate 90 degrees anticlockwise (<c>-rotate270</c>).</summary>
    Rotate270 = 2,

    /// <summary>Rotate 180 degrees (<c>-rotate180</c>).</summary>
    Rotate180 = 3,
}

/// <summary>Frame-quality metric used when quality sorting is enabled.</summary>
public enum PippQualityAlgorithm
{
    /// <summary>The default PIPP algorithm (no switch is emitted).</summary>
    Default = 0,

    /// <summary>The original Ninox algorithm (<c>-qold</c>).</summary>
    Ninox = 1,

    /// <summary>Peak-histogram metric (<c>-qhisto</c>).</summary>
    Histogram = 2,

    /// <summary>Total-brightness metric (<c>-qtotal</c>).</summary>
    TotalPixel = 3,
}

/// <summary>AVI codec (<c>-avi_codec=&lt;index&gt;</c>). Values match the GUI combo box order.</summary>
public enum PippAviCodec
{
    /// <summary>DIB - raw uncompressed (0).</summary>
    Dib = 0,

    /// <summary>ULRG - Ut Video lossless compression (1).</summary>
    Ulrg = 1,

    /// <summary>FMP4 - MPEG-4 lossy compression (2).</summary>
    Fmp4 = 2,
}

/// <summary>Legacy AVI layout selection (<c>-avi_old</c>, <c>-avi_old4</c>).</summary>
public enum PippAviOldFormat
{
    /// <summary>Normal AVI layout (the default; no switch is emitted).</summary>
    None = 0,

    /// <summary>Legacy layout with the original 2 GB size handling (<c>-avi_old</c>).</summary>
    Old = 1,

    /// <summary>Legacy layout sized for 4 GB output files (<c>-avi_old4</c>).</summary>
    Old4 = 2,
}

/// <summary>GIF colour-quantisation algorithm (<c>-gif_quant_type=&lt;index&gt;</c>).</summary>
public enum PippGifQuantisation
{
    /// <summary>Neural-net quantisation (0).</summary>
    NeuralNet = 0,

    /// <summary>Median-cut quantisation (1).</summary>
    MedianCut = 1,
}

/// <summary>Debayer algorithm used for RAW camera files (<c>-raw_db=&lt;index&gt;</c>).</summary>
public enum PippRawDebayerAlgorithm
{
    /// <summary>Bi-linear, fastest (0).</summary>
    Bilinear = 0,

    /// <summary>VNG - variable number of gradients (1).</summary>
    Vng = 1,

    /// <summary>PPG - patterned pixel grouping (2).</summary>
    Ppg = 2,

    /// <summary>AHD - adaptive homogeneity-directed (3).</summary>
    Ahd = 3,
}

/// <summary>Output colour space for RAW files (<c>-raw_ocs=&lt;index&gt;</c>). Values match the GUI combo box order.</summary>
public enum PippRawColourSpace
{
    /// <summary>Raw camera sensor colour, fastest (0).</summary>
    CameraSensor = 0,

    /// <summary>sRGB D65 (1).</summary>
    SrgbD65 = 1,

    /// <summary>Adobe RGB (1998) D65 (2).</summary>
    AdobeRgb1998 = 2,

    /// <summary>Wide gamut RGB D65 (3).</summary>
    WideGamutRgb = 3,

    /// <summary>Kodak ProPhoto RGB D65 (4).</summary>
    ProPhotoRgb = 4,

    /// <summary>XYZ (5).</summary>
    Xyz = 5,

    /// <summary>ACES (6).</summary>
    Aces = 6,
}

/// <summary>Timestamp written into WinJUPOS-friendly output filenames (<c>-wjpos_time=&lt;index&gt;</c>).</summary>
public enum PippWinJuposTimeMode
{
    /// <summary>Mid-point time of the output frames (0).</summary>
    OutputMidPoint = 0,

    /// <summary>Earliest output frame time (1).</summary>
    Earliest = 1,

    /// <summary>First output frame time (2).</summary>
    First = 2,
}

/// <summary>How multiple pixels are combined when binning (<c>-bin_sum</c>, <c>-bin_average</c>).</summary>
public enum PippBinningCalculation
{
    /// <summary>Average the binned pixels.</summary>
    Average = 0,

    /// <summary>Sum the binned pixels (<c>-bin_sum</c>).</summary>
    Sum = 1,
}

/// <summary>
/// Typed, mutable model of the complete <c>pipp_core.exe</c> command-line vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Every property maps to exactly one engine switch. Flags default to <see langword="false"/> and are
/// only emitted when set, because a switch vocabulary has no way to spell "this flag is off"; the
/// GUI checkbox default is noted in each property's remarks where the decompiled
/// <c>reset_*</c> methods show one.
/// </para>
/// <para>
/// Numeric/string properties whose switch is optional use <see cref="Nullable{T}"/>; <see langword="null"/>
/// means "switch absent" and the engine applies its own default. Properties that the GUI always emits
/// (for example <see cref="RawHighlightRecovery"/> and <see cref="OutputSubdirectory"/>) carry the GUI's
/// value as their initializer.
/// </para>
/// <para>
/// This is a record with <c>set</c> accessors, so value equality, <c>with</c> expressions and
/// round-trip assertions come for free.
/// </para>
/// </remarks>
public sealed record PippOptions
{
    // ---------------------------------------------------------------------------------------------
    // Global / mode
    // ---------------------------------------------------------------------------------------------

    /// <summary>Running under the GUI front-end (<c>-gui</c>).</summary>
    /// <remarks>The GUI always prepends this switch; the headless engine does not need it.</remarks>
    public bool Gui { get; set; }

    /// <summary>Test-run mode: process a single frame and write BMP output (<c>-test_run</c>).</summary>
    public bool TestRun { get; set; }

    /// <summary>Draw the detected object box on output frames (<c>-sdp</c>).</summary>
    public bool ShowDetectedPixels { get; set; }

    /// <summary>Write rejected frames to a <c>rejected</c> directory instead of discarding them (<c>-sr</c>).</summary>
    /// <remarks>GUI default: disabled (<c>saveRejectedFramesToolStripMenuItem</c> is unchecked at startup).</remarks>
    public bool SaveRejectedFrames { get; set; }

    /// <summary>Do not use RAM buffers for frame storage (<c>-no_ram_buff</c>).</summary>
    /// <remarks>GUI default: RAM buffering on (<c>use_ram_buffers</c> is true), so the switch is normally absent.</remarks>
    public bool NoRamBuffers { get; set; }

    /// <summary>Validate the command line and input files, then exit (<c>-check</c>).</summary>
    public bool CheckOnly { get; set; }

    /// <summary>Print AVI container diagnostics while processing (<c>-debugavi</c>).</summary>
    public bool DebugAvi { get; set; }

    /// <summary>Join all inputs and treat them as one file, Join mode (<c>-join</c>).</summary>
    public bool Join { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Calibration frames
    // ---------------------------------------------------------------------------------------------

    /// <summary>Subtract the master dark frame (<c>-dark</c>).</summary>
    /// <remarks>GUI default: enabled (the dark-frames checkbox is checked at startup).</remarks>
    public bool DarkSubtraction { get; set; }

    /// <summary>Save the stacked master dark next to the output (<c>-save_dark</c>).</summary>
    public bool SaveMasterDark { get; set; }

    /// <summary>Hot/dead pixel replacement threshold; <see langword="null"/> disables it (<c>-hot_pixel=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default value when enabled: 90.</remarks>
    public int? HotPixelThreshold { get; set; }

    /// <summary>The dark frames are Bayer-pattern data (<c>-dark_bayer</c>).</summary>
    public bool DarkHasBayer { get; set; }

    /// <summary>Apply flat-field calibration (<c>-flat</c>).</summary>
    /// <remarks>GUI default: enabled (the flat-frames checkbox is checked at startup).</remarks>
    public bool FlatCalibration { get; set; }

    /// <summary>Save the stacked master flat next to the output (<c>-save_flat</c>).</summary>
    public bool SaveMasterFlat { get; set; }

    /// <summary>The flat frames are Bayer-pattern data (<c>-flat_bayer</c>).</summary>
    public bool FlatHasBayer { get; set; }

    /// <summary>Subtract flat-dark frames from the flats (<c>-flat_dark</c>).</summary>
    /// <remarks>GUI default: enabled.</remarks>
    public bool FlatDarkSubtraction { get; set; }

    /// <summary>Save the stacked master flat-dark (<c>-save_flat_dark</c>).</summary>
    public bool SaveMasterFlatDark { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Input options
    // ---------------------------------------------------------------------------------------------

    /// <summary>Apply the hot-pixel filter to RAW files (<c>-raw_hpf</c>).</summary>
    public bool RawHotPixelFilter { get; set; }

    /// <summary>Debayer algorithm for RAW files (<c>-raw_db=&lt;index&gt;</c>); <see langword="null"/> leaves it to the engine.</summary>
    public PippRawDebayerAlgorithm? RawDebayerAlgorithm { get; set; }

    /// <summary>Output colour space for RAW files (<c>-raw_ocs=&lt;index&gt;</c>); <see langword="null"/> leaves it to the engine.</summary>
    public PippRawColourSpace? RawColourSpace { get; set; }

    /// <summary>RAW highlight-recovery method, 0-9 (<c>-raw_hl=&lt;index&gt;</c>).</summary>
    /// <remarks>The GUI emits this switch unconditionally. GUI default: 1 (leave highlights unclipped).</remarks>
    public int RawHighlightRecovery { get; set; } = 1;

    /// <summary>Treat 8-bit data as true Y800 rather than a Bayer mosaic (<c>-y8</c>).</summary>
    public bool TrueY800 { get; set; }

    /// <summary>Date/time mask used to build WinJUPOS filenames, for example <c>YYYYMMDD_hhmmss</c> (<c>-wjpos_dtmask=&lt;mask&gt;</c>).</summary>
    public string? WinJuposDateTimeMask { get; set; }

    /// <summary>Convert extracted local timestamps to UT (<c>-wjpos_lt2ut</c>).</summary>
    public bool WinJuposLocalToUtc { get; set; }

    /// <summary>Input frame rate used when interpreting WinJUPOS timestamps (<c>-wjpos_ipfps=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default value: 60.</remarks>
    public int? WinJuposInputFps { get; set; }

    /// <summary>Treat SER pixels as this many bits per pixel, 8-16 (<c>-ser_bpp=&lt;n&gt;</c>); <see langword="null"/> uses the SER header.</summary>
    public int? SerBitDepth { get; set; }

    /// <summary>First frame of the processed range, 1-based (<c>-sf=&lt;n&gt;</c>).</summary>
    public int? StartFrame { get; set; }

    /// <summary>Number of frames to process, counted from <see cref="StartFrame"/> or the centre (<c>-f=&lt;n&gt;</c>).</summary>
    public int? FrameCount { get; set; }

    /// <summary>Take <see cref="FrameCount"/> frames from the centre of the input (<c>-ctrf</c>).</summary>
    public bool FramesFromCentre { get; set; }

    /// <summary>Input frame dropping (<c>-ifd=&lt;n&gt;</c>): keep one frame, then discard this many, repeating.</summary>
    public int InputFrameDrop { get; set; }

    /// <summary>Colour/monochrome handling (<c>-mono</c>, <c>-colour</c>).</summary>
    /// <remarks>GUI default: <see cref="PippColourMode.Auto"/>.</remarks>
    public PippColourMode ColourMode { get; set; } = PippColourMode.Auto;

    /// <summary>Debayer colour input frames into separate colour planes (<c>-db</c>).</summary>
    public bool Debayer { get; set; }

    /// <summary>Keep the Bayer pattern intact while centring/cropping (<c>-pb</c>).</summary>
    public bool ProtectBayer { get; set; }

    /// <summary>Debayer algorithm (<c>-db_type=&lt;index&gt;</c>); <see langword="null"/> leaves it to the engine.</summary>
    public PippDebayerAlgorithm? DebayerAlgorithm { get; set; }

    /// <summary>Bayer pattern of the input (<c>-db=&lt;index&gt;</c>); <see langword="null"/> leaves it to the engine.</summary>
    public PippDebayerPattern? DebayerPattern { get; set; }

    /// <summary>Binning factor, 1-8 (<c>-bin=&lt;n&gt;</c>); <see langword="null"/> disables binning.</summary>
    /// <remarks>GUI default: disabled.</remarks>
    public int? Binning { get; set; }

    /// <summary>How binned pixels are combined (<c>-bin_sum</c> / <c>-bin_average</c>); <see langword="null"/> leaves it to the engine.</summary>
    public PippBinningCalculation? BinningCalculation { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Processing options
    // ---------------------------------------------------------------------------------------------

    /// <summary>Multiplier applied to every input sample (<c>-input_gain=&lt;double&gt;</c>).</summary>
    /// <remarks>GUI default: 1.</remarks>
    public double InputGain { get; set; } = 1.0;

    /// <summary>Gamma applied to every input sample (<c>-gamma=&lt;double&gt;</c>).</summary>
    /// <remarks>GUI default: 1.</remarks>
    public double Gamma { get; set; } = 1.0;

    /// <summary>Apply a 3x3 median filter (<c>-median</c>).</summary>
    public bool MedianFilter { get; set; }

    /// <summary>Convert colour to monochrome using channel selection 0-6 (<c>-convmono=&lt;n&gt;</c>); <see langword="null"/> disables it.</summary>
    /// <remarks>
    /// Channel selections: 0 = RG&amp;B, 1 = R, 2 = G, 3 = B, 4 = R&amp;G, 5 = R&amp;B, 6 = G&amp;B.
    /// The GUI's combo order maps onto these values through a lookup table.
    /// </remarks>
    public int? ConvertToMono { get; set; }

    /// <summary>Histogram stretch level 1-255 (<c>-hs=&lt;n&gt;</c>); <see langword="null"/> disables the stretch.</summary>
    /// <remarks>GUI default value when enabled: 75.</remarks>
    public int? HistogramStretch { get; set; }

    /// <summary>Stretch without clipping the black point (<c>-hs_bp</c>).</summary>
    /// <remarks>
    /// The engine measures the frame's black point as the mean of its 25 darkest samples and subtracts
    /// it before applying the gain, instead of stretching from zero.
    /// </remarks>
    public bool HistogramStretchNoBlackPoint { get; set; }

    /// <summary>Use a separate stretch gain for each of R, G and B (<c>-hsrgb</c>).</summary>
    /// <remarks>
    /// Without it the engine applies the smallest of the three channel gains to all channels. It does
    /// not equalise the channels, despite the wording of the original help text.
    /// </remarks>
    public bool HistogramStretchRgb { get; set; }

    /// <summary>Reject frames with more than this many overexposed pixels (<c>-oe=&lt;n&gt;</c>); <see langword="null"/> disables it.</summary>
    /// <remarks>GUI default value when enabled: 20.</remarks>
    public int? OverexposureThreshold { get; set; }

    /// <summary>Mirror frames horizontally (<c>-flip_x</c>).</summary>
    public bool FlipX { get; set; }

    /// <summary>Mirror frames vertically (<c>-flip_y</c>).</summary>
    public bool FlipY { get; set; }

    /// <summary>Frame rotation (<c>-rotate90</c>, <c>-rotate180</c>, <c>-rotate270</c>).</summary>
    public PippRotation Rotation { get; set; } = PippRotation.None;

    /// <summary>Detect the object in each frame, rejecting frames where it is not found (<c>-chk</c>).</summary>
    public bool CheckForPlanet { get; set; }

    /// <summary>Minimum object size in pixels for detection (<c>-ms=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default value when enabled: 15.</remarks>
    public int? MinPlanetSize { get; set; }

    /// <summary>Minimum pixel value for detection (<c>-mp=&lt;n&gt;</c>); only used with an explicit threshold.</summary>
    /// <remarks>GUI default value when enabled: 40.</remarks>
    public int? MinPixelValue { get; set; }

    /// <summary>Centre the detected object in the output frames (<c>-ctr</c>; <c>-centre</c> is an accepted alias).</summary>
    public bool Centre { get; set; }

    /// <summary>Edge of the shadow used when centring a partially lit disc, 0-8 (<c>-shdw=&lt;index&gt;</c>); 0 means none.</summary>
    public int? ShadowEdge { get; set; }

    /// <summary>Crop width in pixels (<c>-cx=&lt;n&gt;</c>).</summary>
    public int? CropWidth { get; set; }

    /// <summary>Crop height in pixels (<c>-cy=&lt;n&gt;</c>).</summary>
    public int? CropHeight { get; set; }

    /// <summary>Horizontal crop offset; negative values move the crop left (<c>-ox=&lt;n&gt;</c>).</summary>
    public int? OffsetX { get; set; }

    /// <summary>Vertical crop offset; negative values move the crop up (<c>-oy=&lt;n&gt;</c>).</summary>
    public int? OffsetY { get; set; }

    /// <summary>Enable surface-feature tracking (<c>-en_surface_feature_tracking</c>).</summary>
    public bool SurfaceFeatureTracking { get; set; }

    /// <summary>Enable frame-to-frame stabilisation while tracking (<c>-en_ss</c>).</summary>
    public bool EnableStabilisation { get; set; }

    /// <summary>Anchor-feature box left edge (<c>-afb_x=&lt;n&gt;</c>).</summary>
    public int? AnchorFeatureX { get; set; }

    /// <summary>Anchor-feature box top edge (<c>-afb_y=&lt;n&gt;</c>).</summary>
    public int? AnchorFeatureY { get; set; }

    /// <summary>Anchor-feature box width (<c>-afb_width=&lt;n&gt;</c>).</summary>
    public int? AnchorFeatureWidth { get; set; }

    /// <summary>Anchor-feature box height (<c>-afb_height=&lt;n&gt;</c>).</summary>
    public int? AnchorFeatureHeight { get; set; }

    /// <summary>Reject frames where the anchor feature is not found (<c>-freject</c>).</summary>
    public bool AnchorFeatureReject { get; set; }

    /// <summary>Enable the area-of-interest box (<c>-aoi</c>).</summary>
    public bool AreaOfInterest { get; set; }

    /// <summary>Area-of-interest left edge (<c>-aoi_x=&lt;n&gt;</c>).</summary>
    public int? AreaOfInterestX { get; set; }

    /// <summary>Area-of-interest top edge (<c>-aoi_y=&lt;n&gt;</c>).</summary>
    public int? AreaOfInterestY { get; set; }

    /// <summary>Area-of-interest width (<c>-aoi_width=&lt;n&gt;</c>).</summary>
    public int? AreaOfInterestWidth { get; set; }

    /// <summary>Area-of-interest height (<c>-aoi_height=&lt;n&gt;</c>).</summary>
    public int? AreaOfInterestHeight { get; set; }

    /// <summary>Reject frames that do not fully contain the area of interest (<c>-aoi_reject</c>).</summary>
    public bool AreaOfInterestReject { get; set; }

    /// <summary>Crop output frames to the area of interest (<c>-aoi_crop</c>).</summary>
    public bool AreaOfInterestCrop { get; set; }

    /// <summary>Resize output frames (<c>-resize_en</c>).</summary>
    public bool ResizeFrames { get; set; }

    /// <summary>Output width in pixels (<c>-resize_width=&lt;n&gt;</c>).</summary>
    public int? ResizeWidth { get; set; }

    /// <summary>Output height in pixels, or -1 to keep the source aspect ratio (<c>-resize_height=&lt;n&gt;</c>).</summary>
    public int? ResizeHeight { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Animation options
    // ---------------------------------------------------------------------------------------------

    /// <summary>Play the frames forwards (<c>-anim_fwd</c>).</summary>
    /// <remarks>GUI default: enabled (the forward checkbox is checked at startup).</remarks>
    public bool AnimateForward { get; set; }

    /// <summary>Repeat the last forward frame this many times (<c>-anim_fwd_lf_rep=&lt;n&gt;</c>).</summary>
    public int? RepeatForwardLast { get; set; }

    /// <summary>Play the frames backwards after the forward pass (<c>-anim_rev</c>).</summary>
    public bool AnimateReverse { get; set; }

    /// <summary>Repeat the last reverse frame this many times (<c>-anim_rev_lf_rep=&lt;n&gt;</c>).</summary>
    public int? RepeatReverseLast { get; set; }

    /// <summary>Repeat the whole animation this many times (<c>-anim_all_rep=&lt;n&gt;</c>).</summary>
    public int? RepeatAll { get; set; }

    /// <summary>Leave the reverse pass out of the final repeat (<c>-no_anim_rev_last_rep</c>).</summary>
    /// <remarks>GUI default: enabled; the switch is only meaningful together with <see cref="RepeatAll"/>.</remarks>
    public bool NoReverseInLastRepeat { get; set; }

    /// <summary>Repeat the animation forever (<c>-anim_all_rep_always</c>).</summary>
    /// <remarks>GUI default: enabled for GIF output.</remarks>
    public bool RepeatAllForever { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Quality options
    // ---------------------------------------------------------------------------------------------

    /// <summary>Keep the best N frames (<c>-q=&lt;n&gt;</c>).</summary>
    public int? QualityFrameCount { get; set; }

    /// <summary>Keep the best N percent of frames (<c>-qpcnt=&lt;n&gt;</c>).</summary>
    public int? QualityPercent { get; set; }

    /// <summary>Sort every frame by quality without limiting the count (<c>-q</c>).</summary>
    public bool QualityAllFrames { get; set; }

    /// <summary>Quality metric (<c>-qold</c>, <c>-qhisto</c>, <c>-qtotal</c>).</summary>
    public PippQualityAlgorithm QualityAlgorithm { get; set; } = PippQualityAlgorithm.Default;

    /// <summary>Smallest downsampling factor searched by the quality algorithm (<c>-qmin=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default value: 3.</remarks>
    public int? QualityMinDownsample { get; set; }

    /// <summary>Largest downsampling factor searched by the quality algorithm (<c>-qmax=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default value: 5.</remarks>
    public int? QualityMaxDownsample { get; set; }

    /// <summary>Downsampling step (<c>-qinc=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default value: 1.</remarks>
    public int? QualityIncrement { get; set; }

    /// <summary>Weight the final stack by frame quality (<c>-qw</c>).</summary>
    public bool QualityWeighting { get; set; }

    /// <summary>Do not renumber/reorder frames into quality order (<c>-nr</c>).</summary>
    /// <remarks>
    /// The GUI emits this switch both for "disable frame renumbering" and for "do not reorder in quality
    /// order"; both meanings map onto this single property and the builder emits it once.
    /// </remarks>
    public bool NoRenumber { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Output options
    // ---------------------------------------------------------------------------------------------

    /// <summary>Subdirectory created under the output directory (<c>-osubdir=&lt;name&gt;</c>); empty disables it.</summary>
    /// <remarks>The GUI always emits this switch, using <c>pipp_</c> plus an optional timestamp.</remarks>
    public string OutputSubdirectory { get; set; } = string.Empty;

    /// <summary>Output directory (<c>-od=&lt;path&gt;</c>); <see langword="null"/> uses the source directory.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Prefix prepended to output filenames (<c>-op=&lt;prefix&gt;</c>).</summary>
    public string? OutputPrefix { get; set; }

    /// <summary>Suffix appended to output filenames (<c>-os=&lt;suffix&gt;</c>).</summary>
    /// <remarks>GUI default: <c>_pipp</c>.</remarks>
    public string? OutputSuffix { get; set; }

    /// <summary>Formats to write; several may be combined (<c>-avi -ser -bmp -tif -png -fits -gif</c>).</summary>
    /// <remarks>GUI default: <see cref="PippOutputFormats.Avi"/>.</remarks>
    public PippOutputFormats Formats { get; set; } = PippOutputFormats.Avi;

    /// <summary>AVI codec (<c>-avi_codec=&lt;index&gt;</c>).</summary>
    /// <remarks>GUI default: <see cref="PippAviCodec.Dib"/>.</remarks>
    public PippAviCodec AviCodec { get; set; } = PippAviCodec.Dib;

    /// <summary>AVI codec quality, 1-100 (<c>-avi_quality=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default: 10.</remarks>
    public int AviQuality { get; set; } = 10;

    /// <summary>Legacy AVI layout (<c>-avi_old</c> / <c>-avi_old4</c>).</summary>
    public PippAviOldFormat AviOldFormat { get; set; } = PippAviOldFormat.None;

    /// <summary>Pad AVI frames to a safe width multiple (<c>-avi_safe_width</c>).</summary>
    public bool AviSafeWidth { get; set; }

    /// <summary>Take the output frame rate from the input files (<c>-auto_fps</c>).</summary>
    /// <remarks>GUI default: enabled for AVI output.</remarks>
    public bool AutoFps { get; set; }

    /// <summary>Frame-rate numerator, in hundredths when <see cref="FpsScale"/> is 100 (<c>-fps_rate=&lt;n&gt;</c>).</summary>
    /// <remarks>The GUI reduces the requested frames-per-second value to a fraction; its 60 fps default is 60/1.</remarks>
    public int FpsRate { get; set; } = 60;

    /// <summary>Frame-rate denominator (<c>-fps_scale=&lt;n&gt;</c>); must be greater than zero.</summary>
    public int FpsScale { get; set; } = 1;

    /// <summary>GIF colour quantisation algorithm (<c>-gif_quant_type=&lt;index&gt;</c>).</summary>
    /// <remarks>GUI default: <see cref="PippGifQuantisation.NeuralNet"/>.</remarks>
    public PippGifQuantisation GifQuantisation { get; set; } = PippGifQuantisation.NeuralNet;

    /// <summary>GIF unchanged-border tolerance, or -1 when disabled (<c>-gif_border_tol=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default: 5, enabled.</remarks>
    public int GifBorderTolerance { get; set; } = 5;

    /// <summary>GIF transparent-pixel tolerance, or -1 when disabled (<c>-gif_trns_pxl_tol=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default: 5, enabled.</remarks>
    public int GifTransparentPixelTolerance { get; set; } = 5;

    /// <summary>GIF colour-table bit depth, 6-8; 8 selects the engine default (<c>-gif_col_tbl_depth=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default: 8 (the bit-depth override is unchecked).</remarks>
    public int GifColorTableDepth { get; set; } = 8;

    /// <summary>GIF lossy-compression strength; 0 disables it (<c>-gif_lossy_comp=&lt;n&gt;</c>).</summary>
    /// <remarks>GUI default: 0.</remarks>
    public int GifLossyCompression { get; set; }

    /// <summary>Keep the original image filenames instead of renumbering (<c>-keepimgname</c>).</summary>
    /// <remarks>GUI default: enabled.</remarks>
    public bool KeepImageFilenames { get; set; }

    /// <summary>Do not pad output filename numbers with leading zeros (<c>-nlz</c>).</summary>
    /// <remarks>The GUI default is leading zeros on, so this switch is normally absent.</remarks>
    public bool NoLeadingZeros { get; set; }

    /// <summary>Append the frame quality value to the filename (<c>-incqv</c>).</summary>
    public bool IncludeQualityValue { get; set; }

    /// <summary>Split colour output into separate R, G and B files (<c>-rgb</c>).</summary>
    public bool SplitRgb { get; set; }

    /// <summary>Write only one frame in every N (<c>-drop=&lt;n&gt;</c>); 0 disables dropping.</summary>
    public int OutputFrameDrop { get; set; }

    /// <summary>Timestamp written into WinJUPOS-friendly filenames (<c>-wjpos_time=&lt;index&gt;</c>); <see langword="null"/> disables it.</summary>
    public PippWinJuposTimeMode? WinJuposTime { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Engine paths (appended by the GUI immediately before handing the command line to the engine)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Scratch directory for the engine (<c>-temp_dir=&lt;path&gt;</c>).</summary>
    public string? TempDirectory { get; set; }

    /// <summary>Pre-stacked master dark temp file (<c>-dark_tmp=&lt;path&gt;</c>).</summary>
    public string? MasterDarkTmpFile { get; set; }

    /// <summary>Pre-stacked master dark PGM file (<c>-dark_pgm=&lt;path&gt;</c>).</summary>
    public string? MasterDarkPgmFile { get; set; }

    /// <summary>Pre-stacked master flat temp file (<c>-flat_tmp=&lt;path&gt;</c>).</summary>
    public string? MasterFlatTmpFile { get; set; }

    /// <summary>Pre-stacked master flat-dark temp file (<c>-flat_dark_tmp=&lt;path&gt;</c>).</summary>
    public string? MasterFlatDarkTmpFile { get; set; }
}
