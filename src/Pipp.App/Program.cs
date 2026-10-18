using System;
using System.Drawing;
using System.Windows.Forms;

namespace PIPP_Application;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
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
