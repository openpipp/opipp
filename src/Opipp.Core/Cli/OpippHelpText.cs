namespace Opipp.Core.Cli;

/// <summary>
/// Human-readable usage text for the PIPP command-line vocabulary, grouped by the same categories the
/// GUI uses (source/calibration, input, processing, animation, quality, output, misc).
/// </summary>
/// <remarks>
/// The wording follows the engine's own embedded <c>-help</c> table where it is available, extended with
/// the switch spellings the GUI emits.
/// </remarks>
public static class OpippHelpText
{
    /// <summary>The complete usage text.</summary>
    public const string Text = """
OPIPP command line options
=========================

Usage: pipp [options] input_file(s)

Output formats (several may be combined):
  -avi                     Write AVI video frames.
  -ser                     Write a LuCam recorder SER file (mono only).
  -bmp                     Write 8-bit BMP still frames.
  -tif                     Write 8/16-bit TIFF still frames.
  -png                     Write 8/16-bit PNG still frames.
  -fits                    Write 16-bit FITS still frames.
  -gif                     Write an animated GIF.

Calibration frames:
  -dark                    Subtract a master dark frame stacked from the dark inputs.
  -save_dark               Save the stacked master dark beside the output.
  -hot_pixel=<n>           Replace hot/dead pixels above this threshold.
  -dark_bayer              The dark frames are Bayer-pattern (undebayered) data.
  -flat                    Apply flat-field calibration.
  -save_flat               Save the stacked master flat beside the output.
  -flat_bayer              The flat frames are Bayer-pattern (undebayered) data.
  -flat_dark               Subtract flat-dark frames from the flat frames.
  -save_flat_dark          Save the stacked master flat-dark beside the output.

Input options:
  -raw_hpf                 Apply the hot-pixel filter to RAW camera files.
  -raw_db=<index>          RAW debayer algorithm: 0 Bi-linear, 1 VNG, 2 PPG, 3 AHD.
  -raw_ocs=<index>         RAW output colour space: 0 sensor, 1 sRGB, 2 Adobe RGB,
                           3 wide gamut, 4 ProPhoto, 5 XYZ, 6 ACES.
  -raw_hl=<index>          RAW highlight recovery method, 0-9.
  -y8                      Treat 8-bit input as true Y800 data rather than a Bayer mosaic.
  -ser_bpp=<8..16>         Override the SER bits-per-pixel value (default: SER header).
  -sf=<n>                  First frame to process (1-based).
  -f=<n>                   Number of frames to process.
  -ctrf                    Take the processed frames from the centre of the input.
  -ifd=<n>                 Input frame dropping: keep one frame, then discard n, repeating.
  -mono                    Force monochrome input (disables colour autodetection).
  -colour                  Force colour input (disables colour autodetection).
  -db                      Debayer colour input frames.
  -db=<index>              Debayer pattern: 0 GBRG, 1 BGGR, 2 RGGB, 3 GRBG.
  -db_type=<index>         Debayer algorithm: 0 Bi-linear, 1 HQ linear, 2 Smooth Hue.
  -pb                      Protect the Bayer pattern when centring/cropping.
  -bin=<1..8>              Bin input frames by this factor.
  -bin_sum                 Combine binned pixels by summing them.
  -bin_average             Combine binned pixels by averaging them.
  -wjpos_dtmask=<mask>     Date/time mask for WinJUPOS-friendly filenames.
  -wjpos_lt2ut             Convert extracted local timestamps to UT.
  -wjpos_ipfps=<n>         Frame rate used when interpreting WinJUPOS timestamps.

Processing options:
  -input_gain=<double>     Multiply every input sample by this gain.
  -gamma=<double>          Gamma correction applied to every input sample.
  -median                  Apply a 3x3 median filter.
  -convmono=<0..6>         Convert colour to monochrome:
                             0 RG&B, 1 R, 2 G, 3 B, 4 R&G, 5 R&B, 6 G&B.
  -hs=<n>                  Histogram stretch: scale so the frame's bright tail reaches n% of full
                           scale (1-255; 204 is PIPP's default). -hs=<n>% and -hs=<n>p are the
                           percentage spelling of the same setting.
  -hs_bp                   Histogram stretch without clipping the black point: subtract the frame's
                           measured black point instead of stretching from zero.
  -hsrgb                   Use a separate stretch gain for each of R, G and B (default: the smallest
                           channel gain is used for all three).
  -oe=<n>                  Reject frames with more than n overexposed pixels.
  -flip_x                  Mirror frames horizontally.
  -flip_y                  Mirror frames vertically.
  -rotate90                Rotate frames 90 degrees clockwise.
  -rotate180               Rotate frames 180 degrees.
  -rotate270               Rotate frames 90 degrees anticlockwise.
  -chk                     Detect the object in each frame and reject misses.
  -ms=<n>                  Minimum object size in pixels for object detection.
  -mp=<n>                  Minimum pixel value for object detection.
  -ctr                     Centre the detected object in the output frames.
  -centre                  Alias for -ctr.
  -shdw=<index>            Edge of the shadow when centring a partially lit disc, 0-8.
  -cx=<n>                  Crop width in pixels.
  -cy=<n>                  Crop height in pixels.
  -ox=<n>                  Horizontal crop offset (may be negative).
  -oy=<n>                  Vertical crop offset (may be negative).
  -en_surface_feature_tracking   Enable surface-feature tracking.
  -en_ss                   Enable stabilisation while tracking.
  -afb_x=<n>               Anchor-feature box left edge.
  -afb_y=<n>               Anchor-feature box top edge.
  -afb_width=<n>           Anchor-feature box width.
  -afb_height=<n>          Anchor-feature box height.
  -freject                 Reject frames where the anchor feature is not found.
  -aoi                     Enable the area-of-interest box.
  -aoi_x=<n>               Area-of-interest left edge.
  -aoi_y=<n>               Area-of-interest top edge.
  -aoi_width=<n>           Area-of-interest width.
  -aoi_height=<n>          Area-of-interest height.
  -aoi_reject              Reject frames without the complete area of interest.
  -aoi_crop                Crop output frames to the area of interest.
  -resize_en               Resize the output frames.
  -resize_width=<n>        Output width in pixels.
  -resize_height=<n>       Output height in pixels, or -1 to keep the aspect ratio.

Animation options:
  -anim_fwd                Play the frames forwards.
  -anim_fwd_lf_rep=<n>     Repeat the last forward frame n times.
  -anim_rev                Play the frames backwards after the forward pass.
  -anim_rev_lf_rep=<n>     Repeat the last reverse frame n times.
  -anim_all_rep=<n>        Repeat the whole animation n times.
  -no_anim_rev_last_rep    Leave the reverse pass out of the final repeat.
  -anim_all_rep_always     Repeat the animation forever (GIF output).

Quality options:
  -q                       Sort every frame by quality without limiting the count.
  -q=<n>                   Keep the best n frames.
  -qpcnt=<n>               Keep the best n percent of frames.
  -qold                    Use the original Ninox quality algorithm.
  -qhisto                  Use the peak-histogram quality algorithm.
  -qtotal                  Use the total-brightness quality algorithm.
  -qw                      Weight the final stack by frame quality.
  -qmin=<n>                Smallest quality downsampling factor.
  -qmax=<n>                Largest quality downsampling factor.
  -qinc=<n>                Quality downsampling step.
  -nr                      Do not renumber/reorder frames into quality order.

Output options:
  -od=<path>               Output directory.
  -osubdir=<name>          Subdirectory created under the output directory.
  -op=<prefix>             Prefix for output filenames.
  -os=<suffix>             Suffix for output filenames.
  -drop=<n>                Only output one frame in every n.
  -nr                      Do not renumber output frames.
  -nlz                     Do not pad filename numbers with leading zeros.
  -incqv                   Include the frame quality value in the filename.
  -rgb                     Split colour output into separate R, G and B files.
  -keepimgname             Keep the original image filenames.
  -avi_codec=<index>       AVI codec: 0 DIB (uncompressed Y800). This engine has no
                           Ut Video or MPEG-4 encoder, so 1 (ULRG) and 2 (FMP4) are
                           rejected for AVI output; the original engine accepted them.
  -avi_quality=<1..10>     AVI codec quality, 1-10 (the original engine rejects higher
                           values). Unused by the uncompressed DIB path.
  -avi_old                 Write the legacy AVI layout.
  -avi_old4                Write the legacy AVI layout sized for 4 GB files.
  -avi_safe_width          Pad AVI frames to a safe width multiple.
  -auto_fps                Take the output frame rate from the input files.
  -fps_rate=<n>            Frame-rate numerator.
  -fps_scale=<n>           Frame-rate denominator.
  -gif_quant_type=<index>  GIF quantisation: 0 neural-net, 1 median cut.
  -gif_border_tol=<n>      GIF unchanged-border tolerance (-1 disables it).
  -gif_trns_pxl_tol=<n>    GIF transparent-pixel tolerance (-1 disables it).
  -gif_col_tbl_depth=<n>   GIF colour-table bit depth (8 selects the engine default).
  -gif_lossy_comp=<n>      GIF lossy-compression strength (0 disables it).
  -wjpos_time=<index>      WinJUPOS filename timestamp: 0 mid-point, 1 earliest, 2 first.

Final image (OPIPP extension):
  These switches are not part of the original engine's vocabulary: pipp_core.exe
  exits 127 (unknown switch) on them. Without -stack= nothing here is enabled and
  every existing transcript is unchanged.
  -stack=mean|sum|median   Register the selected frames and combine them into ONE final image
                           written as <output>/<name><ext>, instead of one file per frame.
                           mean and sum accumulate in double precision and quantise the
                           result half away from zero, clamped to the input depth; median
                           takes the exact middle sample (the two middle samples averaged
                           half-up, like the legacy stacker, for an even count).
  -stack_coverage=crop     Output covers only the region every contributor can fill after
                           registration, so the frame shrinks per the shifts. This is the
                           default.
  -stack_coverage=canvas   Keep the first frame's canvas; pixels no frame can supply are
                           written as 0 rather than a background estimate.
  -stack_subpixel=on       Apply the fractional part of each alignment offset with bilinear
                           resampling (default).
  -stack_subpixel=off      Apply whole pixels only, rounding half away from zero: exactly
                           the pre-0.2 shift, with no resampling.
  -derotate=on             Correct field rotation while integrating. PIPP's alignment is
                           translation-only, so a capture whose field rotates (alt-az
                           tracking, a handheld camera, camera roll) smears around the edges
                           however well it is centred: each frame is shifted into place but
                           still turned, so the limb of the object and every surface feature
                           streak along an arc. With -derotate=on every frame is
                           measured against frame 0, with a predicted window to
                           keep the search cheap; a keyframe sample anchors long
                           selections, and beyond 120 frames a subset is refined
                           and the rest interpolated. Every frame is then
                           rotated about the image centre by its own angle, and
                           all frames are cropped to the one rectangle every
                           rotation can fill, so the integrated edges are sharp
                           instead of radially smeared. The output is therefore
                           smaller than the input whenever an angle is not zero.
                           Off by default.
  -derotate_search=<deg>   Largest rotation searched either side of frame 0, from 0.1 to 45
                           degrees (default 5). A wider search costs time and can lock onto
                           the wrong angle when the frame repeats itself; a narrower one
                           refuses captures that rotated further. Only used with
                           -derotate=on.
  -stack_coverage, -stack_subpixel, -derotate and -derotate_search require -stack= and are
  rejected otherwise. -qw
  weights mean/sum by each frame's copy count; median expands the copies into frames
  because it takes no weights. A cancelled or failed integration leaves no output file.
  Raw Bayer capture: a mosaic frame can move only by whole pixels, and every frame must
  land on the same CFA phase. An alignment that moves a frame by an odd number of pixels
  (a centring shift of -20,+39 on a raw RGGB capture, for example) moves its 2x2 colour
  tile to another phase, so integrating that raw capture directly fails with exit 1 and
  writes nothing. Debayer first with -db, or -db=<pattern> to override the detected
  pattern (0 GBRG, 1 BGGR, 2 RGGB, 3 GRBG), and integrate the colour frames. Keeping the
  mosaic works only when every shift is an even whole number of pixels. Turning sub-pixel
  registration off (-stack_subpixel=off) makes the shifts integral, but an odd rounded
  offset is still refused, and a fractional offset is refused as well because bilinear
  resampling across CFA sites has no single colour. In the GUI, enable "Debayer
  Monochrome Frames" before a final-image run on a raw colour capture. -derotate
  refuses a raw mosaic outright: rotating resamples across the colour lattice, so
  debayer with -db first and derotate the colour frames.

Misc options:
  -join                    Join all input files and treat them as one input (Join mode).
  -gui                     Running under the GUI front-end.
  -test_run                Test-run mode: process a single frame and write BMP output.
  -sdp                     Draw the detected object box on output frames.
  -sr                      Save rejected frames in a rejected directory.
  -no_ram_buff             Do not use RAM buffers for frame storage.
  -temp_dir=<path>         Scratch directory for the engine.
  -dark_tmp=<path>         Pre-stacked master dark temp file.
  -dark_pgm=<path>         Pre-stacked master dark PGM file.
  -flat_tmp=<path>         Pre-stacked master flat temp file.
  -flat_dark_tmp=<path>    Pre-stacked master flat-dark temp file.
  -debugavi                Output debug details of AVI files.
  -check                   Validate the command line, calibration masters, input files,
                           processing geometry and output settings, then exit without
                           writing anything. Quick structural validation: every HDU of a
                           FITS input is walked (header, data offset and data-block length)
                           without decoding pixels, and the first frame of every other
                           input is decoded for the geometry checks. A full processing run
                           decodes every frame and is the authoritative validation.
                           Exits 0 when valid, 2 when not.
                           Compatibility note: the original engine treats -check as an
                           alias of -chk (planet detection). OPIPP keeps the documented
                           validate-and-exit meaning; use -chk for planet detection.
  -help                    Display this help.

Result codes:
  0                        Success: every input was read and output was produced.
  1                        Partial result or runtime failure: any output written is incomplete.
  2                        Command-line or configuration error; nothing was processed.
  -1                       Failure: no output (every frame was discarded).
  127                      Unknown switch.
  130                      Cancelled.
""";
}
