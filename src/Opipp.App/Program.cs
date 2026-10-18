using System;
using System.Drawing;
using System.Windows.Forms;

namespace PIPP_Application;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		// Headless check for the start-up path guard: PIPP_PATHGUARD_TEST=1 exits 3 when any
		// argument is not a legal Windows path, 0 when all are, without creating a window.
		if (Environment.GetEnvironmentVariable("PIPP_PATHGUARD_TEST") == "1")
		{
			string[] argv = Environment.GetCommandLineArgs();
			int bad = 0;
			for (int i = 1; i < argv.Length; i++)
			{
				if (!c_filelist.IsUsablePath(argv[i]))
				{
					bad++;
				}
			}
			Environment.ExitCode = bad == 0 ? 0 : 3;
			return;
		}
		// Same start-up sequence as the original .NET Framework PIPP.exe. ApplicationConfiguration.Initialize()
		// is deliberately NOT used: it installs a different default font/DPI mode, which would change the
		// metrics the ported designer AutoScaleDimensions (6F, 13F) were captured against.
		//
		// .NET 10 defaults to Segoe UI 9pt, whereas the .NET Framework 4.0 original used Microsoft Sans Serif
		// 8.25pt. With AutoScaleMode.Font that difference scales every ported form up by ~17%, so the original
		// default font is restored to keep the layout pixel-identical to the 862x487 designer size.
		Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25F));
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.Run(new Form1());
	}
}
