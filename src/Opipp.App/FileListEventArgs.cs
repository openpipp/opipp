using System;

namespace PIPP_Application;

public class FileListEventArgs : EventArgs
{
	private int frame_count;

	private bool image_added;

	private int ser_bayer_pattern;

	private int avi_bayer_pattern;

	private bool by8_avi_added;

	public int FrameCount => frame_count;

	public bool ImageAdded => image_added;

	public int SerBayerPattern => ser_bayer_pattern;

	public int AviBayerPattern => avi_bayer_pattern;

	public bool By8AviAdded => by8_avi_added;

	public FileListEventArgs(int frame_count, bool image_added, int ser_bayer_pattern, int avi_bayer_pattern, bool by8_avi_added)
	{
		this.frame_count = frame_count;
		this.image_added = image_added;
		this.ser_bayer_pattern = ser_bayer_pattern;
		this.avi_bayer_pattern = avi_bayer_pattern;
		this.by8_avi_added = by8_avi_added;
	}
}
