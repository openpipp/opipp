using System.Reflection;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// Constructs the real windows the way <c>Program.Main</c> does.
/// </summary>
/// <remarks>
/// 0.1.6 shipped a form whose <c>$this.Icon</c> resource had been replaced with raw .ico bytes while
/// the .resx still declared a BinaryFormatter-serialized object. The project compiled, every test
/// passed, and the application threw before drawing a window. Only constructing the form catches
/// that class of defect.
/// </remarks>
public sealed class StartupTests
{
    [Fact]
    public void MainForm_ConstructsAndCarriesTheVersion()
    {
        string expected = InformationalVersion();
        string? title = null;

        Exception? failure = RunSta(() =>
        {
            using var form = new Form1();
            title = form.Text;
        });

        Assert.Null(failure);
        Assert.NotNull(title);
        Assert.Contains("OPIPP", title);
        Assert.Contains(expected, title);
    }

    [Fact]
    public void AboutDialog_ConstructsAndNamesThisProject()
    {
        string version = InformationalVersion();
        string? title = null;
        string? content = null;

        Exception? failure = RunSta(() =>
        {
            using var dialog = new infodialog(version);
            title = dialog.Text;
            content = string.Join("\n", dialog.Controls.OfType<Control>().Select(c => c.Text));
        });

        Assert.Null(failure);
        Assert.Equal("About OPIPP", title);
        Assert.Contains("OPIPP " + version, content);
        Assert.DoesNotContain("Chris Garry", content);
        Assert.DoesNotContain("astropipp", content);
    }

    private static Exception? RunSta(Action action)
    {
        Exception? failure = null;

        // Background so a form that starts a message loop or a timer cannot keep the test host alive.
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "constructing the form hung for 30 seconds");
        return failure;
    }

    private static string InformationalVersion()
    {
        Assembly assembly = typeof(Form1).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return (informational ?? "0.0.0").Split('+')[0];
    }
}
