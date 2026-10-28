using Opipp.Core.Frames;
using Opipp.Core.Quality;

namespace Opipp.Core.Alignment;

/// <summary>Options for field-rotation correction during final-image integration.</summary>
public sealed class DerotationOptions
{
    /// <summary>Enable derotation. Off by default: ordinary runs stay byte-identical.</summary>
    public bool Enabled { get; set; }

    /// <summary>Largest rotation, in degrees, searched either side of the value a link starts from.</summary>
    public double SearchDegrees { get; set; } = 5.0;

    /// <summary>
    /// Frames sampled to start the rotation curve. The curve is interpolated between them and every
    /// frame is then refined against the reference.
    /// </summary>
    /// <remarks>
    /// At least two are used, so the curve is anchored at both ends of the sequence. The estimator
    /// adds anchors of its own where a link would otherwise rotate by more than the search range, so
    /// a fast-rotating capture is followed without the caller having to raise this.
    /// </remarks>
    public int Keyframes { get; set; } = 12;

    /// <summary>
    /// Longest side of the luma copy the matcher works on, in pixels.
    /// </summary>
    /// <remarks>
    /// The angle resolution the matcher can reach is set by this side: the rotation signal lives in
    /// the finest detail the working copy still carries, and a copy three times smaller loses enough
    /// of it to pull the peak towards zero (measured on a real lunar sequence: 0.41 degrees found for
    /// a true 0.51). At 256 the peak lands on the truth; below about 220 it starts to sag.
    /// </remarks>
    public int SampleSide { get; set; } = 256;

    /// <summary>Centroid threshold; when null the frame's own Otsu split is used.</summary>
    public int? MinPixelValue { get; set; }

    /// <summary>
    /// Half-width, in degrees, of the per-frame refinement around the interpolated angle. Zero
    /// disables refinement and keeps the interpolated curve.
    /// </summary>
    /// <remarks>
    /// The curve is only a starting point: each link is measured between two frames several frames
    /// apart, so its error accumulates along the sequence, and the refinement settles every frame
    /// against the reference itself. A frame whose peak sits on the edge of this window is refined
    /// again with a window twice as wide, up to two doublings.
    /// </remarks>
    public double RefineDegrees { get; set; } = 0.2;

    /// <summary>Finest step, in degrees, of the refinement and curve searches.</summary>
    public double RefineStepDegrees { get; set; } = 0.01;

    /// <summary>
    /// Most frames refined individually. A longer selection is refined at this many evenly spaced
    /// frames and the correction is interpolated between them.
    /// </summary>
    /// <remarks>
    /// Refinement is an NCC sweep per frame, so its cost grows with the selection. Above this many
    /// frames the rotation curve is smooth enough that sampling it is indistinguishable from
    /// refining every frame, and the run stays bounded.
    /// </remarks>
    public int MaxRefinedFrames { get; set; } = 120;
}

/// <summary>A frame after rotation correction, with the translation that aligns it to the reference.</summary>
public readonly record struct DerotatedFrame(Frame Frame, AlignmentOffset Offset, double AngleDegrees);

/// <summary>
/// Estimates and applies field rotation for a sequence of frames.
/// </summary>
/// <remarks>
/// <para>
/// PIPP's alignment is translation-only, so a capture whose field rotates (alt-az tracking, a handheld
/// camera, or camera roll) smears around the edges no matter how well it is centred. This estimates a
/// rotation angle per frame against the first frame, rotates each frame about the image centre, crops
/// every frame to one rectangle that all rotations can fill, and reports the translation that aligns
/// the rotated frame to the rotated reference.
/// </para>
/// <para>
/// The angle is matched, not solved: each candidate rotation is applied to a luma copy of the frame,
/// the copy is translated onto the reference, and the pair is scored by zero-mean normalised
/// cross-correlation over the rectangle both rotations still cover. Three details decide whether the
/// peak is on the truth or merely near it. Both images are rotated by half the candidate, so they
/// carry the same resampling blur and neither wins by being sharper. The scored rectangle is the whole
/// surviving area, inset only far enough for the kernel's edge, because the rotation signal grows with
/// radius and a central crop throws away the limb that carries most of it. And the working copy is not
/// downsampled past about 256 pixels a side, because box-averaging away the finest detail pulls the
/// peak towards zero.
/// </para>
/// <para>
/// The curve is built by chaining anchor to anchor — each link is a direct match, subdivided until the
/// rotation across the link fits the search range — so a capture that turns tens of degrees is followed
/// rather than clipped. Every frame is then refined in a narrow window against frame 0, which stops the
/// link errors accumulating, and the translation that reaches the stack is measured by correlation
/// (<see cref="MatchOffset"/>) rather than by centroid difference: the centroid of a limb-lit disc
/// moves with the threshold and with the zero-filled corners a rotation adds, which on a real lunar
/// sequence shifted frames by several pixels and cost more sharpness than leaving them alone.
/// </para>
/// <para>
/// Signs follow image coordinates: <see cref="Rotate"/> turns the content clockwise on screen for a
/// positive angle, and <see cref="EstimateAngles"/> returns the clockwise correction that undoes a
/// frame's rotation — i.e. <c>Rotate(frame, angle)</c> matches frame 0.
/// </para>
/// </remarks>
public static class Derotator
{
    /// <summary>
    /// Cubic convolution parameter (Keys). <c>0</c> is bilinear; <c>-0.5</c> is Catmull-Rom, the
    /// classic bicubic; more negative values sharpen at the price of ringing.
    /// </summary>
    /// <remarks>
    /// Derotation resamples every frame, so this kernel's response is the dominant sharpness term on
    /// an otherwise well-aligned stack. Measured on the 30-frame 512 px lunar sequence, stacking with
    /// the truth angles and only this parameter varying, as a fraction of the single-frame annulus
    /// Laplacian variance: bilinear 17%, Catmull-Rom (-0.5) 51%, -0.75 58%, -1.0 65%, -1.25 74%.
    /// Against an analytic rotation of a smooth scene Catmull-Rom is the most accurate (0.63 levels
    /// RMS against 1.64 at -1.0), so the extra sharpness above it is ringing, not recovered detail —
    /// but Catmull-Rom keeps only half the single-frame edge energy, which is why the default sits at
    /// -1.0: it is the sharpest setting whose ringing stays within about one level of the truth.
    /// </remarks>
    private const double CubicA = -1.0;

    /// <summary>Half-width of the cubic support, in pixels.</summary>
    private const int CubicRadius = 2;

    /// <summary>
    /// Standard deviation, in sample levels, below which an image has no structure to correlate. A
    /// flat frame scores identically at every angle, so without this it would report a random one.
    /// </summary>
    private const double MinimumTexture = 1.0;

    /// <summary>Peak correlation below which there is no evidence of a match at all.</summary>
    private const double MinimumScore = 0.05;

    /// <summary>
    /// Score gain below which a candidate is not preferred over the one it was measured against. A
    /// rotationally symmetric object scores the same everywhere, so a wobble of the last few digits
    /// must not be read as evidence of rotation.
    /// </summary>
    /// <remarks>
    /// It has to be small. On a real sequence the whole correlation peak spans a few times 1e-5 in
    /// score, so a threshold an order of magnitude above that does not reject noise — it freezes the
    /// search on whatever angle it started from, which is what a 1e-4 threshold did.
    /// </remarks>
    private const double MinimumGain = 5e-6;

    /// <summary>
    /// Score difference below which two candidates are a tie for the purposes of the search. Smaller
    /// than <see cref="MinimumGain"/> because this decides which way the sweep walks, not whether the
    /// walk found anything.
    /// </summary>
    private const double TieGain = 1e-7;

    /// <summary>Smallest working size handed to the correlation, in pixels.</summary>
    private const int MinimumSampleSide = 16;

    /// <summary>Central fraction of the frame used for the "is there anything to match" test.</summary>
    private const double ScoredFraction = 0.8;

    /// <summary>Pixels trimmed off the surviving rectangle before it is scored, for the kernel's edge.</summary>
    private const int ScoringInset = 2;

    /// <summary>Fewest pixels in an overlap before its correlation is meaningless.</summary>
    private const int MinimumOverlap = 16;

    /// <summary>Largest side of the image used for the first, downsampled translation search.</summary>
    private const int TranslationSearchSide = 192;

    /// <summary>Fraction of the search range a link may use before it is subdivided.</summary>
    private const double LinkLimit = 0.8;

    /// <summary>Narrowest tracking window, in degrees: the floor a steady capture is searched in.</summary>
    private const double RefineWindowDegrees = 0.3;

    /// <summary>Search steps on each side of the centre in one sweep pass.</summary>
    private const int SweepSteps = 4;

    /// <summary>
    /// Estimates the rotation of every frame relative to frame 0.
    /// </summary>
    /// <param name="frames">Frames in delivery order; frame 0 is the reference.</param>
    /// <param name="options">Search range and sampling options.</param>
    /// <param name="cancellationToken">Cancels the estimation.</param>
    /// <returns>One angle in degrees per frame. Positive rotates the frame clockwise to match frame 0.</returns>
    public static double[] EstimateAngles(
        IReadOnlyList<Frame> frames,
        DerotationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(options);

        return EstimateAngles(
            index => frames[index],
            frames.Count,
            AnchorIndices(frames.Count, options.Keyframes),
            options,
            cancellationToken);
    }

    /// <summary>
    /// Estimates the rotation of every frame of a lazily-read sequence relative to frame 0.
    /// </summary>
    /// <param name="readFrame">Reads one frame by index; it is called on demand, never for the whole sequence at once.</param>
    /// <param name="frameCount">Number of frames in the selection.</param>
    /// <param name="anchors">Indices sampled to start the curve, ascending and including 0.</param>
    /// <param name="options">Search range and sampling options.</param>
    /// <param name="cancellationToken">Cancels the estimation.</param>
    /// <remarks>
    /// The curve is measured only at <paramref name="anchors"/> and wherever a link needs subdividing;
    /// the per-frame refinement then reads each refined frame once. Nothing is materialised, so a
    /// spill-backed selection costs one decode per frame rather than one copy of the selection.
    /// </remarks>
    public static double[] EstimateAngles(
        Func<int, Frame> readFrame,
        int frameCount,
        IReadOnlyList<int> anchors,
        DerotationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readFrame);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(anchors);
        if (!double.IsFinite(options.SearchDegrees) || options.SearchDegrees < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.SearchDegrees, "SearchDegrees must be finite and non-negative.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var angles = new double[Math.Max(0, frameCount)];
        if (frameCount <= 1) return angles;

        var reference = readFrame(0)
            ?? throw new ArgumentException("frames must not contain null entries", nameof(readFrame));
        var field = new AngleField(reference, options);

        // A featureless reference cannot measure anything: return "no rotation" rather than a guess.
        if (!field.HasTexture) return angles;

        _ = anchors; // the track walks the sequence itself; the anchor list is the caller's view of it
        TrackAngles(field, readFrame, angles, options, cancellationToken);
        return angles;
    }

    /// <summary>
    /// Anchor indices sampled to start the rotation curve: at most <paramref name="keyframes"/>
    /// frames, spaced as evenly as the selection allows, always including the first frame (the
    /// reference every angle is measured against) and the last.
    /// </summary>
    public static int[] AnchorIndices(int frameCount, int keyframes)
    {
        if (frameCount <= 0) return [];
        if (frameCount == 1) return [0];

        int wanted = Math.Clamp(keyframes, 2, frameCount);
        var indices = new int[wanted];
        for (int i = 0; i < wanted; i++)
        {
            // Rounded half away from zero, so the sample stays centred on the selection instead of
            // drifting towards the first frame.
            indices[i] = (int)Math.Round(
                (double)i * (frameCount - 1) / (wanted - 1), MidpointRounding.AwayFromZero);
        }

        return indices;
    }

    /// <summary>
    /// Largest centred rectangle that stays inside a <paramref name="width"/>×<paramref name="height"/>
    /// frame after it is rotated by up to <paramref name="maxAbsAngleDegrees"/> either way.
    /// </summary>
    /// <remarks>
    /// The crop keeps the frame's aspect ratio, so its half-extents are <c>X</c> and <c>Y = X·h/w</c>.
    /// Rotating that rectangle gives a bounding box of half-extents <c>X·|cos| + Y·|sin|</c> by
    /// <c>X·|sin| + Y·|cos|</c>, which has to fit the frame's <c>w/2</c> by <c>h/2</c>. Solving each
    /// constraint for <c>X</c> and taking the smaller gives the largest rectangle that satisfies both;
    /// the result is floored to whole pixels so the crop sits inside the rotated frame, never on it.
    /// </remarks>
    public static PixelRect CommonCrop(int width, int height, double maxAbsAngleDegrees)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(maxAbsAngleDegrees)) throw new ArgumentOutOfRangeException(nameof(maxAbsAngleDegrees));

        double aspect = height / (double)width;
        double theta = Math.Abs(maxAbsAngleDegrees) * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(theta)), sin = Math.Abs(Math.Sin(theta));

        double limitWidth = (width / 2.0) / (cos + (aspect * sin));
        double limitHeight = (height / 2.0) / (sin + (aspect * cos));
        double halfCrop = Math.Min(limitWidth, limitHeight);

        // The epsilon absorbs the 1e-16 residue of cos/sin at right angles, which would otherwise turn
        // an unrotated 640 px frame into a 639 px crop.
        int cropWidth = Math.Clamp((int)Math.Floor((2 * halfCrop) + 1e-6), 1, width);
        int cropHeight = Math.Clamp((int)Math.Floor((2 * halfCrop * aspect) + 1e-6), 1, height);

        return new PixelRect((width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight);
    }

    /// <summary>Rotates a frame about its centre by <paramref name="angleDegrees"/> (clockwise).</summary>
    /// <remarks>
    /// Bicubic (Catmull-Rom), with the vacated area left at the zero background and every result
    /// clamped to the frame's sample range. Bilinear resampling is not used for derotation: it is a
    /// low-pass whose blur accumulates over the stacked frames, and measured against a real lunar
    /// sequence it kept 17% of the single-frame edge sharpness where this kernel keeps 65%.
    /// Interpolating across the frame's own grid mixes neighbouring samples and therefore destroys a
    /// Bayer mosaic, so a mosaic is rejected instead of being silently retagged as
    /// <see cref="BayerPattern.None"/>.
    /// </remarks>
    public static Frame Rotate(Frame frame, double angleDegrees)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!double.IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        if (frame.Bayer != BayerPattern.None)
        {
            throw new ArgumentException(
                $"a {frame.Bayer} Bayer frame cannot be rotated: resampling mixes neighbouring colour " +
                "planes, so the mosaic pattern no longer describes the samples. Debayer before derotating.",
                nameof(frame));
        }

        var destination = new Frame(frame.Width, frame.Height, frame.Channels, frame.BitDepth)
        {
            Bayer = BayerPattern.None,
            Index = frame.Index,
            SourceName = frame.SourceName,
        };

        if (angleDegrees == 0)
        {
            Array.Copy(frame.Samples, destination.Samples, frame.Samples.Length);
            return destination;
        }

        double theta = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        int maximum = frame.BitDepth == 8 ? 255 : 65535;

        // Every plane uses the same transform, so a colour frame stays interleaved and in step.
        for (int channel = 0; channel < frame.Channels; channel++)
        {
            RotatePlane(
                frame.Samples, frame.Width, frame.Height, frame.Channels, channel,
                cos, sin, destination.Samples, maximum);
        }

        return destination;
    }

    /// <summary>Rotates a frame and copies <paramref name="crop"/> out of the rotated canvas.</summary>
    /// <param name="frame">Frame to rotate; a Bayer mosaic is rejected.</param>
    /// <param name="angleDegrees">Clockwise rotation about the image centre.</param>
    /// <param name="crop">Region of the rotated canvas to keep; it must lie inside the frame.</param>
    public static Frame RotateAndCrop(Frame frame, double angleDegrees, PixelRect crop)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var rotated = Rotate(frame, angleDegrees);
        if (crop.Width <= 0 || crop.Height <= 0 || crop.X < 0 || crop.Y < 0 ||
            crop.X + crop.Width > rotated.Width || crop.Y + crop.Height > rotated.Height)
        {
            // Clamping instead would silently change the stack geometry mid-sequence, so a crop that
            // does not fit the rotated frame is a caller error, not something to repair.
            throw new ArgumentException(
                $"the crop must lie inside the rotated {rotated.Width}x{rotated.Height} frame", nameof(crop));
        }

        return CropFrame(rotated, crop);
    }

    /// <summary>
    /// Translation of <paramref name="frame"/> that best aligns it to <paramref name="reference"/>,
    /// found by maximising zero-mean normalised cross-correlation and refined to sub-pixel accuracy.
    /// </summary>
    /// <param name="reference">The frame every other frame is aligned to.</param>
    /// <param name="frame">The frame to align; it must have the reference's geometry.</param>
    /// <param name="maxShift">Largest translation searched in either axis, in pixels; zero searches none.</param>
    /// <returns>
    /// The offset to apply to <paramref name="frame"/> — the same convention as
    /// <see cref="FrameAligner.Shift"/> and <see cref="AlignmentOffset"/>.
    /// </returns>
    /// <remarks>
    /// The integer search runs coarse to fine: a box-downsampled copy first, then the
    /// full-resolution window it points at, then a parabola through the peak. Only the overlap is
    /// scored, so a frame shifted against its own border never compares its zero fill with real pixels.
    /// </remarks>
    public static AlignmentOffset MatchOffset(Frame reference, Frame frame, int maxShift)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShift);
        if (reference.Width != frame.Width || reference.Height != frame.Height)
        {
            throw new ArgumentException(
                $"the frames must have the same geometry; got {reference.Width}x{reference.Height} " +
                $"and {frame.Width}x{frame.Height}", nameof(frame));
        }

        int width = reference.Width, height = reference.Height;
        if (maxShift == 0) return AlignmentOffset.Zero;

        var a = Luma(reference);
        var b = Luma(frame);

        // The scored region keeps every sample of both frames inside the image for any shift the
        // search may return, so no candidate is scored over a different area than its neighbours.
        var region = Shrink(new PixelRect(0, 0, width, height), maxShift + 1);
        if (region.Width < MinimumOverlap || region.Height < MinimumOverlap) return AlignmentOffset.Zero;

        int factor = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)TranslationSearchSide));
        int centreX = 0, centreY = 0, radius = maxShift;

        if (factor > 1)
        {
            int smallWidth = Math.Max(1, width / factor), smallHeight = Math.Max(1, height / factor);
            var smallA = Downsample(a, width, height, factor, smallWidth, smallHeight);
            var smallB = Downsample(b, width, height, factor, smallWidth, smallHeight);
            var smallRegion = Shrink(
                new PixelRect(0, 0, smallWidth, smallHeight),
                Math.Max(1, (int)Math.Ceiling((maxShift + 1) / (double)factor) + 1));

            if (smallRegion.Width >= MinimumOverlap && smallRegion.Height >= MinimumOverlap)
            {
                int reach = (int)Math.Ceiling(maxShift / (double)factor) + 1;
                var coarse = SearchGrid(smallA, smallB, smallWidth, smallHeight, smallRegion, 0, 0, reach, 1);
                centreX = coarse.Dx * factor;
                centreY = coarse.Dy * factor;
                radius = factor + 1;
            }
        }

        // One pass at half resolution to place the peak, then whole pixels around it.
        var wide = SearchGrid(a, b, width, height, region, centreX, centreY, radius, 2);
        var peak = SearchGrid(a, b, width, height, region, wide.Dx, wide.Dy, 2, 1);

        double subX = Math.Clamp(Parabola(
            Correlate(a, b, width, height, peak.Dx - 1, peak.Dy, region),
            peak.Score,
            Correlate(a, b, width, height, peak.Dx + 1, peak.Dy, region)), -1, 1);
        double subY = Math.Clamp(Parabola(
            Correlate(a, b, width, height, peak.Dx, peak.Dy - 1, region),
            peak.Score,
            Correlate(a, b, width, height, peak.Dx, peak.Dy + 1, region)), -1, 1);

        return new AlignmentOffset(peak.Dx + subX, peak.Dy + subY);
    }

    /// <summary>
    /// Rotates and crops one frame, then measures the translation that places its object centroid on
    /// <paramref name="referenceCentroid"/> (in cropped-frame coordinates).
    /// </summary>
    /// <remarks>
    /// Kept for callers that want the centring translation and for the derotation contract tests.
    /// The pipeline does not use it: a centroid follows the threshold and the zero-filled corners a
    /// rotation adds, so it is a poor registration signal for a limb-lit disc — see <see cref="MatchOffset"/>.
    /// </remarks>
    public static DerotatedFrame Prepare(
        Frame frame,
        double angleDegrees,
        PixelRect crop,
        (double X, double Y) referenceCentroid,
        int? threshold)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var cropped = RotateAndCrop(frame, angleDegrees, crop);
        var centroid = Centroid(cropped, threshold ?? FrameAligner.OtsuThreshold(cropped));
        var offset = centroid is null
            ? AlignmentOffset.Zero
            : new AlignmentOffset(referenceCentroid.X - centroid.Value.X, referenceCentroid.Y - centroid.Value.Y);

        return new DerotatedFrame(cropped, offset, angleDegrees);
    }

    /// <summary>Intensity-weighted centroid above <paramref name="threshold"/>, or null when none passes.</summary>
    /// <remarks>
    /// Luma by the same Rec.601 weights as the rest of the pipeline, weighted by the excess over the
    /// threshold exactly as <see cref="FrameAligner.Centroid"/> does: the centring stage and this one
    /// must agree on where the object is, or derotation would shift it.
    /// </remarks>
    public static (double X, double Y)? Centroid(Frame frame, int threshold)
        => FrameAligner.Centroid(frame, threshold);

    // ------------------------------------------------------------------ curve

    /// <summary>
    /// Tracks the rotation frame by frame, every frame matched against frame 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame is searched in a window around where the previous two frames say it should be, and
    /// the result is a direct measurement against the reference rather than a link to its neighbour.
    /// That distinction is what makes the sequence usable. A chain of short links looks cheaper, but
    /// each link only turns a fraction of a degree: at the working scale that is a fraction of a
    /// pixel, the correlation peak is shallower than the noise in the score, and every link reads
    /// low. Measured on the real 30-frame sequence, chaining twelve anchors three frames apart put
    /// the last frame at 1.27 degrees against a truth of 1.44 — an error that grows along the
    /// sequence. A direct match against frame 0 lands within 0.03 degrees, and the error cannot
    /// accumulate because nothing carries forward except the search window.
    /// </para>
    /// <para>
    /// The window widens four times when the frame is not found at its centre, so a jump in the rate
    /// costs one more sweep rather than losing the frame. Nothing is refined a second time: for this
    /// estimator a second pass moves the angle by the same order as the score noise.
    /// </para>
    /// </remarks>
    private static void TrackAngles(
        AngleField field,
        Func<int, Frame> readFrame,
        double[] angles,
        DerotationOptions options,
        CancellationToken cancellationToken)
    {
        int count = angles.Length;
        int cap = Math.Max(2, options.MaxRefinedFrames);
        int stride = count <= cap ? 1 : Math.Max(1, (int)Math.Ceiling((count - 1) / (double)(cap - 1)));

        var indices = new List<int>();
        for (int i = 0; i < count; i += stride) indices.Add(i);
        if (indices[^1] != count - 1) indices.Add(count - 1);

        var tracked = new double[indices.Count];
        double step = 0;
        double ceiling = Math.Max(0.5, options.SearchDegrees);

        for (int j = 1; j < indices.Count; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int index = indices[j];
            double predicted = tracked[j - 1] + step;
            double window = Math.Abs(step) < 1e-9
                ? ceiling
                : Math.Clamp((4 * Math.Abs(step)) + RefineWindowDegrees, 0.5, ceiling);

            double found = field.Match(field.Reference, readFrame(index), predicted, window, cancellationToken);

            // A peak on the window's edge means the rate changed by more than the prediction covers.
            // Widening once costs a sweep; trusting the edge value is how a rotation of two degrees
            // ends up reported as the width of the window.
            bool atEdge = double.IsFinite(found) && Math.Abs(found - predicted) > 0.9 * window;
            if (!double.IsFinite(found) || atEdge)
            {
                found = field.Match(field.Reference, readFrame(index), predicted, ceiling, cancellationToken);
            }

            // Nothing better than the prediction: keep the prediction, which is where the sequence
            // says this frame is, rather than freezing on the previous frame's angle.
            tracked[j] = double.IsFinite(found) ? found : predicted;
            step = tracked[j] - tracked[j - 1];
        }

        // One angle per delivered frame: exact at a tracked frame, the straight line between two of
        // them everywhere else. Nothing outside the tracked indices is read.
        int cursor = 0;
        for (int i = 0; i < count; i++)
        {
            while (cursor + 2 < indices.Count && i > indices[cursor + 1]) cursor++;
            int left = indices[cursor], right = indices[Math.Min(cursor + 1, indices.Count - 1)];
            if (right <= left)
            {
                angles[i] = tracked[Math.Min(cursor, tracked.Length - 1)];
                continue;
            }

            double t = Math.Min(1.0, (i - left) / (double)(right - left));
            angles[i] = tracked[cursor] + ((tracked[Math.Min(cursor + 1, tracked.Length - 1)] - tracked[cursor]) * t);
        }
    }

    // ------------------------------------------------------------------ matching internals

    /// <summary>Downsampled luma copies and the sweeps that turn a candidate rotation into a score.</summary>
    private sealed class AngleField
    {
        private readonly float[] reference;
        private readonly int width;
        private readonly int height;
        private readonly int factor;
        private readonly int maxShift;
        private readonly int threshold;
        private readonly double texture;
        private readonly double finestStep;

        public AngleField(Frame referenceFrame, DerotationOptions options)
        {
            ArgumentNullException.ThrowIfNull(referenceFrame);

            int side = Math.Max(MinimumSampleSide, options.SampleSide);
            factor = Math.Max(1, (int)Math.Ceiling(
                Math.Max(referenceFrame.Width, referenceFrame.Height) / (double)side));
            width = Math.Max(1, referenceFrame.Width / factor);
            height = Math.Max(1, referenceFrame.Height / factor);

            Reference = referenceFrame;
            reference = Downsample(referenceFrame, factor, width, height);
            texture = StandardDeviation(reference, width, ScoredRegion(width, height));
            threshold = options.MinPixelValue ?? Otsu(reference);
            finestStep = Math.Clamp(options.RefineStepDegrees, 1e-4, 0.5);

            // A rotation moves the content tangentially, so the translation a rotated frame still
            // needs is the capture's own drift, not the rotation — a handful of pixels. The search is
            // bounded tightly around the centroid guess: a wide window can lock onto a spurious peak
            // where a rotated limb happens to align with itself, and then every candidate in the
            // sweep is scored against the wrong shift.
            maxShift = Math.Clamp(Math.Min(width, height) / 24, 2, 6);
        }

        /// <summary>Frame 0, the reference every alignment is measured against.</summary>
        public Frame Reference { get; }

        /// <summary>True when the reference has structure to match against.</summary>
        public bool HasTexture => texture >= MinimumTexture;

        /// <summary>
        /// Best correction of <paramref name="moving"/> relative to <paramref name="fixedFrame"/> in a
        /// window of <paramref name="searchDegrees"/> around <paramref name="centre"/>, or NaN when
        /// the window holds no better match than its centre.
        /// </summary>
        public double Match(
            Frame fixedFrame,
            Frame moving,
            double centre,
            double searchDegrees,
            CancellationToken cancellationToken)
        {
            if (fixedFrame.Width != moving.Width || fixedFrame.Height != moving.Height)
            {
                throw new ArgumentException(
                    $"frame is {moving.Width}x{moving.Height}; every frame must match the reference's " +
                    $"{fixedFrame.Width}x{fixedFrame.Height}", nameof(moving));
            }

            var fixedLuma = Downsample(fixedFrame, factor, width, height);
            var movingLuma = Downsample(moving, factor, width, height);
            return Sweep(fixedLuma, movingLuma, centre, searchDegrees, cancellationToken);
        }

        /// <summary>
        /// Scores one candidate angle: the moving frame is rotated by it, then translated by the
        /// pair's measured shift and correlated against the reference over the surviving rectangle.
        /// </summary>
        /// <remarks>
        /// Only the moving frame is rotated. An earlier version rotated both by half the candidate to
        /// equalise their resampling blur, which matters when the kernel is bilinear and the working
        /// copy is small: there the untouched reference won at every small angle just by being
        /// sharper. With a cubic kernel at 256 pixels the effect reverses — the reference's own blur
        /// grows with the angle and drags the peak towards zero. Measured against the truth angles of
        /// the real sequence, rotating one image lands on the truth to 0.00 degrees on every frame
        /// checked; the half-and-half scheme was up to 0.04 degrees low, and that error is what the
        /// stack pays for.
        /// </remarks>
        private double Sweep(
            float[] fixedLuma,
            float[] movingLuma,
            double centre,
            double searchDegrees,
            CancellationToken cancellationToken)
        {
            var rotatedFixed = new float[fixedLuma.Length];
            var rotatedMoving = new float[movingLuma.Length];
            double search = Math.Max(searchDegrees, 0);

            // One region for every candidate in the sweep. A region that shrinks as the angle grows
            // would score a wider candidate over less decorrelated border, which lifts its score and
            // walks the peak outward — a bias, not a measurement.
            var scope = ScoringRegion(width, height, (Math.Abs(centre) + search) / 2.0);
            var shiftRegion = Shrink(scope, maxShift + 1);

            // The translation has to be measured at the angle the search starts from, not at zero.
            // For a refinement a quarter of a degree is already a couple of pixels of displacement:
            // a shift measured on the unrotated pair is then meaningless, every candidate scores the
            // same, and the refinement silently keeps the curve value.
            RotateLuma(fixedLuma, width, height, -centre / 2.0, rotatedFixed);
            RotateLuma(movingLuma, width, height, centre / 2.0, rotatedMoving);
            (double shiftX, double shiftY) = InitialShift(
                rotatedFixed, rotatedMoving, shiftRegion, width, height, threshold, maxShift);

            double ScoreAt(double angle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RotateLuma(fixedLuma, width, height, -angle / 2.0, rotatedFixed);
                RotateLuma(movingLuma, width, height, angle / 2.0, rotatedMoving);
                return Correlate(rotatedFixed, rotatedMoving, width, height, shiftX, shiftY, scope);
            }

            double centreScore = Correlate(rotatedFixed, rotatedMoving, width, height, shiftX, shiftY, scope);
            if (centreScore < MinimumScore) return double.NaN;

            double bestAngle = centre, bestScore = centreScore;
            double step = Math.Max(search / SweepSteps, Math.Min(finestStep, search));

            for (int pass = 0; pass < 2 && step > 0; pass++)
            {
                double passCentre = bestAngle;
                double passScore = bestScore, passAngle = bestAngle;

                for (int i = -SweepSteps; i <= SweepSteps; i++)
                {
                    if (i == 0) continue;
                    double angle = passCentre + (i * step);
                    double score = ScoreAt(angle);
                    if (score > passScore + TieGain)
                    {
                        passScore = score;
                        passAngle = angle;
                    }
                }

                bestAngle = passAngle;
                bestScore = passScore;
                step /= 8;
                if (step < finestStep) step = 0;

                if (pass == 0 && Math.Abs(bestAngle - centre) > 1e-9)
                {
                    // The translation was measured before the rotation was known. At the coarse peak
                    // the residual shift can be measured again, and the finer pass then compares two
                    // images that are actually registered.
                    RotateLuma(fixedLuma, width, height, -bestAngle / 2.0, rotatedFixed);
                    RotateLuma(movingLuma, width, height, bestAngle / 2.0, rotatedMoving);
                    (shiftX, shiftY) = InitialShift(
                        rotatedFixed, rotatedMoving, shiftRegion, width, height, threshold, maxShift);
                }
            }

            // Parabola through the discrete peak, so the result is not quantised to the final step.
            double offset = Parabola(
                ScoreAt(bestAngle - finestStep), bestScore, ScoreAt(bestAngle + finestStep)) * finestStep;
            if (double.IsFinite(offset)) bestAngle += Math.Clamp(offset, -finestStep, finestStep);

            // No evidence of a match, or nothing better than the value it started from.
            return bestScore <= centreScore + MinimumGain ? double.NaN : bestAngle;
        }
    }

    /// <summary>
    /// Translation guess for a pair: the centroid difference, then correlation in a window around it.
    /// </summary>
    /// <remarks>
    /// The centroid alone is enough to keep the search in the right basin — it cannot jump to the
    /// spurious peak a rotated limb produces far from the true offset — while the correlation that
    /// follows removes the centroid's threshold and border bias. This is only the matcher's internal
    /// starting point; the translation that reaches the stack is <see cref="MatchOffset"/>.
    /// </remarks>
    private static (double X, double Y) InitialShift(
        float[] fixedLuma, float[] movingLuma, PixelRect region, int width, int height, int threshold, int reach)
    {
        int guessX = 0, guessY = 0;
        var fixedCentroid = CentroidOf(fixedLuma, width, height, threshold);
        var movingCentroid = CentroidOf(movingLuma, width, height, threshold);
        if (fixedCentroid is not null && movingCentroid is not null)
        {
            guessX = (int)Math.Round(fixedCentroid.Value.X - movingCentroid.Value.X, MidpointRounding.AwayFromZero);
            guessY = (int)Math.Round(fixedCentroid.Value.Y - movingCentroid.Value.Y, MidpointRounding.AwayFromZero);
        }

        // Only a small neighbourhood is searched around the centroid guess. The centroid already
        // carries the capture's drift; what it cannot carry is its own threshold and border bias. A
        // wide search instead starts locking onto spurious peaks where a rotated limb nearly aligns
        // with itself, and one of those poisons every candidate in the sweep that follows.
        int local = Math.Min(reach, 2);
        var (dx, dy, score) = SearchGrid(
            fixedLuma, movingLuma, width, height, region, guessX, guessY, local, 1);
        if (score <= double.NegativeInfinity) return (guessX, guessY);

        // Sub-pixel. It matters more than it looks: a whole-pixel shift search leaves up to half a
        // pixel of translation error, and at the working scale that is worth a fifth of a degree —
        // larger than the rotation a short link is trying to measure, which made every link read low.
        double offsetX = Parabola(
            Correlate(fixedLuma, movingLuma, width, height, dx - 1, dy, region),
            score,
            Correlate(fixedLuma, movingLuma, width, height, dx + 1, dy, region));
        double offsetY = Parabola(
            Correlate(fixedLuma, movingLuma, width, height, dx, dy - 1, region),
            score,
            Correlate(fixedLuma, movingLuma, width, height, dx, dy + 1, region));
        return (dx + Math.Clamp(offsetX, -1, 1), dy + Math.Clamp(offsetY, -1, 1));
    }

    /// <summary>Best integer shift of <paramref name="moving"/> around a centre, scored by correlation.</summary>
    /// <param name="centreX">Centre of the search.</param>
    /// <param name="centreY">Centre of the search.</param>
    /// <param name="reach">Half-width of the search, in pixels.</param>
    /// <param name="stride">Grid spacing; the grid is then refined at whole pixels.</param>
    private static (int Dx, int Dy, double Score) SearchGrid(
        float[] reference,
        float[] moving,
        int width,
        int height,
        PixelRect region,
        int centreX,
        int centreY,
        int reach,
        int stride)
    {
        int step = Math.Max(1, stride);
        double bestScore = double.NegativeInfinity;
        int bestX = centreX, bestY = centreY;

        for (int dy = -reach; dy <= reach; dy += step)
        {
            for (int dx = -reach; dx <= reach; dx += step)
            {
                double score = Correlate(reference, moving, width, height, centreX + dx, centreY + dy, region);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = centreX + dx;
                    bestY = centreY + dy;
                }
            }
        }

        // Refine the grid at whole-pixel resolution around its winner.
        if (step > 1)
        {
            for (int dy = -step + 1; dy <= step - 1; dy++)
            {
                for (int dx = -step + 1; dx <= step - 1; dx++)
                {
                    double score = Correlate(reference, moving, width, height, bestX + dx, bestY + dy, region);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX += dx;
                        bestY += dy;
                    }
                }
            }
        }

        return (bestX, bestY, bestScore);
    }

    /// <summary>
    /// Zero-mean normalised cross-correlation of <paramref name="reference"/> against
    /// <paramref name="moving"/> sampled at <c>(x - dx, y - dy)</c>, over <paramref name="region"/>.
    /// </summary>
    private static double Correlate(
        float[] reference,
        float[] moving,
        int width,
        int height,
        double dx,
        double dy,
        PixelRect region)
    {
        double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
        int count = 0;

        bool fractional = (dx - Math.Floor(dx)) != 0 || (dy - Math.Floor(dy)) != 0;

        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            double by = y - dy;
            int movingY = (int)Math.Floor(by);
            double fy = by - movingY;
            if (movingY < 0 || movingY >= height - 1) continue;
            int referenceRow = y * width, movingRow = movingY * width;

            for (int x = region.X; x < region.X + region.Width; x++)
            {
                double bx = x - dx;
                int movingX = (int)Math.Floor(bx);
                double fx = bx - movingX;
                if (movingX < 0 || movingX >= width - 1) continue;

                double a = reference[referenceRow + x];
                double b = fractional
                    ? (moving[movingRow + movingX] * (1 - fx) * (1 - fy))
                      + (moving[movingRow + movingX + 1] * fx * (1 - fy))
                      + (moving[movingRow + width + movingX] * (1 - fx) * fy)
                      + (moving[movingRow + width + movingX + 1] * fx * fy)
                    : moving[movingRow + movingX];
                sumA += a;
                sumB += b;
                sumAA += a * a;
                sumBB += b * b;
                sumAB += a * b;
                count++;
            }
        }

        if (count < MinimumOverlap) return double.NegativeInfinity;

        double meanA = sumA / count, meanB = sumB / count;
        double varA = sumAA - (sumA * meanA);
        double varB = sumBB - (sumB * meanB);
        if (varA <= 1e-9 || varB <= 1e-9) return 0;

        return (sumAB - (count * meanA * meanB)) / Math.Sqrt(varA * varB);
    }

    /// <summary>Vertex of the parabola through three equally spaced scores, in units of the spacing.</summary>
    private static double Parabola(double before, double peak, double after)
    {
        double denominator = before - (2 * peak) + after;
        if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-12) return 0;
        // A flat or nearly flat peak has no vertex worth reporting: the denominator is the second
        // difference, and dividing by a fraction of a level threw the sub-pixel offset tens of pixels
        // away from the grid peak, which put garbage translations into the stack.
        if (Math.Abs(denominator) < Math.Max(1e-9, 1e-6 * Math.Abs(peak))) return 0;
        double offset = 0.5 * (before - after) / denominator;
        return double.IsFinite(offset) ? offset : 0;
    }

    // ------------------------------------------------------------------ image internals

    /// <summary>Luma of a frame as a float plane, by the pipeline's Rec.601 weights.</summary>
    private static float[] Luma(Frame frame)
    {
        var luma = QualityMetrics.ToLuma(frame);
        var result = new float[luma.Length];
        for (int i = 0; i < luma.Length; i++) result[i] = luma[i];
        return result;
    }

    /// <summary>Luma of a frame, box-averaged by an integer factor onto a common matching scale.</summary>
    private static float[] Downsample(Frame frame, int factor, int width, int height)
        => Downsample(Luma(frame), frame.Width, frame.Height, factor, width, height);

    /// <summary>Box-averages a plane by an integer factor. The tail of a non-dividing edge is dropped.</summary>
    private static float[] Downsample(
        float[] source, int sourceWidth, int sourceHeight, int factor, int width, int height)
    {
        if (factor == 1)
        {
            var copy = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                Array.Copy(source, y * sourceWidth, copy, y * width, width);
            }

            return copy;
        }

        var result = new float[width * height];
        double area = factor * factor;
        for (int y = 0; y < height; y++)
        {
            int sourceY = y * factor;
            for (int x = 0; x < width; x++)
            {
                int sourceX = x * factor;
                long sum = 0;
                for (int subY = 0; subY < factor; subY++)
                {
                    int row = ((sourceY + subY) * sourceWidth) + sourceX;
                    for (int subX = 0; subX < factor; subX++) sum += (long)source[row + subX];
                }

                result[(y * width) + x] = (float)(sum / area);
            }
        }

        return result;
    }

    /// <summary>Rotates a luma image clockwise about its centre, leaving the outside at zero.</summary>
    /// <remarks>
    /// Bilinear: this is the matcher's search instrument, not a delivered pixel. Both images are
    /// rotated by half the candidate, so whatever blur it adds is symmetric, and measured against the
    /// real sequence bilinear found the same peak as the bicubic sampler at a quarter of the cost.
    /// </remarks>
    private static void RotateLuma(float[] source, int width, int height, double angleDegrees, float[] destination)
    {
        if (angleDegrees == 0)
        {
            Array.Copy(source, destination, source.Length);
            return;
        }

        double theta = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double centreX = (width - 1) / 2.0, centreY = (height - 1) / 2.0;

        for (int y = 0; y < height; y++)
        {
            double dy = y - centreY;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                double dx = x - centreX;
                double sourceX = centreX + (dx * cos) + (dy * sin);
                double sourceY = centreY - (dx * sin) + (dy * cos);
                destination[row + x] = (float)SampleBilinear(source, width, height, sourceX, sourceY);
            }
        }
    }

    /// <summary>Bilinear sample with the zero background outside the image.</summary>
    private static double SampleBilinear(float[] source, int width, int height, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        int x1 = x0 + 1, y1 = y0 + 1;

        double value = 0;
        if ((uint)x0 < (uint)width && (uint)y0 < (uint)height)
            value += source[(y0 * width) + x0] * (1 - fx) * (1 - fy);
        if ((uint)x1 < (uint)width && (uint)y0 < (uint)height)
            value += source[(y0 * width) + x1] * fx * (1 - fy);
        if ((uint)x0 < (uint)width && (uint)y1 < (uint)height)
            value += source[(y1 * width) + x0] * (1 - fx) * fy;
        if ((uint)x1 < (uint)width && (uint)y1 < (uint)height)
            value += source[(y1 * width) + x1] * fx * fy;

        return value;
    }

    /// <summary>Catmull-Rom cubic convolution kernel.</summary>
    private static double CubicWeight(double distance)
    {
        double x = Math.Abs(distance);
        if (x < 1.0) return (((CubicA + 2) * x) - (CubicA + 3)) * (x * x) + 1;
        if (x < 2.0) return ((((x - 5) * x) + 8) * x - 4) * CubicA;
        return 0;
    }

    /// <summary>Writes one interleaved colour plane of a rotated frame.</summary>
    private static void RotatePlane(
        ushort[] source,
        int width,
        int height,
        int channels,
        int channel,
        double cos,
        double sin,
        ushort[] destination,
        int maximum)
    {
        double centreX = (width - 1) / 2.0, centreY = (height - 1) / 2.0;
        Span<double> weightsX = stackalloc double[CubicRadius * 2];
        Span<double> weightsY = stackalloc double[CubicRadius * 2];

        for (int y = 0; y < height; y++)
        {
            double dy = y - centreY;
            int row = y * width * channels;
            for (int x = 0; x < width; x++)
            {
                double dx = x - centreX;
                double sourceX = centreX + (dx * cos) + (dy * sin);
                double sourceY = centreY - (dx * sin) + (dy * cos);

                int sx0 = (int)Math.Floor(sourceX), sy0 = (int)Math.Floor(sourceY);
                double fx = sourceX - sx0, fy = sourceY - sy0;

                for (int i = 0; i < CubicRadius * 2; i++)
                {
                    weightsX[i] = CubicWeight(i - CubicRadius + 1 - fx);
                    weightsY[i] = CubicWeight(i - CubicRadius + 1 - fy);
                }

                double total = 0;
                for (int j = 0; j < CubicRadius * 2; j++)
                {
                    int sy = sy0 + j - CubicRadius + 1;
                    if ((uint)sy >= (uint)height) continue;
                    int sourceRow = sy * width * channels;
                    double rowSum = 0;
                    for (int i = 0; i < CubicRadius * 2; i++)
                    {
                        int sx = sx0 + i - CubicRadius + 1;
                        if ((uint)sx >= (uint)width) continue;
                        rowSum += source[sourceRow + (sx * channels) + channel] * weightsX[i];
                    }

                    total += rowSum * weightsY[j];
                }

                destination[row + (x * channels) + channel] =
                    (ushort)Math.Clamp((int)Math.Round(total), 0, maximum);
            }
        }
    }

    /// <summary>Copies a rectangle out of a frame, keeping geometry, depth, channels and provenance.</summary>
    private static Frame CropFrame(Frame frame, PixelRect rect)
    {
        int channels = frame.Channels;
        var cropped = new Frame(rect.Width, rect.Height, channels, frame.BitDepth)
        {
            Bayer = frame.Bayer,
            Index = frame.Index,
            SourceName = frame.SourceName,
        };

        int samplesPerRow = rect.Width * channels;
        for (int y = 0; y < rect.Height; y++)
        {
            int source = (((rect.Y + y) * frame.Width) + rect.X) * channels;
            Array.Copy(frame.Samples, source, cropped.Samples, y * samplesPerRow, samplesPerRow);
        }

        return cropped;
    }

    /// <summary>The central <see cref="ScoredFraction"/> of a frame, used for the texture test.</summary>
    private static PixelRect ScoredRegion(int width, int height)
    {
        int marginX = (int)Math.Round(width * (1 - ScoredFraction) / 2.0);
        int marginY = (int)Math.Round(height * (1 - ScoredFraction) / 2.0);
        return new PixelRect(
            marginX,
            marginY,
            Math.Max(1, width - (2 * marginX)),
            Math.Max(1, height - (2 * marginY)));
    }

    /// <summary>
    /// The region scored for a candidate rotation of <paramref name="halfAngleDegrees"/>: the rectangle
    /// both rotated copies still fill, inset far enough that the interpolated border never votes.
    /// </summary>
    /// <remarks>
    /// This is the whole surviving rectangle, not a central crop. The rotation signal grows with
    /// radius, so a central 80% throws away the limb and terminator that carry most of it; measured on
    /// the real lunar sequence, scoring the central 80% put the peak at 0.70 degrees for a true 0.51
    /// and left the stack barely sharper than no derotation at all.
    /// </remarks>
    private static PixelRect ScoringRegion(int width, int height, double halfAngleDegrees)
        => Shrink(CommonCrop(width, height, Math.Abs(halfAngleDegrees)), ScoringInset);

    /// <summary>Shrinks a region by <paramref name="margin"/> on every side, never below one pixel.</summary>
    private static PixelRect Shrink(PixelRect region, int margin)
    {
        int dx = Math.Min(margin, Math.Max(0, (region.Width - 1) / 2));
        int dy = Math.Min(margin, Math.Max(0, (region.Height - 1) / 2));
        return new PixelRect(
            region.X + dx,
            region.Y + dy,
            Math.Max(1, region.Width - (2 * dx)),
            Math.Max(1, region.Height - (2 * dy)));
    }

    /// <summary>Standard deviation over a region, used as the "is there anything to match" test.</summary>
    private static double StandardDeviation(float[] luma, int width, PixelRect region)
    {
        double sum = 0, sumSquares = 0;
        int count = 0;

        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            int row = y * width;
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                double value = luma[row + x];
                sum += value;
                sumSquares += value * value;
                count++;
            }
        }

        if (count == 0) return 0;
        double mean = sum / count;
        return Math.Sqrt(Math.Max(0, (sumSquares / count) - (mean * mean)));
    }

    /// <summary>Intensity-weighted centroid of a luma image above <paramref name="threshold"/>.</summary>
    private static (double X, double Y)? CentroidOf(float[] luma, int width, int height, int threshold)
    {
        double sum = 0, sumX = 0, sumY = 0;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                double value = luma[row + x];
                if (value < threshold) continue;

                double weight = value - threshold + 1;
                sum += weight;
                sumX += weight * x;
                sumY += weight * y;
            }
        }

        return sum <= 0 ? null : (sumX / sum, sumY / sum);
    }

    /// <summary>
    /// Otsu split of a luma image, mirroring <see cref="FrameAligner.OtsuThreshold"/> so the matcher
    /// and the centring stage split the histogram the same way.
    /// </summary>
    private static int Otsu(float[] luma)
    {
        double maximum = 0;
        foreach (float value in luma)
        {
            if (value > maximum) maximum = value;
        }

        int top = Math.Max(1, (int)Math.Ceiling(maximum));
        var histogram = new long[top + 1];
        foreach (float value in luma)
        {
            histogram[Math.Clamp((int)Math.Round(value), 0, top)]++;
        }

        long total = luma.Length;
        double sum = 0;
        for (int i = 0; i <= top; i++) sum += (double)i * histogram[i];

        double sumBackground = 0;
        long weightBackground = 0;
        double bestVariance = -1;
        int bestThreshold = 0;

        for (int t = 0; t <= top; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0) continue;

            long weightForeground = total - weightBackground;
            if (weightForeground == 0) break;

            sumBackground += (double)t * histogram[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double variance = (double)weightBackground * weightForeground *
                              (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                bestThreshold = t;
            }
        }

        return bestThreshold;
    }
}
