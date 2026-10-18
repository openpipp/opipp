namespace Pipp.Core.Cli;

/// <summary>
/// Human-readable usage text for the PIPP command-line vocabulary, grouped by the same categories the
/// GUI uses (source/calibration, input, processing, animation, quality, output, misc).
/// </summary>
/// <remarks>
/// The wording follows the engine's own embedded <c>-help</c> table where it is available, extended with
/// the switch spellings the GUI emits.
/// </remarks>
public static class PippHelpText
{
    /// <summary>The complete usage text.</summary>
    public const string Text = """
PIPP command line options
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
  -avi_codec=<index>       AVI codec: 0 DIB, 1 ULRG (Ut Video), 2 FMP4 (MPEG-4).
  -avi_quality=<1..100>    AVI codec quality.
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
  -check                   Validate the command line and input files, then exit.
  -help                    Display this help.
""";
}
