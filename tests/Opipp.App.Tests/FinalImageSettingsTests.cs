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
        Assert.False(settings.Derotate);
        Assert.Equal(5.0, settings.DerotateSearchDegrees);
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
            Derotate = true,
            DerotateSearchDegrees = 12.5,
        };

        Assert.Empty(settings.ToSwitches());
    }

    [Fact]
    public void DerotateOff_EmitsTheThreeStackSwitchesOnly_EvenWithASearchRangeStored()
    {
        var settings = new FinalImageSettings
        {
            Enabled = true,
            Derotate = false,
            DerotateSearchDegrees = 20.0,
        };

        Assert.Equal(
            new[] { "-stack=mean", "-stack_coverage=crop", "-stack_subpixel=on" },
            settings.ToSwitches());
        Assert.Equal("mean, crop to overlap, subpixel on", settings.Summary);
    }

    [Theory]
    [InlineData(0.1, "0.1")]
    [InlineData(5, "5.0")]
    [InlineData(12.5, "12.5")]
    [InlineData(45, "45.0")]
    public void DerotateOn_AppendsBothDerotateSwitches_AfterTheStackSwitches(
        double searchDegrees,
        string expectedSearch)
    {
        var settings = new FinalImageSettings
        {
            Enabled = true,
            Method = FinalImageMethod.Sum,
            Coverage = FinalImageCoverage.KeepCanvas,
            Subpixel = false,
            Derotate = true,
            DerotateSearchDegrees = searchDegrees,
        };

        Assert.Equal(
            new[]
            {
                "-stack=sum",
                "-stack_coverage=canvas",
                "-stack_subpixel=off",
                "-derotate=on",
                "-derotate_search=" + expectedSearch,
            },
            settings.ToSwitches());
    }

    [Fact]
    public void Summary_NamesTheDerotationRangeOnlyWhenItIsOn()
    {
        Assert.Equal(
            "mean, crop to overlap, subpixel on, derotate 5.0 deg",
            new FinalImageSettings { Enabled = true, Derotate = true }.Summary);

        Assert.Equal(
            "median, keep first canvas, subpixel off, derotate 12.5 deg",
            new FinalImageSettings
            {
                Enabled = true,
                Method = FinalImageMethod.Median,
                Coverage = FinalImageCoverage.KeepCanvas,
                Subpixel = false,
                Derotate = true,
                DerotateSearchDegrees = 12.5,
            }.Summary);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.09)]
    [InlineData(45.1)]
    [InlineData(double.NaN)]
    public void ADerotateSearchOutsideTheAcceptedRange_IsRejected_NotClamped(double searchDegrees)
    {
        var settings = new FinalImageSettings
        {
            Enabled = true,
            Derotate = true,
            DerotateSearchDegrees = searchDegrees,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ToSwitches());
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("on", true)]
    [InlineData(" off ", false)]
    public void DerotateTokens_AcceptTheWrittenFormAndTheSubpixelSpelling(string token, bool expected)
    {
        Assert.True(FinalImageSettings.TryParseDerotate(token, out bool derotate));
        Assert.Equal(expected, derotate);
        Assert.Equal(expected ? "True" : "False", FinalImageSettings.DerotateToken(expected));
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    public void AnUnknownDerotateToken_IsRejected(string token)
    {
        Assert.False(FinalImageSettings.TryParseDerotate(token, out bool derotate));
        Assert.False(derotate);
    }

    [Theory]
    [InlineData("0.1", 0.1)]
    [InlineData("5.0", 5.0)]
    [InlineData(" 12.5 ", 12.5)]
    [InlineData("\"45\"", 45.0)]
    public void DerotateSearchTokens_RoundTripThroughTheOptionsFileSpelling(string token, double expected)
    {
        Assert.True(FinalImageSettings.TryParseDerotateSearch(token, out double degrees));
        Assert.Equal(expected, degrees, 3);
        Assert.Equal(expected, double.Parse(FinalImageSettings.FormatDerotateSearch(degrees), System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.05")]
    [InlineData("46")]
    [InlineData("-5")]
    [InlineData("wide")]
    [InlineData("")]
    public void AnUnusableDerotateSearchToken_IsRejected_WithTheDefaultKept(string token)
    {
        Assert.False(FinalImageSettings.TryParseDerotateSearch(token, out double degrees));
        Assert.Equal(5.0, degrees);
    }

    [Fact]
    public void TheDerotateSearchIsWrittenInvariantly_WithOneDecimal()
    {
        Assert.Equal("5.0", FinalImageSettings.FormatDerotateSearch(5));
        Assert.Equal("12.5", FinalImageSettings.FormatDerotateSearch(12.5));
        Assert.Equal("0.1", FinalImageSettings.FormatDerotateSearch(0.1));
        Assert.Equal("45.0", FinalImageSettings.FormatDerotateSearch(45.0));
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

    [Theory]
    [InlineData("mean", FinalImageMethod.Mean)]
    [InlineData("sum", FinalImageMethod.Sum)]
    [InlineData("median", FinalImageMethod.Median)]
    [InlineData(" SUM ", FinalImageMethod.Sum)]
    [InlineData("\"median\"", FinalImageMethod.Median)]
    public void MethodTokens_RoundTripThroughTheOptionsFileSpelling(string token, FinalImageMethod expected)
    {
        Assert.True(FinalImageSettings.TryParseMethod(token, out FinalImageMethod method));
        Assert.Equal(expected, method);
        Assert.Equal(token.Trim().Trim('"').ToLowerInvariant(), FinalImageSettings.MethodToken(method));
    }

    [Theory]
    [InlineData("crop", FinalImageCoverage.CropToOverlap)]
    [InlineData("canvas", FinalImageCoverage.KeepCanvas)]
    [InlineData(" Canvas ", FinalImageCoverage.KeepCanvas)]
    public void CoverageTokens_RoundTripThroughTheOptionsFileSpelling(string token, FinalImageCoverage expected)
    {
        Assert.True(FinalImageSettings.TryParseCoverage(token, out FinalImageCoverage coverage));
        Assert.Equal(expected, coverage);
        Assert.Equal(token.Trim().ToLowerInvariant(), FinalImageSettings.CoverageToken(coverage));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void SubpixelTokens_AcceptTheWrittenFormAndABoolean(string token, bool expected)
    {
        Assert.True(FinalImageSettings.TryParseSubpixel(token, out bool subpixel));
        Assert.Equal(expected, subpixel);
    }

    [Theory]
    [InlineData("average")]
    [InlineData("")]
    public void AnUnknownMethodToken_IsRejected(string token)
    {
        Assert.False(FinalImageSettings.TryParseMethod(token, out FinalImageMethod method));
        Assert.Equal(FinalImageMethod.Mean, method);
    }

    [Fact]
    public void AnUnknownCoverageToken_IsRejected()
    {
        Assert.False(FinalImageSettings.TryParseCoverage("expand", out FinalImageCoverage coverage));
        Assert.Equal(FinalImageCoverage.CropToOverlap, coverage);
    }

    [Fact]
    public void AnUnknownSubpixelToken_IsRejected()
    {
        Assert.False(FinalImageSettings.TryParseSubpixel("maybe", out bool subpixel));
        Assert.True(subpixel);
    }
}
