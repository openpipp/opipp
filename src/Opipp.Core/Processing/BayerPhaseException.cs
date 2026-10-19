namespace Opipp.Core.Processing;

/// <summary>
/// A single output container (SER/AVI) was asked to hold frames whose Bayer mosaic patterns differ,
/// so no one header can describe the sequence.
/// </summary>
/// <remarks>
/// A CFA pattern is a property of the whole sequence: SER and AVI store one colour id in the file
/// header, and FITS/PNG/TIFF per-frame outputs do not have this problem because every frame carries
/// its own header. Two things can make the patterns differ between frames of one run:
/// per-frame alignment shifts (an odd whole-pixel shift moves the phase, so frames shifted by an
/// odd and an even number of pixels disagree) and joining inputs whose headers declare different
/// patterns. Averaging or debayering such a sequence would mix colour planes at the same site, and
/// writing it would label half the frames with a pattern they do not have, so the run stops here
/// instead. This is deliberately not an <see cref="ArgumentException"/> or
/// <see cref="InvalidOperationException"/>: the per-frame processing loop catches those and records
/// the frame as "no object", which would hide a whole-run inconsistency.
/// </remarks>
public sealed class BayerPhaseException : Exception
{
    public BayerPhaseException(string message) : base(message) { }
}
