using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using PIPP_Application.Properties;
using Opipp.Core.Pipeline;

namespace PIPP_Application;

public class Form1 : Form
{
	private int flat_dark_frames_stacked;

	private int flat_frames_stacked;

	private int dark_frames_stacked;

	private string version = "v2.5.9";

	private string output_directory;

	private string output_directory_to_open;

	private bool processed_killed;

	private bool pipp_process_running;

	private bool pipp_output_received;

	private bool processing_failed;

	private cPersistantData persistant_data = new cPersistantData();

	private testrunForm test_run_dialog;

	private bool test_run;

	private bool show_detected_pixels;

	private string output_path;

	private string error_data = "";

	private int xcentre;

	private int ycentre;

	private int max_pixel = -1;

	private int max_b_pixel = -1;

	private int max_g_pixel = -1;

	private int max_r_pixel = -1;

	private int detect_threshold = -1;

	private int frame_number = -1;

	private int frame_count = -1;

	private string quality_sort_label1 = "Sorting ";

	private string quality_sort_label2 = " frames by quality";

	private infodialog helpinfo;

	private string temp_directory;

	private string master_dark_tmp_file = "";

	private string master_dark_pgm_file = "";

	private string master_flat_dark_tmp_file = "";

	private string master_flat_dark_pgm_file = "";

	private string master_flat_tmp_file = "";

	private Stopwatch stopwatch;

	private List<string> arg_list2;

	private IContainer components;

	private MenuStrip menuStrip1;

	private ToolStripMenuItem fileToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem1;

	private ToolStripMenuItem menuQuit;

	private OpenFileDialog openFD;

	private OpenFileDialog openoptionsFD;

	private ToolTip toolTip1;

	private ToolStripMenuItem processToolStripMenuItem;

	private ToolStripMenuItem testOptionsToolStripMenuItem;

	private ToolStripMenuItem doProcessToolStripMenuItem;

	private ToolStripMenuItem helpToolStripMenuItem;

	private ToolStripMenuItem aboutToolStripMenuItem;

	public FolderBrowserDialog outputdirectoryFBD;

	private Process pipp_process;

	private ToolStripMenuItem addSourceFilesToolStripMenuItem;

	private ToolStripMenuItem cancelProcessingToolStripMenuItem;

	private ToolStripMenuItem optionsToolStripMenuItem;

	private ToolStripMenuItem resetOptionsToolStripMenuItem;

	private ToolStripMenuItem loadOptionsToolStripMenuItem;

	private ToolStripMenuItem saveOptionsToolStripMenuItem;

	private ToolStripSeparator toolStripSeparator1;

	private ToolStripMenuItem saveRejectedFramesToolStripMenuItem;

	private SaveFileDialog saveoptionsFD;

	private Button dotestrunBTN;

	private ToolStripMenuItem removeAllFilesToolStripMenuItem;

	private TabControl tabControl1;

	private TabPage tabPage1;

	private TableLayoutPanel tableLayoutPanel1;

	private Button remove_all_button;

	private Button remove_button;

	private Button load_button;

	private GroupBox groupBox3;

	private CheckBox issCB;

	private CheckBox solar_closeupCB;

	private CheckBox solar_full_discCB;

	private CheckBox planetaryCB;

	private GroupBox limitframesGB;

	private Label label8;

	private NumericUpDown endframeNUD;

	private CheckBox framelimitCB;

	private GroupBox runtypeGB;

	private RadioButton joinmodeRB;

	private RadioButton batchmodeRB;

	private GroupBox image_filesGB;

	private TabPage tabPage7;

	private SplitContainer splitContainer7;

	private TabPage tabPage2;

	private SplitContainer splitContainer2;

	private GroupBox histostretchGB;

	private CheckBox rgbhistoCB;

	private Label label11;

	private NumericUpDown hs_white_pointNUD;

	private CheckBox hs_white_pointCB;

	private GroupBox croppingGB;

	private Label label7;

	private Label label6;

	private NumericUpDown yoffsetNUD;

	private NumericUpDown xoffsetNUD;

	private Label label5;

	private NumericUpDown cropheightNUD;

	private Label label4;

	private NumericUpDown cropwidthNUD;

	private CheckBox enablecropCB;

	private GroupBox planetdetectGB;

	private CheckBox centreplanetCB;

	private GroupBox minpixelvalueGB;

	private CheckBox autothresholdCB;

	private Label label2;

	private Label label1;

	private NumericUpDown minsizeNUD;

	private NumericUpDown thresholdNUD;

	private CheckBox checkplanetCB;

	private GroupBox groupBox5;

	private Label label3;

	private CheckBox overexposedCB;

	private TabPage tabPage3;

	private SplitContainer splitContainer4;

	private GroupBox qualityoptionsGB;

	private Label label13;

	private Label label12;

	private Label label10;

	private NumericUpDown downsampleincNUD;

	private NumericUpDown maxdownsampleNUD;

	private NumericUpDown mindownsampleNUD;

	private GroupBox qualityalgorithmGB;

	private RadioButton totalpixelalgorithmCB;

	private RadioButton histoalgorithmCB;

	private RadioButton ninoxalgorithmCB;

	private RadioButton defaultalgorithmCB;

	private GroupBox qualitylimitingGB;

	private CheckBox qualityframelimitCB;

	private NumericUpDown qualityframesNUD;

	private GroupBox qualityenableGB;

	private CheckBox qualitysortingCB;

	private CheckBox qreorderCB;

	private GroupBox qualityweightGB;

	private CheckBox qualityweightingCB;

	private TabPage tabPage4;

	private SplitContainer splitContainer5;

	private GroupBox renumberGB;

	private CheckBox noframerenumberCB;

	private CheckBox avi_fpsCB;

	private Label fpsFUDlabel;

	private NumericUpDown avi_fpsNUD;

	private GroupBox groupBox8;

	private Label outtype_label;

	private RadioButton outputtifRB;

	private RadioButton outputbmpRB;

	private RadioButton outputserRB;

	private GroupBox outputprefixGB;

	private TextBox outputprefixTB;

	private GroupBox outputfiledirectoryGB;

	private TextBox outputdirectoryTB;

	private Button defaultouputdirBUT;

	private Button outputdirectoryBUT;

	private GroupBox framedropGB;

	private Label label14;

	private NumericUpDown outputframedropNUD;

	private TabPage tabPage6;

	private SplitContainer splitContainer6;

	private GroupBox mainprocessingGB;

	private Label fileLAB;

	private Label processfileLAB;

	private Label qsframeLAB;

	private ProgressBar processfilePB;

	private Label qualityLAB;

	private Label processframesLAB;

	private ProgressBar qualityPB;

	private Label progressLAB;

	private ProgressBar processPB;

	private GroupBox statusGB;

	private Label statusstateLAB;

	private GroupBox controlGB;

	private Button killpippBTN;

	private Button runBTN;

	private GroupBox outputGB;

	private TextBox outputTB;

	private GroupBox avifilesGB;

	private CheckBox y800decodestrictCB;

	private GroupBox rgbsplitGB;

	private CheckBox rgbsplitCB;

	private CheckBox protectbayerCB;

	private GroupBox convertomonoGB;

	private CheckBox convmonoCB;

	private GroupBox debayerGB;

	private GroupBox debayerenableGB;

	private CheckBox debayerframesCB;

	private Label label9;

	private NumericUpDown startframeNUD;

	private GroupBox serfilesGB;

	private ComboBox serbitdepthCB;

	private Label label15;

	private GroupBox ipframedroppingGB;

	private Label label16;

	private NumericUpDown ipframedropNUD;

	private GroupBox colourdetectionGB;

	private ComboBox colourdetectionCB;

	private PictureBox bayerPB;

	private PictureBox serPB;

	private PictureBox colour_monoPB;

	private PictureBox cropPB;

	private PictureBox pictureBox2;

	private PictureBox objectdetectPB;

	private PictureBox pictureBox3;

	private PictureBox overexposedPB;

	private PictureBox framerangePB;

	private PictureBox framedroppingPB;

	private PictureBox debayerpatternPB;

	private PictureBox splitrgbPB;

	private PictureBox pictureBox4;

	private PictureBox formatPB;

	private PictureBox outputframedroppingPB;

	private PictureBox norenumberPB;

	private PictureBox pictureBox5;

	private GroupBox forward_playGB;

	private PictureBox pictureBox6;

	private Label animation_repeat_allLAB;

	private NumericUpDown animation_repeat_allNUD;

	private Label repeat_forward_lastLAB;

	private NumericUpDown repeat_forward_lastNUD;

	private CheckBox planetary_animation_aviCB;

	private TextBox datetimeTB;

	private GroupBox winjuposGB;

	private CheckBox winjuposfilenameCB;

	private Label label19;

	private NumericUpDown inputframespersecondNUD;

	private ComboBox inputdatetimeformatCB;

	private PictureBox winjuposPB;

	private CheckBox date_time_extract_conv_utCB;

	private Panel limitframesPANEL;

	private Panel panel1;

	private ComboBox limitframesCB;

	private Panel limitframesPANEL2;

	private NumericUpDown numframesNUD;

	private Label label22;

	private Panel winjupossubPANEL;

	private Label label20;

	private ComboBox winjupostimeCB;

	private Panel enablecropping3PANEL;

	private Panel overexposedPANEL;

	private NumericUpDown overexposedNUD;

	private Label wjpos_extractedLAB;

	private Label wjpos_filenameLAB;

	private Label wjpos_extracted2LAB;

	private Label wjpos_filename2LAB;

	private CheckBox wjpos_rememberCB;

	private GroupBox groupBox1;

	private ComboBox quality_limiting_typeCB;

	private GroupBox raw_file_optionsGB;

	private ComboBox raw_debayer_algorithmCB;

	private CheckBox raw_image_debayerCB;

	private Label label21;

	private Panel raw_colour_optionsPANEL;

	private Label label23;

	private ComboBox raw_output_colour_spaceCB;

	private PictureBox pictureBox8;

	private PictureBox pictureBox9;

	private Label label24;

	private ComboBox raw_highlight_recovery_typeCB;

	private Button file_down_BUT;

	private Button file_up_BUT;

	private Panel panel2;

	private GroupBox avi_outputGB;

	private PictureBox pictureBox1;

	private CheckBox old_format_aviCB;

	private ToolStripMenuItem undoResetOptionsToolStripMenuItem;

	private ToolStripSeparator toolStripSeparator2;

	private ToolStripMenuItem loadOptionsFromLastSessionToolStripMenuItem;

	private Label label25;

	private ComboBox avi_codecCB;

	private Label label26;

	private NumericUpDown codec_qualityNUD;

	private Panel codec_qualityGB;

	private Label label27;

	private Panel old_format_aviPAN;

	private TabPage tabPage5;

	private SplitContainer splitContainer1;

	private CheckBox repeat_forward_lastCB;

	private GroupBox sequence_repeatGB;

	private CheckBox animation_repeat_allCB;

	private PictureBox pictureBox10;

	private GroupBox reverse_playGB;

	private CheckBox animation_reverseCB;

	private PictureBox pictureBox7;

	private Panel panel3;

	private CheckBox animation_forwardCB;

	private Panel repeat_reverse_lastPAN;

	private Label repeat_reverse_lastLAB;

	private NumericUpDown repeat_reverse_lastNUD;

	private CheckBox repeat_reverse_lastCB;

	private Panel panel5;

	private CheckBox animation_reverse_not_in_last_repeatCB;

	private GroupBox groupBox9;

	private Label label17;

	private Label label29;

	private Label label28;

	private Label label18;

	private Label avi_settings1LAB;

	private Label avi_settings4LAB;

	private Label avi_settings3LAB;

	private Label avi_settings2LAB;

	private RadioButton outputaviRB;

	private NumericUpDown input_gainNUD;

	private Label input_gainLAB;

	private GroupBox input_gainGB;

	private CheckBox raw_hpfCB;

	private Label label30;

	private ComboBox edge_in_shadowCB;

	private Panel edge_of_shadowPAN;

	private PictureBox moon_shadowPB;

	private ComboBox conv_mono_comboBox;

	private GroupBox noise_filterGB;

	private CheckBox median_filterCB;

	private CheckBox hs_black_pointCB;

	private Button test_detect_thresholdBUT;

	private GroupBox groupBox6;

	private GroupBox flip_rotateGB;

	private ComboBox rotationCB;

	private CheckBox flip_yCB;

	private CheckBox flip_xCB;

	private PictureBox rotatePB;

	private NumericUpDown gammaNUD;

	private Label label31;

	private Panel prefix_PAN;

	private GroupBox groupBox7;

	private TextBox outputsuffixTB;

	private CheckBox avi_archiveCB;

	private ComboBox old_format_max_sizeCB;

	private CheckBox avi_safe_frame_widthCB;

	private Label animframeLAB;

	private Label animLAB;

	private ProgressBar animPB;

	private Panel anim_progPAN;

	private Panel qual_progressPAN;

	private Panel panel7;

	private Panel panel8;

	private Panel enableoffsetPANEL;

	private Panel enablecroppingPANEL;

	private c_filelist image_filelist;

	private TabControl image_filesTAB_CONTROL;

	private TabPage tab_image_files;

	private TabPage tab_dark_files;

	private TabPage tab_flat_files;

	private TabPage tab_flat_dark_files;

	private TableLayoutPanel tableLayoutPanel3;

	private GroupBox dark_filesGB;

	private c_filelist dark_filelist;

	private Panel panel6;

	private Button add_dark_filesBUT;

	private Button remove_dark_filesBUT;

	private Button remove_all_dark_filesBUT;

	private TableLayoutPanel tableLayoutPanel4;

	private GroupBox flat_filesGB;

	private c_filelist flat_filelist;

	private Panel panel9;

	private Button add_flat_filesBUT;

	private Button remove_flat_filesBUT;

	private Button remove_all_flat_filesBUT;

	private TableLayoutPanel tableLayoutPanel5;

	private GroupBox flat_dark_filesGB;

	private c_filelist flat_dark_filelist;

	private Panel panel10;

	private Button add_flat_dark_filesBUT;

	private Button remove_flat_dark_filesBUT;

	private Button remove_all_flat_dark_filesBUT;

	private GroupBox date_timeGB;

	private CheckBox extract_date_timeCB;

	private PictureBox pictureBox11;

	private ComboBox debay_typeCB;

	private ComboBox debay_patternCB;

	private Label db_patternLAB;

	private Label db_algorithmLAB;

	private PictureBox pictureBox12;

	private PictureBox pictureBox13;

	private ToolStripMenuItem check_updatesMenuItem;

	private CheckBox create_output_subdirCB;

	private ToolStripMenuItem use_ram_buffersMenuItem;

	private ToolStripSeparator toolStripSeparator3;

	private RadioButton outputfitsRB;

	private CheckBox addleadingzerosCB;

	private RadioButton outputgifRB;

	private CheckBox includequalityvalueCB;

	private GroupBox dark_frame_optionsGB;

	private CheckBox dark_frame_subtractionCB;

	private CheckBox save_master_dark_CB;

	private Label hot_pixel_thresholdLAB;

	private NumericUpDown hot_pixel_thresholdNUD;

	private CheckBox replace_hot_pixelsCB;

	private GroupBox hot_pixels_optionsGB;

	private CheckBox dark_data_has_bayerCB;

	private GroupBox flat_frame_optionsGB;

	private CheckBox save_master_flat_CB;

	private CheckBox flat_frame_calibrationCB;

	private GroupBox flat_dark_frame_optionsGB;

	private CheckBox save_master_flat_dark_CB;

	private CheckBox flat_dark_frame_subtractionCB;

	private GroupBox calibrationGB;

	private Panel flat_stacking_progress_PAN;

	private Label flat_frame_stackingLAB;

	private ProgressBar flat_stackingPB;

	private Label flat_stackingLAB;

	private Panel flat_dark_stacking_progress_PAN;

	private Label flat_dark_frame_stackingLAB;

	private ProgressBar flat_dark_stackingPB;

	private Label flat_dark_stackingLAB;

	private Panel dark_stacking_progress_PAN;

	private Label dark_frame_stackingLAB;

	private ProgressBar dark_stackingPB;

	private Label dark_stackingLAB;

	private CheckBox flat_data_has_bayerCB;

	private GroupBox surface_stabiliseGB;

	private Label label33;

	private Label label32;

	public NumericUpDown afb_yNUD;

	public NumericUpDown afb_xNUD;

	private PictureBox pictureBox14;

	private CheckBox enablestabilisationCB;

	private GroupBox anchor_feature_boxGB;

	private CheckBox afb_rejectCB;

	private GroupBox area_of_interestGB;

	private CheckBox aoi_cropCB;

	private Label label36;

	private Label label38;

	public NumericUpDown aoi_heightNUD;

	public NumericUpDown aoi_widthNUD;

	private CheckBox aoi_rejectCB;

	private Label label35;

	private Label label37;

	public NumericUpDown aoi_yNUD;

	public NumericUpDown aoi_xNUD;

	private CheckBox aoi_enableCB;

	private GroupBox groupBox10;

	private RadioButton stabilise_surfaceRB;

	private RadioButton stabilise_objectRB;

	private RadioButton stabilise_noneRB;

	private Label label39;

	private Label label40;

	public NumericUpDown afb_heightNUD;

	public NumericUpDown afb_widthNUD;

	private GroupBox output_subdirectoryGB;

	private CheckBox subdir_append_date_timeCB;

	private TextBox subdir_nameTB;

	private GroupBox subdir_nameGB;

	private GroupBox animimated_gif_optionsGB;

	private Label label34;

	private NumericUpDown gif_fpsNUD;

	private CheckBox gif_fpsCB;

	private CheckBox gif_repeat_foreverCB;

	private Label label41;

	private PictureBox pictureBox15;

	private CheckBox planetary_animation_gifCB;

	private GroupBox binningGB;

	private ComboBox binningCB;

	private Label label42;

	private PictureBox pictureBox16;

	private RadioButton outputpngRB;

	private CheckBox open_folderCB;

	private CheckBox keepimagefilenamesCB;

	private CheckBox enableSurfaceTrackingCB;

	private ComboBox binning_calculationCB;

	private Label label43;

	private Label label44;

	private ComboBox gif_adv_optionsCB;

	private GroupBox gif_advanced_optionsGB;

	private CheckBox gif_lossy_comp_levelCB;

	private CheckBox gif_bit_depthCB;

	private CheckBox gif_trans_pixel_tolCB;

	private CheckBox gif_border_tolCB;

	private NumericUpDown gif_lossy_comp_levelNUD;

	private NumericUpDown gif_bit_depthNUD;

	private NumericUpDown gif_trans_pixel_tolNUD;

	private NumericUpDown gif_border_tolNUD;

	private Button output_folderBTN;

	private GroupBox groupBox2;

	private Label resize_heightLAB;

	public NumericUpDown resize_heightNUD;

	private Label resize_widthLAB;

	public NumericUpDown resize_widthNUD;

	private CheckBox resize_keep_aspect_ratioCB;

	private CheckBox resize_framesCB;

	private PictureBox pictureBox17;

	private Label label45;

	private ComboBox gif_quantCB;

	private int last_source_file_frame_count;

	private void animation_forwardCB_CheckedChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void repeat_forward_lastCB_CheckedChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void animation_reverseCB_CheckedChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void repeat_reverse_lastCB_CheckedChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void animation_repeat_allCB_CheckedChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void animation_repeat_foreverCB_CheckedChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void set_animation_options_details()
	{
		if (outputaviRB.Checked)
		{
			avi_settings1LAB.Text = "AVI";
			avi_settings2LAB.Text = avi_codecCB.Text;
			if (avi_codecCB.SelectedIndex == 2)
			{
				avi_settings3LAB.Text = codec_qualityNUD.Value.ToString();
			}
			else
			{
				avi_settings3LAB.Text = "N/A";
			}
			repeat_reverse_lastPAN.Enabled = animation_reverseCB.Checked;
			avi_settings4LAB.Text = avi_fpsNUD.Value.ToString();
			forward_playGB.Enabled = true;
			reverse_playGB.Enabled = true;
			sequence_repeatGB.Enabled = true;
			if (repeat_forward_lastCB.Checked)
			{
				repeat_forward_lastNUD.Enabled = true;
				repeat_forward_lastLAB.Enabled = true;
			}
			else
			{
				repeat_forward_lastNUD.Enabled = false;
				repeat_forward_lastLAB.Enabled = false;
			}
			if (repeat_reverse_lastCB.Checked)
			{
				repeat_reverse_lastNUD.Enabled = true;
				repeat_reverse_lastLAB.Enabled = true;
			}
			else
			{
				repeat_reverse_lastNUD.Enabled = false;
				repeat_reverse_lastLAB.Enabled = false;
			}
			if (!animation_reverseCB.Checked && !animation_forwardCB.Checked)
			{
				animation_reverseCB.Checked = true;
			}
			if (animation_repeat_allCB.Checked)
			{
				animation_repeat_allNUD.Enabled = true;
				animation_repeat_allLAB.Enabled = true;
				if (animation_forwardCB.Checked && animation_reverseCB.Checked)
				{
					animation_reverse_not_in_last_repeatCB.Enabled = true;
				}
				else
				{
					animation_reverse_not_in_last_repeatCB.Enabled = false;
				}
			}
			else
			{
				animation_repeat_allNUD.Enabled = false;
				animation_repeat_allLAB.Enabled = false;
				animation_reverse_not_in_last_repeatCB.Enabled = false;
			}
		}
		else if (outputgifRB.Checked)
		{
			avi_settings1LAB.Text = "Animated GIF";
			avi_settings2LAB.Text = "N/A";
			avi_settings3LAB.Text = "N/A";
			avi_settings4LAB.Text = gif_fpsNUD.Value.ToString();
			forward_playGB.Enabled = true;
			reverse_playGB.Enabled = true;
			sequence_repeatGB.Enabled = true;
			repeat_reverse_lastPAN.Enabled = animation_reverseCB.Checked;
			if (repeat_reverse_lastCB.Checked)
			{
				repeat_reverse_lastNUD.Enabled = true;
				repeat_reverse_lastLAB.Enabled = true;
			}
			else
			{
				repeat_reverse_lastNUD.Enabled = false;
				repeat_reverse_lastLAB.Enabled = false;
			}
			if (!animation_reverseCB.Checked && !animation_forwardCB.Checked)
			{
				animation_reverseCB.Checked = true;
			}
			if (repeat_forward_lastCB.Checked)
			{
				repeat_forward_lastNUD.Enabled = true;
				repeat_forward_lastLAB.Enabled = true;
			}
			else
			{
				repeat_forward_lastNUD.Enabled = false;
				repeat_forward_lastLAB.Enabled = false;
			}
			animation_reverse_not_in_last_repeatCB.Enabled = false;
			if (animation_repeat_allCB.Checked)
			{
				if (gif_repeat_foreverCB.Checked)
				{
					animation_repeat_allNUD.Enabled = false;
					animation_repeat_allLAB.Enabled = false;
				}
				else
				{
					animation_repeat_allNUD.Enabled = true;
					animation_repeat_allLAB.Enabled = true;
				}
				if (animation_forwardCB.Checked && animation_reverseCB.Checked)
				{
					if (gif_repeat_foreverCB.Enabled && gif_repeat_foreverCB.Checked)
					{
						animation_repeat_allNUD.Enabled = false;
					}
					else
					{
						animation_repeat_allNUD.Enabled = true;
					}
				}
			}
			else
			{
				animation_repeat_allNUD.Enabled = false;
				animation_repeat_allLAB.Enabled = false;
			}
		}
		else
		{
			if (outputserRB.Checked)
			{
				avi_settings1LAB.Text = "SER (GIF or AVI Required)";
			}
			else if (outputtifRB.Checked)
			{
				avi_settings1LAB.Text = "TIF (GIF or AVI Required)";
			}
			else if (outputpngRB.Checked)
			{
				avi_settings1LAB.Text = "PNG (GIF or AVI Required)";
			}
			else
			{
				avi_settings1LAB.Text = "BMP (GIF or AVI Required)";
			}
			avi_settings2LAB.Text = "N/A";
			avi_settings3LAB.Text = "N/A";
			avi_settings4LAB.Text = "N/A";
			forward_playGB.Enabled = false;
			reverse_playGB.Enabled = false;
			sequence_repeatGB.Enabled = false;
		}
	}

	private void reset_animation_options()
	{
		animation_forwardCB.Checked = true;
		repeat_forward_lastCB.Checked = false;
		repeat_forward_lastNUD.Value = 1m;
		animation_reverseCB.Checked = false;
		repeat_reverse_lastCB.Checked = false;
		repeat_reverse_lastNUD.Value = 1m;
		animation_repeat_allCB.Checked = false;
		animation_repeat_allNUD.Value = 1m;
		animation_reverse_not_in_last_repeatCB.Checked = true;
	}

	private bool generate_cmdline_options_animation_options_tab(ref List<string> arg_list)
	{
		bool result = false;
		if (animation_forwardCB.Enabled && animation_forwardCB.Checked)
		{
			arg_list.Add("-anim_fwd");
			if (repeat_forward_lastCB.Checked)
			{
				result = true;
				arg_list.Add("-anim_fwd_lf_rep=" + repeat_forward_lastNUD.Value);
			}
		}
		if (animation_reverseCB.Enabled && animation_reverseCB.Checked)
		{
			result = true;
			arg_list.Add("-anim_rev");
			if (repeat_reverse_lastCB.Checked)
			{
				arg_list.Add("-anim_rev_lf_rep=" + repeat_reverse_lastNUD.Value);
			}
		}
		if (animation_repeat_allCB.Enabled && animation_repeat_allCB.Checked)
		{
			result = true;
			arg_list.Add("-anim_all_rep=" + animation_repeat_allNUD.Value);
			if (animation_reverse_not_in_last_repeatCB.Checked)
			{
				arg_list.Add("-no_anim_rev_last_rep");
			}
		}
		return result;
	}

	private void flat_dark_filelist_FileListUpdated(object sender, EventArgs e)
	{
		FileListEventArgs e2 = (FileListEventArgs)e;
		if (flat_dark_filelist.Items.Count == 0)
		{
			remove_flat_dark_filesBUT.Enabled = false;
			remove_all_flat_dark_filesBUT.Enabled = false;
			flat_dark_frame_optionsGB.Enabled = false;
		}
		else
		{
			remove_all_flat_dark_filesBUT.Enabled = true;
			flat_dark_frame_optionsGB.Enabled = true;
		}
		flat_dark_frames_stacked = 0;
		tab_flat_dark_files.Text = "Flat Dark Files (" + e2.FrameCount + " frames)";
	}

	private void add_flat_dark_filesBUT_Click(object sender, EventArgs e)
	{
		flat_dark_filelist.addsourceBTN_Click(sender, e);
	}

	private void remove_flat_dark_filesBUT_Click(object sender, EventArgs e)
	{
		flat_dark_filelist.remove_button_Click(sender, e);
	}

	private void remove_all_flat_dark_filesBUT_Click(object sender, EventArgs e)
	{
		flat_dark_filelist.remove_all_button_Click(sender, e);
	}

	private void flat_dark_filelist_ItemSelectionChanged(object sender, EventArgs e)
	{
		if (flat_dark_filelist.SelectedItems.Count > 0)
		{
			remove_flat_dark_filesBUT.Enabled = true;
		}
		else
		{
			remove_flat_dark_filesBUT.Enabled = false;
		}
	}

	private void flat_dark_stbtraction_cb_changed(object sender, EventArgs e)
	{
		flat_frames_stacked = 0;
		if (flat_dark_frame_subtractionCB.Enabled && flat_dark_frame_subtractionCB.Checked)
		{
			save_master_flat_dark_CB.Enabled = true;
		}
		else
		{
			save_master_flat_dark_CB.Enabled = false;
		}
	}

	private void reset_source_files_flat_dark_files()
	{
		flat_dark_frame_subtractionCB.Checked = true;
		save_master_flat_CB.Checked = false;
	}

	private int generate_cmdline_options_source_flat_dark_files_tab(ref List<string> arg_list)
	{
		if (flat_dark_frame_subtractionCB.Checked)
		{
			arg_list.Add("-flat_dark");
		}
		if (save_master_flat_dark_CB.Checked)
		{
			arg_list.Add("-save_flat_dark");
		}
		return 1;
	}

	private void flat_filelist_FileListUpdated(object sender, EventArgs e)
	{
		FileListEventArgs e2 = (FileListEventArgs)e;
		if (flat_filelist.Items.Count == 0)
		{
			remove_flat_filesBUT.Enabled = false;
			remove_all_flat_filesBUT.Enabled = false;
			flat_frame_optionsGB.Enabled = false;
		}
		else
		{
			remove_all_flat_filesBUT.Enabled = true;
			flat_frame_optionsGB.Enabled = true;
		}
		flat_frames_stacked = 0;
		tab_flat_files.Text = "Flat Files (" + e2.FrameCount + " frames)";
	}

	private void add_flat_filesBUT_Click(object sender, EventArgs e)
	{
		flat_filelist.addsourceBTN_Click(sender, e);
	}

	private void remove_flat_filesBUT_Click(object sender, EventArgs e)
	{
		flat_filelist.remove_button_Click(sender, e);
	}

	private void remove_all_flat_filesBUT_Click(object sender, EventArgs e)
	{
		flat_filelist.remove_all_button_Click(sender, e);
	}

	private void flat_filelist_ItemSelectionChanged(object sender, EventArgs e)
	{
		if (flat_filelist.SelectedItems.Count > 0)
		{
			remove_flat_filesBUT.Enabled = true;
		}
		else
		{
			remove_flat_filesBUT.Enabled = false;
		}
	}

	private void handle_flat_file_enables(object sender, EventArgs e)
	{
		if (flat_frame_calibrationCB.Enabled && flat_frame_calibrationCB.Checked)
		{
			save_master_flat_CB.Enabled = true;
		}
		else
		{
			save_master_flat_CB.Enabled = false;
		}
	}

	private void reset_source_files_flat_files()
	{
		flat_frame_calibrationCB.Checked = true;
		save_master_flat_CB.Checked = false;
		flat_data_has_bayerCB.Checked = false;
	}

	private int generate_cmdline_options_source_flat_files_tab(ref List<string> arg_list)
	{
		if (flat_frame_calibrationCB.Checked)
		{
			arg_list.Add("-flat");
		}
		if (save_master_flat_CB.Checked)
		{
			arg_list.Add("-save_flat");
		}
		if (flat_data_has_bayerCB.Checked)
		{
			arg_list.Add("-flat_bayer");
		}
		return 1;
	}

	private void dark_filelist_FileListUpdated(object sender, EventArgs e)
	{
		FileListEventArgs e2 = (FileListEventArgs)e;
		if (e2.FrameCount == 0)
		{
			remove_dark_filesBUT.Enabled = false;
			remove_all_dark_filesBUT.Enabled = false;
			dark_frame_optionsGB.Enabled = false;
		}
		else
		{
			remove_all_dark_filesBUT.Enabled = true;
			dark_frame_optionsGB.Enabled = true;
		}
		dark_frames_stacked = 0;
		tab_dark_files.Text = "Dark Files (" + e2.FrameCount + " frames)";
	}

	private void add_dark_filesBUT_Click(object sender, EventArgs e)
	{
		dark_filelist.addsourceBTN_Click(sender, e);
	}

	private void remove_dark_filesBUT_Click(object sender, EventArgs e)
	{
		dark_filelist.remove_button_Click(sender, e);
	}

	private void remove_all_dark_filesBUT_Click(object sender, EventArgs e)
	{
		dark_filelist.remove_all_button_Click(sender, e);
	}

	private void dark_filelist_ItemSelectionChanged(object sender, EventArgs e)
	{
		if (dark_filelist.SelectedItems.Count > 0)
		{
			remove_dark_filesBUT.Enabled = true;
		}
		else
		{
			remove_dark_filesBUT.Enabled = false;
		}
	}

	private void handle_dark_file_enables(object sender, EventArgs e)
	{
		if (dark_frame_optionsGB.Enabled && dark_frame_subtractionCB.Checked)
		{
			hot_pixels_optionsGB.Enabled = true;
			save_master_dark_CB.Enabled = true;
		}
		else
		{
			hot_pixels_optionsGB.Enabled = false;
			save_master_dark_CB.Enabled = false;
		}
		if (hot_pixels_optionsGB.Enabled && replace_hot_pixelsCB.Checked)
		{
			hot_pixel_thresholdLAB.Enabled = true;
			hot_pixel_thresholdNUD.Enabled = true;
			dark_data_has_bayerCB.Enabled = true;
		}
		else
		{
			hot_pixel_thresholdLAB.Enabled = false;
			hot_pixel_thresholdNUD.Enabled = false;
			dark_data_has_bayerCB.Enabled = false;
		}
	}

	private void reset_source_files_dark_files()
	{
		dark_frame_subtractionCB.Checked = true;
		save_master_dark_CB.Checked = false;
		replace_hot_pixelsCB.Checked = false;
		hot_pixel_thresholdNUD.Value = 90m;
		dark_data_has_bayerCB.Checked = false;
	}

	private int generate_cmdline_options_source_dark_files_tab(ref List<string> arg_list)
	{
		if (dark_frame_subtractionCB.Checked)
		{
			arg_list.Add("-dark");
		}
		if (save_master_dark_CB.Checked)
		{
			arg_list.Add("-save_dark");
		}
		if (replace_hot_pixelsCB.Checked)
		{
			arg_list.Add("-hot_pixel=" + hot_pixel_thresholdNUD.Value);
		}
		if (dark_data_has_bayerCB.Checked)
		{
			arg_list.Add("-dark_bayer");
		}
		return 1;
	}

	private void binningCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		enable_debayer_options();
	}

	private void extract_date_timeCB_CheckedChanged(object sender, EventArgs e)
	{
		if (extract_date_timeCB.Checked)
		{
			winjupossubPANEL.Enabled = true;
			update_date_time_extracted();
		}
		else
		{
			winjupossubPANEL.Enabled = false;
		}
	}

	private void datetimeTB_Validated(object sender, EventArgs e)
	{
		switch (inputdatetimeformatCB.SelectedIndex)
		{
		case 9:
			cPersistentData.custom_date_time_filter1 = datetimeTB.Text;
			break;
		case 10:
			cPersistentData.custom_date_time_filter2 = datetimeTB.Text;
			break;
		case 11:
			cPersistentData.custom_date_time_filter3 = datetimeTB.Text;
			break;
		}
		update_date_time_extracted();
	}

	private void inputdatetimeformatCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		switch (inputdatetimeformatCB.SelectedIndex)
		{
		case 0:
			datetimeTB.Text = "YYYYMMDD_hhmmss";
			datetimeTB.ReadOnly = true;
			break;
		case 1:
			datetimeTB.Text = "YYYYMMDD_N_hhmmss";
			datetimeTB.ReadOnly = true;
			break;
		case 2:
			datetimeTB.Text = "DDMMYY_hhmmss";
			datetimeTB.ReadOnly = true;
			break;
		case 3:
			datetimeTB.Text = "DDMMYY_N_hhmmss";
			datetimeTB.ReadOnly = true;
			break;
		case 4:
			datetimeTB.Text = "YY-MM-DD hh-mm-ss";
			datetimeTB.ReadOnly = true;
			break;
		case 5:
			datetimeTB.Text = "DD_MM_YYYY hh_mm_ss";
			datetimeTB.ReadOnly = true;
			break;
		case 6:
			datetimeTB.Text = "YYYY-MM-DD-hhmm_t";
			datetimeTB.ReadOnly = true;
			break;
		case 7:
			datetimeTB.Text = "YYYY-MM-DD-hhmm.t";
			datetimeTB.ReadOnly = true;
			break;
		case 8:
			datetimeTB.Text = "YYYYMMDD_hhmm_ss";
			datetimeTB.ReadOnly = true;
			break;
		case 9:
			datetimeTB.Text = cPersistentData.custom_date_time_filter1;
			datetimeTB.ReadOnly = false;
			break;
		case 10:
			datetimeTB.Text = cPersistentData.custom_date_time_filter2;
			datetimeTB.ReadOnly = false;
			break;
		case 11:
			datetimeTB.Text = cPersistentData.custom_date_time_filter3;
			datetimeTB.ReadOnly = false;
			break;
		}
		update_date_time_extracted();
	}

	private void wjpos_rememberCB_CheckedChanged(object sender, EventArgs e)
	{
		cPersistentData.winjupos_remember = wjpos_rememberCB.Checked;
	}

	private void update_date_time_extracted()
	{
		if (image_filelist.Items.Count > 0)
		{
			wjpos_filename2LAB.Text = image_filelist.Items[0].Text;
			if (ManagedEngineApi.TryExtractDateTime(image_filelist.Items[0].Text, datetimeTB.Text, out string extracted))
			{
				wjpos_extracted2LAB.Text = extracted;
				wjpos_extracted2LAB.ForeColor = Color.Black;
			}
			else
			{
				wjpos_extracted2LAB.Text = "Failed";
				wjpos_extracted2LAB.ForeColor = Color.Red;
			}
		}
		else
		{
			wjpos_filename2LAB.Text = "None";
			wjpos_extracted2LAB.Text = "None";
			wjpos_extracted2LAB.ForeColor = Color.Black;
		}
	}

	private void startframeNUD_ValueChanged(object sender, EventArgs e)
	{
		if (startframeNUD.Value > endframeNUD.Value)
		{
			endframeNUD.Value = startframeNUD.Value;
		}
	}

	private void endframeNUD_ValueChanged(object sender, EventArgs e)
	{
		if (startframeNUD.Value > endframeNUD.Value)
		{
			endframeNUD.Value = startframeNUD.Value;
		}
	}

	private void colourdetectionCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		enable_debayer_options();
	}

	private void debayerframesCB_CheckedChanged(object sender, EventArgs e)
	{
		enable_debayer_options();
	}

	private void debay_patternCB_Changed(object sender, EventArgs e)
	{
		switch (debay_patternCB.SelectedIndex)
		{
		case 0:
			debayerpatternPB.Image = Resources.gbrg1;
			break;
		case 1:
			debayerpatternPB.Image = Resources.bggr1;
			break;
		case 2:
			debayerpatternPB.Image = Resources.rggb1;
			break;
		case 3:
			debayerpatternPB.Image = Resources.grbg1;
			break;
		}
	}

	private void enable_debayer_options()
	{
		if (colourdetectionCB.SelectedIndex == 2)
		{
			debayerenableGB.Enabled = false;
		}
		else
		{
			debayerenableGB.Enabled = true;
		}
		if (binningCB.SelectedIndex > 0 && debayerenableGB.Enabled)
		{
			debayerenableGB.Enabled = false;
		}
		if (colourdetectionCB.SelectedIndex == 2 || debayerframesCB.Checked)
		{
			protectbayerCB.Enabled = false;
		}
		else
		{
			protectbayerCB.Enabled = true;
		}
		if (colourdetectionCB.SelectedIndex == 2 || !debayerframesCB.Checked || !debayerenableGB.Enabled)
		{
			debayerGB.Enabled = false;
		}
		else
		{
			debayerGB.Enabled = true;
		}
	}

	private void framelimitCB_CheckedChanged(object sender, EventArgs e)
	{
		framelimit_control();
	}

	private void limitframesCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		framelimit_control();
	}

	private void framelimit_control()
	{
		if (framelimitCB.Checked)
		{
			limitframesCB.Enabled = true;
			limitframesPANEL.Enabled = true;
			limitframesPANEL2.Enabled = true;
		}
		else
		{
			startframeNUD.Enabled = false;
			endframeNUD.Enabled = false;
			limitframesPANEL.Enabled = false;
			limitframesPANEL2.Enabled = false;
			limitframesCB.Enabled = false;
		}
		if (limitframesCB.SelectedIndex == 0)
		{
			startframeNUD.Enabled = true;
			endframeNUD.Enabled = true;
			limitframesPANEL.Visible = true;
			limitframesPANEL2.Visible = false;
		}
		else
		{
			limitframesPANEL.Visible = false;
			limitframesPANEL2.Visible = true;
		}
	}

	private void raw_image_debayerCB_CheckedChanged(object sender, EventArgs e)
	{
		raw_colour_optionsPANEL.Enabled = raw_image_debayerCB.Checked;
	}

	private void reset_input_options()
	{
		raw_hpfCB.Checked = false;
		raw_image_debayerCB.Checked = true;
		raw_debayer_algorithmCB.SelectedIndex = 0;
		raw_output_colour_spaceCB.SelectedIndex = 0;
		raw_highlight_recovery_typeCB.SelectedIndex = 1;
		y800decodestrictCB.Checked = false;
		extract_date_timeCB.Checked = false;
		date_time_extract_conv_utCB.Checked = true;
		wjpos_rememberCB.Checked = cPersistentData.winjupos_remember;
		if (!wjpos_rememberCB.Checked)
		{
			inputdatetimeformatCB.SelectedIndex = 0;
		}
		inputframespersecondNUD.Value = 60m;
		serbitdepthCB.SelectedIndex = 0;
		framelimitCB.Checked = false;
		limitframesCB.SelectedIndex = 0;
		startframeNUD.Value = 1m;
		endframeNUD.Value = 1200m;
		numframesNUD.Value = 1200m;
		ipframedropNUD.Value = 0m;
		colourdetectionCB.SelectedIndex = 0;
		debayerframesCB.Checked = false;
		protectbayerCB.Checked = false;
		debay_typeCB.SelectedIndex = 0;
		debay_patternCB.SelectedIndex = 0;
		binningCB.SelectedIndex = 0;
		binning_calculationCB.SelectedIndex = 0;
	}

	private int generate_cmdline_options_input_options_tab(ref List<string> arg_list)
	{
		if (raw_hpfCB.Checked)
		{
			arg_list.Add("-raw_hpf");
		}
		if (raw_image_debayerCB.Checked)
		{
			arg_list.Add("-raw_db=" + raw_debayer_algorithmCB.SelectedIndex);
			arg_list.Add("-raw_ocs=" + raw_output_colour_spaceCB.SelectedIndex);
		}
		arg_list.Add("-raw_hl=" + raw_highlight_recovery_typeCB.SelectedIndex);
		if (y800decodestrictCB.Checked)
		{
			arg_list.Add("-y8");
		}
		if (extract_date_timeCB.Checked)
		{
			if (datetimeTB.Text != "")
			{
				arg_list.Add("-wjpos_dtmask=" + datetimeTB.Text);
			}
			if (date_time_extract_conv_utCB.Checked)
			{
				arg_list.Add("-wjpos_lt2ut");
			}
			arg_list.Add("-wjpos_ipfps=" + inputframespersecondNUD.Value);
		}
		switch (serbitdepthCB.SelectedIndex)
		{
		case 1:
			arg_list.Add("-ser_bpp=16");
			break;
		case 2:
			arg_list.Add("-ser_bpp=15");
			break;
		case 3:
			arg_list.Add("-ser_bpp=14");
			break;
		case 4:
			arg_list.Add("-ser_bpp=13");
			break;
		case 5:
			arg_list.Add("-ser_bpp=12");
			break;
		case 6:
			arg_list.Add("-ser_bpp=11");
			break;
		case 7:
			arg_list.Add("-ser_bpp=10");
			break;
		case 8:
			arg_list.Add("-ser_bpp=9");
			break;
		case 9:
			arg_list.Add("-ser_bpp=8");
			break;
		}
		if (framelimitCB.Checked)
		{
			if (limitframesCB.SelectedIndex == 0)
			{
				if (startframeNUD.Value > 1m)
				{
					arg_list.Add("-sf=" + startframeNUD.Value);
				}
				if (endframeNUD.Value > 0m)
				{
					arg_list.Add("-f=" + (endframeNUD.Value - startframeNUD.Value + 1m));
				}
			}
			else if (limitframesCB.SelectedIndex == 1)
			{
				arg_list.Add("-ctrf");
				if (endframeNUD.Value > 0m)
				{
					arg_list.Add("-f=" + numframesNUD.Value);
				}
			}
		}
		if (ipframedropNUD.Value > 0m)
		{
			arg_list.Add("-ifd=" + ipframedropNUD.Value);
		}
		switch (colourdetectionCB.SelectedIndex)
		{
		case 1:
			arg_list.Add("-mono");
			break;
		case 2:
			arg_list.Add("-colour");
			break;
		}
		if (debayerenableGB.Enabled && debayerframesCB.Checked)
		{
			arg_list.Add("-db");
		}
		if (protectbayerCB.Enabled && protectbayerCB.Checked)
		{
			arg_list.Add("-pb");
		}
		if (debayerGB.Enabled)
		{
			arg_list.Add("-db_type=" + debay_typeCB.SelectedIndex);
			arg_list.Add("-db=" + debay_patternCB.SelectedIndex);
		}
		if (binningCB.SelectedIndex > 0)
		{
			arg_list.Add("-bin=" + (binningCB.SelectedIndex + 1));
			if (binning_calculationCB.SelectedIndex == 1)
			{
				arg_list.Add("-bin_sum");
			}
		}
		return 1;
	}

	private void reset_options_with_save()
	{
		save_options(Application.LocalUserAppDataPath + "\\reset_options.pof");
		reset_options();
	}

	private void undo_reset_options()
	{
		load_options(Application.LocalUserAppDataPath + "\\reset_options.pof");
	}

	private void load_options(string filename = "")
	{
		string text;
		if (filename == "")
		{
			openoptionsFD.Title = "Select PIPP Options File";
			openoptionsFD.FileName = "";
			openoptionsFD.Filter = "PIPP Options File|*.pof";
			openoptionsFD.InitialDirectory = cPersistentData.options_directory;
			DialogResult dialogResult = openoptionsFD.ShowDialog();
			if (openoptionsFD.FileNames.Length > 0 && dialogResult == DialogResult.OK)
			{
				cPersistentData.options_directory = Path.GetDirectoryName(openoptionsFD.FileName);
			}
			text = openoptionsFD.FileNames[0];
			if (text == "")
			{
				return;
			}
		}
		else
		{
			text = filename;
		}
		string text2 = "";
		int num = 0;
		string[] array;
		try
		{
			array = File.ReadAllLines(text);
		}
		catch
		{
			return;
		}
		if (array[0] != "[PIPP OPTIONS FILE]")
		{
			MessageBoxEx.Show(text + " is not a valid PIPP Options File", "Load Options", 10000u);
			return;
		}
		reset_options();
		for (int i = 1; i < array.Length; i++)
		{
			bool flag = false;
			string[] array2 = array[i].Split(' ');
			string text3 = string.Join(" ", array2, 1, array2.Length - 1);
			bool flag2 = !bool.TryParse(text3, out var result);
			bool flag3 = !int.TryParse(text3, out var result2);
			bool flag4 = !decimal.TryParse(text3, out var result3);
			switch (array2[0])
			{
			case "BATCH_MODE":
				batchmodeRB.Checked = result;
				flag = flag2;
				break;
			case "JOIN_MODE":
				joinmodeRB.Checked = result;
				flag = flag2;
				break;
			case "PLANETARY_MODE":
				planetaryCB.Checked = result;
				flag = flag2;
				break;
			case "FULLDISC_MODE":
				solar_full_discCB.Checked = result;
				flag = flag2;
				break;
			case "CLOSEUP_MODE":
				solar_closeupCB.Checked = result;
				flag = flag2;
				break;
			case "PLANETARY_ANNIMATION_MODE":
				planetary_animation_aviCB.Checked = result;
				flag = flag2;
				break;
			case "PLANETARY_ANNIMATION_AVI_MODE":
				planetary_animation_aviCB.Checked = result;
				flag = flag2;
				break;
			case "PLANETARY_ANNIMATION_GIF_MODE":
				planetary_animation_gifCB.Checked = result;
				flag = flag2;
				break;
			case "ISS_MODE":
				issCB.Checked = result;
				flag = flag2;
				break;
			case "AVI_ARCHIVE_MODE":
				avi_archiveCB.Checked = result;
				flag = flag2;
				break;
			case "FRAME_LIMIT":
				framelimitCB.Checked = result;
				flag = flag2;
				break;
			case "FRAME_RANGE_TYPE":
				limitframesCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "START_FRAME_VALUE":
				startframeNUD.Value = result2;
				flag = flag3;
				break;
			case "END_FRAME_VALUE":
				endframeNUD.Value = result2;
				flag = flag3;
				break;
			case "NUM_FRAMES_VALUE":
				numframesNUD.Value = result2;
				flag = flag3;
				break;
			case "FRAME_LIMIT_VALUE":
				flag = flag3;
				break;
			case "DARK_FRAME_SUBTRACT":
				dark_frame_subtractionCB.Checked = result;
				flag = flag2;
				break;
			case "SAVE_MASTER_DARK":
				save_master_dark_CB.Checked = result;
				flag = flag2;
				break;
			case "REPLACE_HOT_PIXELS":
				replace_hot_pixelsCB.Checked = result;
				flag = flag2;
				break;
			case "HOT_PIXEL_THRESHOLD":
				hot_pixel_thresholdNUD.Value = result2;
				flag = flag3;
				break;
			case "DARK_DATA_HAS_BAYER":
				dark_data_has_bayerCB.Checked = result;
				flag = flag2;
				break;
			case "FLAT_FRAME_CALIBRATE":
				flat_frame_calibrationCB.Checked = result;
				flag = flag2;
				break;
			case "SAVE_MASTER_FLAT":
				save_master_flat_CB.Checked = result;
				flag = flag2;
				break;
			case "FLAT_DATA_HAS_BAYER":
				flat_data_has_bayerCB.Checked = result;
				flag = flag2;
				break;
			case "FLAT_DARK_FRAME_SUBTRACT":
				flat_dark_frame_subtractionCB.Checked = result;
				flag = flag2;
				break;
			case "SAVE_MASTER_FLAT_DARK":
				save_master_flat_dark_CB.Checked = result;
				flag = flag2;
				break;
			case "BINNING":
				binningCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "BIN_CALC":
				binning_calculationCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "CR2_CONV_SRGB":
				flag = flag2;
				break;
			case "CR2_HOT_PIXEL_FILTER":
				flag = flag2;
				break;
			case "CR2_DEBAYER":
				flag = flag2;
				break;
			case "CR2_DB_DEFAULT":
				flag = flag2;
				break;
			case "CR2_DB_OPTIONS":
				flag = flag2;
				break;
			case "CR2_UNDEREXPOSE":
				flag = flag2;
				break;
			case "CR2_GAMMA_CORRECT":
				flag = flag2;
				break;
			case "RAW_HPF":
				raw_hpfCB.Checked = result;
				flag = flag2;
				break;
			case "RAW_DEBAYER":
				raw_image_debayerCB.Checked = result;
				flag = flag2;
				break;
			case "RAW_DEBAYER_ALGORITHM":
				raw_debayer_algorithmCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "RAW_COLOUR_SPACE":
				raw_output_colour_spaceCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "RAW_HIGHLIGHTS":
				raw_highlight_recovery_typeCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "ROBUST_AVI":
				flag = flag2;
				break;
			case "TRUE_Y800":
				y800decodestrictCB.Checked = result;
				flag = flag2;
				break;
			case "BY8_DEBAYER":
				flag = flag2;
				break;
			case "BY8_DB_DEFAULT":
				flag = flag2;
				break;
			case "BY8_DB_OPTIONS":
				flag = flag2;
				break;
			case "SER_DEBAYER":
				flag = flag2;
				break;
			case "SER_DB_DEFAULT":
				flag = flag2;
				break;
			case "SER_DB_OPTIONS":
				flag = flag2;
				break;
			case "SER_BIT_DEPTH":
				serbitdepthCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "INPUT_FRAME_COLOUR":
				colourdetectionCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "IP_FRAME_DROP_VALUE":
				ipframedropNUD.Value = result2;
				flag = flag3;
				break;
			case "DATE_TIME_EXTRACT":
				extract_date_timeCB.Checked = result;
				flag = flag2;
				break;
			case "WJUPOS_CONV_UT":
				date_time_extract_conv_utCB.Checked = result;
				flag = flag2;
				break;
			case "WJUPOS_DATETIME_FORMAT":
				inputdatetimeformatCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "WJUPOS_DATETIME_TEXT":
				datetimeTB.Text = text3;
				flag = false;
				break;
			case "WJUPOS_INPUT_FPS":
				inputframespersecondNUD.Value = result2;
				flag = flag3;
				break;
			case "INPUT_GAIN":
				input_gainNUD.Value = result3;
				flag = flag4;
				break;
			case "GAMMA":
				gammaNUD.Value = result3;
				flag = flag4;
				break;
			case "MEDIAN_FILTER":
				median_filterCB.Checked = result;
				flag = flag2;
				break;
			case "CHECK_PLANET":
				checkplanetCB.Checked = result;
				flag = flag2;
				break;
			case "MIN_PLANET_SIZE":
				minsizeNUD.Value = result2;
				flag = flag3;
				break;
			case "HARD_EDGE":
				flag = flag2;
				break;
			case "EDGE_IN_SHADOW":
				edge_in_shadowCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "CENTRE_PLANET":
				centreplanetCB.Checked = result;
				flag = flag2;
				break;
			case "AUTO_THRESHOLD":
				autothresholdCB.Checked = result;
				flag = flag2;
				break;
			case "THRESHOLD":
				thresholdNUD.Value = result2;
				flag = flag3;
				break;
			case "OVEREXPOSED":
				overexposedCB.Checked = result;
				flag = flag2;
				break;
			case "OVEREXPOSED_VALUE":
				overexposedNUD.Value = result2;
				flag = flag3;
				break;
			case "ENABLE_CROP":
				enablecropCB.Checked = result;
				flag = flag2;
				break;
			case "CROP_WIDTH":
				cropwidthNUD.Value = result2;
				flag = flag3;
				break;
			case "CROP_HEIGHT":
				cropheightNUD.Value = result2;
				flag = flag3;
				break;
			case "X_OFFSET":
				xoffsetNUD.Value = result2;
				flag = flag3;
				break;
			case "Y_OFFSET":
				yoffsetNUD.Value = result2;
				flag = flag3;
				break;
			case "HISTO_STRETCH":
				hs_white_pointCB.Checked = result;
				flag = flag2;
				break;
			case "HISTO_STRETCH_VALUE":
				hs_white_pointNUD.Value = result2;
				flag = flag3;
				break;
			case "HISTO_STRETCH_NO_BP":
				hs_black_pointCB.Checked = result;
				flag = flag2;
				break;
			case "RGB_STRETCH":
				rgbhistoCB.Checked = result;
				flag = flag2;
				break;
			case "KEEP_IMAGE_FILENAMES":
				keepimagefilenamesCB.Checked = result;
				flag = flag2;
				break;
			case "NO_FRAME_RENUMBER":
				noframerenumberCB.Checked = result;
				flag = flag2;
				break;
			case "ADD_LEADING_ZEROS":
				addleadingzerosCB.Checked = result;
				flag = flag2;
				break;
			case "ADD_QUALITY_VALUE":
				includequalityvalueCB.Checked = result;
				flag = flag2;
				break;
			case "STABILISE_NONE_MODE":
				stabilise_noneRB.Checked = result;
				flag = flag2;
				break;
			case "STABILISE_OBJECT_MODE":
				stabilise_objectRB.Checked = result;
				flag = flag2;
				break;
			case "STABILISE_SURFACE_MODE":
				stabilise_surfaceRB.Checked = result;
				flag = flag2;
				break;
			case "SURFACE_FEATURE_TRACKING_EN":
				enableSurfaceTrackingCB.Checked = result;
				flag = flag2;
				break;
			case "STABILISE_EN":
				enablestabilisationCB.Checked = result;
				flag = flag2;
				break;
			case "AFB_X":
				afb_xNUD.Value = result2;
				flag = flag3;
				break;
			case "AFB_Y":
				afb_yNUD.Value = result2;
				flag = flag3;
				break;
			case "AFB_WIDTH":
				afb_widthNUD.Value = result2;
				flag = flag3;
				break;
			case "AFB_HEIGHT":
				afb_heightNUD.Value = result2;
				flag = flag3;
				break;
			case "AFB_REJECT":
				afb_rejectCB.Checked = result;
				flag = flag2;
				break;
			case "AOI_ENABLE":
				aoi_enableCB.Checked = result;
				flag = flag2;
				break;
			case "AOI_X":
				aoi_xNUD.Value = result2;
				flag = flag3;
				break;
			case "AOI_Y":
				aoi_yNUD.Value = result2;
				flag = flag3;
				break;
			case "AOI_WIDTH":
				aoi_widthNUD.Value = result2;
				flag = flag3;
				break;
			case "AOI_HEIGHT":
				aoi_heightNUD.Value = result2;
				flag = flag3;
				break;
			case "AOI_REJECT":
				aoi_rejectCB.Checked = result;
				flag = flag2;
				break;
			case "AOI_CROP":
				aoi_cropCB.Checked = result;
				flag = flag2;
				break;
			case "RESIZE_FRAMES":
				resize_framesCB.Checked = result;
				flag = flag2;
				break;
			case "RESIZE_KEEP_ASPECT_RATIO":
				resize_keep_aspect_ratioCB.Checked = result;
				flag = flag2;
				break;
			case "RESIZE_WIDTH":
				resize_widthNUD.Value = result2;
				flag = flag3;
				break;
			case "RESIZE_HEIGHT":
				resize_heightNUD.Value = result2;
				flag = flag3;
				break;
			case "DEBAYER_DISABLED":
				debayerframesCB.Checked = !result;
				flag = flag2;
				break;
			case "DEBAYER_ENABLED":
				debayerframesCB.Checked = result;
				flag = flag2;
				break;
			case "PROTECT_BAYER":
				protectbayerCB.Checked = result;
				flag = flag2;
				break;
			case "DEBAYER_GBRG":
				flag = flag2;
				break;
			case "DEBAYER_BGGR":
				flag = flag2;
				break;
			case "DEBAYER_RGGB":
				flag = flag2;
				break;
			case "DEBAYER_GRGB":
				flag = flag2;
				break;
			case "DEBAYER_TYPE":
				debay_typeCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "DEBAYER_PATTERN":
				debay_patternCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "DEBAYER_BILINEAR":
				flag = flag2;
				break;
			case "DEBAYER_SMOOTHHUE":
				flag = flag2;
				break;
			case "CONV_MONO":
				convmonoCB.Checked = result;
				flag = flag2;
				break;
			case "CONV_MONO_TYPE":
				conv_mono_comboBox.SelectedIndex = result2;
				flag = flag3;
				break;
			case "FLIP_X":
				flip_xCB.Checked = result;
				flag = flag2;
				break;
			case "FLIP_Y":
				flip_yCB.Checked = result;
				flag = flag2;
				break;
			case "ROTATE":
				rotationCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "ENABLE_QUALITY":
				qualitysortingCB.Checked = result;
				flag = flag2;
				break;
			case "QUALITY_LIMITING":
				qualityframelimitCB.Checked = result;
				flag = flag2;
				break;
			case "QUALITY__LIMITING_TYPE":
				quality_limiting_typeCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "QUALITY_FRAMES":
				qualityframesNUD.Value = result2;
				flag = flag3;
				break;
			case "QUALITY_REORDER":
				qreorderCB.Checked = result;
				flag = flag2;
				break;
			case "DEFAULT_QUALITY":
				defaultalgorithmCB.Checked = result;
				flag = flag2;
				break;
			case "NINOX_QUALITY":
				ninoxalgorithmCB.Checked = result;
				flag = flag2;
				break;
			case "HISTO_QUALITY":
				histoalgorithmCB.Checked = result;
				flag = flag2;
				break;
			case "TOTALPIXEL_QUALITY":
				totalpixelalgorithmCB.Checked = result;
				flag = flag2;
				break;
			case "QMIN":
				mindownsampleNUD.Value = result2;
				flag = flag3;
				break;
			case "QMAX":
				maxdownsampleNUD.Value = result2;
				flag = flag3;
				break;
			case "QINC":
				downsampleincNUD.Value = result2;
				flag = flag3;
				break;
			case "ENABLE_QWEIGHT":
				qualityweightingCB.Checked = result;
				flag = flag2;
				break;
			case "ANIM_REPEAT_FWD_LAST_ENABLE":
				repeat_forward_lastCB.Checked = result;
				flag = flag2;
				break;
			case "ANIM_REPEAT_FWD_LAST_VALUE":
				repeat_forward_lastNUD.Value = result2;
				flag = flag3;
				break;
			case "ANIM_REV_ENABLE":
				animation_reverseCB.Checked = result;
				flag = flag2;
				break;
			case "ANIM_FWD_ENABLE":
				animation_forwardCB.Checked = result;
				flag = flag2;
				break;
			case "ANIM_REPEAT_REV_LAST_ENABLE":
				repeat_reverse_lastCB.Checked = result;
				flag = flag2;
				break;
			case "ANIM_REPEAT_REV_LAST_VALUE":
				repeat_reverse_lastNUD.Value = result2;
				flag = flag3;
				break;
			case "ANIM_REPEAT_ALL_ENABLE":
				animation_repeat_allCB.Checked = result;
				flag = flag2;
				break;
			case "ANIM_REPEAT_ALL_VALUE":
				animation_repeat_allNUD.Value = result2;
				flag = flag3;
				break;
			case "ANIM_REPEAT_REV_DISABLED_LAST":
				animation_reverse_not_in_last_repeatCB.Checked = result;
				flag = flag2;
				break;
			case "AVI_LASTFRAME_REPEAT":
				flag = flag3;
				break;
			case "AVI_REPEAT":
				flag = flag3;
				break;
			case "OUTPUT_IN_SRC_DIR":
				flag = flag2;
				break;
			case "CREATE_OUTPUT_SUBDIR":
				create_output_subdirCB.Checked = result;
				flag = flag2;
				break;
			case "SUBDIR_NAME":
				subdir_nameTB.Text = text3;
				flag = false;
				break;
			case "SUBDIR_APPEND_TIME":
				subdir_append_date_timeCB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_DIRECTORY":
				outputdirectoryTB.Text = text3;
				flag = false;
				break;
			case "OUTPUT_PREFIX":
				outputprefixTB.Text = text3;
				flag = false;
				break;
			case "OUTPUT_SUFFIX":
				outputsuffixTB.Text = text3;
				flag = false;
				break;
			case "OUTPUT_AVI":
				outputaviRB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_SER":
				outputserRB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_BMP":
				outputbmpRB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_TIF":
				outputtifRB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_PNG":
				outputpngRB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_FITS":
				outputfitsRB.Checked = result;
				flag = flag2;
				break;
			case "OUTPUT_GIF":
				outputgifRB.Checked = result;
				flag = flag2;
				break;
			case "AVI_FPS_AS_INPUT":
				avi_fpsCB.Checked = result;
				flag = flag2;
				break;
			case "AVI_FPS_VALUE":
				avi_fpsNUD.Value = result3;
				flag = flag4;
				break;
			case "RGB_SPLIT":
				rgbsplitCB.Checked = result;
				flag = flag2;
				break;
			case "FRAME_DROP_ENABLE":
				flag = flag2;
				break;
			case "FRAME_DROP_VALUE":
				outputframedropNUD.Value = result2 - 1;
				flag = flag3;
				break;
			case "AVI_OLD_FORMAT":
				old_format_aviCB.Checked = result;
				flag = flag2;
				break;
			case "AVI_OLD_FORMAT_MAX_SIZE":
				old_format_max_sizeCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "AVI_SAFE_WIDTH":
				avi_safe_frame_widthCB.Checked = result;
				flag = flag2;
				break;
			case "AVI_YUY2_CODEC":
				flag = flag2;
				break;
			case "AVI_CODEC":
				avi_codecCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "AVI_CODEC_QUALITY":
				codec_qualityNUD.Value = result2;
				flag = flag3;
				break;
			case "GIF_REPEAT_ALL_FOREVER_VALUE":
				gif_repeat_foreverCB.Checked = result;
				flag = flag2;
				break;
			case "GIF_FPS_AS_INPUT":
				gif_fpsCB.Checked = result;
				flag = flag2;
				break;
			case "GIF_FPS_VALUE":
				gif_fpsNUD.Value = result3;
				flag = flag4;
				break;
			case "GIF_COLOUR_QUANT_TYPE":
				gif_quantCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "GIF_ADV_OPTIONS":
				gif_adv_optionsCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "GIF_UNCHANGED_BORDER_EN":
				gif_border_tolCB.Checked = result;
				flag = flag2;
				break;
			case "GIF_UNCHANGED_BORDER_TOL":
				gif_border_tolNUD.Value = result2;
				flag = flag3;
				break;
			case "GIF_TRANS_PIXEL_EN":
				gif_trans_pixel_tolCB.Checked = result;
				flag = flag2;
				break;
			case "GIF_TRANS_PIXEL_TOL":
				gif_trans_pixel_tolNUD.Value = result2;
				flag = flag3;
				break;
			case "GIF_BIT_DEPTH_EN":
				gif_bit_depthCB.Checked = result;
				flag = flag2;
				break;
			case "GIF_BIT_DEPTH_VAL":
				gif_bit_depthNUD.Value = result2;
				flag = flag3;
				break;
			case "GIF_LOSSY_COMP_EN":
				gif_lossy_comp_levelCB.Checked = result;
				flag = flag2;
				break;
			case "GIF_LOSSY_COMP_VAL":
				gif_lossy_comp_levelNUD.Value = result2;
				flag = flag3;
				break;
			case "WJUPOS_NAME_ENABLE":
				winjuposfilenameCB.Checked = result;
				flag = flag2;
				break;
			case "WJUPOS_TIME":
				winjupostimeCB.SelectedIndex = result2;
				flag = flag3;
				break;
			case "FPS_AS_INPUT":
				avi_fpsCB.Checked = result;
				flag = flag2;
				break;
			case "FPS_VALUE":
				avi_fpsNUD.Value = result3;
				gif_fpsNUD.Value = result3;
				flag = flag4;
				break;
			case "ANIM_REPEAT_ALL_FOREVER_VALUE":
				gif_repeat_foreverCB.Checked = result;
				flag = flag2;
				break;
			case "SAVE_REJECTED":
				saveRejectedFramesToolStripMenuItem.Checked = result;
				flag = flag2;
				break;
			case "DEBUG_FIND":
				flag = flag2;
				break;
			default:
				flag = true;
				break;
			}
			if (flag)
			{
				string text4 = text2;
				text2 = text4 + array2[0] + " '" + text3 + "'\r\n";
				num++;
			}
		}
		if (num > 0)
		{
			MessageBoxEx.Show(num + " Errors Found In Options File:\r\n" + text2, "Load Options", MessageBoxButtons.OK, MessageBoxIcon.Hand, 10000u);
		}
	}

	private void save_options(string filename = "")
	{
		List<string> list = new List<string>();
		list.Add("[PIPP OPTIONS FILE]");
		list.Add("BATCH_MODE " + batchmodeRB.Checked);
		list.Add("JOIN_MODE " + joinmodeRB.Checked);
		list.Add("PLANETARY_MODE " + planetaryCB.Checked);
		list.Add("FULLDISC_MODE " + solar_full_discCB.Checked);
		list.Add("CLOSEUP_MODE " + solar_closeupCB.Checked);
		list.Add("PLANETARY_ANNIMATION_AVI_MODE " + planetary_animation_aviCB.Checked);
		list.Add("PLANETARY_ANNIMATION_GIF_MODE " + planetary_animation_gifCB.Checked);
		list.Add("ISS_MODE " + issCB.Checked);
		list.Add("AVI_ARCHIVE_MODE " + avi_archiveCB.Checked);
		list.Add("FRAME_LIMIT " + framelimitCB.Checked);
		list.Add("FRAME_RANGE_TYPE " + limitframesCB.SelectedIndex);
		list.Add("START_FRAME_VALUE " + startframeNUD.Value);
		list.Add("END_FRAME_VALUE " + endframeNUD.Value);
		list.Add("NUM_FRAMES_VALUE " + numframesNUD.Value);
		list.Add("DARK_FRAME_SUBTRACT " + dark_frame_subtractionCB.Checked);
		list.Add("SAVE_MASTER_DARK " + save_master_dark_CB.Checked);
		list.Add("REPLACE_HOT_PIXELS " + replace_hot_pixelsCB.Checked);
		list.Add("HOT_PIXEL_THRESHOLD " + hot_pixel_thresholdNUD.Value);
		list.Add("DARK_DATA_HAS_BAYER " + dark_data_has_bayerCB.Checked);
		list.Add("FLAT_FRAME_CALIBRATE " + flat_frame_calibrationCB.Checked);
		list.Add("SAVE_MASTER_FLAT " + save_master_flat_CB.Checked);
		list.Add("FLAT_DATA_HAS_BAYER " + flat_data_has_bayerCB.Checked);
		list.Add("FLAT_DARK_FRAME_SUBTRACT " + flat_dark_frame_subtractionCB.Checked);
		list.Add("SAVE_MASTER_FLAT_DARK " + save_master_flat_dark_CB.Checked);
		list.Add("BINNING " + binningCB.SelectedIndex);
		list.Add("BIN_CALC " + binning_calculationCB.SelectedIndex);
		list.Add("DEBAYER_ENABLED " + debayerframesCB.Checked);
		list.Add("PROTECT_BAYER " + protectbayerCB.Checked);
		list.Add("DEBAYER_TYPE " + debay_typeCB.SelectedIndex);
		list.Add("DEBAYER_PATTERN " + debay_patternCB.SelectedIndex);
		list.Add("RAW_HPF " + raw_hpfCB.Checked);
		list.Add("RAW_DEBAYER " + raw_image_debayerCB.Checked);
		list.Add("RAW_DEBAYER_ALGORITHM " + raw_debayer_algorithmCB.SelectedIndex);
		list.Add("RAW_COLOUR_SPACE " + raw_output_colour_spaceCB.SelectedIndex);
		list.Add("RAW_HIGHLIGHTS " + raw_highlight_recovery_typeCB.SelectedIndex);
		list.Add("TRUE_Y800 " + y800decodestrictCB.Checked);
		list.Add("SER_BIT_DEPTH " + serbitdepthCB.SelectedIndex);
		list.Add("INPUT_FRAME_COLOUR " + colourdetectionCB.SelectedIndex);
		list.Add("IP_FRAME_DROP_VALUE " + ipframedropNUD.Value);
		list.Add("AVI_OLD_FORMAT " + old_format_aviCB.Checked);
		list.Add("AVI_SAFE_WIDTH " + avi_safe_frame_widthCB.Checked);
		list.Add("AVI_OLD_FORMAT_MAX_SIZE " + old_format_max_sizeCB.SelectedIndex);
		list.Add("AVI_CODEC " + avi_codecCB.SelectedIndex);
		list.Add("AVI_CODEC_QUALITY " + codec_qualityNUD.Value);
		list.Add("DATE_TIME_EXTRACT " + extract_date_timeCB.Checked);
		list.Add("WJUPOS_CONV_UT " + date_time_extract_conv_utCB.Checked);
		list.Add("WJUPOS_DATETIME_FORMAT " + inputdatetimeformatCB.SelectedIndex);
		list.Add("WJUPOS_DATETIME_TEXT " + datetimeTB.Text);
		list.Add("WJUPOS_INPUT_FPS " + inputframespersecondNUD.Value);
		list.Add("INPUT_GAIN " + input_gainNUD.Value);
		list.Add("GAMMA " + gammaNUD.Value);
		list.Add("MEDIAN_FILTER " + median_filterCB.Checked);
		list.Add("CHECK_PLANET " + checkplanetCB.Checked);
		list.Add("MIN_PLANET_SIZE " + minsizeNUD.Value);
		list.Add("EDGE_IN_SHADOW " + edge_in_shadowCB.SelectedIndex);
		list.Add("CENTRE_PLANET " + centreplanetCB.Checked);
		list.Add("AUTO_THRESHOLD " + autothresholdCB.Checked);
		list.Add("THRESHOLD " + thresholdNUD.Value);
		list.Add("OVEREXPOSED " + overexposedCB.Checked);
		list.Add("OVEREXPOSED_VALUE " + overexposedNUD.Value);
		list.Add("ENABLE_CROP " + enablecropCB.Checked);
		list.Add("CROP_WIDTH " + cropwidthNUD.Value);
		list.Add("CROP_HEIGHT " + cropheightNUD.Value);
		list.Add("X_OFFSET " + xoffsetNUD.Value);
		list.Add("Y_OFFSET " + yoffsetNUD.Value);
		list.Add("HISTO_STRETCH " + hs_white_pointCB.Checked);
		list.Add("HISTO_STRETCH_VALUE " + hs_white_pointNUD.Value);
		list.Add("HISTO_STRETCH_NO_BP " + hs_black_pointCB.Checked);
		list.Add("RGB_STRETCH " + rgbhistoCB.Checked);
		list.Add("KEEP_IMAGE_FILENAMES " + keepimagefilenamesCB.Checked);
		list.Add("NO_FRAME_RENUMBER " + noframerenumberCB.Checked);
		list.Add("ADD_LEADING_ZEROS " + addleadingzerosCB.Checked);
		list.Add("ADD_QUALITY_VALUE " + includequalityvalueCB.Checked);
		list.Add("STABILISE_NONE_MODE " + stabilise_noneRB.Checked);
		list.Add("STABILISE_OBJECT_MODE " + stabilise_objectRB.Checked);
		list.Add("STABILISE_SURFACE_MODE " + stabilise_surfaceRB.Checked);
		list.Add("SURFACE_FEATURE_TRACKING_EN " + enableSurfaceTrackingCB.Checked);
		list.Add("STABILISE_EN " + enablestabilisationCB.Checked);
		list.Add("AFB_X " + afb_xNUD.Value);
		list.Add("AFB_Y " + afb_yNUD.Value);
		list.Add("AFB_WIDTH " + afb_widthNUD.Value);
		list.Add("AFB_HEIGHT " + afb_heightNUD.Value);
		list.Add("AFB_REJECT " + afb_rejectCB.Checked);
		list.Add("AOI_ENABLE " + aoi_enableCB.Checked);
		list.Add("AOI_X " + aoi_xNUD.Value);
		list.Add("AOI_Y " + aoi_yNUD.Value);
		list.Add("AOI_WIDTH " + aoi_widthNUD.Value);
		list.Add("AOI_HEIGHT " + aoi_heightNUD.Value);
		list.Add("AOI_REJECT " + aoi_rejectCB.Checked);
		list.Add("AOI_CROP " + aoi_cropCB.Checked);
		list.Add("CONV_MONO " + convmonoCB.Checked);
		list.Add("CONV_MONO_TYPE " + conv_mono_comboBox.SelectedIndex);
		list.Add("FLIP_X " + flip_xCB.Checked);
		list.Add("FLIP_Y " + flip_yCB.Checked);
		list.Add("ROTATE " + rotationCB.SelectedIndex);
		list.Add("RESIZE_FRAMES " + resize_framesCB.Checked);
		list.Add("RESIZE_KEEP_ASPECT_RATIO " + resize_keep_aspect_ratioCB.Checked);
		list.Add("RESIZE_WIDTH " + resize_widthNUD.Value);
		list.Add("RESIZE_HEIGHT " + resize_heightNUD.Value);
		list.Add("ANIM_FWD_ENABLE " + animation_forwardCB.Checked);
		list.Add("ANIM_REPEAT_FWD_LAST_ENABLE " + repeat_forward_lastCB.Checked);
		list.Add("ANIM_REPEAT_FWD_LAST_VALUE " + repeat_forward_lastNUD.Value);
		list.Add("ANIM_REV_ENABLE " + animation_reverseCB.Checked);
		list.Add("ANIM_FWD_ENABLE " + animation_forwardCB.Checked);
		list.Add("ANIM_REPEAT_REV_LAST_ENABLE " + repeat_reverse_lastCB.Checked);
		list.Add("ANIM_REPEAT_REV_LAST_VALUE " + repeat_reverse_lastNUD.Value);
		list.Add("ANIM_REPEAT_ALL_ENABLE " + animation_repeat_allCB.Checked);
		list.Add("ANIM_REPEAT_ALL_VALUE " + animation_repeat_allNUD.Value);
		list.Add("ANIM_REPEAT_REV_DISABLED_LAST " + animation_reverse_not_in_last_repeatCB.Checked);
		list.Add("ENABLE_QUALITY " + qualitysortingCB.Checked);
		list.Add("QUALITY_LIMITING " + qualityframelimitCB.Checked);
		list.Add("QUALITY__LIMITING_TYPE " + quality_limiting_typeCB.SelectedIndex);
		list.Add("QUALITY_FRAMES " + qualityframesNUD.Value);
		list.Add("QUALITY_REORDER " + qreorderCB.Checked);
		list.Add("DEFAULT_QUALITY " + defaultalgorithmCB.Checked);
		list.Add("NINOX_QUALITY " + ninoxalgorithmCB.Checked);
		list.Add("HISTO_QUALITY " + histoalgorithmCB.Checked);
		list.Add("TOTALPIXEL_QUALITY " + totalpixelalgorithmCB.Checked);
		list.Add("QMIN " + mindownsampleNUD.Value);
		list.Add("QMAX " + maxdownsampleNUD.Value);
		list.Add("QINC " + downsampleincNUD.Value);
		list.Add("ENABLE_QWEIGHT " + qualityweightingCB.Checked);
		list.Add("CREATE_OUTPUT_SUBDIR " + create_output_subdirCB.Checked);
		list.Add("SUBDIR_NAME " + subdir_nameTB.Text);
		list.Add("SUBDIR_APPEND_TIME " + subdir_append_date_timeCB.Checked);
		list.Add("OUTPUT_DIRECTORY " + outputdirectoryTB.Text);
		list.Add("OUTPUT_PREFIX " + outputprefixTB.Text);
		list.Add("OUTPUT_SUFFIX " + outputsuffixTB.Text);
		list.Add("OUTPUT_AVI " + outputaviRB.Checked);
		list.Add("OUTPUT_SER " + outputserRB.Checked);
		list.Add("OUTPUT_BMP " + outputbmpRB.Checked);
		list.Add("OUTPUT_TIF " + outputtifRB.Checked);
		list.Add("OUTPUT_PNG " + outputpngRB.Checked);
		list.Add("OUTPUT_FITS " + outputfitsRB.Checked);
		list.Add("OUTPUT_GIF " + outputgifRB.Checked);
		list.Add("AVI_FPS_AS_INPUT " + avi_fpsCB.Checked);
		list.Add("AVI_FPS_VALUE " + avi_fpsNUD.Value);
		list.Add("GIF_REPEAT_ALL_FOREVER_VALUE " + gif_repeat_foreverCB.Checked);
		list.Add("GIF_FPS_AS_INPUT " + gif_fpsCB.Checked);
		list.Add("GIF_FPS_VALUE " + gif_fpsNUD.Value);
		list.Add("GIF_COLOUR_QUANT_TYPE " + gif_quantCB.SelectedIndex);
		list.Add("GIF_ADV_OPTIONS " + gif_adv_optionsCB.SelectedIndex);
		list.Add("GIF_UNCHANGED_BORDER_EN " + gif_border_tolCB.Checked);
		list.Add("GIF_UNCHANGED_BORDER_TOL " + gif_border_tolNUD.Value);
		list.Add("GIF_TRANS_PIXEL_EN " + gif_trans_pixel_tolCB.Checked);
		list.Add("GIF_TRANS_PIXEL_TOL " + gif_trans_pixel_tolNUD.Value);
		list.Add("GIF_BIT_DEPTH_EN " + gif_bit_depthCB.Checked);
		list.Add("GIF_BIT_DEPTH_VAL " + gif_bit_depthNUD.Value);
		list.Add("GIF_LOSSY_COMP_EN " + gif_lossy_comp_levelCB.Checked);
		list.Add("GIF_LOSSY_COMP_VAL " + gif_lossy_comp_levelNUD.Value);
		list.Add("RGB_SPLIT " + rgbsplitCB.Checked);
		list.Add("FRAME_DROP_VALUE " + (outputframedropNUD.Value + 1m));
		list.Add("WJUPOS_NAME_ENABLE " + winjuposfilenameCB.Checked);
		list.Add("WJUPOS_TIME " + winjupostimeCB.SelectedIndex);
		list.Add("SAVE_REJECTED " + saveRejectedFramesToolStripMenuItem.Checked);
		saveoptionsFD.Title = "Save PIPP Options File";
		saveoptionsFD.Filter = "PIPP Options File|*.pof";
		saveoptionsFD.InitialDirectory = cPersistentData.options_directory;
		if (filename == "")
		{
			DialogResult dialogResult = saveoptionsFD.ShowDialog();
			if (saveoptionsFD.FileNames.Length > 0 && dialogResult == DialogResult.OK)
			{
				cPersistentData.options_directory = Path.GetDirectoryName(saveoptionsFD.FileName);
				File.WriteAllLines(saveoptionsFD.FileNames[0], list);
			}
			return;
		}
		try
		{
			File.WriteAllLines(filename, list);
		}
		catch
		{
		}
	}

	private void doProcessToolStripMenuItem_Click(object sender, EventArgs e)
	{
		test_run = false;
		show_detected_pixels = false;
		start_processing();
	}

	private void cancelProcessingToolStripMenuItem_Click(object sender, EventArgs e)
	{
		cancel_processing();
	}

	private void addSourceFilesToolStripMenuItem_Click(object sender, EventArgs e)
	{
		image_filelist.add_source_dialog();
	}

	private void resetOptionsToolStripMenuItem_Click(object sender, EventArgs e)
	{
		undoResetOptionsToolStripMenuItem.Enabled = true;
		reset_options_with_save();
	}

	private void undoResetOptionsToolStripMenuItem_Click(object sender, EventArgs e)
	{
		undo_reset_options();
	}

	private void saveOptionsToolStripMenuItem_Click(object sender, EventArgs e)
	{
		save_options();
	}

	private void loadOptionsToolStripMenuItem_Click(object sender, EventArgs e)
	{
		load_options();
	}

	private void loadOptionsFromLastSessionToolStripMenuItem_Click(object sender, EventArgs e)
	{
		load_options(Application.LocalUserAppDataPath + "\\quit_options.pof");
	}

	private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
	{
		// Both strings are supplied by the managed engine metadata (see ManagedEngineApi); the
		// About dialog displays them verbatim.
		string dcrawVersion = ManagedEngineApi.DcrawVersion;
		string pippVersion = ManagedEngineApi.PippVersion;
		if (helpinfo == null)
		{
			helpinfo = new infodialog(pippVersion, dcrawVersion);
			helpinfo.FormClosed += aboutToolStripMenuItem_Closed;
			helpinfo.Show();
		}
	}

	private void aboutToolStripMenuItem_Closed(object sender, FormClosedEventArgs e)
	{
		helpinfo = null;
	}

	private void menuQuit_Click(object sender, EventArgs e)
	{
		DialogResult dialogResult = MessageBox.Show("Confirm Quit?", "PIPP Quit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		if (dialogResult != DialogResult.No)
		{
			Application.Exit();
		}
	}

	private void reset_menu_strip()
	{
		saveRejectedFramesToolStripMenuItem.Checked = false;
	}

	private int generate_cmdline_options_menu_strip(ref List<string> arg_list)
	{
		if (saveRejectedFramesToolStripMenuItem.Checked)
		{
			arg_list.Add("-sr");
		}
		if (!use_ram_buffersMenuItem.Checked)
		{
			arg_list.Add("-no_ram_buff");
		}
		return 1;
	}

	private void outputdirectoryBUT_Click(object sender, EventArgs e)
	{
		outputdirectoryFBD.SelectedPath = cPersistentData.output_directory;
		if (outputdirectoryFBD.ShowDialog() == DialogResult.OK && outputdirectoryFBD.SelectedPath != "")
		{
			outputdirectoryTB.Text = outputdirectoryFBD.SelectedPath;
			cPersistentData.output_directory = outputdirectoryFBD.SelectedPath;
		}
	}

	private void defaultouputdirBUT_Click(object sender, EventArgs e)
	{
		outputdirectoryTB.Text = "Default (Source File Directory)";
	}

	private void create_output_subdirCB_CheckedChanged(object sender, EventArgs e)
	{
		if (create_output_subdirCB.Checked)
		{
			subdir_nameGB.Enabled = true;
		}
		else
		{
			subdir_nameGB.Enabled = false;
		}
	}

	private void output_formatRB_CheckedChanged(object sender, EventArgs e)
	{
		if (outputaviRB.Checked)
		{
			outtype_label.Text = "(8-bit only - Colour/Monochrome)";
			avi_outputGB.Enabled = true;
			avi_outputGB.Visible = true;
			avi_outputGB.BringToFront();
			animimated_gif_optionsGB.Visible = false;
			renumberGB.Enabled = false;
			renumberGB.Visible = false;
			winjuposGB.Enabled = true;
			winjuposGB.Visible = true;
			winjuposGB.BringToFront();
			rgbsplitGB.Enabled = true;
		}
		else if (outputserRB.Checked)
		{
			outtype_label.Text = "(8-bit/16-bit - Colour/Monochrome)";
			avi_outputGB.Enabled = false;
			avi_outputGB.Visible = false;
			animimated_gif_optionsGB.Visible = false;
			renumberGB.Enabled = false;
			renumberGB.Visible = false;
			winjuposGB.Enabled = true;
			winjuposGB.Visible = true;
			winjuposGB.BringToFront();
			rgbsplitGB.Enabled = true;
		}
		else if (outputbmpRB.Checked)
		{
			outtype_label.Text = "(8-bit only - Colour/Monochrome)";
			avi_outputGB.Enabled = false;
			avi_outputGB.Visible = false;
			animimated_gif_optionsGB.Visible = false;
			renumberGB.Text = "BMP File Naming";
			renumberGB.Enabled = true;
			renumberGB.Visible = true;
			renumberGB.BringToFront();
			winjuposGB.Enabled = false;
			winjuposGB.Visible = false;
			rgbsplitGB.Enabled = true;
		}
		else if (outputtifRB.Checked)
		{
			outtype_label.Text = "(8-bit/16-bit - Colour/Monochrome)";
			avi_outputGB.Enabled = false;
			avi_outputGB.Visible = false;
			animimated_gif_optionsGB.Visible = false;
			renumberGB.Text = "TIFF File Naming";
			renumberGB.Enabled = true;
			renumberGB.Visible = true;
			renumberGB.BringToFront();
			winjuposGB.Enabled = false;
			winjuposGB.Visible = false;
			rgbsplitGB.Enabled = true;
		}
		else if (outputpngRB.Checked)
		{
			outtype_label.Text = "(8-bit/16-bit - Colour/Monochrome)";
			avi_outputGB.Enabled = false;
			avi_outputGB.Visible = false;
			animimated_gif_optionsGB.Visible = false;
			renumberGB.Text = "PNG File Naming";
			renumberGB.Enabled = true;
			renumberGB.Visible = true;
			renumberGB.BringToFront();
			winjuposGB.Enabled = false;
			winjuposGB.Visible = false;
			rgbsplitGB.Enabled = true;
		}
		else if (outputfitsRB.Checked)
		{
			outtype_label.Text = "(8-bit/16-bit - Colour/Monochrome)";
			avi_outputGB.Enabled = false;
			avi_outputGB.Visible = false;
			animimated_gif_optionsGB.Visible = false;
			renumberGB.Text = "FITS File Naming";
			renumberGB.Enabled = true;
			renumberGB.Visible = true;
			renumberGB.BringToFront();
			winjuposGB.Enabled = false;
			winjuposGB.Visible = false;
			rgbsplitGB.Enabled = true;
		}
		else if (outputgifRB.Checked)
		{
			outtype_label.Text = "(8-bit only - Colour/Monochrome)";
			avi_outputGB.Enabled = false;
			avi_outputGB.Visible = false;
			animimated_gif_optionsGB.Visible = true;
			animimated_gif_optionsGB.BringToFront();
			renumberGB.Enabled = false;
			renumberGB.Visible = false;
			winjuposGB.Enabled = false;
			winjuposGB.Visible = false;
			rgbsplitGB.Enabled = false;
		}
		set_animation_options_details();
	}

	private void avi_codecCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (avi_codecCB.SelectedIndex == 0)
		{
			old_format_aviPAN.Enabled = true;
			avi_safe_frame_widthCB.Enabled = true;
		}
		else
		{
			old_format_aviPAN.Enabled = false;
			avi_safe_frame_widthCB.Enabled = false;
		}
		if (avi_codecCB.SelectedIndex == 2)
		{
			codec_qualityGB.Enabled = true;
		}
		else
		{
			codec_qualityGB.Enabled = false;
		}
		set_animation_options_details();
	}

	private void codec_qualityNUD_ValueChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void old_format_aviCB_CheckedChanged(object sender, EventArgs e)
	{
		old_format_max_sizeCB.Enabled = old_format_aviCB.Checked;
	}

	private void fpsNUD_ValueChanged(object sender, EventArgs e)
	{
		set_animation_options_details();
	}

	private void winjuposfilenameCB_CheckedChanged(object sender, EventArgs e)
	{
		if (winjuposfilenameCB.Checked)
		{
			winjupostimeCB.Enabled = true;
		}
		else
		{
			winjupostimeCB.Enabled = false;
		}
	}

	private void reset_output_options()
	{
		outputdirectoryTB.Text = "Default (Source File Directory)";
		create_output_subdirCB.Checked = true;
		subdir_nameTB.Text = "pipp_";
		subdir_append_date_timeCB.Checked = true;
		outputprefixTB.Text = "";
		outputsuffixTB.Text = "_pipp";
		outputaviRB.Checked = true;
		avi_codecCB.SelectedIndex = 0;
		codec_qualityNUD.Value = 10m;
		old_format_aviCB.Checked = false;
		old_format_max_sizeCB.SelectedIndex = 0;
		avi_safe_frame_widthCB.Checked = false;
		gif_repeat_foreverCB.Checked = true;
		gif_fpsCB.Checked = false;
		gif_fpsNUD.Value = 3m;
		gif_quantCB.SelectedIndex = 0;
		gif_adv_optionsCB.SelectedIndex = 4;
		gif_adv_optionsCB.SelectedIndex = 1;
		avi_fpsCB.Checked = true;
		avi_fpsNUD.Value = 60m;
		keepimagefilenamesCB.Checked = true;
		noframerenumberCB.Checked = false;
		addleadingzerosCB.Checked = true;
		includequalityvalueCB.Checked = false;
		rgbsplitCB.Checked = false;
		outputframedropNUD.Value = 0m;
		winjuposfilenameCB.Checked = false;
		winjupostimeCB.SelectedIndex = 0;
	}

	private int generate_cmdline_options_output_options_tab(ref List<string> arg_list)
	{
		DateTime now = DateTime.Now;
		string text = "";
		if (create_output_subdirCB.Checked && !test_run)
		{
			text = ((subdir_nameTB.Text.Length <= 0) ? (text + "pipp_") : (text + subdir_nameTB.Text));
			if (subdir_append_date_timeCB.Checked)
			{
				text += now.Year.ToString("0000");
				text += now.Month.ToString("00");
				text += now.Day.ToString("00");
				text += "_";
				text += now.Hour.ToString("00");
				text += now.Minute.ToString("00");
				text += now.Second.ToString("00");
			}
		}
		arg_list.Add("-osubdir=" + text);
		if (test_run)
		{
			output_directory = Path.GetTempPath() + "pipp_testrun";
			output_path = output_directory;
			output_path = output_path.TrimStart('"');
			arg_list.Add("-od=" + output_directory);
		}
		else if (!outputdirectoryTB.Text.StartsWith("Default "))
		{
			string text2 = outputdirectoryTB.Text;
			arg_list.Add("-od=" + text2);
			output_directory = Path.Combine(outputdirectoryTB.Text, text);
		}
		else
		{
			string text3 = image_filelist.Items[0].SubItems[1].Text;
			arg_list.Add("-od=" + text3);
			output_directory = Path.Combine(text3, text);
		}
		if (outputprefixTB.Text != "")
		{
			arg_list.Add("-op=" + outputprefixTB.Text);
		}
		if (outputsuffixTB.Text != "")
		{
			arg_list.Add("-os=" + outputsuffixTB.Text);
		}
		if (test_run)
		{
			arg_list.Add("-bmp");
		}
		else
		{
			if (outputaviRB.Checked)
			{
				arg_list.Add("-avi");
			}
			if (outputserRB.Checked)
			{
				arg_list.Add("-ser");
			}
			if (outputbmpRB.Checked)
			{
				arg_list.Add("-bmp");
			}
			if (outputtifRB.Checked)
			{
				arg_list.Add("-tif");
			}
			if (outputpngRB.Checked)
			{
				arg_list.Add("-png");
			}
			if (outputfitsRB.Checked)
			{
				arg_list.Add("-fits");
			}
			if (outputgifRB.Checked)
			{
				arg_list.Add("-gif");
			}
		}
		if (outputaviRB.Checked)
		{
			if (avi_outputGB.Enabled)
			{
				arg_list.Add("-avi_codec=" + avi_codecCB.SelectedIndex);
			}
			if (avi_outputGB.Enabled && codec_qualityGB.Enabled)
			{
				arg_list.Add("-avi_quality=" + codec_qualityNUD.Value);
			}
			if (avi_outputGB.Enabled && old_format_aviCB.Checked)
			{
				if (old_format_max_sizeCB.SelectedIndex == 0)
				{
					arg_list.Add("-avi_old4");
				}
				else
				{
					arg_list.Add("-avi_old");
				}
			}
			if (avi_safe_frame_widthCB.Enabled && avi_safe_frame_widthCB.Checked)
			{
				arg_list.Add("-avi_safe_width");
			}
			if (avi_fpsCB.Checked)
			{
				arg_list.Add("-auto_fps");
			}
			int num = (int)(avi_fpsNUD.Value * 100m);
			int num2;
			if (num % 10 != 0)
			{
				num2 = 100;
			}
			else if (num % 100 != 0)
			{
				num2 = 10;
				num /= 10;
			}
			else
			{
				num2 = 1;
				num /= 100;
			}
			arg_list.Add("-fps_rate=" + num);
			arg_list.Add("-fps_scale=" + num2);
		}
		if (outputgifRB.Checked)
		{
			if (gif_repeat_foreverCB.Checked)
			{
				arg_list.Add("-anim_all_rep_always");
			}
			if (gif_fpsCB.Checked)
			{
				arg_list.Add("-auto_fps");
			}
			int num3 = (int)(gif_fpsNUD.Value * 100m);
			int num4;
			if (num3 % 10 != 0)
			{
				num4 = 100;
			}
			else if (num3 % 100 != 0)
			{
				num4 = 10;
				num3 /= 10;
			}
			else
			{
				num4 = 1;
				num3 /= 100;
			}
			arg_list.Add("-fps_rate=" + num3);
			arg_list.Add("-fps_scale=" + num4);
			arg_list.Add("-gif_quant_type=" + gif_quantCB.SelectedIndex);
			int num5 = 0;
			int num6 = 0;
			int num7 = 8;
			int num8 = -1;
			num5 = ((!gif_border_tolCB.Checked) ? (-1) : ((int)gif_border_tolNUD.Value));
			num6 = ((!gif_trans_pixel_tolCB.Checked) ? (-1) : ((int)gif_trans_pixel_tolNUD.Value));
			num7 = ((!gif_bit_depthCB.Checked) ? 8 : ((int)gif_bit_depthNUD.Value));
			num8 = (gif_lossy_comp_levelCB.Checked ? ((int)gif_lossy_comp_levelNUD.Value) : 0);
			arg_list.Add("-gif_border_tol=" + num5);
			arg_list.Add("-gif_trns_pxl_tol=" + num6);
			arg_list.Add("-gif_col_tbl_depth=" + num7);
			arg_list.Add("-gif_lossy_comp=" + num8);
		}
		if (renumberGB.Enabled && keepimagefilenamesCB.Checked)
		{
			arg_list.Add("-keepimgname");
		}
		if (renumberGB.Enabled && noframerenumberCB.Checked)
		{
			arg_list.Add("-nr");
		}
		if (renumberGB.Enabled && !addleadingzerosCB.Checked)
		{
			arg_list.Add("-nlz");
		}
		if (renumberGB.Enabled && includequalityvalueCB.Checked)
		{
			arg_list.Add("-incqv");
		}
		if (rgbsplitGB.Enabled && rgbsplitCB.Checked && !test_run)
		{
			arg_list.Add("-rgb");
		}
		if (outputframedropNUD.Value > 0m)
		{
			arg_list.Add("-drop=" + outputframedropNUD.Value);
		}
		if (winjuposfilenameCB.Checked)
		{
			arg_list.Add("-wjpos_time=" + winjupostimeCB.SelectedIndex);
		}
		return 1;
	}

	private void gif_adv_optionsCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		switch (gif_adv_optionsCB.SelectedIndex)
		{
		case 0:
			gif_border_tolNUD.Value = 0m;
			gif_trans_pixel_tolCB.Checked = true;
			gif_trans_pixel_tolNUD.Value = 0m;
			gif_bit_depthCB.Checked = false;
			gif_bit_depthNUD.Value = 7m;
			gif_lossy_comp_levelCB.Checked = false;
			gif_lossy_comp_levelNUD.Value = 1m;
			break;
		case 1:
			gif_border_tolNUD.Value = 5m;
			gif_trans_pixel_tolCB.Checked = true;
			gif_trans_pixel_tolNUD.Value = 5m;
			gif_bit_depthCB.Checked = false;
			gif_bit_depthNUD.Value = 7m;
			gif_lossy_comp_levelCB.Checked = false;
			gif_lossy_comp_levelNUD.Value = 1m;
			break;
		case 2:
			gif_border_tolNUD.Value = 5m;
			gif_trans_pixel_tolCB.Checked = true;
			gif_trans_pixel_tolNUD.Value = 5m;
			gif_bit_depthCB.Checked = false;
			gif_bit_depthNUD.Value = 7m;
			gif_lossy_comp_levelCB.Checked = true;
			gif_lossy_comp_levelNUD.Value = 5m;
			break;
		case 3:
			gif_border_tolNUD.Value = 5m;
			gif_trans_pixel_tolCB.Checked = true;
			gif_trans_pixel_tolNUD.Value = 5m;
			gif_bit_depthCB.Checked = true;
			gif_bit_depthNUD.Value = 7m;
			gif_lossy_comp_levelCB.Checked = true;
			gif_lossy_comp_levelNUD.Value = 5m;
			break;
		case 4:
			gif_border_tolNUD.Value = 5m;
			gif_trans_pixel_tolCB.Checked = true;
			gif_trans_pixel_tolNUD.Value = 5m;
			gif_bit_depthCB.Checked = true;
			gif_bit_depthNUD.Value = 6m;
			gif_lossy_comp_levelCB.Checked = true;
			gif_lossy_comp_levelNUD.Value = 5m;
			break;
		}
	}

	private void gif_trans_pixel_tolCB_CheckedChanged(object sender, EventArgs e)
	{
		gif_trans_pixel_tolNUD.Enabled = gif_trans_pixel_tolCB.Checked;
	}

	private void gif_bit_depthCB_CheckedChanged(object sender, EventArgs e)
	{
		gif_bit_depthNUD.Enabled = gif_bit_depthCB.Checked;
	}

	private void gif_lossy_comp_levelCB_CheckedChanged(object sender, EventArgs e)
	{
		gif_lossy_comp_levelNUD.Enabled = gif_lossy_comp_levelCB.Checked;
	}

	private void output_folderBTN_Click(object sender, EventArgs e)
	{
		Process.Start("explorer.exe", output_directory_to_open);
	}

	private void convmonoCB_CheckedChanged(object sender, EventArgs e)
	{
		if (convmonoCB.Checked)
		{
			conv_mono_comboBox.Enabled = true;
		}
		else
		{
			conv_mono_comboBox.Enabled = false;
		}
	}

	private void histostretchCB_CheckedChanged(object sender, EventArgs e)
	{
		if (hs_white_pointCB.Checked)
		{
			hs_white_pointNUD.Enabled = true;
			rgbhistoCB.Enabled = true;
			return;
		}
		hs_white_pointNUD.Enabled = false;
		if (hs_black_pointCB.Checked)
		{
			rgbhistoCB.Enabled = true;
		}
		else
		{
			rgbhistoCB.Enabled = false;
		}
	}

	private void hs_black_pointCB_CheckedChanged(object sender, EventArgs e)
	{
		if (hs_black_pointCB.Checked)
		{
			rgbhistoCB.Enabled = true;
		}
		else if (hs_white_pointCB.Checked)
		{
			rgbhistoCB.Enabled = true;
		}
		else
		{
			rgbhistoCB.Enabled = false;
		}
	}

	private void overexposedCB_CheckedChanged(object sender, EventArgs e)
	{
		if (overexposedCB.Checked)
		{
			overexposedNUD.Enabled = true;
			overexposedPANEL.Enabled = true;
		}
		else
		{
			overexposedNUD.Enabled = false;
			overexposedPANEL.Enabled = false;
		}
	}

	private void checkplanetCB_CheckedChanged(object sender, EventArgs e)
	{
		if (checkplanetCB.Checked)
		{
			minsizeNUD.Enabled = true;
			autothresholdCB.Enabled = true;
			minpixelvalueGB.Enabled = true;
			centreplanetCB.Enabled = true;
			if (autothresholdCB.Checked)
			{
				thresholdNUD.Enabled = false;
			}
			else
			{
				thresholdNUD.Enabled = true;
			}
		}
		else
		{
			minsizeNUD.Enabled = false;
			autothresholdCB.Enabled = false;
			thresholdNUD.Enabled = false;
			minpixelvalueGB.Enabled = false;
			centreplanetCB.Enabled = false;
		}
	}

	private void autothresholdCB_CheckedChanged(object sender, EventArgs e)
	{
		if (autothresholdCB.Checked)
		{
			thresholdNUD.Enabled = false;
		}
		else
		{
			thresholdNUD.Enabled = true;
		}
	}

	private void centreplanetCB_CheckedChanged(object sender, EventArgs e)
	{
		if (centreplanetCB.Checked)
		{
			edge_of_shadowPAN.Enabled = true;
		}
		else
		{
			edge_of_shadowPAN.Enabled = false;
		}
		enablecropCB_CheckedChanged(sender, e);
	}

	private void centreplanetCB_EnabledChanged(object sender, EventArgs e)
	{
		if (centreplanetCB.Enabled)
		{
			if (centreplanetCB.Checked)
			{
				edge_of_shadowPAN.Enabled = true;
			}
			else
			{
				edge_of_shadowPAN.Enabled = false;
			}
		}
		else
		{
			edge_of_shadowPAN.Enabled = false;
		}
		enablecropCB_CheckedChanged(sender, e);
	}

	private void edge_in_shadowCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		switch (edge_in_shadowCB.SelectedIndex)
		{
		case 1:
			moon_shadowPB.Image = Resources.moon_shadow_tlsh;
			break;
		case 2:
			moon_shadowPB.Image = Resources.moon_shadow_lhs;
			break;
		case 3:
			moon_shadowPB.Image = Resources.moon_shadow_blhs;
			break;
		case 4:
			moon_shadowPB.Image = Resources.moon_shadow_trhs;
			break;
		case 5:
			moon_shadowPB.Image = Resources.moon_shadow_rhs;
			break;
		case 6:
			moon_shadowPB.Image = Resources.moon_shadow_brhs;
			break;
		case 7:
			moon_shadowPB.Image = Resources.moon_shadow_t;
			break;
		case 8:
			moon_shadowPB.Image = Resources.moon_shadow_b;
			break;
		default:
			moon_shadowPB.Image = Resources.moon_shadow_none;
			break;
		}
	}

	private void enablecropCB_CheckedChanged(object sender, EventArgs e)
	{
		if (enablecropCB.Checked)
		{
			enablecroppingPANEL.Enabled = true;
		}
		else
		{
			enablecroppingPANEL.Enabled = false;
		}
		if (enablecropCB.Checked || (centreplanetCB.Enabled && centreplanetCB.Checked))
		{
			enableoffsetPANEL.Enabled = true;
		}
		else
		{
			enableoffsetPANEL.Enabled = false;
		}
	}

	private void reset_processing_options()
	{
		input_gainNUD.Value = 1m;
		gammaNUD.Value = 1m;
		median_filterCB.Checked = false;
		convmonoCB.Checked = false;
		conv_mono_comboBox.SelectedIndex = 0;
		hs_white_pointCB.Checked = false;
		hs_white_pointNUD.Value = 75m;
		hs_black_pointCB.Checked = false;
		rgbhistoCB.Checked = true;
		overexposedCB.Checked = false;
		overexposedNUD.Value = 20m;
		checkplanetCB.Checked = false;
		minsizeNUD.Value = 15m;
		autothresholdCB.Checked = true;
		thresholdNUD.Value = 40m;
		centreplanetCB.Checked = false;
		edge_in_shadowCB.SelectedIndex = 0;
		enablecropCB.Checked = false;
		cropwidthNUD.Value = 450m;
		cropheightNUD.Value = 450m;
		xoffsetNUD.Value = 0m;
		yoffsetNUD.Value = 0m;
		flip_xCB.Checked = false;
		flip_yCB.Checked = false;
		rotationCB.SelectedIndex = 0;
		stabilise_noneRB.Checked = true;
		enableSurfaceTrackingCB.Checked = false;
		enablestabilisationCB.Checked = true;
		afb_xNUD.Value = 150m;
		afb_yNUD.Value = 150m;
		afb_widthNUD.Value = 128m;
		afb_heightNUD.Value = 128m;
		afb_rejectCB.Checked = true;
		aoi_enableCB.Checked = false;
		aoi_xNUD.Value = 10m;
		aoi_yNUD.Value = 10m;
		aoi_widthNUD.Value = 300m;
		aoi_heightNUD.Value = 300m;
		aoi_rejectCB.Checked = true;
		aoi_cropCB.Checked = true;
		resize_framesCB.Checked = false;
		resize_keep_aspect_ratioCB.Checked = true;
		resize_widthNUD.Value = 500m;
		resize_heightNUD.Value = 400m;
	}

	private int generate_cmdline_options_processing_options_tab(ref List<string> arg_list)
	{
		if (input_gainNUD.Value != 1m && input_gainNUD.Value > 0m)
		{
			arg_list.Add("-input_gain=" + input_gainNUD.Value.ToString(CultureInfo.CreateSpecificCulture("en-GB")));
		}
		if (gammaNUD.Value != 1m && gammaNUD.Value > 0m)
		{
			arg_list.Add("-gamma=" + gammaNUD.Value.ToString(CultureInfo.CreateSpecificCulture("en-GB")));
		}
		if (median_filterCB.Checked)
		{
			arg_list.Add("-median");
		}
		if (convmonoCB.Checked)
		{
			switch (conv_mono_comboBox.SelectedIndex)
			{
			case 0:
				arg_list.Add("-convmono=3");
				break;
			case 1:
				arg_list.Add("-convmono=2");
				break;
			case 2:
				arg_list.Add("-convmono=1");
				break;
			case 3:
				arg_list.Add("-convmono=0");
				break;
			case 4:
				arg_list.Add("-convmono=4");
				break;
			case 5:
				arg_list.Add("-convmono=5");
				break;
			case 6:
				arg_list.Add("-convmono=6");
				break;
			default:
				arg_list.Add("-convmono=3");
				break;
			}
		}
		if (hs_white_pointCB.Checked)
		{
			arg_list.Add("-hs=" + hs_white_pointNUD.Value);
		}
		if (hs_black_pointCB.Checked)
		{
			arg_list.Add("-hs_bp");
		}
		if ((hs_white_pointCB.Checked || hs_black_pointCB.Checked) && rgbhistoCB.Checked)
		{
			arg_list.Add("-hsrgb");
		}
		if (overexposedCB.Checked)
		{
			arg_list.Add("-oe=" + overexposedNUD.Value);
		}
		if (flip_xCB.Checked)
		{
			arg_list.Add("-flip_x");
		}
		if (flip_yCB.Checked)
		{
			arg_list.Add("-flip_y");
		}
		switch (rotationCB.SelectedIndex)
		{
		case 1:
			arg_list.Add("-rotate90");
			break;
		case 2:
			arg_list.Add("-rotate270");
			break;
		case 3:
			arg_list.Add("-rotate180");
			break;
		}
		if (stabilise_objectRB.Checked && checkplanetCB.Checked)
		{
			arg_list.Add("-chk");
			arg_list.Add("-ms=" + minsizeNUD.Value);
			if (minpixelvalueGB.Enabled && !autothresholdCB.Checked)
			{
				arg_list.Add("-mp=" + thresholdNUD.Value);
			}
		}
		if (stabilise_objectRB.Checked && checkplanetCB.Checked)
		{
			if (centreplanetCB.Enabled && centreplanetCB.Checked)
			{
				arg_list.Add("-ctr");
			}
			if (edge_in_shadowCB.SelectedIndex > 0)
			{
				arg_list.Add("-shdw=" + edge_in_shadowCB.SelectedIndex);
			}
		}
		if (stabilise_noneRB.Checked || stabilise_objectRB.Checked)
		{
			if (enablecroppingPANEL.Enabled)
			{
				arg_list.Add("-cx=" + cropwidthNUD.Value);
				arg_list.Add("-cy=" + cropheightNUD.Value);
			}
			if (enableoffsetPANEL.Enabled)
			{
				if (xoffsetNUD.Value != 0m)
				{
					arg_list.Add("-ox=" + xoffsetNUD.Value);
				}
				if (yoffsetNUD.Value != 0m)
				{
					arg_list.Add("-oy=" + yoffsetNUD.Value);
				}
			}
		}
		if (stabilise_surfaceRB.Checked && enableSurfaceTrackingCB.Checked)
		{
			arg_list.Add("-en_surface_feature_tracking");
			if (enablestabilisationCB.Enabled && enablestabilisationCB.Checked)
			{
				arg_list.Add("-en_ss");
			}
			arg_list.Add("-afb_x=" + afb_xNUD.Value);
			arg_list.Add("-afb_y=" + afb_yNUD.Value);
			arg_list.Add("-afb_width=" + afb_widthNUD.Value);
			arg_list.Add("-afb_height=" + afb_heightNUD.Value);
			if (afb_rejectCB.Checked)
			{
				arg_list.Add("-freject");
			}
			if (aoi_enableCB.Checked)
			{
				arg_list.Add("-aoi");
				arg_list.Add("-aoi_x=" + aoi_xNUD.Value);
				arg_list.Add("-aoi_y=" + aoi_yNUD.Value);
				arg_list.Add("-aoi_width=" + aoi_widthNUD.Value);
				arg_list.Add("-aoi_height=" + aoi_heightNUD.Value);
				if (aoi_rejectCB.Checked)
				{
					arg_list.Add("-aoi_reject");
				}
				if (aoi_cropCB.Checked)
				{
					arg_list.Add("-aoi_crop");
				}
			}
		}
		if (resize_framesCB.Checked)
		{
			arg_list.Add("-resize_en");
			arg_list.Add("-resize_width=" + resize_widthNUD.Value);
			if (resize_keep_aspect_ratioCB.Checked)
			{
				arg_list.Add("-resize_height=-1");
			}
			else
			{
				arg_list.Add("-resize_height=" + resize_heightNUD.Value);
			}
		}
		return 1;
	}

	private void enableSurfaceTrackingCB_CheckedChanged(object sender, EventArgs e)
	{
		if (enableSurfaceTrackingCB.Checked)
		{
			anchor_feature_boxGB.Enabled = true;
			area_of_interestGB.Enabled = true;
			enablestabilisationCB.Enabled = true;
		}
		else
		{
			anchor_feature_boxGB.Enabled = false;
			area_of_interestGB.Enabled = false;
			enablestabilisationCB.Enabled = false;
		}
		if (!test_run_dialog.IsDisposed)
		{
			if (stabilise_surfaceRB.Checked && enableSurfaceTrackingCB.Checked)
			{
				test_run_dialog.enable_afb(enable: true);
			}
			else
			{
				test_run_dialog.enable_afb(enable: false);
			}
		}
		if (stabilise_surfaceRB.Checked && enableSurfaceTrackingCB.Checked && aoi_enableCB.Checked)
		{
			if (aoi_xNUD.Value > afb_xNUD.Value)
			{
				aoi_xNUD.Value = afb_xNUD.Value;
			}
			if (aoi_yNUD.Value > afb_yNUD.Value)
			{
				aoi_yNUD.Value = afb_yNUD.Value;
			}
			if (aoi_xNUD.Value + aoi_widthNUD.Value < afb_xNUD.Value + afb_widthNUD.Value)
			{
				aoi_widthNUD.Value = afb_xNUD.Value + afb_widthNUD.Value - aoi_xNUD.Value;
			}
			if (aoi_yNUD.Value + aoi_heightNUD.Value < afb_yNUD.Value + afb_heightNUD.Value)
			{
				aoi_heightNUD.Value = afb_yNUD.Value + afb_heightNUD.Value - aoi_yNUD.Value;
			}
		}
		if (test_run_dialog != null && !test_run_dialog.IsDisposed)
		{
			test_run_dialog.update_image();
		}
	}

	private void stabilisation_CheckedChanged()
	{
		if (stabilise_noneRB.Checked)
		{
			planetdetectGB.Visible = false;
			croppingGB.Visible = true;
			surface_stabiliseGB.Visible = false;
		}
		else if (stabilise_objectRB.Checked)
		{
			planetdetectGB.Visible = true;
			croppingGB.Visible = true;
			surface_stabiliseGB.Visible = false;
		}
		else if (stabilise_surfaceRB.Checked)
		{
			planetdetectGB.Visible = false;
			croppingGB.Visible = false;
			surface_stabiliseGB.Visible = true;
		}
	}

	public bool is_afb_enabled()
	{
		if (stabilise_surfaceRB.Checked)
		{
			return enableSurfaceTrackingCB.Checked;
		}
		return false;
	}

	public bool is_aoi_enabled()
	{
		if (stabilise_surfaceRB.Checked && aoi_enableCB.Checked)
		{
			return enableSurfaceTrackingCB.Checked;
		}
		return false;
	}

	private void resize_framesCB_CheckedChanged(object sender, EventArgs e)
	{
		if (resize_framesCB.Checked)
		{
			resize_keep_aspect_ratioCB.Enabled = true;
			resize_heightNUD.Enabled = true;
			resize_heightLAB.Enabled = true;
			resize_widthNUD.Enabled = true;
			resize_widthLAB.Enabled = true;
			resize_keep_aspect_ratioCB_CheckedChanged(sender, e);
		}
		else
		{
			resize_keep_aspect_ratioCB.Enabled = false;
			resize_heightNUD.Enabled = false;
			resize_heightLAB.Enabled = false;
			resize_widthNUD.Enabled = false;
			resize_widthLAB.Enabled = false;
		}
	}

	private void resize_keep_aspect_ratioCB_CheckedChanged(object sender, EventArgs e)
	{
		if (resize_keep_aspect_ratioCB.Checked)
		{
			resize_heightNUD.Enabled = false;
			resize_heightLAB.Enabled = false;
		}
		else
		{
			resize_heightNUD.Enabled = true;
			resize_heightLAB.Enabled = true;
		}
	}

	private void qualitysortingCB_CheckedChanged(object sender, EventArgs e)
	{
		set_quality_dialogue_options();
		includequalityvalueCB.Enabled = qualitysortingCB.Checked;
	}

	private void qualityframelimitCB_CheckedChanged(object sender, EventArgs e)
	{
		set_quality_dialogue_options();
	}

	private void quality_limiting_typeCB_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (quality_limiting_typeCB.SelectedIndex == 0)
		{
			qualityframesNUD.Maximum = 99999m;
			qualityframesNUD.Value = 1200m;
		}
		else
		{
			qualityframesNUD.Maximum = 100m;
			qualityframesNUD.Value = 25m;
		}
	}

	private void mindownsampleNUD_ValueChanged(object sender, EventArgs e)
	{
		if (mindownsampleNUD.Value > maxdownsampleNUD.Value)
		{
			mindownsampleNUD.Value = maxdownsampleNUD.Value;
		}
	}

	private void maxdownsampleNUD_ValueChanged(object sender, EventArgs e)
	{
		if (maxdownsampleNUD.Value < mindownsampleNUD.Value)
		{
			maxdownsampleNUD.Value = mindownsampleNUD.Value;
		}
	}

	private void set_quality_dialogue_options()
	{
		if (qualitysortingCB.Checked)
		{
			qualitylimitingGB.Enabled = true;
			qualityframelimitCB.Enabled = true;
			qualityframesNUD.Enabled = true;
			quality_limiting_typeCB.Enabled = true;
			qualityalgorithmGB.Enabled = true;
			if (defaultalgorithmCB.Checked)
			{
				qualityweightGB.Enabled = true;
				qualityoptionsGB.Enabled = true;
			}
			else if (ninoxalgorithmCB.Checked)
			{
				qualityweightGB.Enabled = true;
				qualityoptionsGB.Enabled = true;
			}
			else if (histoalgorithmCB.Checked)
			{
				qualityweightGB.Enabled = true;
				qualityoptionsGB.Enabled = false;
			}
			else
			{
				qualityweightGB.Enabled = false;
				qualityoptionsGB.Enabled = false;
			}
			if (qualityframelimitCB.Checked)
			{
				qualityframesNUD.Enabled = true;
				quality_limiting_typeCB.Enabled = true;
			}
			else
			{
				qualityframesNUD.Enabled = false;
				quality_limiting_typeCB.Enabled = false;
			}
			qreorderCB.Enabled = true;
			if (defaultalgorithmCB.Checked || ninoxalgorithmCB.Checked)
			{
				qualityoptionsGB.Enabled = true;
			}
		}
		else
		{
			qualitylimitingGB.Enabled = false;
			qualityframelimitCB.Enabled = false;
			qualityframesNUD.Enabled = false;
			quality_limiting_typeCB.Enabled = false;
			qreorderCB.Enabled = false;
			qualityalgorithmGB.Enabled = false;
			qualityweightGB.Enabled = false;
			qualityoptionsGB.Enabled = false;
		}
	}

	private void reset_quality_options()
	{
		qualitysortingCB.Checked = false;
		qreorderCB.Checked = true;
		qualityframelimitCB.Checked = true;
		quality_limiting_typeCB.SelectedIndex = 0;
		qualityframesNUD.Value = 1200m;
		defaultalgorithmCB.Checked = true;
		mindownsampleNUD.Value = 3m;
		maxdownsampleNUD.Value = 5m;
		downsampleincNUD.Value = 1m;
		qualityweightingCB.Checked = false;
	}

	private int generate_cmdline_options_quality_options_tab(ref List<string> arg_list, bool animation_mode)
	{
		if (animation_mode && !test_run && qualitysortingCB.Checked)
		{
			MessageBoxEx.Show("Error: Quality Estimation & Animation Options cannot be used together", "Quality Estimation & Animation Options", MessageBoxButtons.OK, MessageBoxIcon.Hand, 10000u);
			return -1;
		}
		if (qualitysortingCB.Checked && !test_run)
		{
			qual_progressPAN.Show();
			if (qualityframelimitCB.Checked)
			{
				if (quality_limiting_typeCB.SelectedIndex == 0)
				{
					arg_list.Add("-q=" + qualityframesNUD.Value);
				}
				else
				{
					arg_list.Add("-qpcnt=" + qualityframesNUD.Value);
				}
			}
			else
			{
				arg_list.Add("-q");
			}
			if (!qreorderCB.Checked)
			{
				arg_list.Add("-nr");
			}
		}
		else
		{
			qual_progressPAN.Hide();
		}
		calibrationGB.Visible = false;
		if (dark_frame_subtractionCB.Checked && dark_filelist.total_frame_count > 0)
		{
			dark_stacking_progress_PAN.Show();
			calibrationGB.Visible = true;
		}
		else
		{
			dark_stacking_progress_PAN.Hide();
		}
		if (flat_frame_calibrationCB.Checked && flat_filelist.total_frame_count > 0)
		{
			flat_stacking_progress_PAN.Show();
			calibrationGB.Visible = true;
		}
		else
		{
			flat_stacking_progress_PAN.Hide();
		}
		if (flat_dark_frame_subtractionCB.Checked && flat_dark_filelist.total_frame_count > 0 && flat_frame_calibrationCB.Checked && flat_filelist.total_frame_count > 0)
		{
			flat_dark_stacking_progress_PAN.Show();
			calibrationGB.Visible = true;
		}
		else
		{
			flat_dark_stacking_progress_PAN.Hide();
		}
		if (qualitysortingCB.Checked && !test_run)
		{
			if (ninoxalgorithmCB.Checked)
			{
				arg_list.Add("-qold");
			}
			if (histoalgorithmCB.Checked)
			{
				arg_list.Add("-qhisto");
			}
			if (totalpixelalgorithmCB.Checked)
			{
				arg_list.Add("-qtotal");
			}
		}
		if (qualitysortingCB.Checked && !test_run && qualityoptionsGB.Enabled)
		{
			arg_list.Add("-qmin=" + mindownsampleNUD.Value);
			arg_list.Add("-qmax=" + maxdownsampleNUD.Value);
			arg_list.Add("-qinc=" + downsampleincNUD.Value);
		}
		if (qualitysortingCB.Checked && !test_run && qualityweightGB.Enabled && qualityweightingCB.Checked)
		{
			arg_list.Add("-qw");
		}
		return 1;
	}

	public Form1()
	{
		InitializeComponent();
		stabilisation_CheckedChanged();
		temp_directory = Path.Combine(Path.GetTempPath(), "pipp_" + Path.GetRandomFileName());
		Directory.CreateDirectory(temp_directory);
		dark_filelist.set_initial_source_dir(1);
		flat_filelist.set_initial_source_dir(2);
		flat_dark_filelist.set_initial_source_dir(3);
		binningCB.SelectedIndex = 0;
		binning_calculationCB.SelectedIndex = 0;
		raw_debayer_algorithmCB.SelectedIndex = 0;
		raw_output_colour_spaceCB.SelectedIndex = 0;
		raw_highlight_recovery_typeCB.SelectedIndex = 1;
		serbitdepthCB.SelectedIndex = 0;
		colourdetectionCB.SelectedIndex = 0;
		debay_typeCB.SelectedIndex = 0;
		debay_patternCB.SelectedIndex = 0;
		limitframesCB.SelectedIndex = 0;
		edge_in_shadowCB.SelectedIndex = 0;
		inputdatetimeformatCB.SelectedIndex = 0;
		quality_limiting_typeCB.SelectedIndex = 0;
		winjupostimeCB.SelectedIndex = 0;
		avi_codecCB.SelectedIndex = 0;
		old_format_max_sizeCB.SelectedIndex = 0;
		conv_mono_comboBox.SelectedIndex = 0;
		rotationCB.SelectedIndex = 0;
		gif_quantCB.SelectedIndex = 0;
		gif_adv_optionsCB.SelectedIndex = 4;
		gif_adv_optionsCB.SelectedIndex = 1;
		pipp_process_running = false;
		pipp_output_received = false;
		output_path = "";
		test_run_dialog = new testrunForm(this);
		cPersistentData.init();
		if (cPersistentData.load() < 0)
		{
			bool flag = true;
			try
			{
				XmlSerializer xmlSerializer = new XmlSerializer(persistant_data.GetType());
				TextReader textReader = new StreamReader(Application.LocalUserAppDataPath + "\\pipp.config", detectEncodingFromByteOrderMarks: false);
				persistant_data = (cPersistantData)xmlSerializer.Deserialize(textReader);
				textReader.Close();
			}
			catch
			{
				flag = false;
			}
			if (flag)
			{
				cPersistentData.source_directory = persistant_data.source_directory;
				cPersistentData.source_filter_index = persistant_data.source_filter_index;
				cPersistentData.options_directory = persistant_data.options_directory;
				cPersistentData.output_directory = persistant_data.output_directory;
				cPersistentData.custom_date_time_filter1 = persistant_data.custom_date_time_filter1;
				cPersistentData.custom_date_time_filter2 = persistant_data.custom_date_time_filter2;
				cPersistentData.custom_date_time_filter3 = persistant_data.custom_date_time_filter3;
				cPersistentData.winjupos_remember = persistant_data.winjupos_remember;
				cPersistentData.inputdatetimeformat = persistant_data.inputdatetimeformat;
				cPersistentData.raw_file_extensions = persistant_data.raw_file_extensions;
			}
		}
		wjpos_rememberCB.Checked = cPersistentData.winjupos_remember;
		if (cPersistentData.winjupos_remember)
		{
			inputdatetimeformatCB.SelectedIndex = cPersistentData.inputdatetimeformat;
		}
		open_folderCB.Checked = cPersistentData.open_output_folder;
		if (cPersistentData.raw_file_extensions == string.Empty || cPersistentData.raw_file_extensions.StartsWith(","))
		{
			cPersistentData.raw_file_extensions = "3fr,arw,cr2,crw,dcr,dng,erf,orf,raf,raw,rw2,nef,nrw,mef,mos,mrw,pef,x3f";
		}
		check_updatesMenuItem.Checked = cPersistentData.check_for_updates;
		use_ram_buffersMenuItem.Checked = cPersistentData.use_ram_buffers;
		Thread thread = new Thread(CheckForNewVersion);
		thread.Start();
	}

	public void set_dark_frame_progress(int progress)
	{
		if (progress > 100)
		{
			progress = 50;
		}
		Invoke((MethodInvoker)(() =>
		{
			dark_stackingPB.Value = progress;
		}));
		Invoke((MethodInvoker)(() =>
		{
			dark_stackingLAB.Text = progress + "%";
		}));
	}

	public void set_flat_dark_frame_progress(int progress)
	{
		if (progress > 100)
		{
			progress = 100;
		}
		Invoke((MethodInvoker)(() =>
		{
			flat_dark_stackingPB.Value = progress;
		}));
		Invoke((MethodInvoker)(() =>
		{
			flat_dark_stackingLAB.Text = progress + "%";
		}));
	}

	public void set_flat_frame_progress(int progress)
	{
		if (progress > 100)
		{
			progress = 100;
		}
		Invoke((MethodInvoker)(() =>
		{
			flat_stackingPB.Value = progress;
		}));
		Invoke((MethodInvoker)(() =>
		{
			flat_stackingLAB.Text = progress + "%";
		}));
	}

	private void CheckForNewVersion()
	{
		if (!check_updatesMenuItem.Checked)
		{
			return;
		}
		using WebClient webClient = new WebClient();
		string text;
		try
		{
			text = webClient.DownloadString("https://sites.google.com/site/astropipp/version.txt");
		}
		catch
		{
			return;
		}
		string[] array = text.Split('\n');
		array[0] = array[0].TrimEnd('\r', '\n');
		if (string.Equals(version, array[0], StringComparison.Ordinal))
		{
			return;
		}
		string[] array2 = array[0].Split('.');
		if (array2.Length < 3 || !array2[0].StartsWith("v"))
		{
			return;
		}
		array2[0] = array2[0].Substring(1);
		int[] array3 = new int[3];
		for (int i = 0; i < 3; i++)
		{
			if (!int.TryParse(array2[i], out array3[i]))
			{
				return;
			}
		}
		string[] array4 = version.Split('.');
		if (array4.Length < 3 || !array4[0].StartsWith("v"))
		{
			return;
		}
		array4[0] = array4[0].Substring(1);
		int[] array5 = new int[3];
		for (int j = 0; j < 3; j++)
		{
			if (!int.TryParse(array4[j], out array5[j]))
			{
				return;
			}
		}
		for (int k = 0; k < 3; k++)
		{
			if (array5[k] > array3[k])
			{
				return;
			}
			if (array5[k] < array3[k])
			{
				break;
			}
		}
		MessageBox.Show("A new version of PIPP is available:\n\n" + text + "\n\nTo upgrade from version " + version + " download the latest version from\nhttp://sites.google.com/site/astropipp/downloads\n\nThis check can be disabled under the Options menu", "PIPP Update Check", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void EnabledChangedEventHandler(object sender, EventArgs e)
	{
		Control control = (Control)sender;
		if (control.Enabled)
		{
			control.BackColor = Color.White;
		}
		else
		{
			control.BackColor = Color.WhiteSmoke;
		}
	}

	private void runBTN_Click(object sender, EventArgs e)
	{
		test_run = false;
		show_detected_pixels = false;
		start_processing();
	}

	private void pipp_process_OutputDataReceived(object sender, DataReceivedEventArgs e)
	{
		if (e.Data == null)
		{
			pipp_process.CancelOutputRead();
			stopwatch.Stop();
			outputTB.AppendText("Runtime: " + Math.Round(stopwatch.Elapsed.TotalSeconds, 1) + " seconds");
			if (processed_killed)
			{
				outputTB.AppendText("\r\nPROCESSING CANCELLED\r\n");
			}
			else
			{
				outputTB.AppendText("\r\nPROCESSING COMPLETE\r\n");
			}
			pipp_output_received = true;
			set_testrun_details();
		}
		else
		{
			string rx_text = e.Data;
			pipp_process_handle_received_string(ref rx_text);
		}
	}

	private void pipp_process_handle_received_string(ref string rx_text)
	{
		if (string.IsNullOrEmpty(rx_text))
		{
			return;
		}
		if (!rx_text.StartsWith("@"))
		{
			outputTB.AppendText(Encoding.UTF8.GetString(Encoding.Default.GetBytes(rx_text)));
			outputTB.AppendText("\r\n");
		}
		else if (rx_text.StartsWith("@PROGRESS="))
		{
			string s = rx_text.Remove(0, 10);
			if (int.TryParse(s, out var result))
			{
				if (result > 100)
				{
					result = 100;
				}
				processPB.Value = result;
				progressLAB.Text = result + "%";
			}
		}
		else if (rx_text.StartsWith("@PFILE="))
		{
			string s2 = rx_text.Remove(0, 7);
			if (int.TryParse(s2, out var result2))
			{
				if (result2 > 100)
				{
					result2 = 100;
				}
				processfilePB.Value = result2;
			}
		}
		else if (rx_text.StartsWith("@PFILELAB="))
		{
			string text = Encoding.UTF8.GetString(Encoding.Default.GetBytes(rx_text.Remove(0, 10)));
			processfileLAB.Text = text;
		}
		else if (rx_text.StartsWith("@FNAMELAB="))
		{
			string text2 = Encoding.UTF8.GetString(Encoding.Default.GetBytes(rx_text.Remove(0, 10)));
			fileLAB.Text = "File: " + text2;
		}
		else if (rx_text.StartsWith("@PFRAMELAB="))
		{
			string s3 = rx_text.Remove(0, 11);
			if (int.TryParse(s3, out var result3))
			{
				processframesLAB.Text = "Processing " + result3 + " frames";
			}
		}
		else if (rx_text.StartsWith("@QSORT="))
		{
			string s4 = rx_text.Remove(0, 7);
			if (int.TryParse(s4, out var result4))
			{
				qualityPB.Value = result4;
				qualityLAB.Text = result4 + "%";
			}
		}
		else if (rx_text.StartsWith("@QSFRAMESLAB="))
		{
			string s5 = rx_text.Remove(0, 13);
			if (int.TryParse(s5, out var result5))
			{
				qsframeLAB.Text = quality_sort_label1 + result5 + quality_sort_label2;
			}
		}
		else if (rx_text.StartsWith("@ANIM="))
		{
			string s6 = rx_text.Remove(0, 6);
			if (int.TryParse(s6, out var result6))
			{
				if (result6 > 100)
				{
					result6 = 100;
				}
				animPB.Value = result6;
				animLAB.Text = result6 + "%";
			}
		}
		else if (rx_text.StartsWith("XPOS="))
		{
			string s7 = rx_text.Remove(0, 6);
			if (int.TryParse(s7, out var result7))
			{
				xcentre = result7;
			}
		}
		else if (rx_text.StartsWith("@YPOS="))
		{
			string s8 = rx_text.Remove(0, 6);
			if (int.TryParse(s8, out var result8))
			{
				ycentre = result8;
			}
		}
		else if (rx_text.StartsWith("@MAX_PIXEL="))
		{
			string s9 = rx_text.Remove(0, 11);
			if (int.TryParse(s9, out var result9))
			{
				max_pixel = result9;
				max_b_pixel = -1;
				max_g_pixel = -1;
				max_r_pixel = -1;
			}
		}
		else if (rx_text.StartsWith("@MAX_R_PIXEL="))
		{
			string s10 = rx_text.Remove(0, 13);
			if (int.TryParse(s10, out var result10))
			{
				max_r_pixel = result10;
				max_pixel = -1;
			}
		}
		else if (rx_text.StartsWith("@MAX_G_PIXEL="))
		{
			string s11 = rx_text.Remove(0, 13);
			if (int.TryParse(s11, out var result11))
			{
				max_g_pixel = result11;
				max_pixel = -1;
			}
		}
		else if (rx_text.StartsWith("@MAX_B_PIXEL="))
		{
			string s12 = rx_text.Remove(0, 13);
			if (int.TryParse(s12, out var result12))
			{
				max_b_pixel = result12;
				max_pixel = -1;
			}
		}
		else if (rx_text.StartsWith("@DETECT_THRESHOLD="))
		{
			string s13 = rx_text.Remove(0, 18);
			if (int.TryParse(s13, out var result13))
			{
				detect_threshold = result13;
			}
			if (show_detected_pixels && autothresholdCB.Checked)
			{
				thresholdNUD.Value = detect_threshold;
			}
		}
		else if (rx_text.StartsWith("@FNUM="))
		{
			string s14 = rx_text.Remove(0, 6);
			if (int.TryParse(s14, out var result14))
			{
				frame_number = result14;
			}
		}
		else if (rx_text.StartsWith("@FCNT="))
		{
			string s15 = rx_text.Remove(0, 6);
			if (int.TryParse(s15, out var result15))
			{
				frame_count = result15;
			}
		}
	}

	private void pipp_process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
	{
		if (e.Data == null)
		{
			pipp_process.CancelErrorRead();
			if (error_data != "")
			{
				MessageBox.Show(error_data, "Processing Errors", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
		if (!string.IsNullOrEmpty(e.Data))
		{
			error_data = error_data + e.Data + "\r\n";
		}
	}

	private void pipp_process_Exited(object sender, EventArgs e)
	{
		runBTN.Enabled = true;
		runBTN.Text = "Start Processing";
		dotestrunBTN.Enabled = true;
		dotestrunBTN.Text = "Test Options";
		Text = "OPIPP 0.1.5-alpha - Open PIPP";
		((Control)tabPage1).Enabled = true;
		((Control)tabPage7).Enabled = true;
		((Control)tabPage2).Enabled = true;
		((Control)tabPage3).Enabled = true;
		((Control)tabPage5).Enabled = true;
		((Control)tabPage4).Enabled = true;
		pipp_process_running = false;
		killpippBTN.Enabled = false;
		doProcessToolStripMenuItem.Enabled = true;
		cancelProcessingToolStripMenuItem.Enabled = false;
		if (processed_killed)
		{
			statusstateLAB.Text = "CANCELLED";
			statusstateLAB.Left = (statusGB.Width - statusstateLAB.Width) / 2;
			dark_frame_stackingLAB.Text = "Stacking 0 dark frames";
			set_dark_frame_progress(0);
			flat_dark_stackingLAB.Text = "Stacking 0 flat dark frames";
			set_flat_dark_frame_progress(0);
			flat_stackingLAB.Text = "Stacking 0 flat frames";
			set_flat_frame_progress(0);
			fileLAB.Text = "File: none";
			processfilePB.Value = 0;
			processfileLAB.Text = "File 0 of 0";
			processframesLAB.Text = "Processing 0 frames";
			processPB.Value = 0;
			progressLAB.Text = "0%";
			qsframeLAB.Text = quality_sort_label1 + "0" + quality_sort_label2;
			qualityPB.Value = 0;
			qualityLAB.Text = "0%";
			string path = output_directory.Trim('"', ' ');
			if (Directory.Exists(path))
			{
				string[] files = Directory.GetFiles(path, "*.tmp");
				foreach (string path2 in files)
				{
					File.Delete(path2);
				}
			}
			return;
		}
		int exitCode = pipp_process.ExitCode;
		if (exitCode < 0)
		{
			statusstateLAB.Text = "FAILED WITH ERRORS";
			processing_failed = true;
		}
		else
		{
			statusstateLAB.Text = "COMPLETE";
			processing_failed = false;
		}
		statusstateLAB.Left = (statusGB.Width - statusstateLAB.Width) / 2;
		if (test_run)
		{
			if (exitCode >= 0)
			{
				set_testrun_details();
			}
			return;
		}
		string text = output_directory.Trim('"', ' ');
		text += "\\logs\\pipp_options.pof";
		save_options(text);
		if (exitCode >= 0)
		{
			output_directory_to_open = output_directory;
			output_folderBTN.Enabled = true;
			SystemSounds.Asterisk.Play();
			if (open_folderCB.Checked)
			{
				Process.Start("explorer.exe", output_directory_to_open);
			}
		}
	}

	private void set_testrun_details()
	{
		if (!test_run || pipp_process_running || !pipp_output_received)
		{
			return;
		}
		if (processing_failed)
		{
			test_run_dialog.Dispose();
			return;
		}
		if (test_run_dialog.IsDisposed)
		{
			test_run_dialog = new testrunForm(this);
		}
		test_run_dialog.set_images(output_path + "\\pipp_input_frame.bmp", output_path + "\\pipp_output_frame.bmp");
		test_run_dialog.set_max_pixel(max_pixel, max_r_pixel, max_g_pixel, max_b_pixel, frame_number, frame_count);
		if (show_detected_pixels)
		{
			test_run_dialog.set_detect_threshold(detect_threshold);
		}
		test_run_dialog.Show();
	}

	private void killpippBTN_Click(object sender, EventArgs e)
	{
		cancel_processing();
	}

	private void Form1_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (!exit_checks())
		{
			e.Cancel = true;
		}
		try
		{
			Directory.Delete(temp_directory, recursive: true);
		}
		catch
		{
		}
	}

	public void do_test_run(bool show_detected_pixels_value)
	{
		test_run = true;
		show_detected_pixels = show_detected_pixels_value;
		start_processing();
	}

	public bool processing_acive()
	{
		return !dotestrunBTN.Enabled;
	}

	private void testOptionsToolStripMenuItem_Click(object sender, EventArgs e)
	{
		do_test_run(show_detected_pixels_value: false);
	}

	private void dotestrunBTN_Click(object sender, EventArgs e)
	{
		do_test_run(show_detected_pixels_value: false);
	}

	private void test_detect_thresholdBUT_Click(object sender, EventArgs e)
	{
		do_test_run(show_detected_pixels_value: true);
	}

	private void removeAllFilesToolStripMenuItem_Click(object sender, EventArgs e)
	{
		image_filelist.Items.Clear();
		image_filelist.total_frame_count = 0;
		tab_image_files.Text = "Image Files (0 frames)";
		remove_button.Enabled = false;
		file_up_BUT.Enabled = false;
		file_down_BUT.Enabled = false;
		remove_all_button.Enabled = false;
		dark_filelist.Items.Clear();
		dark_filelist.total_frame_count = 0;
		tab_dark_files.Text = "Dark Files (0 frames)";
		remove_dark_filesBUT.Enabled = false;
		remove_all_dark_filesBUT.Enabled = false;
		flat_filelist.Items.Clear();
		flat_filelist.total_frame_count = 0;
		tab_flat_files.Text = "Flat Files (0 frames)";
		remove_flat_filesBUT.Enabled = false;
		remove_all_flat_filesBUT.Enabled = false;
		flat_dark_filelist.Items.Clear();
		flat_dark_filelist.total_frame_count = 0;
		tab_flat_dark_files.Text = "Flat Files (0 frames)";
		remove_flat_dark_filesBUT.Enabled = false;
		remove_all_flat_dark_filesBUT.Enabled = false;
		update_date_time_extracted();
	}

	private void start_processing()
	{
		arg_list2 = new List<string>();
		if (test_run)
		{
			if (image_filelist.Items.Count == 0)
			{
				MessageBoxEx.Show("No Image Files Specified", "Test Options", MessageBoxButtons.OK, MessageBoxIcon.Hand, 10000u);
				return;
			}
		}
		else
		{
			if (image_filelist.Items.Count == 0)
			{
				MessageBoxEx.Show("No Image Files Specified", "Start Processing", MessageBoxButtons.OK, MessageBoxIcon.Hand, 10000u);
				return;
			}
			if (!create_output_subdirCB.Checked && outputprefixTB.Text == "" && outputsuffixTB.Text == "")
			{
				MessageBoxEx.Show("Cannot generate output files in the same source file directory without an output filename prefix or suffix", "Start Processing", MessageBoxButtons.OK, MessageBoxIcon.Hand, 10000u);
				return;
			}
		}
		if (generate_cmdline_options(ref arg_list2) >= 0)
		{
			if (histoalgorithmCB.Checked)
			{
				quality_sort_label2 = " frames by peak histogram";
			}
			else if (totalpixelalgorithmCB.Checked)
			{
				quality_sort_label2 = " frames by total brigtness";
			}
			else
			{
				quality_sort_label2 = " frames by quality";
			}
			pipp_process_running = true;
			pipp_output_received = false;
			outputTB.Clear();
			runBTN.Enabled = false;
			dotestrunBTN.Enabled = false;
			dark_frame_stackingLAB.Text = "Stacking 0 dark frames";
			set_dark_frame_progress(0);
			flat_dark_stackingLAB.Text = "Stacking 0 flat dark frames";
			set_flat_dark_frame_progress(0);
			flat_stackingLAB.Text = "Stacking 0 flat frames";
			set_flat_frame_progress(0);
			fileLAB.Text = "File: None";
			processfilePB.Value = 0;
			processfileLAB.Text = "File 0 of 0";
			processframesLAB.Text = "Processing 0 frames";
			processPB.Value = 0;
			progressLAB.Text = "0%";
			qsframeLAB.Text = quality_sort_label1 + "0" + quality_sort_label2;
			qualityPB.Value = 0;
			qualityLAB.Text = "0%";
			if (test_run)
			{
				dotestrunBTN.Text = "Processing...";
			}
			else
			{
				runBTN.Text = "Processing...";
			}
			stopwatch = new Stopwatch();
			stopwatch.Start();
			Text = "PIPP (Processing)";
			statusstateLAB.Text = "RUNNING";
			statusstateLAB.Left = (statusGB.Width - statusstateLAB.Width) / 2;
			doProcessToolStripMenuItem.Enabled = false;
			cancelProcessingToolStripMenuItem.Enabled = true;
			killpippBTN.Enabled = true;
			processed_killed = false;
			((Control)tabPage1).Enabled = false;
			((Control)tabPage7).Enabled = false;
			((Control)tabPage2).Enabled = false;
			((Control)tabPage3).Enabled = false;
			((Control)tabPage5).Enabled = false;
			((Control)tabPage4).Enabled = false;
			outputTB.AppendText(ManagedEngineApi.PippVersion + "\r\n\r\n");
			error_data = "";
			Task.Factory.StartNew(() =>
			{
				do_processing_thread();
			});
		}
	}

	private void do_processing_thread()
	{
		arg_list2.Add("-temp_dir=" + temp_directory);
		if (dark_filelist.Items.Count > 0 && dark_frame_subtractionCB.Checked)
		{
			Invoke((MethodInvoker)(() =>
			{
				outputTB.AppendText("Processing Dark Frames \r\n");
			}));
			int required_frames = dark_filelist.total_frame_count;
			if (test_run && dark_frames_stacked <= 10 && required_frames > 10)
			{
				required_frames = 10;
			}
			Invoke((MethodInvoker)(() =>
			{
				dark_frame_stackingLAB.Text = "Stacking " + required_frames + " dark frames";
			}));
			if (dark_frames_stacked < required_frames)
			{
				List<string> list = new List<string>();
				for (int x = 0; x < dark_filelist.Items.Count; x++)
				{
					string file = "";
					Invoke((MethodInvoker)(() =>
					{
						file = dark_filelist.Items[x].Text;
					}));
					string directory = "";
					Invoke((MethodInvoker)(() =>
					{
						directory = dark_filelist.Items[x].SubItems[1].Text;
					}));
					string item = Path.Combine(directory, file);
					list.Add(item);
				}
				list.Add("");
				master_dark_tmp_file = Path.Combine(temp_directory, "pipp_master_dark.tmp");
				master_dark_pgm_file = Path.Combine(temp_directory, "pipp_master_dark.pgm");
				// Managed master-dark stacker: no native stack_frames call.
				int num = MasterFrameBuilder.Build(list, null, master_dark_tmp_file, master_dark_pgm_file, required_frames, set_dark_frame_progress);
				if (num >= 0)
				{
					dark_frames_stacked = required_frames;
				}
			}
			else
			{
				Invoke((MethodInvoker)(() =>
				{
					outputTB.AppendText("  Using cached master dark frame \r\n");
				}));
			}
			set_dark_frame_progress(100);
			arg_list2.Add("-dark_tmp=" + master_dark_tmp_file);
			arg_list2.Add("-dark_pgm=" + master_dark_pgm_file);
		}
		if (flat_dark_filelist.Items.Count > 0 && flat_dark_frame_subtractionCB.Checked && flat_filelist.Items.Count > 0 && flat_frame_calibrationCB.Checked)
		{
			Invoke((MethodInvoker)(() =>
			{
				outputTB.AppendText("Processing Flat Dark Frames \r\n");
			}));
			int required_frames2 = flat_dark_filelist.total_frame_count;
			if (test_run && flat_dark_frames_stacked <= 10 && required_frames2 > 10)
			{
				required_frames2 = 10;
			}
			Invoke((MethodInvoker)(() =>
			{
				flat_dark_frame_stackingLAB.Text = "Stacking " + required_frames2 + " flat dark frames";
			}));
			master_flat_dark_tmp_file = Path.Combine(temp_directory, "pipp_master_flat_dark.tmp");
			master_flat_dark_pgm_file = Path.Combine(temp_directory, "pipp_master_flat_dark.pgm");
			if (flat_dark_frames_stacked < required_frames2)
			{
				List<string> list2 = new List<string>();
				for (int x2 = 0; x2 < flat_dark_filelist.Items.Count; x2++)
				{
					string file2 = "";
					Invoke((MethodInvoker)(() =>
					{
						file2 = flat_dark_filelist.Items[x2].Text;
					}));
					string directory2 = "";
					Invoke((MethodInvoker)(() =>
					{
						directory2 = flat_dark_filelist.Items[x2].SubItems[1].Text;
					}));
					string item2 = Path.Combine(directory2, file2);
					list2.Add(item2);
				}
				list2.Add("");
				int num2 = MasterFrameBuilder.Build(list2, null, master_flat_dark_tmp_file, master_flat_dark_pgm_file, required_frames2, set_flat_dark_frame_progress);
				if (num2 >= 0)
				{
					flat_dark_frames_stacked = required_frames2;
				}
				flat_frames_stacked = 0;
			}
			else
			{
				Invoke((MethodInvoker)(() =>
				{
					outputTB.AppendText("  Using cached master flat dark frame \r\n");
				}));
			}
			set_flat_dark_frame_progress(100);
			arg_list2.Add("-flat_dark_tmp=" + master_flat_dark_tmp_file);
		}
		else
		{
			master_flat_dark_tmp_file = "";
			master_flat_dark_pgm_file = "";
		}
		if (flat_filelist.Items.Count > 0 && flat_frame_calibrationCB.Checked)
		{
			Invoke((MethodInvoker)(() =>
			{
				outputTB.AppendText("Processing Flat Frames \r\n");
			}));
			int required_frames3 = flat_filelist.total_frame_count;
			if (test_run && flat_frames_stacked <= 10 && required_frames3 > 10)
			{
				required_frames3 = 10;
			}
			Invoke((MethodInvoker)(() =>
			{
				flat_frame_stackingLAB.Text = "Stacking " + required_frames3 + " flat frames";
			}));
			if (flat_frames_stacked < required_frames3)
			{
				List<string> list3 = new List<string>();
				for (int x3 = 0; x3 < flat_filelist.Items.Count; x3++)
				{
					string file3 = "";
					Invoke((MethodInvoker)(() =>
					{
						file3 = flat_filelist.Items[x3].Text;
					}));
					string directory3 = "";
					Invoke((MethodInvoker)(() =>
					{
						directory3 = flat_filelist.Items[x3].SubItems[1].Text;
					}));
					string item3 = Path.Combine(directory3, file3);
					list3.Add(item3);
				}
				list3.Add("");
				master_flat_tmp_file = Path.Combine(temp_directory, "pipp_master_flat.tmp");
				string subtract = string.IsNullOrEmpty(master_flat_dark_tmp_file) ? null : master_flat_dark_tmp_file;
				int num3 = MasterFrameBuilder.Build(list3, subtract, master_flat_tmp_file, null, required_frames3, set_flat_frame_progress);
				if (num3 >= 0)
				{
					flat_frames_stacked = required_frames3;
				}
			}
			else
			{
				Invoke((MethodInvoker)(() =>
				{
					outputTB.AppendText("  Using cached master flat frame \r\n");
				}));
			}
			set_flat_frame_progress(100);
			arg_list2.Add("-flat_tmp=" + master_flat_tmp_file);
		}
		// Engine path is centralised in EngineLocator (see the comment there about the managed replacement).
		string text = EngineLocator.FindEngineExecutable();
		pipp_process.StartInfo.FileName = text;
		pipp_process.StartInfo.UseShellExecute = false;
		pipp_process.StartInfo.CreateNoWindow = true;
		pipp_process.StartInfo.RedirectStandardOutput = true;
		pipp_process.StartInfo.RedirectStandardError = true;
		pipp_process.StartInfo.RedirectStandardInput = true;
		pipp_process.EnableRaisingEvents = true;
		pipp_process.Start();
		pipp_process.BeginOutputReadLine();
		pipp_process.BeginErrorReadLine();
		StreamWriter pipp_stream_writer = pipp_process.StandardInput;
		if (!processed_killed)
		{
			Invoke((MethodInvoker)(() =>
			{
				send_arguments_to_pipp_process(ref pipp_stream_writer, ref arg_list2);
			}));
		}
		if (!processed_killed)
		{
			Invoke((MethodInvoker)(() =>
			{
				send_source_files_to_pipp_process(ref pipp_stream_writer);
			}));
		}
		pipp_stream_writer.WriteLine("DONE");
		pipp_stream_writer.Close();
	}

	private void send_arguments_to_pipp_process(ref StreamWriter pipp_stream_writer, ref List<string> arg_list)
	{
		foreach (string item in arg_list)
		{
			if (item.Length > 1 && item[0] == '-')
			{
				pipp_stream_writer.WriteLine(Encoding.Default.GetString(Encoding.UTF8.GetBytes(item)));
			}
		}
	}

	private void send_source_files_to_pipp_process(ref StreamWriter pipp_stream_writer)
	{
		if (test_run && (batchmodeRB.Checked || show_detected_pixels))
		{
			if (image_filelist.SelectedIndices.Count > 0)
			{
				int index = image_filelist.SelectedIndices[0];
				string path = image_filelist.Items[index].Text;
				string path2 = image_filelist.Items[index].SubItems[1].Text;
				string s = Path.Combine(path2, path);
				pipp_stream_writer.WriteLine(Encoding.Default.GetString(Encoding.UTF8.GetBytes(s)));
			}
			else
			{
				string path3 = image_filelist.Items[0].Text;
				string path4 = image_filelist.Items[0].SubItems[1].Text;
				string s2 = Path.Combine(path4, path3);
				pipp_stream_writer.WriteLine(Encoding.Default.GetString(Encoding.UTF8.GetBytes(s2)));
			}
			pipp_stream_writer.Flush();
		}
		else
		{
			for (int i = 0; i < image_filelist.Items.Count; i++)
			{
				string path5 = image_filelist.Items[i].Text;
				string path6 = image_filelist.Items[i].SubItems[1].Text;
				string s3 = Path.Combine(path6, path5);
				pipp_stream_writer.WriteLine(Encoding.Default.GetString(Encoding.UTF8.GetBytes(s3)));
				pipp_stream_writer.Flush();
			}
		}
	}

	private void cancel_processing()
	{
		// There is no in-process native engine to abort: cancellation terminates the engine process.
		processed_killed = true;
		if (!pipp_process.HasExited)
		{
			pipp_process.Kill();
		}
	}

	private bool exit_checks()
	{
		if (pipp_process_running)
		{
			DialogResult dialogResult = MessageBox.Show("Processing In Progress.\rConfirm Quit?", "PIPP Quit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (dialogResult != DialogResult.Yes)
			{
				return false;
			}
			try
			{
				pipp_process.Kill();
			}
			catch
			{
			}
		}
		save_options(Application.LocalUserAppDataPath + "\\quit_options.pof");
		cPersistentData.inputdatetimeformat = inputdatetimeformatCB.SelectedIndex;
		cPersistentData.check_for_updates = check_updatesMenuItem.Checked;
		cPersistentData.use_ram_buffers = use_ram_buffersMenuItem.Checked;
		cPersistentData.open_output_folder = open_folderCB.Checked;
		cPersistentData.save();
		return true;
	}

	private void reset_options()
	{
		reset_source_files();
		reset_source_files_dark_files();
		reset_source_files_flat_files();
		reset_source_files_flat_dark_files();
		reset_input_options();
		reset_processing_options();
		reset_quality_options();
		reset_animation_options();
		reset_output_options();
		reset_menu_strip();
	}

	private int generate_cmdline_options(ref List<string> arg_list)
	{
		arg_list.Add("-gui");
		if (test_run)
		{
			arg_list.Add("-test_run");
		}
		if (show_detected_pixels)
		{
			arg_list.Add("-sdp");
		}
		int num = generate_cmdline_options_source_files_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_source_dark_files_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_source_flat_files_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_source_flat_dark_files_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_input_options_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_processing_options_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		bool flag = generate_cmdline_options_animation_options_tab(ref arg_list);
		if (outputgifRB.Checked)
		{
			flag = true;
		}
		if (flag && !test_run)
		{
			anim_progPAN.Show();
		}
		else
		{
			anim_progPAN.Hide();
		}
		num = generate_cmdline_options_quality_options_tab(ref arg_list, flag);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_output_options_tab(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		num = generate_cmdline_options_menu_strip(ref arg_list);
		if (num < 0)
		{
			return num;
		}
		return 1;
	}

	private void stabilisation_CheckedChanged_event_handler(object sender, EventArgs e)
	{
		stabilisation_CheckedChanged();
		enableSurfaceTrackingCB_CheckedChanged(sender, e);
		if (test_run_dialog != null && !test_run_dialog.IsDisposed)
		{
			test_run_dialog.update_image();
		}
	}

	private void Form1_Load(object sender, EventArgs e)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		if (commandLineArgs != null && commandLineArgs.Length > 1)
		{
			List<string> list = commandLineArgs.ToList();
			list.RemoveAt(0);
			image_filelist.add_source(list.ToArray());
		}
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
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PIPP_Application.Form1));
		this.menuStrip1 = new System.Windows.Forms.MenuStrip();
		this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
		this.addSourceFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.removeAllFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.menuQuit = new System.Windows.Forms.ToolStripMenuItem();
		this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.resetOptionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.undoResetOptionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
		this.saveOptionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.loadOptionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.loadOptionsFromLastSessionToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
		this.saveRejectedFramesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
		this.check_updatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.use_ram_buffersMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.processToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.testOptionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.doProcessToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.cancelProcessingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.openFD = new System.Windows.Forms.OpenFileDialog();
		this.openoptionsFD = new System.Windows.Forms.OpenFileDialog();
		this.dotestrunBTN = new System.Windows.Forms.Button();
		this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
		this.subdir_nameTB = new System.Windows.Forms.TextBox();
		this.subdir_append_date_timeCB = new System.Windows.Forms.CheckBox();
		this.create_output_subdirCB = new System.Windows.Forms.CheckBox();
		this.outputprefixGB = new System.Windows.Forms.GroupBox();
		this.outputprefixTB = new System.Windows.Forms.TextBox();
		this.groupBox8 = new System.Windows.Forms.GroupBox();
		this.outputpngRB = new System.Windows.Forms.RadioButton();
		this.outputgifRB = new System.Windows.Forms.RadioButton();
		this.outputfitsRB = new System.Windows.Forms.RadioButton();
		this.formatPB = new System.Windows.Forms.PictureBox();
		this.outtype_label = new System.Windows.Forms.Label();
		this.outputtifRB = new System.Windows.Forms.RadioButton();
		this.outputbmpRB = new System.Windows.Forms.RadioButton();
		this.outputserRB = new System.Windows.Forms.RadioButton();
		this.outputaviRB = new System.Windows.Forms.RadioButton();
		this.avi_fpsCB = new System.Windows.Forms.CheckBox();
		this.fpsFUDlabel = new System.Windows.Forms.Label();
		this.avi_fpsNUD = new System.Windows.Forms.NumericUpDown();
		this.qualitylimitingGB = new System.Windows.Forms.GroupBox();
		this.quality_limiting_typeCB = new System.Windows.Forms.ComboBox();
		this.qualityframelimitCB = new System.Windows.Forms.CheckBox();
		this.qualityframesNUD = new System.Windows.Forms.NumericUpDown();
		this.groupBox5 = new System.Windows.Forms.GroupBox();
		this.overexposedPB = new System.Windows.Forms.PictureBox();
		this.overexposedPANEL = new System.Windows.Forms.Panel();
		this.overexposedNUD = new System.Windows.Forms.NumericUpDown();
		this.label3 = new System.Windows.Forms.Label();
		this.overexposedCB = new System.Windows.Forms.CheckBox();
		this.checkplanetCB = new System.Windows.Forms.CheckBox();
		this.test_detect_thresholdBUT = new System.Windows.Forms.Button();
		this.centreplanetCB = new System.Windows.Forms.CheckBox();
		this.cropwidthNUD = new System.Windows.Forms.NumericUpDown();
		this.label4 = new System.Windows.Forms.Label();
		this.cropheightNUD = new System.Windows.Forms.NumericUpDown();
		this.label5 = new System.Windows.Forms.Label();
		this.xoffsetNUD = new System.Windows.Forms.NumericUpDown();
		this.yoffsetNUD = new System.Windows.Forms.NumericUpDown();
		this.label6 = new System.Windows.Forms.Label();
		this.label7 = new System.Windows.Forms.Label();
		this.hs_black_pointCB = new System.Windows.Forms.CheckBox();
		this.batchmodeRB = new System.Windows.Forms.RadioButton();
		this.joinmodeRB = new System.Windows.Forms.RadioButton();
		this.limitframesGB = new System.Windows.Forms.GroupBox();
		this.limitframesPANEL2 = new System.Windows.Forms.Panel();
		this.numframesNUD = new System.Windows.Forms.NumericUpDown();
		this.label22 = new System.Windows.Forms.Label();
		this.limitframesPANEL = new System.Windows.Forms.Panel();
		this.startframeNUD = new System.Windows.Forms.NumericUpDown();
		this.label9 = new System.Windows.Forms.Label();
		this.label8 = new System.Windows.Forms.Label();
		this.endframeNUD = new System.Windows.Forms.NumericUpDown();
		this.framerangePB = new System.Windows.Forms.PictureBox();
		this.panel1 = new System.Windows.Forms.Panel();
		this.limitframesCB = new System.Windows.Forms.ComboBox();
		this.framelimitCB = new System.Windows.Forms.CheckBox();
		this.load_button = new System.Windows.Forms.Button();
		this.remove_button = new System.Windows.Forms.Button();
		this.remove_all_button = new System.Windows.Forms.Button();
		this.y800decodestrictCB = new System.Windows.Forms.CheckBox();
		this.rgbsplitGB = new System.Windows.Forms.GroupBox();
		this.splitrgbPB = new System.Windows.Forms.PictureBox();
		this.rgbsplitCB = new System.Windows.Forms.CheckBox();
		this.convertomonoGB = new System.Windows.Forms.GroupBox();
		this.conv_mono_comboBox = new System.Windows.Forms.ComboBox();
		this.pictureBox3 = new System.Windows.Forms.PictureBox();
		this.convmonoCB = new System.Windows.Forms.CheckBox();
		this.debay_typeCB = new System.Windows.Forms.ComboBox();
		this.debayerframesCB = new System.Windows.Forms.CheckBox();
		this.serbitdepthCB = new System.Windows.Forms.ComboBox();
		this.raw_debayer_algorithmCB = new System.Windows.Forms.ComboBox();
		this.raw_image_debayerCB = new System.Windows.Forms.CheckBox();
		this.wjpos_rememberCB = new System.Windows.Forms.CheckBox();
		this.date_time_extract_conv_utCB = new System.Windows.Forms.CheckBox();
		this.codec_qualityNUD = new System.Windows.Forms.NumericUpDown();
		this.median_filterCB = new System.Windows.Forms.CheckBox();
		this.gammaNUD = new System.Windows.Forms.NumericUpDown();
		this.label31 = new System.Windows.Forms.Label();
		this.groupBox7 = new System.Windows.Forms.GroupBox();
		this.outputsuffixTB = new System.Windows.Forms.TextBox();
		this.add_dark_filesBUT = new System.Windows.Forms.Button();
		this.remove_dark_filesBUT = new System.Windows.Forms.Button();
		this.remove_all_dark_filesBUT = new System.Windows.Forms.Button();
		this.add_flat_filesBUT = new System.Windows.Forms.Button();
		this.remove_flat_filesBUT = new System.Windows.Forms.Button();
		this.remove_all_flat_filesBUT = new System.Windows.Forms.Button();
		this.add_flat_dark_filesBUT = new System.Windows.Forms.Button();
		this.remove_flat_dark_filesBUT = new System.Windows.Forms.Button();
		this.remove_all_flat_dark_filesBUT = new System.Windows.Forms.Button();
		this.debay_patternCB = new System.Windows.Forms.ComboBox();
		this.file_down_BUT = new System.Windows.Forms.Button();
		this.file_up_BUT = new System.Windows.Forms.Button();
		this.addleadingzerosCB = new System.Windows.Forms.CheckBox();
		this.dark_data_has_bayerCB = new System.Windows.Forms.CheckBox();
		this.hot_pixel_thresholdNUD = new System.Windows.Forms.NumericUpDown();
		this.save_master_dark_CB = new System.Windows.Forms.CheckBox();
		this.hot_pixel_thresholdLAB = new System.Windows.Forms.Label();
		this.save_master_flat_CB = new System.Windows.Forms.CheckBox();
		this.save_master_flat_dark_CB = new System.Windows.Forms.CheckBox();
		this.label32 = new System.Windows.Forms.Label();
		this.label33 = new System.Windows.Forms.Label();
		this.label35 = new System.Windows.Forms.Label();
		this.label37 = new System.Windows.Forms.Label();
		this.label36 = new System.Windows.Forms.Label();
		this.label38 = new System.Windows.Forms.Label();
		this.aoi_yNUD = new System.Windows.Forms.NumericUpDown();
		this.afb_xNUD = new System.Windows.Forms.NumericUpDown();
		this.aoi_cropCB = new System.Windows.Forms.CheckBox();
		this.aoi_heightNUD = new System.Windows.Forms.NumericUpDown();
		this.aoi_widthNUD = new System.Windows.Forms.NumericUpDown();
		this.aoi_xNUD = new System.Windows.Forms.NumericUpDown();
		this.afb_yNUD = new System.Windows.Forms.NumericUpDown();
		this.stabilise_objectRB = new System.Windows.Forms.RadioButton();
		this.stabilise_noneRB = new System.Windows.Forms.RadioButton();
		this.label39 = new System.Windows.Forms.Label();
		this.label40 = new System.Windows.Forms.Label();
		this.afb_heightNUD = new System.Windows.Forms.NumericUpDown();
		this.afb_widthNUD = new System.Windows.Forms.NumericUpDown();
		this.label34 = new System.Windows.Forms.Label();
		this.gif_fpsNUD = new System.Windows.Forms.NumericUpDown();
		this.gif_fpsCB = new System.Windows.Forms.CheckBox();
		this.gif_repeat_foreverCB = new System.Windows.Forms.CheckBox();
		this.binningCB = new System.Windows.Forms.ComboBox();
		this.binning_calculationCB = new System.Windows.Forms.ComboBox();
		this.resize_widthNUD = new System.Windows.Forms.NumericUpDown();
		this.resize_widthLAB = new System.Windows.Forms.Label();
		this.resize_heightNUD = new System.Windows.Forms.NumericUpDown();
		this.resize_heightLAB = new System.Windows.Forms.Label();
		this.framedropGB = new System.Windows.Forms.GroupBox();
		this.outputframedroppingPB = new System.Windows.Forms.PictureBox();
		this.label14 = new System.Windows.Forms.Label();
		this.outputframedropNUD = new System.Windows.Forms.NumericUpDown();
		this.outputfiledirectoryGB = new System.Windows.Forms.GroupBox();
		this.pictureBox4 = new System.Windows.Forms.PictureBox();
		this.outputdirectoryTB = new System.Windows.Forms.TextBox();
		this.defaultouputdirBUT = new System.Windows.Forms.Button();
		this.output_subdirectoryGB = new System.Windows.Forms.GroupBox();
		this.subdir_nameGB = new System.Windows.Forms.GroupBox();
		this.outputdirectoryBUT = new System.Windows.Forms.Button();
		this.qualityweightGB = new System.Windows.Forms.GroupBox();
		this.qualityweightingCB = new System.Windows.Forms.CheckBox();
		this.qreorderCB = new System.Windows.Forms.CheckBox();
		this.qualitysortingCB = new System.Windows.Forms.CheckBox();
		this.defaultalgorithmCB = new System.Windows.Forms.RadioButton();
		this.ninoxalgorithmCB = new System.Windows.Forms.RadioButton();
		this.histoalgorithmCB = new System.Windows.Forms.RadioButton();
		this.totalpixelalgorithmCB = new System.Windows.Forms.RadioButton();
		this.qualityoptionsGB = new System.Windows.Forms.GroupBox();
		this.label13 = new System.Windows.Forms.Label();
		this.label12 = new System.Windows.Forms.Label();
		this.label10 = new System.Windows.Forms.Label();
		this.downsampleincNUD = new System.Windows.Forms.NumericUpDown();
		this.maxdownsampleNUD = new System.Windows.Forms.NumericUpDown();
		this.mindownsampleNUD = new System.Windows.Forms.NumericUpDown();
		this.autothresholdCB = new System.Windows.Forms.CheckBox();
		this.thresholdNUD = new System.Windows.Forms.NumericUpDown();
		this.label2 = new System.Windows.Forms.Label();
		this.minsizeNUD = new System.Windows.Forms.NumericUpDown();
		this.label1 = new System.Windows.Forms.Label();
		this.histostretchGB = new System.Windows.Forms.GroupBox();
		this.rgbhistoCB = new System.Windows.Forms.CheckBox();
		this.pictureBox2 = new System.Windows.Forms.PictureBox();
		this.label11 = new System.Windows.Forms.Label();
		this.hs_white_pointNUD = new System.Windows.Forms.NumericUpDown();
		this.hs_white_pointCB = new System.Windows.Forms.CheckBox();
		this.groupBox3 = new System.Windows.Forms.GroupBox();
		this.planetary_animation_gifCB = new System.Windows.Forms.CheckBox();
		this.avi_archiveCB = new System.Windows.Forms.CheckBox();
		this.planetary_animation_aviCB = new System.Windows.Forms.CheckBox();
		this.issCB = new System.Windows.Forms.CheckBox();
		this.solar_closeupCB = new System.Windows.Forms.CheckBox();
		this.solar_full_discCB = new System.Windows.Forms.CheckBox();
		this.planetaryCB = new System.Windows.Forms.CheckBox();
		this.protectbayerCB = new System.Windows.Forms.CheckBox();
		this.ipframedroppingGB = new System.Windows.Forms.GroupBox();
		this.framedroppingPB = new System.Windows.Forms.PictureBox();
		this.label16 = new System.Windows.Forms.Label();
		this.ipframedropNUD = new System.Windows.Forms.NumericUpDown();
		this.colourdetectionGB = new System.Windows.Forms.GroupBox();
		this.colour_monoPB = new System.Windows.Forms.PictureBox();
		this.colourdetectionCB = new System.Windows.Forms.ComboBox();
		this.raw_output_colour_spaceCB = new System.Windows.Forms.ComboBox();
		this.raw_highlight_recovery_typeCB = new System.Windows.Forms.ComboBox();
		this.inputdatetimeformatCB = new System.Windows.Forms.ComboBox();
		this.datetimeTB = new System.Windows.Forms.TextBox();
		this.winjuposfilenameCB = new System.Windows.Forms.CheckBox();
		this.label19 = new System.Windows.Forms.Label();
		this.inputframespersecondNUD = new System.Windows.Forms.NumericUpDown();
		this.winjuposGB = new System.Windows.Forms.GroupBox();
		this.winjuposPB = new System.Windows.Forms.PictureBox();
		this.winjupostimeCB = new System.Windows.Forms.ComboBox();
		this.label20 = new System.Windows.Forms.Label();
		this.old_format_aviCB = new System.Windows.Forms.CheckBox();
		this.avi_codecCB = new System.Windows.Forms.ComboBox();
		this.input_gainNUD = new System.Windows.Forms.NumericUpDown();
		this.input_gainLAB = new System.Windows.Forms.Label();
		this.edge_in_shadowCB = new System.Windows.Forms.ComboBox();
		this.old_format_max_sizeCB = new System.Windows.Forms.ComboBox();
		this.avi_safe_frame_widthCB = new System.Windows.Forms.CheckBox();
		this.extract_date_timeCB = new System.Windows.Forms.CheckBox();
		this.includequalityvalueCB = new System.Windows.Forms.CheckBox();
		this.noframerenumberCB = new System.Windows.Forms.CheckBox();
		this.dark_frame_subtractionCB = new System.Windows.Forms.CheckBox();
		this.replace_hot_pixelsCB = new System.Windows.Forms.CheckBox();
		this.flat_frame_calibrationCB = new System.Windows.Forms.CheckBox();
		this.flat_dark_frame_subtractionCB = new System.Windows.Forms.CheckBox();
		this.flat_data_has_bayerCB = new System.Windows.Forms.CheckBox();
		this.enablestabilisationCB = new System.Windows.Forms.CheckBox();
		this.aoi_enableCB = new System.Windows.Forms.CheckBox();
		this.aoi_rejectCB = new System.Windows.Forms.CheckBox();
		this.afb_rejectCB = new System.Windows.Forms.CheckBox();
		this.stabilise_surfaceRB = new System.Windows.Forms.RadioButton();
		this.keepimagefilenamesCB = new System.Windows.Forms.CheckBox();
		this.enableSurfaceTrackingCB = new System.Windows.Forms.CheckBox();
		this.gif_border_tolNUD = new System.Windows.Forms.NumericUpDown();
		this.gif_trans_pixel_tolNUD = new System.Windows.Forms.NumericUpDown();
		this.gif_bit_depthNUD = new System.Windows.Forms.NumericUpDown();
		this.gif_lossy_comp_levelNUD = new System.Windows.Forms.NumericUpDown();
		this.winjupossubPANEL = new System.Windows.Forms.Panel();
		this.groupBox1 = new System.Windows.Forms.GroupBox();
		this.wjpos_extracted2LAB = new System.Windows.Forms.Label();
		this.wjpos_extractedLAB = new System.Windows.Forms.Label();
		this.wjpos_filename2LAB = new System.Windows.Forms.Label();
		this.wjpos_filenameLAB = new System.Windows.Forms.Label();
		this.minpixelvalueGB = new System.Windows.Forms.GroupBox();
		this.groupBox6 = new System.Windows.Forms.GroupBox();
		this.flip_rotateGB = new System.Windows.Forms.GroupBox();
		this.rotatePB = new System.Windows.Forms.PictureBox();
		this.rotationCB = new System.Windows.Forms.ComboBox();
		this.flip_yCB = new System.Windows.Forms.CheckBox();
		this.flip_xCB = new System.Windows.Forms.CheckBox();
		this.label30 = new System.Windows.Forms.Label();
		this.animation_repeat_allLAB = new System.Windows.Forms.Label();
		this.animation_repeat_allNUD = new System.Windows.Forms.NumericUpDown();
		this.repeat_forward_lastLAB = new System.Windows.Forms.Label();
		this.repeat_forward_lastNUD = new System.Windows.Forms.NumericUpDown();
		this.repeat_reverse_lastLAB = new System.Windows.Forms.Label();
		this.repeat_reverse_lastNUD = new System.Windows.Forms.NumericUpDown();
		this.forward_playGB = new System.Windows.Forms.GroupBox();
		this.pictureBox6 = new System.Windows.Forms.PictureBox();
		this.panel3 = new System.Windows.Forms.Panel();
		this.repeat_forward_lastCB = new System.Windows.Forms.CheckBox();
		this.animation_forwardCB = new System.Windows.Forms.CheckBox();
		this.outputdirectoryFBD = new System.Windows.Forms.FolderBrowserDialog();
		this.pipp_process = new System.Diagnostics.Process();
		this.saveoptionsFD = new System.Windows.Forms.SaveFileDialog();
		this.tabPage6 = new System.Windows.Forms.TabPage();
		this.splitContainer6 = new System.Windows.Forms.SplitContainer();
		this.mainprocessingGB = new System.Windows.Forms.GroupBox();
		this.anim_progPAN = new System.Windows.Forms.Panel();
		this.animframeLAB = new System.Windows.Forms.Label();
		this.animPB = new System.Windows.Forms.ProgressBar();
		this.animLAB = new System.Windows.Forms.Label();
		this.qual_progressPAN = new System.Windows.Forms.Panel();
		this.qualityPB = new System.Windows.Forms.ProgressBar();
		this.qualityLAB = new System.Windows.Forms.Label();
		this.qsframeLAB = new System.Windows.Forms.Label();
		this.panel7 = new System.Windows.Forms.Panel();
		this.processframesLAB = new System.Windows.Forms.Label();
		this.processPB = new System.Windows.Forms.ProgressBar();
		this.progressLAB = new System.Windows.Forms.Label();
		this.panel8 = new System.Windows.Forms.Panel();
		this.fileLAB = new System.Windows.Forms.Label();
		this.processfilePB = new System.Windows.Forms.ProgressBar();
		this.processfileLAB = new System.Windows.Forms.Label();
		this.calibrationGB = new System.Windows.Forms.GroupBox();
		this.flat_stacking_progress_PAN = new System.Windows.Forms.Panel();
		this.flat_frame_stackingLAB = new System.Windows.Forms.Label();
		this.flat_stackingPB = new System.Windows.Forms.ProgressBar();
		this.flat_stackingLAB = new System.Windows.Forms.Label();
		this.flat_dark_stacking_progress_PAN = new System.Windows.Forms.Panel();
		this.flat_dark_frame_stackingLAB = new System.Windows.Forms.Label();
		this.flat_dark_stackingPB = new System.Windows.Forms.ProgressBar();
		this.flat_dark_stackingLAB = new System.Windows.Forms.Label();
		this.dark_stacking_progress_PAN = new System.Windows.Forms.Panel();
		this.dark_frame_stackingLAB = new System.Windows.Forms.Label();
		this.dark_stackingPB = new System.Windows.Forms.ProgressBar();
		this.dark_stackingLAB = new System.Windows.Forms.Label();
		this.statusGB = new System.Windows.Forms.GroupBox();
		this.statusstateLAB = new System.Windows.Forms.Label();
		this.controlGB = new System.Windows.Forms.GroupBox();
		this.output_folderBTN = new System.Windows.Forms.Button();
		this.open_folderCB = new System.Windows.Forms.CheckBox();
		this.killpippBTN = new System.Windows.Forms.Button();
		this.runBTN = new System.Windows.Forms.Button();
		this.outputGB = new System.Windows.Forms.GroupBox();
		this.outputTB = new System.Windows.Forms.TextBox();
		this.tabPage4 = new System.Windows.Forms.TabPage();
		this.splitContainer5 = new System.Windows.Forms.SplitContainer();
		this.prefix_PAN = new System.Windows.Forms.Panel();
		this.renumberGB = new System.Windows.Forms.GroupBox();
		this.norenumberPB = new System.Windows.Forms.PictureBox();
		this.animimated_gif_optionsGB = new System.Windows.Forms.GroupBox();
		this.label45 = new System.Windows.Forms.Label();
		this.gif_quantCB = new System.Windows.Forms.ComboBox();
		this.gif_advanced_optionsGB = new System.Windows.Forms.GroupBox();
		this.label44 = new System.Windows.Forms.Label();
		this.gif_adv_optionsCB = new System.Windows.Forms.ComboBox();
		this.gif_lossy_comp_levelCB = new System.Windows.Forms.CheckBox();
		this.gif_bit_depthCB = new System.Windows.Forms.CheckBox();
		this.gif_trans_pixel_tolCB = new System.Windows.Forms.CheckBox();
		this.gif_border_tolCB = new System.Windows.Forms.CheckBox();
		this.pictureBox15 = new System.Windows.Forms.PictureBox();
		this.label41 = new System.Windows.Forms.Label();
		this.avi_outputGB = new System.Windows.Forms.GroupBox();
		this.old_format_aviPAN = new System.Windows.Forms.Panel();
		this.label25 = new System.Windows.Forms.Label();
		this.pictureBox1 = new System.Windows.Forms.PictureBox();
		this.codec_qualityGB = new System.Windows.Forms.Panel();
		this.label27 = new System.Windows.Forms.Label();
		this.label26 = new System.Windows.Forms.Label();
		this.tabPage3 = new System.Windows.Forms.TabPage();
		this.splitContainer4 = new System.Windows.Forms.SplitContainer();
		this.qualityalgorithmGB = new System.Windows.Forms.GroupBox();
		this.qualityenableGB = new System.Windows.Forms.GroupBox();
		this.pictureBox5 = new System.Windows.Forms.PictureBox();
		this.tabPage2 = new System.Windows.Forms.TabPage();
		this.splitContainer2 = new System.Windows.Forms.SplitContainer();
		this.noise_filterGB = new System.Windows.Forms.GroupBox();
		this.pictureBox12 = new System.Windows.Forms.PictureBox();
		this.input_gainGB = new System.Windows.Forms.GroupBox();
		this.pictureBox13 = new System.Windows.Forms.PictureBox();
		this.groupBox2 = new System.Windows.Forms.GroupBox();
		this.pictureBox17 = new System.Windows.Forms.PictureBox();
		this.resize_keep_aspect_ratioCB = new System.Windows.Forms.CheckBox();
		this.resize_framesCB = new System.Windows.Forms.CheckBox();
		this.surface_stabiliseGB = new System.Windows.Forms.GroupBox();
		this.area_of_interestGB = new System.Windows.Forms.GroupBox();
		this.anchor_feature_boxGB = new System.Windows.Forms.GroupBox();
		this.pictureBox14 = new System.Windows.Forms.PictureBox();
		this.croppingGB = new System.Windows.Forms.GroupBox();
		this.enablecropping3PANEL = new System.Windows.Forms.Panel();
		this.enableoffsetPANEL = new System.Windows.Forms.Panel();
		this.enablecroppingPANEL = new System.Windows.Forms.Panel();
		this.cropPB = new System.Windows.Forms.PictureBox();
		this.enablecropCB = new System.Windows.Forms.CheckBox();
		this.planetdetectGB = new System.Windows.Forms.GroupBox();
		this.objectdetectPB = new System.Windows.Forms.PictureBox();
		this.edge_of_shadowPAN = new System.Windows.Forms.Panel();
		this.moon_shadowPB = new System.Windows.Forms.PictureBox();
		this.groupBox10 = new System.Windows.Forms.GroupBox();
		this.tabPage1 = new System.Windows.Forms.TabPage();
		this.image_filesTAB_CONTROL = new System.Windows.Forms.TabControl();
		this.tab_image_files = new System.Windows.Forms.TabPage();
		this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
		this.image_filesGB = new System.Windows.Forms.GroupBox();
		this.image_filelist = new PIPP_Application.c_filelist();
		this.panel2 = new System.Windows.Forms.Panel();
		this.runtypeGB = new System.Windows.Forms.GroupBox();
		this.tab_dark_files = new System.Windows.Forms.TabPage();
		this.tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
		this.dark_filesGB = new System.Windows.Forms.GroupBox();
		this.dark_filelist = new PIPP_Application.c_filelist();
		this.panel6 = new System.Windows.Forms.Panel();
		this.hot_pixels_optionsGB = new System.Windows.Forms.GroupBox();
		this.dark_frame_optionsGB = new System.Windows.Forms.GroupBox();
		this.tab_flat_files = new System.Windows.Forms.TabPage();
		this.tableLayoutPanel4 = new System.Windows.Forms.TableLayoutPanel();
		this.flat_filesGB = new System.Windows.Forms.GroupBox();
		this.flat_filelist = new PIPP_Application.c_filelist();
		this.panel9 = new System.Windows.Forms.Panel();
		this.flat_frame_optionsGB = new System.Windows.Forms.GroupBox();
		this.tab_flat_dark_files = new System.Windows.Forms.TabPage();
		this.tableLayoutPanel5 = new System.Windows.Forms.TableLayoutPanel();
		this.flat_dark_filesGB = new System.Windows.Forms.GroupBox();
		this.flat_dark_filelist = new PIPP_Application.c_filelist();
		this.panel10 = new System.Windows.Forms.Panel();
		this.flat_dark_frame_optionsGB = new System.Windows.Forms.GroupBox();
		this.tabControl1 = new System.Windows.Forms.TabControl();
		this.tabPage7 = new System.Windows.Forms.TabPage();
		this.splitContainer7 = new System.Windows.Forms.SplitContainer();
		this.date_timeGB = new System.Windows.Forms.GroupBox();
		this.pictureBox11 = new System.Windows.Forms.PictureBox();
		this.raw_file_optionsGB = new System.Windows.Forms.GroupBox();
		this.label24 = new System.Windows.Forms.Label();
		this.pictureBox9 = new System.Windows.Forms.PictureBox();
		this.raw_colour_optionsPANEL = new System.Windows.Forms.Panel();
		this.label23 = new System.Windows.Forms.Label();
		this.label21 = new System.Windows.Forms.Label();
		this.raw_hpfCB = new System.Windows.Forms.CheckBox();
		this.binningGB = new System.Windows.Forms.GroupBox();
		this.label43 = new System.Windows.Forms.Label();
		this.pictureBox16 = new System.Windows.Forms.PictureBox();
		this.label42 = new System.Windows.Forms.Label();
		this.debayerGB = new System.Windows.Forms.GroupBox();
		this.db_patternLAB = new System.Windows.Forms.Label();
		this.db_algorithmLAB = new System.Windows.Forms.Label();
		this.debayerpatternPB = new System.Windows.Forms.PictureBox();
		this.debayerenableGB = new System.Windows.Forms.GroupBox();
		this.bayerPB = new System.Windows.Forms.PictureBox();
		this.serfilesGB = new System.Windows.Forms.GroupBox();
		this.serPB = new System.Windows.Forms.PictureBox();
		this.label15 = new System.Windows.Forms.Label();
		this.avifilesGB = new System.Windows.Forms.GroupBox();
		this.pictureBox8 = new System.Windows.Forms.PictureBox();
		this.tabPage5 = new System.Windows.Forms.TabPage();
		this.splitContainer1 = new System.Windows.Forms.SplitContainer();
		this.sequence_repeatGB = new System.Windows.Forms.GroupBox();
		this.pictureBox10 = new System.Windows.Forms.PictureBox();
		this.panel5 = new System.Windows.Forms.Panel();
		this.animation_reverse_not_in_last_repeatCB = new System.Windows.Forms.CheckBox();
		this.animation_repeat_allCB = new System.Windows.Forms.CheckBox();
		this.reverse_playGB = new System.Windows.Forms.GroupBox();
		this.repeat_reverse_lastPAN = new System.Windows.Forms.Panel();
		this.repeat_reverse_lastCB = new System.Windows.Forms.CheckBox();
		this.pictureBox7 = new System.Windows.Forms.PictureBox();
		this.animation_reverseCB = new System.Windows.Forms.CheckBox();
		this.groupBox9 = new System.Windows.Forms.GroupBox();
		this.avi_settings1LAB = new System.Windows.Forms.Label();
		this.avi_settings4LAB = new System.Windows.Forms.Label();
		this.avi_settings3LAB = new System.Windows.Forms.Label();
		this.avi_settings2LAB = new System.Windows.Forms.Label();
		this.label29 = new System.Windows.Forms.Label();
		this.label28 = new System.Windows.Forms.Label();
		this.label18 = new System.Windows.Forms.Label();
		this.label17 = new System.Windows.Forms.Label();
		this.menuStrip1.SuspendLayout();
		this.outputprefixGB.SuspendLayout();
		this.groupBox8.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.formatPB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.avi_fpsNUD).BeginInit();
		this.qualitylimitingGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.qualityframesNUD).BeginInit();
		this.groupBox5.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.overexposedPB).BeginInit();
		this.overexposedPANEL.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.overexposedNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.cropwidthNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.cropheightNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.xoffsetNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.yoffsetNUD).BeginInit();
		this.limitframesGB.SuspendLayout();
		this.limitframesPANEL2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numframesNUD).BeginInit();
		this.limitframesPANEL.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.startframeNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.endframeNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.framerangePB).BeginInit();
		this.panel1.SuspendLayout();
		this.rgbsplitGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitrgbPB).BeginInit();
		this.convertomonoGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox3).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.codec_qualityNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.gammaNUD).BeginInit();
		this.groupBox7.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.hot_pixel_thresholdNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_yNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.afb_xNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_heightNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_widthNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_xNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.afb_yNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.afb_heightNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.afb_widthNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.gif_fpsNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.resize_widthNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.resize_heightNUD).BeginInit();
		this.framedropGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.outputframedroppingPB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.outputframedropNUD).BeginInit();
		this.outputfiledirectoryGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox4).BeginInit();
		this.output_subdirectoryGB.SuspendLayout();
		this.subdir_nameGB.SuspendLayout();
		this.qualityweightGB.SuspendLayout();
		this.qualityoptionsGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.downsampleincNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.maxdownsampleNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.mindownsampleNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.thresholdNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.minsizeNUD).BeginInit();
		this.histostretchGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox2).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.hs_white_pointNUD).BeginInit();
		this.groupBox3.SuspendLayout();
		this.ipframedroppingGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.framedroppingPB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.ipframedropNUD).BeginInit();
		this.colourdetectionGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.colour_monoPB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.inputframespersecondNUD).BeginInit();
		this.winjuposGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.winjuposPB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.input_gainNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.gif_border_tolNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.gif_trans_pixel_tolNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.gif_bit_depthNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.gif_lossy_comp_levelNUD).BeginInit();
		this.winjupossubPANEL.SuspendLayout();
		this.groupBox1.SuspendLayout();
		this.minpixelvalueGB.SuspendLayout();
		this.groupBox6.SuspendLayout();
		this.flip_rotateGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.rotatePB).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.animation_repeat_allNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.repeat_forward_lastNUD).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.repeat_reverse_lastNUD).BeginInit();
		this.forward_playGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox6).BeginInit();
		this.panel3.SuspendLayout();
		this.tabPage6.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitContainer6).BeginInit();
		this.splitContainer6.Panel1.SuspendLayout();
		this.splitContainer6.Panel2.SuspendLayout();
		this.splitContainer6.SuspendLayout();
		this.mainprocessingGB.SuspendLayout();
		this.anim_progPAN.SuspendLayout();
		this.qual_progressPAN.SuspendLayout();
		this.panel7.SuspendLayout();
		this.panel8.SuspendLayout();
		this.calibrationGB.SuspendLayout();
		this.flat_stacking_progress_PAN.SuspendLayout();
		this.flat_dark_stacking_progress_PAN.SuspendLayout();
		this.dark_stacking_progress_PAN.SuspendLayout();
		this.statusGB.SuspendLayout();
		this.controlGB.SuspendLayout();
		this.outputGB.SuspendLayout();
		this.tabPage4.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitContainer5).BeginInit();
		this.splitContainer5.Panel1.SuspendLayout();
		this.splitContainer5.Panel2.SuspendLayout();
		this.splitContainer5.SuspendLayout();
		this.prefix_PAN.SuspendLayout();
		this.renumberGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.norenumberPB).BeginInit();
		this.animimated_gif_optionsGB.SuspendLayout();
		this.gif_advanced_optionsGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox15).BeginInit();
		this.avi_outputGB.SuspendLayout();
		this.old_format_aviPAN.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox1).BeginInit();
		this.codec_qualityGB.SuspendLayout();
		this.tabPage3.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitContainer4).BeginInit();
		this.splitContainer4.Panel1.SuspendLayout();
		this.splitContainer4.Panel2.SuspendLayout();
		this.splitContainer4.SuspendLayout();
		this.qualityalgorithmGB.SuspendLayout();
		this.qualityenableGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox5).BeginInit();
		this.tabPage2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitContainer2).BeginInit();
		this.splitContainer2.Panel1.SuspendLayout();
		this.splitContainer2.Panel2.SuspendLayout();
		this.splitContainer2.SuspendLayout();
		this.noise_filterGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox12).BeginInit();
		this.input_gainGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox13).BeginInit();
		this.groupBox2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox17).BeginInit();
		this.surface_stabiliseGB.SuspendLayout();
		this.area_of_interestGB.SuspendLayout();
		this.anchor_feature_boxGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox14).BeginInit();
		this.croppingGB.SuspendLayout();
		this.enablecropping3PANEL.SuspendLayout();
		this.enableoffsetPANEL.SuspendLayout();
		this.enablecroppingPANEL.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.cropPB).BeginInit();
		this.planetdetectGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.objectdetectPB).BeginInit();
		this.edge_of_shadowPAN.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.moon_shadowPB).BeginInit();
		this.groupBox10.SuspendLayout();
		this.tabPage1.SuspendLayout();
		this.image_filesTAB_CONTROL.SuspendLayout();
		this.tab_image_files.SuspendLayout();
		this.tableLayoutPanel1.SuspendLayout();
		this.image_filesGB.SuspendLayout();
		this.panel2.SuspendLayout();
		this.runtypeGB.SuspendLayout();
		this.tab_dark_files.SuspendLayout();
		this.tableLayoutPanel3.SuspendLayout();
		this.dark_filesGB.SuspendLayout();
		this.panel6.SuspendLayout();
		this.hot_pixels_optionsGB.SuspendLayout();
		this.dark_frame_optionsGB.SuspendLayout();
		this.tab_flat_files.SuspendLayout();
		this.tableLayoutPanel4.SuspendLayout();
		this.flat_filesGB.SuspendLayout();
		this.panel9.SuspendLayout();
		this.flat_frame_optionsGB.SuspendLayout();
		this.tab_flat_dark_files.SuspendLayout();
		this.tableLayoutPanel5.SuspendLayout();
		this.flat_dark_filesGB.SuspendLayout();
		this.panel10.SuspendLayout();
		this.flat_dark_frame_optionsGB.SuspendLayout();
		this.tabControl1.SuspendLayout();
		this.tabPage7.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitContainer7).BeginInit();
		this.splitContainer7.Panel1.SuspendLayout();
		this.splitContainer7.Panel2.SuspendLayout();
		this.splitContainer7.SuspendLayout();
		this.date_timeGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox11).BeginInit();
		this.raw_file_optionsGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox9).BeginInit();
		this.raw_colour_optionsPANEL.SuspendLayout();
		this.binningGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox16).BeginInit();
		this.debayerGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.debayerpatternPB).BeginInit();
		this.debayerenableGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.bayerPB).BeginInit();
		this.serfilesGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.serPB).BeginInit();
		this.avifilesGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox8).BeginInit();
		this.tabPage5.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.splitContainer1).BeginInit();
		this.splitContainer1.Panel1.SuspendLayout();
		this.splitContainer1.Panel2.SuspendLayout();
		this.splitContainer1.SuspendLayout();
		this.sequence_repeatGB.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox10).BeginInit();
		this.panel5.SuspendLayout();
		this.reverse_playGB.SuspendLayout();
		this.repeat_reverse_lastPAN.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox7).BeginInit();
		this.groupBox9.SuspendLayout();
		base.SuspendLayout();
		this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[4] { this.fileToolStripMenuItem, this.optionsToolStripMenuItem, this.processToolStripMenuItem, this.helpToolStripMenuItem });
		this.menuStrip1.Location = new System.Drawing.Point(0, 0);
		this.menuStrip1.Name = "menuStrip1";
		this.menuStrip1.Padding = new System.Windows.Forms.Padding(6, 1, 0, 1);
		this.menuStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
		this.menuStrip1.Size = new System.Drawing.Size(862, 24);
		this.menuStrip1.TabIndex = 0;
		this.menuStrip1.Text = "menuStrip1";
		this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[4] { this.toolStripMenuItem1, this.addSourceFilesToolStripMenuItem, this.removeAllFilesToolStripMenuItem, this.menuQuit });
		this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
		this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 22);
		this.fileToolStripMenuItem.Text = "&File";
		this.fileToolStripMenuItem.ToolTipText = "Something to do with files!";
		this.toolStripMenuItem1.Name = "toolStripMenuItem1";
		this.toolStripMenuItem1.Size = new System.Drawing.Size(196, 6);
		this.addSourceFilesToolStripMenuItem.Name = "addSourceFilesToolStripMenuItem";
		this.addSourceFilesToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.A | System.Windows.Forms.Keys.Alt;
		this.addSourceFilesToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
		this.addSourceFilesToolStripMenuItem.Text = "&Add Source Files";
		this.addSourceFilesToolStripMenuItem.Click += new System.EventHandler(this.addSourceFilesToolStripMenuItem_Click);
		this.removeAllFilesToolStripMenuItem.Name = "removeAllFilesToolStripMenuItem";
		this.removeAllFilesToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.R | System.Windows.Forms.Keys.Alt;
		this.removeAllFilesToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
		this.removeAllFilesToolStripMenuItem.Text = "&Remove All Files";
		this.removeAllFilesToolStripMenuItem.Click += new System.EventHandler(this.removeAllFilesToolStripMenuItem_Click);
		this.menuQuit.Name = "menuQuit";
		this.menuQuit.ShortcutKeys = System.Windows.Forms.Keys.Q | System.Windows.Forms.Keys.Alt;
		this.menuQuit.Size = new System.Drawing.Size(199, 22);
		this.menuQuit.Text = "&Quit";
		this.menuQuit.Click += new System.EventHandler(this.menuQuit_Click);
		this.optionsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[11]
		{
			this.resetOptionsToolStripMenuItem, this.undoResetOptionsToolStripMenuItem, this.toolStripSeparator2, this.saveOptionsToolStripMenuItem, this.loadOptionsToolStripMenuItem, this.loadOptionsFromLastSessionToolStripMenuItem, this.toolStripSeparator1, this.saveRejectedFramesToolStripMenuItem, this.toolStripSeparator3, this.check_updatesMenuItem,
			this.use_ram_buffersMenuItem
		});
		this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
		this.optionsToolStripMenuItem.Size = new System.Drawing.Size(61, 22);
		this.optionsToolStripMenuItem.Text = "Options";
		this.resetOptionsToolStripMenuItem.Name = "resetOptionsToolStripMenuItem";
		this.resetOptionsToolStripMenuItem.Size = new System.Drawing.Size(264, 22);
		this.resetOptionsToolStripMenuItem.Text = "Reset Options";
		this.resetOptionsToolStripMenuItem.Click += new System.EventHandler(this.resetOptionsToolStripMenuItem_Click);
		this.undoResetOptionsToolStripMenuItem.Enabled = false;
		this.undoResetOptionsToolStripMenuItem.Name = "undoResetOptionsToolStripMenuItem";
		this.undoResetOptionsToolStripMenuItem.Size = new System.Drawing.Size(264, 22);
		this.undoResetOptionsToolStripMenuItem.Text = "Undo Reset Options";
		this.undoResetOptionsToolStripMenuItem.Click += new System.EventHandler(this.undoResetOptionsToolStripMenuItem_Click);
		this.toolStripSeparator2.Name = "toolStripSeparator2";
		this.toolStripSeparator2.Size = new System.Drawing.Size(261, 6);
		this.saveOptionsToolStripMenuItem.Name = "saveOptionsToolStripMenuItem";
		this.saveOptionsToolStripMenuItem.Size = new System.Drawing.Size(264, 22);
		this.saveOptionsToolStripMenuItem.Text = "Save Options";
		this.saveOptionsToolStripMenuItem.Click += new System.EventHandler(this.saveOptionsToolStripMenuItem_Click);
		this.loadOptionsToolStripMenuItem.Name = "loadOptionsToolStripMenuItem";
		this.loadOptionsToolStripMenuItem.Size = new System.Drawing.Size(264, 22);
		this.loadOptionsToolStripMenuItem.Text = "Load Options";
		this.loadOptionsToolStripMenuItem.Click += new System.EventHandler(this.loadOptionsToolStripMenuItem_Click);
		this.loadOptionsFromLastSessionToolStripMenuItem.Name = "loadOptionsFromLastSessionToolStripMenuItem";
		this.loadOptionsFromLastSessionToolStripMenuItem.Size = new System.Drawing.Size(264, 22);
		this.loadOptionsFromLastSessionToolStripMenuItem.Text = "Load Options From Last Session";
		this.loadOptionsFromLastSessionToolStripMenuItem.Click += new System.EventHandler(this.loadOptionsFromLastSessionToolStripMenuItem_Click);
		this.toolStripSeparator1.Name = "toolStripSeparator1";
		this.toolStripSeparator1.Size = new System.Drawing.Size(261, 6);
		this.saveRejectedFramesToolStripMenuItem.CheckOnClick = true;
		this.saveRejectedFramesToolStripMenuItem.Name = "saveRejectedFramesToolStripMenuItem";
		this.saveRejectedFramesToolStripMenuItem.Size = new System.Drawing.Size(264, 22);
		this.saveRejectedFramesToolStripMenuItem.Text = "Save Rejected Frames";
		this.saveRejectedFramesToolStripMenuItem.ToolTipText = "By default and rejected frames are discarded.  This\r\noption causes all rejected frames to be saved as\r\nindividual bitmap images so that they can be\r\ninspected.";
		this.toolStripSeparator3.Name = "toolStripSeparator3";
		this.toolStripSeparator3.Size = new System.Drawing.Size(261, 6);
		this.check_updatesMenuItem.Checked = true;
		this.check_updatesMenuItem.CheckOnClick = true;
		this.check_updatesMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
		this.check_updatesMenuItem.Name = "check_updatesMenuItem";
		this.check_updatesMenuItem.Size = new System.Drawing.Size(264, 22);
		this.check_updatesMenuItem.Text = "Check For Updates On Startup";
		this.use_ram_buffersMenuItem.Checked = true;
		this.use_ram_buffersMenuItem.CheckOnClick = true;
		this.use_ram_buffersMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
		this.use_ram_buffersMenuItem.Name = "use_ram_buffersMenuItem";
		this.use_ram_buffersMenuItem.Size = new System.Drawing.Size(264, 22);
		this.use_ram_buffersMenuItem.Text = "Use RAM Buffers For Quality Sorting";
		this.processToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[3] { this.testOptionsToolStripMenuItem, this.doProcessToolStripMenuItem, this.cancelProcessingToolStripMenuItem });
		this.processToolStripMenuItem.Name = "processToolStripMenuItem";
		this.processToolStripMenuItem.Size = new System.Drawing.Size(76, 22);
		this.processToolStripMenuItem.Text = "Processing";
		this.testOptionsToolStripMenuItem.Name = "testOptionsToolStripMenuItem";
		this.testOptionsToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.T | System.Windows.Forms.Keys.Alt;
		this.testOptionsToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
		this.testOptionsToolStripMenuItem.Text = "&Test Options";
		this.testOptionsToolStripMenuItem.Click += new System.EventHandler(this.testOptionsToolStripMenuItem_Click);
		this.doProcessToolStripMenuItem.Name = "doProcessToolStripMenuItem";
		this.doProcessToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.P | System.Windows.Forms.Keys.Alt;
		this.doProcessToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
		this.doProcessToolStripMenuItem.Text = "Start &Processing";
		this.doProcessToolStripMenuItem.Click += new System.EventHandler(this.doProcessToolStripMenuItem_Click);
		this.cancelProcessingToolStripMenuItem.Enabled = false;
		this.cancelProcessingToolStripMenuItem.Name = "cancelProcessingToolStripMenuItem";
		this.cancelProcessingToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
		this.cancelProcessingToolStripMenuItem.Text = "Cancel Processing";
		this.cancelProcessingToolStripMenuItem.Click += new System.EventHandler(this.cancelProcessingToolStripMenuItem_Click);
		this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[1] { this.aboutToolStripMenuItem });
		this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
		this.helpToolStripMenuItem.Size = new System.Drawing.Size(44, 22);
		this.helpToolStripMenuItem.Text = "Help";
		this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
		this.aboutToolStripMenuItem.Size = new System.Drawing.Size(107, 22);
		this.aboutToolStripMenuItem.Text = "About";
		this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
		this.openFD.FileName = "openFileDialog1";
		this.openFD.Multiselect = true;
		this.openoptionsFD.FileName = "openFileDialog1";
		this.dotestrunBTN.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.dotestrunBTN.BackColor = System.Drawing.Color.Transparent;
		this.dotestrunBTN.Location = new System.Drawing.Point(770, 1);
		this.dotestrunBTN.Name = "dotestrunBTN";
		this.dotestrunBTN.Size = new System.Drawing.Size(86, 21);
		this.dotestrunBTN.TabIndex = 2;
		this.dotestrunBTN.Text = "Test Options";
		this.dotestrunBTN.UseVisualStyleBackColor = false;
		this.dotestrunBTN.Click += new System.EventHandler(this.dotestrunBTN_Click);
		this.toolTip1.AutoPopDelay = 15000;
		this.toolTip1.InitialDelay = 500;
		this.toolTip1.ReshowDelay = 100;
		this.toolTip1.ShowAlways = true;
		this.subdir_nameTB.Location = new System.Drawing.Point(6, 16);
		this.subdir_nameTB.Name = "subdir_nameTB";
		this.subdir_nameTB.Size = new System.Drawing.Size(185, 20);
		this.subdir_nameTB.TabIndex = 16;
		this.subdir_nameTB.Text = "pipp_";
		this.toolTip1.SetToolTip(this.subdir_nameTB, "Set the name for the generated subdirectory");
		this.subdir_append_date_timeCB.AutoSize = true;
		this.subdir_append_date_timeCB.Checked = true;
		this.subdir_append_date_timeCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.subdir_append_date_timeCB.Location = new System.Drawing.Point(206, 18);
		this.subdir_append_date_timeCB.Name = "subdir_append_date_timeCB";
		this.subdir_append_date_timeCB.Size = new System.Drawing.Size(130, 17);
		this.subdir_append_date_timeCB.TabIndex = 18;
		this.subdir_append_date_timeCB.Text = "Append date and time";
		this.toolTip1.SetToolTip(this.subdir_append_date_timeCB, "Append current date and time to end of subdirectory name.\r\nEnabling this guarantees that a new  subdirectory will be\r\ngenerated for each processing run.");
		this.subdir_append_date_timeCB.UseVisualStyleBackColor = true;
		this.create_output_subdirCB.AutoSize = true;
		this.create_output_subdirCB.Checked = true;
		this.create_output_subdirCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.create_output_subdirCB.Location = new System.Drawing.Point(5, 16);
		this.create_output_subdirCB.Name = "create_output_subdirCB";
		this.create_output_subdirCB.Size = new System.Drawing.Size(119, 17);
		this.create_output_subdirCB.TabIndex = 15;
		this.create_output_subdirCB.Text = "Create Subdirectory";
		this.toolTip1.SetToolTip(this.create_output_subdirCB, "Generate a subdirectory in the output directory for\r\nthe generated output files.");
		this.create_output_subdirCB.UseVisualStyleBackColor = true;
		this.create_output_subdirCB.CheckedChanged += new System.EventHandler(this.create_output_subdirCB_CheckedChanged);
		this.outputprefixGB.BackColor = System.Drawing.Color.White;
		this.outputprefixGB.Controls.Add(this.outputprefixTB);
		this.outputprefixGB.Dock = System.Windows.Forms.DockStyle.Left;
		this.outputprefixGB.ImeMode = System.Windows.Forms.ImeMode.NoControl;
		this.outputprefixGB.Location = new System.Drawing.Point(0, 0);
		this.outputprefixGB.Name = "outputprefixGB";
		this.outputprefixGB.Size = new System.Drawing.Size(203, 50);
		this.outputprefixGB.TabIndex = 6;
		this.outputprefixGB.TabStop = false;
		this.outputprefixGB.Text = "Output Filename Prefix";
		this.toolTip1.SetToolTip(this.outputprefixGB, "Set a prefix for the output filenames.");
		this.outputprefixTB.Dock = System.Windows.Forms.DockStyle.Top;
		this.outputprefixTB.Location = new System.Drawing.Point(3, 16);
		this.outputprefixTB.Name = "outputprefixTB";
		this.outputprefixTB.Size = new System.Drawing.Size(197, 20);
		this.outputprefixTB.TabIndex = 0;
		this.toolTip1.SetToolTip(this.outputprefixTB, "Set a prefix for the output filenames.");
		this.groupBox8.BackColor = System.Drawing.Color.White;
		this.groupBox8.Controls.Add(this.outputpngRB);
		this.groupBox8.Controls.Add(this.outputgifRB);
		this.groupBox8.Controls.Add(this.outputfitsRB);
		this.groupBox8.Controls.Add(this.formatPB);
		this.groupBox8.Controls.Add(this.outtype_label);
		this.groupBox8.Controls.Add(this.outputtifRB);
		this.groupBox8.Controls.Add(this.outputbmpRB);
		this.groupBox8.Controls.Add(this.outputserRB);
		this.groupBox8.Controls.Add(this.outputaviRB);
		this.groupBox8.Dock = System.Windows.Forms.DockStyle.Top;
		this.groupBox8.Location = new System.Drawing.Point(0, 0);
		this.groupBox8.Name = "groupBox8";
		this.groupBox8.Size = new System.Drawing.Size(420, 63);
		this.groupBox8.TabIndex = 0;
		this.groupBox8.TabStop = false;
		this.groupBox8.Text = "Output Format";
		this.toolTip1.SetToolTip(this.groupBox8, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputpngRB.AutoSize = true;
		this.outputpngRB.Location = new System.Drawing.Point(110, 40);
		this.outputpngRB.Name = "outputpngRB";
		this.outputpngRB.Size = new System.Drawing.Size(48, 17);
		this.outputpngRB.TabIndex = 17;
		this.outputpngRB.Text = "PNG";
		this.toolTip1.SetToolTip(this.outputpngRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF, PNG or FITS image files.");
		this.outputpngRB.UseVisualStyleBackColor = true;
		this.outputpngRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.outputgifRB.AutoSize = true;
		this.outputgifRB.Location = new System.Drawing.Point(164, 19);
		this.outputgifRB.Name = "outputgifRB";
		this.outputgifRB.Size = new System.Drawing.Size(89, 17);
		this.outputgifRB.TabIndex = 16;
		this.outputgifRB.Text = "Animated GIF";
		this.toolTip1.SetToolTip(this.outputgifRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputgifRB.UseVisualStyleBackColor = true;
		this.outputgifRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.outputfitsRB.AutoSize = true;
		this.outputfitsRB.Location = new System.Drawing.Point(57, 41);
		this.outputfitsRB.Name = "outputfitsRB";
		this.outputfitsRB.Size = new System.Drawing.Size(48, 17);
		this.outputfitsRB.TabIndex = 15;
		this.outputfitsRB.Text = "FITS";
		this.toolTip1.SetToolTip(this.outputfitsRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputfitsRB.UseVisualStyleBackColor = true;
		this.outputfitsRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.formatPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.formatPB.Image = PIPP_Application.Properties.Resources.file;
		this.formatPB.Location = new System.Drawing.Point(395, 11);
		this.formatPB.Name = "formatPB";
		this.formatPB.Size = new System.Drawing.Size(20, 20);
		this.formatPB.TabIndex = 14;
		this.formatPB.TabStop = false;
		this.outtype_label.AutoSize = true;
		this.outtype_label.Location = new System.Drawing.Point(228, 41);
		this.outtype_label.Name = "outtype_label";
		this.outtype_label.RightToLeft = System.Windows.Forms.RightToLeft.No;
		this.outtype_label.Size = new System.Drawing.Size(161, 13);
		this.outtype_label.TabIndex = 5;
		this.outtype_label.Text = "(8-bit only - Colour/Monochrome)";
		this.outputtifRB.AutoSize = true;
		this.outputtifRB.Location = new System.Drawing.Point(6, 40);
		this.outputtifRB.Name = "outputtifRB";
		this.outputtifRB.Size = new System.Drawing.Size(47, 17);
		this.outputtifRB.TabIndex = 4;
		this.outputtifRB.Text = "TIFF";
		this.toolTip1.SetToolTip(this.outputtifRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputtifRB.UseVisualStyleBackColor = true;
		this.outputtifRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.outputbmpRB.AutoSize = true;
		this.outputbmpRB.Location = new System.Drawing.Point(110, 19);
		this.outputbmpRB.Name = "outputbmpRB";
		this.outputbmpRB.Size = new System.Drawing.Size(48, 17);
		this.outputbmpRB.TabIndex = 3;
		this.outputbmpRB.Text = "BMP";
		this.toolTip1.SetToolTip(this.outputbmpRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputbmpRB.UseVisualStyleBackColor = true;
		this.outputbmpRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.outputserRB.AutoSize = true;
		this.outputserRB.Location = new System.Drawing.Point(57, 19);
		this.outputserRB.Name = "outputserRB";
		this.outputserRB.Size = new System.Drawing.Size(47, 17);
		this.outputserRB.TabIndex = 2;
		this.outputserRB.Text = "SER";
		this.toolTip1.SetToolTip(this.outputserRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputserRB.UseVisualStyleBackColor = true;
		this.outputserRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.outputaviRB.AutoSize = true;
		this.outputaviRB.Checked = true;
		this.outputaviRB.Location = new System.Drawing.Point(6, 19);
		this.outputaviRB.Name = "outputaviRB";
		this.outputaviRB.Size = new System.Drawing.Size(42, 17);
		this.outputaviRB.TabIndex = 1;
		this.outputaviRB.TabStop = true;
		this.outputaviRB.Text = "AVI";
		this.toolTip1.SetToolTip(this.outputaviRB, "Select whether the output frames are written as an AVI,\r\nSER or Animated GIF video file, or a directory of BMP,\r\nTIFF or FITS image files.");
		this.outputaviRB.UseVisualStyleBackColor = true;
		this.outputaviRB.CheckedChanged += new System.EventHandler(this.output_formatRB_CheckedChanged);
		this.avi_fpsCB.AutoSize = true;
		this.avi_fpsCB.Checked = true;
		this.avi_fpsCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.avi_fpsCB.Location = new System.Drawing.Point(9, 118);
		this.avi_fpsCB.Name = "avi_fpsCB";
		this.avi_fpsCB.Size = new System.Drawing.Size(248, 17);
		this.avi_fpsCB.TabIndex = 5;
		this.avi_fpsCB.Text = "Use Same Framerate As Input Video If Possible";
		this.toolTip1.SetToolTip(this.avi_fpsCB, "Control the framerate of the output video.");
		this.avi_fpsCB.UseVisualStyleBackColor = true;
		this.fpsFUDlabel.AutoSize = true;
		this.fpsFUDlabel.Location = new System.Drawing.Point(90, 142);
		this.fpsFUDlabel.Name = "fpsFUDlabel";
		this.fpsFUDlabel.Size = new System.Drawing.Size(142, 13);
		this.fpsFUDlabel.TabIndex = 4;
		this.fpsFUDlabel.Text = "Framerate To Use Otherwise";
		this.toolTip1.SetToolTip(this.fpsFUDlabel, "Control the framerate of the output video.");
		this.avi_fpsNUD.DecimalPlaces = 2;
		this.avi_fpsNUD.Increment = new decimal(new int[4] { 1, 0, 0, 131072 });
		this.avi_fpsNUD.Location = new System.Drawing.Point(19, 140);
		this.avi_fpsNUD.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.avi_fpsNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 131072 });
		this.avi_fpsNUD.Name = "avi_fpsNUD";
		this.avi_fpsNUD.Size = new System.Drawing.Size(65, 20);
		this.avi_fpsNUD.TabIndex = 3;
		this.toolTip1.SetToolTip(this.avi_fpsNUD, "Control the framerate of the output video.");
		this.avi_fpsNUD.Value = new decimal(new int[4] { 60, 0, 0, 0 });
		this.avi_fpsNUD.ValueChanged += new System.EventHandler(this.fpsNUD_ValueChanged);
		this.qualitylimitingGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.qualitylimitingGB.Controls.Add(this.quality_limiting_typeCB);
		this.qualitylimitingGB.Controls.Add(this.qualityframelimitCB);
		this.qualitylimitingGB.Controls.Add(this.qualityframesNUD);
		this.qualitylimitingGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.qualitylimitingGB.Enabled = false;
		this.qualitylimitingGB.Location = new System.Drawing.Point(0, 64);
		this.qualitylimitingGB.Name = "qualitylimitingGB";
		this.qualitylimitingGB.Size = new System.Drawing.Size(420, 69);
		this.qualitylimitingGB.TabIndex = 9;
		this.qualitylimitingGB.TabStop = false;
		this.qualitylimitingGB.Text = "Quality Limiting";
		this.toolTip1.SetToolTip(this.qualitylimitingGB, "Limit the frames that are output to the XXXX best\r\nquality frames.");
		this.qualitylimitingGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.quality_limiting_typeCB.DisplayMember = "0";
		this.quality_limiting_typeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.quality_limiting_typeCB.FormattingEnabled = true;
		this.quality_limiting_typeCB.Items.AddRange(new object[2] { "Number Of Frames To Keep", "Percentage Of Frames To Keep" });
		this.quality_limiting_typeCB.Location = new System.Drawing.Point(5, 40);
		this.quality_limiting_typeCB.MaxDropDownItems = 10;
		this.quality_limiting_typeCB.Name = "quality_limiting_typeCB";
		this.quality_limiting_typeCB.Size = new System.Drawing.Size(221, 21);
		this.quality_limiting_typeCB.TabIndex = 11;
		this.quality_limiting_typeCB.ValueMember = "0";
		this.quality_limiting_typeCB.SelectedIndexChanged += new System.EventHandler(this.quality_limiting_typeCB_SelectedIndexChanged);
		this.qualityframelimitCB.AutoSize = true;
		this.qualityframelimitCB.Checked = true;
		this.qualityframelimitCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.qualityframelimitCB.Location = new System.Drawing.Point(6, 19);
		this.qualityframelimitCB.Name = "qualityframelimitCB";
		this.qualityframelimitCB.Size = new System.Drawing.Size(193, 17);
		this.qualityframelimitCB.TabIndex = 3;
		this.qualityframelimitCB.Text = "Only Keep The Best Quality Frames";
		this.toolTip1.SetToolTip(this.qualityframelimitCB, "Limit the frames that are output to the XXXX best\r\nquality frames.");
		this.qualityframelimitCB.UseVisualStyleBackColor = true;
		this.qualityframelimitCB.CheckedChanged += new System.EventHandler(this.qualityframelimitCB_CheckedChanged);
		this.qualityframesNUD.Enabled = false;
		this.qualityframesNUD.Location = new System.Drawing.Point(241, 41);
		this.qualityframesNUD.Maximum = new decimal(new int[4] { 99999, 0, 0, 0 });
		this.qualityframesNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.qualityframesNUD.Name = "qualityframesNUD";
		this.qualityframesNUD.Size = new System.Drawing.Size(65, 20);
		this.qualityframesNUD.TabIndex = 3;
		this.toolTip1.SetToolTip(this.qualityframesNUD, "Limit the frames that are output to the XXXX best\r\nquality frames.");
		this.qualityframesNUD.Value = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.qualityframesNUD.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.groupBox5.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.groupBox5.BackColor = System.Drawing.Color.White;
		this.groupBox5.Controls.Add(this.overexposedPB);
		this.groupBox5.Controls.Add(this.overexposedPANEL);
		this.groupBox5.Controls.Add(this.overexposedCB);
		this.groupBox5.Dock = System.Windows.Forms.DockStyle.Top;
		this.groupBox5.Location = new System.Drawing.Point(0, 239);
		this.groupBox5.Name = "groupBox5";
		this.groupBox5.Size = new System.Drawing.Size(420, 66);
		this.groupBox5.TabIndex = 1;
		this.groupBox5.TabStop = false;
		this.groupBox5.Text = "Overexposed Detection";
		this.toolTip1.SetToolTip(this.groupBox5, "Enable rejection of overexposed frames.  A frame is\r\nconsidered to be overexposed when the number of\r\npixels with maximum value (255) is greater than\r\nthe value set here.");
		this.overexposedPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.overexposedPB.Image = PIPP_Application.Properties.Resources.overexposed;
		this.overexposedPB.Location = new System.Drawing.Point(395, 11);
		this.overexposedPB.Name = "overexposedPB";
		this.overexposedPB.Size = new System.Drawing.Size(20, 20);
		this.overexposedPB.TabIndex = 15;
		this.overexposedPB.TabStop = false;
		this.overexposedPANEL.AutoSize = true;
		this.overexposedPANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.overexposedPANEL.BackColor = System.Drawing.Color.WhiteSmoke;
		this.overexposedPANEL.Controls.Add(this.overexposedNUD);
		this.overexposedPANEL.Controls.Add(this.label3);
		this.overexposedPANEL.Dock = System.Windows.Forms.DockStyle.Top;
		this.overexposedPANEL.Enabled = false;
		this.overexposedPANEL.Location = new System.Drawing.Point(3, 33);
		this.overexposedPANEL.Margin = new System.Windows.Forms.Padding(0);
		this.overexposedPANEL.Name = "overexposedPANEL";
		this.overexposedPANEL.Size = new System.Drawing.Size(414, 28);
		this.overexposedPANEL.TabIndex = 16;
		this.overexposedPANEL.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.overexposedNUD.Enabled = false;
		this.overexposedNUD.Location = new System.Drawing.Point(1, 5);
		this.overexposedNUD.Maximum = new decimal(new int[4] { 255, 0, 0, 0 });
		this.overexposedNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.overexposedNUD.Name = "overexposedNUD";
		this.overexposedNUD.Size = new System.Drawing.Size(65, 20);
		this.overexposedNUD.TabIndex = 6;
		this.toolTip1.SetToolTip(this.overexposedNUD, "Enable rejection of overexposed frames.  A frame is\r\nconsidered to be overexposed when the number of\r\npixels with maximum value (255) is greater than\r\nthe value set here.");
		this.overexposedNUD.Value = new decimal(new int[4] { 20, 0, 0, 0 });
		this.label3.AutoSize = true;
		this.label3.Location = new System.Drawing.Point(72, 7);
		this.label3.Name = "label3";
		this.label3.Size = new System.Drawing.Size(247, 13);
		this.label3.TabIndex = 7;
		this.label3.Text = "Number Of Maximum Value Pixels Before Rejection";
		this.toolTip1.SetToolTip(this.label3, "Enable rejection of overexposed frames.  A frame is\r\nconsidered to be overexposed when the number of\r\npixels with maximum value (255) is greater than\r\nthe value set here.");
		this.overexposedCB.AutoSize = true;
		this.overexposedCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.overexposedCB.Location = new System.Drawing.Point(3, 16);
		this.overexposedCB.Name = "overexposedCB";
		this.overexposedCB.Size = new System.Drawing.Size(414, 17);
		this.overexposedCB.TabIndex = 4;
		this.overexposedCB.Text = "Reject Frames With Overexposed Pixels";
		this.toolTip1.SetToolTip(this.overexposedCB, "Enable rejection of overexposed frames.  A frame is\r\nconsidered to be overexposed when the number of\r\npixels with maximum value (255) is greater than\r\nthe value set here.");
		this.overexposedCB.UseVisualStyleBackColor = true;
		this.overexposedCB.CheckedChanged += new System.EventHandler(this.overexposedCB_CheckedChanged);
		this.checkplanetCB.AutoSize = true;
		this.checkplanetCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.checkplanetCB.Location = new System.Drawing.Point(3, 16);
		this.checkplanetCB.Name = "checkplanetCB";
		this.checkplanetCB.Size = new System.Drawing.Size(418, 17);
		this.checkplanetCB.TabIndex = 0;
		this.checkplanetCB.Text = "Enable Object Detection (Rejects Frames Without Object)";
		this.toolTip1.SetToolTip(this.checkplanetCB, "Reject frames where a complete object is not\r\nfound completely on the frame.");
		this.checkplanetCB.UseVisualStyleBackColor = true;
		this.checkplanetCB.CheckedChanged += new System.EventHandler(this.checkplanetCB_CheckedChanged);
		this.test_detect_thresholdBUT.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.test_detect_thresholdBUT.BackColor = System.Drawing.Color.Transparent;
		this.test_detect_thresholdBUT.Location = new System.Drawing.Point(321, 13);
		this.test_detect_thresholdBUT.Name = "test_detect_thresholdBUT";
		this.test_detect_thresholdBUT.Size = new System.Drawing.Size(89, 42);
		this.test_detect_thresholdBUT.TabIndex = 7;
		this.test_detect_thresholdBUT.Text = "Test Detect Threshold";
		this.toolTip1.SetToolTip(this.test_detect_thresholdBUT, "Press this button to test the the object detect\r\nthreshold with the current frame.  Pixels that are\r\ndetected will be coloured red, undetected pixels will\r\nbe monochrome.");
		this.test_detect_thresholdBUT.UseVisualStyleBackColor = false;
		this.test_detect_thresholdBUT.Click += new System.EventHandler(this.test_detect_thresholdBUT_Click);
		this.centreplanetCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.centreplanetCB.Enabled = false;
		this.centreplanetCB.Location = new System.Drawing.Point(3, 142);
		this.centreplanetCB.Name = "centreplanetCB";
		this.centreplanetCB.Padding = new System.Windows.Forms.Padding(0, 3, 0, 0);
		this.centreplanetCB.Size = new System.Drawing.Size(418, 20);
		this.centreplanetCB.TabIndex = 4;
		this.centreplanetCB.Text = "Centre Object In Each Frame";
		this.toolTip1.SetToolTip(this.centreplanetCB, "Center the object in each frame to make\r\nthe stacking software's job easier.  To use this\r\nfunctionality the 'Reject frames without\r\nplanet/object' option must be selected.");
		this.centreplanetCB.UseVisualStyleBackColor = true;
		this.centreplanetCB.CheckedChanged += new System.EventHandler(this.centreplanetCB_CheckedChanged);
		this.centreplanetCB.EnabledChanged += new System.EventHandler(this.centreplanetCB_EnabledChanged);
		this.cropwidthNUD.Location = new System.Drawing.Point(8, 5);
		this.cropwidthNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.cropwidthNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.cropwidthNUD.Name = "cropwidthNUD";
		this.cropwidthNUD.Size = new System.Drawing.Size(65, 20);
		this.cropwidthNUD.TabIndex = 6;
		this.toolTip1.SetToolTip(this.cropwidthNUD, "Specify the width in pxels to crop each frame to.");
		this.cropwidthNUD.Value = new decimal(new int[4] { 448, 0, 0, 0 });
		this.label4.AutoSize = true;
		this.label4.Location = new System.Drawing.Point(79, 7);
		this.label4.Name = "label4";
		this.label4.Size = new System.Drawing.Size(76, 13);
		this.label4.TabIndex = 6;
		this.label4.Text = "Crop Width (X)";
		this.toolTip1.SetToolTip(this.label4, "Specify the width in pxels to crop each frame to.");
		this.cropheightNUD.Location = new System.Drawing.Point(8, 31);
		this.cropheightNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.cropheightNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.cropheightNUD.Name = "cropheightNUD";
		this.cropheightNUD.Size = new System.Drawing.Size(65, 20);
		this.cropheightNUD.TabIndex = 7;
		this.toolTip1.SetToolTip(this.cropheightNUD, "Specify the height in pxels to crop each frame to.");
		this.cropheightNUD.Value = new decimal(new int[4] { 448, 0, 0, 0 });
		this.label5.AutoSize = true;
		this.label5.Location = new System.Drawing.Point(79, 31);
		this.label5.Name = "label5";
		this.label5.Size = new System.Drawing.Size(79, 13);
		this.label5.TabIndex = 8;
		this.label5.Text = "Crop Height (Y)";
		this.toolTip1.SetToolTip(this.label5, "Specify the height in pxels to crop each frame to.");
		this.xoffsetNUD.Location = new System.Drawing.Point(8, 5);
		this.xoffsetNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.xoffsetNUD.Minimum = new decimal(new int[4] { 5000, 0, 0, -2147483648 });
		this.xoffsetNUD.Name = "xoffsetNUD";
		this.xoffsetNUD.Size = new System.Drawing.Size(65, 20);
		this.xoffsetNUD.TabIndex = 9;
		this.toolTip1.SetToolTip(this.xoffsetNUD, "Offset the cropped area.  A negative value moves\r\nthe cropped area to the left and a positive value\r\nmoves the cropped area to the right.\r\n");
		this.yoffsetNUD.Location = new System.Drawing.Point(8, 31);
		this.yoffsetNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.yoffsetNUD.Minimum = new decimal(new int[4] { 5000, 0, 0, -2147483648 });
		this.yoffsetNUD.Name = "yoffsetNUD";
		this.yoffsetNUD.Size = new System.Drawing.Size(65, 20);
		this.yoffsetNUD.TabIndex = 10;
		this.toolTip1.SetToolTip(this.yoffsetNUD, "Offset the cropped area.  A negative value moves\r\nthe cropped area downwards and a positive value\r\nmoves the cropped area upwards.\r\n");
		this.label6.AutoSize = true;
		this.label6.Location = new System.Drawing.Point(79, 7);
		this.label6.Name = "label6";
		this.label6.Size = new System.Drawing.Size(45, 13);
		this.label6.TabIndex = 11;
		this.label6.Text = "X Offset";
		this.toolTip1.SetToolTip(this.label6, "Offset the cropped area.  A negative value moves\r\nthe cropped area to the left and a positive value\r\nmoves the cropped area to the right.\r\n");
		this.label7.AutoSize = true;
		this.label7.Location = new System.Drawing.Point(79, 31);
		this.label7.Name = "label7";
		this.label7.Size = new System.Drawing.Size(45, 13);
		this.label7.TabIndex = 12;
		this.label7.Text = "Y Offset";
		this.toolTip1.SetToolTip(this.label7, "Offset the cropped area.  A negative value moves\r\nthe cropped area downwards and a positive value\r\nmoves the cropped area upwards.\r\n");
		this.hs_black_pointCB.AutoSize = true;
		this.hs_black_pointCB.Location = new System.Drawing.Point(3, 35);
		this.hs_black_pointCB.Name = "hs_black_pointCB";
		this.hs_black_pointCB.Size = new System.Drawing.Size(182, 17);
		this.hs_black_pointCB.TabIndex = 8;
		this.hs_black_pointCB.Text = "Set Histogram Black Point To 0%";
		this.toolTip1.SetToolTip(this.hs_black_pointCB, "Set the black point for each frame to 0.  This\r\noperation can be useful where frames have a\r\nbright background.");
		this.hs_black_pointCB.UseVisualStyleBackColor = true;
		this.hs_black_pointCB.CheckedChanged += new System.EventHandler(this.hs_black_pointCB_CheckedChanged);
		this.batchmodeRB.AutoSize = true;
		this.batchmodeRB.Checked = true;
		this.batchmodeRB.ForeColor = System.Drawing.SystemColors.ControlText;
		this.batchmodeRB.Location = new System.Drawing.Point(7, 20);
		this.batchmodeRB.Name = "batchmodeRB";
		this.batchmodeRB.Size = new System.Drawing.Size(83, 17);
		this.batchmodeRB.TabIndex = 0;
		this.batchmodeRB.TabStop = true;
		this.batchmodeRB.Text = "Batch Mode";
		this.toolTip1.SetToolTip(this.batchmodeRB, "Process each input file separately.");
		this.batchmodeRB.UseVisualStyleBackColor = true;
		this.joinmodeRB.AutoSize = true;
		this.joinmodeRB.Location = new System.Drawing.Point(7, 43);
		this.joinmodeRB.Name = "joinmodeRB";
		this.joinmodeRB.Size = new System.Drawing.Size(74, 17);
		this.joinmodeRB.TabIndex = 1;
		this.joinmodeRB.Text = "Join Mode";
		this.toolTip1.SetToolTip(this.joinmodeRB, "Process all input files together\r\nas if they were a single file.");
		this.joinmodeRB.UseVisualStyleBackColor = true;
		this.limitframesGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.limitframesGB.Controls.Add(this.limitframesPANEL2);
		this.limitframesGB.Controls.Add(this.limitframesPANEL);
		this.limitframesGB.Controls.Add(this.framerangePB);
		this.limitframesGB.Controls.Add(this.panel1);
		this.limitframesGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.limitframesGB.Location = new System.Drawing.Point(0, 92);
		this.limitframesGB.Name = "limitframesGB";
		this.limitframesGB.Size = new System.Drawing.Size(425, 96);
		this.limitframesGB.TabIndex = 1;
		this.limitframesGB.TabStop = false;
		this.limitframesGB.Text = "Input Frame Range";
		this.toolTip1.SetToolTip(this.limitframesGB, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.limitframesPANEL2.AutoSize = true;
		this.limitframesPANEL2.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.limitframesPANEL2.BackColor = System.Drawing.Color.WhiteSmoke;
		this.limitframesPANEL2.Controls.Add(this.numframesNUD);
		this.limitframesPANEL2.Controls.Add(this.label22);
		this.limitframesPANEL2.Dock = System.Windows.Forms.DockStyle.Top;
		this.limitframesPANEL2.Enabled = false;
		this.limitframesPANEL2.Location = new System.Drawing.Point(3, 67);
		this.limitframesPANEL2.Name = "limitframesPANEL2";
		this.limitframesPANEL2.Size = new System.Drawing.Size(419, 26);
		this.limitframesPANEL2.TabIndex = 16;
		this.toolTip1.SetToolTip(this.limitframesPANEL2, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.limitframesPANEL2.Visible = false;
		this.limitframesPANEL2.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.numframesNUD.Location = new System.Drawing.Point(3, 3);
		this.numframesNUD.Maximum = new decimal(new int[4] { 999999999, 0, 0, 0 });
		this.numframesNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numframesNUD.Name = "numframesNUD";
		this.numframesNUD.Size = new System.Drawing.Size(59, 20);
		this.numframesNUD.TabIndex = 9;
		this.toolTip1.SetToolTip(this.numframesNUD, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.numframesNUD.Value = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.label22.AutoSize = true;
		this.label22.Location = new System.Drawing.Point(63, 7);
		this.label22.Name = "label22";
		this.label22.Size = new System.Drawing.Size(95, 13);
		this.label22.TabIndex = 10;
		this.label22.Text = "Number Of Frames";
		this.toolTip1.SetToolTip(this.label22, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.limitframesPANEL.AutoSize = true;
		this.limitframesPANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.limitframesPANEL.BackColor = System.Drawing.Color.WhiteSmoke;
		this.limitframesPANEL.Controls.Add(this.startframeNUD);
		this.limitframesPANEL.Controls.Add(this.label9);
		this.limitframesPANEL.Controls.Add(this.label8);
		this.limitframesPANEL.Controls.Add(this.endframeNUD);
		this.limitframesPANEL.Dock = System.Windows.Forms.DockStyle.Top;
		this.limitframesPANEL.Enabled = false;
		this.limitframesPANEL.Location = new System.Drawing.Point(3, 41);
		this.limitframesPANEL.Name = "limitframesPANEL";
		this.limitframesPANEL.Size = new System.Drawing.Size(419, 26);
		this.limitframesPANEL.TabIndex = 15;
		this.toolTip1.SetToolTip(this.limitframesPANEL, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.limitframesPANEL.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.startframeNUD.Enabled = false;
		this.startframeNUD.Location = new System.Drawing.Point(3, 3);
		this.startframeNUD.Maximum = new decimal(new int[4] { 999999999, 0, 0, 0 });
		this.startframeNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.startframeNUD.Name = "startframeNUD";
		this.startframeNUD.Size = new System.Drawing.Size(59, 20);
		this.startframeNUD.TabIndex = 9;
		this.toolTip1.SetToolTip(this.startframeNUD, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.startframeNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.startframeNUD.ValueChanged += new System.EventHandler(this.startframeNUD_ValueChanged);
		this.label9.AutoSize = true;
		this.label9.Location = new System.Drawing.Point(63, 7);
		this.label9.Name = "label9";
		this.label9.Size = new System.Drawing.Size(61, 13);
		this.label9.TabIndex = 10;
		this.label9.Text = "Start Frame";
		this.toolTip1.SetToolTip(this.label9, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.label8.AutoSize = true;
		this.label8.Location = new System.Drawing.Point(198, 7);
		this.label8.Name = "label8";
		this.label8.Size = new System.Drawing.Size(58, 13);
		this.label8.TabIndex = 8;
		this.label8.Text = "End Frame";
		this.toolTip1.SetToolTip(this.label8, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.endframeNUD.Enabled = false;
		this.endframeNUD.Location = new System.Drawing.Point(138, 3);
		this.endframeNUD.Maximum = new decimal(new int[4] { 999999999, 0, 0, 0 });
		this.endframeNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.endframeNUD.Name = "endframeNUD";
		this.endframeNUD.Size = new System.Drawing.Size(59, 20);
		this.endframeNUD.TabIndex = 7;
		this.toolTip1.SetToolTip(this.endframeNUD, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.endframeNUD.Value = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.endframeNUD.ValueChanged += new System.EventHandler(this.endframeNUD_ValueChanged);
		this.framerangePB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.framerangePB.Image = PIPP_Application.Properties.Resources.input_range1;
		this.framerangePB.Location = new System.Drawing.Point(397, 11);
		this.framerangePB.Name = "framerangePB";
		this.framerangePB.Size = new System.Drawing.Size(20, 20);
		this.framerangePB.TabIndex = 12;
		this.framerangePB.TabStop = false;
		this.panel1.AutoSize = true;
		this.panel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panel1.Controls.Add(this.limitframesCB);
		this.panel1.Controls.Add(this.framelimitCB);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel1.Location = new System.Drawing.Point(3, 16);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(419, 25);
		this.panel1.TabIndex = 14;
		this.limitframesCB.DisplayMember = "0";
		this.limitframesCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.limitframesCB.FormattingEnabled = true;
		this.limitframesCB.Items.AddRange(new object[2] { "Start Frame to End Frame", "Frames From Centre of Video" });
		this.limitframesCB.Location = new System.Drawing.Point(138, 1);
		this.limitframesCB.MaxDropDownItems = 10;
		this.limitframesCB.Name = "limitframesCB";
		this.limitframesCB.Size = new System.Drawing.Size(190, 21);
		this.limitframesCB.TabIndex = 10;
		this.toolTip1.SetToolTip(this.limitframesCB, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.limitframesCB.ValueMember = "0";
		this.limitframesCB.SelectedIndexChanged += new System.EventHandler(this.limitframesCB_SelectedIndexChanged);
		this.framelimitCB.AutoSize = true;
		this.framelimitCB.Location = new System.Drawing.Point(3, 3);
		this.framelimitCB.Name = "framelimitCB";
		this.framelimitCB.Size = new System.Drawing.Size(114, 17);
		this.framelimitCB.TabIndex = 0;
		this.framelimitCB.Text = "Limit Frame Range";
		this.toolTip1.SetToolTip(this.framelimitCB, "This control limits the frames that are processed from\r\nthe input frames.  The frames processed can be\r\nspecified as a range or as the number of frames\r\nprocessed from the centre of the input frames.");
		this.framelimitCB.UseVisualStyleBackColor = true;
		this.framelimitCB.CheckedChanged += new System.EventHandler(this.framelimitCB_CheckedChanged);
		this.load_button.Location = new System.Drawing.Point(3, 4);
		this.load_button.Name = "load_button";
		this.load_button.Size = new System.Drawing.Size(150, 21);
		this.load_button.TabIndex = 5;
		this.load_button.Text = "Add Image Files";
		this.toolTip1.SetToolTip(this.load_button, "Add new source files to list of files to be processed.");
		this.load_button.UseVisualStyleBackColor = true;
		this.load_button.Click += new System.EventHandler(this.addsourceBTN_Click);
		this.remove_button.Enabled = false;
		this.remove_button.Location = new System.Drawing.Point(3, 27);
		this.remove_button.Name = "remove_button";
		this.remove_button.Size = new System.Drawing.Size(150, 21);
		this.remove_button.TabIndex = 6;
		this.remove_button.Text = "Remove Selected Files";
		this.toolTip1.SetToolTip(this.remove_button, "Remove selected source files from list of files to be\r\nprocessed.");
		this.remove_button.UseVisualStyleBackColor = true;
		this.remove_button.Click += new System.EventHandler(this.remove_button_Click);
		this.remove_all_button.Enabled = false;
		this.remove_all_button.Location = new System.Drawing.Point(3, 50);
		this.remove_all_button.Name = "remove_all_button";
		this.remove_all_button.Size = new System.Drawing.Size(150, 21);
		this.remove_all_button.TabIndex = 7;
		this.remove_all_button.Text = "Remove All Image Files";
		this.toolTip1.SetToolTip(this.remove_all_button, "Remove all source files from list of files to be processed.\r\n");
		this.remove_all_button.UseVisualStyleBackColor = true;
		this.remove_all_button.Click += new System.EventHandler(this.remove_all_button_Click);
		this.y800decodestrictCB.AutoSize = true;
		this.y800decodestrictCB.Location = new System.Drawing.Point(3, 19);
		this.y800decodestrictCB.Name = "y800decodestrictCB";
		this.y800decodestrictCB.Size = new System.Drawing.Size(197, 17);
		this.y800decodestrictCB.TabIndex = 4;
		this.y800decodestrictCB.Text = "Decode Y800 Codec Frames Strictly";
		this.toolTip1.SetToolTip(this.y800decodestrictCB, "Use strict Y800 decoding rather than Imaging Source\r\ncompatible Y800 decoding.  If this is selected by\r\nmistake then pixel values outside of the 16 to 235\r\nrange will be clipped.");
		this.y800decodestrictCB.UseVisualStyleBackColor = true;
		this.rgbsplitGB.BackColor = System.Drawing.Color.White;
		this.rgbsplitGB.Controls.Add(this.splitrgbPB);
		this.rgbsplitGB.Controls.Add(this.rgbsplitCB);
		this.rgbsplitGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.rgbsplitGB.Location = new System.Drawing.Point(0, 273);
		this.rgbsplitGB.Name = "rgbsplitGB";
		this.rgbsplitGB.Size = new System.Drawing.Size(420, 42);
		this.rgbsplitGB.TabIndex = 17;
		this.rgbsplitGB.TabStop = false;
		this.rgbsplitGB.Text = "Colour (RGB) Channel Splitting";
		this.toolTip1.SetToolTip(this.rgbsplitGB, "Spilt colour output frames into individual R, G and B\r\nchannels.  When this option is selected three mono\r\noutput files are begenerated instead of one colour\r\noutput file.");
		this.splitrgbPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.splitrgbPB.Image = PIPP_Application.Properties.Resources.split_rgb;
		this.splitrgbPB.Location = new System.Drawing.Point(395, 11);
		this.splitrgbPB.Name = "splitrgbPB";
		this.splitrgbPB.Size = new System.Drawing.Size(20, 20);
		this.splitrgbPB.TabIndex = 12;
		this.splitrgbPB.TabStop = false;
		this.rgbsplitCB.AutoSize = true;
		this.rgbsplitCB.Location = new System.Drawing.Point(6, 19);
		this.rgbsplitCB.Name = "rgbsplitCB";
		this.rgbsplitCB.Size = new System.Drawing.Size(217, 17);
		this.rgbsplitCB.TabIndex = 2;
		this.rgbsplitCB.Text = "Split RGB Channels (Colour Output Only)";
		this.toolTip1.SetToolTip(this.rgbsplitCB, "Spilt colour output frames into individual R, G and B\r\nchannels.  When this option is selected three mono\r\noutput files are generated instead of one colour\r\noutput file.");
		this.rgbsplitCB.UseVisualStyleBackColor = true;
		this.convertomonoGB.BackColor = System.Drawing.Color.White;
		this.convertomonoGB.Controls.Add(this.conv_mono_comboBox);
		this.convertomonoGB.Controls.Add(this.pictureBox3);
		this.convertomonoGB.Controls.Add(this.convmonoCB);
		this.convertomonoGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.convertomonoGB.Location = new System.Drawing.Point(0, 113);
		this.convertomonoGB.Name = "convertomonoGB";
		this.convertomonoGB.Size = new System.Drawing.Size(420, 46);
		this.convertomonoGB.TabIndex = 18;
		this.convertomonoGB.TabStop = false;
		this.convertomonoGB.Text = "Monochrome Conversion";
		this.toolTip1.SetToolTip(this.convertomonoGB, "Convert colour frames to monochrome frames.");
		this.conv_mono_comboBox.DisplayMember = "0";
		this.conv_mono_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.conv_mono_comboBox.Enabled = false;
		this.conv_mono_comboBox.FormattingEnabled = true;
		this.conv_mono_comboBox.Items.AddRange(new object[7] { "Use RG & B Channels", "Use R Channel Only", "Use G Channel Only", "Use B Channel Only", "Use R & G Channels", "Use R & B Channels", "Use G & B Channels" });
		this.conv_mono_comboBox.Location = new System.Drawing.Point(189, 17);
		this.conv_mono_comboBox.MaxDropDownItems = 10;
		this.conv_mono_comboBox.Name = "conv_mono_comboBox";
		this.conv_mono_comboBox.Size = new System.Drawing.Size(151, 21);
		this.conv_mono_comboBox.TabIndex = 15;
		this.conv_mono_comboBox.ValueMember = "0";
		this.pictureBox3.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox3.Image = PIPP_Application.Properties.Resources.mono;
		this.pictureBox3.Location = new System.Drawing.Point(395, 11);
		this.pictureBox3.Name = "pictureBox3";
		this.pictureBox3.Size = new System.Drawing.Size(20, 20);
		this.pictureBox3.TabIndex = 14;
		this.pictureBox3.TabStop = false;
		this.convmonoCB.AutoSize = true;
		this.convmonoCB.Location = new System.Drawing.Point(3, 19);
		this.convmonoCB.Name = "convmonoCB";
		this.convmonoCB.Size = new System.Drawing.Size(177, 17);
		this.convmonoCB.TabIndex = 2;
		this.convmonoCB.Text = "Convert Colour To Monochrome";
		this.convmonoCB.UseVisualStyleBackColor = true;
		this.convmonoCB.CheckedChanged += new System.EventHandler(this.convmonoCB_CheckedChanged);
		this.debay_typeCB.DisplayMember = "0";
		this.debay_typeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.debay_typeCB.FormattingEnabled = true;
		this.debay_typeCB.Items.AddRange(new object[3] { "Bilinear", "HQ Linear", "Smooth Hue" });
		this.debay_typeCB.Location = new System.Drawing.Point(9, 19);
		this.debay_typeCB.MaxDropDownItems = 10;
		this.debay_typeCB.Name = "debay_typeCB";
		this.debay_typeCB.Size = new System.Drawing.Size(99, 21);
		this.debay_typeCB.TabIndex = 19;
		this.toolTip1.SetToolTip(this.debay_typeCB, "Debayer algorithm selection.\r\nBilinear: This is the recommended algorithm to\r\nuse, it is fast and produces high quality results.\r\nSmooth Hue: This alternative algorithm is much\r\nslower.");
		this.debay_typeCB.ValueMember = "0";
		this.debayerframesCB.AutoSize = true;
		this.debayerframesCB.Location = new System.Drawing.Point(6, 19);
		this.debayerframesCB.Name = "debayerframesCB";
		this.debayerframesCB.Size = new System.Drawing.Size(171, 17);
		this.debayerframesCB.TabIndex = 4;
		this.debayerframesCB.Text = "Debayer Monochrome Frames ";
		this.toolTip1.SetToolTip(this.debayerframesCB, "Enable colour debayering for any file format.  File\r\nformats that are known to contain raw image data\r\nwith a bayer pattern have their own debayer\r\ncontrols below.");
		this.debayerframesCB.UseVisualStyleBackColor = true;
		this.debayerframesCB.CheckedChanged += new System.EventHandler(this.debayerframesCB_CheckedChanged);
		this.serbitdepthCB.DisplayMember = "0";
		this.serbitdepthCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.serbitdepthCB.FormattingEnabled = true;
		this.serbitdepthCB.ItemHeight = 13;
		this.serbitdepthCB.Items.AddRange(new object[10] { "As SER File Header", "16 bits per pixel", "15 bits per pixel", "14 bits per pixel", "13 bits per pixel", "12 bits per pixel", "11 bits per pixel", "10 bits per pixel", "9 bits per pixel", "8 bits per pixel" });
		this.serbitdepthCB.Location = new System.Drawing.Point(3, 20);
		this.serbitdepthCB.MaxDropDownItems = 10;
		this.serbitdepthCB.Name = "serbitdepthCB";
		this.serbitdepthCB.Size = new System.Drawing.Size(127, 21);
		this.serbitdepthCB.TabIndex = 8;
		this.toolTip1.SetToolTip(this.serbitdepthCB, "This option allows the pixel depth value in the SER\r\nfile to be overridden.  This option is not\r\nrecommended unless the pixel depth value in the\r\nSER file header is incorrect.");
		this.serbitdepthCB.ValueMember = "0";
		this.raw_debayer_algorithmCB.DisplayMember = "0";
		this.raw_debayer_algorithmCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.raw_debayer_algorithmCB.FormattingEnabled = true;
		this.raw_debayer_algorithmCB.ItemHeight = 13;
		this.raw_debayer_algorithmCB.Items.AddRange(new object[4] { "Bilinear (Fastest)", "VNG (Variable Number of Gradients)", "PPG (Patterned Pixel Groupling)", "AHD (Adaptive Homogeneity-Directed)" });
		this.raw_debayer_algorithmCB.Location = new System.Drawing.Point(3, 8);
		this.raw_debayer_algorithmCB.MaxDropDownItems = 10;
		this.raw_debayer_algorithmCB.Name = "raw_debayer_algorithmCB";
		this.raw_debayer_algorithmCB.Size = new System.Drawing.Size(208, 21);
		this.raw_debayer_algorithmCB.TabIndex = 9;
		this.toolTip1.SetToolTip(this.raw_debayer_algorithmCB, "Debayer algorithm selection for raw image files.");
		this.raw_debayer_algorithmCB.ValueMember = "0";
		this.raw_image_debayerCB.AutoSize = true;
		this.raw_image_debayerCB.Checked = true;
		this.raw_image_debayerCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.raw_image_debayerCB.Location = new System.Drawing.Point(3, 38);
		this.raw_image_debayerCB.Name = "raw_image_debayerCB";
		this.raw_image_debayerCB.Size = new System.Drawing.Size(147, 17);
		this.raw_image_debayerCB.TabIndex = 6;
		this.raw_image_debayerCB.Text = "Debayer Raw Image Files";
		this.toolTip1.SetToolTip(this.raw_image_debayerCB, "Enable colour debayering for raw image files.");
		this.raw_image_debayerCB.UseVisualStyleBackColor = true;
		this.raw_image_debayerCB.CheckedChanged += new System.EventHandler(this.raw_image_debayerCB_CheckedChanged);
		this.wjpos_rememberCB.AutoSize = true;
		this.wjpos_rememberCB.Location = new System.Drawing.Point(323, 17);
		this.wjpos_rememberCB.Name = "wjpos_rememberCB";
		this.wjpos_rememberCB.Size = new System.Drawing.Size(77, 17);
		this.wjpos_rememberCB.TabIndex = 30;
		this.wjpos_rememberCB.Text = "Remember";
		this.toolTip1.SetToolTip(this.wjpos_rememberCB, "Enable this control for PIPP to remember the input\r\ndate/time format selection between sessions.");
		this.wjpos_rememberCB.UseVisualStyleBackColor = true;
		this.wjpos_rememberCB.CheckedChanged += new System.EventHandler(this.wjpos_rememberCB_CheckedChanged);
		this.date_time_extract_conv_utCB.AutoSize = true;
		this.date_time_extract_conv_utCB.Checked = true;
		this.date_time_extract_conv_utCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.date_time_extract_conv_utCB.Location = new System.Drawing.Point(3, 3);
		this.date_time_extract_conv_utCB.Name = "date_time_extract_conv_utCB";
		this.date_time_extract_conv_utCB.Size = new System.Drawing.Size(233, 17);
		this.date_time_extract_conv_utCB.TabIndex = 24;
		this.date_time_extract_conv_utCB.Text = "Convert From Local Time To Universal Time";
		this.toolTip1.SetToolTip(this.date_time_extract_conv_utCB, "Enable this option if the date and time in the input\r\nfilenames is local time rather than UT.  PIPP will\r\nthen convert the date and time to UT, also taking\r\ninto account daylight savings time.");
		this.date_time_extract_conv_utCB.UseVisualStyleBackColor = true;
		this.codec_qualityNUD.Location = new System.Drawing.Point(4, 4);
		this.codec_qualityNUD.Maximum = new decimal(new int[4] { 10, 0, 0, 0 });
		this.codec_qualityNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.codec_qualityNUD.Name = "codec_qualityNUD";
		this.codec_qualityNUD.Size = new System.Drawing.Size(44, 20);
		this.codec_qualityNUD.TabIndex = 16;
		this.toolTip1.SetToolTip(this.codec_qualityNUD, "Selects the quality of the generated AVI file when\r\nthe FMP4 codec is used.  Increasing the quality\r\nincreases the fle size.");
		this.codec_qualityNUD.Value = new decimal(new int[4] { 10, 0, 0, 0 });
		this.codec_qualityNUD.ValueChanged += new System.EventHandler(this.codec_qualityNUD_ValueChanged);
		this.median_filterCB.AutoSize = true;
		this.median_filterCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.median_filterCB.Location = new System.Drawing.Point(3, 16);
		this.median_filterCB.Name = "median_filterCB";
		this.median_filterCB.Size = new System.Drawing.Size(414, 17);
		this.median_filterCB.TabIndex = 1;
		this.median_filterCB.Text = "Enable Median Noise Filter (Monochrome/Raw Colour Only)";
		this.toolTip1.SetToolTip(this.median_filterCB, "Experimental noise reduction filter.  This filter supports\r\nboth true monochrome frames and raw colour frames\r\nthat are being debayered.\r\n\r\nThis filter does not work with colour frames.");
		this.median_filterCB.UseVisualStyleBackColor = true;
		this.gammaNUD.DecimalPlaces = 1;
		this.gammaNUD.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.gammaNUD.Location = new System.Drawing.Point(6, 44);
		this.gammaNUD.Maximum = new decimal(new int[4] { 5, 0, 0, 0 });
		this.gammaNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.gammaNUD.Name = "gammaNUD";
		this.gammaNUD.Size = new System.Drawing.Size(60, 20);
		this.gammaNUD.TabIndex = 17;
		this.toolTip1.SetToolTip(this.gammaNUD, "This control specifies a gamma correct value to apply\r\nto each frame.  Setting the gamma correct value to 1.0\r\ndisables this function.");
		this.gammaNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.label31.AutoSize = true;
		this.label31.Location = new System.Drawing.Point(72, 46);
		this.label31.Name = "label31";
		this.label31.Size = new System.Drawing.Size(210, 13);
		this.label31.TabIndex = 18;
		this.label31.Text = "Gamma Correct Value (Use 1.0 To Disable)";
		this.toolTip1.SetToolTip(this.label31, "This control specifies a gamma correct value to apply\r\nto each frame.  Setting the gamma correct value to 1.0\r\ndisables this function.");
		this.groupBox7.BackColor = System.Drawing.Color.White;
		this.groupBox7.Controls.Add(this.outputsuffixTB);
		this.groupBox7.Dock = System.Windows.Forms.DockStyle.Left;
		this.groupBox7.ImeMode = System.Windows.Forms.ImeMode.NoControl;
		this.groupBox7.Location = new System.Drawing.Point(203, 0);
		this.groupBox7.Name = "groupBox7";
		this.groupBox7.Size = new System.Drawing.Size(211, 50);
		this.groupBox7.TabIndex = 7;
		this.groupBox7.TabStop = false;
		this.groupBox7.Text = "Output Filename Suffix";
		this.toolTip1.SetToolTip(this.groupBox7, "Set a suffix for the output filenames.");
		this.outputsuffixTB.Dock = System.Windows.Forms.DockStyle.Top;
		this.outputsuffixTB.Location = new System.Drawing.Point(3, 16);
		this.outputsuffixTB.Name = "outputsuffixTB";
		this.outputsuffixTB.Size = new System.Drawing.Size(205, 20);
		this.outputsuffixTB.TabIndex = 0;
		this.outputsuffixTB.Text = "_pipp";
		this.toolTip1.SetToolTip(this.outputsuffixTB, "Set a sufffix for the output filenames.");
		this.add_dark_filesBUT.Location = new System.Drawing.Point(3, 4);
		this.add_dark_filesBUT.Name = "add_dark_filesBUT";
		this.add_dark_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.add_dark_filesBUT.TabIndex = 5;
		this.add_dark_filesBUT.Text = "Add Dark Files";
		this.toolTip1.SetToolTip(this.add_dark_filesBUT, "Add new dark files to list of files to be processed.");
		this.add_dark_filesBUT.UseVisualStyleBackColor = true;
		this.add_dark_filesBUT.Click += new System.EventHandler(this.add_dark_filesBUT_Click);
		this.remove_dark_filesBUT.Enabled = false;
		this.remove_dark_filesBUT.Location = new System.Drawing.Point(3, 27);
		this.remove_dark_filesBUT.Name = "remove_dark_filesBUT";
		this.remove_dark_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.remove_dark_filesBUT.TabIndex = 6;
		this.remove_dark_filesBUT.Text = "Remove Selected Files";
		this.toolTip1.SetToolTip(this.remove_dark_filesBUT, "Remove selected dark files from list of files to be \r\nprocessed.");
		this.remove_dark_filesBUT.UseVisualStyleBackColor = true;
		this.remove_dark_filesBUT.Click += new System.EventHandler(this.remove_dark_filesBUT_Click);
		this.remove_all_dark_filesBUT.Enabled = false;
		this.remove_all_dark_filesBUT.Location = new System.Drawing.Point(3, 50);
		this.remove_all_dark_filesBUT.Name = "remove_all_dark_filesBUT";
		this.remove_all_dark_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.remove_all_dark_filesBUT.TabIndex = 7;
		this.remove_all_dark_filesBUT.Text = "Remove All Dark Files";
		this.toolTip1.SetToolTip(this.remove_all_dark_filesBUT, "Remove all dark files from list of files to be processed.\r\n");
		this.remove_all_dark_filesBUT.UseVisualStyleBackColor = true;
		this.remove_all_dark_filesBUT.Click += new System.EventHandler(this.remove_all_dark_filesBUT_Click);
		this.add_flat_filesBUT.Location = new System.Drawing.Point(3, 4);
		this.add_flat_filesBUT.Name = "add_flat_filesBUT";
		this.add_flat_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.add_flat_filesBUT.TabIndex = 5;
		this.add_flat_filesBUT.Text = "Add Flat Files";
		this.toolTip1.SetToolTip(this.add_flat_filesBUT, "Add new flat files to list of files to be processed.");
		this.add_flat_filesBUT.UseVisualStyleBackColor = true;
		this.add_flat_filesBUT.Click += new System.EventHandler(this.add_flat_filesBUT_Click);
		this.remove_flat_filesBUT.Enabled = false;
		this.remove_flat_filesBUT.Location = new System.Drawing.Point(3, 27);
		this.remove_flat_filesBUT.Name = "remove_flat_filesBUT";
		this.remove_flat_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.remove_flat_filesBUT.TabIndex = 6;
		this.remove_flat_filesBUT.Text = "Remove Selected Files";
		this.toolTip1.SetToolTip(this.remove_flat_filesBUT, "Remove selected flat files from list of files to be\r\nprocessed.");
		this.remove_flat_filesBUT.UseVisualStyleBackColor = true;
		this.remove_flat_filesBUT.Click += new System.EventHandler(this.remove_flat_filesBUT_Click);
		this.remove_all_flat_filesBUT.Enabled = false;
		this.remove_all_flat_filesBUT.Location = new System.Drawing.Point(3, 50);
		this.remove_all_flat_filesBUT.Name = "remove_all_flat_filesBUT";
		this.remove_all_flat_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.remove_all_flat_filesBUT.TabIndex = 7;
		this.remove_all_flat_filesBUT.Text = "Remove All Flat Files";
		this.toolTip1.SetToolTip(this.remove_all_flat_filesBUT, "Remove all flat files from list of files to be processed.\r\n");
		this.remove_all_flat_filesBUT.UseVisualStyleBackColor = true;
		this.remove_all_flat_filesBUT.Click += new System.EventHandler(this.remove_all_flat_filesBUT_Click);
		this.add_flat_dark_filesBUT.Location = new System.Drawing.Point(3, 4);
		this.add_flat_dark_filesBUT.Name = "add_flat_dark_filesBUT";
		this.add_flat_dark_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.add_flat_dark_filesBUT.TabIndex = 5;
		this.add_flat_dark_filesBUT.Text = "Add Flat Dark Files";
		this.toolTip1.SetToolTip(this.add_flat_dark_filesBUT, "Add new flat dark files to list of files to be processed.");
		this.add_flat_dark_filesBUT.UseVisualStyleBackColor = true;
		this.add_flat_dark_filesBUT.Click += new System.EventHandler(this.add_flat_dark_filesBUT_Click);
		this.remove_flat_dark_filesBUT.Enabled = false;
		this.remove_flat_dark_filesBUT.Location = new System.Drawing.Point(3, 27);
		this.remove_flat_dark_filesBUT.Name = "remove_flat_dark_filesBUT";
		this.remove_flat_dark_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.remove_flat_dark_filesBUT.TabIndex = 6;
		this.remove_flat_dark_filesBUT.Text = "Remove Selected Files";
		this.toolTip1.SetToolTip(this.remove_flat_dark_filesBUT, "Remove selected flat dark files from list of files to be\r\nprocessed.");
		this.remove_flat_dark_filesBUT.UseVisualStyleBackColor = true;
		this.remove_flat_dark_filesBUT.Click += new System.EventHandler(this.remove_flat_dark_filesBUT_Click);
		this.remove_all_flat_dark_filesBUT.Enabled = false;
		this.remove_all_flat_dark_filesBUT.Location = new System.Drawing.Point(3, 50);
		this.remove_all_flat_dark_filesBUT.Name = "remove_all_flat_dark_filesBUT";
		this.remove_all_flat_dark_filesBUT.Size = new System.Drawing.Size(150, 21);
		this.remove_all_flat_dark_filesBUT.TabIndex = 7;
		this.remove_all_flat_dark_filesBUT.Text = "Remove All Flat Dark Files";
		this.toolTip1.SetToolTip(this.remove_all_flat_dark_filesBUT, "Remove all flat dark files from list of files to be processed.\r\n");
		this.remove_all_flat_dark_filesBUT.UseVisualStyleBackColor = true;
		this.remove_all_flat_dark_filesBUT.Click += new System.EventHandler(this.remove_all_flat_dark_filesBUT_Click);
		this.debay_patternCB.DisplayMember = "0";
		this.debay_patternCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.debay_patternCB.FormattingEnabled = true;
		this.debay_patternCB.Items.AddRange(new object[4] { "GBRG", "BGGR", "RGGB", "GRBG" });
		this.debay_patternCB.Location = new System.Drawing.Point(124, 18);
		this.debay_patternCB.MaxDropDownItems = 10;
		this.debay_patternCB.Name = "debay_patternCB";
		this.debay_patternCB.Size = new System.Drawing.Size(99, 21);
		this.debay_patternCB.TabIndex = 20;
		this.toolTip1.SetToolTip(this.debay_patternCB, "Select pattern to use for debayering raw  mono\r\nframes from a colour camera to generate colour\r\nframes.");
		this.debay_patternCB.ValueMember = "0";
		this.debay_patternCB.SelectedIndexChanged += new System.EventHandler(this.debay_patternCB_Changed);
		this.file_down_BUT.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
		this.file_down_BUT.Enabled = false;
		this.file_down_BUT.Font = new System.Drawing.Font("Wingdings", 10f);
		this.file_down_BUT.Image = PIPP_Application.Properties.Resources.arrow_down;
		this.file_down_BUT.Location = new System.Drawing.Point(162, 40);
		this.file_down_BUT.Name = "file_down_BUT";
		this.file_down_BUT.Size = new System.Drawing.Size(39, 23);
		this.file_down_BUT.TabIndex = 5;
		this.toolTip1.SetToolTip(this.file_down_BUT, "Move selected source file(s) down in the source file list.");
		this.file_down_BUT.UseVisualStyleBackColor = true;
		this.file_down_BUT.Click += new System.EventHandler(this.file_down_BUT_Click);
		this.file_up_BUT.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
		this.file_up_BUT.Enabled = false;
		this.file_up_BUT.Font = new System.Drawing.Font("Wingdings", 10f);
		this.file_up_BUT.Image = PIPP_Application.Properties.Resources.arrow_up;
		this.file_up_BUT.Location = new System.Drawing.Point(162, 14);
		this.file_up_BUT.Name = "file_up_BUT";
		this.file_up_BUT.Size = new System.Drawing.Size(39, 23);
		this.file_up_BUT.TabIndex = 4;
		this.toolTip1.SetToolTip(this.file_up_BUT, "Move selected source file(s) up in the source file list.");
		this.file_up_BUT.UseVisualStyleBackColor = true;
		this.file_up_BUT.Click += new System.EventHandler(this.file_up_BUT_Click);
		this.addleadingzerosCB.AutoSize = true;
		this.addleadingzerosCB.Checked = true;
		this.addleadingzerosCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.addleadingzerosCB.Location = new System.Drawing.Point(6, 64);
		this.addleadingzerosCB.Name = "addleadingzerosCB";
		this.addleadingzerosCB.Size = new System.Drawing.Size(254, 17);
		this.addleadingzerosCB.TabIndex = 17;
		this.addleadingzerosCB.Text = "Add Leading Zeros To Image Filename Numbers";
		this.toolTip1.SetToolTip(this.addleadingzerosCB, "By default the numbers in image filenames will be\r\nall made to be the same length by adding leading\r\nzeros.  This option allows this behaviour to be \r\ndisabled.");
		this.addleadingzerosCB.UseVisualStyleBackColor = true;
		this.dark_data_has_bayerCB.AutoSize = true;
		this.dark_data_has_bayerCB.Location = new System.Drawing.Point(199, 19);
		this.dark_data_has_bayerCB.Name = "dark_data_has_bayerCB";
		this.dark_data_has_bayerCB.Size = new System.Drawing.Size(217, 17);
		this.dark_data_has_bayerCB.TabIndex = 5;
		this.dark_data_has_bayerCB.Text = "Data Is Monochrome With Bayer Pattern";
		this.toolTip1.SetToolTip(this.dark_data_has_bayerCB, "Enable this control when the image data is raw\r\ncolour data (monochrome data with a bayer\r\npattern).  This allows PIPP to use the correct\r\nneighbouring pixels when doing hot pixel\r\nreplacement.");
		this.dark_data_has_bayerCB.UseVisualStyleBackColor = true;
		this.hot_pixel_thresholdNUD.Location = new System.Drawing.Point(121, 42);
		this.hot_pixel_thresholdNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.hot_pixel_thresholdNUD.Name = "hot_pixel_thresholdNUD";
		this.hot_pixel_thresholdNUD.Size = new System.Drawing.Size(58, 20);
		this.hot_pixel_thresholdNUD.TabIndex = 3;
		this.toolTip1.SetToolTip(this.hot_pixel_thresholdNUD, "This control sets the the threshold that a master dark\r\npixel must reach to be considered as a hot pixel.");
		this.hot_pixel_thresholdNUD.Value = new decimal(new int[4] { 90, 0, 0, 0 });
		this.save_master_dark_CB.AutoSize = true;
		this.save_master_dark_CB.Location = new System.Drawing.Point(6, 43);
		this.save_master_dark_CB.Name = "save_master_dark_CB";
		this.save_master_dark_CB.Size = new System.Drawing.Size(144, 17);
		this.save_master_dark_CB.TabIndex = 1;
		this.save_master_dark_CB.Text = "Save Master Dark Frame";
		this.toolTip1.SetToolTip(this.save_master_dark_CB, "When this option is enabled the master dark frame is\r\nsaved as a TIFF file in the logs directory that PIPP\r\ngenerates.");
		this.save_master_dark_CB.UseVisualStyleBackColor = true;
		this.hot_pixel_thresholdLAB.AutoSize = true;
		this.hot_pixel_thresholdLAB.Location = new System.Drawing.Point(6, 44);
		this.hot_pixel_thresholdLAB.Name = "hot_pixel_thresholdLAB";
		this.hot_pixel_thresholdLAB.Size = new System.Drawing.Size(109, 13);
		this.hot_pixel_thresholdLAB.TabIndex = 4;
		this.hot_pixel_thresholdLAB.Text = "Hot Pixel threshold %:";
		this.toolTip1.SetToolTip(this.hot_pixel_thresholdLAB, "This control sets the the threshold that a master dark\r\npixel must reach to be considered as a hot pixel.");
		this.save_master_flat_CB.AutoSize = true;
		this.save_master_flat_CB.Location = new System.Drawing.Point(6, 43);
		this.save_master_flat_CB.Name = "save_master_flat_CB";
		this.save_master_flat_CB.Size = new System.Drawing.Size(138, 17);
		this.save_master_flat_CB.TabIndex = 1;
		this.save_master_flat_CB.Text = "Save Master Flat Frame";
		this.toolTip1.SetToolTip(this.save_master_flat_CB, "When this option is enabled the master flat frame\r\nis saved as a TIFF file in the logs directory that PIPP\r\ngenerates.");
		this.save_master_flat_CB.UseVisualStyleBackColor = true;
		this.save_master_flat_dark_CB.AutoSize = true;
		this.save_master_flat_dark_CB.Location = new System.Drawing.Point(6, 43);
		this.save_master_flat_dark_CB.Name = "save_master_flat_dark_CB";
		this.save_master_flat_dark_CB.Size = new System.Drawing.Size(164, 17);
		this.save_master_flat_dark_CB.TabIndex = 1;
		this.save_master_flat_dark_CB.Text = "Save Master Flat Dark Frame";
		this.toolTip1.SetToolTip(this.save_master_flat_dark_CB, "When this option is enabled the master dark frame is\r\nsaved as a TIFF file in the logs directory that PIPP\r\ngenerates.");
		this.save_master_flat_dark_CB.UseVisualStyleBackColor = true;
		this.label32.AutoSize = true;
		this.label32.Location = new System.Drawing.Point(77, 21);
		this.label32.Name = "label32";
		this.label32.Size = new System.Drawing.Size(54, 13);
		this.label32.TabIndex = 19;
		this.label32.Text = "X Position";
		this.toolTip1.SetToolTip(this.label32, "Specify the X (Horizontal) position of the Anchor\r\nFeature Box.\r\n");
		this.label33.AutoSize = true;
		this.label33.Location = new System.Drawing.Point(77, 47);
		this.label33.Name = "label33";
		this.label33.Size = new System.Drawing.Size(54, 13);
		this.label33.TabIndex = 20;
		this.label33.Text = "Y Position";
		this.toolTip1.SetToolTip(this.label33, "Specify the Y (Vertical) position of the Anchor\r\nFeature Box.");
		this.label35.AutoSize = true;
		this.label35.Location = new System.Drawing.Point(76, 70);
		this.label35.Name = "label35";
		this.label35.Size = new System.Drawing.Size(54, 13);
		this.label35.TabIndex = 20;
		this.label35.Text = "Y Position";
		this.toolTip1.SetToolTip(this.label35, "Specify the Y (Vertical) position of the Area Of\r\nInterest.");
		this.label37.AutoSize = true;
		this.label37.Location = new System.Drawing.Point(76, 44);
		this.label37.Name = "label37";
		this.label37.Size = new System.Drawing.Size(54, 13);
		this.label37.TabIndex = 19;
		this.label37.Text = "X Position";
		this.toolTip1.SetToolTip(this.label37, "Specify the X (Horizontal) position of the Area Of\r\nInterest.");
		this.label36.AutoSize = true;
		this.label36.Location = new System.Drawing.Point(249, 70);
		this.label36.Name = "label36";
		this.label36.Size = new System.Drawing.Size(38, 13);
		this.label36.TabIndex = 27;
		this.label36.Text = "Height";
		this.toolTip1.SetToolTip(this.label36, "Specify the height of the Area Of Interest.");
		this.label38.AutoSize = true;
		this.label38.Location = new System.Drawing.Point(249, 44);
		this.label38.Name = "label38";
		this.label38.Size = new System.Drawing.Size(35, 13);
		this.label38.TabIndex = 26;
		this.label38.Text = "Width";
		this.toolTip1.SetToolTip(this.label38, "Specify the width of the Area Of Interest.");
		this.aoi_yNUD.Location = new System.Drawing.Point(5, 68);
		this.aoi_yNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.aoi_yNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.aoi_yNUD.Name = "aoi_yNUD";
		this.aoi_yNUD.Size = new System.Drawing.Size(65, 20);
		this.aoi_yNUD.TabIndex = 15;
		this.toolTip1.SetToolTip(this.aoi_yNUD, "Specify the Y (Vertical) position of the Area Of\r\nInterest.");
		this.aoi_yNUD.Value = new decimal(new int[4] { 10, 0, 0, 0 });
		this.aoi_yNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.afb_xNUD.Location = new System.Drawing.Point(6, 19);
		this.afb_xNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.afb_xNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.afb_xNUD.Name = "afb_xNUD";
		this.afb_xNUD.Size = new System.Drawing.Size(65, 20);
		this.afb_xNUD.TabIndex = 14;
		this.toolTip1.SetToolTip(this.afb_xNUD, "Specify the X (Horizontal) position of the Anchor\r\nFeature Box.");
		this.afb_xNUD.Value = new decimal(new int[4] { 150, 0, 0, 0 });
		this.afb_xNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.aoi_cropCB.AutoSize = true;
		this.aoi_cropCB.Checked = true;
		this.aoi_cropCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.aoi_cropCB.Location = new System.Drawing.Point(3, 117);
		this.aoi_cropCB.Name = "aoi_cropCB";
		this.aoi_cropCB.Size = new System.Drawing.Size(178, 17);
		this.aoi_cropCB.TabIndex = 28;
		this.aoi_cropCB.Text = "Crop Frames To Area Of Interest";
		this.toolTip1.SetToolTip(this.aoi_cropCB, "Enable cropping of each frame to the Area Of Interest.");
		this.aoi_cropCB.UseVisualStyleBackColor = true;
		this.aoi_heightNUD.Location = new System.Drawing.Point(178, 68);
		this.aoi_heightNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.aoi_heightNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.aoi_heightNUD.Name = "aoi_heightNUD";
		this.aoi_heightNUD.Size = new System.Drawing.Size(65, 20);
		this.aoi_heightNUD.TabIndex = 25;
		this.toolTip1.SetToolTip(this.aoi_heightNUD, "Specify the height of the Area Of Interest.");
		this.aoi_heightNUD.Value = new decimal(new int[4] { 300, 0, 0, 0 });
		this.aoi_heightNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.aoi_widthNUD.Location = new System.Drawing.Point(178, 42);
		this.aoi_widthNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.aoi_widthNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.aoi_widthNUD.Name = "aoi_widthNUD";
		this.aoi_widthNUD.Size = new System.Drawing.Size(65, 20);
		this.aoi_widthNUD.TabIndex = 24;
		this.toolTip1.SetToolTip(this.aoi_widthNUD, "Specify the width of the Area Of Interest.");
		this.aoi_widthNUD.Value = new decimal(new int[4] { 300, 0, 0, 0 });
		this.aoi_widthNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.aoi_xNUD.Location = new System.Drawing.Point(5, 42);
		this.aoi_xNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.aoi_xNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.aoi_xNUD.Name = "aoi_xNUD";
		this.aoi_xNUD.Size = new System.Drawing.Size(65, 20);
		this.aoi_xNUD.TabIndex = 14;
		this.toolTip1.SetToolTip(this.aoi_xNUD, "Specify the X (Horizontal) position of the Area Of\r\nInterest.");
		this.aoi_xNUD.Value = new decimal(new int[4] { 10, 0, 0, 0 });
		this.aoi_xNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.afb_yNUD.Location = new System.Drawing.Point(6, 45);
		this.afb_yNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.afb_yNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.afb_yNUD.Name = "afb_yNUD";
		this.afb_yNUD.Size = new System.Drawing.Size(65, 20);
		this.afb_yNUD.TabIndex = 15;
		this.toolTip1.SetToolTip(this.afb_yNUD, "Specify the Y (Vertical) position of the Anchor\r\nFeature Box.");
		this.afb_yNUD.Value = new decimal(new int[4] { 150, 0, 0, 0 });
		this.afb_yNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.stabilise_objectRB.AutoSize = true;
		this.stabilise_objectRB.Location = new System.Drawing.Point(75, 19);
		this.stabilise_objectRB.Name = "stabilise_objectRB";
		this.stabilise_objectRB.Size = new System.Drawing.Size(105, 17);
		this.stabilise_objectRB.TabIndex = 1;
		this.stabilise_objectRB.Text = "Object/Planetary";
		this.toolTip1.SetToolTip(this.stabilise_objectRB, "Show controls for object or planetary frame \r\nstabilisation.  This type of stabilisation works by\r\ndetecting a complete bright object on a dark\r\nbackground.");
		this.stabilise_objectRB.UseVisualStyleBackColor = true;
		this.stabilise_objectRB.CheckedChanged += new System.EventHandler(this.stabilisation_CheckedChanged_event_handler);
		this.stabilise_noneRB.AutoSize = true;
		this.stabilise_noneRB.Checked = true;
		this.stabilise_noneRB.Location = new System.Drawing.Point(6, 19);
		this.stabilise_noneRB.Name = "stabilise_noneRB";
		this.stabilise_noneRB.Size = new System.Drawing.Size(51, 17);
		this.stabilise_noneRB.TabIndex = 0;
		this.stabilise_noneRB.TabStop = true;
		this.stabilise_noneRB.Text = "None";
		this.toolTip1.SetToolTip(this.stabilise_noneRB, "Do not show controls for any kind of frame \r\nstabilisation.");
		this.stabilise_noneRB.UseVisualStyleBackColor = true;
		this.stabilise_noneRB.CheckedChanged += new System.EventHandler(this.stabilisation_CheckedChanged_event_handler);
		this.label39.AutoSize = true;
		this.label39.Location = new System.Drawing.Point(249, 47);
		this.label39.Name = "label39";
		this.label39.Size = new System.Drawing.Size(38, 13);
		this.label39.TabIndex = 33;
		this.label39.Text = "Height";
		this.toolTip1.SetToolTip(this.label39, "Specify the height of the Area Of Interest.");
		this.label40.AutoSize = true;
		this.label40.Location = new System.Drawing.Point(249, 21);
		this.label40.Name = "label40";
		this.label40.Size = new System.Drawing.Size(35, 13);
		this.label40.TabIndex = 32;
		this.label40.Text = "Width";
		this.toolTip1.SetToolTip(this.label40, "Specify the width of the Area Of Interest.");
		this.afb_heightNUD.Increment = new decimal(new int[4] { 16, 0, 0, 0 });
		this.afb_heightNUD.Location = new System.Drawing.Point(178, 45);
		this.afb_heightNUD.Maximum = new decimal(new int[4] { 512, 0, 0, 0 });
		this.afb_heightNUD.Minimum = new decimal(new int[4] { 64, 0, 0, 0 });
		this.afb_heightNUD.Name = "afb_heightNUD";
		this.afb_heightNUD.Size = new System.Drawing.Size(65, 20);
		this.afb_heightNUD.TabIndex = 31;
		this.toolTip1.SetToolTip(this.afb_heightNUD, "Specify the height of the Anchor Feature Box.");
		this.afb_heightNUD.Value = new decimal(new int[4] { 128, 0, 0, 0 });
		this.afb_heightNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.afb_widthNUD.Increment = new decimal(new int[4] { 16, 0, 0, 0 });
		this.afb_widthNUD.Location = new System.Drawing.Point(178, 19);
		this.afb_widthNUD.Maximum = new decimal(new int[4] { 512, 0, 0, 0 });
		this.afb_widthNUD.Minimum = new decimal(new int[4] { 64, 0, 0, 0 });
		this.afb_widthNUD.Name = "afb_widthNUD";
		this.afb_widthNUD.Size = new System.Drawing.Size(65, 20);
		this.afb_widthNUD.TabIndex = 30;
		this.toolTip1.SetToolTip(this.afb_widthNUD, "Specify the width of the Anchor Feature Box");
		this.afb_widthNUD.Value = new decimal(new int[4] { 128, 0, 0, 0 });
		this.afb_widthNUD.ValueChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.label34.AutoSize = true;
		this.label34.Location = new System.Drawing.Point(90, 67);
		this.label34.Name = "label34";
		this.label34.Size = new System.Drawing.Size(142, 13);
		this.label34.TabIndex = 7;
		this.label34.Text = "Framerate To Use Otherwise";
		this.toolTip1.SetToolTip(this.label34, "Control the framerate of the output animated GIF.");
		this.gif_fpsNUD.DecimalPlaces = 2;
		this.gif_fpsNUD.Increment = new decimal(new int[4] { 1, 0, 0, 131072 });
		this.gif_fpsNUD.Location = new System.Drawing.Point(19, 65);
		this.gif_fpsNUD.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.gif_fpsNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 131072 });
		this.gif_fpsNUD.Name = "gif_fpsNUD";
		this.gif_fpsNUD.Size = new System.Drawing.Size(65, 20);
		this.gif_fpsNUD.TabIndex = 6;
		this.toolTip1.SetToolTip(this.gif_fpsNUD, "Control the framerate of the output animated GIF.");
		this.gif_fpsNUD.Value = new decimal(new int[4] { 3, 0, 0, 0 });
		this.gif_fpsNUD.ValueChanged += new System.EventHandler(this.fpsNUD_ValueChanged);
		this.gif_fpsCB.AutoSize = true;
		this.gif_fpsCB.Location = new System.Drawing.Point(9, 43);
		this.gif_fpsCB.Name = "gif_fpsCB";
		this.gif_fpsCB.Size = new System.Drawing.Size(248, 17);
		this.gif_fpsCB.TabIndex = 8;
		this.gif_fpsCB.Text = "Use Same Framerate As Input Video If Possible";
		this.toolTip1.SetToolTip(this.gif_fpsCB, "Control the framerate of the output animated GIF.");
		this.gif_fpsCB.UseVisualStyleBackColor = true;
		this.gif_repeat_foreverCB.AutoSize = true;
		this.gif_repeat_foreverCB.Checked = true;
		this.gif_repeat_foreverCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.gif_repeat_foreverCB.Location = new System.Drawing.Point(9, 20);
		this.gif_repeat_foreverCB.Name = "gif_repeat_foreverCB";
		this.gif_repeat_foreverCB.Size = new System.Drawing.Size(144, 17);
		this.gif_repeat_foreverCB.TabIndex = 0;
		this.gif_repeat_foreverCB.Text = "Loop Frames Repeatedly";
		this.toolTip1.SetToolTip(this.gif_repeat_foreverCB, "This option controls whether the animated GIF will\r\nplay repeatedly or just play once.");
		this.gif_repeat_foreverCB.UseVisualStyleBackColor = true;
		this.binningCB.DisplayMember = "0";
		this.binningCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.binningCB.FormattingEnabled = true;
		this.binningCB.ItemHeight = 13;
		this.binningCB.Items.AddRange(new object[5] { "Binning Disabled", "2x2 Binning", "3x3 Binning", "4x4 Binning", "5x5 Binning" });
		this.binningCB.Location = new System.Drawing.Point(3, 16);
		this.binningCB.MaxDropDownItems = 10;
		this.binningCB.Name = "binningCB";
		this.binningCB.Size = new System.Drawing.Size(127, 21);
		this.binningCB.TabIndex = 9;
		this.binningCB.Tag = "";
		this.toolTip1.SetToolTip(this.binningCB, "Pixel binning is the process of combining blocks of \r\npixels to form a single pixel.  This reduces the number\r\nof pixels and therefore reduces the size of the image.");
		this.binningCB.ValueMember = "0";
		this.binningCB.SelectedIndexChanged += new System.EventHandler(this.binningCB_SelectedIndexChanged);
		this.binning_calculationCB.DisplayMember = "0";
		this.binning_calculationCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.binning_calculationCB.FormattingEnabled = true;
		this.binning_calculationCB.ItemHeight = 13;
		this.binning_calculationCB.Items.AddRange(new object[2] { "Average Of Pixels", "Sum Of Pixels" });
		this.binning_calculationCB.Location = new System.Drawing.Point(3, 43);
		this.binning_calculationCB.MaxDropDownItems = 10;
		this.binning_calculationCB.Name = "binning_calculationCB";
		this.binning_calculationCB.Size = new System.Drawing.Size(127, 21);
		this.binning_calculationCB.TabIndex = 16;
		this.binning_calculationCB.Tag = "";
		this.toolTip1.SetToolTip(this.binning_calculationCB, "Pixel binning is the process of combining blocks of \r\npixels to form a single pixel.  This reduces the number\r\nof pixels and therefore reduces the size of the image.");
		this.binning_calculationCB.ValueMember = "0";
		this.resize_widthNUD.Enabled = false;
		this.resize_widthNUD.Location = new System.Drawing.Point(3, 56);
		this.resize_widthNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.resize_widthNUD.Minimum = new decimal(new int[4] { 10, 0, 0, 0 });
		this.resize_widthNUD.Name = "resize_widthNUD";
		this.resize_widthNUD.Size = new System.Drawing.Size(65, 20);
		this.resize_widthNUD.TabIndex = 28;
		this.toolTip1.SetToolTip(this.resize_widthNUD, "Specify the X (Horizontal) position of the Anchor\r\nFeature Box.");
		this.resize_widthNUD.Value = new decimal(new int[4] { 150, 0, 0, 0 });
		this.resize_widthLAB.AutoSize = true;
		this.resize_widthLAB.Enabled = false;
		this.resize_widthLAB.Location = new System.Drawing.Point(74, 58);
		this.resize_widthLAB.Name = "resize_widthLAB";
		this.resize_widthLAB.Size = new System.Drawing.Size(35, 13);
		this.resize_widthLAB.TabIndex = 33;
		this.resize_widthLAB.Text = "Width";
		this.toolTip1.SetToolTip(this.resize_widthLAB, "Specify the width of the Area Of Interest.");
		this.resize_heightNUD.Enabled = false;
		this.resize_heightNUD.Location = new System.Drawing.Point(153, 56);
		this.resize_heightNUD.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.resize_heightNUD.Minimum = new decimal(new int[4] { 10, 0, 0, 0 });
		this.resize_heightNUD.Name = "resize_heightNUD";
		this.resize_heightNUD.Size = new System.Drawing.Size(65, 20);
		this.resize_heightNUD.TabIndex = 34;
		this.toolTip1.SetToolTip(this.resize_heightNUD, "Specify the X (Horizontal) position of the Anchor\r\nFeature Box.");
		this.resize_heightNUD.Value = new decimal(new int[4] { 150, 0, 0, 0 });
		this.resize_heightLAB.AutoSize = true;
		this.resize_heightLAB.Enabled = false;
		this.resize_heightLAB.Location = new System.Drawing.Point(226, 58);
		this.resize_heightLAB.Name = "resize_heightLAB";
		this.resize_heightLAB.Size = new System.Drawing.Size(38, 13);
		this.resize_heightLAB.TabIndex = 35;
		this.resize_heightLAB.Text = "Height";
		this.toolTip1.SetToolTip(this.resize_heightLAB, "Specify the height of the Area Of Interest.");
		this.framedropGB.Controls.Add(this.outputframedroppingPB);
		this.framedropGB.Controls.Add(this.label14);
		this.framedropGB.Controls.Add(this.outputframedropNUD);
		this.framedropGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.framedropGB.Location = new System.Drawing.Point(0, 315);
		this.framedropGB.Name = "framedropGB";
		this.framedropGB.Size = new System.Drawing.Size(420, 48);
		this.framedropGB.TabIndex = 0;
		this.framedropGB.TabStop = false;
		this.framedropGB.Text = "Output Frame Dropping";
		this.outputframedroppingPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.outputframedroppingPB.Image = PIPP_Application.Properties.Resources.frame_dropping;
		this.outputframedroppingPB.Location = new System.Drawing.Point(395, 11);
		this.outputframedroppingPB.Name = "outputframedroppingPB";
		this.outputframedroppingPB.Size = new System.Drawing.Size(20, 20);
		this.outputframedroppingPB.TabIndex = 13;
		this.outputframedroppingPB.TabStop = false;
		this.label14.AutoSize = true;
		this.label14.Location = new System.Drawing.Point(56, 21);
		this.label14.Name = "label14";
		this.label14.Size = new System.Drawing.Size(214, 13);
		this.label14.TabIndex = 2;
		this.label14.Text = "Frames To Drop After Each Frame Is Output";
		this.outputframedropNUD.Location = new System.Drawing.Point(6, 19);
		this.outputframedropNUD.Maximum = new decimal(new int[4] { 99999, 0, 0, 0 });
		this.outputframedropNUD.Name = "outputframedropNUD";
		this.outputframedropNUD.Size = new System.Drawing.Size(44, 20);
		this.outputframedropNUD.TabIndex = 1;
		this.outputfiledirectoryGB.BackColor = System.Drawing.Color.White;
		this.outputfiledirectoryGB.Controls.Add(this.pictureBox4);
		this.outputfiledirectoryGB.Controls.Add(this.outputdirectoryTB);
		this.outputfiledirectoryGB.Controls.Add(this.defaultouputdirBUT);
		this.outputfiledirectoryGB.Controls.Add(this.output_subdirectoryGB);
		this.outputfiledirectoryGB.Controls.Add(this.outputdirectoryBUT);
		this.outputfiledirectoryGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.outputfiledirectoryGB.ImeMode = System.Windows.Forms.ImeMode.NoControl;
		this.outputfiledirectoryGB.Location = new System.Drawing.Point(0, 63);
		this.outputfiledirectoryGB.Name = "outputfiledirectoryGB";
		this.outputfiledirectoryGB.Size = new System.Drawing.Size(420, 160);
		this.outputfiledirectoryGB.TabIndex = 4;
		this.outputfiledirectoryGB.TabStop = false;
		this.outputfiledirectoryGB.Text = "Output Directory";
		this.pictureBox4.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox4.Image = PIPP_Application.Properties.Resources.folder1;
		this.pictureBox4.Location = new System.Drawing.Point(395, 11);
		this.pictureBox4.Name = "pictureBox4";
		this.pictureBox4.Size = new System.Drawing.Size(20, 20);
		this.pictureBox4.TabIndex = 13;
		this.pictureBox4.TabStop = false;
		this.outputdirectoryTB.Cursor = System.Windows.Forms.Cursors.Arrow;
		this.outputdirectoryTB.Dock = System.Windows.Forms.DockStyle.Left;
		this.outputdirectoryTB.Location = new System.Drawing.Point(3, 16);
		this.outputdirectoryTB.Name = "outputdirectoryTB";
		this.outputdirectoryTB.ReadOnly = true;
		this.outputdirectoryTB.RightToLeft = System.Windows.Forms.RightToLeft.No;
		this.outputdirectoryTB.Size = new System.Drawing.Size(386, 20);
		this.outputdirectoryTB.TabIndex = 1;
		this.outputdirectoryTB.Tag = "";
		this.outputdirectoryTB.Text = "Default (Source File Directory)";
		this.outputdirectoryTB.WordWrap = false;
		this.defaultouputdirBUT.Location = new System.Drawing.Point(4, 43);
		this.defaultouputdirBUT.Name = "defaultouputdirBUT";
		this.defaultouputdirBUT.Size = new System.Drawing.Size(94, 23);
		this.defaultouputdirBUT.TabIndex = 5;
		this.defaultouputdirBUT.Text = "Use Default";
		this.defaultouputdirBUT.UseVisualStyleBackColor = true;
		this.defaultouputdirBUT.Click += new System.EventHandler(this.defaultouputdirBUT_Click);
		this.output_subdirectoryGB.BackColor = System.Drawing.Color.White;
		this.output_subdirectoryGB.Controls.Add(this.subdir_nameGB);
		this.output_subdirectoryGB.Controls.Add(this.create_output_subdirCB);
		this.output_subdirectoryGB.ImeMode = System.Windows.Forms.ImeMode.NoControl;
		this.output_subdirectoryGB.Location = new System.Drawing.Point(5, 72);
		this.output_subdirectoryGB.Name = "output_subdirectoryGB";
		this.output_subdirectoryGB.Size = new System.Drawing.Size(409, 83);
		this.output_subdirectoryGB.TabIndex = 22;
		this.output_subdirectoryGB.TabStop = false;
		this.output_subdirectoryGB.Text = "Output Subdirectory";
		this.subdir_nameGB.Controls.Add(this.subdir_nameTB);
		this.subdir_nameGB.Controls.Add(this.subdir_append_date_timeCB);
		this.subdir_nameGB.Location = new System.Drawing.Point(6, 35);
		this.subdir_nameGB.Name = "subdir_nameGB";
		this.subdir_nameGB.Size = new System.Drawing.Size(396, 44);
		this.subdir_nameGB.TabIndex = 19;
		this.subdir_nameGB.TabStop = false;
		this.subdir_nameGB.Text = "Subdirectory Name";
		this.outputdirectoryBUT.AutoEllipsis = true;
		this.outputdirectoryBUT.Location = new System.Drawing.Point(104, 43);
		this.outputdirectoryBUT.Name = "outputdirectoryBUT";
		this.outputdirectoryBUT.Size = new System.Drawing.Size(94, 23);
		this.outputdirectoryBUT.TabIndex = 3;
		this.outputdirectoryBUT.Text = "Select Directory";
		this.outputdirectoryBUT.UseVisualStyleBackColor = true;
		this.outputdirectoryBUT.Click += new System.EventHandler(this.outputdirectoryBUT_Click);
		this.qualityweightGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.qualityweightGB.Controls.Add(this.qualityweightingCB);
		this.qualityweightGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.qualityweightGB.Enabled = false;
		this.qualityweightGB.Location = new System.Drawing.Point(0, 0);
		this.qualityweightGB.Name = "qualityweightGB";
		this.qualityweightGB.Size = new System.Drawing.Size(424, 45);
		this.qualityweightGB.TabIndex = 8;
		this.qualityweightGB.TabStop = false;
		this.qualityweightGB.Text = "Quality Weighting";
		this.qualityweightGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.qualityweightingCB.AutoSize = true;
		this.qualityweightingCB.Location = new System.Drawing.Point(6, 19);
		this.qualityweightingCB.Name = "qualityweightingCB";
		this.qualityweightingCB.Size = new System.Drawing.Size(145, 17);
		this.qualityweightingCB.TabIndex = 3;
		this.qualityweightingCB.Text = "Enable Quality Weighting";
		this.qualityweightingCB.UseVisualStyleBackColor = true;
		this.qreorderCB.AutoSize = true;
		this.qreorderCB.Checked = true;
		this.qreorderCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.qreorderCB.Enabled = false;
		this.qreorderCB.Location = new System.Drawing.Point(6, 42);
		this.qreorderCB.Name = "qreorderCB";
		this.qreorderCB.Size = new System.Drawing.Size(177, 17);
		this.qreorderCB.TabIndex = 5;
		this.qreorderCB.Text = "Reorder Frames In Quality Order";
		this.qreorderCB.UseVisualStyleBackColor = true;
		this.qualitysortingCB.AutoSize = true;
		this.qualitysortingCB.Location = new System.Drawing.Point(6, 19);
		this.qualitysortingCB.Name = "qualitysortingCB";
		this.qualitysortingCB.Size = new System.Drawing.Size(145, 17);
		this.qualitysortingCB.TabIndex = 2;
		this.qualitysortingCB.Text = "Enable Quality Estimation";
		this.qualitysortingCB.UseVisualStyleBackColor = true;
		this.qualitysortingCB.CheckedChanged += new System.EventHandler(this.qualitysortingCB_CheckedChanged);
		this.defaultalgorithmCB.AutoSize = true;
		this.defaultalgorithmCB.Checked = true;
		this.defaultalgorithmCB.Location = new System.Drawing.Point(6, 20);
		this.defaultalgorithmCB.Name = "defaultalgorithmCB";
		this.defaultalgorithmCB.Size = new System.Drawing.Size(167, 17);
		this.defaultalgorithmCB.TabIndex = 0;
		this.defaultalgorithmCB.TabStop = true;
		this.defaultalgorithmCB.Text = "Default PIPP Quality Algorithm";
		this.defaultalgorithmCB.UseVisualStyleBackColor = true;
		this.defaultalgorithmCB.CheckedChanged += new System.EventHandler(this.qualitysortingCB_CheckedChanged);
		this.ninoxalgorithmCB.AutoSize = true;
		this.ninoxalgorithmCB.Location = new System.Drawing.Point(6, 43);
		this.ninoxalgorithmCB.Name = "ninoxalgorithmCB";
		this.ninoxalgorithmCB.Size = new System.Drawing.Size(171, 17);
		this.ninoxalgorithmCB.TabIndex = 1;
		this.ninoxalgorithmCB.Text = "Original Ninox Quality Algorithm";
		this.ninoxalgorithmCB.UseVisualStyleBackColor = true;
		this.ninoxalgorithmCB.CheckedChanged += new System.EventHandler(this.qualitysortingCB_CheckedChanged);
		this.histoalgorithmCB.AutoSize = true;
		this.histoalgorithmCB.Location = new System.Drawing.Point(6, 66);
		this.histoalgorithmCB.Name = "histoalgorithmCB";
		this.histoalgorithmCB.Size = new System.Drawing.Size(146, 17);
		this.histoalgorithmCB.TabIndex = 2;
		this.histoalgorithmCB.Text = "Peak Histogram Algorithm";
		this.histoalgorithmCB.UseVisualStyleBackColor = true;
		this.histoalgorithmCB.CheckedChanged += new System.EventHandler(this.qualitysortingCB_CheckedChanged);
		this.totalpixelalgorithmCB.AutoSize = true;
		this.totalpixelalgorithmCB.Location = new System.Drawing.Point(6, 89);
		this.totalpixelalgorithmCB.Name = "totalpixelalgorithmCB";
		this.totalpixelalgorithmCB.Size = new System.Drawing.Size(147, 17);
		this.totalpixelalgorithmCB.TabIndex = 3;
		this.totalpixelalgorithmCB.Text = "Total Brightness Algorithm";
		this.totalpixelalgorithmCB.UseVisualStyleBackColor = true;
		this.totalpixelalgorithmCB.CheckedChanged += new System.EventHandler(this.qualitysortingCB_CheckedChanged);
		this.qualityoptionsGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.qualityoptionsGB.Controls.Add(this.label13);
		this.qualityoptionsGB.Controls.Add(this.label12);
		this.qualityoptionsGB.Controls.Add(this.label10);
		this.qualityoptionsGB.Controls.Add(this.downsampleincNUD);
		this.qualityoptionsGB.Controls.Add(this.maxdownsampleNUD);
		this.qualityoptionsGB.Controls.Add(this.mindownsampleNUD);
		this.qualityoptionsGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.qualityoptionsGB.Enabled = false;
		this.qualityoptionsGB.Location = new System.Drawing.Point(0, 247);
		this.qualityoptionsGB.Name = "qualityoptionsGB";
		this.qualityoptionsGB.Size = new System.Drawing.Size(420, 101);
		this.qualityoptionsGB.TabIndex = 3;
		this.qualityoptionsGB.TabStop = false;
		this.qualityoptionsGB.Text = "Quality Algorithm Options";
		this.qualityoptionsGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.label13.AutoSize = true;
		this.label13.Location = new System.Drawing.Point(76, 71);
		this.label13.Name = "label13";
		this.label13.Size = new System.Drawing.Size(139, 13);
		this.label13.TabIndex = 7;
		this.label13.Text = "Subsample Increment Value";
		this.label12.AutoSize = true;
		this.label12.Location = new System.Drawing.Point(76, 45);
		this.label12.Name = "label12";
		this.label12.Size = new System.Drawing.Size(136, 13);
		this.label12.TabIndex = 6;
		this.label12.Text = "Maximum Subsample Value";
		this.label10.AutoSize = true;
		this.label10.Location = new System.Drawing.Point(76, 21);
		this.label10.Name = "label10";
		this.label10.Size = new System.Drawing.Size(133, 13);
		this.label10.TabIndex = 5;
		this.label10.Text = "Minimum Subsample Value";
		this.downsampleincNUD.Location = new System.Drawing.Point(5, 71);
		this.downsampleincNUD.Maximum = new decimal(new int[4] { 10, 0, 0, 0 });
		this.downsampleincNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.downsampleincNUD.Name = "downsampleincNUD";
		this.downsampleincNUD.Size = new System.Drawing.Size(65, 20);
		this.downsampleincNUD.TabIndex = 4;
		this.downsampleincNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.maxdownsampleNUD.Location = new System.Drawing.Point(5, 45);
		this.maxdownsampleNUD.Maximum = new decimal(new int[4] { 10, 0, 0, 0 });
		this.maxdownsampleNUD.Minimum = new decimal(new int[4] { 2, 0, 0, 0 });
		this.maxdownsampleNUD.Name = "maxdownsampleNUD";
		this.maxdownsampleNUD.Size = new System.Drawing.Size(65, 20);
		this.maxdownsampleNUD.TabIndex = 3;
		this.maxdownsampleNUD.Value = new decimal(new int[4] { 5, 0, 0, 0 });
		this.maxdownsampleNUD.ValueChanged += new System.EventHandler(this.maxdownsampleNUD_ValueChanged);
		this.mindownsampleNUD.Location = new System.Drawing.Point(5, 19);
		this.mindownsampleNUD.Maximum = new decimal(new int[4] { 10, 0, 0, 0 });
		this.mindownsampleNUD.Minimum = new decimal(new int[4] { 2, 0, 0, 0 });
		this.mindownsampleNUD.Name = "mindownsampleNUD";
		this.mindownsampleNUD.Size = new System.Drawing.Size(65, 20);
		this.mindownsampleNUD.TabIndex = 2;
		this.mindownsampleNUD.Value = new decimal(new int[4] { 3, 0, 0, 0 });
		this.mindownsampleNUD.ValueChanged += new System.EventHandler(this.mindownsampleNUD_ValueChanged);
		this.autothresholdCB.AutoSize = true;
		this.autothresholdCB.Checked = true;
		this.autothresholdCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.autothresholdCB.Location = new System.Drawing.Point(6, 17);
		this.autothresholdCB.Name = "autothresholdCB";
		this.autothresholdCB.Size = new System.Drawing.Size(181, 17);
		this.autothresholdCB.TabIndex = 3;
		this.autothresholdCB.Text = "Auto Object Detection Threshold";
		this.autothresholdCB.UseVisualStyleBackColor = true;
		this.autothresholdCB.CheckedChanged += new System.EventHandler(this.autothresholdCB_CheckedChanged);
		this.thresholdNUD.Enabled = false;
		this.thresholdNUD.Location = new System.Drawing.Point(6, 35);
		this.thresholdNUD.Maximum = new decimal(new int[4] { 255, 0, 0, 0 });
		this.thresholdNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.thresholdNUD.Name = "thresholdNUD";
		this.thresholdNUD.Size = new System.Drawing.Size(45, 20);
		this.thresholdNUD.TabIndex = 4;
		this.thresholdNUD.Value = new decimal(new int[4] { 40, 0, 0, 0 });
		this.label2.AutoSize = true;
		this.label2.Location = new System.Drawing.Point(57, 37);
		this.label2.Name = "label2";
		this.label2.Size = new System.Drawing.Size(185, 13);
		this.label2.TabIndex = 5;
		this.label2.Text = "Object Detection Threshold (1 to 255)";
		this.minsizeNUD.Enabled = false;
		this.minsizeNUD.Location = new System.Drawing.Point(4, 18);
		this.minsizeNUD.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.minsizeNUD.Minimum = new decimal(new int[4] { 4, 0, 0, 0 });
		this.minsizeNUD.Name = "minsizeNUD";
		this.minsizeNUD.Size = new System.Drawing.Size(65, 20);
		this.minsizeNUD.TabIndex = 1;
		this.minsizeNUD.Value = new decimal(new int[4] { 15, 0, 0, 0 });
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(75, 20);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(141, 13);
		this.label1.TabIndex = 2;
		this.label1.Text = "Minimum Object Size (Pixels)";
		this.histostretchGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.histostretchGB.BackColor = System.Drawing.Color.White;
		this.histostretchGB.Controls.Add(this.rgbhistoCB);
		this.histostretchGB.Controls.Add(this.hs_black_pointCB);
		this.histostretchGB.Controls.Add(this.pictureBox2);
		this.histostretchGB.Controls.Add(this.label11);
		this.histostretchGB.Controls.Add(this.hs_white_pointNUD);
		this.histostretchGB.Controls.Add(this.hs_white_pointCB);
		this.histostretchGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.histostretchGB.Location = new System.Drawing.Point(0, 159);
		this.histostretchGB.Name = "histostretchGB";
		this.histostretchGB.Size = new System.Drawing.Size(420, 80);
		this.histostretchGB.TabIndex = 13;
		this.histostretchGB.TabStop = false;
		this.histostretchGB.Text = "Histogram Equalisation";
		this.rgbhistoCB.AutoSize = true;
		this.rgbhistoCB.Checked = true;
		this.rgbhistoCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.rgbhistoCB.Enabled = false;
		this.rgbhistoCB.Location = new System.Drawing.Point(3, 55);
		this.rgbhistoCB.Name = "rgbhistoCB";
		this.rgbhistoCB.Size = new System.Drawing.Size(224, 17);
		this.rgbhistoCB.TabIndex = 7;
		this.rgbhistoCB.Text = "Equalise R, G and B Channels Individually";
		this.rgbhistoCB.UseVisualStyleBackColor = true;
		this.pictureBox2.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox2.Image = PIPP_Application.Properties.Resources.histogram;
		this.pictureBox2.Location = new System.Drawing.Point(395, 11);
		this.pictureBox2.Name = "pictureBox2";
		this.pictureBox2.Size = new System.Drawing.Size(20, 20);
		this.pictureBox2.TabIndex = 14;
		this.pictureBox2.TabStop = false;
		this.label11.AutoSize = true;
		this.label11.Location = new System.Drawing.Point(244, 17);
		this.label11.Name = "label11";
		this.label11.Size = new System.Drawing.Size(15, 13);
		this.label11.TabIndex = 6;
		this.label11.Text = "%";
		this.hs_white_pointNUD.Enabled = false;
		this.hs_white_pointNUD.Location = new System.Drawing.Point(196, 15);
		this.hs_white_pointNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.hs_white_pointNUD.Name = "hs_white_pointNUD";
		this.hs_white_pointNUD.Size = new System.Drawing.Size(42, 20);
		this.hs_white_pointNUD.TabIndex = 6;
		this.hs_white_pointNUD.Value = new decimal(new int[4] { 75, 0, 0, 0 });
		this.hs_white_pointCB.AutoSize = true;
		this.hs_white_pointCB.Location = new System.Drawing.Point(3, 16);
		this.hs_white_pointCB.Name = "hs_white_pointCB";
		this.hs_white_pointCB.Size = new System.Drawing.Size(187, 17);
		this.hs_white_pointCB.TabIndex = 1;
		this.hs_white_pointCB.Text = "Stretch Histogram White Point To:";
		this.hs_white_pointCB.UseVisualStyleBackColor = true;
		this.hs_white_pointCB.CheckedChanged += new System.EventHandler(this.histostretchCB_CheckedChanged);
		this.groupBox3.BackColor = System.Drawing.Color.White;
		this.groupBox3.Controls.Add(this.planetary_animation_gifCB);
		this.groupBox3.Controls.Add(this.avi_archiveCB);
		this.groupBox3.Controls.Add(this.planetary_animation_aviCB);
		this.groupBox3.Controls.Add(this.issCB);
		this.groupBox3.Controls.Add(this.solar_closeupCB);
		this.groupBox3.Controls.Add(this.solar_full_discCB);
		this.groupBox3.Controls.Add(this.planetaryCB);
		this.groupBox3.Location = new System.Drawing.Point(363, 3);
		this.groupBox3.Name = "groupBox3";
		this.groupBox3.Size = new System.Drawing.Size(447, 68);
		this.groupBox3.TabIndex = 3;
		this.groupBox3.TabStop = false;
		this.groupBox3.Text = "Optimise Options For:";
		this.planetary_animation_gifCB.AutoSize = true;
		this.planetary_animation_gifCB.ForeColor = System.Drawing.Color.Green;
		this.planetary_animation_gifCB.Location = new System.Drawing.Point(232, 32);
		this.planetary_animation_gifCB.Name = "planetary_animation_gifCB";
		this.planetary_animation_gifCB.Size = new System.Drawing.Size(145, 17);
		this.planetary_animation_gifCB.TabIndex = 6;
		this.planetary_animation_gifCB.Text = "Planetary Animation (GIF)";
		this.planetary_animation_gifCB.UseVisualStyleBackColor = true;
		this.planetary_animation_gifCB.CheckedChanged += new System.EventHandler(this.planetary_animation_gifCB_CheckedChanged);
		this.avi_archiveCB.AutoSize = true;
		this.avi_archiveCB.ForeColor = System.Drawing.Color.Green;
		this.avi_archiveCB.Location = new System.Drawing.Point(7, 49);
		this.avi_archiveCB.Name = "avi_archiveCB";
		this.avi_archiveCB.Size = new System.Drawing.Size(82, 17);
		this.avi_archiveCB.TabIndex = 5;
		this.avi_archiveCB.Text = "AVI Archive";
		this.avi_archiveCB.UseVisualStyleBackColor = true;
		this.avi_archiveCB.CheckedChanged += new System.EventHandler(this.avi_archiveCB_CheckedChanged);
		this.planetary_animation_aviCB.AutoSize = true;
		this.planetary_animation_aviCB.ForeColor = System.Drawing.Color.Green;
		this.planetary_animation_aviCB.Location = new System.Drawing.Point(232, 15);
		this.planetary_animation_aviCB.Name = "planetary_animation_aviCB";
		this.planetary_animation_aviCB.Size = new System.Drawing.Size(145, 17);
		this.planetary_animation_aviCB.TabIndex = 4;
		this.planetary_animation_aviCB.Text = "Planetary Animation (AVI)";
		this.planetary_animation_aviCB.UseVisualStyleBackColor = true;
		this.planetary_animation_aviCB.CheckedChanged += new System.EventHandler(this.planetaryanimationCB_CheckedChanged);
		this.issCB.AutoSize = true;
		this.issCB.ForeColor = System.Drawing.Color.Green;
		this.issCB.Location = new System.Drawing.Point(7, 32);
		this.issCB.Name = "issCB";
		this.issCB.Size = new System.Drawing.Size(43, 17);
		this.issCB.TabIndex = 3;
		this.issCB.Text = "ISS";
		this.issCB.UseVisualStyleBackColor = true;
		this.issCB.CheckedChanged += new System.EventHandler(this.issCB_CheckedChanged);
		this.solar_closeupCB.AutoSize = true;
		this.solar_closeupCB.ForeColor = System.Drawing.Color.Green;
		this.solar_closeupCB.Location = new System.Drawing.Point(95, 15);
		this.solar_closeupCB.Name = "solar_closeupCB";
		this.solar_closeupCB.Size = new System.Drawing.Size(126, 17);
		this.solar_closeupCB.TabIndex = 2;
		this.solar_closeupCB.Text = "Solar/Lunar Close-up";
		this.solar_closeupCB.UseVisualStyleBackColor = true;
		this.solar_closeupCB.CheckedChanged += new System.EventHandler(this.solarCB_CheckedChanged);
		this.solar_full_discCB.AutoSize = true;
		this.solar_full_discCB.ForeColor = System.Drawing.Color.Green;
		this.solar_full_discCB.Location = new System.Drawing.Point(95, 32);
		this.solar_full_discCB.Name = "solar_full_discCB";
		this.solar_full_discCB.Size = new System.Drawing.Size(125, 17);
		this.solar_full_discCB.TabIndex = 1;
		this.solar_full_discCB.Text = "Solar/Lunar Full Disc";
		this.solar_full_discCB.UseVisualStyleBackColor = true;
		this.solar_full_discCB.CheckedChanged += new System.EventHandler(this.lunarCB_CheckedChanged);
		this.planetaryCB.AutoSize = true;
		this.planetaryCB.ForeColor = System.Drawing.Color.Green;
		this.planetaryCB.Location = new System.Drawing.Point(7, 15);
		this.planetaryCB.Name = "planetaryCB";
		this.planetaryCB.Size = new System.Drawing.Size(70, 17);
		this.planetaryCB.TabIndex = 0;
		this.planetaryCB.Text = "Planetary";
		this.planetaryCB.UseVisualStyleBackColor = true;
		this.planetaryCB.CheckedChanged += new System.EventHandler(this.planetaryCB_CheckedChanged);
		this.protectbayerCB.AutoSize = true;
		this.protectbayerCB.Location = new System.Drawing.Point(6, 39);
		this.protectbayerCB.Name = "protectbayerCB";
		this.protectbayerCB.Size = new System.Drawing.Size(334, 17);
		this.protectbayerCB.TabIndex = 3;
		this.protectbayerCB.Text = "Protect Bayer Pattern When Centring/Cropping (Do Not Debayer)";
		this.protectbayerCB.UseVisualStyleBackColor = true;
		this.ipframedroppingGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.ipframedroppingGB.Controls.Add(this.framedroppingPB);
		this.ipframedroppingGB.Controls.Add(this.label16);
		this.ipframedroppingGB.Controls.Add(this.ipframedropNUD);
		this.ipframedroppingGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.ipframedroppingGB.Location = new System.Drawing.Point(0, 188);
		this.ipframedroppingGB.Name = "ipframedroppingGB";
		this.ipframedroppingGB.Size = new System.Drawing.Size(425, 49);
		this.ipframedroppingGB.TabIndex = 17;
		this.ipframedroppingGB.TabStop = false;
		this.ipframedroppingGB.Text = "Input Frame Dropping";
		this.framedroppingPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.framedroppingPB.Image = PIPP_Application.Properties.Resources.frame_dropping;
		this.framedroppingPB.Location = new System.Drawing.Point(397, 11);
		this.framedroppingPB.Name = "framedroppingPB";
		this.framedroppingPB.Size = new System.Drawing.Size(20, 20);
		this.framedroppingPB.TabIndex = 13;
		this.framedroppingPB.TabStop = false;
		this.label16.AutoSize = true;
		this.label16.Location = new System.Drawing.Point(52, 22);
		this.label16.Name = "label16";
		this.label16.Size = new System.Drawing.Size(206, 13);
		this.label16.TabIndex = 2;
		this.label16.Text = "Frames To Drop After Each Frame Is Input";
		this.ipframedropNUD.Location = new System.Drawing.Point(6, 19);
		this.ipframedropNUD.Maximum = new decimal(new int[4] { 99999, 0, 0, 0 });
		this.ipframedropNUD.Name = "ipframedropNUD";
		this.ipframedropNUD.Size = new System.Drawing.Size(44, 20);
		this.ipframedropNUD.TabIndex = 1;
		this.colourdetectionGB.Controls.Add(this.colour_monoPB);
		this.colourdetectionGB.Controls.Add(this.colourdetectionCB);
		this.colourdetectionGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.colourdetectionGB.Location = new System.Drawing.Point(0, 237);
		this.colourdetectionGB.Name = "colourdetectionGB";
		this.colourdetectionGB.Size = new System.Drawing.Size(425, 50);
		this.colourdetectionGB.TabIndex = 18;
		this.colourdetectionGB.TabStop = false;
		this.colourdetectionGB.Text = "Input Frame Colour/Monochrome";
		this.colour_monoPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.colour_monoPB.Image = PIPP_Application.Properties.Resources.mono_colour2;
		this.colour_monoPB.Location = new System.Drawing.Point(397, 11);
		this.colour_monoPB.Name = "colour_monoPB";
		this.colour_monoPB.Size = new System.Drawing.Size(20, 20);
		this.colour_monoPB.TabIndex = 11;
		this.colour_monoPB.TabStop = false;
		this.colourdetectionCB.DisplayMember = "0";
		this.colourdetectionCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.colourdetectionCB.FormattingEnabled = true;
		this.colourdetectionCB.Items.AddRange(new object[3] { "AutoDetect", "Monochrome", "Colour" });
		this.colourdetectionCB.Location = new System.Drawing.Point(9, 19);
		this.colourdetectionCB.MaxDropDownItems = 10;
		this.colourdetectionCB.Name = "colourdetectionCB";
		this.colourdetectionCB.Size = new System.Drawing.Size(99, 21);
		this.colourdetectionCB.TabIndex = 9;
		this.colourdetectionCB.ValueMember = "0";
		this.colourdetectionCB.SelectedIndexChanged += new System.EventHandler(this.colourdetectionCB_SelectedIndexChanged);
		this.raw_output_colour_spaceCB.DisplayMember = "0";
		this.raw_output_colour_spaceCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.raw_output_colour_spaceCB.FormattingEnabled = true;
		this.raw_output_colour_spaceCB.ItemHeight = 13;
		this.raw_output_colour_spaceCB.Items.AddRange(new object[7] { "Raw Camera Sensor Colour (Fastest)", "sRGB D65", "Adobe RGB (1998) D65", "Wide Gamut RGB D65", "Kodak ProPhoto RGB D65", "XYZ ", "ACES" });
		this.raw_output_colour_spaceCB.Location = new System.Drawing.Point(3, 35);
		this.raw_output_colour_spaceCB.MaxDropDownItems = 10;
		this.raw_output_colour_spaceCB.Name = "raw_output_colour_spaceCB";
		this.raw_output_colour_spaceCB.Size = new System.Drawing.Size(208, 21);
		this.raw_output_colour_spaceCB.TabIndex = 9;
		this.raw_output_colour_spaceCB.ValueMember = "0";
		this.raw_highlight_recovery_typeCB.DisplayMember = "0";
		this.raw_highlight_recovery_typeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.raw_highlight_recovery_typeCB.FormattingEnabled = true;
		this.raw_highlight_recovery_typeCB.ItemHeight = 13;
		this.raw_highlight_recovery_typeCB.Items.AddRange(new object[10] { "Clip all highlights to solid white", "Leave highlights unclipped (fastest)", "Blend clipped and unclipped values together", "Reconstruct highlights. (-H=3)", "Reconstruct highlights. (-H=4)", "Reconstruct highlights. (-H=5)", "Reconstruct highlights. (-H=6)", "Reconstruct highlights. (-H=7)", "Reconstruct highlights. (-H=8)", "Reconstruct highlights. (-H=9)" });
		this.raw_highlight_recovery_typeCB.Location = new System.Drawing.Point(13, 120);
		this.raw_highlight_recovery_typeCB.MaxDropDownItems = 10;
		this.raw_highlight_recovery_typeCB.Name = "raw_highlight_recovery_typeCB";
		this.raw_highlight_recovery_typeCB.Size = new System.Drawing.Size(234, 21);
		this.raw_highlight_recovery_typeCB.TabIndex = 11;
		this.raw_highlight_recovery_typeCB.ValueMember = "0";
		this.inputdatetimeformatCB.DisplayMember = "0";
		this.inputdatetimeformatCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.inputdatetimeformatCB.FormattingEnabled = true;
		this.inputdatetimeformatCB.Items.AddRange(new object[12]
		{
			"Firecapture 1", "Firecapture 1 (with count)", "Firecapture 2", "Firecapture 2 (with count)", "IC Capture", "SharpCap", "WinJUPOS", "WinJUPOS (old style)", "wxAstroCapture", "Custom 1",
			"Custom 2", "Custom 3"
		});
		this.inputdatetimeformatCB.Location = new System.Drawing.Point(5, 15);
		this.inputdatetimeformatCB.MaxDropDownItems = 10;
		this.inputdatetimeformatCB.Name = "inputdatetimeformatCB";
		this.inputdatetimeformatCB.Size = new System.Drawing.Size(149, 21);
		this.inputdatetimeformatCB.TabIndex = 21;
		this.inputdatetimeformatCB.ValueMember = "0";
		this.inputdatetimeformatCB.SelectedIndexChanged += new System.EventHandler(this.inputdatetimeformatCB_SelectedIndexChanged);
		this.datetimeTB.BackColor = System.Drawing.SystemColors.Window;
		this.datetimeTB.Location = new System.Drawing.Point(160, 16);
		this.datetimeTB.Name = "datetimeTB";
		this.datetimeTB.Size = new System.Drawing.Size(155, 20);
		this.datetimeTB.TabIndex = 0;
		this.datetimeTB.Validated += new System.EventHandler(this.datetimeTB_Validated);
		this.winjuposfilenameCB.AutoSize = true;
		this.winjuposfilenameCB.Location = new System.Drawing.Point(6, 16);
		this.winjuposfilenameCB.Name = "winjuposfilenameCB";
		this.winjuposfilenameCB.Size = new System.Drawing.Size(232, 17);
		this.winjuposfilenameCB.TabIndex = 3;
		this.winjuposfilenameCB.Text = "Generate WinJUPOS Compatible Filenames";
		this.winjuposfilenameCB.UseVisualStyleBackColor = true;
		this.winjuposfilenameCB.CheckedChanged += new System.EventHandler(this.winjuposfilenameCB_CheckedChanged);
		this.label19.AutoSize = true;
		this.label19.ForeColor = System.Drawing.SystemColors.ControlText;
		this.label19.Location = new System.Drawing.Point(54, 113);
		this.label19.Name = "label19";
		this.label19.Size = new System.Drawing.Size(299, 13);
		this.label19.TabIndex = 20;
		this.label19.Text = "Input Frames Per Second (For SER Files Without Timestamps)";
		this.inputframespersecondNUD.Location = new System.Drawing.Point(4, 111);
		this.inputframespersecondNUD.Maximum = new decimal(new int[4] { 500, 0, 0, 0 });
		this.inputframespersecondNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.inputframespersecondNUD.Name = "inputframespersecondNUD";
		this.inputframespersecondNUD.Size = new System.Drawing.Size(44, 20);
		this.inputframespersecondNUD.TabIndex = 19;
		this.inputframespersecondNUD.Value = new decimal(new int[4] { 60, 0, 0, 0 });
		this.winjuposGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.winjuposGB.Controls.Add(this.winjuposPB);
		this.winjuposGB.Controls.Add(this.winjuposfilenameCB);
		this.winjuposGB.Controls.Add(this.winjupostimeCB);
		this.winjuposGB.Controls.Add(this.label20);
		this.winjuposGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.winjuposGB.Location = new System.Drawing.Point(0, 588);
		this.winjuposGB.Name = "winjuposGB";
		this.winjuposGB.Size = new System.Drawing.Size(424, 70);
		this.winjuposGB.TabIndex = 19;
		this.winjuposGB.TabStop = false;
		this.winjuposGB.Text = "WinJUPOS Compatible Filenames";
		this.winjuposGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.winjuposPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.winjuposPB.Image = PIPP_Application.Properties.Resources.winjupos;
		this.winjuposPB.Location = new System.Drawing.Point(399, 11);
		this.winjuposPB.Name = "winjuposPB";
		this.winjuposPB.Size = new System.Drawing.Size(20, 20);
		this.winjuposPB.TabIndex = 20;
		this.winjuposPB.TabStop = false;
		this.winjupostimeCB.DisplayMember = "0";
		this.winjupostimeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.winjupostimeCB.FormattingEnabled = true;
		this.winjupostimeCB.IntegralHeight = false;
		this.winjupostimeCB.Items.AddRange(new object[3] { "Output Frames Mid-Point Time", "Earliest Output Frame Time", "First Output Frame Time" });
		this.winjupostimeCB.Location = new System.Drawing.Point(6, 39);
		this.winjupostimeCB.MaxDropDownItems = 10;
		this.winjupostimeCB.Name = "winjupostimeCB";
		this.winjupostimeCB.Size = new System.Drawing.Size(179, 21);
		this.winjupostimeCB.TabIndex = 24;
		this.winjupostimeCB.ValueMember = "0";
		this.label20.AutoSize = true;
		this.label20.Location = new System.Drawing.Point(191, 43);
		this.label20.Name = "label20";
		this.label20.Size = new System.Drawing.Size(131, 13);
		this.label20.TabIndex = 25;
		this.label20.Text = "Time To Use For Filename";
		this.old_format_aviCB.AutoSize = true;
		this.old_format_aviCB.Location = new System.Drawing.Point(6, 4);
		this.old_format_aviCB.Name = "old_format_aviCB";
		this.old_format_aviCB.Size = new System.Drawing.Size(163, 17);
		this.old_format_aviCB.TabIndex = 21;
		this.old_format_aviCB.Text = "Generate Old Format AVI File";
		this.old_format_aviCB.UseVisualStyleBackColor = true;
		this.old_format_aviCB.CheckedChanged += new System.EventHandler(this.old_format_aviCB_CheckedChanged);
		this.avi_codecCB.DisplayMember = "0";
		this.avi_codecCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.avi_codecCB.FormattingEnabled = true;
		this.avi_codecCB.ItemHeight = 13;
		this.avi_codecCB.Items.AddRange(new object[3] { "DIB (RAW Uncompressed)", "ULRG (Ut Video Lossless Compression)", "FMP4 (MPEG-4 Lossy Compression)" });
		this.avi_codecCB.Location = new System.Drawing.Point(7, 17);
		this.avi_codecCB.MaxDropDownItems = 10;
		this.avi_codecCB.Name = "avi_codecCB";
		this.avi_codecCB.Size = new System.Drawing.Size(230, 21);
		this.avi_codecCB.TabIndex = 22;
		this.avi_codecCB.ValueMember = "0";
		this.avi_codecCB.SelectedIndexChanged += new System.EventHandler(this.avi_codecCB_SelectedIndexChanged);
		this.input_gainNUD.DecimalPlaces = 1;
		this.input_gainNUD.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.input_gainNUD.Location = new System.Drawing.Point(6, 18);
		this.input_gainNUD.Maximum = new decimal(new int[4] { 64, 0, 0, 0 });
		this.input_gainNUD.Minimum = new decimal(new int[4] { 5, 0, 0, 65536 });
		this.input_gainNUD.Name = "input_gainNUD";
		this.input_gainNUD.Size = new System.Drawing.Size(60, 20);
		this.input_gainNUD.TabIndex = 17;
		this.input_gainNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.input_gainLAB.AutoSize = true;
		this.input_gainLAB.Location = new System.Drawing.Point(72, 20);
		this.input_gainLAB.Name = "input_gainLAB";
		this.input_gainLAB.Size = new System.Drawing.Size(239, 13);
		this.input_gainLAB.TabIndex = 18;
		this.input_gainLAB.Text = "Gain To Apply To Each Frame Before Processing";
		this.edge_in_shadowCB.DisplayMember = "0";
		this.edge_in_shadowCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.edge_in_shadowCB.FormattingEnabled = true;
		this.edge_in_shadowCB.ItemHeight = 13;
		this.edge_in_shadowCB.Items.AddRange(new object[9] { "None", "Top Left Hand Side ", "Left Hand Side", "Bottom Left Hand Side", "Top Right Hand Side", "Right Hand Side", "Bottom Right Hand Side", "Top", "Bottom" });
		this.edge_in_shadowCB.Location = new System.Drawing.Point(3, 4);
		this.edge_in_shadowCB.MaxDropDownItems = 10;
		this.edge_in_shadowCB.Name = "edge_in_shadowCB";
		this.edge_in_shadowCB.Size = new System.Drawing.Size(165, 21);
		this.edge_in_shadowCB.TabIndex = 10;
		this.edge_in_shadowCB.ValueMember = "0";
		this.edge_in_shadowCB.SelectedIndexChanged += new System.EventHandler(this.edge_in_shadowCB_SelectedIndexChanged);
		this.old_format_max_sizeCB.DisplayMember = "0";
		this.old_format_max_sizeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.old_format_max_sizeCB.Enabled = false;
		this.old_format_max_sizeCB.FormattingEnabled = true;
		this.old_format_max_sizeCB.Items.AddRange(new object[2] { "4GB Maximum Size", "2GB Maximum Size" });
		this.old_format_max_sizeCB.Location = new System.Drawing.Point(205, 2);
		this.old_format_max_sizeCB.MaxDropDownItems = 10;
		this.old_format_max_sizeCB.Name = "old_format_max_sizeCB";
		this.old_format_max_sizeCB.Size = new System.Drawing.Size(149, 21);
		this.old_format_max_sizeCB.TabIndex = 22;
		this.old_format_max_sizeCB.ValueMember = "0";
		this.avi_safe_frame_widthCB.AutoSize = true;
		this.avi_safe_frame_widthCB.Location = new System.Drawing.Point(9, 95);
		this.avi_safe_frame_widthCB.Name = "avi_safe_frame_widthCB";
		this.avi_safe_frame_widthCB.Size = new System.Drawing.Size(289, 17);
		this.avi_safe_frame_widthCB.TabIndex = 22;
		this.avi_safe_frame_widthCB.Text = "Modify AVI Frame Width To Safe Size (DIB Codec Only)";
		this.avi_safe_frame_widthCB.UseVisualStyleBackColor = true;
		this.extract_date_timeCB.AutoSize = true;
		this.extract_date_timeCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.extract_date_timeCB.Location = new System.Drawing.Point(3, 16);
		this.extract_date_timeCB.Name = "extract_date_timeCB";
		this.extract_date_timeCB.Size = new System.Drawing.Size(413, 17);
		this.extract_date_timeCB.TabIndex = 4;
		this.extract_date_timeCB.Text = "Extract File Start Date/Time From Filename";
		this.extract_date_timeCB.UseVisualStyleBackColor = true;
		this.extract_date_timeCB.CheckedChanged += new System.EventHandler(this.extract_date_timeCB_CheckedChanged);
		this.includequalityvalueCB.AutoSize = true;
		this.includequalityvalueCB.Location = new System.Drawing.Point(6, 87);
		this.includequalityvalueCB.Name = "includequalityvalueCB";
		this.includequalityvalueCB.Size = new System.Drawing.Size(183, 17);
		this.includequalityvalueCB.TabIndex = 18;
		this.includequalityvalueCB.Text = "Include Quality Value In Filename";
		this.includequalityvalueCB.UseVisualStyleBackColor = true;
		this.noframerenumberCB.AutoSize = true;
		this.noframerenumberCB.Location = new System.Drawing.Point(6, 41);
		this.noframerenumberCB.Name = "noframerenumberCB";
		this.noframerenumberCB.Size = new System.Drawing.Size(191, 17);
		this.noframerenumberCB.TabIndex = 1;
		this.noframerenumberCB.Text = "Disable Image Frame Renumbering";
		this.noframerenumberCB.UseVisualStyleBackColor = true;
		this.dark_frame_subtractionCB.AutoSize = true;
		this.dark_frame_subtractionCB.Checked = true;
		this.dark_frame_subtractionCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.dark_frame_subtractionCB.Location = new System.Drawing.Point(6, 20);
		this.dark_frame_subtractionCB.Name = "dark_frame_subtractionCB";
		this.dark_frame_subtractionCB.Size = new System.Drawing.Size(174, 17);
		this.dark_frame_subtractionCB.TabIndex = 0;
		this.dark_frame_subtractionCB.Text = "Enable Dark Frame Subtraction";
		this.dark_frame_subtractionCB.UseVisualStyleBackColor = true;
		this.dark_frame_subtractionCB.CheckedChanged += new System.EventHandler(this.handle_dark_file_enables);
		this.replace_hot_pixelsCB.AutoSize = true;
		this.replace_hot_pixelsCB.Location = new System.Drawing.Point(6, 19);
		this.replace_hot_pixelsCB.Name = "replace_hot_pixelsCB";
		this.replace_hot_pixelsCB.Size = new System.Drawing.Size(170, 17);
		this.replace_hot_pixelsCB.TabIndex = 2;
		this.replace_hot_pixelsCB.Text = "Enable Hot Pixel Replacement";
		this.replace_hot_pixelsCB.UseVisualStyleBackColor = true;
		this.replace_hot_pixelsCB.CheckedChanged += new System.EventHandler(this.handle_dark_file_enables);
		this.flat_frame_calibrationCB.AutoSize = true;
		this.flat_frame_calibrationCB.Checked = true;
		this.flat_frame_calibrationCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.flat_frame_calibrationCB.Location = new System.Drawing.Point(6, 20);
		this.flat_frame_calibrationCB.Name = "flat_frame_calibrationCB";
		this.flat_frame_calibrationCB.Size = new System.Drawing.Size(162, 17);
		this.flat_frame_calibrationCB.TabIndex = 0;
		this.flat_frame_calibrationCB.Text = "Enable Flat Frame Correction";
		this.flat_frame_calibrationCB.UseVisualStyleBackColor = true;
		this.flat_frame_calibrationCB.CheckedChanged += new System.EventHandler(this.handle_flat_file_enables);
		this.flat_dark_frame_subtractionCB.AutoSize = true;
		this.flat_dark_frame_subtractionCB.Checked = true;
		this.flat_dark_frame_subtractionCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.flat_dark_frame_subtractionCB.Location = new System.Drawing.Point(6, 20);
		this.flat_dark_frame_subtractionCB.Name = "flat_dark_frame_subtractionCB";
		this.flat_dark_frame_subtractionCB.Size = new System.Drawing.Size(194, 17);
		this.flat_dark_frame_subtractionCB.TabIndex = 0;
		this.flat_dark_frame_subtractionCB.Text = "Enable Flat Dark Frame Subtraction";
		this.flat_dark_frame_subtractionCB.UseVisualStyleBackColor = true;
		this.flat_dark_frame_subtractionCB.CheckedChanged += new System.EventHandler(this.flat_dark_stbtraction_cb_changed);
		this.flat_data_has_bayerCB.AutoSize = true;
		this.flat_data_has_bayerCB.Location = new System.Drawing.Point(174, 19);
		this.flat_data_has_bayerCB.Name = "flat_data_has_bayerCB";
		this.flat_data_has_bayerCB.Size = new System.Drawing.Size(218, 17);
		this.flat_data_has_bayerCB.TabIndex = 2;
		this.flat_data_has_bayerCB.Text = "Data Is Monochrome WIth Bayer Pattern";
		this.flat_data_has_bayerCB.UseVisualStyleBackColor = true;
		this.enablestabilisationCB.AutoSize = true;
		this.enablestabilisationCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.enablestabilisationCB.Enabled = false;
		this.enablestabilisationCB.Location = new System.Drawing.Point(3, 33);
		this.enablestabilisationCB.Name = "enablestabilisationCB";
		this.enablestabilisationCB.Size = new System.Drawing.Size(418, 17);
		this.enablestabilisationCB.TabIndex = 1;
		this.enablestabilisationCB.Text = "Enable Surface Stabilisation";
		this.enablestabilisationCB.UseVisualStyleBackColor = true;
		this.aoi_enableCB.AutoSize = true;
		this.aoi_enableCB.Location = new System.Drawing.Point(3, 19);
		this.aoi_enableCB.Name = "aoi_enableCB";
		this.aoi_enableCB.Size = new System.Drawing.Size(136, 17);
		this.aoi_enableCB.TabIndex = 29;
		this.aoi_enableCB.Text = "Enable Area Of Interest";
		this.aoi_enableCB.UseVisualStyleBackColor = true;
		this.aoi_enableCB.CheckedChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.aoi_rejectCB.AutoSize = true;
		this.aoi_rejectCB.Checked = true;
		this.aoi_rejectCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.aoi_rejectCB.Location = new System.Drawing.Point(3, 94);
		this.aoi_rejectCB.Name = "aoi_rejectCB";
		this.aoi_rejectCB.Size = new System.Drawing.Size(258, 17);
		this.aoi_rejectCB.TabIndex = 23;
		this.aoi_rejectCB.Text = "Reject Frames Without Complete Area Of Interest";
		this.aoi_rejectCB.UseVisualStyleBackColor = true;
		this.afb_rejectCB.AutoSize = true;
		this.afb_rejectCB.Checked = true;
		this.afb_rejectCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.afb_rejectCB.Location = new System.Drawing.Point(5, 71);
		this.afb_rejectCB.Name = "afb_rejectCB";
		this.afb_rejectCB.Size = new System.Drawing.Size(210, 17);
		this.afb_rejectCB.TabIndex = 23;
		this.afb_rejectCB.Text = "Reject Frames Without Anchor Feature";
		this.afb_rejectCB.UseVisualStyleBackColor = true;
		this.stabilise_surfaceRB.AutoSize = true;
		this.stabilise_surfaceRB.Location = new System.Drawing.Point(196, 19);
		this.stabilise_surfaceRB.Name = "stabilise_surfaceRB";
		this.stabilise_surfaceRB.Size = new System.Drawing.Size(101, 17);
		this.stabilise_surfaceRB.TabIndex = 2;
		this.stabilise_surfaceRB.Text = "Surface Feature";
		this.stabilise_surfaceRB.UseVisualStyleBackColor = true;
		this.stabilise_surfaceRB.CheckedChanged += new System.EventHandler(this.stabilisation_CheckedChanged_event_handler);
		this.keepimagefilenamesCB.AutoSize = true;
		this.keepimagefilenamesCB.Checked = true;
		this.keepimagefilenamesCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.keepimagefilenamesCB.Location = new System.Drawing.Point(6, 18);
		this.keepimagefilenamesCB.Name = "keepimagefilenamesCB";
		this.keepimagefilenamesCB.Size = new System.Drawing.Size(171, 17);
		this.keepimagefilenamesCB.TabIndex = 19;
		this.keepimagefilenamesCB.Text = "Keep Original Image Filenames";
		this.keepimagefilenamesCB.UseVisualStyleBackColor = true;
		this.enableSurfaceTrackingCB.AutoSize = true;
		this.enableSurfaceTrackingCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.enableSurfaceTrackingCB.Location = new System.Drawing.Point(3, 16);
		this.enableSurfaceTrackingCB.Name = "enableSurfaceTrackingCB";
		this.enableSurfaceTrackingCB.Size = new System.Drawing.Size(418, 17);
		this.enableSurfaceTrackingCB.TabIndex = 25;
		this.enableSurfaceTrackingCB.Text = "Enable Surface Feature Tracking";
		this.enableSurfaceTrackingCB.UseVisualStyleBackColor = true;
		this.enableSurfaceTrackingCB.CheckedChanged += new System.EventHandler(this.enableSurfaceTrackingCB_CheckedChanged);
		this.gif_border_tolNUD.Location = new System.Drawing.Point(194, 48);
		this.gif_border_tolNUD.Maximum = new decimal(new int[4] { 20, 0, 0, 0 });
		this.gif_border_tolNUD.Name = "gif_border_tolNUD";
		this.gif_border_tolNUD.Size = new System.Drawing.Size(44, 20);
		this.gif_border_tolNUD.TabIndex = 14;
		this.gif_trans_pixel_tolNUD.Location = new System.Drawing.Point(194, 72);
		this.gif_trans_pixel_tolNUD.Maximum = new decimal(new int[4] { 20, 0, 0, 0 });
		this.gif_trans_pixel_tolNUD.Name = "gif_trans_pixel_tolNUD";
		this.gif_trans_pixel_tolNUD.Size = new System.Drawing.Size(44, 20);
		this.gif_trans_pixel_tolNUD.TabIndex = 15;
		this.gif_trans_pixel_tolNUD.Value = new decimal(new int[4] { 4, 0, 0, 0 });
		this.gif_bit_depthNUD.Location = new System.Drawing.Point(194, 97);
		this.gif_bit_depthNUD.Maximum = new decimal(new int[4] { 7, 0, 0, 0 });
		this.gif_bit_depthNUD.Minimum = new decimal(new int[4] { 6, 0, 0, 0 });
		this.gif_bit_depthNUD.Name = "gif_bit_depthNUD";
		this.gif_bit_depthNUD.Size = new System.Drawing.Size(44, 20);
		this.gif_bit_depthNUD.TabIndex = 16;
		this.gif_bit_depthNUD.Value = new decimal(new int[4] { 6, 0, 0, 0 });
		this.gif_lossy_comp_levelNUD.Location = new System.Drawing.Point(194, 120);
		this.gif_lossy_comp_levelNUD.Maximum = new decimal(new int[4] { 20, 0, 0, 0 });
		this.gif_lossy_comp_levelNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.gif_lossy_comp_levelNUD.Name = "gif_lossy_comp_levelNUD";
		this.gif_lossy_comp_levelNUD.Size = new System.Drawing.Size(44, 20);
		this.gif_lossy_comp_levelNUD.TabIndex = 17;
		this.gif_lossy_comp_levelNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.winjupossubPANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.winjupossubPANEL.BackColor = System.Drawing.Color.WhiteSmoke;
		this.winjupossubPANEL.Controls.Add(this.groupBox1);
		this.winjupossubPANEL.Controls.Add(this.inputframespersecondNUD);
		this.winjupossubPANEL.Controls.Add(this.label19);
		this.winjupossubPANEL.Controls.Add(this.date_time_extract_conv_utCB);
		this.winjupossubPANEL.Dock = System.Windows.Forms.DockStyle.Top;
		this.winjupossubPANEL.Enabled = false;
		this.winjupossubPANEL.Location = new System.Drawing.Point(3, 33);
		this.winjupossubPANEL.Name = "winjupossubPANEL";
		this.winjupossubPANEL.Size = new System.Drawing.Size(413, 139);
		this.winjupossubPANEL.TabIndex = 20;
		this.winjupossubPANEL.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.groupBox1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.groupBox1.Controls.Add(this.wjpos_rememberCB);
		this.groupBox1.Controls.Add(this.wjpos_extracted2LAB);
		this.groupBox1.Controls.Add(this.inputdatetimeformatCB);
		this.groupBox1.Controls.Add(this.wjpos_extractedLAB);
		this.groupBox1.Controls.Add(this.wjpos_filename2LAB);
		this.groupBox1.Controls.Add(this.datetimeTB);
		this.groupBox1.Controls.Add(this.wjpos_filenameLAB);
		this.groupBox1.Location = new System.Drawing.Point(2, 26);
		this.groupBox1.Name = "groupBox1";
		this.groupBox1.Size = new System.Drawing.Size(408, 79);
		this.groupBox1.TabIndex = 30;
		this.groupBox1.TabStop = false;
		this.groupBox1.Text = "Input Filename Date/Time Format";
		this.wjpos_extracted2LAB.AutoSize = true;
		this.wjpos_extracted2LAB.Location = new System.Drawing.Point(134, 55);
		this.wjpos_extracted2LAB.Name = "wjpos_extracted2LAB";
		this.wjpos_extracted2LAB.Size = new System.Drawing.Size(33, 13);
		this.wjpos_extracted2LAB.TabIndex = 29;
		this.wjpos_extracted2LAB.Text = "None";
		this.wjpos_extractedLAB.AutoSize = true;
		this.wjpos_extractedLAB.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.wjpos_extractedLAB.Location = new System.Drawing.Point(6, 55);
		this.wjpos_extractedLAB.Name = "wjpos_extractedLAB";
		this.wjpos_extractedLAB.Size = new System.Drawing.Size(129, 13);
		this.wjpos_extractedLAB.TabIndex = 27;
		this.wjpos_extractedLAB.Text = "Extracted Date/Time:";
		this.wjpos_filename2LAB.AutoSize = true;
		this.wjpos_filename2LAB.Location = new System.Drawing.Point(65, 40);
		this.wjpos_filename2LAB.Name = "wjpos_filename2LAB";
		this.wjpos_filename2LAB.Size = new System.Drawing.Size(33, 13);
		this.wjpos_filename2LAB.TabIndex = 28;
		this.wjpos_filename2LAB.Text = "None";
		this.wjpos_filenameLAB.AutoSize = true;
		this.wjpos_filenameLAB.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.wjpos_filenameLAB.Location = new System.Drawing.Point(6, 40);
		this.wjpos_filenameLAB.Name = "wjpos_filenameLAB";
		this.wjpos_filenameLAB.Size = new System.Drawing.Size(61, 13);
		this.wjpos_filenameLAB.TabIndex = 26;
		this.wjpos_filenameLAB.Text = "Filename:";
		this.minpixelvalueGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.minpixelvalueGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.minpixelvalueGB.Controls.Add(this.groupBox6);
		this.minpixelvalueGB.Controls.Add(this.minsizeNUD);
		this.minpixelvalueGB.Controls.Add(this.label1);
		this.minpixelvalueGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.minpixelvalueGB.Enabled = false;
		this.minpixelvalueGB.Location = new System.Drawing.Point(3, 33);
		this.minpixelvalueGB.Name = "minpixelvalueGB";
		this.minpixelvalueGB.Size = new System.Drawing.Size(418, 109);
		this.minpixelvalueGB.TabIndex = 3;
		this.minpixelvalueGB.TabStop = false;
		this.minpixelvalueGB.Text = "Object Detection Parameters";
		this.minpixelvalueGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.groupBox6.Controls.Add(this.autothresholdCB);
		this.groupBox6.Controls.Add(this.test_detect_thresholdBUT);
		this.groupBox6.Controls.Add(this.thresholdNUD);
		this.groupBox6.Controls.Add(this.label2);
		this.groupBox6.Location = new System.Drawing.Point(2, 44);
		this.groupBox6.Name = "groupBox6";
		this.groupBox6.Size = new System.Drawing.Size(413, 61);
		this.groupBox6.TabIndex = 8;
		this.groupBox6.TabStop = false;
		this.groupBox6.Text = "Object Detection Threshold";
		this.flip_rotateGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.flip_rotateGB.BackColor = System.Drawing.Color.White;
		this.flip_rotateGB.Controls.Add(this.rotatePB);
		this.flip_rotateGB.Controls.Add(this.rotationCB);
		this.flip_rotateGB.Controls.Add(this.flip_yCB);
		this.flip_rotateGB.Controls.Add(this.flip_xCB);
		this.flip_rotateGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.flip_rotateGB.Location = new System.Drawing.Point(0, 305);
		this.flip_rotateGB.Name = "flip_rotateGB";
		this.flip_rotateGB.Size = new System.Drawing.Size(420, 65);
		this.flip_rotateGB.TabIndex = 17;
		this.flip_rotateGB.TabStop = false;
		this.flip_rotateGB.Text = "Flip And Rotate";
		this.rotatePB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.rotatePB.Image = PIPP_Application.Properties.Resources.rotate;
		this.rotatePB.Location = new System.Drawing.Point(395, 11);
		this.rotatePB.Name = "rotatePB";
		this.rotatePB.Size = new System.Drawing.Size(20, 20);
		this.rotatePB.TabIndex = 17;
		this.rotatePB.TabStop = false;
		this.rotationCB.DisplayMember = "0";
		this.rotationCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.rotationCB.FormattingEnabled = true;
		this.rotationCB.Items.AddRange(new object[4] { "No Rotation", "Rotate 90 Degrees Clockwise", "Rotate 90 Degrees Anticlockwise", "Rotate 180 Degrees" });
		this.rotationCB.Location = new System.Drawing.Point(3, 36);
		this.rotationCB.MaxDropDownItems = 10;
		this.rotationCB.Name = "rotationCB";
		this.rotationCB.Size = new System.Drawing.Size(203, 21);
		this.rotationCB.TabIndex = 16;
		this.rotationCB.ValueMember = "0";
		this.flip_yCB.AutoSize = true;
		this.flip_yCB.Location = new System.Drawing.Point(125, 13);
		this.flip_yCB.Name = "flip_yCB";
		this.flip_yCB.Size = new System.Drawing.Size(87, 17);
		this.flip_yCB.TabIndex = 1;
		this.flip_yCB.Text = "Flip Vertically";
		this.flip_yCB.UseVisualStyleBackColor = true;
		this.flip_xCB.AutoSize = true;
		this.flip_xCB.Location = new System.Drawing.Point(3, 13);
		this.flip_xCB.Name = "flip_xCB";
		this.flip_xCB.Size = new System.Drawing.Size(99, 17);
		this.flip_xCB.TabIndex = 0;
		this.flip_xCB.Text = "Flip Horizontally";
		this.flip_xCB.UseVisualStyleBackColor = true;
		this.label30.AutoSize = true;
		this.label30.Location = new System.Drawing.Point(200, 9);
		this.label30.Name = "label30";
		this.label30.Size = new System.Drawing.Size(183, 13);
		this.label30.TabIndex = 11;
		this.label30.Text = "Edge In Shadow (Lunar Images Only)";
		this.animation_repeat_allLAB.AutoSize = true;
		this.animation_repeat_allLAB.Enabled = false;
		this.animation_repeat_allLAB.Location = new System.Drawing.Point(74, 27);
		this.animation_repeat_allLAB.Name = "animation_repeat_allLAB";
		this.animation_repeat_allLAB.Size = new System.Drawing.Size(200, 13);
		this.animation_repeat_allLAB.TabIndex = 2;
		this.animation_repeat_allLAB.Text = "Number Of Complete Sequence Repeats";
		this.animation_repeat_allNUD.Enabled = false;
		this.animation_repeat_allNUD.Location = new System.Drawing.Point(24, 25);
		this.animation_repeat_allNUD.Maximum = new decimal(new int[4] { 256, 0, 0, 0 });
		this.animation_repeat_allNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.animation_repeat_allNUD.Name = "animation_repeat_allNUD";
		this.animation_repeat_allNUD.Size = new System.Drawing.Size(44, 20);
		this.animation_repeat_allNUD.TabIndex = 1;
		this.animation_repeat_allNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.repeat_forward_lastLAB.AutoSize = true;
		this.repeat_forward_lastLAB.Enabled = false;
		this.repeat_forward_lastLAB.Location = new System.Drawing.Point(74, 28);
		this.repeat_forward_lastLAB.Name = "repeat_forward_lastLAB";
		this.repeat_forward_lastLAB.Size = new System.Drawing.Size(170, 13);
		this.repeat_forward_lastLAB.TabIndex = 15;
		this.repeat_forward_lastLAB.Text = "Number Of Frame Times To Pause";
		this.repeat_forward_lastNUD.Enabled = false;
		this.repeat_forward_lastNUD.Location = new System.Drawing.Point(24, 26);
		this.repeat_forward_lastNUD.Maximum = new decimal(new int[4] { 256, 0, 0, 0 });
		this.repeat_forward_lastNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.repeat_forward_lastNUD.Name = "repeat_forward_lastNUD";
		this.repeat_forward_lastNUD.Size = new System.Drawing.Size(44, 20);
		this.repeat_forward_lastNUD.TabIndex = 14;
		this.repeat_forward_lastNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.repeat_reverse_lastLAB.AutoSize = true;
		this.repeat_reverse_lastLAB.Enabled = false;
		this.repeat_reverse_lastLAB.Location = new System.Drawing.Point(74, 28);
		this.repeat_reverse_lastLAB.Name = "repeat_reverse_lastLAB";
		this.repeat_reverse_lastLAB.Size = new System.Drawing.Size(170, 13);
		this.repeat_reverse_lastLAB.TabIndex = 15;
		this.repeat_reverse_lastLAB.Text = "Number Of Frame Times To Pause";
		this.repeat_reverse_lastNUD.Enabled = false;
		this.repeat_reverse_lastNUD.Location = new System.Drawing.Point(24, 26);
		this.repeat_reverse_lastNUD.Maximum = new decimal(new int[4] { 256, 0, 0, 0 });
		this.repeat_reverse_lastNUD.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.repeat_reverse_lastNUD.Name = "repeat_reverse_lastNUD";
		this.repeat_reverse_lastNUD.Size = new System.Drawing.Size(44, 20);
		this.repeat_reverse_lastNUD.TabIndex = 14;
		this.repeat_reverse_lastNUD.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.forward_playGB.BackColor = System.Drawing.Color.White;
		this.forward_playGB.Controls.Add(this.pictureBox6);
		this.forward_playGB.Controls.Add(this.panel3);
		this.forward_playGB.Controls.Add(this.animation_forwardCB);
		this.forward_playGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.forward_playGB.Location = new System.Drawing.Point(0, 0);
		this.forward_playGB.Name = "forward_playGB";
		this.forward_playGB.Size = new System.Drawing.Size(420, 89);
		this.forward_playGB.TabIndex = 18;
		this.forward_playGB.TabStop = false;
		this.forward_playGB.Text = "Forward Play";
		this.forward_playGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.pictureBox6.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox6.Image = PIPP_Application.Properties.Resources.play;
		this.pictureBox6.Location = new System.Drawing.Point(395, 11);
		this.pictureBox6.Name = "pictureBox6";
		this.pictureBox6.Size = new System.Drawing.Size(20, 20);
		this.pictureBox6.TabIndex = 13;
		this.pictureBox6.TabStop = false;
		this.panel3.Controls.Add(this.repeat_forward_lastLAB);
		this.panel3.Controls.Add(this.repeat_forward_lastNUD);
		this.panel3.Controls.Add(this.repeat_forward_lastCB);
		this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel3.Location = new System.Drawing.Point(3, 33);
		this.panel3.Margin = new System.Windows.Forms.Padding(0);
		this.panel3.Name = "panel3";
		this.panel3.Size = new System.Drawing.Size(414, 51);
		this.panel3.TabIndex = 0;
		this.repeat_forward_lastCB.AutoSize = true;
		this.repeat_forward_lastCB.Location = new System.Drawing.Point(0, 3);
		this.repeat_forward_lastCB.Name = "repeat_forward_lastCB";
		this.repeat_forward_lastCB.Size = new System.Drawing.Size(216, 17);
		this.repeat_forward_lastCB.TabIndex = 17;
		this.repeat_forward_lastCB.Text = "Pause Final frame Of Each Forward Play";
		this.repeat_forward_lastCB.UseVisualStyleBackColor = true;
		this.repeat_forward_lastCB.CheckedChanged += new System.EventHandler(this.repeat_forward_lastCB_CheckedChanged);
		this.animation_forwardCB.AutoSize = true;
		this.animation_forwardCB.Checked = true;
		this.animation_forwardCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.animation_forwardCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.animation_forwardCB.Location = new System.Drawing.Point(3, 16);
		this.animation_forwardCB.Name = "animation_forwardCB";
		this.animation_forwardCB.Size = new System.Drawing.Size(414, 17);
		this.animation_forwardCB.TabIndex = 20;
		this.animation_forwardCB.Text = "Play All Frames In Forward Order";
		this.animation_forwardCB.UseVisualStyleBackColor = true;
		this.animation_forwardCB.CheckedChanged += new System.EventHandler(this.animation_forwardCB_CheckedChanged);
		this.outputdirectoryFBD.Description = "Select Output Directory";
		this.pipp_process.StartInfo.Domain = "";
		this.pipp_process.StartInfo.LoadUserProfile = false;
		this.pipp_process.StartInfo.Password = null;
		this.pipp_process.StartInfo.StandardErrorEncoding = null;
		this.pipp_process.StartInfo.StandardOutputEncoding = null;
		this.pipp_process.StartInfo.UserName = "";
		this.pipp_process.SynchronizingObject = this;
		this.pipp_process.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(this.pipp_process_OutputDataReceived);
		this.pipp_process.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler(this.pipp_process_ErrorDataReceived);
		this.pipp_process.Exited += new System.EventHandler(this.pipp_process_Exited);
		this.saveoptionsFD.DefaultExt = "pof";
		this.tabPage6.BackColor = System.Drawing.Color.White;
		this.tabPage6.Controls.Add(this.splitContainer6);
		this.tabPage6.Location = new System.Drawing.Point(4, 22);
		this.tabPage6.Name = "tabPage6";
		this.tabPage6.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage6.Size = new System.Drawing.Size(854, 437);
		this.tabPage6.TabIndex = 5;
		this.tabPage6.Text = "Do Processing";
		this.splitContainer6.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer6.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
		this.splitContainer6.IsSplitterFixed = true;
		this.splitContainer6.Location = new System.Drawing.Point(3, 3);
		this.splitContainer6.Name = "splitContainer6";
		this.splitContainer6.Panel1.Controls.Add(this.mainprocessingGB);
		this.splitContainer6.Panel1.Controls.Add(this.calibrationGB);
		this.splitContainer6.Panel1.Controls.Add(this.statusGB);
		this.splitContainer6.Panel1.Controls.Add(this.controlGB);
		this.splitContainer6.Panel2.AutoScroll = true;
		this.splitContainer6.Panel2.Controls.Add(this.outputGB);
		this.splitContainer6.Size = new System.Drawing.Size(848, 431);
		this.splitContainer6.SplitterDistance = 268;
		this.splitContainer6.TabIndex = 0;
		this.mainprocessingGB.AutoSize = true;
		this.mainprocessingGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.mainprocessingGB.Controls.Add(this.anim_progPAN);
		this.mainprocessingGB.Controls.Add(this.qual_progressPAN);
		this.mainprocessingGB.Controls.Add(this.panel7);
		this.mainprocessingGB.Controls.Add(this.panel8);
		this.mainprocessingGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.mainprocessingGB.Location = new System.Drawing.Point(0, 283);
		this.mainprocessingGB.Name = "mainprocessingGB";
		this.mainprocessingGB.Size = new System.Drawing.Size(268, 171);
		this.mainprocessingGB.TabIndex = 11;
		this.mainprocessingGB.TabStop = false;
		this.mainprocessingGB.Text = "Processing";
		this.anim_progPAN.Controls.Add(this.animframeLAB);
		this.anim_progPAN.Controls.Add(this.animPB);
		this.anim_progPAN.Controls.Add(this.animLAB);
		this.anim_progPAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.anim_progPAN.Location = new System.Drawing.Point(3, 130);
		this.anim_progPAN.Margin = new System.Windows.Forms.Padding(0);
		this.anim_progPAN.Name = "anim_progPAN";
		this.anim_progPAN.Size = new System.Drawing.Size(262, 38);
		this.anim_progPAN.TabIndex = 25;
		this.anim_progPAN.Visible = false;
		this.animframeLAB.AutoSize = true;
		this.animframeLAB.Location = new System.Drawing.Point(0, 4);
		this.animframeLAB.Name = "animframeLAB";
		this.animframeLAB.Size = new System.Drawing.Size(125, 13);
		this.animframeLAB.TabIndex = 24;
		this.animframeLAB.Text = "Creating Animation Video";
		this.animPB.Location = new System.Drawing.Point(3, 20);
		this.animPB.MarqueeAnimationSpeed = 1000;
		this.animPB.Name = "animPB";
		this.animPB.Size = new System.Drawing.Size(152, 15);
		this.animPB.TabIndex = 22;
		this.animLAB.AutoSize = true;
		this.animLAB.Location = new System.Drawing.Point(161, 20);
		this.animLAB.Name = "animLAB";
		this.animLAB.Size = new System.Drawing.Size(21, 13);
		this.animLAB.TabIndex = 23;
		this.animLAB.Text = "0%";
		this.qual_progressPAN.Controls.Add(this.qualityPB);
		this.qual_progressPAN.Controls.Add(this.qualityLAB);
		this.qual_progressPAN.Controls.Add(this.qsframeLAB);
		this.qual_progressPAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.qual_progressPAN.Location = new System.Drawing.Point(3, 92);
		this.qual_progressPAN.Margin = new System.Windows.Forms.Padding(0);
		this.qual_progressPAN.Name = "qual_progressPAN";
		this.qual_progressPAN.Size = new System.Drawing.Size(262, 38);
		this.qual_progressPAN.TabIndex = 26;
		this.qual_progressPAN.Visible = false;
		this.qualityPB.Location = new System.Drawing.Point(3, 20);
		this.qualityPB.MarqueeAnimationSpeed = 1000;
		this.qualityPB.Name = "qualityPB";
		this.qualityPB.Size = new System.Drawing.Size(152, 15);
		this.qualityPB.TabIndex = 1;
		this.qualityLAB.AutoSize = true;
		this.qualityLAB.Location = new System.Drawing.Point(161, 20);
		this.qualityLAB.Name = "qualityLAB";
		this.qualityLAB.Size = new System.Drawing.Size(21, 13);
		this.qualityLAB.TabIndex = 2;
		this.qualityLAB.Text = "0%";
		this.qsframeLAB.AutoSize = true;
		this.qsframeLAB.Location = new System.Drawing.Point(0, 4);
		this.qsframeLAB.Name = "qsframeLAB";
		this.qsframeLAB.Size = new System.Drawing.Size(130, 13);
		this.qsframeLAB.TabIndex = 20;
		this.qsframeLAB.Text = "Sorting 0 frames by quality";
		this.panel7.Controls.Add(this.processframesLAB);
		this.panel7.Controls.Add(this.processPB);
		this.panel7.Controls.Add(this.progressLAB);
		this.panel7.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel7.Location = new System.Drawing.Point(3, 54);
		this.panel7.Margin = new System.Windows.Forms.Padding(0);
		this.panel7.Name = "panel7";
		this.panel7.Size = new System.Drawing.Size(262, 38);
		this.panel7.TabIndex = 26;
		this.processframesLAB.AutoSize = true;
		this.processframesLAB.Location = new System.Drawing.Point(3, 4);
		this.processframesLAB.Name = "processframesLAB";
		this.processframesLAB.Size = new System.Drawing.Size(102, 13);
		this.processframesLAB.TabIndex = 16;
		this.processframesLAB.Text = "Processing 0 frames";
		this.processPB.Location = new System.Drawing.Point(3, 20);
		this.processPB.MarqueeAnimationSpeed = 1000;
		this.processPB.Name = "processPB";
		this.processPB.Size = new System.Drawing.Size(152, 15);
		this.processPB.TabIndex = 1;
		this.progressLAB.AutoSize = true;
		this.progressLAB.Location = new System.Drawing.Point(161, 20);
		this.progressLAB.Name = "progressLAB";
		this.progressLAB.Size = new System.Drawing.Size(21, 13);
		this.progressLAB.TabIndex = 2;
		this.progressLAB.Text = "0%";
		this.panel8.Controls.Add(this.fileLAB);
		this.panel8.Controls.Add(this.processfilePB);
		this.panel8.Controls.Add(this.processfileLAB);
		this.panel8.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel8.Location = new System.Drawing.Point(3, 16);
		this.panel8.Margin = new System.Windows.Forms.Padding(0);
		this.panel8.Name = "panel8";
		this.panel8.Size = new System.Drawing.Size(262, 38);
		this.panel8.TabIndex = 26;
		this.fileLAB.AutoSize = true;
		this.fileLAB.Location = new System.Drawing.Point(3, 4);
		this.fileLAB.Name = "fileLAB";
		this.fileLAB.Size = new System.Drawing.Size(53, 13);
		this.fileLAB.TabIndex = 21;
		this.fileLAB.Text = "File: none";
		this.processfilePB.Location = new System.Drawing.Point(3, 20);
		this.processfilePB.MarqueeAnimationSpeed = 1000;
		this.processfilePB.Name = "processfilePB";
		this.processfilePB.Size = new System.Drawing.Size(152, 15);
		this.processfilePB.TabIndex = 17;
		this.processfileLAB.AutoSize = true;
		this.processfileLAB.Location = new System.Drawing.Point(161, 20);
		this.processfileLAB.Name = "processfileLAB";
		this.processfileLAB.Size = new System.Drawing.Size(53, 13);
		this.processfileLAB.TabIndex = 19;
		this.processfileLAB.Text = "File 0 of 0";
		this.calibrationGB.AutoSize = true;
		this.calibrationGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.calibrationGB.Controls.Add(this.flat_stacking_progress_PAN);
		this.calibrationGB.Controls.Add(this.flat_dark_stacking_progress_PAN);
		this.calibrationGB.Controls.Add(this.dark_stacking_progress_PAN);
		this.calibrationGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.calibrationGB.Location = new System.Drawing.Point(0, 150);
		this.calibrationGB.Name = "calibrationGB";
		this.calibrationGB.Size = new System.Drawing.Size(268, 133);
		this.calibrationGB.TabIndex = 12;
		this.calibrationGB.TabStop = false;
		this.calibrationGB.Text = "Calibration";
		this.calibrationGB.Visible = false;
		this.flat_stacking_progress_PAN.Controls.Add(this.flat_frame_stackingLAB);
		this.flat_stacking_progress_PAN.Controls.Add(this.flat_stackingPB);
		this.flat_stacking_progress_PAN.Controls.Add(this.flat_stackingLAB);
		this.flat_stacking_progress_PAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.flat_stacking_progress_PAN.Location = new System.Drawing.Point(3, 92);
		this.flat_stacking_progress_PAN.Margin = new System.Windows.Forms.Padding(0);
		this.flat_stacking_progress_PAN.Name = "flat_stacking_progress_PAN";
		this.flat_stacking_progress_PAN.Size = new System.Drawing.Size(262, 38);
		this.flat_stacking_progress_PAN.TabIndex = 32;
		this.flat_stacking_progress_PAN.Visible = false;
		this.flat_frame_stackingLAB.AutoSize = true;
		this.flat_frame_stackingLAB.Location = new System.Drawing.Point(3, 4);
		this.flat_frame_stackingLAB.Name = "flat_frame_stackingLAB";
		this.flat_frame_stackingLAB.Size = new System.Drawing.Size(109, 13);
		this.flat_frame_stackingLAB.TabIndex = 16;
		this.flat_frame_stackingLAB.Text = "Stacking 0 flat frames";
		this.flat_stackingPB.Location = new System.Drawing.Point(3, 20);
		this.flat_stackingPB.MarqueeAnimationSpeed = 1000;
		this.flat_stackingPB.Name = "flat_stackingPB";
		this.flat_stackingPB.Size = new System.Drawing.Size(152, 15);
		this.flat_stackingPB.TabIndex = 1;
		this.flat_stackingLAB.AutoSize = true;
		this.flat_stackingLAB.Location = new System.Drawing.Point(161, 20);
		this.flat_stackingLAB.Name = "flat_stackingLAB";
		this.flat_stackingLAB.Size = new System.Drawing.Size(21, 13);
		this.flat_stackingLAB.TabIndex = 2;
		this.flat_stackingLAB.Text = "0%";
		this.flat_dark_stacking_progress_PAN.Controls.Add(this.flat_dark_frame_stackingLAB);
		this.flat_dark_stacking_progress_PAN.Controls.Add(this.flat_dark_stackingPB);
		this.flat_dark_stacking_progress_PAN.Controls.Add(this.flat_dark_stackingLAB);
		this.flat_dark_stacking_progress_PAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.flat_dark_stacking_progress_PAN.Location = new System.Drawing.Point(3, 54);
		this.flat_dark_stacking_progress_PAN.Margin = new System.Windows.Forms.Padding(0);
		this.flat_dark_stacking_progress_PAN.Name = "flat_dark_stacking_progress_PAN";
		this.flat_dark_stacking_progress_PAN.Size = new System.Drawing.Size(262, 38);
		this.flat_dark_stacking_progress_PAN.TabIndex = 31;
		this.flat_dark_stacking_progress_PAN.Visible = false;
		this.flat_dark_frame_stackingLAB.AutoSize = true;
		this.flat_dark_frame_stackingLAB.Location = new System.Drawing.Point(3, 4);
		this.flat_dark_frame_stackingLAB.Name = "flat_dark_frame_stackingLAB";
		this.flat_dark_frame_stackingLAB.Size = new System.Drawing.Size(133, 13);
		this.flat_dark_frame_stackingLAB.TabIndex = 16;
		this.flat_dark_frame_stackingLAB.Text = "Stacking 0 flat dark frames";
		this.flat_dark_stackingPB.Location = new System.Drawing.Point(3, 20);
		this.flat_dark_stackingPB.MarqueeAnimationSpeed = 1000;
		this.flat_dark_stackingPB.Name = "flat_dark_stackingPB";
		this.flat_dark_stackingPB.Size = new System.Drawing.Size(152, 15);
		this.flat_dark_stackingPB.TabIndex = 1;
		this.flat_dark_stackingLAB.AutoSize = true;
		this.flat_dark_stackingLAB.Location = new System.Drawing.Point(161, 20);
		this.flat_dark_stackingLAB.Name = "flat_dark_stackingLAB";
		this.flat_dark_stackingLAB.Size = new System.Drawing.Size(21, 13);
		this.flat_dark_stackingLAB.TabIndex = 2;
		this.flat_dark_stackingLAB.Text = "0%";
		this.dark_stacking_progress_PAN.Controls.Add(this.dark_frame_stackingLAB);
		this.dark_stacking_progress_PAN.Controls.Add(this.dark_stackingPB);
		this.dark_stacking_progress_PAN.Controls.Add(this.dark_stackingLAB);
		this.dark_stacking_progress_PAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.dark_stacking_progress_PAN.Location = new System.Drawing.Point(3, 16);
		this.dark_stacking_progress_PAN.Margin = new System.Windows.Forms.Padding(0);
		this.dark_stacking_progress_PAN.Name = "dark_stacking_progress_PAN";
		this.dark_stacking_progress_PAN.Size = new System.Drawing.Size(262, 38);
		this.dark_stacking_progress_PAN.TabIndex = 29;
		this.dark_stacking_progress_PAN.Visible = false;
		this.dark_frame_stackingLAB.AutoSize = true;
		this.dark_frame_stackingLAB.Location = new System.Drawing.Point(3, 4);
		this.dark_frame_stackingLAB.Name = "dark_frame_stackingLAB";
		this.dark_frame_stackingLAB.Size = new System.Drawing.Size(116, 13);
		this.dark_frame_stackingLAB.TabIndex = 16;
		this.dark_frame_stackingLAB.Text = "Stacking 0 dark frames";
		this.dark_stackingPB.Location = new System.Drawing.Point(3, 20);
		this.dark_stackingPB.MarqueeAnimationSpeed = 1000;
		this.dark_stackingPB.Name = "dark_stackingPB";
		this.dark_stackingPB.Size = new System.Drawing.Size(152, 15);
		this.dark_stackingPB.TabIndex = 1;
		this.dark_stackingLAB.AutoSize = true;
		this.dark_stackingLAB.Location = new System.Drawing.Point(161, 20);
		this.dark_stackingLAB.Name = "dark_stackingLAB";
		this.dark_stackingLAB.Size = new System.Drawing.Size(21, 13);
		this.dark_stackingLAB.TabIndex = 2;
		this.dark_stackingLAB.Text = "0%";
		this.statusGB.BackColor = System.Drawing.Color.Cornsilk;
		this.statusGB.Controls.Add(this.statusstateLAB);
		this.statusGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.statusGB.Location = new System.Drawing.Point(0, 112);
		this.statusGB.Name = "statusGB";
		this.statusGB.Size = new System.Drawing.Size(268, 38);
		this.statusGB.TabIndex = 6;
		this.statusGB.TabStop = false;
		this.statusGB.Text = "Status";
		this.statusstateLAB.AutoSize = true;
		this.statusstateLAB.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.statusstateLAB.Location = new System.Drawing.Point(109, 16);
		this.statusstateLAB.Name = "statusstateLAB";
		this.statusstateLAB.Size = new System.Drawing.Size(35, 13);
		this.statusstateLAB.TabIndex = 17;
		this.statusstateLAB.Text = "IDLE";
		this.controlGB.Controls.Add(this.output_folderBTN);
		this.controlGB.Controls.Add(this.open_folderCB);
		this.controlGB.Controls.Add(this.killpippBTN);
		this.controlGB.Controls.Add(this.runBTN);
		this.controlGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.controlGB.Location = new System.Drawing.Point(0, 0);
		this.controlGB.Name = "controlGB";
		this.controlGB.Size = new System.Drawing.Size(268, 112);
		this.controlGB.TabIndex = 7;
		this.controlGB.TabStop = false;
		this.controlGB.Text = "Control";
		this.output_folderBTN.Enabled = false;
		this.output_folderBTN.Location = new System.Drawing.Point(3, 52);
		this.output_folderBTN.Name = "output_folderBTN";
		this.output_folderBTN.Size = new System.Drawing.Size(221, 23);
		this.output_folderBTN.TabIndex = 19;
		this.output_folderBTN.Text = "Open Output Folder";
		this.output_folderBTN.UseVisualStyleBackColor = true;
		this.output_folderBTN.Click += new System.EventHandler(this.output_folderBTN_Click);
		this.open_folderCB.AutoSize = true;
		this.open_folderCB.Location = new System.Drawing.Point(4, 85);
		this.open_folderCB.Name = "open_folderCB";
		this.open_folderCB.Size = new System.Drawing.Size(198, 17);
		this.open_folderCB.TabIndex = 18;
		this.open_folderCB.Text = "Open Output Folder When Complete";
		this.open_folderCB.UseVisualStyleBackColor = true;
		this.killpippBTN.Enabled = false;
		this.killpippBTN.Location = new System.Drawing.Point(119, 19);
		this.killpippBTN.Name = "killpippBTN";
		this.killpippBTN.Size = new System.Drawing.Size(105, 23);
		this.killpippBTN.TabIndex = 1;
		this.killpippBTN.Text = "Cancel Processing";
		this.killpippBTN.UseVisualStyleBackColor = true;
		this.killpippBTN.Click += new System.EventHandler(this.killpippBTN_Click);
		this.runBTN.Location = new System.Drawing.Point(3, 19);
		this.runBTN.Name = "runBTN";
		this.runBTN.Size = new System.Drawing.Size(107, 23);
		this.runBTN.TabIndex = 0;
		this.runBTN.Text = "Start Processing";
		this.runBTN.UseVisualStyleBackColor = true;
		this.runBTN.Click += new System.EventHandler(this.runBTN_Click);
		this.outputGB.BackColor = System.Drawing.Color.White;
		this.outputGB.Controls.Add(this.outputTB);
		this.outputGB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.outputGB.Location = new System.Drawing.Point(0, 0);
		this.outputGB.Name = "outputGB";
		this.outputGB.Size = new System.Drawing.Size(576, 431);
		this.outputGB.TabIndex = 1;
		this.outputGB.TabStop = false;
		this.outputGB.Text = "Output";
		this.outputTB.BackColor = System.Drawing.Color.Cornsilk;
		this.outputTB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.outputTB.ForeColor = System.Drawing.Color.Black;
		this.outputTB.Location = new System.Drawing.Point(3, 16);
		this.outputTB.Multiline = true;
		this.outputTB.Name = "outputTB";
		this.outputTB.ReadOnly = true;
		this.outputTB.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
		this.outputTB.Size = new System.Drawing.Size(570, 412);
		this.outputTB.TabIndex = 0;
		this.tabPage4.AutoScroll = true;
		this.tabPage4.BackColor = System.Drawing.Color.White;
		this.tabPage4.Controls.Add(this.splitContainer5);
		this.tabPage4.Location = new System.Drawing.Point(4, 22);
		this.tabPage4.Name = "tabPage4";
		this.tabPage4.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage4.Size = new System.Drawing.Size(854, 437);
		this.tabPage4.TabIndex = 3;
		this.tabPage4.Text = "Output Options";
		this.splitContainer5.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer5.IsSplitterFixed = true;
		this.splitContainer5.Location = new System.Drawing.Point(3, 3);
		this.splitContainer5.Name = "splitContainer5";
		this.splitContainer5.Panel1.Controls.Add(this.framedropGB);
		this.splitContainer5.Panel1.Controls.Add(this.rgbsplitGB);
		this.splitContainer5.Panel1.Controls.Add(this.prefix_PAN);
		this.splitContainer5.Panel1.Controls.Add(this.outputfiledirectoryGB);
		this.splitContainer5.Panel1.Controls.Add(this.groupBox8);
		this.splitContainer5.Panel2.Controls.Add(this.winjuposGB);
		this.splitContainer5.Panel2.Controls.Add(this.renumberGB);
		this.splitContainer5.Panel2.Controls.Add(this.animimated_gif_optionsGB);
		this.splitContainer5.Panel2.Controls.Add(this.avi_outputGB);
		this.splitContainer5.Size = new System.Drawing.Size(848, 431);
		this.splitContainer5.SplitterDistance = 420;
		this.splitContainer5.TabIndex = 7;
		this.prefix_PAN.Controls.Add(this.groupBox7);
		this.prefix_PAN.Controls.Add(this.outputprefixGB);
		this.prefix_PAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.prefix_PAN.Location = new System.Drawing.Point(0, 223);
		this.prefix_PAN.Name = "prefix_PAN";
		this.prefix_PAN.Size = new System.Drawing.Size(420, 50);
		this.prefix_PAN.TabIndex = 21;
		this.renumberGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.renumberGB.Controls.Add(this.keepimagefilenamesCB);
		this.renumberGB.Controls.Add(this.includequalityvalueCB);
		this.renumberGB.Controls.Add(this.addleadingzerosCB);
		this.renumberGB.Controls.Add(this.norenumberPB);
		this.renumberGB.Controls.Add(this.noframerenumberCB);
		this.renumberGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.renumberGB.Enabled = false;
		this.renumberGB.Location = new System.Drawing.Point(0, 473);
		this.renumberGB.Name = "renumberGB";
		this.renumberGB.Size = new System.Drawing.Size(424, 115);
		this.renumberGB.TabIndex = 15;
		this.renumberGB.TabStop = false;
		this.renumberGB.Text = "Image File Naming";
		this.renumberGB.Visible = false;
		this.renumberGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.norenumberPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.norenumberPB.Image = PIPP_Application.Properties.Resources.norenumber;
		this.norenumberPB.Location = new System.Drawing.Point(399, 11);
		this.norenumberPB.Name = "norenumberPB";
		this.norenumberPB.Size = new System.Drawing.Size(20, 20);
		this.norenumberPB.TabIndex = 16;
		this.norenumberPB.TabStop = false;
		this.animimated_gif_optionsGB.Controls.Add(this.label45);
		this.animimated_gif_optionsGB.Controls.Add(this.gif_quantCB);
		this.animimated_gif_optionsGB.Controls.Add(this.gif_advanced_optionsGB);
		this.animimated_gif_optionsGB.Controls.Add(this.pictureBox15);
		this.animimated_gif_optionsGB.Controls.Add(this.label41);
		this.animimated_gif_optionsGB.Controls.Add(this.label34);
		this.animimated_gif_optionsGB.Controls.Add(this.gif_fpsNUD);
		this.animimated_gif_optionsGB.Controls.Add(this.gif_fpsCB);
		this.animimated_gif_optionsGB.Controls.Add(this.gif_repeat_foreverCB);
		this.animimated_gif_optionsGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.animimated_gif_optionsGB.Location = new System.Drawing.Point(0, 171);
		this.animimated_gif_optionsGB.Name = "animimated_gif_optionsGB";
		this.animimated_gif_optionsGB.Size = new System.Drawing.Size(424, 302);
		this.animimated_gif_optionsGB.TabIndex = 21;
		this.animimated_gif_optionsGB.TabStop = false;
		this.animimated_gif_optionsGB.Text = "Animated GIF Options";
		this.animimated_gif_optionsGB.Visible = false;
		this.label45.AutoSize = true;
		this.label45.Location = new System.Drawing.Point(244, 96);
		this.label45.Name = "label45";
		this.label45.Size = new System.Drawing.Size(148, 13);
		this.label45.TabIndex = 27;
		this.label45.Text = "Colour Quantisation Algorithm:";
		this.gif_quantCB.DisplayMember = "0";
		this.gif_quantCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.gif_quantCB.FormattingEnabled = true;
		this.gif_quantCB.ItemHeight = 13;
		this.gif_quantCB.Items.AddRange(new object[2] { "Neural-Net Quantisation Algorithm", "Median Cut Quantisation Algorithm" });
		this.gif_quantCB.Location = new System.Drawing.Point(8, 93);
		this.gif_quantCB.MaxDropDownItems = 10;
		this.gif_quantCB.Name = "gif_quantCB";
		this.gif_quantCB.Size = new System.Drawing.Size(230, 21);
		this.gif_quantCB.TabIndex = 26;
		this.gif_quantCB.ValueMember = "0";
		this.gif_advanced_optionsGB.Controls.Add(this.gif_lossy_comp_levelNUD);
		this.gif_advanced_optionsGB.Controls.Add(this.label44);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_bit_depthNUD);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_adv_optionsCB);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_trans_pixel_tolNUD);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_border_tolNUD);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_lossy_comp_levelCB);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_bit_depthCB);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_trans_pixel_tolCB);
		this.gif_advanced_optionsGB.Controls.Add(this.gif_border_tolCB);
		this.gif_advanced_optionsGB.Location = new System.Drawing.Point(6, 121);
		this.gif_advanced_optionsGB.Name = "gif_advanced_optionsGB";
		this.gif_advanced_optionsGB.Size = new System.Drawing.Size(415, 151);
		this.gif_advanced_optionsGB.TabIndex = 25;
		this.gif_advanced_optionsGB.TabStop = false;
		this.gif_advanced_optionsGB.Text = "Advanced Options";
		this.label44.AutoSize = true;
		this.label44.Location = new System.Drawing.Point(180, 22);
		this.label44.Name = "label44";
		this.label44.Size = new System.Drawing.Size(136, 13);
		this.label44.TabIndex = 24;
		this.label44.Text = "Advanced Options Presets:";
		this.gif_adv_optionsCB.DisplayMember = "0";
		this.gif_adv_optionsCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.gif_adv_optionsCB.FormattingEnabled = true;
		this.gif_adv_optionsCB.ItemHeight = 13;
		this.gif_adv_optionsCB.Items.AddRange(new object[5] { "1. Maximum Quality", "2. Good Quality", "3. Medium Quality", "4. Small File Size", "5. Smaller File Size" });
		this.gif_adv_optionsCB.Location = new System.Drawing.Point(6, 19);
		this.gif_adv_optionsCB.MaxDropDownItems = 10;
		this.gif_adv_optionsCB.Name = "gif_adv_optionsCB";
		this.gif_adv_optionsCB.Size = new System.Drawing.Size(168, 21);
		this.gif_adv_optionsCB.TabIndex = 23;
		this.gif_adv_optionsCB.ValueMember = "0";
		this.gif_adv_optionsCB.SelectedIndexChanged += new System.EventHandler(this.gif_adv_optionsCB_SelectedIndexChanged);
		this.gif_lossy_comp_levelCB.AutoSize = true;
		this.gif_lossy_comp_levelCB.Location = new System.Drawing.Point(5, 121);
		this.gif_lossy_comp_levelCB.Name = "gif_lossy_comp_levelCB";
		this.gif_lossy_comp_levelCB.Size = new System.Drawing.Size(148, 17);
		this.gif_lossy_comp_levelCB.TabIndex = 3;
		this.gif_lossy_comp_levelCB.Text = "Lossy Compression Level:";
		this.gif_lossy_comp_levelCB.UseVisualStyleBackColor = true;
		this.gif_lossy_comp_levelCB.CheckedChanged += new System.EventHandler(this.gif_lossy_comp_levelCB_CheckedChanged);
		this.gif_bit_depthCB.AutoSize = true;
		this.gif_bit_depthCB.Location = new System.Drawing.Point(5, 98);
		this.gif_bit_depthCB.Name = "gif_bit_depthCB";
		this.gif_bit_depthCB.Size = new System.Drawing.Size(183, 17);
		this.gif_bit_depthCB.TabIndex = 2;
		this.gif_bit_depthCB.Text = "Reduced Colour Table Bit Depth:";
		this.gif_bit_depthCB.UseVisualStyleBackColor = true;
		this.gif_bit_depthCB.CheckedChanged += new System.EventHandler(this.gif_bit_depthCB_CheckedChanged);
		this.gif_trans_pixel_tolCB.AutoSize = true;
		this.gif_trans_pixel_tolCB.Checked = true;
		this.gif_trans_pixel_tolCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.gif_trans_pixel_tolCB.Location = new System.Drawing.Point(6, 75);
		this.gif_trans_pixel_tolCB.Name = "gif_trans_pixel_tolCB";
		this.gif_trans_pixel_tolCB.Size = new System.Drawing.Size(162, 17);
		this.gif_trans_pixel_tolCB.TabIndex = 1;
		this.gif_trans_pixel_tolCB.Text = "Transparent Pixel Tolerance:";
		this.gif_trans_pixel_tolCB.UseVisualStyleBackColor = true;
		this.gif_trans_pixel_tolCB.CheckedChanged += new System.EventHandler(this.gif_trans_pixel_tolCB_CheckedChanged);
		this.gif_border_tolCB.AutoCheck = false;
		this.gif_border_tolCB.AutoSize = true;
		this.gif_border_tolCB.Checked = true;
		this.gif_border_tolCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.gif_border_tolCB.Location = new System.Drawing.Point(6, 51);
		this.gif_border_tolCB.Name = "gif_border_tolCB";
		this.gif_border_tolCB.Size = new System.Drawing.Size(170, 17);
		this.gif_border_tolCB.TabIndex = 0;
		this.gif_border_tolCB.Text = "Unchanged Border Tolerance:";
		this.gif_border_tolCB.UseVisualStyleBackColor = true;
		this.pictureBox15.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox15.Image = PIPP_Application.Properties.Resources.gif;
		this.pictureBox15.Location = new System.Drawing.Point(399, 11);
		this.pictureBox15.Name = "pictureBox15";
		this.pictureBox15.Size = new System.Drawing.Size(20, 20);
		this.pictureBox15.TabIndex = 21;
		this.pictureBox15.TabStop = false;
		this.label41.AutoSize = true;
		this.label41.Location = new System.Drawing.Point(9, 277);
		this.label41.Name = "label41";
		this.label41.Size = new System.Drawing.Size(271, 13);
		this.label41.TabIndex = 9;
		this.label41.Text = "(See Animation Options Tab for more animation controls)";
		this.avi_outputGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.avi_outputGB.Controls.Add(this.fpsFUDlabel);
		this.avi_outputGB.Controls.Add(this.avi_fpsNUD);
		this.avi_outputGB.Controls.Add(this.avi_fpsCB);
		this.avi_outputGB.Controls.Add(this.avi_safe_frame_widthCB);
		this.avi_outputGB.Controls.Add(this.old_format_aviPAN);
		this.avi_outputGB.Controls.Add(this.label25);
		this.avi_outputGB.Controls.Add(this.avi_codecCB);
		this.avi_outputGB.Controls.Add(this.pictureBox1);
		this.avi_outputGB.Controls.Add(this.codec_qualityGB);
		this.avi_outputGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.avi_outputGB.Location = new System.Drawing.Point(0, 0);
		this.avi_outputGB.Name = "avi_outputGB";
		this.avi_outputGB.Size = new System.Drawing.Size(424, 171);
		this.avi_outputGB.TabIndex = 20;
		this.avi_outputGB.TabStop = false;
		this.avi_outputGB.Text = "AVI File Options";
		this.avi_outputGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.old_format_aviPAN.Controls.Add(this.old_format_max_sizeCB);
		this.old_format_aviPAN.Controls.Add(this.old_format_aviCB);
		this.old_format_aviPAN.Location = new System.Drawing.Point(3, 68);
		this.old_format_aviPAN.Margin = new System.Windows.Forms.Padding(0);
		this.old_format_aviPAN.Name = "old_format_aviPAN";
		this.old_format_aviPAN.Size = new System.Drawing.Size(414, 24);
		this.old_format_aviPAN.TabIndex = 27;
		this.old_format_aviPAN.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.label25.AutoSize = true;
		this.label25.Location = new System.Drawing.Point(243, 20);
		this.label25.Name = "label25";
		this.label25.Size = new System.Drawing.Size(61, 13);
		this.label25.TabIndex = 23;
		this.label25.Text = "AVI Codec:";
		this.pictureBox1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox1.Image = PIPP_Application.Properties.Resources.avi;
		this.pictureBox1.Location = new System.Drawing.Point(399, 11);
		this.pictureBox1.Name = "pictureBox1";
		this.pictureBox1.Size = new System.Drawing.Size(20, 20);
		this.pictureBox1.TabIndex = 20;
		this.pictureBox1.TabStop = false;
		this.codec_qualityGB.Controls.Add(this.label27);
		this.codec_qualityGB.Controls.Add(this.codec_qualityNUD);
		this.codec_qualityGB.Controls.Add(this.label26);
		this.codec_qualityGB.Location = new System.Drawing.Point(3, 41);
		this.codec_qualityGB.Margin = new System.Windows.Forms.Padding(0);
		this.codec_qualityGB.Name = "codec_qualityGB";
		this.codec_qualityGB.Size = new System.Drawing.Size(414, 24);
		this.codec_qualityGB.TabIndex = 24;
		this.codec_qualityGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.label27.AutoSize = true;
		this.label27.Location = new System.Drawing.Point(122, 6);
		this.label27.Name = "label27";
		this.label27.Size = new System.Drawing.Size(244, 13);
		this.label27.TabIndex = 26;
		this.label27.Text = "(1 Lowest Quality, 6 Good Quality, 10 Best Quality)";
		this.label26.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.label26.AutoSize = true;
		this.label26.Location = new System.Drawing.Point(54, 6);
		this.label26.Name = "label26";
		this.label26.Size = new System.Drawing.Size(42, 13);
		this.label26.TabIndex = 25;
		this.label26.Text = "Quality:";
		this.tabPage3.AutoScroll = true;
		this.tabPage3.BackColor = System.Drawing.Color.White;
		this.tabPage3.Controls.Add(this.splitContainer4);
		this.tabPage3.Location = new System.Drawing.Point(4, 22);
		this.tabPage3.Name = "tabPage3";
		this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage3.Size = new System.Drawing.Size(854, 437);
		this.tabPage3.TabIndex = 2;
		this.tabPage3.Text = "Quality Options";
		this.splitContainer4.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer4.IsSplitterFixed = true;
		this.splitContainer4.Location = new System.Drawing.Point(3, 3);
		this.splitContainer4.Name = "splitContainer4";
		this.splitContainer4.Panel1.Controls.Add(this.qualityoptionsGB);
		this.splitContainer4.Panel1.Controls.Add(this.qualityalgorithmGB);
		this.splitContainer4.Panel1.Controls.Add(this.qualitylimitingGB);
		this.splitContainer4.Panel1.Controls.Add(this.qualityenableGB);
		this.splitContainer4.Panel2.Controls.Add(this.qualityweightGB);
		this.splitContainer4.Size = new System.Drawing.Size(848, 431);
		this.splitContainer4.SplitterDistance = 420;
		this.splitContainer4.TabIndex = 9;
		this.qualityalgorithmGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.qualityalgorithmGB.Controls.Add(this.totalpixelalgorithmCB);
		this.qualityalgorithmGB.Controls.Add(this.histoalgorithmCB);
		this.qualityalgorithmGB.Controls.Add(this.ninoxalgorithmCB);
		this.qualityalgorithmGB.Controls.Add(this.defaultalgorithmCB);
		this.qualityalgorithmGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.qualityalgorithmGB.Enabled = false;
		this.qualityalgorithmGB.Location = new System.Drawing.Point(0, 133);
		this.qualityalgorithmGB.Name = "qualityalgorithmGB";
		this.qualityalgorithmGB.Size = new System.Drawing.Size(420, 114);
		this.qualityalgorithmGB.TabIndex = 1;
		this.qualityalgorithmGB.TabStop = false;
		this.qualityalgorithmGB.Text = "Quality Algorithm Selection";
		this.qualityalgorithmGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.qualityenableGB.BackColor = System.Drawing.Color.White;
		this.qualityenableGB.Controls.Add(this.pictureBox5);
		this.qualityenableGB.Controls.Add(this.qualitysortingCB);
		this.qualityenableGB.Controls.Add(this.qreorderCB);
		this.qualityenableGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.qualityenableGB.Location = new System.Drawing.Point(0, 0);
		this.qualityenableGB.Name = "qualityenableGB";
		this.qualityenableGB.Size = new System.Drawing.Size(420, 64);
		this.qualityenableGB.TabIndex = 0;
		this.qualityenableGB.TabStop = false;
		this.qualityenableGB.Text = "Quality Estimation";
		this.pictureBox5.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox5.Image = PIPP_Application.Properties.Resources.quality1;
		this.pictureBox5.Location = new System.Drawing.Point(395, 11);
		this.pictureBox5.Name = "pictureBox5";
		this.pictureBox5.Size = new System.Drawing.Size(20, 20);
		this.pictureBox5.TabIndex = 12;
		this.pictureBox5.TabStop = false;
		this.tabPage2.AutoScroll = true;
		this.tabPage2.BackColor = System.Drawing.Color.White;
		this.tabPage2.Controls.Add(this.splitContainer2);
		this.tabPage2.Location = new System.Drawing.Point(4, 22);
		this.tabPage2.Name = "tabPage2";
		this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage2.Size = new System.Drawing.Size(854, 437);
		this.tabPage2.TabIndex = 1;
		this.tabPage2.Text = "Processing Options";
		this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer2.IsSplitterFixed = true;
		this.splitContainer2.Location = new System.Drawing.Point(3, 3);
		this.splitContainer2.Name = "splitContainer2";
		this.splitContainer2.Panel1.BackColor = System.Drawing.Color.White;
		this.splitContainer2.Panel1.Controls.Add(this.flip_rotateGB);
		this.splitContainer2.Panel1.Controls.Add(this.groupBox5);
		this.splitContainer2.Panel1.Controls.Add(this.histostretchGB);
		this.splitContainer2.Panel1.Controls.Add(this.convertomonoGB);
		this.splitContainer2.Panel1.Controls.Add(this.noise_filterGB);
		this.splitContainer2.Panel1.Controls.Add(this.input_gainGB);
		this.splitContainer2.Panel2.BackColor = System.Drawing.Color.White;
		this.splitContainer2.Panel2.Controls.Add(this.groupBox2);
		this.splitContainer2.Panel2.Controls.Add(this.surface_stabiliseGB);
		this.splitContainer2.Panel2.Controls.Add(this.croppingGB);
		this.splitContainer2.Panel2.Controls.Add(this.planetdetectGB);
		this.splitContainer2.Panel2.Controls.Add(this.groupBox10);
		this.splitContainer2.Size = new System.Drawing.Size(848, 431);
		this.splitContainer2.SplitterDistance = 420;
		this.splitContainer2.TabIndex = 16;
		this.noise_filterGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.noise_filterGB.Controls.Add(this.pictureBox12);
		this.noise_filterGB.Controls.Add(this.median_filterCB);
		this.noise_filterGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.noise_filterGB.Location = new System.Drawing.Point(0, 71);
		this.noise_filterGB.Name = "noise_filterGB";
		this.noise_filterGB.Size = new System.Drawing.Size(420, 42);
		this.noise_filterGB.TabIndex = 20;
		this.noise_filterGB.TabStop = false;
		this.noise_filterGB.Text = "Noise Filter";
		this.pictureBox12.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox12.Image = PIPP_Application.Properties.Resources.noise;
		this.pictureBox12.Location = new System.Drawing.Point(395, 11);
		this.pictureBox12.Name = "pictureBox12";
		this.pictureBox12.Size = new System.Drawing.Size(20, 20);
		this.pictureBox12.TabIndex = 15;
		this.pictureBox12.TabStop = false;
		this.input_gainGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.input_gainGB.Controls.Add(this.pictureBox13);
		this.input_gainGB.Controls.Add(this.gammaNUD);
		this.input_gainGB.Controls.Add(this.label31);
		this.input_gainGB.Controls.Add(this.input_gainNUD);
		this.input_gainGB.Controls.Add(this.input_gainLAB);
		this.input_gainGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.input_gainGB.Location = new System.Drawing.Point(0, 0);
		this.input_gainGB.Name = "input_gainGB";
		this.input_gainGB.Size = new System.Drawing.Size(420, 71);
		this.input_gainGB.TabIndex = 19;
		this.input_gainGB.TabStop = false;
		this.input_gainGB.Text = "Input Gain And Gamma Correction";
		this.pictureBox13.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox13.Image = PIPP_Application.Properties.Resources.gain;
		this.pictureBox13.Location = new System.Drawing.Point(395, 11);
		this.pictureBox13.Name = "pictureBox13";
		this.pictureBox13.Size = new System.Drawing.Size(20, 20);
		this.pictureBox13.TabIndex = 19;
		this.pictureBox13.TabStop = false;
		this.groupBox2.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.groupBox2.BackColor = System.Drawing.Color.White;
		this.groupBox2.Controls.Add(this.pictureBox17);
		this.groupBox2.Controls.Add(this.resize_heightLAB);
		this.groupBox2.Controls.Add(this.resize_heightNUD);
		this.groupBox2.Controls.Add(this.resize_widthLAB);
		this.groupBox2.Controls.Add(this.resize_widthNUD);
		this.groupBox2.Controls.Add(this.resize_keep_aspect_ratioCB);
		this.groupBox2.Controls.Add(this.resize_framesCB);
		this.groupBox2.Dock = System.Windows.Forms.DockStyle.Top;
		this.groupBox2.Location = new System.Drawing.Point(0, 640);
		this.groupBox2.Name = "groupBox2";
		this.groupBox2.Size = new System.Drawing.Size(424, 86);
		this.groupBox2.TabIndex = 23;
		this.groupBox2.TabStop = false;
		this.groupBox2.Text = "Frame Resize (Reduce)";
		this.pictureBox17.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox17.Image = PIPP_Application.Properties.Resources.resize;
		this.pictureBox17.Location = new System.Drawing.Point(399, 11);
		this.pictureBox17.Name = "pictureBox17";
		this.pictureBox17.Size = new System.Drawing.Size(20, 20);
		this.pictureBox17.TabIndex = 36;
		this.pictureBox17.TabStop = false;
		this.resize_keep_aspect_ratioCB.AutoSize = true;
		this.resize_keep_aspect_ratioCB.Checked = true;
		this.resize_keep_aspect_ratioCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.resize_keep_aspect_ratioCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.resize_keep_aspect_ratioCB.Enabled = false;
		this.resize_keep_aspect_ratioCB.Location = new System.Drawing.Point(3, 33);
		this.resize_keep_aspect_ratioCB.Name = "resize_keep_aspect_ratioCB";
		this.resize_keep_aspect_ratioCB.Size = new System.Drawing.Size(418, 17);
		this.resize_keep_aspect_ratioCB.TabIndex = 27;
		this.resize_keep_aspect_ratioCB.Text = "Keep Original Aspect Ratio";
		this.resize_keep_aspect_ratioCB.UseVisualStyleBackColor = true;
		this.resize_keep_aspect_ratioCB.CheckedChanged += new System.EventHandler(this.resize_keep_aspect_ratioCB_CheckedChanged);
		this.resize_framesCB.AutoSize = true;
		this.resize_framesCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.resize_framesCB.Location = new System.Drawing.Point(3, 16);
		this.resize_framesCB.Name = "resize_framesCB";
		this.resize_framesCB.Size = new System.Drawing.Size(418, 17);
		this.resize_framesCB.TabIndex = 26;
		this.resize_framesCB.Text = "Resize Frames";
		this.resize_framesCB.UseVisualStyleBackColor = true;
		this.resize_framesCB.CheckedChanged += new System.EventHandler(this.resize_framesCB_CheckedChanged);
		this.surface_stabiliseGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.surface_stabiliseGB.BackColor = System.Drawing.Color.White;
		this.surface_stabiliseGB.Controls.Add(this.area_of_interestGB);
		this.surface_stabiliseGB.Controls.Add(this.anchor_feature_boxGB);
		this.surface_stabiliseGB.Controls.Add(this.pictureBox14);
		this.surface_stabiliseGB.Controls.Add(this.enablestabilisationCB);
		this.surface_stabiliseGB.Controls.Add(this.enableSurfaceTrackingCB);
		this.surface_stabiliseGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.surface_stabiliseGB.Location = new System.Drawing.Point(0, 342);
		this.surface_stabiliseGB.Name = "surface_stabiliseGB";
		this.surface_stabiliseGB.Size = new System.Drawing.Size(424, 298);
		this.surface_stabiliseGB.TabIndex = 3;
		this.surface_stabiliseGB.TabStop = false;
		this.surface_stabiliseGB.Text = "Surface Stabilisation";
		this.area_of_interestGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.area_of_interestGB.Controls.Add(this.aoi_enableCB);
		this.area_of_interestGB.Controls.Add(this.aoi_cropCB);
		this.area_of_interestGB.Controls.Add(this.label36);
		this.area_of_interestGB.Controls.Add(this.label38);
		this.area_of_interestGB.Controls.Add(this.aoi_rejectCB);
		this.area_of_interestGB.Controls.Add(this.aoi_heightNUD);
		this.area_of_interestGB.Controls.Add(this.aoi_widthNUD);
		this.area_of_interestGB.Controls.Add(this.label35);
		this.area_of_interestGB.Controls.Add(this.label37);
		this.area_of_interestGB.Controls.Add(this.aoi_yNUD);
		this.area_of_interestGB.Controls.Add(this.aoi_xNUD);
		this.area_of_interestGB.Enabled = false;
		this.area_of_interestGB.Location = new System.Drawing.Point(3, 155);
		this.area_of_interestGB.Name = "area_of_interestGB";
		this.area_of_interestGB.Size = new System.Drawing.Size(418, 144);
		this.area_of_interestGB.TabIndex = 24;
		this.area_of_interestGB.TabStop = false;
		this.area_of_interestGB.Text = "Area Of Interest (AOI)";
		this.area_of_interestGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.anchor_feature_boxGB.BackColor = System.Drawing.Color.WhiteSmoke;
		this.anchor_feature_boxGB.Controls.Add(this.label39);
		this.anchor_feature_boxGB.Controls.Add(this.afb_rejectCB);
		this.anchor_feature_boxGB.Controls.Add(this.label40);
		this.anchor_feature_boxGB.Controls.Add(this.afb_heightNUD);
		this.anchor_feature_boxGB.Controls.Add(this.label33);
		this.anchor_feature_boxGB.Controls.Add(this.afb_widthNUD);
		this.anchor_feature_boxGB.Controls.Add(this.label32);
		this.anchor_feature_boxGB.Controls.Add(this.afb_yNUD);
		this.anchor_feature_boxGB.Controls.Add(this.afb_xNUD);
		this.anchor_feature_boxGB.Enabled = false;
		this.anchor_feature_boxGB.Location = new System.Drawing.Point(3, 56);
		this.anchor_feature_boxGB.Name = "anchor_feature_boxGB";
		this.anchor_feature_boxGB.Size = new System.Drawing.Size(418, 93);
		this.anchor_feature_boxGB.TabIndex = 23;
		this.anchor_feature_boxGB.TabStop = false;
		this.anchor_feature_boxGB.Text = "Anchor Feature Box (AFB)";
		this.anchor_feature_boxGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.pictureBox14.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox14.Image = PIPP_Application.Properties.Resources.crater;
		this.pictureBox14.Location = new System.Drawing.Point(399, 11);
		this.pictureBox14.Name = "pictureBox14";
		this.pictureBox14.Size = new System.Drawing.Size(20, 20);
		this.pictureBox14.TabIndex = 13;
		this.pictureBox14.TabStop = false;
		this.croppingGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.croppingGB.BackColor = System.Drawing.Color.White;
		this.croppingGB.Controls.Add(this.enablecropping3PANEL);
		this.croppingGB.Controls.Add(this.cropPB);
		this.croppingGB.Controls.Add(this.enablecropCB);
		this.croppingGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.croppingGB.Location = new System.Drawing.Point(0, 246);
		this.croppingGB.Name = "croppingGB";
		this.croppingGB.Size = new System.Drawing.Size(424, 96);
		this.croppingGB.TabIndex = 2;
		this.croppingGB.TabStop = false;
		this.croppingGB.Text = "Cropping";
		this.enablecropping3PANEL.AutoSize = true;
		this.enablecropping3PANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.enablecropping3PANEL.BackColor = System.Drawing.Color.Transparent;
		this.enablecropping3PANEL.Controls.Add(this.enableoffsetPANEL);
		this.enablecropping3PANEL.Controls.Add(this.enablecroppingPANEL);
		this.enablecropping3PANEL.Dock = System.Windows.Forms.DockStyle.Top;
		this.enablecropping3PANEL.Location = new System.Drawing.Point(3, 33);
		this.enablecropping3PANEL.Name = "enablecropping3PANEL";
		this.enablecropping3PANEL.Size = new System.Drawing.Size(418, 60);
		this.enablecropping3PANEL.TabIndex = 14;
		this.enableoffsetPANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.enableoffsetPANEL.BackColor = System.Drawing.Color.WhiteSmoke;
		this.enableoffsetPANEL.Controls.Add(this.xoffsetNUD);
		this.enableoffsetPANEL.Controls.Add(this.yoffsetNUD);
		this.enableoffsetPANEL.Controls.Add(this.label7);
		this.enableoffsetPANEL.Controls.Add(this.label6);
		this.enableoffsetPANEL.Enabled = false;
		this.enableoffsetPANEL.Location = new System.Drawing.Point(182, 0);
		this.enableoffsetPANEL.Name = "enableoffsetPANEL";
		this.enableoffsetPANEL.Size = new System.Drawing.Size(154, 57);
		this.enableoffsetPANEL.TabIndex = 10;
		this.enableoffsetPANEL.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.enablecroppingPANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.enablecroppingPANEL.BackColor = System.Drawing.Color.WhiteSmoke;
		this.enablecroppingPANEL.Controls.Add(this.cropwidthNUD);
		this.enablecroppingPANEL.Controls.Add(this.label4);
		this.enablecroppingPANEL.Controls.Add(this.label5);
		this.enablecroppingPANEL.Controls.Add(this.cropheightNUD);
		this.enablecroppingPANEL.Enabled = false;
		this.enablecroppingPANEL.Location = new System.Drawing.Point(0, 1);
		this.enablecroppingPANEL.Name = "enablecroppingPANEL";
		this.enablecroppingPANEL.Size = new System.Drawing.Size(182, 56);
		this.enablecroppingPANEL.TabIndex = 9;
		this.enablecroppingPANEL.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.cropPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.cropPB.Image = PIPP_Application.Properties.Resources.crop4;
		this.cropPB.Location = new System.Drawing.Point(399, 11);
		this.cropPB.Name = "cropPB";
		this.cropPB.Size = new System.Drawing.Size(20, 20);
		this.cropPB.TabIndex = 13;
		this.cropPB.TabStop = false;
		this.enablecropCB.AutoSize = true;
		this.enablecropCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.enablecropCB.Location = new System.Drawing.Point(3, 16);
		this.enablecropCB.Name = "enablecropCB";
		this.enablecropCB.Size = new System.Drawing.Size(418, 17);
		this.enablecropCB.TabIndex = 1;
		this.enablecropCB.Text = "Enable Cropping";
		this.enablecropCB.UseVisualStyleBackColor = true;
		this.enablecropCB.CheckedChanged += new System.EventHandler(this.enablecropCB_CheckedChanged);
		this.planetdetectGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.planetdetectGB.BackColor = System.Drawing.Color.White;
		this.planetdetectGB.Controls.Add(this.objectdetectPB);
		this.planetdetectGB.Controls.Add(this.edge_of_shadowPAN);
		this.planetdetectGB.Controls.Add(this.centreplanetCB);
		this.planetdetectGB.Controls.Add(this.minpixelvalueGB);
		this.planetdetectGB.Controls.Add(this.checkplanetCB);
		this.planetdetectGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.planetdetectGB.Location = new System.Drawing.Point(0, 44);
		this.planetdetectGB.Name = "planetdetectGB";
		this.planetdetectGB.Size = new System.Drawing.Size(424, 202);
		this.planetdetectGB.TabIndex = 0;
		this.planetdetectGB.TabStop = false;
		this.planetdetectGB.Text = "Object Detection";
		this.objectdetectPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.objectdetectPB.Image = PIPP_Application.Properties.Resources.object_detect;
		this.objectdetectPB.Location = new System.Drawing.Point(399, 11);
		this.objectdetectPB.Name = "objectdetectPB";
		this.objectdetectPB.Size = new System.Drawing.Size(20, 20);
		this.objectdetectPB.TabIndex = 14;
		this.objectdetectPB.TabStop = false;
		this.edge_of_shadowPAN.AutoSize = true;
		this.edge_of_shadowPAN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.edge_of_shadowPAN.Controls.Add(this.moon_shadowPB);
		this.edge_of_shadowPAN.Controls.Add(this.edge_in_shadowCB);
		this.edge_of_shadowPAN.Controls.Add(this.label30);
		this.edge_of_shadowPAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.edge_of_shadowPAN.Enabled = false;
		this.edge_of_shadowPAN.Location = new System.Drawing.Point(3, 162);
		this.edge_of_shadowPAN.Name = "edge_of_shadowPAN";
		this.edge_of_shadowPAN.Size = new System.Drawing.Size(418, 28);
		this.edge_of_shadowPAN.TabIndex = 19;
		this.moon_shadowPB.Image = PIPP_Application.Properties.Resources.moon_shadow_none;
		this.moon_shadowPB.Location = new System.Drawing.Point(174, 5);
		this.moon_shadowPB.Name = "moon_shadowPB";
		this.moon_shadowPB.Size = new System.Drawing.Size(20, 20);
		this.moon_shadowPB.TabIndex = 20;
		this.moon_shadowPB.TabStop = false;
		this.groupBox10.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.groupBox10.BackColor = System.Drawing.Color.White;
		this.groupBox10.Controls.Add(this.stabilise_surfaceRB);
		this.groupBox10.Controls.Add(this.stabilise_objectRB);
		this.groupBox10.Controls.Add(this.stabilise_noneRB);
		this.groupBox10.Dock = System.Windows.Forms.DockStyle.Top;
		this.groupBox10.Location = new System.Drawing.Point(0, 0);
		this.groupBox10.Name = "groupBox10";
		this.groupBox10.Size = new System.Drawing.Size(424, 44);
		this.groupBox10.TabIndex = 22;
		this.groupBox10.TabStop = false;
		this.groupBox10.Text = "Frame Stabilisation Mode";
		this.tabPage1.AutoScroll = true;
		this.tabPage1.BackColor = System.Drawing.Color.White;
		this.tabPage1.Controls.Add(this.image_filesTAB_CONTROL);
		this.tabPage1.Location = new System.Drawing.Point(4, 22);
		this.tabPage1.Margin = new System.Windows.Forms.Padding(0);
		this.tabPage1.Name = "tabPage1";
		this.tabPage1.Size = new System.Drawing.Size(854, 437);
		this.tabPage1.TabIndex = 0;
		this.tabPage1.Text = "Source Files";
		this.image_filesTAB_CONTROL.Controls.Add(this.tab_image_files);
		this.image_filesTAB_CONTROL.Controls.Add(this.tab_dark_files);
		this.image_filesTAB_CONTROL.Controls.Add(this.tab_flat_files);
		this.image_filesTAB_CONTROL.Controls.Add(this.tab_flat_dark_files);
		this.image_filesTAB_CONTROL.Dock = System.Windows.Forms.DockStyle.Fill;
		this.image_filesTAB_CONTROL.Location = new System.Drawing.Point(0, 0);
		this.image_filesTAB_CONTROL.Margin = new System.Windows.Forms.Padding(0);
		this.image_filesTAB_CONTROL.Name = "image_filesTAB_CONTROL";
		this.image_filesTAB_CONTROL.Padding = new System.Drawing.Point(0, 0);
		this.image_filesTAB_CONTROL.SelectedIndex = 0;
		this.image_filesTAB_CONTROL.Size = new System.Drawing.Size(854, 437);
		this.image_filesTAB_CONTROL.TabIndex = 9;
		this.tab_image_files.Controls.Add(this.tableLayoutPanel1);
		this.tab_image_files.Location = new System.Drawing.Point(4, 22);
		this.tab_image_files.Margin = new System.Windows.Forms.Padding(0);
		this.tab_image_files.Name = "tab_image_files";
		this.tab_image_files.Size = new System.Drawing.Size(846, 411);
		this.tab_image_files.TabIndex = 0;
		this.tab_image_files.Text = "Image Files (0 frames)";
		this.tab_image_files.UseVisualStyleBackColor = true;
		this.tableLayoutPanel1.ColumnCount = 1;
		this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel1.Controls.Add(this.image_filesGB, 0, 0);
		this.tableLayoutPanel1.Controls.Add(this.panel2, 0, 1);
		this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
		this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
		this.tableLayoutPanel1.Name = "tableLayoutPanel1";
		this.tableLayoutPanel1.RowCount = 2;
		this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 83f));
		this.tableLayoutPanel1.Size = new System.Drawing.Size(846, 411);
		this.tableLayoutPanel1.TabIndex = 8;
		this.image_filesGB.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.image_filesGB.Controls.Add(this.image_filelist);
		this.image_filesGB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.image_filesGB.Location = new System.Drawing.Point(3, 3);
		this.image_filesGB.Name = "image_filesGB";
		this.image_filesGB.Size = new System.Drawing.Size(840, 322);
		this.image_filesGB.TabIndex = 9;
		this.image_filesGB.TabStop = false;
		this.image_filesGB.Text = "Image Files List";
		this.image_filelist.AccessibleRole = System.Windows.Forms.AccessibleRole.None;
		this.image_filelist.AllowColumnReorder = true;
		this.image_filelist.AllowDrop = true;
		this.image_filelist.BackColor = System.Drawing.Color.Cornsilk;
		this.image_filelist.Dock = System.Windows.Forms.DockStyle.Fill;
		this.image_filelist.FullRowSelect = true;
		this.image_filelist.GridLines = true;
		this.image_filelist.HideSelection = false;
		this.image_filelist.Location = new System.Drawing.Point(3, 16);
		this.image_filelist.Name = "image_filelist";
		this.image_filelist.ShowGroups = false;
		this.image_filelist.Size = new System.Drawing.Size(834, 303);
		this.image_filelist.TabIndex = 8;
		this.image_filelist.UseCompatibleStateImageBehavior = false;
		this.image_filelist.View = System.Windows.Forms.View.Details;
		this.image_filelist.FileListUpdated += new System.EventHandler(this.source_filelist_FileListUpdated);
		this.image_filelist.ItemSelectionChanged += new System.Windows.Forms.ListViewItemSelectionChangedEventHandler(this.source_filelist_ItemSelectionChanged);
		this.panel2.BackColor = System.Drawing.Color.White;
		this.panel2.Controls.Add(this.load_button);
		this.panel2.Controls.Add(this.groupBox3);
		this.panel2.Controls.Add(this.file_down_BUT);
		this.panel2.Controls.Add(this.remove_button);
		this.panel2.Controls.Add(this.runtypeGB);
		this.panel2.Controls.Add(this.file_up_BUT);
		this.panel2.Controls.Add(this.remove_all_button);
		this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel2.Location = new System.Drawing.Point(3, 331);
		this.panel2.Name = "panel2";
		this.panel2.Size = new System.Drawing.Size(840, 77);
		this.panel2.TabIndex = 10;
		this.runtypeGB.Controls.Add(this.joinmodeRB);
		this.runtypeGB.Controls.Add(this.batchmodeRB);
		this.runtypeGB.Location = new System.Drawing.Point(217, 3);
		this.runtypeGB.Name = "runtypeGB";
		this.runtypeGB.Size = new System.Drawing.Size(131, 69);
		this.runtypeGB.TabIndex = 0;
		this.runtypeGB.TabStop = false;
		this.runtypeGB.Text = "Multiple Source Files:";
		this.tab_dark_files.Controls.Add(this.tableLayoutPanel3);
		this.tab_dark_files.Location = new System.Drawing.Point(4, 22);
		this.tab_dark_files.Margin = new System.Windows.Forms.Padding(0);
		this.tab_dark_files.Name = "tab_dark_files";
		this.tab_dark_files.Size = new System.Drawing.Size(846, 411);
		this.tab_dark_files.TabIndex = 1;
		this.tab_dark_files.Text = "Dark Files (0 frames)";
		this.tab_dark_files.UseVisualStyleBackColor = true;
		this.tableLayoutPanel3.ColumnCount = 1;
		this.tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel3.Controls.Add(this.dark_filesGB, 0, 0);
		this.tableLayoutPanel3.Controls.Add(this.panel6, 0, 1);
		this.tableLayoutPanel3.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tableLayoutPanel3.Location = new System.Drawing.Point(0, 0);
		this.tableLayoutPanel3.Margin = new System.Windows.Forms.Padding(0);
		this.tableLayoutPanel3.Name = "tableLayoutPanel3";
		this.tableLayoutPanel3.RowCount = 2;
		this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 83f));
		this.tableLayoutPanel3.Size = new System.Drawing.Size(846, 411);
		this.tableLayoutPanel3.TabIndex = 9;
		this.dark_filesGB.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.dark_filesGB.Controls.Add(this.dark_filelist);
		this.dark_filesGB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dark_filesGB.Location = new System.Drawing.Point(3, 3);
		this.dark_filesGB.Name = "dark_filesGB";
		this.dark_filesGB.Size = new System.Drawing.Size(840, 322);
		this.dark_filesGB.TabIndex = 9;
		this.dark_filesGB.TabStop = false;
		this.dark_filesGB.Text = "Dark Files List";
		this.dark_filelist.AccessibleRole = System.Windows.Forms.AccessibleRole.None;
		this.dark_filelist.AllowColumnReorder = true;
		this.dark_filelist.AllowDrop = true;
		this.dark_filelist.BackColor = System.Drawing.Color.Cornsilk;
		this.dark_filelist.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dark_filelist.FullRowSelect = true;
		this.dark_filelist.GridLines = true;
		this.dark_filelist.HideSelection = false;
		this.dark_filelist.Location = new System.Drawing.Point(3, 16);
		this.dark_filelist.Name = "dark_filelist";
		this.dark_filelist.ShowGroups = false;
		this.dark_filelist.Size = new System.Drawing.Size(834, 303);
		this.dark_filelist.TabIndex = 8;
		this.dark_filelist.UseCompatibleStateImageBehavior = false;
		this.dark_filelist.View = System.Windows.Forms.View.Details;
		this.dark_filelist.FileListUpdated += new System.EventHandler(this.dark_filelist_FileListUpdated);
		this.dark_filelist.ItemSelectionChanged += new System.Windows.Forms.ListViewItemSelectionChangedEventHandler(this.dark_filelist_ItemSelectionChanged);
		this.panel6.BackColor = System.Drawing.Color.White;
		this.panel6.Controls.Add(this.hot_pixels_optionsGB);
		this.panel6.Controls.Add(this.dark_frame_optionsGB);
		this.panel6.Controls.Add(this.add_dark_filesBUT);
		this.panel6.Controls.Add(this.remove_dark_filesBUT);
		this.panel6.Controls.Add(this.remove_all_dark_filesBUT);
		this.panel6.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel6.Location = new System.Drawing.Point(3, 331);
		this.panel6.Name = "panel6";
		this.panel6.Size = new System.Drawing.Size(840, 77);
		this.panel6.TabIndex = 10;
		this.hot_pixels_optionsGB.Controls.Add(this.dark_data_has_bayerCB);
		this.hot_pixels_optionsGB.Controls.Add(this.replace_hot_pixelsCB);
		this.hot_pixels_optionsGB.Controls.Add(this.hot_pixel_thresholdLAB);
		this.hot_pixels_optionsGB.Controls.Add(this.hot_pixel_thresholdNUD);
		this.hot_pixels_optionsGB.Enabled = false;
		this.hot_pixels_optionsGB.Location = new System.Drawing.Point(370, 3);
		this.hot_pixels_optionsGB.Name = "hot_pixels_optionsGB";
		this.hot_pixels_optionsGB.Size = new System.Drawing.Size(441, 71);
		this.hot_pixels_optionsGB.TabIndex = 9;
		this.hot_pixels_optionsGB.TabStop = false;
		this.hot_pixels_optionsGB.Text = "Hot Pixel Options";
		this.dark_frame_optionsGB.Controls.Add(this.save_master_dark_CB);
		this.dark_frame_optionsGB.Controls.Add(this.dark_frame_subtractionCB);
		this.dark_frame_optionsGB.Enabled = false;
		this.dark_frame_optionsGB.Location = new System.Drawing.Point(170, 3);
		this.dark_frame_optionsGB.Name = "dark_frame_optionsGB";
		this.dark_frame_optionsGB.Size = new System.Drawing.Size(194, 71);
		this.dark_frame_optionsGB.TabIndex = 8;
		this.dark_frame_optionsGB.TabStop = false;
		this.dark_frame_optionsGB.Text = "Dark Frame Options";
		this.dark_frame_optionsGB.EnabledChanged += new System.EventHandler(this.handle_dark_file_enables);
		this.tab_flat_files.Controls.Add(this.tableLayoutPanel4);
		this.tab_flat_files.Location = new System.Drawing.Point(4, 22);
		this.tab_flat_files.Margin = new System.Windows.Forms.Padding(0);
		this.tab_flat_files.Name = "tab_flat_files";
		this.tab_flat_files.Size = new System.Drawing.Size(846, 411);
		this.tab_flat_files.TabIndex = 2;
		this.tab_flat_files.Text = "Flat Files (0 frames)";
		this.tab_flat_files.UseVisualStyleBackColor = true;
		this.tableLayoutPanel4.ColumnCount = 1;
		this.tableLayoutPanel4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel4.Controls.Add(this.flat_filesGB, 0, 0);
		this.tableLayoutPanel4.Controls.Add(this.panel9, 0, 1);
		this.tableLayoutPanel4.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tableLayoutPanel4.Location = new System.Drawing.Point(0, 0);
		this.tableLayoutPanel4.Margin = new System.Windows.Forms.Padding(0);
		this.tableLayoutPanel4.Name = "tableLayoutPanel4";
		this.tableLayoutPanel4.RowCount = 2;
		this.tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 83f));
		this.tableLayoutPanel4.Size = new System.Drawing.Size(846, 411);
		this.tableLayoutPanel4.TabIndex = 10;
		this.flat_filesGB.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.flat_filesGB.Controls.Add(this.flat_filelist);
		this.flat_filesGB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.flat_filesGB.Location = new System.Drawing.Point(3, 3);
		this.flat_filesGB.Name = "flat_filesGB";
		this.flat_filesGB.Size = new System.Drawing.Size(840, 322);
		this.flat_filesGB.TabIndex = 9;
		this.flat_filesGB.TabStop = false;
		this.flat_filesGB.Text = "Flat Files List";
		this.flat_filelist.AccessibleRole = System.Windows.Forms.AccessibleRole.None;
		this.flat_filelist.AllowColumnReorder = true;
		this.flat_filelist.AllowDrop = true;
		this.flat_filelist.BackColor = System.Drawing.Color.Cornsilk;
		this.flat_filelist.Dock = System.Windows.Forms.DockStyle.Fill;
		this.flat_filelist.FullRowSelect = true;
		this.flat_filelist.GridLines = true;
		this.flat_filelist.HideSelection = false;
		this.flat_filelist.Location = new System.Drawing.Point(3, 16);
		this.flat_filelist.Name = "flat_filelist";
		this.flat_filelist.ShowGroups = false;
		this.flat_filelist.Size = new System.Drawing.Size(834, 303);
		this.flat_filelist.TabIndex = 8;
		this.flat_filelist.UseCompatibleStateImageBehavior = false;
		this.flat_filelist.View = System.Windows.Forms.View.Details;
		this.flat_filelist.FileListUpdated += new System.EventHandler(this.flat_filelist_FileListUpdated);
		this.flat_filelist.ItemSelectionChanged += new System.Windows.Forms.ListViewItemSelectionChangedEventHandler(this.flat_filelist_ItemSelectionChanged);
		this.panel9.BackColor = System.Drawing.Color.White;
		this.panel9.Controls.Add(this.flat_frame_optionsGB);
		this.panel9.Controls.Add(this.add_flat_filesBUT);
		this.panel9.Controls.Add(this.remove_flat_filesBUT);
		this.panel9.Controls.Add(this.remove_all_flat_filesBUT);
		this.panel9.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel9.Location = new System.Drawing.Point(3, 331);
		this.panel9.Name = "panel9";
		this.panel9.Size = new System.Drawing.Size(840, 77);
		this.panel9.TabIndex = 10;
		this.flat_frame_optionsGB.Controls.Add(this.flat_data_has_bayerCB);
		this.flat_frame_optionsGB.Controls.Add(this.save_master_flat_CB);
		this.flat_frame_optionsGB.Controls.Add(this.flat_frame_calibrationCB);
		this.flat_frame_optionsGB.Enabled = false;
		this.flat_frame_optionsGB.Location = new System.Drawing.Point(170, 3);
		this.flat_frame_optionsGB.Name = "flat_frame_optionsGB";
		this.flat_frame_optionsGB.Size = new System.Drawing.Size(404, 71);
		this.flat_frame_optionsGB.TabIndex = 9;
		this.flat_frame_optionsGB.TabStop = false;
		this.flat_frame_optionsGB.Text = "Flat Frame Options";
		this.tab_flat_dark_files.Controls.Add(this.tableLayoutPanel5);
		this.tab_flat_dark_files.Location = new System.Drawing.Point(4, 22);
		this.tab_flat_dark_files.Margin = new System.Windows.Forms.Padding(0);
		this.tab_flat_dark_files.Name = "tab_flat_dark_files";
		this.tab_flat_dark_files.Size = new System.Drawing.Size(846, 411);
		this.tab_flat_dark_files.TabIndex = 3;
		this.tab_flat_dark_files.Text = "Flat Dark Files (0 frames)";
		this.tab_flat_dark_files.UseVisualStyleBackColor = true;
		this.tableLayoutPanel5.ColumnCount = 1;
		this.tableLayoutPanel5.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel5.Controls.Add(this.flat_dark_filesGB, 0, 0);
		this.tableLayoutPanel5.Controls.Add(this.panel10, 0, 1);
		this.tableLayoutPanel5.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tableLayoutPanel5.Location = new System.Drawing.Point(0, 0);
		this.tableLayoutPanel5.Margin = new System.Windows.Forms.Padding(0);
		this.tableLayoutPanel5.Name = "tableLayoutPanel5";
		this.tableLayoutPanel5.RowCount = 2;
		this.tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 83f));
		this.tableLayoutPanel5.Size = new System.Drawing.Size(846, 411);
		this.tableLayoutPanel5.TabIndex = 10;
		this.flat_dark_filesGB.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.flat_dark_filesGB.Controls.Add(this.flat_dark_filelist);
		this.flat_dark_filesGB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.flat_dark_filesGB.Location = new System.Drawing.Point(3, 3);
		this.flat_dark_filesGB.Name = "flat_dark_filesGB";
		this.flat_dark_filesGB.Size = new System.Drawing.Size(840, 322);
		this.flat_dark_filesGB.TabIndex = 9;
		this.flat_dark_filesGB.TabStop = false;
		this.flat_dark_filesGB.Text = "Flat Dark Files List";
		this.flat_dark_filelist.AccessibleRole = System.Windows.Forms.AccessibleRole.None;
		this.flat_dark_filelist.AllowColumnReorder = true;
		this.flat_dark_filelist.AllowDrop = true;
		this.flat_dark_filelist.BackColor = System.Drawing.Color.Cornsilk;
		this.flat_dark_filelist.Dock = System.Windows.Forms.DockStyle.Fill;
		this.flat_dark_filelist.FullRowSelect = true;
		this.flat_dark_filelist.GridLines = true;
		this.flat_dark_filelist.HideSelection = false;
		this.flat_dark_filelist.Location = new System.Drawing.Point(3, 16);
		this.flat_dark_filelist.Name = "flat_dark_filelist";
		this.flat_dark_filelist.ShowGroups = false;
		this.flat_dark_filelist.Size = new System.Drawing.Size(834, 303);
		this.flat_dark_filelist.TabIndex = 8;
		this.flat_dark_filelist.UseCompatibleStateImageBehavior = false;
		this.flat_dark_filelist.View = System.Windows.Forms.View.Details;
		this.flat_dark_filelist.FileListUpdated += new System.EventHandler(this.flat_dark_filelist_FileListUpdated);
		this.flat_dark_filelist.ItemSelectionChanged += new System.Windows.Forms.ListViewItemSelectionChangedEventHandler(this.dark_filelist_ItemSelectionChanged);
		this.panel10.BackColor = System.Drawing.Color.White;
		this.panel10.Controls.Add(this.flat_dark_frame_optionsGB);
		this.panel10.Controls.Add(this.add_flat_dark_filesBUT);
		this.panel10.Controls.Add(this.remove_flat_dark_filesBUT);
		this.panel10.Controls.Add(this.remove_all_flat_dark_filesBUT);
		this.panel10.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel10.Location = new System.Drawing.Point(3, 331);
		this.panel10.Name = "panel10";
		this.panel10.Size = new System.Drawing.Size(840, 77);
		this.panel10.TabIndex = 10;
		this.flat_dark_frame_optionsGB.Controls.Add(this.save_master_flat_dark_CB);
		this.flat_dark_frame_optionsGB.Controls.Add(this.flat_dark_frame_subtractionCB);
		this.flat_dark_frame_optionsGB.Enabled = false;
		this.flat_dark_frame_optionsGB.Location = new System.Drawing.Point(170, 3);
		this.flat_dark_frame_optionsGB.Name = "flat_dark_frame_optionsGB";
		this.flat_dark_frame_optionsGB.Size = new System.Drawing.Size(206, 71);
		this.flat_dark_frame_optionsGB.TabIndex = 9;
		this.flat_dark_frame_optionsGB.TabStop = false;
		this.flat_dark_frame_optionsGB.Text = "Flat Dark Frame Options";
		this.tabControl1.Controls.Add(this.tabPage1);
		this.tabControl1.Controls.Add(this.tabPage7);
		this.tabControl1.Controls.Add(this.tabPage2);
		this.tabControl1.Controls.Add(this.tabPage3);
		this.tabControl1.Controls.Add(this.tabPage5);
		this.tabControl1.Controls.Add(this.tabPage4);
		this.tabControl1.Controls.Add(this.tabPage6);
		this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.tabControl1.Location = new System.Drawing.Point(0, 24);
		this.tabControl1.Margin = new System.Windows.Forms.Padding(0);
		this.tabControl1.Multiline = true;
		this.tabControl1.Name = "tabControl1";
		this.tabControl1.Padding = new System.Drawing.Point(0, 0);
		this.tabControl1.SelectedIndex = 0;
		this.tabControl1.Size = new System.Drawing.Size(862, 463);
		this.tabControl1.TabIndex = 9;
		this.tabPage7.BackColor = System.Drawing.Color.White;
		this.tabPage7.Controls.Add(this.splitContainer7);
		this.tabPage7.Location = new System.Drawing.Point(4, 22);
		this.tabPage7.Name = "tabPage7";
		this.tabPage7.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage7.Size = new System.Drawing.Size(854, 437);
		this.tabPage7.TabIndex = 6;
		this.tabPage7.Text = "Input Options";
		this.splitContainer7.BackColor = System.Drawing.Color.White;
		this.splitContainer7.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer7.IsSplitterFixed = true;
		this.splitContainer7.Location = new System.Drawing.Point(3, 3);
		this.splitContainer7.Name = "splitContainer7";
		this.splitContainer7.Panel1.Controls.Add(this.date_timeGB);
		this.splitContainer7.Panel1.Controls.Add(this.raw_file_optionsGB);
		this.splitContainer7.Panel1.Controls.Add(this.binningGB);
		this.splitContainer7.Panel2.Controls.Add(this.debayerGB);
		this.splitContainer7.Panel2.Controls.Add(this.debayerenableGB);
		this.splitContainer7.Panel2.Controls.Add(this.colourdetectionGB);
		this.splitContainer7.Panel2.Controls.Add(this.ipframedroppingGB);
		this.splitContainer7.Panel2.Controls.Add(this.limitframesGB);
		this.splitContainer7.Panel2.Controls.Add(this.serfilesGB);
		this.splitContainer7.Panel2.Controls.Add(this.avifilesGB);
		this.splitContainer7.Size = new System.Drawing.Size(848, 431);
		this.splitContainer7.SplitterDistance = 419;
		this.splitContainer7.TabIndex = 0;
		this.date_timeGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.date_timeGB.Controls.Add(this.pictureBox11);
		this.date_timeGB.Controls.Add(this.winjupossubPANEL);
		this.date_timeGB.Controls.Add(this.extract_date_timeCB);
		this.date_timeGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.date_timeGB.Location = new System.Drawing.Point(0, 230);
		this.date_timeGB.Name = "date_timeGB";
		this.date_timeGB.Size = new System.Drawing.Size(419, 176);
		this.date_timeGB.TabIndex = 20;
		this.date_timeGB.TabStop = false;
		this.date_timeGB.Text = "Date/Time Extraction";
		this.pictureBox11.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox11.Image = PIPP_Application.Properties.Resources.clock;
		this.pictureBox11.Location = new System.Drawing.Point(394, 11);
		this.pictureBox11.Name = "pictureBox11";
		this.pictureBox11.Size = new System.Drawing.Size(20, 20);
		this.pictureBox11.TabIndex = 15;
		this.pictureBox11.TabStop = false;
		this.raw_file_optionsGB.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.raw_file_optionsGB.Controls.Add(this.label24);
		this.raw_file_optionsGB.Controls.Add(this.raw_highlight_recovery_typeCB);
		this.raw_file_optionsGB.Controls.Add(this.pictureBox9);
		this.raw_file_optionsGB.Controls.Add(this.raw_colour_optionsPANEL);
		this.raw_file_optionsGB.Controls.Add(this.raw_image_debayerCB);
		this.raw_file_optionsGB.Controls.Add(this.raw_hpfCB);
		this.raw_file_optionsGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.raw_file_optionsGB.Location = new System.Drawing.Point(0, 79);
		this.raw_file_optionsGB.Name = "raw_file_optionsGB";
		this.raw_file_optionsGB.Size = new System.Drawing.Size(419, 151);
		this.raw_file_optionsGB.TabIndex = 5;
		this.raw_file_optionsGB.TabStop = false;
		this.raw_file_optionsGB.Text = "Raw Image File Options (DCRAW)";
		this.label24.AutoSize = true;
		this.label24.Location = new System.Drawing.Point(253, 123);
		this.label24.Name = "label24";
		this.label24.Size = new System.Drawing.Size(124, 13);
		this.label24.TabIndex = 12;
		this.label24.Text = "Highlight Recovery Type";
		this.pictureBox9.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox9.Image = PIPP_Application.Properties.Resources.dcraw;
		this.pictureBox9.Location = new System.Drawing.Point(394, 11);
		this.pictureBox9.Name = "pictureBox9";
		this.pictureBox9.Size = new System.Drawing.Size(20, 20);
		this.pictureBox9.TabIndex = 15;
		this.pictureBox9.TabStop = false;
		this.raw_colour_optionsPANEL.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.raw_colour_optionsPANEL.Controls.Add(this.label23);
		this.raw_colour_optionsPANEL.Controls.Add(this.raw_output_colour_spaceCB);
		this.raw_colour_optionsPANEL.Controls.Add(this.raw_debayer_algorithmCB);
		this.raw_colour_optionsPANEL.Controls.Add(this.label21);
		this.raw_colour_optionsPANEL.Location = new System.Drawing.Point(10, 58);
		this.raw_colour_optionsPANEL.Margin = new System.Windows.Forms.Padding(0);
		this.raw_colour_optionsPANEL.Name = "raw_colour_optionsPANEL";
		this.raw_colour_optionsPANEL.Size = new System.Drawing.Size(384, 59);
		this.raw_colour_optionsPANEL.TabIndex = 11;
		this.raw_colour_optionsPANEL.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.label23.AutoSize = true;
		this.label23.Location = new System.Drawing.Point(217, 38);
		this.label23.Name = "label23";
		this.label23.Size = new System.Drawing.Size(106, 13);
		this.label23.TabIndex = 10;
		this.label23.Text = "Output Colour Space";
		this.label21.AutoSize = true;
		this.label21.Location = new System.Drawing.Point(217, 11);
		this.label21.Name = "label21";
		this.label21.Size = new System.Drawing.Size(93, 13);
		this.label21.TabIndex = 10;
		this.label21.Text = "Debayer Algorithm";
		this.raw_hpfCB.AutoSize = true;
		this.raw_hpfCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.raw_hpfCB.Location = new System.Drawing.Point(3, 16);
		this.raw_hpfCB.Name = "raw_hpfCB";
		this.raw_hpfCB.Size = new System.Drawing.Size(413, 17);
		this.raw_hpfCB.TabIndex = 16;
		this.raw_hpfCB.Text = "Enable Raw Image Hot Pixel Filter";
		this.raw_hpfCB.UseVisualStyleBackColor = true;
		this.binningGB.Controls.Add(this.label43);
		this.binningGB.Controls.Add(this.binning_calculationCB);
		this.binningGB.Controls.Add(this.pictureBox16);
		this.binningGB.Controls.Add(this.label42);
		this.binningGB.Controls.Add(this.binningCB);
		this.binningGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.binningGB.Location = new System.Drawing.Point(0, 0);
		this.binningGB.Name = "binningGB";
		this.binningGB.Size = new System.Drawing.Size(419, 79);
		this.binningGB.TabIndex = 19;
		this.binningGB.TabStop = false;
		this.binningGB.Text = "Binning";
		this.label43.AutoSize = true;
		this.label43.Location = new System.Drawing.Point(136, 46);
		this.label43.Name = "label43";
		this.label43.Size = new System.Drawing.Size(100, 13);
		this.label43.TabIndex = 17;
		this.label43.Text = "Binning Calculation:";
		this.pictureBox16.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox16.Image = PIPP_Application.Properties.Resources.binning;
		this.pictureBox16.Location = new System.Drawing.Point(394, 11);
		this.pictureBox16.Name = "pictureBox16";
		this.pictureBox16.Size = new System.Drawing.Size(20, 20);
		this.pictureBox16.TabIndex = 15;
		this.pictureBox16.TabStop = false;
		this.label42.AutoSize = true;
		this.label42.Location = new System.Drawing.Point(136, 19);
		this.label42.Name = "label42";
		this.label42.Size = new System.Drawing.Size(72, 13);
		this.label42.TabIndex = 10;
		this.label42.Text = "Binning Type:";
		this.debayerGB.BackColor = System.Drawing.Color.White;
		this.debayerGB.Controls.Add(this.db_patternLAB);
		this.debayerGB.Controls.Add(this.debay_typeCB);
		this.debayerGB.Controls.Add(this.debay_patternCB);
		this.debayerGB.Controls.Add(this.db_algorithmLAB);
		this.debayerGB.Controls.Add(this.debayerpatternPB);
		this.debayerGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.debayerGB.Location = new System.Drawing.Point(0, 349);
		this.debayerGB.Name = "debayerGB";
		this.debayerGB.Size = new System.Drawing.Size(425, 68);
		this.debayerGB.TabIndex = 16;
		this.debayerGB.TabStop = false;
		this.debayerGB.Text = "Colour Debayer Options";
		this.debayerGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.db_patternLAB.AutoSize = true;
		this.db_patternLAB.Location = new System.Drawing.Point(123, 46);
		this.db_patternLAB.Name = "db_patternLAB";
		this.db_patternLAB.Size = new System.Drawing.Size(87, 13);
		this.db_patternLAB.TabIndex = 20;
		this.db_patternLAB.Text = "Debayer Pattern:";
		this.db_algorithmLAB.AutoSize = true;
		this.db_algorithmLAB.Location = new System.Drawing.Point(8, 46);
		this.db_algorithmLAB.Name = "db_algorithmLAB";
		this.db_algorithmLAB.Size = new System.Drawing.Size(96, 13);
		this.db_algorithmLAB.TabIndex = 19;
		this.db_algorithmLAB.Text = "Debayer Algorithm:";
		this.debayerpatternPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.debayerpatternPB.Image = PIPP_Application.Properties.Resources.gbrg1;
		this.debayerpatternPB.Location = new System.Drawing.Point(397, 11);
		this.debayerpatternPB.Name = "debayerpatternPB";
		this.debayerpatternPB.Size = new System.Drawing.Size(20, 20);
		this.debayerpatternPB.TabIndex = 18;
		this.debayerpatternPB.TabStop = false;
		this.debayerenableGB.Controls.Add(this.bayerPB);
		this.debayerenableGB.Controls.Add(this.protectbayerCB);
		this.debayerenableGB.Controls.Add(this.debayerframesCB);
		this.debayerenableGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.debayerenableGB.Location = new System.Drawing.Point(0, 287);
		this.debayerenableGB.Name = "debayerenableGB";
		this.debayerenableGB.Size = new System.Drawing.Size(425, 62);
		this.debayerenableGB.TabIndex = 7;
		this.debayerenableGB.TabStop = false;
		this.debayerenableGB.Text = "Colour Debayer";
		this.debayerenableGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.bayerPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.bayerPB.Image = PIPP_Application.Properties.Resources.bayer_pattern;
		this.bayerPB.Location = new System.Drawing.Point(397, 11);
		this.bayerPB.Name = "bayerPB";
		this.bayerPB.Size = new System.Drawing.Size(20, 20);
		this.bayerPB.TabIndex = 10;
		this.bayerPB.TabStop = false;
		this.serfilesGB.Controls.Add(this.serPB);
		this.serfilesGB.Controls.Add(this.label15);
		this.serfilesGB.Controls.Add(this.serbitdepthCB);
		this.serfilesGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.serfilesGB.Location = new System.Drawing.Point(0, 43);
		this.serfilesGB.Name = "serfilesGB";
		this.serfilesGB.Size = new System.Drawing.Size(425, 49);
		this.serfilesGB.TabIndex = 8;
		this.serfilesGB.TabStop = false;
		this.serfilesGB.Text = "SER File Options";
		this.serPB.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.serPB.Image = PIPP_Application.Properties.Resources.ser;
		this.serPB.Location = new System.Drawing.Point(400, 11);
		this.serPB.Name = "serPB";
		this.serPB.Size = new System.Drawing.Size(20, 20);
		this.serPB.TabIndex = 13;
		this.serPB.TabStop = false;
		this.label15.AutoSize = true;
		this.label15.Location = new System.Drawing.Point(136, 23);
		this.label15.Name = "label15";
		this.label15.Size = new System.Drawing.Size(64, 13);
		this.label15.TabIndex = 9;
		this.label15.Text = "Pixel Depth:";
		this.avifilesGB.Controls.Add(this.pictureBox8);
		this.avifilesGB.Controls.Add(this.y800decodestrictCB);
		this.avifilesGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.avifilesGB.Location = new System.Drawing.Point(0, 0);
		this.avifilesGB.Name = "avifilesGB";
		this.avifilesGB.Size = new System.Drawing.Size(425, 43);
		this.avifilesGB.TabIndex = 6;
		this.avifilesGB.TabStop = false;
		this.avifilesGB.Text = "AVI File Options";
		this.pictureBox8.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox8.Image = PIPP_Application.Properties.Resources.avi;
		this.pictureBox8.Location = new System.Drawing.Point(400, 11);
		this.pictureBox8.Name = "pictureBox8";
		this.pictureBox8.Size = new System.Drawing.Size(20, 20);
		this.pictureBox8.TabIndex = 14;
		this.pictureBox8.TabStop = false;
		this.tabPage5.BackColor = System.Drawing.Color.White;
		this.tabPage5.Controls.Add(this.splitContainer1);
		this.tabPage5.Location = new System.Drawing.Point(4, 22);
		this.tabPage5.Name = "tabPage5";
		this.tabPage5.Padding = new System.Windows.Forms.Padding(3);
		this.tabPage5.Size = new System.Drawing.Size(854, 437);
		this.tabPage5.TabIndex = 7;
		this.tabPage5.Text = "Animation Options";
		this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
		this.splitContainer1.IsSplitterFixed = true;
		this.splitContainer1.Location = new System.Drawing.Point(3, 3);
		this.splitContainer1.Name = "splitContainer1";
		this.splitContainer1.Panel1.BackColor = System.Drawing.Color.White;
		this.splitContainer1.Panel1.Controls.Add(this.sequence_repeatGB);
		this.splitContainer1.Panel1.Controls.Add(this.reverse_playGB);
		this.splitContainer1.Panel1.Controls.Add(this.forward_playGB);
		this.splitContainer1.Panel2.BackColor = System.Drawing.Color.White;
		this.splitContainer1.Panel2.Controls.Add(this.groupBox9);
		this.splitContainer1.Size = new System.Drawing.Size(848, 431);
		this.splitContainer1.SplitterDistance = 420;
		this.splitContainer1.TabIndex = 10;
		this.sequence_repeatGB.BackColor = System.Drawing.Color.White;
		this.sequence_repeatGB.Controls.Add(this.pictureBox10);
		this.sequence_repeatGB.Controls.Add(this.panel5);
		this.sequence_repeatGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.sequence_repeatGB.Location = new System.Drawing.Point(0, 180);
		this.sequence_repeatGB.Name = "sequence_repeatGB";
		this.sequence_repeatGB.Size = new System.Drawing.Size(420, 118);
		this.sequence_repeatGB.TabIndex = 20;
		this.sequence_repeatGB.TabStop = false;
		this.sequence_repeatGB.Text = "Complete Sequence Repeat";
		this.sequence_repeatGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.pictureBox10.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox10.Image = PIPP_Application.Properties.Resources.repeat1;
		this.pictureBox10.Location = new System.Drawing.Point(395, 11);
		this.pictureBox10.Name = "pictureBox10";
		this.pictureBox10.Size = new System.Drawing.Size(20, 20);
		this.pictureBox10.TabIndex = 13;
		this.pictureBox10.TabStop = false;
		this.panel5.Controls.Add(this.animation_reverse_not_in_last_repeatCB);
		this.panel5.Controls.Add(this.animation_repeat_allNUD);
		this.panel5.Controls.Add(this.animation_repeat_allCB);
		this.panel5.Controls.Add(this.animation_repeat_allLAB);
		this.panel5.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel5.Location = new System.Drawing.Point(3, 16);
		this.panel5.Margin = new System.Windows.Forms.Padding(0);
		this.panel5.Name = "panel5";
		this.panel5.Size = new System.Drawing.Size(414, 80);
		this.panel5.TabIndex = 19;
		this.animation_reverse_not_in_last_repeatCB.AutoSize = true;
		this.animation_reverse_not_in_last_repeatCB.Checked = true;
		this.animation_reverse_not_in_last_repeatCB.CheckState = System.Windows.Forms.CheckState.Checked;
		this.animation_reverse_not_in_last_repeatCB.Enabled = false;
		this.animation_reverse_not_in_last_repeatCB.Location = new System.Drawing.Point(0, 51);
		this.animation_reverse_not_in_last_repeatCB.Name = "animation_reverse_not_in_last_repeatCB";
		this.animation_reverse_not_in_last_repeatCB.Size = new System.Drawing.Size(268, 17);
		this.animation_reverse_not_in_last_repeatCB.TabIndex = 21;
		this.animation_reverse_not_in_last_repeatCB.Text = "Do Not Do Reverse Play In Final Repeat (AVI Only)";
		this.animation_reverse_not_in_last_repeatCB.UseVisualStyleBackColor = true;
		this.animation_repeat_allCB.AutoSize = true;
		this.animation_repeat_allCB.Location = new System.Drawing.Point(0, 3);
		this.animation_repeat_allCB.Name = "animation_repeat_allCB";
		this.animation_repeat_allCB.Size = new System.Drawing.Size(176, 17);
		this.animation_repeat_allCB.TabIndex = 16;
		this.animation_repeat_allCB.Text = "Repeat All Frames In Sequence";
		this.animation_repeat_allCB.UseVisualStyleBackColor = true;
		this.animation_repeat_allCB.CheckedChanged += new System.EventHandler(this.animation_repeat_allCB_CheckedChanged);
		this.reverse_playGB.BackColor = System.Drawing.Color.White;
		this.reverse_playGB.Controls.Add(this.repeat_reverse_lastPAN);
		this.reverse_playGB.Controls.Add(this.pictureBox7);
		this.reverse_playGB.Controls.Add(this.animation_reverseCB);
		this.reverse_playGB.Dock = System.Windows.Forms.DockStyle.Top;
		this.reverse_playGB.Location = new System.Drawing.Point(0, 89);
		this.reverse_playGB.Name = "reverse_playGB";
		this.reverse_playGB.Size = new System.Drawing.Size(420, 91);
		this.reverse_playGB.TabIndex = 19;
		this.reverse_playGB.TabStop = false;
		this.reverse_playGB.Text = "Reverse Play";
		this.reverse_playGB.EnabledChanged += new System.EventHandler(this.EnabledChangedEventHandler);
		this.repeat_reverse_lastPAN.Controls.Add(this.repeat_reverse_lastLAB);
		this.repeat_reverse_lastPAN.Controls.Add(this.repeat_reverse_lastNUD);
		this.repeat_reverse_lastPAN.Controls.Add(this.repeat_reverse_lastCB);
		this.repeat_reverse_lastPAN.Dock = System.Windows.Forms.DockStyle.Top;
		this.repeat_reverse_lastPAN.Enabled = false;
		this.repeat_reverse_lastPAN.Location = new System.Drawing.Point(3, 33);
		this.repeat_reverse_lastPAN.Margin = new System.Windows.Forms.Padding(0);
		this.repeat_reverse_lastPAN.Name = "repeat_reverse_lastPAN";
		this.repeat_reverse_lastPAN.Size = new System.Drawing.Size(414, 51);
		this.repeat_reverse_lastPAN.TabIndex = 18;
		this.repeat_reverse_lastCB.AutoSize = true;
		this.repeat_reverse_lastCB.Location = new System.Drawing.Point(0, 3);
		this.repeat_reverse_lastCB.Name = "repeat_reverse_lastCB";
		this.repeat_reverse_lastCB.Size = new System.Drawing.Size(221, 17);
		this.repeat_reverse_lastCB.TabIndex = 17;
		this.repeat_reverse_lastCB.Text = "Pause Final Frame Of Each Reverse Play";
		this.repeat_reverse_lastCB.UseVisualStyleBackColor = true;
		this.repeat_reverse_lastCB.CheckedChanged += new System.EventHandler(this.repeat_reverse_lastCB_CheckedChanged);
		this.pictureBox7.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.pictureBox7.Image = PIPP_Application.Properties.Resources.reverse;
		this.pictureBox7.Location = new System.Drawing.Point(395, 11);
		this.pictureBox7.Name = "pictureBox7";
		this.pictureBox7.Size = new System.Drawing.Size(20, 20);
		this.pictureBox7.TabIndex = 13;
		this.pictureBox7.TabStop = false;
		this.animation_reverseCB.AutoSize = true;
		this.animation_reverseCB.Dock = System.Windows.Forms.DockStyle.Top;
		this.animation_reverseCB.Location = new System.Drawing.Point(3, 16);
		this.animation_reverseCB.Name = "animation_reverseCB";
		this.animation_reverseCB.Size = new System.Drawing.Size(414, 17);
		this.animation_reverseCB.TabIndex = 16;
		this.animation_reverseCB.Text = "Play All Frames In Reverse Order After Forward Play";
		this.animation_reverseCB.UseVisualStyleBackColor = true;
		this.animation_reverseCB.CheckedChanged += new System.EventHandler(this.animation_reverseCB_CheckedChanged);
		this.groupBox9.Controls.Add(this.avi_settings1LAB);
		this.groupBox9.Controls.Add(this.avi_settings4LAB);
		this.groupBox9.Controls.Add(this.avi_settings3LAB);
		this.groupBox9.Controls.Add(this.avi_settings2LAB);
		this.groupBox9.Controls.Add(this.label29);
		this.groupBox9.Controls.Add(this.label28);
		this.groupBox9.Controls.Add(this.label18);
		this.groupBox9.Controls.Add(this.label17);
		this.groupBox9.Dock = System.Windows.Forms.DockStyle.Top;
		this.groupBox9.Location = new System.Drawing.Point(0, 0);
		this.groupBox9.Name = "groupBox9";
		this.groupBox9.Size = new System.Drawing.Size(424, 105);
		this.groupBox9.TabIndex = 0;
		this.groupBox9.TabStop = false;
		this.groupBox9.Text = "Selected Output Settings";
		this.avi_settings1LAB.AutoSize = true;
		this.avi_settings1LAB.Location = new System.Drawing.Point(114, 20);
		this.avi_settings1LAB.Name = "avi_settings1LAB";
		this.avi_settings1LAB.Size = new System.Drawing.Size(25, 13);
		this.avi_settings1LAB.TabIndex = 6;
		this.avi_settings1LAB.Text = "Yes";
		this.avi_settings4LAB.AutoSize = true;
		this.avi_settings4LAB.Location = new System.Drawing.Point(114, 80);
		this.avi_settings4LAB.Name = "avi_settings4LAB";
		this.avi_settings4LAB.Size = new System.Drawing.Size(19, 13);
		this.avi_settings4LAB.TabIndex = 5;
		this.avi_settings4LAB.Text = "10";
		this.avi_settings3LAB.AutoSize = true;
		this.avi_settings3LAB.Location = new System.Drawing.Point(114, 60);
		this.avi_settings3LAB.Name = "avi_settings3LAB";
		this.avi_settings3LAB.Size = new System.Drawing.Size(13, 13);
		this.avi_settings3LAB.TabIndex = 4;
		this.avi_settings3LAB.Text = "5";
		this.avi_settings2LAB.AutoSize = true;
		this.avi_settings2LAB.Location = new System.Drawing.Point(114, 40);
		this.avi_settings2LAB.Name = "avi_settings2LAB";
		this.avi_settings2LAB.Size = new System.Drawing.Size(134, 13);
		this.avi_settings2LAB.TabIndex = 1;
		this.avi_settings2LAB.Text = "DIB (RAW Uncompressed)";
		this.label29.Location = new System.Drawing.Point(7, 80);
		this.label29.Name = "label29";
		this.label29.Size = new System.Drawing.Size(107, 13);
		this.label29.TabIndex = 3;
		this.label29.Text = "Frames Per Second:";
		this.label29.TextAlign = System.Drawing.ContentAlignment.TopRight;
		this.label28.Location = new System.Drawing.Point(7, 60);
		this.label28.Name = "label28";
		this.label28.Size = new System.Drawing.Size(107, 13);
		this.label28.TabIndex = 2;
		this.label28.Text = "Quality:";
		this.label28.TextAlign = System.Drawing.ContentAlignment.TopRight;
		this.label18.Location = new System.Drawing.Point(7, 40);
		this.label18.Name = "label18";
		this.label18.Size = new System.Drawing.Size(107, 13);
		this.label18.TabIndex = 1;
		this.label18.Text = "Codec:";
		this.label18.TextAlign = System.Drawing.ContentAlignment.TopRight;
		this.label17.Location = new System.Drawing.Point(6, 20);
		this.label17.Name = "label17";
		this.label17.Size = new System.Drawing.Size(108, 13);
		this.label17.TabIndex = 0;
		this.label17.Text = "Output File Type:";
		this.label17.TextAlign = System.Drawing.ContentAlignment.TopRight;
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(862, 487);
		base.Controls.Add(this.dotestrunBTN);
		base.Controls.Add(this.tabControl1);
		base.Controls.Add(this.menuStrip1);
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.MainMenuStrip = this.menuStrip1;
		base.Name = "Form1";
		this.Text = "OPIPP 0.1.2-alpha - Open PIPP";
		base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
		base.Load += new System.EventHandler(this.Form1_Load);
		this.menuStrip1.ResumeLayout(false);
		this.menuStrip1.PerformLayout();
		this.outputprefixGB.ResumeLayout(false);
		this.outputprefixGB.PerformLayout();
		this.groupBox8.ResumeLayout(false);
		this.groupBox8.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.formatPB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.avi_fpsNUD).EndInit();
		this.qualitylimitingGB.ResumeLayout(false);
		this.qualitylimitingGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.qualityframesNUD).EndInit();
		this.groupBox5.ResumeLayout(false);
		this.groupBox5.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.overexposedPB).EndInit();
		this.overexposedPANEL.ResumeLayout(false);
		this.overexposedPANEL.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.overexposedNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.cropwidthNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.cropheightNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.xoffsetNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.yoffsetNUD).EndInit();
		this.limitframesGB.ResumeLayout(false);
		this.limitframesGB.PerformLayout();
		this.limitframesPANEL2.ResumeLayout(false);
		this.limitframesPANEL2.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numframesNUD).EndInit();
		this.limitframesPANEL.ResumeLayout(false);
		this.limitframesPANEL.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.startframeNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.endframeNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.framerangePB).EndInit();
		this.panel1.ResumeLayout(false);
		this.panel1.PerformLayout();
		this.rgbsplitGB.ResumeLayout(false);
		this.rgbsplitGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.splitrgbPB).EndInit();
		this.convertomonoGB.ResumeLayout(false);
		this.convertomonoGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox3).EndInit();
		((System.ComponentModel.ISupportInitialize)this.codec_qualityNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.gammaNUD).EndInit();
		this.groupBox7.ResumeLayout(false);
		this.groupBox7.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.hot_pixel_thresholdNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_yNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.afb_xNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_heightNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_widthNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.aoi_xNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.afb_yNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.afb_heightNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.afb_widthNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.gif_fpsNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.resize_widthNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.resize_heightNUD).EndInit();
		this.framedropGB.ResumeLayout(false);
		this.framedropGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.outputframedroppingPB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.outputframedropNUD).EndInit();
		this.outputfiledirectoryGB.ResumeLayout(false);
		this.outputfiledirectoryGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox4).EndInit();
		this.output_subdirectoryGB.ResumeLayout(false);
		this.output_subdirectoryGB.PerformLayout();
		this.subdir_nameGB.ResumeLayout(false);
		this.subdir_nameGB.PerformLayout();
		this.qualityweightGB.ResumeLayout(false);
		this.qualityweightGB.PerformLayout();
		this.qualityoptionsGB.ResumeLayout(false);
		this.qualityoptionsGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.downsampleincNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.maxdownsampleNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.mindownsampleNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.thresholdNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.minsizeNUD).EndInit();
		this.histostretchGB.ResumeLayout(false);
		this.histostretchGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox2).EndInit();
		((System.ComponentModel.ISupportInitialize)this.hs_white_pointNUD).EndInit();
		this.groupBox3.ResumeLayout(false);
		this.groupBox3.PerformLayout();
		this.ipframedroppingGB.ResumeLayout(false);
		this.ipframedroppingGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.framedroppingPB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.ipframedropNUD).EndInit();
		this.colourdetectionGB.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.colour_monoPB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.inputframespersecondNUD).EndInit();
		this.winjuposGB.ResumeLayout(false);
		this.winjuposGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.winjuposPB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.input_gainNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.gif_border_tolNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.gif_trans_pixel_tolNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.gif_bit_depthNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.gif_lossy_comp_levelNUD).EndInit();
		this.winjupossubPANEL.ResumeLayout(false);
		this.winjupossubPANEL.PerformLayout();
		this.groupBox1.ResumeLayout(false);
		this.groupBox1.PerformLayout();
		this.minpixelvalueGB.ResumeLayout(false);
		this.minpixelvalueGB.PerformLayout();
		this.groupBox6.ResumeLayout(false);
		this.groupBox6.PerformLayout();
		this.flip_rotateGB.ResumeLayout(false);
		this.flip_rotateGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.rotatePB).EndInit();
		((System.ComponentModel.ISupportInitialize)this.animation_repeat_allNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.repeat_forward_lastNUD).EndInit();
		((System.ComponentModel.ISupportInitialize)this.repeat_reverse_lastNUD).EndInit();
		this.forward_playGB.ResumeLayout(false);
		this.forward_playGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox6).EndInit();
		this.panel3.ResumeLayout(false);
		this.panel3.PerformLayout();
		this.tabPage6.ResumeLayout(false);
		this.splitContainer6.Panel1.ResumeLayout(false);
		this.splitContainer6.Panel1.PerformLayout();
		this.splitContainer6.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer6).EndInit();
		this.splitContainer6.ResumeLayout(false);
		this.mainprocessingGB.ResumeLayout(false);
		this.anim_progPAN.ResumeLayout(false);
		this.anim_progPAN.PerformLayout();
		this.qual_progressPAN.ResumeLayout(false);
		this.qual_progressPAN.PerformLayout();
		this.panel7.ResumeLayout(false);
		this.panel7.PerformLayout();
		this.panel8.ResumeLayout(false);
		this.panel8.PerformLayout();
		this.calibrationGB.ResumeLayout(false);
		this.flat_stacking_progress_PAN.ResumeLayout(false);
		this.flat_stacking_progress_PAN.PerformLayout();
		this.flat_dark_stacking_progress_PAN.ResumeLayout(false);
		this.flat_dark_stacking_progress_PAN.PerformLayout();
		this.dark_stacking_progress_PAN.ResumeLayout(false);
		this.dark_stacking_progress_PAN.PerformLayout();
		this.statusGB.ResumeLayout(false);
		this.statusGB.PerformLayout();
		this.controlGB.ResumeLayout(false);
		this.controlGB.PerformLayout();
		this.outputGB.ResumeLayout(false);
		this.outputGB.PerformLayout();
		this.tabPage4.ResumeLayout(false);
		this.splitContainer5.Panel1.ResumeLayout(false);
		this.splitContainer5.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer5).EndInit();
		this.splitContainer5.ResumeLayout(false);
		this.prefix_PAN.ResumeLayout(false);
		this.renumberGB.ResumeLayout(false);
		this.renumberGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.norenumberPB).EndInit();
		this.animimated_gif_optionsGB.ResumeLayout(false);
		this.animimated_gif_optionsGB.PerformLayout();
		this.gif_advanced_optionsGB.ResumeLayout(false);
		this.gif_advanced_optionsGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox15).EndInit();
		this.avi_outputGB.ResumeLayout(false);
		this.avi_outputGB.PerformLayout();
		this.old_format_aviPAN.ResumeLayout(false);
		this.old_format_aviPAN.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox1).EndInit();
		this.codec_qualityGB.ResumeLayout(false);
		this.codec_qualityGB.PerformLayout();
		this.tabPage3.ResumeLayout(false);
		this.splitContainer4.Panel1.ResumeLayout(false);
		this.splitContainer4.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer4).EndInit();
		this.splitContainer4.ResumeLayout(false);
		this.qualityalgorithmGB.ResumeLayout(false);
		this.qualityalgorithmGB.PerformLayout();
		this.qualityenableGB.ResumeLayout(false);
		this.qualityenableGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox5).EndInit();
		this.tabPage2.ResumeLayout(false);
		this.splitContainer2.Panel1.ResumeLayout(false);
		this.splitContainer2.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer2).EndInit();
		this.splitContainer2.ResumeLayout(false);
		this.noise_filterGB.ResumeLayout(false);
		this.noise_filterGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox12).EndInit();
		this.input_gainGB.ResumeLayout(false);
		this.input_gainGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox13).EndInit();
		this.groupBox2.ResumeLayout(false);
		this.groupBox2.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox17).EndInit();
		this.surface_stabiliseGB.ResumeLayout(false);
		this.surface_stabiliseGB.PerformLayout();
		this.area_of_interestGB.ResumeLayout(false);
		this.area_of_interestGB.PerformLayout();
		this.anchor_feature_boxGB.ResumeLayout(false);
		this.anchor_feature_boxGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox14).EndInit();
		this.croppingGB.ResumeLayout(false);
		this.croppingGB.PerformLayout();
		this.enablecropping3PANEL.ResumeLayout(false);
		this.enableoffsetPANEL.ResumeLayout(false);
		this.enableoffsetPANEL.PerformLayout();
		this.enablecroppingPANEL.ResumeLayout(false);
		this.enablecroppingPANEL.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.cropPB).EndInit();
		this.planetdetectGB.ResumeLayout(false);
		this.planetdetectGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.objectdetectPB).EndInit();
		this.edge_of_shadowPAN.ResumeLayout(false);
		this.edge_of_shadowPAN.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.moon_shadowPB).EndInit();
		this.groupBox10.ResumeLayout(false);
		this.groupBox10.PerformLayout();
		this.tabPage1.ResumeLayout(false);
		this.image_filesTAB_CONTROL.ResumeLayout(false);
		this.tab_image_files.ResumeLayout(false);
		this.tableLayoutPanel1.ResumeLayout(false);
		this.image_filesGB.ResumeLayout(false);
		this.panel2.ResumeLayout(false);
		this.runtypeGB.ResumeLayout(false);
		this.runtypeGB.PerformLayout();
		this.tab_dark_files.ResumeLayout(false);
		this.tableLayoutPanel3.ResumeLayout(false);
		this.dark_filesGB.ResumeLayout(false);
		this.panel6.ResumeLayout(false);
		this.hot_pixels_optionsGB.ResumeLayout(false);
		this.hot_pixels_optionsGB.PerformLayout();
		this.dark_frame_optionsGB.ResumeLayout(false);
		this.dark_frame_optionsGB.PerformLayout();
		this.tab_flat_files.ResumeLayout(false);
		this.tableLayoutPanel4.ResumeLayout(false);
		this.flat_filesGB.ResumeLayout(false);
		this.panel9.ResumeLayout(false);
		this.flat_frame_optionsGB.ResumeLayout(false);
		this.flat_frame_optionsGB.PerformLayout();
		this.tab_flat_dark_files.ResumeLayout(false);
		this.tableLayoutPanel5.ResumeLayout(false);
		this.flat_dark_filesGB.ResumeLayout(false);
		this.panel10.ResumeLayout(false);
		this.flat_dark_frame_optionsGB.ResumeLayout(false);
		this.flat_dark_frame_optionsGB.PerformLayout();
		this.tabControl1.ResumeLayout(false);
		this.tabPage7.ResumeLayout(false);
		this.splitContainer7.Panel1.ResumeLayout(false);
		this.splitContainer7.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer7).EndInit();
		this.splitContainer7.ResumeLayout(false);
		this.date_timeGB.ResumeLayout(false);
		this.date_timeGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox11).EndInit();
		this.raw_file_optionsGB.ResumeLayout(false);
		this.raw_file_optionsGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox9).EndInit();
		this.raw_colour_optionsPANEL.ResumeLayout(false);
		this.raw_colour_optionsPANEL.PerformLayout();
		this.binningGB.ResumeLayout(false);
		this.binningGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox16).EndInit();
		this.debayerGB.ResumeLayout(false);
		this.debayerGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.debayerpatternPB).EndInit();
		this.debayerenableGB.ResumeLayout(false);
		this.debayerenableGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.bayerPB).EndInit();
		this.serfilesGB.ResumeLayout(false);
		this.serfilesGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.serPB).EndInit();
		this.avifilesGB.ResumeLayout(false);
		this.avifilesGB.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox8).EndInit();
		this.tabPage5.ResumeLayout(false);
		this.splitContainer1.Panel1.ResumeLayout(false);
		this.splitContainer1.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.splitContainer1).EndInit();
		this.splitContainer1.ResumeLayout(false);
		this.sequence_repeatGB.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.pictureBox10).EndInit();
		this.panel5.ResumeLayout(false);
		this.panel5.PerformLayout();
		this.reverse_playGB.ResumeLayout(false);
		this.reverse_playGB.PerformLayout();
		this.repeat_reverse_lastPAN.ResumeLayout(false);
		this.repeat_reverse_lastPAN.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox7).EndInit();
		this.groupBox9.ResumeLayout(false);
		this.groupBox9.PerformLayout();
		base.ResumeLayout(false);
		base.PerformLayout();
	}

	private void source_filelist_FileListUpdated(object sender, EventArgs e)
	{
		FileListEventArgs e2 = (FileListEventArgs)e;
		if (e2.FrameCount == 0)
		{
			remove_button.Enabled = false;
			file_up_BUT.Enabled = false;
			file_down_BUT.Enabled = false;
			remove_all_button.Enabled = false;
		}
		else
		{
			remove_all_button.Enabled = true;
		}
		bool flag = false;
		if (e2.FrameCount > 0 && last_source_file_frame_count == 0)
		{
			flag = true;
		}
		last_source_file_frame_count = e2.FrameCount;
		tab_image_files.Text = "Image Files (" + e2.FrameCount + " frames)";
		update_date_time_extracted();
		if (e2.ImageAdded && !joinmodeRB.Checked)
		{
			joinmodeRB.Checked = true;
			MessageBoxEx.Show("Join mode had been selected because one or\r\nmore source files is an individual image file", "Add Source Files", MessageBoxButtons.OK, MessageBoxIcon.Asterisk, 10000u);
		}
		if (e2.SerBayerPattern >= 0 && !debayerframesCB.Checked)
		{
			DialogResult dialogResult = MessageBox.Show("A SER file with a bayer pattern has been added.  Do you want to enable debayering and select the correct bayer pattern?", "Add Source Files", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (dialogResult == DialogResult.Yes)
			{
				debay_patternCB.SelectedIndex = e2.SerBayerPattern;
				debayerframesCB.Checked = true;
			}
			else
			{
				protectbayerCB.Checked = true;
			}
		}
		if (e2.AviBayerPattern >= 0 && !debayerframesCB.Checked && !e2.By8AviAdded)
		{
			DialogResult dialogResult2 = MessageBox.Show("An AVI file with a bayer pattern (" + e2.AviBayerPattern switch
			{
				0 => "GRBG", 
				1 => "BGGR", 
				2 => "RGGB", 
				_ => "GRBG", 
			} + ") has been added.  Do you want to enable debayering and select the correct bayer pattern?\n\nNote: Detection of bayer patterns in AVI files is not 100% accurate and is only designed to work with astro images. You should check that the correct bayer pattern has been selected before starting processing.", "Add Source Files", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (dialogResult2 == DialogResult.Yes)
			{
				debay_patternCB.SelectedIndex = e2.AviBayerPattern;
				debayerframesCB.Checked = true;
			}
			else
			{
				protectbayerCB.Checked = true;
			}
		}
		if (e2.By8AviAdded && !debayerframesCB.Checked)
		{
			DialogResult dialogResult3 = MessageBox.Show("An AVI file with the BY8 Codec has been added.  Do you want to enable debayering and select the correct bayer pattern?", "Add Source Files", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (dialogResult3 == DialogResult.Yes)
			{
				debay_patternCB.SelectedIndex = 0;
				debayerframesCB.Checked = true;
			}
			else
			{
				protectbayerCB.Checked = true;
			}
		}
		if (flag)
		{
			do_test_run(show_detected_pixels_value: false);
		}
	}

	private void addsourceBTN_Click(object sender, EventArgs e)
	{
		image_filelist.addsourceBTN_Click(sender, e);
	}

	private void remove_button_Click(object sender, EventArgs e)
	{
		image_filelist.remove_button_Click(sender, e);
	}

	private void remove_all_button_Click(object sender, EventArgs e)
	{
		image_filelist.remove_all_button_Click(sender, e);
	}

	private void source_filelist_ItemSelectionChanged(object sender, EventArgs e)
	{
		if (image_filelist.SelectedItems.Count > 0)
		{
			file_up_BUT.Enabled = true;
			file_down_BUT.Enabled = true;
			remove_button.Enabled = true;
		}
		else
		{
			file_up_BUT.Enabled = false;
			file_down_BUT.Enabled = false;
			remove_button.Enabled = false;
		}
	}

	private void file_up_BUT_Click(object sender, EventArgs e)
	{
		image_filelist.file_up_BUT_Click(sender, e);
	}

	private void file_down_BUT_Click(object sender, EventArgs e)
	{
		image_filelist.file_down_BUT_Click(sender, e);
	}

	private void planetaryCB_CheckedChanged(object sender, EventArgs e)
	{
		if (planetaryCB.Checked)
		{
			solar_full_discCB.Checked = false;
			solar_closeupCB.Checked = false;
			issCB.Checked = false;
			planetary_animation_aviCB.Checked = false;
			planetary_animation_gifCB.Checked = false;
			avi_archiveCB.Checked = false;
		}
		enable_processing_options();
	}

	private void lunarCB_CheckedChanged(object sender, EventArgs e)
	{
		if (solar_full_discCB.Checked)
		{
			planetaryCB.Checked = false;
			solar_closeupCB.Checked = false;
			issCB.Checked = false;
			planetary_animation_aviCB.Checked = false;
			planetary_animation_gifCB.Checked = false;
			avi_archiveCB.Checked = false;
		}
		enable_processing_options();
	}

	private void solarCB_CheckedChanged(object sender, EventArgs e)
	{
		if (solar_closeupCB.Checked)
		{
			planetaryCB.Checked = false;
			solar_full_discCB.Checked = false;
			issCB.Checked = false;
			planetary_animation_aviCB.Checked = false;
			planetary_animation_gifCB.Checked = false;
			avi_archiveCB.Checked = false;
		}
		enable_processing_options();
	}

	private void issCB_CheckedChanged(object sender, EventArgs e)
	{
		if (issCB.Checked)
		{
			planetaryCB.Checked = false;
			solar_full_discCB.Checked = false;
			solar_closeupCB.Checked = false;
			planetary_animation_aviCB.Checked = false;
			planetary_animation_gifCB.Checked = false;
			avi_archiveCB.Checked = false;
		}
		enable_processing_options();
	}

	private void planetaryanimationCB_CheckedChanged(object sender, EventArgs e)
	{
		if (planetary_animation_aviCB.Checked)
		{
			planetaryCB.Checked = false;
			solar_full_discCB.Checked = false;
			solar_closeupCB.Checked = false;
			issCB.Checked = false;
			planetary_animation_gifCB.Checked = false;
			avi_archiveCB.Checked = false;
		}
		enable_processing_options();
	}

	private void planetary_animation_gifCB_CheckedChanged(object sender, EventArgs e)
	{
		if (planetary_animation_gifCB.Checked)
		{
			planetaryCB.Checked = false;
			solar_full_discCB.Checked = false;
			solar_closeupCB.Checked = false;
			issCB.Checked = false;
			planetary_animation_aviCB.Checked = false;
			avi_archiveCB.Checked = false;
		}
		enable_processing_options();
	}

	private void avi_archiveCB_CheckedChanged(object sender, EventArgs e)
	{
		if (avi_archiveCB.Checked)
		{
			planetaryCB.Checked = false;
			solar_full_discCB.Checked = false;
			solar_closeupCB.Checked = false;
			issCB.Checked = false;
			planetary_animation_aviCB.Checked = false;
			planetary_animation_gifCB.Checked = false;
		}
		enable_processing_options();
	}

	private void enable_processing_options()
	{
		Color green = Color.Green;
		Color controlText = SystemColors.ControlText;
		if (checkplanetCB.ForeColor == green)
		{
			checkplanetCB.ForeColor = controlText;
			checkplanetCB.Checked = false;
		}
		if (minsizeNUD.ForeColor == green)
		{
			minsizeNUD.ForeColor = controlText;
			minsizeNUD.Value = 15m;
		}
		if (outputpngRB.ForeColor == green)
		{
			outputpngRB.ForeColor = controlText;
			outputaviRB.Checked = true;
		}
		if (outputtifRB.ForeColor == green)
		{
			outputtifRB.ForeColor = controlText;
			outputtifRB.Checked = true;
		}
		if (enablecropCB.ForeColor == green)
		{
			enablecropCB.ForeColor = controlText;
			enablecropCB.Checked = false;
		}
		if (centreplanetCB.ForeColor == green)
		{
			centreplanetCB.ForeColor = controlText;
			centreplanetCB.Checked = false;
		}
		if (cropwidthNUD.ForeColor == green)
		{
			cropwidthNUD.ForeColor = controlText;
			cropwidthNUD.Value = 448m;
		}
		if (cropheightNUD.ForeColor == green)
		{
			cropheightNUD.ForeColor = controlText;
			cropheightNUD.Value = 448m;
		}
		if (noframerenumberCB.ForeColor == green)
		{
			noframerenumberCB.ForeColor = controlText;
		}
		if (planetdetectGB.ForeColor == green)
		{
			planetdetectGB.Enabled = false;
		}
		if (hs_white_pointCB.ForeColor == green)
		{
			hs_white_pointCB.Checked = false;
			hs_white_pointCB.ForeColor = controlText;
		}
		if (hs_white_pointNUD.ForeColor == green)
		{
			hs_white_pointNUD.Value = 75m;
			hs_white_pointNUD.ForeColor = controlText;
		}
		if (hs_black_pointCB.ForeColor == green)
		{
			hs_black_pointCB.Checked = false;
			hs_black_pointCB.ForeColor = controlText;
		}
		if (rgbhistoCB.ForeColor == green)
		{
			rgbhistoCB.Checked = true;
			rgbhistoCB.ForeColor = controlText;
		}
		if (autothresholdCB.ForeColor == green)
		{
			autothresholdCB.Checked = true;
			autothresholdCB.ForeColor = controlText;
		}
		if (thresholdNUD.ForeColor == green)
		{
			thresholdNUD.Value = 40m;
			thresholdNUD.ForeColor = controlText;
		}
		stabilise_noneRB.Checked = true;
		if (stabilise_noneRB.ForeColor == green)
		{
			stabilise_noneRB.ForeColor = controlText;
		}
		if (stabilise_objectRB.ForeColor == green)
		{
			stabilise_objectRB.ForeColor = controlText;
		}
		if (stabilise_surfaceRB.ForeColor == green)
		{
			stabilise_surfaceRB.ForeColor = controlText;
		}
		if (enableSurfaceTrackingCB.ForeColor == green)
		{
			enableSurfaceTrackingCB.Checked = false;
			enableSurfaceTrackingCB.ForeColor = controlText;
		}
		if (enablestabilisationCB.ForeColor == green)
		{
			enablestabilisationCB.Checked = false;
			enablestabilisationCB.ForeColor = controlText;
		}
		if (qualitysortingCB.ForeColor == green)
		{
			qualitysortingCB.Checked = false;
			qualitysortingCB.ForeColor = controlText;
		}
		if (qreorderCB.ForeColor == green)
		{
			qreorderCB.Checked = true;
			qreorderCB.ForeColor = controlText;
		}
		if (qualityframelimitCB.ForeColor == green)
		{
			qualityframelimitCB.Checked = true;
			qualityframelimitCB.ForeColor = controlText;
		}
		if (totalpixelalgorithmCB.ForeColor == green)
		{
			defaultalgorithmCB.Checked = true;
			totalpixelalgorithmCB.ForeColor = controlText;
		}
		if (convmonoCB.ForeColor == green)
		{
			convmonoCB.Checked = false;
			convmonoCB.ForeColor = controlText;
		}
		if (repeat_forward_lastCB.ForeColor == green)
		{
			repeat_forward_lastCB.Checked = false;
			repeat_forward_lastCB.ForeColor = controlText;
		}
		if (repeat_forward_lastNUD.ForeColor == green)
		{
			repeat_forward_lastNUD.Value = 1m;
			repeat_forward_lastNUD.ForeColor = controlText;
		}
		if (animation_reverseCB.ForeColor == green)
		{
			animation_reverseCB.Checked = false;
			animation_reverseCB.ForeColor = controlText;
		}
		if (repeat_reverse_lastCB.ForeColor == green)
		{
			repeat_reverse_lastCB.Checked = false;
			repeat_reverse_lastCB.ForeColor = controlText;
		}
		if (repeat_reverse_lastNUD.ForeColor == green)
		{
			repeat_reverse_lastNUD.Value = 1m;
			repeat_reverse_lastNUD.ForeColor = controlText;
		}
		if (animation_repeat_allCB.ForeColor == green)
		{
			animation_repeat_allCB.Checked = false;
			animation_repeat_allCB.ForeColor = controlText;
		}
		if (animation_repeat_allNUD.ForeColor == green)
		{
			animation_repeat_allNUD.Value = 1m;
			animation_repeat_allNUD.ForeColor = controlText;
		}
		if (animation_reverse_not_in_last_repeatCB.ForeColor == green)
		{
			animation_reverse_not_in_last_repeatCB.Checked = true;
			animation_reverse_not_in_last_repeatCB.ForeColor = controlText;
		}
		if (outputaviRB.ForeColor == green || outputgifRB.ForeColor == green)
		{
			outputgifRB.Checked = false;
			outputgifRB.ForeColor = controlText;
			outputaviRB.Checked = true;
			outputaviRB.ForeColor = controlText;
		}
		if (gif_fpsCB.ForeColor == green)
		{
			gif_fpsCB.Checked = false;
			gif_fpsCB.ForeColor = controlText;
		}
		if (gif_fpsNUD.ForeColor == green)
		{
			gif_fpsNUD.Value = 3m;
			gif_fpsNUD.ForeColor = controlText;
		}
		if (avi_fpsCB.ForeColor == green)
		{
			avi_fpsCB.ForeColor = controlText;
			avi_fpsCB.Checked = true;
		}
		if (avi_fpsNUD.ForeColor == green)
		{
			avi_fpsNUD.Value = 60m;
			avi_fpsNUD.ForeColor = controlText;
		}
		if (repeat_forward_lastNUD.ForeColor == green)
		{
			repeat_forward_lastNUD.Value = 1m;
			repeat_forward_lastNUD.ForeColor = controlText;
		}
		if (old_format_aviCB.ForeColor == green)
		{
			old_format_aviCB.Checked = false;
			old_format_aviCB.ForeColor = controlText;
		}
		if (avi_codecCB.ForeColor == green)
		{
			avi_codecCB.SelectedIndex = 0;
			avi_codecCB.ForeColor = controlText;
		}
		if (outputsuffixTB.ForeColor == green)
		{
			outputsuffixTB.Text = "_pipp";
			outputsuffixTB.ForeColor = controlText;
		}
		if (create_output_subdirCB.ForeColor == green)
		{
			create_output_subdirCB.Checked = true;
			create_output_subdirCB.ForeColor = controlText;
		}
		if (outputaviRB.ForeColor == green)
		{
			outputaviRB.ForeColor = controlText;
			outputaviRB.Checked = true;
		}
		if (includequalityvalueCB.ForeColor == green)
		{
			includequalityvalueCB.ForeColor = controlText;
			includequalityvalueCB.Checked = false;
		}
		if (planetaryCB.Checked)
		{
			stabilise_objectRB.Checked = true;
			stabilise_objectRB.ForeColor = green;
			planetdetectGB.Enabled = true;
			planetdetectGB.BackColor = Color.White;
			checkplanetCB.Checked = true;
			checkplanetCB.ForeColor = green;
			noframerenumberCB.Checked = false;
			minsizeNUD.Value = 25m;
			centreplanetCB.Checked = true;
			centreplanetCB.ForeColor = green;
			enablecropCB.Checked = true;
			enablecropCB.ForeColor = green;
			cropwidthNUD.Value = 448m;
			cropwidthNUD.ForeColor = green;
			cropheightNUD.Value = 448m;
			cropheightNUD.ForeColor = green;
			minpixelvalueGB.BackColor = Color.White;
		}
		else if (solar_full_discCB.Checked)
		{
			stabilise_objectRB.Checked = true;
			stabilise_objectRB.ForeColor = green;
			planetdetectGB.Enabled = true;
			planetdetectGB.BackColor = Color.White;
			checkplanetCB.Checked = true;
			checkplanetCB.ForeColor = green;
			noframerenumberCB.Checked = false;
			minsizeNUD.Value = 50m;
			centreplanetCB.Checked = true;
			centreplanetCB.ForeColor = green;
			enablecropCB.Checked = true;
			enablecropCB.ForeColor = green;
			cropwidthNUD.Value = 1200m;
			cropwidthNUD.ForeColor = green;
			cropheightNUD.Value = 1200m;
			cropheightNUD.ForeColor = green;
			qualitysortingCB.Checked = true;
			qualitysortingCB.ForeColor = green;
			qreorderCB.Checked = true;
			qreorderCB.ForeColor = green;
			qualityframelimitCB.Checked = false;
			qualityframelimitCB.ForeColor = green;
			totalpixelalgorithmCB.Checked = true;
			totalpixelalgorithmCB.ForeColor = green;
			convmonoCB.Checked = true;
			convmonoCB.ForeColor = green;
			outputtifRB.Checked = true;
			outputtifRB.ForeColor = green;
			includequalityvalueCB.Checked = true;
			includequalityvalueCB.ForeColor = green;
		}
		else if (solar_closeupCB.Checked)
		{
			stabilise_surfaceRB.Checked = true;
			stabilise_surfaceRB.ForeColor = green;
			enableSurfaceTrackingCB.Checked = true;
			enableSurfaceTrackingCB.ForeColor = green;
			enablestabilisationCB.Checked = true;
			enablestabilisationCB.ForeColor = green;
			planetdetectGB.Enabled = false;
			planetdetectGB.BackColor = Color.WhiteSmoke;
			noframerenumberCB.Checked = false;
			enablecropCB.Checked = false;
			minpixelvalueGB.BackColor = Color.WhiteSmoke;
			convmonoCB.Checked = true;
			convmonoCB.ForeColor = green;
		}
		else if (issCB.Checked)
		{
			stabilise_objectRB.Checked = true;
			stabilise_objectRB.ForeColor = green;
			planetdetectGB.Enabled = true;
			planetdetectGB.BackColor = Color.White;
			checkplanetCB.Checked = true;
			checkplanetCB.ForeColor = green;
			minsizeNUD.Value = 10m;
			minsizeNUD.ForeColor = green;
			centreplanetCB.Checked = true;
			centreplanetCB.ForeColor = green;
			noframerenumberCB.Checked = true;
			noframerenumberCB.ForeColor = green;
			enablecropCB.Checked = true;
			enablecropCB.ForeColor = green;
			cropwidthNUD.Value = 300m;
			cropwidthNUD.ForeColor = green;
			cropheightNUD.Value = 300m;
			cropheightNUD.ForeColor = green;
			minpixelvalueGB.BackColor = Color.White;
			outputtifRB.Checked = true;
			outputtifRB.ForeColor = green;
		}
		else if (planetary_animation_aviCB.Checked)
		{
			stabilise_objectRB.Checked = true;
			stabilise_objectRB.ForeColor = green;
			planetdetectGB.Enabled = true;
			planetdetectGB.BackColor = Color.White;
			checkplanetCB.Checked = true;
			checkplanetCB.ForeColor = green;
			centreplanetCB.Checked = true;
			centreplanetCB.ForeColor = green;
			autothresholdCB.Checked = false;
			autothresholdCB.ForeColor = green;
			thresholdNUD.Value = 20m;
			thresholdNUD.ForeColor = green;
			hs_white_pointCB.Checked = true;
			hs_white_pointCB.ForeColor = green;
			rgbhistoCB.Checked = false;
			rgbhistoCB.ForeColor = green;
			hs_white_pointNUD.Value = 95m;
			hs_white_pointNUD.ForeColor = green;
			hs_black_pointCB.Checked = true;
			hs_black_pointCB.ForeColor = green;
			outputaviRB.Checked = true;
			outputaviRB.ForeColor = green;
			avi_fpsCB.Checked = false;
			avi_fpsCB.ForeColor = green;
			avi_fpsNUD.Value = 10m;
			avi_fpsNUD.ForeColor = green;
			old_format_aviCB.Checked = true;
			old_format_aviCB.ForeColor = green;
			avi_codecCB.SelectedIndex = 2;
			avi_codecCB.ForeColor = green;
			repeat_forward_lastCB.Checked = true;
			repeat_forward_lastCB.ForeColor = green;
			repeat_forward_lastNUD.Value = avi_fpsNUD.Value * 2m;
			repeat_forward_lastNUD.ForeColor = green;
			animation_repeat_allCB.Checked = true;
			animation_repeat_allCB.ForeColor = green;
			animation_repeat_allNUD.Value = 5m;
			animation_repeat_allNUD.ForeColor = green;
		}
		else if (planetary_animation_gifCB.Checked)
		{
			stabilise_objectRB.Checked = true;
			stabilise_objectRB.ForeColor = green;
			planetdetectGB.Enabled = true;
			planetdetectGB.BackColor = Color.White;
			checkplanetCB.Checked = true;
			checkplanetCB.ForeColor = green;
			centreplanetCB.Checked = true;
			centreplanetCB.ForeColor = green;
			autothresholdCB.Checked = false;
			autothresholdCB.ForeColor = green;
			thresholdNUD.Value = 20m;
			thresholdNUD.ForeColor = green;
			hs_white_pointCB.Checked = true;
			hs_white_pointCB.ForeColor = green;
			rgbhistoCB.Checked = false;
			rgbhistoCB.ForeColor = green;
			hs_white_pointNUD.Value = 95m;
			hs_white_pointNUD.ForeColor = green;
			hs_black_pointCB.Checked = true;
			hs_black_pointCB.ForeColor = green;
			outputgifRB.Checked = true;
			outputgifRB.ForeColor = green;
			gif_fpsCB.Checked = false;
			gif_fpsCB.ForeColor = green;
			gif_fpsNUD.Value = 3m;
			gif_fpsNUD.ForeColor = green;
			repeat_forward_lastCB.Checked = true;
			repeat_forward_lastCB.ForeColor = green;
			repeat_forward_lastNUD.Value = gif_fpsNUD.Value * 2m;
			repeat_forward_lastNUD.ForeColor = green;
		}
		else if (avi_archiveCB.Checked)
		{
			stabilise_noneRB.Checked = true;
			stabilise_noneRB.ForeColor = green;
			outputaviRB.Checked = true;
			outputaviRB.ForeColor = green;
			outputsuffixTB.Text = "_ulrg";
			outputsuffixTB.ForeColor = green;
			avi_codecCB.SelectedIndex = 1;
			avi_codecCB.ForeColor = green;
			create_output_subdirCB.Checked = false;
			create_output_subdirCB.ForeColor = green;
		}
		else
		{
			planetdetectGB.Enabled = true;
			planetdetectGB.BackColor = Color.White;
			noframerenumberCB.Checked = false;
			if (checkplanetCB.Checked)
			{
				minpixelvalueGB.BackColor = Color.White;
			}
			else
			{
				minpixelvalueGB.BackColor = Color.WhiteSmoke;
			}
		}
	}

	private void reset_source_files()
	{
		batchmodeRB.Checked = true;
		planetaryCB.Checked = false;
		solar_full_discCB.Checked = false;
		solar_closeupCB.Checked = false;
		issCB.Checked = false;
		planetary_animation_aviCB.Checked = false;
		planetary_animation_gifCB.Checked = false;
		avi_archiveCB.Checked = false;
	}

	private int generate_cmdline_options_source_files_tab(ref List<string> arg_list)
	{
		if (image_filelist.Items.Count > 1 && joinmodeRB.Checked)
		{
			arg_list.Add("-join");
		}
		return 1;
	}
}
