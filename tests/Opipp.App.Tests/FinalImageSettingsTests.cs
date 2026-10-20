using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The operator's final-image choice and the exact engine switches it produces. These are the
/// strings the engine parses, so the expected values are written out rather than derived from the
/// production helpers.
/// </summary>
public sealed class FinalImageSettingsTests
{
    [Fact]
    public void Defaults_AreDisabled_WithMeanCropAndSubpixelOn_AndNoSwitches()
    {
        var settings = new FinalImageSettings();

        Assert.False(settings.Enabled);
        Assert.Equal(FinalImageMethod.Mean, settings.Method);
        Assert.Equal(FinalImageCoverage.CropToOverlap, settings.Coverage);
        Assert.True(settings.Subpixel);
        Assert.Empty(settings.ToSwitches());
        Assert.Equal("off", settings.Summary);
    }

    [Theory]
    [InlineData(FinalImageMethod.Mean, FinalImageCoverage.CropToOverlap, true, "mean", "crop", "on")]
    [InlineData(FinalImageMethod.Mean, FinalImageCoverage.CropToOverlap, false, "mean", "crop", "off")]
    [InlineData(FinalImageMethod.Mean, FinalImageCoverage.KeepCanvas, true, "mean", "canvas", "on")]
    [InlineData(FinalImageMethod.Mean, FinalImageCoverage.KeepCanvas, false, "mean", "canvas", "off")]
    [InlineData(FinalImageMethod.Sum, FinalImageCoverage.CropToOverlap, true, "sum", "crop", "on")]
    [InlineData(FinalImageMethod.Sum, FinalImageCoverage.CropToOverlap, false, "sum", "crop", "off")]
    [InlineData(FinalImageMethod.Sum, FinalImageCoverage.KeepCanvas, true, "sum", "canvas", "on")]
    [InlineData(FinalImageMethod.Sum, FinalImageCoverage.KeepCanvas, false, "sum", "canvas", "off")]
    [InlineData(FinalImageMethod.Median, FinalImageCoverage.CropToOverlap, true, "median", "crop", "on")]
    [InlineData(FinalImageMethod.Median, FinalImageCoverage.CropToOverlap, false, "median", "crop", "off")]
    [InlineData(FinalImageMethod.Median, FinalImageCoverage.KeepCanvas, true, "median", "canvas", "on")]
    [InlineData(FinalImageMethod.Median, FinalImageCoverage.KeepCanvas, false, "median", "canvas", "off")]
    public void ToSwitches_EmitsTheThreeEngineSwitches_ForEveryCombination(
        FinalImageMethod method,
        FinalImageCoverage coverage,
        bool subpixel,
        string expectedMethod,
        string expectedCoverage,
        string expectedSubpixel)
    {
        var settings = new FinalImageSettings
        {
            Enabled = true,
            Method = method,
            Coverage = coverage,
            Subpixel = subpixel,
        };

        Assert.Equal(
            new[]
            {
                "-stack=" + expectedMethod,
                "-stack_coverage=" + expectedCoverage,
                "-stack_subpixel=" + expectedSubpixel,
            },
            settings.ToSwitches());
    }

    [Fact]
    public void Summary_NamesTheMethodAndCoverage()
    {
        Assert.Equal(
            "median, keep first canvas, subpixel off",
            new FinalImageSettings
            {
                Enabled = true,
                Method = FinalImageMethod.Median,
                Coverage = FinalImageCoverage.KeepCanvas,
                Subpixel = false,
            }.Summary);

        Assert.Equal(
            "mean, crop to overlap, subpixel on",
            new FinalImageSettings { Enabled = true }.Summary);
    }

    [Theory]
    [InlineData(FinalImageMethod.Sum)]
    [InlineData(FinalImageMethod.Median)]
    public void Disabled_StillEmitsNothing_WhateverIsSelected(FinalImageMethod method)
    {
        var settings = new FinalImageSettings
        {
            Enabled = false,
            Method = method,
            Coverage = FinalImageCoverage.KeepCanvas,
            Subpixel = false,
        };

        Assert.Empty(settings.ToSwitches());
    }

    [Fact]
    public void AnUnknownMethod_IsRejected_NotMappedToADefault()
    {
        var settings = new FinalImageSettings { Enabled = true, Method = (FinalImageMethod)99 };

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ToSwitches());
    }

    [Fact]
    public void AnUnknownCoverage_IsRejected_NotMappedToADefault()
    {
        var settings = new FinalImageSettings { Enabled = true, Coverage = (FinalImageCoverage)99 };

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ToSwitches());
    }
}
