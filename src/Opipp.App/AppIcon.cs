using System;
using System.Drawing;
using System.Windows.Forms;

namespace PIPP_Application;

/// <summary>
/// The application icon used by every window.
/// </summary>
/// <remarks>
/// Deliberately not loaded from a <c>resx</c> resource. The ported form stored a BinaryFormatter
/// serialized <see cref="Icon"/> under <c>$this.Icon</c>; replacing that blob with raw .ico bytes
/// built fine but threw in <c>InitializeComponent</c>, so the application exited before showing a
/// window. Extracting the icon from the executable uses the icon the build already embedded
/// (<c>ApplicationIcon</c> in the project file) and cannot deserialize anything.
/// </remarks>
internal static class AppIcon
{
    private static Icon? _cached;

    public static Icon Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            _cached = SystemIcons.Application;
        }

        return _cached;
    }
}
