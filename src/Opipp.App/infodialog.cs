using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PIPP_Application;

/// <summary>
/// The Help / About window.
/// </summary>
/// <remarks>
/// This is deliberately not a port of the original dialog. The original About box showed the PIPP
/// logo, the original author's copyright line, a link to the PIPP site, and third-party credits for
/// dcraw, FFmpeg and NeuQuant. None of those components are part of OPIPP: the engine is fully
/// managed, bundles no dcraw or FFmpeg code, and does not use NeuQuant. Shipping that dialog would
/// misattribute this work and imply an endorsement that does not exist, so the window was replaced
/// with one that describes OPIPP only.
/// </remarks>
public class infodialog : Form
{
    public static bool open;

    private const string ProjectUrl = "https://github.com/openpipp/opipp";
    private const string SiteUrl = "https://openpipp.github.io/opipp/";

    public infodialog(string version)
    {
        open = true;
        Build(version);
    }

    ~infodialog()
    {
        open = false;
    }

    private void Build(string version)
    {
        var logo = new PictureBox
        {
            Location = new Point(22, 24),
            Size = new Size(96, 96),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadLogo(),
            TabStop = false,
        };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(140, 22),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Text = "OPIPP " + version,
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(142, 52),
            Text = "Open PIPP - an open, fully managed continuation of PIPP",
        };

        var disclaimer = new Label
        {
            AutoSize = true,
            Location = new Point(142, 76),
            Text = "Not affiliated with the original PIPP project or its author.",
        };

        var copyright = new Label
        {
            AutoSize = true,
            Location = new Point(142, 100),
            Text = "Copyright (c) 2026 OPIPP contributors - GPLv3 or later",
        };

        var link = new LinkLabel
        {
            AutoSize = true,
            Location = new Point(140, 124),
            Text = SiteUrl,
        };
        link.LinkClicked += (_, _) => OpenUrl(SiteUrl);

        var notes = new Label
        {
            AutoSize = false,
            Location = new Point(22, 136),
            Size = new Size(500, 96),
            Text = "AI-assisted development. No original PIPP binaries or source are redistributed, " +
                   "and no native PIPP, dcraw or FFmpeg components are bundled: the application and " +
                   "its engine are entirely managed code.",
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(447, 246),
            Size = new Size(75, 26),
        };
        ok.Click += (_, _) => Close();

        AcceptButton = ok;
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = SystemColors.Control;
        ClientSize = new Size(544, 286);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "About OPIPP";

        Icon = AppIcon.Load();

        Controls.AddRange([logo, title, subtitle, disclaimer, copyright, link, notes, ok]);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A missing browser handler must not take the application down from an About box.
        }
    }

    /// <summary>Loads the project's own icon; null when the resource is absent, so the box still opens.</summary>
    private static Image? LoadLogo()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Opipp.App.opipp-icon.png");
            if (stream is null) return null;
            using Image image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
