namespace Opipp.Core.Tests;

using Opipp.Core.Pipeline;
using Xunit;

/// <summary>
/// The diagnostic retention-budget override (<c>OPIPP_RETENTION_BUDGET_MB</c>). It exists so the
/// memory probe can move the spill point without adding a CLI switch (which would change
/// <c>-gui</c> transcripts); an absent or unusable value must leave the 512 MB default untouched,
/// because that default is what the published memory numbers mean.
/// </summary>
public class PipelineSettingsTests
{
    private const string Variable = "OPIPP_RETENTION_BUDGET_MB";
    private const long Megabyte = 1024L * 1024L;
    private const long DefaultBudget = 512L * Megabyte;

    /// <summary>Runs an assertion with the override at <paramref name="value"/>, restoring it afterwards.</summary>
    private static void WithOverride(string? value, Action assert)
    {
        string? previous = Environment.GetEnvironmentVariable(Variable);
        try
        {
            Environment.SetEnvironmentVariable(Variable, value);
            assert();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, previous);
        }
    }

    [Fact]
    public void RetentionBudget_UsesTheOverrideAndUnsettingItRestoresTheDefault()
    {
        string? previous = Environment.GetEnvironmentVariable(Variable);
        try
        {
            // 4096 MB rather than a tiny value: another test constructing settings in parallel must
            // not be pushed into spilling by a budget this test set. Bigger than the default only
            // ever means "spill later", which no assertion depends on.
            Environment.SetEnvironmentVariable(Variable, "4096");
            Assert.Equal(4096L * Megabyte, new PipelineSettings().RetentionBudgetBytes);

            Environment.SetEnvironmentVariable(Variable, null);
            Assert.Equal(DefaultBudget, new PipelineSettings().RetentionBudgetBytes);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, previous);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-64")]
    [InlineData("1.5")]
    [InlineData("lots")]
    [InlineData("9223372036854775807")] // parses, but the byte count would overflow
    public void RetentionBudget_IgnoresAnUnusableOverride(string value) =>
        WithOverride(value, () => Assert.Equal(DefaultBudget, new PipelineSettings().RetentionBudgetBytes));

    [Fact]
    public void RetentionBudget_ExplicitValueWinsOverTheOverride() =>
        WithOverride("4096", () => Assert.Equal(7L, new PipelineSettings { RetentionBudgetBytes = 7 }.RetentionBudgetBytes));
}
