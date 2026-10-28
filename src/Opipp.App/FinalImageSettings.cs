#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PIPP_Application;

/// <summary>How the selected frames of an input are combined into the one final image.</summary>
public enum FinalImageMethod
{
    /// <summary>Arithmetic mean of every contributing frame.</summary>
    Mean = 0,

    /// <summary>Sum of every contributing frame, clamped at the output depth.</summary>
    Sum = 1,

    /// <summary>Per-pixel median of every contributing frame.</summary>
    Median = 2,
}

/// <summary>Which pixels the final image covers once the frames have been aligned.</summary>
public enum FinalImageCoverage
{
    /// <summary>Output only the region every contributing frame can fill.</summary>
    CropToOverlap = 0,

    /// <summary>Keep the first frame's canvas and leave pixels without a contributor unpainted.</summary>
    KeepCanvas = 1,
}

/// <summary>
/// The operator's final-image integration choice, and the engine switches that carry it.
/// </summary>
/// <remarks>
/// Disabled is the default and emits no switches, so a run whose operator never opened the dialog
/// asks the engine for exactly the per-input output files it produced before this control existed.
/// The three switch names and value spellings are the engine's contract; <see cref="ToSwitches"/>
/// is the single place that produces them.
/// </remarks>
public sealed class FinalImageSettings
{
    /// <summary>When false no integration is requested and <see cref="ToSwitches"/> is empty.</summary>
    public bool Enabled { get; set; }

    /// <summary>Combination method; mean by default.</summary>
    public FinalImageMethod Method { get; set; } = FinalImageMethod.Mean;

    /// <summary>Border policy; crop to the region every frame covers by default.</summary>
    public FinalImageCoverage Coverage { get; set; } = FinalImageCoverage.CropToOverlap;

    /// <summary>Apply the fractional part of each alignment offset with bilinear resampling.</summary>
    public bool Subpixel { get; set; } = true;

    /// <summary>
    /// Correct field rotation as well as translation while integrating. Off by default: the
    /// rotation search costs time and only helps a capture whose field actually rotates.
    /// </summary>
    public bool Derotate { get; set; }

    /// <summary>
    /// Largest rotation, in degrees either side of the reference frame, that the derotation search
    /// considers. Clamped to <see cref="MinDerotateSearchDegrees"/>..<see cref="MaxDerotateSearchDegrees"/>
    /// by the dialog; <see cref="ToSwitches"/> refuses a value outside that range.
    /// </summary>
    public double DerotateSearchDegrees { get; set; } = DefaultDerotateSearchDegrees;

    /// <summary>Smallest rotation range the engine accepts, in degrees.</summary>
    public const double MinDerotateSearchDegrees = 0.1;

    /// <summary>Largest rotation range the engine accepts, in degrees.</summary>
    public const double MaxDerotateSearchDegrees = 45.0;

    /// <summary>The rotation range assumed when the operator never changed it, in degrees.</summary>
    public const double DefaultDerotateSearchDegrees = 5.0;

    /// <summary>
    /// The exact engine switches for the current choice, in method/coverage/subpixel order, followed
    /// by the derotation switches when derotation is on.
    /// </summary>
    /// <returns>Three switches when enabled, plus two more when derotation is on; an empty list when
    /// disabled.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A method or coverage value is not one the
    /// engine accepts, or the derotation search range is outside 0.1..45 degrees. An unknown value
    /// must be rejected, not silently mapped onto a default.</exception>
    public IReadOnlyList<string> ToSwitches()
    {
        if (!Enabled)
        {
            return Array.Empty<string>();
        }

        var switches = new List<string>
        {
            "-stack=" + MethodToken(Method),
            "-stack_coverage=" + CoverageToken(Coverage),
            "-stack_subpixel=" + (Subpixel ? "on" : "off"),
        };

        if (Derotate)
        {
            if (!IsValidDerotateSearch(DerotateSearchDegrees))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DerotateSearchDegrees),
                    DerotateSearchDegrees,
                    "derotation search must be " + MinDerotateSearchDegrees + ".." + MaxDerotateSearchDegrees + " degrees");
            }

            switches.Add("-derotate=on");
            switches.Add("-derotate_search=" + FormatDerotateSearch(DerotateSearchDegrees));
        }

        return switches;
    }

    /// <summary>
    /// One short line naming the method, the coverage, the subpixel state and, when it is on, the
    /// derotation range, e.g. <c>mean, crop to overlap, subpixel on, derotate 5.0 deg</c>.
    /// <c>off</c> while integration is disabled.
    /// </summary>
    public string Summary => Enabled
        ? MethodToken(Method) + ", " + CoverageLabel(Coverage) + ", subpixel " + (Subpixel ? "on" : "off")
            + (Derotate ? ", derotate " + FormatDerotateSearch(DerotateSearchDegrees) + " deg" : string.Empty)
        : "off";

    /// <summary>The engine's lowercase spelling of a method.</summary>
    public static string MethodToken(FinalImageMethod method) => method switch
    {
        FinalImageMethod.Mean => "mean",
        FinalImageMethod.Sum => "sum",
        FinalImageMethod.Median => "median",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unknown final-image method"),
    };

    /// <summary>
    /// Reads the method token an options file stores. Case and surrounding quotes/space are
    /// ignored; an absent or unrecognised token is refused so the caller can report it rather than
    /// silently running a different method than the file named.
    /// </summary>
    /// <returns>True only for <c>mean</c>, <c>sum</c> or <c>median</c>.</returns>
    public static bool TryParseMethod(string? token, out FinalImageMethod method)
    {
        switch (Normalize(token))
        {
            case "mean":
                method = FinalImageMethod.Mean;
                return true;
            case "sum":
                method = FinalImageMethod.Sum;
                return true;
            case "median":
                method = FinalImageMethod.Median;
                return true;
            default:
                method = FinalImageMethod.Mean;
                return false;
        }
    }

    /// <summary>The engine's spelling of a coverage policy.</summary>
    public static string CoverageToken(FinalImageCoverage coverage) => coverage switch
    {
        FinalImageCoverage.CropToOverlap => "crop",
        FinalImageCoverage.KeepCanvas => "canvas",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage), coverage, "unknown final-image coverage"),
    };

    /// <summary>
    /// Reads the coverage token an options file stores (<c>crop</c> or <c>canvas</c>), with the
    /// same tolerance and the same refusal of an unknown value as <see cref="TryParseMethod"/>.
    /// </summary>
    /// <returns>True only for <c>crop</c> or <c>canvas</c>.</returns>
    public static bool TryParseCoverage(string? token, out FinalImageCoverage coverage)
    {
        switch (Normalize(token))
        {
            case "crop":
                coverage = FinalImageCoverage.CropToOverlap;
                return true;
            case "canvas":
                coverage = FinalImageCoverage.KeepCanvas;
                return true;
            default:
                coverage = FinalImageCoverage.CropToOverlap;
                return false;
        }
    }

    /// <summary>The options-file spelling of the subpixel state: <c>on</c> or <c>off</c>.</summary>
    public static string SubpixelToken(bool subpixel) => subpixel ? "on" : "off";

    /// <summary>
    /// Reads the subpixel token an options file stores. <c>on</c>/<c>off</c> are the written form;
    /// <c>true</c>/<c>false</c> are accepted too so a hand-edited file using the file's other
    /// boolean spelling still loads.
    /// </summary>
    /// <returns>False when the value is neither token nor a boolean.</returns>
    public static bool TryParseSubpixel(string? token, out bool subpixel)
    {
        switch (Normalize(token))
        {
            case "on":
            case "true":
                subpixel = true;
                return true;
            case "off":
            case "false":
                subpixel = false;
                return true;
            default:
                subpixel = true;
                return false;
        }
    }

    /// <summary>Trims the padding and the quotes a text-valued options line can carry.</summary>
    private static string Normalize(string? token) =>
        (token ?? string.Empty).Trim().Trim('"').Trim().ToLowerInvariant();

    /// <summary>The options-file spelling of the derotation state: <c>True</c> or <c>False</c>.</summary>
    public static string DerotateToken(bool derotate) => derotate ? "True" : "False";

    /// <summary>
    /// Reads the derotation toggle an options file stores. Capitalised booleans are the written form;
    /// <c>on</c>/<c>off</c> are accepted too so a hand-edited file using the subpixel key's spelling
    /// still loads.
    /// </summary>
    /// <returns>False when the value is neither a boolean nor one of those tokens.</returns>
    public static bool TryParseDerotate(string? token, out bool derotate)
    {
        switch (Normalize(token))
        {
            case "on":
            case "true":
                derotate = true;
                return true;
            case "off":
            case "false":
                derotate = false;
                return true;
            default:
                derotate = false;
                return false;
        }
    }

    /// <summary>True only for a finite search range the engine accepts (0.1..45 degrees).</summary>
    public static bool IsValidDerotateSearch(double degrees) =>
        !double.IsNaN(degrees)
        && !double.IsInfinity(degrees)
        && degrees >= MinDerotateSearchDegrees
        && degrees <= MaxDerotateSearchDegrees;

    /// <summary>
    /// The engine's and the options file's spelling of a search range: invariant, one decimal, e.g.
    /// <c>5.0</c> or <c>12.5</c>. Written invariantly so a profile saved on a comma-decimal machine
    /// still names the same angle when it is read back.
    /// </summary>
    public static string FormatDerotateSearch(double degrees) =>
        degrees.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the derotation search range an options file stores, in invariant or the current culture,
    /// and refuses a value outside 0.1..45 degrees so the caller can report it instead of silently
    /// searching a range the engine would reject.
    /// </summary>
    /// <returns>True only for a finite value inside the accepted range.</returns>
    public static bool TryParseDerotateSearch(string? token, out double degrees)
    {
        string text = (token ?? string.Empty).Trim().Trim('"').Trim();
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

        if (!parsed || !IsValidDerotateSearch(value))
        {
            degrees = DefaultDerotateSearchDegrees;
            return false;
        }

        degrees = value;
        return true;
    }

    /// <summary>The operator-facing method name ("Mean", "Sum", "Median").</summary>
    public static string MethodLabel(FinalImageMethod method) => method switch
    {
        FinalImageMethod.Mean => "Mean",
        FinalImageMethod.Sum => "Sum",
        FinalImageMethod.Median => "Median",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unknown final-image method"),
    };

    /// <summary>The operator-facing coverage name ("Crop to overlap", "Keep first canvas").</summary>
    public static string CoverageChoiceLabel(FinalImageCoverage coverage) => coverage switch
    {
        FinalImageCoverage.CropToOverlap => "Crop to overlap",
        FinalImageCoverage.KeepCanvas => "Keep first canvas",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage), coverage, "unknown final-image coverage"),
    };

    /// <summary>The coverage name used inside <see cref="Summary"/> (lower case, mid-sentence).</summary>
    public static string CoverageLabel(FinalImageCoverage coverage) => coverage switch
    {
        FinalImageCoverage.CropToOverlap => "crop to overlap",
        FinalImageCoverage.KeepCanvas => "keep first canvas",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage), coverage, "unknown final-image coverage"),
    };
}
