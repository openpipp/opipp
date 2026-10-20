#nullable enable

using System;
using System.Collections.Generic;

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
    /// The exact engine switches for the current choice, in method/coverage/subpixel order.
    /// </summary>
    /// <returns>Three switches when enabled; an empty list when disabled.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A method or coverage value is not one the
    /// engine accepts. An unknown value must be rejected, not silently mapped onto a default.</exception>
    public IReadOnlyList<string> ToSwitches()
    {
        if (!Enabled)
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            "-stack=" + MethodToken(Method),
            "-stack_coverage=" + CoverageToken(Coverage),
            "-stack_subpixel=" + (Subpixel ? "on" : "off"),
        };
    }

    /// <summary>
    /// One short line naming the method, the coverage and the subpixel state, e.g.
    /// <c>mean, crop to overlap, subpixel on</c>. <c>off</c> while integration is disabled.
    /// </summary>
    public string Summary => Enabled
        ? MethodToken(Method) + ", " + CoverageLabel(Coverage) + ", subpixel " + (Subpixel ? "on" : "off")
        : "off";

    /// <summary>The engine's lowercase spelling of a method.</summary>
    public static string MethodToken(FinalImageMethod method) => method switch
    {
        FinalImageMethod.Mean => "mean",
        FinalImageMethod.Sum => "sum",
        FinalImageMethod.Median => "median",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unknown final-image method"),
    };

    /// <summary>The engine's spelling of a coverage policy.</summary>
    public static string CoverageToken(FinalImageCoverage coverage) => coverage switch
    {
        FinalImageCoverage.CropToOverlap => "crop",
        FinalImageCoverage.KeepCanvas => "canvas",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage), coverage, "unknown final-image coverage"),
    };

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
