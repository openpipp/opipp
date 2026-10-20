using Opipp.Core.Cli;

namespace Opipp.Cli.Tests;

/// <summary>
/// Parsing of the final-image integration switches (OPIPP extensions: <c>-stack</c>,
/// <c>-stack_coverage</c>, <c>-stack_subpixel</c>), including the defaults an absent switch keeps.
/// </summary>
public class FinalImageSwitchTests
{
    [Theory]
    [InlineData("mean", PippStackMethod.Mean)]
    [InlineData("sum", PippStackMethod.Sum)]
    [InlineData("median", PippStackMethod.Median)]
    [InlineData("MEAN", PippStackMethod.Mean)]
    [InlineData("Median", PippStackMethod.Median)]
    public void StackMethod_IsParsedCaseInsensitively(string value, PippStackMethod expected)
    {
        ParseResult result = CommandLineParser.Parse([$"-stack={value}"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(expected, result.Options.StackMethod);
    }

    [Theory]
    [InlineData("crop", PippStackCoverage.Crop)]
    [InlineData("canvas", PippStackCoverage.Canvas)]
    public void StackCoverage_IsParsed(string value, PippStackCoverage expected)
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", $"-stack_coverage={value}"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(expected, result.Options.StackCoverage);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("ON", true)]
    public void StackSubpixel_IsParsed(string value, bool expected)
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", $"-stack_subpixel={value}"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(expected, result.Options.StackSubpixel);
    }

    /// <summary>An absent switch stays null, so the pipeline sees "not requested" rather than a default.</summary>
    [Fact]
    public void AbsentSwitches_LeaveIntegrationUnrequested()
    {
        ParseResult result = CommandLineParser.Parse(["-tif", "capture.ser"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Null(result.Options.StackMethod);
        Assert.Null(result.Options.StackCoverage);
        Assert.Null(result.Options.StackSubpixel);
    }

    /// <summary>A qualifier may precede -stack: the cross-switch check runs after the whole line.</summary>
    [Fact]
    public void QualifierBeforeStack_IsAccepted()
    {
        ParseResult result = CommandLineParser.Parse(
            ["-stack_coverage=canvas", "-stack_subpixel=off", "-stack=sum"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.Equal(PippStackMethod.Sum, result.Options.StackMethod);
        Assert.Equal(PippStackCoverage.Canvas, result.Options.StackCoverage);
        Assert.Equal(false, result.Options.StackSubpixel);
    }

    [Fact]
    public void StackWithoutAValue_IsReported()
    {
        ParseResult result = CommandLineParser.Parse(["-stack"]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("-stack", StringComparison.Ordinal));
        Assert.Null(result.Options.StackMethod);
    }

    [Theory]
    [InlineData("-stack_coverage=crop")]
    [InlineData("-stack_subpixel=on")]
    public void QualifierWithoutStack_IsReported(string qualifier)
    {
        ParseResult result = CommandLineParser.Parse(["-tif", qualifier]);

        Assert.False(result.Success);
        Assert.Contains(
            result.Errors,
            error => error.Contains(qualifier.Split('=')[0], StringComparison.Ordinal) &&
                     error.Contains("-stack=mean|sum|median", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-stack=average", "average")]
    [InlineData("-stack=sigma", "sigma")]
    public void UnknownMethod_IsReportedWithTheValue(string token, string value)
    {
        ParseResult result = CommandLineParser.Parse([token]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains($"'{value}'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-stack_coverage=stretch", "stretch")]
    [InlineData("-stack_subpixel=maybe", "maybe")]
    public void UnknownQualifierValue_IsReportedWithTheValue(string qualifier, string value)
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", qualifier]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains($"'{value}'", StringComparison.Ordinal));
    }

    /// <summary>None of these is an unknown switch to this parser, so the CLI must exit 2, not 127.</summary>
    [Fact]
    public void IntegrationSwitches_AreNotUnknownSwitches()
    {
        ParseResult result = CommandLineParser.Parse(["-stack=mean", "-stack_coverage=crop", "-stack_subpixel=on"]);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("Unknown switch", StringComparison.Ordinal));
    }
}
