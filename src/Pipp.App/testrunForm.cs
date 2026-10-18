using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using PIPP_Application.Properties;

namespace PIPP_Application;

public class testrunForm : Form
{
	private const int c_min_afb = 64;

	private const int c_max_afb = 512;

	private const int c_min_edge_distance = 32;

	private Form1 parent;

	private int max_l_pixel = -1;

	private int max_r_pixel = -1;

	private int max_g_pixel = -1;

	private int max_b_pixel = -1;

	private int image_width = -1;

	private int image_height = -1;

	private int detect_threshold = -1;

	private int frame_number = -1;

	private int frame_count = -1;

	private string input_frame_image = "";

	private string output_frame_image = "";

	private int currectZoom = -1;

	private Rectangle afb_rect;

	private Rectangle afb_rect_tl;

	private Rectangle afb_rect_bl;

	private Rectangle afb_rect_tr;

	private Rectangle afb_rect_br;

	private Rectangle afb_rect_ctr;

	private Rectangle aoi_rect;

	private Rectangle aoi_rect_tl;

	private Rectangle aoi_rect_bl;

	private Rectangle aoi_rect_tr;

	private Rectangle aoi_rect_br;

	private int active_rect;

	private int active_area;

	private Point last_point;

	private IContainer components;

	private TableLayoutPanel imageTAB;

	private PictureBox hrulerPB;

	private PictureBox vrulerPB;

	private ComboBox zoomCB;

	private Label rmax_valueLAB;

	private Panel panel2;

	private Panel test_imagePAN;

	private Timer timer1;

	private Button save_frameBUT;

	private GroupBox zoomGB;

	private PictureBox testrunPB;

	private TableLayoutPanel tableLayoutPanel2;

	private Panel lhsPAN;

	private GroupBox max_pixel_value_rgbGB;

	private GroupBox max_pixel_value_greyGB;

	private Label grey_textLAB;

	private Label grey_max_valueLAB;

	private Label b_textLAB;

	private Label g_textLAB;

	private Label r_textLAB;

	private Label bmax_valueLAB;

	private Label gmax_valueLAB;

	private GroupBox frame_sizeGB;

	private Label frame_sizeLAB;

	private GroupBox detect_thresholdGB;

	private Label detect_thresholdLAB;

	private GroupBox frame_numberGB;

	private Label frame_numberLAB;

	private Button zoom_minusBUT;

	private Button zoom_plusBUT;

	private Button update_imageBUT;

	private GroupBox groupBox1;

	private GroupBox optionsGB;

	private CheckBox show_afb_aoiCB;

	public testrunForm(Form1 parent_val)
	{
		parent = parent_val;
		InitializeComponent();
	}

	public void enable_afb(bool enable)
	{
		optionsGB.Visible = enable;
	}

	private double get_zoom()
	{
		double result = 1.0;
		switch (zoomCB.SelectedIndex)
		{
		case 0:
			result = 4.0;
			break;
		case 1:
			result = 2.0;
			break;
		case 2:
			result = 1.0;
			break;
		case 3:
			result = 0.5;
			break;
		case 4:
			result = 0.25;
			break;
		case 5:
			result = 0.125;
			break;
		}
		return result;
	}

	public void set_images(string input_frame, string output_frame)
	{
		input_frame_image = input_frame;
		output_frame_image = output_frame;
		detect_threshold = -1;
		frame_number = -1;
		frame_count = -1;
		update_image();
		update_image();
	}

	private void set_image(bool input_frame)
	{
		string path = ((!input_frame) ? output_frame_image : input_frame_image);
		try
		{
			FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
			testrunPB.Image = Image.FromStream(fileStream);
			fileStream.Close();
			if (testrunPB.Image.Width != image_width || testrunPB.Image.Height != image_height)
			{
				if (testrunPB.Image.Width > 2560 || testrunPB.Image.Height > 2560)
				{
					zoomCB.SelectedIndex = 5;
				}
				else if (testrunPB.Image.Width > 1280 || testrunPB.Image.Height > 1280)
				{
					zoomCB.SelectedIndex = 4;
				}
				else if (testrunPB.Image.Width > 640 || testrunPB.Image.Height > 640)
				{
					zoomCB.SelectedIndex = 3;
				}
				else
				{
					zoomCB.SelectedIndex = 2;
				}
				resize_image();
			}
			image_width = testrunPB.Image.Width;
			image_height = testrunPB.Image.Height;
		}
		catch
		{
			return;
		}
		frame_sizeLAB.Text = testrunPB.Image.Width + " x " + testrunPB.Image.Height;
		update_max_label();
		update_detect_threshold_label();
		if (WindowState == FormWindowState.Minimized)
		{
			WindowState = FormWindowState.Normal;
		}
		Focus();
	}

	public void set_max_pixel(int l_value, int r_value, int g_value, int b_value, int fn_value, int fc_value)
	{
		max_l_pixel = l_value;
		max_r_pixel = r_value;
		max_g_pixel = g_value;
		max_b_pixel = b_value;
		frame_number = fn_value;
		frame_count = fc_value;
		update_max_label();
	}

	public void set_detect_threshold(int dt_value)
	{
		detect_threshold = dt_value;
		update_detect_threshold_label();
	}

	public void update_max_label()
	{
		max_pixel_value_rgbGB.Visible = false;
		max_pixel_value_greyGB.Visible = false;
		if (max_l_pixel >= 0)
		{
			max_pixel_value_greyGB.Visible = true;
			grey_max_valueLAB.Text = max_l_pixel + "%";
		}
		else if (max_r_pixel >= 0 && max_g_pixel >= 0 && max_b_pixel >= 0)
		{
			max_pixel_value_rgbGB.Visible = true;
			rmax_valueLAB.Text = max_r_pixel + "%";
			gmax_valueLAB.Text = max_g_pixel + "%";
			bmax_valueLAB.Text = max_b_pixel + "%";
		}
		frame_numberLAB.Text = frame_number + " of " + frame_count;
	}

	public void update_detect_threshold_label()
	{
		if (detect_threshold >= 0)
		{
			detect_thresholdGB.Visible = true;
			detect_thresholdLAB.Text = detect_threshold.ToString();
			Text = "PIPP - Oject Detect Threshold";
			return;
		}
		detect_thresholdGB.Visible = false;
		if (optionsGB.Visible && show_afb_aoiCB.Checked)
		{
			Text = "PIPP - Input Frame and AFB/AOI";
		}
		else
		{
			Text = "PIPP - Output Frame";
		}
	}

	public void update_image()
	{
		set_image(parent.is_afb_enabled() && show_afb_aoiCB.Checked);
		testrunPB.Refresh();
	}

	private void zoomCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		resize_image();
	}

	private void resize_image()
	{
		int num = 1;
		int num2 = 1;
		switch (zoomCB.SelectedIndex)
		{
		case 0:
			num = 4;
			hrulerPB.Image = Resources.h_ruler_x4;
			vrulerPB.Image = Resources.v_ruler_x4;
			break;
		case 1:
			num = 2;
			hrulerPB.Image = Resources.h_ruler_x2;
			vrulerPB.Image = Resources.v_ruler_x2;
			break;
		case 2:
			hrulerPB.Image = Resources.h_ruler_x1;
			vrulerPB.Image = Resources.v_ruler_x1;
			break;
		case 3:
			num2 = 2;
			hrulerPB.Image = Resources.h_ruler_x0_5;
			vrulerPB.Image = Resources.v_ruler_x0_5;
			break;
		case 4:
			num2 = 4;
			hrulerPB.Image = Resources.h_ruler_x0_25;
			vrulerPB.Image = Resources.v_ruler_x0_25;
			break;
		case 5:
			num2 = 8;
			hrulerPB.Image = Resources.h_ruler_x0_125;
			vrulerPB.Image = Resources.v_ruler_x0_125;
			break;
		}
		currectZoom = zoomCB.SelectedIndex;
		testrunPB.Width = testrunPB.Image.Width * num / num2;
		testrunPB.Height = testrunPB.Image.Height * num / num2;
		imageTAB.Size = new Size(testrunPB.Width + 30, testrunPB.Height + 30);
	}

	private void testrunPB_DoubleClick(object sender, EventArgs e)
	{
		if (testrunPB.Image.Width > 2560 || testrunPB.Image.Height > 2560)
		{
			zoomCB.SelectedIndex = 5;
		}
		else if (testrunPB.Image.Width > 1280 || testrunPB.Image.Height > 1280)
		{
			zoomCB.SelectedIndex = 4;
		}
		else if (testrunPB.Image.Width > 640 || testrunPB.Image.Height > 640)
		{
			zoomCB.SelectedIndex = 3;
		}
		else
		{
			zoomCB.SelectedIndex = 2;
		}
		int num = Location.X;
		int num2 = Location.Y;
		if (num < 0)
		{
			num = 25;
		}
		if (num2 < 0)
		{
			num2 = 25;
		}
		Location = new Point(num, num2);
		resize_image();
	}

	private void test_imagePAN_Scroll(object sender, ScrollEventArgs e)
	{
		resize_image();
	}

	private void button1_Click(object sender, EventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Filter = "Joint Picture Expert Group Image|*.jpg|Windows Bitmap Image|*.bmp|Portable Network Graphics|*.png|Tagged Image File Format|*.tif";
		saveFileDialog.Title = "Save Frame As Image File";
		DialogResult dialogResult = saveFileDialog.ShowDialog();
		if (saveFileDialog.FileNames.Length <= 0 || dialogResult != DialogResult.OK)
		{
			return;
		}
		ImageFormat format;
		if (saveFileDialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || saveFileDialog.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
		{
			format = ImageFormat.Jpeg;
		}
		else if (saveFileDialog.FileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
		{
			format = ImageFormat.Bmp;
		}
		else
		{
			if (!saveFileDialog.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				if (saveFileDialog.FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || saveFileDialog.FileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
				{
					format = ImageFormat.Tiff;
					ImageCodecInfo encoderInfo = GetEncoderInfo("image/tiff");
					_ = Encoder.Compression;
					EncoderParameters encoderParameters = new EncoderParameters(1);
					encoderParameters.Param[0] = new EncoderParameter(Encoder.Compression, 6L);
					try
					{
						using Bitmap bitmap = new Bitmap(testrunPB.Image);
						bitmap.Save(saveFileDialog.FileName, encoderInfo, encoderParameters);
						return;
					}
					catch
					{
						MessageBox.Show("Error ocurred saving " + saveFileDialog.FileName, "Save Frame As Image File");
						return;
					}
				}
				MessageBox.Show("Error: Unsupported Image Type", "Save Frame As Image File");
				return;
			}
			format = ImageFormat.Png;
		}
		try
		{
			using Bitmap bitmap2 = new Bitmap(testrunPB.Image);
			bitmap2.Save(saveFileDialog.FileName, format);
		}
		catch
		{
			MessageBox.Show("Error ocurred saving " + saveFileDialog.FileName, "Save Frame As Image File");
		}
	}

	private void zoom_plusBUT_Click(object sender, EventArgs e)
	{
		if (zoomCB.SelectedIndex > 0)
		{
			zoomCB.SelectedIndex--;
		}
	}

	private void zoom_minusBUT_Click(object sender, EventArgs e)
	{
		if (zoomCB.SelectedIndex < zoomCB.Items.Count - 1)
		{
			zoomCB.SelectedIndex++;
		}
	}

	private void update_imageBUT_Click(object sender, EventArgs e)
	{
		if (!parent.processing_acive())
		{
			parent.do_test_run(detect_threshold >= 0);
		}
	}

	private static ImageCodecInfo GetEncoderInfo(string mimeType)
	{
		ImageCodecInfo[] imageEncoders = ImageCodecInfo.GetImageEncoders();
		for (int i = 0; i < imageEncoders.Length; i++)
		{
			if (imageEncoders[i].MimeType == mimeType)
			{
				return imageEncoders[i];
			}
		}
		return null;
	}

	private void testrunPB_Paint(object sender, PaintEventArgs e)
	{
		if (!show_afb_aoiCB.Checked)
		{
			return;
		}
		if (parent.is_afb_enabled())
		{
			double zoom = get_zoom();
			int num = (int)((double)(int)parent.afb_xNUD.Value * zoom);
			int num2 = (int)((double)(int)parent.afb_yNUD.Value * zoom);
			int num3 = (int)((double)(int)parent.afb_widthNUD.Value * zoom);
			int num4 = (int)((double)(int)parent.afb_heightNUD.Value * zoom);
			afb_rect = new Rectangle(num, num2, num3, num4);
			afb_rect_tl = new Rectangle(num, num2, 10, 10);
			afb_rect_bl = new Rectangle(num, num2 + num4 - 10, 10, 10);
			afb_rect_tr = new Rectangle(num + num3 - 10, num2, 10, 10);
			afb_rect_br = new Rectangle(num + num3 - 10, num2 + num4 - 10, 10, 10);
			afb_rect_ctr = new Rectangle(num + num3 / 2 - 5, num2 + num4 / 2 - 5, 10, 10);
			using Pen pen = new Pen(Color.Red, 1f);
			e.Graphics.DrawRectangle(pen, afb_rect);
			e.Graphics.DrawRectangle(pen, afb_rect_tl);
			e.Graphics.DrawRectangle(pen, afb_rect_bl);
			e.Graphics.DrawRectangle(pen, afb_rect_tr);
			e.Graphics.DrawRectangle(pen, afb_rect_br);
			e.Graphics.DrawRectangle(pen, afb_rect_ctr);
			PointF point = new PointF(num + 10, num2);
			using Font font = new Font("Arial", 11f);
			e.Graphics.DrawString("AFB", font, Brushes.Red, point);
		}
		if (parent.is_aoi_enabled())
		{
			double zoom2 = get_zoom();
			int num5 = (int)((double)(int)parent.aoi_xNUD.Value * zoom2);
			int num6 = (int)((double)(int)parent.aoi_yNUD.Value * zoom2);
			int num7 = (int)((double)(int)parent.aoi_widthNUD.Value * zoom2);
			int num8 = (int)((double)(int)parent.aoi_heightNUD.Value * zoom2);
			aoi_rect = new Rectangle(num5, num6, num7, num8);
			aoi_rect_tl = new Rectangle(num5, num6, 10, 10);
			aoi_rect_bl = new Rectangle(num5, num6 + num8 - 10, 10, 10);
			aoi_rect_tr = new Rectangle(num5 + num7 - 10, num6, 10, 10);
			aoi_rect_br = new Rectangle(num5 + num7 - 10, num6 + num8 - 10, 10, 10);
			using (Pen pen2 = new Pen(Color.Blue, 1f))
			{
				e.Graphics.DrawRectangle(pen2, aoi_rect);
				e.Graphics.DrawRectangle(pen2, aoi_rect_tl);
				e.Graphics.DrawRectangle(pen2, aoi_rect_bl);
				e.Graphics.DrawRectangle(pen2, aoi_rect_tr);
				e.Graphics.DrawRectangle(pen2, aoi_rect_br);
			}
			PointF point2 = new PointF(num5 + 10, num6);
			using Font font2 = new Font("Arial", 11f);
			e.Graphics.DrawString("AOI", font2, Brushes.Blue, point2);
		}
	}

	private void testrunPB_MouseDown(object sender, MouseEventArgs e)
	{
		Point pt = new Point(e.X, e.Y);
		if (afb_rect_tl.Contains(pt))
		{
			active_rect = 1;
			active_area = 0;
			last_point = pt;
		}
		if (afb_rect_bl.Contains(pt))
		{
			active_rect = 2;
			active_area = 0;
			last_point = pt;
		}
		if (afb_rect_tr.Contains(pt))
		{
			active_rect = 3;
			active_area = 0;
			last_point = pt;
		}
		if (afb_rect_br.Contains(pt))
		{
			active_rect = 4;
			active_area = 0;
			last_point = pt;
		}
		if (afb_rect_ctr.Contains(pt))
		{
			active_rect = 5;
			active_area = 0;
			last_point = pt;
		}
		if (aoi_rect_tl.Contains(pt))
		{
			active_rect = 1;
			active_area = 1;
			last_point = pt;
		}
		if (aoi_rect_bl.Contains(pt))
		{
			active_rect = 2;
			active_area = 1;
			last_point = pt;
		}
		if (aoi_rect_tr.Contains(pt))
		{
			active_rect = 3;
			active_area = 1;
			last_point = pt;
		}
		if (aoi_rect_br.Contains(pt))
		{
			active_rect = 4;
			active_area = 1;
			last_point = pt;
		}
	}

	private void testrunPB_MouseUp(object sender, MouseEventArgs e)
	{
		testrunPB_MouseMove(sender, e);
		active_rect = 0;
	}

	private void testrunPB_MouseMove(object sender, MouseEventArgs e)
	{
		if (active_rect <= 0)
		{
			return;
		}
		double zoom = get_zoom();
		int num = e.X - last_point.X;
		int num2 = e.Y - last_point.Y;
		if (active_area == 0)
		{
			int num3 = (int)((double)num % (16.0 * zoom));
			int num4 = (int)((double)num2 % (16.0 * zoom));
			if (active_rect == 5)
			{
				num3 = 0;
				num4 = 0;
			}
			num -= num3;
			num2 -= num4;
			last_point = new Point(e.X - num3, e.Y - num4);
			_ = (int)parent.afb_xNUD.Value;
			_ = (int)parent.afb_yNUD.Value;
			_ = (int)parent.afb_widthNUD.Value;
			_ = (int)parent.afb_heightNUD.Value;
			if (active_rect == 1)
			{
				if (parent.afb_widthNUD.Value - (decimal)(int)((double)num / zoom) > 512m)
				{
					num = (int)((double)((int)parent.afb_widthNUD.Value - 512) * zoom);
				}
				if (parent.afb_widthNUD.Value - (decimal)(int)((double)num / zoom) < 64m)
				{
					num = (int)((double)((int)parent.afb_widthNUD.Value - 64) * zoom);
				}
				if (parent.afb_xNUD.Value + (decimal)(int)((double)num / zoom) < 32m)
				{
					num = (int)((double)(32 - (int)parent.afb_xNUD.Value) * zoom);
				}
				if (parent.afb_heightNUD.Value - (decimal)(int)((double)num2 / zoom) > 512m)
				{
					num2 = (int)((double)((int)parent.afb_heightNUD.Value - 512) * zoom);
				}
				if (parent.afb_heightNUD.Value - (decimal)(int)((double)num2 / zoom) < 64m)
				{
					num2 = (int)((double)((int)parent.afb_heightNUD.Value - 64) * zoom);
				}
				if (parent.afb_yNUD.Value + (decimal)(int)((double)num2 / zoom) < 32m)
				{
					num2 = (int)((double)(32 - (int)parent.afb_yNUD.Value) * zoom);
				}
				num -= (int)((double)num % (16.0 * zoom));
				num2 -= (int)((double)num2 % (16.0 * zoom));
				parent.afb_xNUD.Value += (decimal)(int)((double)num / zoom);
				parent.afb_yNUD.Value += (decimal)(int)((double)num2 / zoom);
				parent.afb_widthNUD.Value -= (decimal)(int)((double)num / zoom);
				parent.afb_heightNUD.Value -= (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 2)
			{
				if (parent.afb_widthNUD.Value - (decimal)(int)((double)num / zoom) > 512m)
				{
					num = (int)((double)((int)parent.afb_widthNUD.Value - 512) * zoom);
				}
				if (parent.afb_widthNUD.Value - (decimal)(int)((double)num / zoom) < 64m)
				{
					num = (int)((double)((int)parent.afb_widthNUD.Value - 64) * zoom);
				}
				if (parent.afb_xNUD.Value + (decimal)(int)((double)num / zoom) < 32m)
				{
					num = (int)((double)(1 - (int)parent.afb_xNUD.Value) * zoom);
				}
				if (parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) > 512m)
				{
					num2 = (int)((double)(512 - (int)parent.afb_heightNUD.Value) * zoom);
				}
				if (parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) < 64m)
				{
					num2 = (int)((double)(64 - (int)parent.afb_heightNUD.Value) * zoom);
				}
				if (parent.afb_yNUD.Value + parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) >= (decimal)(image_height - 32))
				{
					num2 = (int)((double)(image_height - 32 - (int)(parent.afb_yNUD.Value + parent.afb_heightNUD.Value)) * zoom);
				}
				num -= (int)((double)num % (16.0 * zoom));
				num2 -= (int)((double)num2 % (16.0 * zoom));
				parent.afb_xNUD.Value += (decimal)(int)((double)num / zoom);
				parent.afb_widthNUD.Value -= (decimal)(int)((double)num / zoom);
				parent.afb_heightNUD.Value += (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 3)
			{
				if (parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) > 512m)
				{
					num = (int)((double)(512 - (int)parent.afb_widthNUD.Value) * zoom);
				}
				if (parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) < 64m)
				{
					num = (int)((double)(64 - (int)parent.afb_widthNUD.Value) * zoom);
				}
				if (parent.afb_xNUD.Value + parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) >= (decimal)(image_width - 32))
				{
					num = (int)((double)(image_width - 32 - (int)(parent.afb_xNUD.Value + parent.afb_widthNUD.Value)) * zoom);
				}
				if (parent.afb_heightNUD.Value - (decimal)(int)((double)num2 / zoom) > 512m)
				{
					num2 = (int)((double)((int)parent.afb_heightNUD.Value - 512) * zoom);
				}
				if (parent.afb_heightNUD.Value - (decimal)(int)((double)num2 / zoom) < 64m)
				{
					num2 = (int)((double)((int)parent.afb_heightNUD.Value - 64) * zoom);
				}
				if (parent.afb_yNUD.Value + (decimal)(int)((double)num2 / zoom) < 32m)
				{
					num2 = (int)((double)(32 - (int)parent.afb_yNUD.Value) * zoom);
				}
				num -= (int)((double)num % (16.0 * zoom));
				num2 -= (int)((double)num2 % (16.0 * zoom));
				parent.afb_widthNUD.Value += (decimal)(int)((double)num / zoom);
				parent.afb_yNUD.Value += (decimal)(int)((double)num2 / zoom);
				parent.afb_heightNUD.Value -= (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 4)
			{
				if (parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) > 512m)
				{
					num = (int)((double)(512 - (int)parent.afb_widthNUD.Value) * zoom);
				}
				if (parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) < 64m)
				{
					num = (int)((double)(64 - (int)parent.afb_widthNUD.Value) * zoom);
				}
				if (parent.afb_xNUD.Value + parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) >= (decimal)(image_width - 32))
				{
					num = (int)((double)(image_width - 32 - (int)(parent.afb_xNUD.Value + parent.afb_widthNUD.Value)) * zoom);
				}
				if (parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) > 512m)
				{
					num2 = (int)((double)(512 - (int)parent.afb_heightNUD.Value) * zoom);
				}
				if (parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) < 64m)
				{
					num2 = (int)((double)(64 - (int)parent.afb_heightNUD.Value) * zoom);
				}
				if (parent.afb_yNUD.Value + parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) >= (decimal)(image_height - 32))
				{
					num2 = (int)((double)(image_height - 32 - (int)(parent.afb_yNUD.Value + parent.afb_heightNUD.Value)) * zoom);
				}
				num -= (int)((double)num % (16.0 * zoom));
				num2 -= (int)((double)num2 % (16.0 * zoom));
				parent.afb_widthNUD.Value += (decimal)(int)((double)num / zoom);
				parent.afb_heightNUD.Value += (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 5)
			{
				if (parent.afb_xNUD.Value + (decimal)(int)((double)num / zoom) < 32m)
				{
					num = (int)((double)(32 - (int)parent.afb_xNUD.Value) * zoom);
				}
				if (parent.afb_xNUD.Value + parent.afb_widthNUD.Value + (decimal)(int)((double)num / zoom) >= (decimal)(image_width - 32))
				{
					num = (int)((double)(image_width - 32 - (int)parent.afb_xNUD.Value - (int)parent.afb_widthNUD.Value) * zoom);
				}
				if (parent.afb_yNUD.Value + (decimal)(int)((double)num2 / zoom) < 32m)
				{
					num2 = (int)((double)(32 - (int)parent.afb_yNUD.Value) * zoom);
				}
				if (parent.afb_yNUD.Value + parent.afb_heightNUD.Value + (decimal)(int)((double)num2 / zoom) >= (decimal)(image_height - 32))
				{
					num2 = (int)((double)(image_height - 32 - (int)parent.afb_yNUD.Value - (int)parent.afb_heightNUD.Value) * zoom);
				}
				parent.afb_xNUD.Value += (decimal)(int)((double)num / zoom);
				parent.afb_yNUD.Value += (decimal)(int)((double)num2 / zoom);
			}
		}
		else
		{
			if (active_area != 1)
			{
				return;
			}
			last_point = new Point(e.X, e.Y);
			int num5 = (int)((double)(int)parent.afb_widthNUD.Value * zoom);
			int num6 = (int)((double)(int)parent.afb_heightNUD.Value * zoom);
			int num7 = (int)parent.afb_xNUD.Value;
			num7 = (int)((double)num7 * zoom);
			int num8 = (int)parent.afb_yNUD.Value;
			num8 = (int)((double)num8 * zoom);
			int num9 = (int)parent.afb_xNUD.Value;
			num9 = (int)((double)num9 * zoom) + num5;
			int num10 = (int)parent.afb_yNUD.Value;
			num10 = (int)((double)num10 * zoom) + num6;
			int num11 = (int)parent.aoi_xNUD.Value;
			num11 = (int)((double)num11 * zoom);
			int num12 = (int)parent.aoi_yNUD.Value;
			num12 = (int)((double)num12 * zoom);
			int num13 = (int)parent.aoi_widthNUD.Value;
			num13 = (int)((double)num13 * zoom);
			int num14 = (int)parent.aoi_heightNUD.Value;
			num14 = (int)((double)num14 * zoom);
			if (active_rect == 1)
			{
				if (num11 + num > num7)
				{
					num = num7 - num11;
				}
				if (num11 + num < 1)
				{
					num = 1 - num11;
				}
				if (num12 + num2 > num8)
				{
					num2 = num8 - num12;
				}
				if (num12 + num2 < 1)
				{
					num2 = 1 - num12;
				}
				parent.aoi_xNUD.Value += (decimal)(int)((double)num / zoom);
				parent.aoi_yNUD.Value += (decimal)(int)((double)num2 / zoom);
				parent.aoi_widthNUD.Value -= (decimal)(int)((double)num / zoom);
				parent.aoi_heightNUD.Value -= (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 2)
			{
				if (num11 + num > num7)
				{
					num = num7 - num11;
				}
				if (num11 + num < 1)
				{
					num = 1 - num11;
				}
				if (num12 + num14 + num2 < num10)
				{
					num2 = num10 - num12 - num14;
				}
				parent.aoi_xNUD.Value += (decimal)(int)((double)num / zoom);
				parent.aoi_widthNUD.Value -= (decimal)(int)((double)num / zoom);
				parent.aoi_heightNUD.Value += (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 3)
			{
				if (num12 + num2 > num8)
				{
					num2 = num8 - num12;
				}
				if (num12 + num2 < 1)
				{
					num2 = 1 - num12;
				}
				if (num11 + num13 + num < num9)
				{
					num = num9 - num11 - num13;
				}
				parent.aoi_widthNUD.Value += (decimal)(int)((double)num / zoom);
				parent.aoi_yNUD.Value += (decimal)(int)((double)num2 / zoom);
				parent.aoi_heightNUD.Value -= (decimal)(int)((double)num2 / zoom);
			}
			if (active_rect == 4)
			{
				if (num11 + num13 + num < num9)
				{
					num = num9 - num11 - num13;
				}
				if (num12 + num14 + num2 < num10)
				{
					num2 = num10 - num12 - num14;
				}
				parent.aoi_widthNUD.Value += (decimal)(int)((double)num / zoom);
				parent.aoi_heightNUD.Value += (decimal)(int)((double)num2 / zoom);
			}
		}
	}

	private void show_afb_aoiCB_CheckedChanged(object sender, EventArgs e)
	{
		if (!show_afb_aoiCB.Checked && !parent.processing_acive())
		{
			parent.do_test_run(detect_threshold >= 0);
		}
		testrunPB.Refresh();
		set_image(show_afb_aoiCB.Checked);
		update_detect_threshold_label();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.components = new System.ComponentModel.Container();
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PIPP_Application.testrunForm));
		this.imageTAB = new System.Windows.Forms.TableLayoutPanel();
		this.panel2 = new System.Windows.Forms.Panel();
		this.vrulerPB = new System.Windows.Forms.PictureBox();
		this.hrulerPB = new System.Windows.Forms.PictureBox();
		this.test_imagePAN = new System.Windows.Forms.Panel();
		this.testrunPB = new System.Windows.Forms.PictureBox();
		this.rmax_valueLAB = new System.Windows.Forms.Label();
		this.zoomGB = new System.Windows.Forms.GroupBox();
		this.zoom_minusBUT = new System.Windows.Forms.Button();
		this.zoomCB = new System.Windows.Forms.ComboBox();
		this.zoom_plusBUT = new System.Windows.Forms.Button();
		this.save_frameBUT = new System.Windows.Forms.Button();
		this.timer1 = new System.Windows.Forms.Timer(this.components);
		this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
		this.lhsPAN = new System.Windows.Forms.Panel();
		this.optionsGB = new System.Windows.Forms.GroupBox();
		this.show_afb_aoiCB = new System.Windows.Forms.CheckBox();
		this.groupBox1 = new System.Windows.Forms.GroupBox();
		this.update_imageBUT = new System.Windows.Forms.Button();
		this.max_pixel_value_greyGB = new System.Windows.Forms.GroupBox();
		this.grey_textLAB = new System.Windows.Forms.Label();
		this.grey_max_valueLAB = new System.Windows.Forms.Label();
		this.max_pixel_value_rgbGB = new System.Windows.Forms.GroupBox();
		this.b_textLAB = new System.Windows.Forms.Label();
		this.g_textLAB = new System.Windows.Forms.Label();
		this.r_textLAB = new System.Windows.Forms.Label();
		this.bmax_valueLAB = new System.Windows.Forms.Label();
		this.gmax_valueLAB = new System.Windows.Forms.Label();
		this.frame_sizeGB = new System.Windows.Forms.GroupBox();
		this.frame_sizeLAB = new System.Windows.Forms.Label();
		this.detect_thresholdGB = new System.Windows.Forms.GroupBox();
		this.detect_thresholdLAB = new System.Windows.Forms.Label();
		this.frame_numberGB = new System.Windows.Forms.GroupBox();
		this.frame_numberLAB = new System.Windows.Forms.Label();
		this.imageTAB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.vrulerPB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.hrulerPB).BeginInit();
		this.test_imagePAN.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.testrunPB).BeginInit();
		this.zoomGB.SuspendLayout();
		this.tableLayoutPanel2.SuspendLayout();
		this.lhsPAN.SuspendLayout();
		this.optionsGB.SuspendLayout();
		this.groupBox1.SuspendLayout();
		this.max_pixel_value_greyGB.SuspendLayout();
		this.max_pixel_value_rgbGB.SuspendLayout();
		this.frame_sizeGB.SuspendLayout();
		this.detect_thresholdGB.SuspendLayout();
		this.frame_numberGB.SuspendLayout();
		base.SuspendLayout();
		this.imageTAB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.imageTAB.BackColor = System.Drawing.Color.White;
		this.imageTAB.ColumnCount = 2;
		this.imageTAB.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 30f));
		this.imageTAB.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
		this.imageTAB.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20f));
		this.imageTAB.Controls.Add(this.panel2, 0, 0);
		this.imageTAB.Controls.Add(this.vrulerPB, 0, 1);
		this.imageTAB.Controls.Add(this.hrulerPB, 1, 0);
		this.imageTAB.Controls.Add(this.test_imagePAN, 1, 1);
		this.imageTAB.Location = new System.Drawing.Point(120, 0);
		this.imageTAB.Margin = new System.Windows.Forms.Padding(0);
		this.imageTAB.Name = "imageTAB";
		this.imageTAB.RowCount = 2;
		this.imageTAB.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30f));
		this.imageTAB.RowStyles.Add(new System.Windows.Forms.RowStyle());
		this.imageTAB.Size = new System.Drawing.Size(160, 300);
		this.imageTAB.TabIndex = 1;
		this.panel2.BackColor = System.Drawing.Color.Cornsilk;
		this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel2.Location = new System.Drawing.Point(0, 0);
		this.panel2.Margin = new System.Windows.Forms.Padding(0);
		this.panel2.Name = "panel2";
		this.panel2.Size = new System.Drawing.Size(30, 30);
		this.panel2.TabIndex = 5;
		this.vrulerPB.Dock = System.Windows.Forms.DockStyle.Top;
		this.vrulerPB.Image = (System.Drawing.Image)resources.GetObject("vrulerPB.Image");
		this.vrulerPB.Location = new System.Drawing.Point(0, 30);
		this.vrulerPB.Margin = new System.Windows.Forms.Padding(0);
		this.vrulerPB.Name = "vrulerPB";
		this.vrulerPB.Size = new System.Drawing.Size(30, 2048);
		this.vrulerPB.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
		this.vrulerPB.TabIndex = 2;
		this.vrulerPB.TabStop = false;
		this.hrulerPB.Dock = System.Windows.Forms.DockStyle.Left;
		this.hrulerPB.Image = PIPP_Application.Properties.Resources.h_ruler_x1;
		this.hrulerPB.Location = new System.Drawing.Point(30, 0);
		this.hrulerPB.Margin = new System.Windows.Forms.Padding(0);
		this.hrulerPB.Name = "hrulerPB";
		this.hrulerPB.Size = new System.Drawing.Size(24000, 30);
		this.hrulerPB.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
		this.hrulerPB.TabIndex = 1;
		this.hrulerPB.TabStop = false;
		this.test_imagePAN.AutoScroll = true;
		this.test_imagePAN.AutoSize = true;
		this.test_imagePAN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.test_imagePAN.Controls.Add(this.testrunPB);
		this.test_imagePAN.Location = new System.Drawing.Point(30, 30);
		this.test_imagePAN.Margin = new System.Windows.Forms.Padding(0);
		this.test_imagePAN.Name = "test_imagePAN";
		this.test_imagePAN.Size = new System.Drawing.Size(30, 30);
		this.test_imagePAN.TabIndex = 6;
		this.test_imagePAN.Scroll += new System.Windows.Forms.ScrollEventHandler(this.test_imagePAN_Scroll);
		this.testrunPB.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
		this.testrunPB.Location = new System.Drawing.Point(0, 0);
		this.testrunPB.Margin = new System.Windows.Forms.Padding(0);
		this.testrunPB.Name = "testrunPB";
		this.testrunPB.Size = new System.Drawing.Size(30, 30);
		this.testrunPB.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
		this.testrunPB.TabIndex = 4;
		this.testrunPB.TabStop = false;
		this.testrunPB.Paint += new System.Windows.Forms.PaintEventHandler(this.testrunPB_Paint);
		this.testrunPB.DoubleClick += new System.EventHandler(this.testrunPB_DoubleClick);
		this.testrunPB.MouseDown += new System.Windows.Forms.MouseEventHandler(this.testrunPB_MouseDown);
		this.testrunPB.MouseMove += new System.Windows.Forms.MouseEventHandler(this.testrunPB_MouseMove);
		this.testrunPB.MouseUp += new System.Windows.Forms.MouseEventHandler(this.testrunPB_MouseUp);
		this.rmax_valueLAB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.rmax_valueLAB.Location = new System.Drawing.Point(2, 16);
		this.rmax_valueLAB.Name = "rmax_valueLAB";
		this.rmax_valueLAB.Size = new System.Drawing.Size(33, 13);
		this.rmax_valueLAB.TabIndex = 2;
		this.rmax_valueLAB.Text = "100%";
		this.rmax_valueLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.zoomGB.Controls.Add(this.zoom_minusBUT);
		this.zoomGB.Controls.Add(this.zoomCB);
		this.zoomGB.Controls.Add(this.zoom_plusBUT);
		this.zoomGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.zoomGB.Location = new System.Drawing.Point(4, 223);
		this.zoomGB.Name = "zoomGB";
		this.zoomGB.Size = new System.Drawing.Size(110, 47);
		this.zoomGB.TabIndex = 4;
		this.zoomGB.TabStop = false;
		this.zoomGB.Text = "Zoom";
		this.zoom_minusBUT.Location = new System.Drawing.Point(4, 17);
		this.zoom_minusBUT.Name = "zoom_minusBUT";
		this.zoom_minusBUT.Size = new System.Drawing.Size(18, 23);
		this.zoom_minusBUT.TabIndex = 13;
		this.zoom_minusBUT.Text = "-";
		this.zoom_minusBUT.UseVisualStyleBackColor = true;
		this.zoom_minusBUT.Click += new System.EventHandler(this.zoom_minusBUT_Click);
		this.zoomCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.zoomCB.FormattingEnabled = true;
		this.zoomCB.Items.AddRange(new object[6] { "400%", "200%", "100%", "50%", "25%", "12.5%" });
		this.zoomCB.Location = new System.Drawing.Point(28, 17);
		this.zoomCB.Name = "zoomCB";
		this.zoomCB.Size = new System.Drawing.Size(51, 21);
		this.zoomCB.TabIndex = 1;
		this.zoomCB.SelectedIndexChanged += new System.EventHandler(this.zoomCB_SelectedIndexChanged);
		this.zoom_plusBUT.Location = new System.Drawing.Point(86, 17);
		this.zoom_plusBUT.Name = "zoom_plusBUT";
		this.zoom_plusBUT.Size = new System.Drawing.Size(18, 23);
		this.zoom_plusBUT.TabIndex = 12;
		this.zoom_plusBUT.Text = "+";
		this.zoom_plusBUT.UseVisualStyleBackColor = true;
		this.zoom_plusBUT.Click += new System.EventHandler(this.zoom_plusBUT_Click);
		this.save_frameBUT.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.save_frameBUT.Location = new System.Drawing.Point(5, 47);
		this.save_frameBUT.Name = "save_frameBUT";
		this.save_frameBUT.Size = new System.Drawing.Size(100, 23);
		this.save_frameBUT.TabIndex = 3;
		this.save_frameBUT.Text = "Save Image";
		this.save_frameBUT.UseVisualStyleBackColor = true;
		this.save_frameBUT.Click += new System.EventHandler(this.button1_Click);
		this.tableLayoutPanel2.AutoSize = true;
		this.tableLayoutPanel2.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tableLayoutPanel2.BackColor = System.Drawing.Color.Cornsilk;
		this.tableLayoutPanel2.ColumnCount = 2;
		this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 120f));
		this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
		this.tableLayoutPanel2.Controls.Add(this.imageTAB, 1, 0);
		this.tableLayoutPanel2.Controls.Add(this.lhsPAN, 0, 0);
		this.tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tableLayoutPanel2.Location = new System.Drawing.Point(0, 0);
		this.tableLayoutPanel2.Name = "tableLayoutPanel2";
		this.tableLayoutPanel2.RowCount = 1;
		this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel2.Size = new System.Drawing.Size(561, 396);
		this.tableLayoutPanel2.TabIndex = 2;
		this.lhsPAN.AutoSize = true;
		this.lhsPAN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.lhsPAN.BackColor = System.Drawing.Color.Cornsilk;
		this.lhsPAN.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.lhsPAN.Controls.Add(this.optionsGB);
		this.lhsPAN.Controls.Add(this.groupBox1);
		this.lhsPAN.Controls.Add(this.zoomGB);
		this.lhsPAN.Controls.Add(this.max_pixel_value_greyGB);
		this.lhsPAN.Controls.Add(this.max_pixel_value_rgbGB);
		this.lhsPAN.Controls.Add(this.frame_sizeGB);
		this.lhsPAN.Controls.Add(this.detect_thresholdGB);
		this.lhsPAN.Controls.Add(this.frame_numberGB);
		this.lhsPAN.Dock = System.Windows.Forms.DockStyle.Fill;
		this.lhsPAN.Location = new System.Drawing.Point(0, 0);
		this.lhsPAN.Margin = new System.Windows.Forms.Padding(0);
		this.lhsPAN.Name = "lhsPAN";
		this.lhsPAN.Padding = new System.Windows.Forms.Padding(4);
		this.lhsPAN.Size = new System.Drawing.Size(120, 396);
		this.lhsPAN.TabIndex = 2;
		this.optionsGB.Controls.Add(this.show_afb_aoiCB);
		this.optionsGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.optionsGB.Location = new System.Drawing.Point(4, 345);
		this.optionsGB.Name = "optionsGB";
		this.optionsGB.Padding = new System.Windows.Forms.Padding(5);
		this.optionsGB.Size = new System.Drawing.Size(110, 40);
		this.optionsGB.TabIndex = 15;
		this.optionsGB.TabStop = false;
		this.optionsGB.Text = "Options";
		this.optionsGB.Visible = false;
		this.show_afb_aoiCB.AutoSize = true;
		this.show_afb_aoiCB.Checked = true;
		this.show_afb_aoiCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.show_afb_aoiCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.show_afb_aoiCB.Location = new System.Drawing.Point(5, 18);
		this.show_afb_aoiCB.Name = "show_afb_aoiCB";
		this.show_afb_aoiCB.Size = new System.Drawing.Size(100, 17);
		this.show_afb_aoiCB.TabIndex = 15;
		this.show_afb_aoiCB.Text = "Show AFB/AOI";
		this.show_afb_aoiCB.UseVisualStyleBackColor = true;
		this.show_afb_aoiCB.CheckedChanged += new System.EventHandler(this.show_afb_aoiCB_CheckedChanged);
		this.groupBox1.Controls.Add(this.save_frameBUT);
		this.groupBox1.Controls.Add(this.update_imageBUT);
		this.groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
		this.groupBox1.Location = new System.Drawing.Point(4, 270);
		this.groupBox1.Name = "groupBox1";
		this.groupBox1.Padding = new System.Windows.Forms.Padding(5);
		this.groupBox1.Size = new System.Drawing.Size(110, 75);
		this.groupBox1.TabIndex = 13;
		this.groupBox1.TabStop = false;
		this.groupBox1.Text = "Control";
		this.update_imageBUT.Dock = System.Windows.Forms.DockStyle.Top;
		this.update_imageBUT.Location = new System.Drawing.Point(5, 18);
		this.update_imageBUT.Name = "update_imageBUT";
		this.update_imageBUT.Size = new System.Drawing.Size(100, 23);
		this.update_imageBUT.TabIndex = 12;
		this.update_imageBUT.Text = "Update Image";
		this.update_imageBUT.UseVisualStyleBackColor = true;
		this.update_imageBUT.Click += new System.EventHandler(this.update_imageBUT_Click);
		this.max_pixel_value_greyGB.Controls.Add(this.grey_textLAB);
		this.max_pixel_value_greyGB.Controls.Add(this.grey_max_valueLAB);
		this.max_pixel_value_greyGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.max_pixel_value_greyGB.Location = new System.Drawing.Point(4, 169);
		this.max_pixel_value_greyGB.Name = "max_pixel_value_greyGB";
		this.max_pixel_value_greyGB.Size = new System.Drawing.Size(110, 54);
		this.max_pixel_value_greyGB.TabIndex = 8;
		this.max_pixel_value_greyGB.TabStop = false;
		this.max_pixel_value_greyGB.Text = "Max Pixel Value";
		this.grey_textLAB.AutoSize = true;
		this.grey_textLAB.BackColor = System.Drawing.Color.Gray;
		this.grey_textLAB.ForeColor = System.Drawing.Color.White;
		this.grey_textLAB.Location = new System.Drawing.Point(37, 35);
		this.grey_textLAB.Name = "grey_textLAB";
		this.grey_textLAB.Size = new System.Drawing.Size(29, 13);
		this.grey_textLAB.TabIndex = 6;
		this.grey_textLAB.Text = "Grey";
		this.grey_textLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.grey_max_valueLAB.Location = new System.Drawing.Point(36, 16);
		this.grey_max_valueLAB.Name = "grey_max_valueLAB";
		this.grey_max_valueLAB.Size = new System.Drawing.Size(33, 13);
		this.grey_max_valueLAB.TabIndex = 3;
		this.grey_max_valueLAB.Text = "100%";
		this.grey_max_valueLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.max_pixel_value_rgbGB.Controls.Add(this.b_textLAB);
		this.max_pixel_value_rgbGB.Controls.Add(this.g_textLAB);
		this.max_pixel_value_rgbGB.Controls.Add(this.r_textLAB);
		this.max_pixel_value_rgbGB.Controls.Add(this.bmax_valueLAB);
		this.max_pixel_value_rgbGB.Controls.Add(this.gmax_valueLAB);
		this.max_pixel_value_rgbGB.Controls.Add(this.rmax_valueLAB);
		this.max_pixel_value_rgbGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.max_pixel_value_rgbGB.Location = new System.Drawing.Point(4, 115);
		this.max_pixel_value_rgbGB.Name = "max_pixel_value_rgbGB";
		this.max_pixel_value_rgbGB.Size = new System.Drawing.Size(110, 54);
		this.max_pixel_value_rgbGB.TabIndex = 5;
		this.max_pixel_value_rgbGB.TabStop = false;
		this.max_pixel_value_rgbGB.Text = "Max Pixel Value";
		this.b_textLAB.AutoSize = true;
		this.b_textLAB.BackColor = System.Drawing.Color.Blue;
		this.b_textLAB.ForeColor = System.Drawing.Color.White;
		this.b_textLAB.Location = new System.Drawing.Point(79, 35);
		this.b_textLAB.Name = "b_textLAB";
		this.b_textLAB.Size = new System.Drawing.Size(14, 13);
		this.b_textLAB.TabIndex = 7;
		this.b_textLAB.Text = "B";
		this.b_textLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.g_textLAB.AutoSize = true;
		this.g_textLAB.BackColor = System.Drawing.Color.Green;
		this.g_textLAB.ForeColor = System.Drawing.Color.White;
		this.g_textLAB.Location = new System.Drawing.Point(44, 35);
		this.g_textLAB.Name = "g_textLAB";
		this.g_textLAB.Size = new System.Drawing.Size(15, 13);
		this.g_textLAB.TabIndex = 6;
		this.g_textLAB.Text = "G";
		this.g_textLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.r_textLAB.AutoSize = true;
		this.r_textLAB.BackColor = System.Drawing.Color.Red;
		this.r_textLAB.ForeColor = System.Drawing.Color.White;
		this.r_textLAB.Location = new System.Drawing.Point(9, 35);
		this.r_textLAB.Name = "r_textLAB";
		this.r_textLAB.Size = new System.Drawing.Size(15, 13);
		this.r_textLAB.TabIndex = 5;
		this.r_textLAB.Text = "R";
		this.r_textLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.bmax_valueLAB.Location = new System.Drawing.Point(72, 16);
		this.bmax_valueLAB.Name = "bmax_valueLAB";
		this.bmax_valueLAB.Size = new System.Drawing.Size(33, 13);
		this.bmax_valueLAB.TabIndex = 4;
		this.bmax_valueLAB.Text = "100%";
		this.bmax_valueLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.gmax_valueLAB.Location = new System.Drawing.Point(37, 16);
		this.gmax_valueLAB.Name = "gmax_valueLAB";
		this.gmax_valueLAB.Size = new System.Drawing.Size(33, 13);
		this.gmax_valueLAB.TabIndex = 3;
		this.gmax_valueLAB.Text = "100%";
		this.gmax_valueLAB.TextAlign = System.Drawing.ContentAlignment.TopCenter;
		this.frame_sizeGB.Controls.Add(this.frame_sizeLAB);
		this.frame_sizeGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.frame_sizeGB.Location = new System.Drawing.Point(4, 78);
		this.frame_sizeGB.Name = "frame_sizeGB";
		this.frame_sizeGB.Size = new System.Drawing.Size(110, 37);
		this.frame_sizeGB.TabIndex = 9;
		this.frame_sizeGB.TabStop = false;
		this.frame_sizeGB.Text = "Frame Size";
		this.frame_sizeLAB.AutoSize = true;
		this.frame_sizeLAB.Location = new System.Drawing.Point(6, 16);
		this.frame_sizeLAB.Name = "frame_sizeLAB";
		this.frame_sizeLAB.Size = new System.Drawing.Size(51, 13);
		this.frame_sizeLAB.TabIndex = 0;
		this.frame_sizeLAB.Text = "100 x100";
		this.detect_thresholdGB.Controls.Add(this.detect_thresholdLAB);
		this.detect_thresholdGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.detect_thresholdGB.Location = new System.Drawing.Point(4, 41);
		this.detect_thresholdGB.Name = "detect_thresholdGB";
		this.detect_thresholdGB.Size = new System.Drawing.Size(110, 37);
		this.detect_thresholdGB.TabIndex = 10;
		this.detect_thresholdGB.TabStop = false;
		this.detect_thresholdGB.Text = "Detect Threshold";
		this.detect_thresholdLAB.AutoSize = true;
		this.detect_thresholdLAB.Location = new System.Drawing.Point(6, 16);
		this.detect_thresholdLAB.Name = "detect_thresholdLAB";
		this.detect_thresholdLAB.Size = new System.Drawing.Size(19, 13);
		this.detect_thresholdLAB.TabIndex = 0;
		this.detect_thresholdLAB.Text = "40";
		this.frame_numberGB.Controls.Add(this.frame_numberLAB);
		this.frame_numberGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.frame_numberGB.Location = new System.Drawing.Point(4, 4);
		this.frame_numberGB.Name = "frame_numberGB";
		this.frame_numberGB.Size = new System.Drawing.Size(110, 37);
		this.frame_numberGB.TabIndex = 11;
		this.frame_numberGB.TabStop = false;
		this.frame_numberGB.Text = "Frame Number";
		this.frame_numberLAB.AutoSize = true;
		this.frame_numberLAB.Location = new System.Drawing.Point(6, 16);
		this.frame_numberLAB.Name = "frame_numberLAB";
		this.frame_numberLAB.Size = new System.Drawing.Size(19, 13);
		this.frame_numberLAB.TabIndex = 0;
		this.frame_numberLAB.Text = "40";
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.AutoSize = true;
		base.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.BackColor = System.Drawing.SystemColors.Control;
		base.ClientSize = new System.Drawing.Size(561, 396);
		base.Controls.Add(this.tableLayoutPanel2);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Fixed3D;
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.MaximizeBox = false;
		base.Name = "testrunForm";
		this.Text = "PIPP - Frame Viewer";
		this.imageTAB.ResumeLayout(false);
		this.imageTAB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.vrulerPB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.hrulerPB).EndInit();
		this.test_imagePAN.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.testrunPB).EndInit();
		this.zoomGB.ResumeLayout(false);
		this.tableLayoutPanel2.ResumeLayout(false);
		this.tableLayoutPanel2.PerformLayout();
		this.lhsPAN.ResumeLayout(false);
		this.optionsGB.ResumeLayout(false);
		this.optionsGB.PerformLayout();
		this.groupBox1.ResumeLayout(false);
		this.max_pixel_value_greyGB.ResumeLayout(false);
		this.max_pixel_value_greyGB.PerformLayout();
		this.max_pixel_value_rgbGB.ResumeLayout(false);
		this.max_pixel_value_rgbGB.PerformLayout();
		this.frame_sizeGB.ResumeLayout(false);
		this.frame_sizeGB.PerformLayout();
		this.detect_thresholdGB.ResumeLayout(false);
		this.detect_thresholdGB.PerformLayout();
		this.frame_numberGB.ResumeLayout(false);
		this.frame_numberGB.PerformLayout();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
