using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PIPP_Application;

/// <summary>
/// Managed replacement for the decompiled <c>c_dll</c> class.
///
/// The original UI P/Invoked a native x64 build of <c>pipp_dll.dll</c>. This port keeps the exact same
/// public surface (same method names, parameter lists and calling convention) so the ported UI code is
/// unchanged, but no native binary is shipped with the repository: everything is resolved lazily and
/// every entry point throws <see cref="PippNativeUnavailableException"/> when the DLL cannot be loaded.
///
/// <see cref="IsAvailable"/> lets the UI start and stay usable without the native engine (see Form1's
/// constructor, which only registers the progress/print callbacks when the DLL is present).
/// </summary>
internal static class LegacyNativeApi
{
	/// <summary>Name of the legacy native engine library (probed on the default native search path).</summary>
	public const string DllName = "pipp_dll.dll";

	/// <summary>
	/// The original c_dll.cs also carried a hard-coded relative path
	/// ("..\..\..\..\pipp_core\Release\pipp_dll.dll") that was never used; it is intentionally gone -
	/// the native library must be next to the executable (or on PATH) to be picked up.
	/// </summary>
	private static readonly Lazy<bool> available = new Lazy<bool>(Probe, isThreadSafe: true);

	/// <summary>True when <c>pipp_dll.dll</c> can be loaded by the current process.</summary>
	public static bool IsAvailable => available.Value;

	private static bool Probe()
	{
		if (!NativeLibrary.TryLoad(DllName, out IntPtr handle))
		{
			return false;
		}
		// Keep the module loaded: all [DllImport] calls below resolve against it for the process lifetime.
		GC.KeepAlive(handle);
		return true;
	}

	private static void EnsureAvailable(string operation)
	{
		if (!IsAvailable)
		{
			throw new PippNativeUnavailableException(operation);
		}
	}

	private static MulticastDelegate? _progressCallback;

	public static void set_abort_request(int abort)
	{
		if (!IsAvailable)
		{
			// Managed engine: cancellation is signalled by terminating the engine process, so this
			// flag has nothing to do. Stay a no-op instead of failing the UI's cancel path.
			return;
		}

		Native.set_abort_request(abort);
	}

	public static int get_pipp_version(StringBuilder filename)
	{
		if (!IsAvailable)
		{
			filename.Clear();
			filename.Append("managed ").Append(typeof(LegacyNativeApi).Assembly.GetName().Version);
			return 0;
		}

		return Native.get_pipp_version(filename);
	}

	public static int get_dcraw_version(StringBuilder filename)
	{
		EnsureAvailable(nameof(get_dcraw_version));
		return Native.get_dcraw_version(filename);
	}

	public static int raw_decode_check(string filename)
	{
		EnsureAvailable(nameof(raw_decode_check));
		return Native.raw_decode_check(filename);
	}

	public static int date_time_check(string filename, string date_time_mask, StringBuilder output)
	{
		EnsureAvailable(nameof(date_time_check));
		return Native.date_time_check(filename, date_time_mask, output);
	}

	public static int video_check(string filename, ref int width, ref int height, ref int fps_rate, ref int fps_scale, StringBuilder codec, ref int colour, ref int bayer_pattern)
	{
		EnsureAvailable(nameof(video_check));
		return Native.video_check(filename, ref width, ref height, ref fps_rate, ref fps_scale, codec, ref colour, ref bayer_pattern);
	}

	public static int ser_check(string filename, ref int width, ref int height, ref int fps_rate, ref int fps_scale, StringBuilder colour_id)
	{
		EnsureAvailable(nameof(ser_check));
		return Native.ser_check(filename, ref width, ref height, ref fps_rate, ref fps_scale, colour_id);
	}

	public static int ser_info(string ser_filename, string tmp_filename)
	{
		EnsureAvailable(nameof(ser_info));
		return Native.ser_info(ser_filename, tmp_filename);
	}

	public static int avi_info(string avi_filename, string tmp_filename)
	{
		EnsureAvailable(nameof(avi_info));
		return Native.avi_info(avi_filename, tmp_filename);
	}

	public static int fits_info(string avi_filename, string tmp_filename)
	{
		EnsureAvailable(nameof(fits_info));
		return Native.fits_info(avi_filename, tmp_filename);
	}

	public static int stack_frames(string[] dark_files, string tmp_frame_to_subtract, string stacked_tiff_filename, string stacked_pgm_filename, int frames)
	{
		if (!IsAvailable)
		{
			// Managed replacement for the DLL's stacker: mean of the inputs, optional reference
			// frame subtracted, TIFF + PGM written for the engine's -dark_pgm/-flat_tmp handoff.
			return Pipp.Core.Pipeline.MasterFrameBuilder.Build(
				dark_files,
				string.IsNullOrEmpty(tmp_frame_to_subtract) ? null : tmp_frame_to_subtract,
				string.IsNullOrEmpty(stacked_tiff_filename) ? null : stacked_tiff_filename,
				string.IsNullOrEmpty(stacked_pgm_filename) ? null : stacked_pgm_filename,
				frames,
				ReportManagedProgress);
		}
		return Native.stack_frames(dark_files, tmp_frame_to_subtract, stacked_tiff_filename, stacked_pgm_filename, frames);
	}

	private static void ReportManagedProgress(int percent)
	{
		if (_progressCallback is null) return;
		try
		{
			_progressCallback.DynamicInvoke(percent);
		}
		catch (Exception)
		{
			// A UI callback must never abort calibration stacking.
		}
	}

	/// <summary>
	/// Registers the native progress callback. The caller must keep <paramref name="progress_callback"/>
	/// alive for as long as the native library can call it (Form1 stores it in a field).
	/// </summary>
	public static void set_progress_fn(MulticastDelegate progress_callback)
	{
		_progressCallback = progress_callback;
		if (!IsAvailable) return; // managed path invokes it through ReportManagedProgress
		Native.set_progress_fn(Marshal.GetFunctionPointerForDelegate(progress_callback));
	}

	/// <summary>
	/// Registers the native "print to GUI" callback. The caller must keep
	/// <paramref name="progress_callback"/> alive for as long as the native library can call it.
	/// </summary>
	public static void set_gui_print_fn(MulticastDelegate progress_callback)
	{
		if (!IsAvailable) return;
		Native.set_gui_print_fn(Marshal.GetFunctionPointerForDelegate(progress_callback));
	}

	/// <summary>
	/// Raw P/Invoke declarations, identical to the ones in the original c_dll.cs. Delegates are passed as
	/// <see cref="IntPtr"/> function pointers because .NET (Core) can no longer marshal a
	/// <see cref="MulticastDelegate"/>-typed parameter for an unmanaged call.
	/// </summary>
	private static class Native
	{
		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern void set_abort_request(int abort);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int get_pipp_version(StringBuilder filename);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int get_dcraw_version(StringBuilder filename);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int raw_decode_check(string filename);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int date_time_check(string filename, string date_time_mask, StringBuilder output);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int video_check(string filename, ref int width, ref int height, ref int fps_rate, ref int fps_scale, StringBuilder codec, ref int colour, ref int bayer_pattern);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int ser_check(string filename, ref int width, ref int height, ref int fps_rate, ref int fps_scale, StringBuilder colour_id);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int ser_info(string ser_filename, string tmp_filename);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int avi_info(string avi_filename, string tmp_filename);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int fits_info(string avi_filename, string tmp_filename);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern int stack_frames(string[] dark_files, string tmp_frame_to_subtract, string stacked_tiff_filename, string stacked_pgm_filename, int frames);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern void set_progress_fn(IntPtr progress_callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern void set_gui_print_fn(IntPtr progress_callback);
	}
}

/// <summary>
/// Thrown by <see cref="LegacyNativeApi"/> when the legacy native engine library (<c>pipp_dll.dll</c>)
/// is not available. The managed UI still runs; only features that need the native library fail.
/// </summary>
public sealed class PippNativeUnavailableException : InvalidOperationException
{
	public PippNativeUnavailableException(string operation)
		: base("'" + operation + "' requires the legacy native library '" + LegacyNativeApi.DllName +
			"', which was not found. The PIPP UI runs without it, but native engine features are " +
			"unavailable until the managed engine (Pipp.Core/Pipp.Cli) replaces it.")
	{
		Operation = operation;
	}

	/// <summary>Name of the LegacyNativeApi entry point that was called.</summary>
	public string Operation { get; }
}
