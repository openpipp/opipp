using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Opipp.Core.Pipeline;

namespace PIPP_Application;

public class c_filelist : ListView
{
	private int initial_source_directory;

	private OpenFileDialog openFD;

	/// <summary>
	/// FITS extensions. One FITS file can hold several image HDUs, but every one of them is a still
	/// image, so these stay in the picker's still-image group exactly as the original UI showed them.
	/// </summary>
	private static readonly string[] FitsExtensions = { "fit", "fits", "fts" };

	/// <summary>
	/// Still-image extensions <see cref="FrameSourceFactory"/> can actually open. JPEG is absent on
	/// purpose: the factory has no <c>.jpg</c>/<c>.jpeg</c> mapping, so offering it only put a row in
	/// this list that the engine refused once the run started.
	/// </summary>
	private static readonly string[] ImageExtensions = { "bmp", "tif", "tiff", "png" };

	/// <summary>
	/// Video containers the engine can open: the managed AVI reader (DIB/Y800 and MJPEG). MOV, MP4,
	/// MKV and the rest needed the native helper's FFmpeg demuxers and are not offered.
	/// </summary>
	private static readonly string[] VideoExtensions = { "avi" };

	/// <summary>
	/// RAW extensions the engine can open: only the uncompressed baseline DNG subset of
	/// <c>RawDngReader</c>. Every other camera RAW format needed dcraw.
	/// </summary>
	private static readonly string[] RawExtensions = { "dng" };

	/// <summary>The SER container, kept as its own picker entry as in the original UI.</summary>
	private static readonly string[] SerExtensions = { "ser" };

	private cListViewColumnSorter lvwColumnSorter;

	public int total_frame_count;

	private ColumnHeader ColHdrFilename;

	private ColumnHeader ColHdrDirectory;

	private ColumnHeader ColHdrDate;

	private ColumnHeader ColHdrType;

	private ColumnHeader ColHdrFrames;

	private ColumnHeader ColHdrSize;

	private ColumnHeader ColHdrframerate;

	private ColumnHeader ColHdrFilesize;

	public event EventHandler FileListUpdated;

	public c_filelist()
	{
		openFD = new OpenFileDialog();
		openFD.Multiselect = true;
		lvwColumnSorter = new cListViewColumnSorter();
		ColHdrFilename = new ColumnHeader();
		ColHdrFilename.Text = "Filename";
		ColHdrFilename.Width = 187;
		ColHdrDirectory = new ColumnHeader();
		ColHdrDirectory.DisplayIndex = 7;
		ColHdrDirectory.Text = "Directory";
		ColHdrDirectory.Width = 242;
		ColHdrDate = new ColumnHeader();
		ColHdrDate.DisplayIndex = 5;
		ColHdrDate.Text = "Date";
		ColHdrDate.Width = 86;
		ColHdrType = new ColumnHeader();
		ColHdrType.DisplayIndex = 1;
		ColHdrType.Text = "Type";
		ColHdrType.Width = 74;
		ColHdrFrames = new ColumnHeader();
		ColHdrFrames.DisplayIndex = 2;
		ColHdrFrames.Text = "Frames";
		ColHdrSize = new ColumnHeader();
		ColHdrSize.DisplayIndex = 4;
		ColHdrSize.Text = "Size";
		ColHdrSize.Width = 67;
		ColHdrframerate = new ColumnHeader();
		ColHdrframerate.DisplayIndex = 3;
		ColHdrframerate.Text = "FPS";
		ColHdrframerate.Width = 50;
		ColHdrFilesize = new ColumnHeader();
		ColHdrFilesize.DisplayIndex = 6;
		ColHdrFilesize.Text = "Filesize";
		ColHdrFilesize.Width = 75;
		AccessibleRole = AccessibleRole.None;
		AllowColumnReorder = true;
		AllowDrop = true;
		BackColor = Color.Cornsilk;
		Columns.AddRange(new ColumnHeader[8] { ColHdrFilename, ColHdrDirectory, ColHdrDate, ColHdrType, ColHdrFrames, ColHdrSize, ColHdrframerate, ColHdrFilesize });
		FullRowSelect = true;
		GridLines = true;
		HideSelection = false;
		Location = new Point(3, 16);
		Name = "listView_files";
		ShowGroups = false;
		Size = new Size(836, 250);
		TabIndex = 8;
		UseCompatibleStateImageBehavior = false;
		View = View.Details;
		ColumnClick += listView_files_ColumnClick;
		KeyDown += listView_files_KeyDown;
		DragDrop += listView_files_DragDrop;
		DragEnter += listView_files_DragEnter;
		MouseUp += listView_files__MouseUp;
	}

	/// <summary>
	/// The <c>Filter</c> of the "Add Source Files" dialog: exactly the formats
	/// <see cref="FrameSourceFactory"/> can open, grouped into the entries the original UI had.
	/// </summary>
	/// <remarks>
	/// The original list was wider than the engine: it offered JPEG (never a frame source), MOV/MP4/
	/// MKV and friends (the managed reader is AVI-only) and every camera RAW the native dcraw build
	/// knew (Opipp.Core decodes only the uncompressed DNG subset). Those entries only moved the
	/// failure from "add" to "run", so the five entries below are built from the same extension set
	/// the factory dispatches on and a test asserts the union still equals
	/// <see cref="FrameSourceFactory.Extensions"/>.
	/// </remarks>
	internal static string SourceFilter =>
		"All Supported Types|" + Patterns(ImageExtensions.Concat(FitsExtensions).Concat(VideoExtensions).Concat(SerExtensions).Concat(RawExtensions))
		+ "|Video File(s)|" + Patterns(VideoExtensions)
		+ "|LuCam Recorder SER|" + Patterns(SerExtensions)
		+ "|Image File(s)|" + Patterns(ImageExtensions.Concat(FitsExtensions))
		+ "|Raw Image File(s)|" + Patterns(RawExtensions);

	/// <summary>Every extension the picker offers, without the leading dot.</summary>
	internal static IEnumerable<string> OfferedExtensions =>
		ImageExtensions.Concat(FitsExtensions).Concat(VideoExtensions).Concat(SerExtensions).Concat(RawExtensions);

	private static string Patterns(IEnumerable<string> extensions) =>
		string.Join(";", extensions.Select(extension => "*." + extension));

	/// <summary>True for the still formats that need join mode; false for the AVI and SER containers.</summary>
	private static bool IsStillImage(string extension) =>
		Array.IndexOf(VideoExtensions, extension) < 0 && Array.IndexOf(SerExtensions, extension) < 0;

	public void set_initial_source_dir(int init_src_dir)
	{
		initial_source_directory = init_src_dir;
	}

	protected virtual void OnFileListUpdated(FileListEventArgs e)
	{
		if (FileListUpdated != null)
		{
			FileListUpdated(this, e);
		}
	}

	public void addsourceBTN_Click(object sender, EventArgs e)
	{
		add_source_dialog();
	}

	public void remove_button_Click(object sender, EventArgs e)
	{
		_ = SelectedIndices;
		for (int num = SelectedIndices.Count - 1; num >= 0; num--)
		{
			total_frame_count -= Convert.ToInt32(Items[SelectedIndices[num]].SubItems[4].Text);
			Items.RemoveAt(SelectedIndices[num]);
		}
		OnFileListUpdated(new FileListEventArgs(total_frame_count, image_added: false, -1, -1, by8_avi_added: false));
		Focus();
	}

	public void remove_all_button_Click(object sender, EventArgs e)
	{
		Items.Clear();
		total_frame_count = 0;
		OnFileListUpdated(new FileListEventArgs(total_frame_count, image_added: false, -1, -1, by8_avi_added: false));
	}

	private void listView_files_KeyDown(object sender, KeyEventArgs e)
	{
		if (SelectedItems != null && SelectedItems.Count > 0 && e.KeyCode == Keys.Delete)
		{
			_ = SelectedIndices;
			for (int num = SelectedIndices.Count - 1; num >= 0; num--)
			{
				total_frame_count -= Convert.ToInt32(Items[SelectedIndices[num]].SubItems[4].Text);
				Items.RemoveAt(SelectedIndices[num]);
			}
			OnFileListUpdated(new FileListEventArgs(total_frame_count, image_added: false, -1, -1, by8_avi_added: false));
		}
	}

	private void listView_files_ColumnClick(object sender, ColumnClickEventArgs e)
	{
		if (e.Column == lvwColumnSorter.SortColumn)
		{
			if (lvwColumnSorter.Order == SortOrder.Ascending)
			{
				lvwColumnSorter.Order = SortOrder.Descending;
			}
			else
			{
				lvwColumnSorter.Order = SortOrder.Ascending;
			}
		}
		else
		{
			lvwColumnSorter.SortColumn = e.Column;
			lvwColumnSorter.Order = SortOrder.Ascending;
		}
		Sort();
	}

	private void listView_files_DragEnter(object sender, DragEventArgs e)
	{
		e.Effect = DragDropEffects.Copy;
	}

	private void listView_files_DragDrop(object sender, DragEventArgs e)
	{
		if (e != null)
		{
			string[] array = (string[])e.Data.GetData(DataFormats.FileDrop, autoConvert: false);
			if (array != null && array.Length > 0)
			{
				add_source(array);
			}
		}
	}

	private void listView_files__MouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Right)
		{
			return;
		}
		ListViewItem itemAt = GetItemAt(e.X, e.Y);
		if (itemAt != null)
		{
			itemAt.Selected = true;
			ContextMenu contextMenu = new ContextMenu();
			contextMenu.MenuItems.Clear();
			MenuItem menuItem = new MenuItem("Remove File From Files List");
			contextMenu.MenuItems.Add(menuItem);
			menuItem.Click += remove_button_Click;
			MenuItem item = new MenuItem("-");
			if (itemAt.SubItems[3].Text.StartsWith("ser"))
			{
				contextMenu.MenuItems.Add(item);
				MenuItem menuItem2 = new MenuItem("Show SER File Details");
				contextMenu.MenuItems.Add(menuItem2);
				menuItem2.Click += listView_files__FileInfoBox;
			}
			else if (itemAt.SubItems[3].Text.StartsWith("avi"))
			{
				contextMenu.MenuItems.Add(item);
				MenuItem menuItem2 = new MenuItem("Show AVI File Details");
				contextMenu.MenuItems.Add(menuItem2);
				menuItem2.Click += listView_files__FileInfoBox;
			}
			else if (itemAt.SubItems[3].Text.StartsWith("fits"))
			{
				contextMenu.MenuItems.Add(item);
				MenuItem menuItem2 = new MenuItem("Show FITS File Details");
				contextMenu.MenuItems.Add(menuItem2);
				menuItem2.Click += listView_files__FileInfoBox;
			}
			contextMenu.Show(this, e.Location, LeftRightAlignment.Right);
		}
	}

	private void listView_files__FileInfoBox(object sender, EventArgs e)
	{
		if (SelectedItems == null || SelectedItems.Count <= 0)
		{
			return;
		}
		for (int i = 0; i < Items.Count; i++)
		{
			if (Items[i].Selected)
			{
				cFileInfoBox cFileInfoBox2 = new cFileInfoBox(Path.Combine(Items[i].SubItems[1].Text, Items[i].SubItems[0].Text));
				cFileInfoBox2.Text = "PIPP - File Details: " + Items[i].SubItems[0].Text;
				break;
			}
		}
	}

	public void file_up_BUT_Click(object sender, EventArgs e)
	{
		if (SelectedItems != null && SelectedItems.Count > 0)
		{
			lvwColumnSorter.SortColumn = 0;
			lvwColumnSorter.Order = SortOrder.None;
			for (int i = 1; i < Items.Count; i++)
			{
				if (Items[i].Selected && !Items[i - 1].Selected)
				{
					ListViewItem item = Items[i];
					Items.RemoveAt(i);
					Items.Insert(i - 1, item);
				}
			}
		}
		Focus();
	}

	public void file_down_BUT_Click(object sender, EventArgs e)
	{
		if (SelectedItems != null && SelectedItems.Count > 0 && Items.Count > 1)
		{
			lvwColumnSorter.SortColumn = 0;
			lvwColumnSorter.Order = SortOrder.None;
			for (int num = Items.Count - 2; num >= 0; num--)
			{
				if (Items[num].Selected && !Items[num + 1].Selected)
				{
					ListViewItem item = Items[num];
					Items.RemoveAt(num);
					Items.Insert(num + 1, item);
				}
			}
		}
		Focus();
	}

	public void add_source_dialog()
	{
		openFD.Title = "Select Source File(s)";
		switch (initial_source_directory)
		{
		case 1:
			openFD.InitialDirectory = cPersistentData.dark_directory;
			break;
		case 2:
			openFD.InitialDirectory = cPersistentData.flat_directory;
			break;
		case 3:
			openFD.InitialDirectory = cPersistentData.flat_dark_directory;
			break;
		default:
			openFD.InitialDirectory = cPersistentData.source_directory;
			break;
		}
		openFD.FileName = "";
		openFD.Filter = SourceFilter;
		openFD.FilterIndex = cPersistentData.source_filter_index;
		DialogResult dialogResult = openFD.ShowDialog();
		if (openFD.FileNames.Length > 0 && dialogResult == DialogResult.OK)
		{
			switch (initial_source_directory)
			{
			case 1:
				cPersistentData.dark_directory = Path.GetDirectoryName(openFD.FileName);
				break;
			case 2:
				cPersistentData.flat_directory = Path.GetDirectoryName(openFD.FileName);
				break;
			case 3:
				cPersistentData.flat_dark_directory = Path.GetDirectoryName(openFD.FileName);
				break;
			default:
				cPersistentData.source_directory = Path.GetDirectoryName(openFD.FileName);
				break;
			}
			cPersistentData.source_filter_index = openFD.FilterIndex;
		}
		add_source(openFD.FileNames);
	}

	/// <summary>
	/// True when <paramref name="path"/> can be turned into a <see cref="FileInfo"/> at all. The
	/// original assumes every argument is a native path and aborts during Form_Load otherwise.
	/// </summary>
	public static bool IsUsablePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		// .NET 10 no longer performs the CAS path check that killed the .NET Framework original, so
		// a URI is silently accepted as a *relative* file name here instead of throwing. Reject the
		// shapes that can never name a local file on Windows before the filesystem sees them:
		//   file:///C:/x.ser, http://..., ::, C:\dir:name.ser
		if (path.Contains("://", StringComparison.Ordinal))
		{
			return false;
		}
		int colon = path.IndexOf(':');
		if (colon is 0 || colon > 1)
		{
			return false;
		}

		try
		{
			_ = new FileInfo(path);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (NotSupportedException)
		{
			return false;
		}
		catch (PathTooLongException)
		{
			return false;
		}
	}

	public void add_source(string[] filelist)
	{
		bool flag = false;
		int num = -1;
		int avi_bayer_pattern = -1;
		bool by8_avi_added = false;
		int count = Items.Count;
		ListViewItemSorter = null;
		int num2 = 0;
		int num3 = 0;
		double num4 = 0.0;
		int num5 = 0;
		bool unsupported_format_reported = false;
		string text = "";
		foreach (string text2 in filelist)
		{
			if (string.IsNullOrWhiteSpace(text2))
			{
				continue;
			}
			num4 = 0.0;
			num2 = 0;
			num3 = 0;

			// The original crashes on start-up here when an argument is not a legal Windows path
			// (Form1_Load -> add_source -> new FileInfo -> FileIOPermission): a URI such as
			// file:///C:/x.ser or a colon in a non-drive position raises NotSupportedException
			// ("The given path's format is not supported") before the window ever appears.
			// Report it with the other files that could not be added instead of dying.
			if (!IsUsablePath(text2))
			{
				num5++;
				text = text + text2 + "\n";
				continue;
			}
			FileInfo fileInfo = new FileInfo(text2);
			DateTime lastWriteTime = fileInfo.LastWriteTime;
			string extension = fileInfo.Extension;
			if (!(extension != ""))
			{
				continue;
			}
			extension = extension.ToLowerInvariant();
			extension = extension.Remove(0, 1);
			if (Array.IndexOf(FitsExtensions, extension) >= 0)
			{
				extension = "fits";
			}
			// Only formats FrameSourceFactory can open are admitted. The legacy guard also trusted
			// cPersistentData.raw_file_extensions (a saved setting), so a stale or hand-edited list
			// re-admitted JPEG and camera RAW files the engine cannot decode; those only failed once
			// the run started. The factory is the single authority now, and the reason is reported.
			if (!FrameSourceFactory.IsSupported(text2))
			{
				num5++;
				text = text + text2 + " (unsupported file type)\n";
				unsupported_format_reported = true;
				continue;
			}
			bool still_image = IsStillImage(extension);
			int num6 = 1;
			if (extension == "fits")
			{
				// One FITS file can hold several images (the primary HDU plus every image
				// extension), and both the reader and the analysis count each of them. Adding one
				// frame per file made every master built from multi-image files too small: the
				// stage's frame limit was the file count, so with two two-image files both frames
				// came from the first file and the second was never opened. Probe the real reader
				// (headers only) and use the count it reports; it is disposed before the next file.
				if (!ManagedEngineApi.TryProbeFits(text2, out FitsFileProbe fits))
				{
					num5++;
					text = text + text2 + "\n";
					continue;
				}
				num6 = fits.FrameCount;
				num2 = fits.Width;
				num3 = fits.Height;
			}
			else if (extension == "dng")
			{
				// Only the uncompressed baseline DNG subset is decodable. Probe the header so a
				// lossy/lossless-JPEG DNG, a tiled DNG or a file wearing the extension that is not a
				// DNG is refused here, with the other files that could not be added, rather than
				// aborting the run on its first frame.
				if (!ManagedEngineApi.TryProbeDng(text2, out DngFileProbe dng))
				{
					num5++;
					text = text + text2 + " (unsupported DNG variant)\n";
					unsupported_format_reported = true;
					continue;
				}
				num2 = dng.Width;
				num3 = dng.Height;
			}
			else if (Array.IndexOf(VideoExtensions, extension) >= 0)
			{
				VideoProbe video;
				num6 = ManagedEngineApi.TryProbeVideo(text2, out video) ? video.FrameCount : 0;
				if (num6 < 1)
				{
					// Only AVI is implemented by the managed reader; MOV/MP4/MKV/... needed the
					// native helper's FFmpeg probe (and the managed engine cannot process them).
					num5++;
					text = text + text2 + "\n";
					continue;
				}
				num2 = video.Width;
				num3 = video.Height;
				num4 = ((video.FpsScale <= 0) ? ((double)video.FpsRate) : ((double)video.FpsRate / (double)video.FpsScale));
				num4 = Math.Round(num4, 2, MidpointRounding.AwayFromZero);
				avi_bayer_pattern = video.BayerPattern;
				if (extension.ToString() == "avi" && video.Codec.StartsWith("BY8", StringComparison.Ordinal))
				{
					by8_avi_added = true;
				}
				extension = string.Concat(extension, " (", video.Codec, ")");
			}
			else if (extension == "ser")
			{
				SerFileProbe ser;
				num6 = ManagedEngineApi.TryProbeSer(text2, out ser) ? ser.FrameCount : 0;
				if (num6 < 1)
				{
					DialogResult dialogResult = MessageBoxEx.Show("File: " + text2 + " has no frames and will not be added\n\nDo you want to show the SER file details for this file?", "Add Source Files", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk, 10000u);
					if (dialogResult == DialogResult.Yes)
					{
						cFileInfoBox cFileInfoBox2 = new cFileInfoBox(text2);
						string fileName = Path.GetFileName(text2);
						cFileInfoBox2.Text = "PIPP - File Details: " + fileName;
					}
					continue;
				}
				num2 = ser.Width;
				num3 = ser.Height;
				num4 = ((ser.FpsScale <= 0) ? 0.0 : ((double)ser.FpsRate / (double)ser.FpsScale));
				num4 = Math.Round(num4, 2, MidpointRounding.AwayFromZero);
				extension = string.Concat(extension, " (", ser.ColourId, ")");
				if (num < 0)
				{
					switch (ser.ColourId)
					{
					case "GBRG":
						num = 0;
						break;
					case "BGGR":
						num = 1;
						break;
					case "RGGB":
						num = 2;
						break;
					case "GRBG":
						num = 3;
						break;
					}
				}
			}
			ListViewItem listViewItem = new ListViewItem(Path.GetFileName(text2));
			listViewItem.SubItems.Add(Path.GetDirectoryName(text2));
			listViewItem.SubItems.Add(lastWriteTime.ToString());
			listViewItem.SubItems.Add(extension);
			listViewItem.SubItems.Add(num6.ToString());
			if (num2 == 0 && num3 == 0)
			{
				listViewItem.SubItems.Add("-");
			}
			else
			{
				listViewItem.SubItems.Add(num2 + "x" + num3);
			}
			if (extension.StartsWith("ser"))
			{
				if (num4 == 0.0)
				{
					listViewItem.SubItems.Add("No TS");
				}
				else if (num4 == -1.0)
				{
					listViewItem.SubItems.Add("Unordered TS");
				}
				else
				{
					listViewItem.SubItems.Add(num4.ToString());
				}
			}
			else if (num4 == 0.0)
			{
				listViewItem.SubItems.Add("-");
			}
			else
			{
				listViewItem.SubItems.Add(num4.ToString());
			}
			double num7 = fileInfo.Length;
			if (num7 > 1073741824.0)
			{
				double num8 = num7 / 1073741824.0;
				listViewItem.SubItems.Add(num8.ToString("#.##") + " GB");
			}
			else if (num7 > 1048576.0)
			{
				double num9 = num7 / 1048576.0;
				listViewItem.SubItems.Add(num9.ToString("#.##") + " MB");
			}
			else if (num7 > 1024.0)
			{
				double num10 = num7 / 1024.0;
				listViewItem.SubItems.Add(num10.ToString("#.##") + " KB");
			}
			else
			{
				listViewItem.SubItems.Add(num7 + " B");
			}
			listViewItem.SubItems[7].Tag = fileInfo.Length;
			bool flag2 = still_image;
			bool flag3 = false;
			for (int j = 0; j < count; j++)
			{
				if (listViewItem.SubItems[0].Text == Items[j].Text && listViewItem.SubItems[1].Text == Items[j].SubItems[1].Text)
				{
					flag3 = true;
				}
			}
			if (!flag3)
			{
				total_frame_count += num6;
				Items.Add(listViewItem);
				if (!flag && flag2)
				{
					flag = true;
				}
			}
		}
		if (num5 == 1)
		{
			ReportAddFailures("The following 1 file could not be added:\n" + text + DescribeSupportedFormats(unsupported_format_reported));
		}
		else if (num5 > 1)
		{
			ReportAddFailures("The following " + num5 + " files could not be added:\n" + text + DescribeSupportedFormats(unsupported_format_reported));
		}
		ListViewItemSorter = lvwColumnSorter;
		OnFileListUpdated(new FileListEventArgs(total_frame_count, flag, num, avi_bayer_pattern, by8_avi_added));
	}

	/// <summary>
	/// Receives the "could not be added" report instead of a modal message box when a test installs
	/// it; null keeps the real message box. The hook lives here because the source, dark, flat and
	/// flat-dark pickers all share this control.
	/// </summary>
	[Browsable(false)]
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	internal Action<string> AddFailureDialogOverride { get; set; }

	private void ReportAddFailures(string message)
	{
		if (AddFailureDialogOverride != null)
		{
			AddFailureDialogOverride(message);
			return;
		}
		MessageBoxEx.Show(message, "Add Source Files", MessageBoxButtons.OK, MessageBoxIcon.Asterisk, 10000u);
	}

	/// <summary>Appends the engine's format list when a file was refused because of its type.</summary>
	private static string DescribeSupportedFormats(bool any_unsupported_format) =>
		any_unsupported_format
			? "\n\nOPIPP can only open: " + string.Join(", ", FrameSourceFactory.Extensions) + "."
			: string.Empty;
}
