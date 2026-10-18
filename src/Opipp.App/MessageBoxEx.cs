using System.Runtime.InteropServices;
using System.Text;

namespace System.Windows.Forms;

public class MessageBoxEx
{
	public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

	public delegate void TimerProc(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime);

	public struct CWPRETSTRUCT
	{
		public IntPtr lResult;

		public IntPtr lParam;

		public IntPtr wParam;

		public uint message;

		public IntPtr hwnd;
	}

	public const int WH_CALLWNDPROCRET = 12;

	public const int WM_DESTROY = 2;

	public const int WM_INITDIALOG = 272;

	public const int WM_TIMER = 275;

	public const int WM_USER = 1024;

	public const int DM_GETDEFID = 1024;

	private const int TimerID = 42;

	private static HookProc hookProc;

	private static TimerProc hookTimer;

	private static uint hookTimeout;

	private static string hookCaption;

	private static IntPtr hHook;

	public static DialogResult Show(string text, uint uTimeout)
	{
		Setup("", uTimeout);
		return MessageBox.Show(text);
	}

	public static DialogResult Show(string text, string caption, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(text, caption);
	}

	public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(text, caption, buttons);
	}

	public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(text, caption, buttons, icon);
	}

	public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defButton, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(text, caption, buttons, icon, defButton);
	}

	public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defButton, MessageBoxOptions options, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(text, caption, buttons, icon, defButton, options);
	}

	public static DialogResult Show(IWin32Window owner, string text, uint uTimeout)
	{
		Setup("", uTimeout);
		return MessageBox.Show(owner, text);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(owner, text, caption);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(owner, text, caption, buttons);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(owner, text, caption, buttons, icon);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defButton, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(owner, text, caption, buttons, icon, defButton);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defButton, MessageBoxOptions options, uint uTimeout)
	{
		Setup(caption, uTimeout);
		return MessageBox.Show(owner, text, caption, buttons, icon, defButton, options);
	}

	[DllImport("User32.dll")]
	public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, TimerProc lpTimerFunc);

	[DllImport("User32.dll")]
	public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hInstance, int threadId);

	[DllImport("kernel32.dll")]
	private static extern int GetCurrentThreadId();

	[DllImport("user32.dll")]
	public static extern int UnhookWindowsHookEx(IntPtr idHook);

	[DllImport("user32.dll")]
	public static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern int GetWindowTextLength(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxLength);

	[DllImport("user32.dll")]
	public static extern int EndDialog(IntPtr hDlg, IntPtr nResult);

	static MessageBoxEx()
	{
		hookProc = MessageBoxHookProc;
		hookTimer = MessageBoxTimerProc;
		hookTimeout = 0u;
		hookCaption = null;
		hHook = IntPtr.Zero;
	}

	private static void Setup(string caption, uint uTimeout)
	{
		if (hHook != IntPtr.Zero)
		{
			throw new NotSupportedException("multiple calls are not supported");
		}
		hookTimeout = uTimeout;
		hookCaption = ((caption != null) ? caption : "");
		// AppDomain.GetCurrentThreadId() was removed in .NET (Core); the Win32 API gives the same native id.
		hHook = SetWindowsHookEx(12, hookProc, IntPtr.Zero, GetCurrentThreadId());
	}

	private static IntPtr MessageBoxHookProc(int nCode, IntPtr wParam, IntPtr lParam)
	{
		if (nCode < 0)
		{
			return CallNextHookEx(hHook, nCode, wParam, lParam);
		}
		CWPRETSTRUCT cWPRETSTRUCT = (CWPRETSTRUCT)Marshal.PtrToStructure(lParam, typeof(CWPRETSTRUCT));
		IntPtr idHook = hHook;
		if (hookCaption != null && cWPRETSTRUCT.message == 272)
		{
			int windowTextLength = GetWindowTextLength(cWPRETSTRUCT.hwnd);
			StringBuilder stringBuilder = new StringBuilder(windowTextLength + 1);
			GetWindowText(cWPRETSTRUCT.hwnd, stringBuilder, stringBuilder.Capacity);
			if (hookCaption == stringBuilder.ToString())
			{
				hookCaption = null;
				SetTimer(cWPRETSTRUCT.hwnd, (UIntPtr)42u, hookTimeout, hookTimer);
				UnhookWindowsHookEx(hHook);
				hHook = IntPtr.Zero;
			}
		}
		return CallNextHookEx(idHook, nCode, wParam, lParam);
	}

	private static void MessageBoxTimerProc(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime)
	{
		if (nIDEvent == (UIntPtr)42u)
		{
			short num = (short)(int)SendMessage(hWnd, 1024, IntPtr.Zero, IntPtr.Zero);
			EndDialog(hWnd, (IntPtr)num);
		}
	}
}
