using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PIPP_Application;

public class c_filelist : ListView
{
	private int initial_source_directory;

	private OpenFileDialog openFD;

	private List<string> fits_extensions = new List<string> { "fit", "fits", "fts" };

	private List<string> image_extensions = new List<string> { "bmp", "tif", "tiff", "jpg", "jpeg", "png" };

	private List<string> video_extensions = new List<string>
	{
		"avi", "mov", "mp4", "mpg", "mpeg", "mkv", "mts", "m2ts", "m2t", "mvi",
		"wmv", "mlv"
	};

	private List<string> raw_extensions = new List<string>
	{
		"3fr", "arw", "cr2", "crw", "dcr", "dng", "erf", "orf", "raf", "raw",
		"rw2", "nef", "nrw", "mef", "mos", "mrw", "pef", "x3f"
	};

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
		image_extensions.AddRange(fits_extensions);
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
		string text = string.Empty;
		string[] array = cPersistentData.raw_file_extensions.Split(',');
		for (int i = 0; i < array.Length; i++)
		{
			text = ((i == array.Length - 1) ? (text + "*." + array[i]) : (text + "*." + array[i] + ";"));
		}
		openFD.Filter = "All Supported Types|*." + string.Join(";*.", image_extensions) + ";*." + string.Join(";*.", video_extensions) + ";*.ser;*." + string.Join(";*.", array) + ";|Video File(s)|*." + string.Join(";*.", video_extensions) + "|LuCam Recorder SER|*.ser|Image File(s)|*." + string.Join(";*.", image_extensions) + "|Raw Image File(s)|*." + string.Join(";*.", array);
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
			string[] source = cPersistentData.raw_file_extensions.Split(',');
			if (!(extension != ""))
			{
				continue;
			}
			extension = extension.ToLower();
			extension = extension.Remove(0, 1);
			if (fits_extensions.Contains(extension))
			{
				extension = "fits";
			}
			if (!image_extensions.Contains(extension) && !video_extensions.Contains(extension) && !extension.StartsWith("ser") && !source.Contains(extension))
			{
				// The native UI called raw_decode_check here: dcraw either recognised the unknown
				// extension as a camera RAW file (and it was added to raw_file_extensions) or the file
				// was rejected. Opipp.Core has no RAW decoder, so every unknown extension is rejected.
				// Known raw extensions still reach this list through raw_file_extensions, but the
				// managed engine cannot decode them either.
				num5++;
				text = text + text2 + "\n";
				continue;
			}
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
			else if (video_extensions.Contains(extension))
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
			else if (extension.StartsWith("ser"))
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
			source = cPersistentData.raw_file_extensions.Split(',');
			bool flag2 = false;
			if (image_extensions.Contains(extension) || source.Contains(extension))
			{
				flag2 = true;
			}
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
			MessageBoxEx.Show("The following 1 file could not be added:\n" + text, "Add Source Files", MessageBoxButtons.OK, MessageBoxIcon.Asterisk, 10000u);
		}
		else if (num5 > 1)
		{
			MessageBoxEx.Show("The following " + num5 + " files could not be added:\n" + text, "Add Source Files", MessageBoxButtons.OK, MessageBoxIcon.Asterisk, 10000u);
		}
		ListViewItemSorter = lvwColumnSorter;
		OnFileListUpdated(new FileListEventArgs(total_frame_count, flag, num, avi_bayer_pattern, by8_avi_added));
	}
}
