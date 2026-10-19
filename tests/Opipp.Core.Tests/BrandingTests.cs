using System.Security.Cryptography;
using Xunit;

namespace Opipp.Core.Tests;

/// <summary>
/// Guards against shipping the original project's branding or attribution.
/// </summary>
/// <remarks>
/// The GUI was ported from a decompiled front-end, and the port initially carried the original About
/// box, its copyright line, the original application icon and an update check pointing at the PIPP
/// website. Those are not this project's to redistribute: they misattribute the work and imply an
/// endorsement that does not exist. These tests fail on any reappearance in shipped source.
/// </remarks>
public sealed class BrandingTests
{
    private static readonly string[] Forbidden =
    [
        "Chris Garry",
        "astropipp",
        "Copyright (c) 2012-2017",
    ];

    [Fact]
    public void ShippedSource_ContainsNoOriginalProjectBranding()
    {
        string root = FindRepositoryRoot();
        var hits = new List<string>();

        foreach (string file in EnumerateShippedFiles(root))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string forbidden in Forbidden)
            {
                if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add($"{Path.GetRelativePath(root, file)}: {forbidden}");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Shipped source still contains original-project branding:" + Environment.NewLine +
            string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void ApplicationIcon_IsTheProjectArtwork()
    {
        string root = FindRepositoryRoot();
        string used = Path.Combine(root, "src", "Opipp.App", "app.ico");
        string artwork = Path.Combine(root, "docs", "assets", "opipp.ico");

        Assert.True(File.Exists(used), $"missing {used}");
        Assert.True(File.Exists(artwork), $"missing {artwork}");
        Assert.Equal(Sha256(artwork), Sha256(used));
    }

    private static IEnumerable<string> EnumerateShippedFiles(string root)
    {
        string source = Path.Combine(root, "src");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);
            if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extension = Path.GetExtension(file);
            if (extension is ".cs" or ".resx" or ".csproj" or ".ico")
            {
                yield return file;
            }
        }

        yield return Path.Combine(root, "Directory.Build.props");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
