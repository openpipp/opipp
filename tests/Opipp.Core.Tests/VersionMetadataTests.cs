using System.Xml.Linq;
using Xunit;

namespace Opipp.Core.Tests;

/// <summary>
/// The version appears three times in Directory.Build.props, and a partial bump shipped a 0.1.6 build
/// whose FileVersion still said 0.1.5. Assembly metadata is what users see in Explorer, the About box
/// and crash reports, so the three values must agree with the display version.
/// </summary>
public sealed class VersionMetadataTests
{
    [Fact]
    public void DirectoryBuildProps_VersionFieldsAgree()
    {
        XDocument document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "Directory.Build.props"));

        string display = Required(document, "Version");
        string assembly = Required(document, "AssemblyVersion");
        string file = Required(document, "FileVersion");

        string numeric = display.Split('-', '+')[0];
        string[] parts = numeric.Split('.');
        Assert.True(parts.Length >= 2, $"unexpected Version '{display}'");

        string expected = string.Join('.', parts[0], parts[1], parts.Length > 2 ? parts[2] : "0", "0");
        Assert.Equal(expected, assembly);
        Assert.Equal(expected, file);
    }

    private static string Required(XDocument document, string name)
    {
        string? value = document.Descendants(name).Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Directory.Build.props has no <{name}>");
        return value!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OPIPP.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("could not find OPIPP.slnx above " + AppContext.BaseDirectory);
    }
}
