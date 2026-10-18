using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PIPP_Application;

public class infodialog : Form
{
	public static bool open;

	private IContainer components;

	private Button okBTN;

	private Label main_label;

	private Label label2;

	private Label copyrightLAB;

	private LinkLabel linkLabel1;

	private PictureBox logoPB;

	private Label dcraw_version_lab;

	private Label ffmpeg1LAB;

	private Label ffmpeg2LAB;

	private Label ffmpeg3LAB;

	private LinkLabel linkLabel2;

	private Label thirdpartyLAB;

	private Panel panel1;

	private Label neu_quant1LAB;

	private Label neu_quant2LAB;

	public infodialog(string name_and_version, string dcraw_version)
	{
		open = true;
		InitializeComponent();
		main_label.Text = name_and_version;
		dcraw_version_lab.Text = "dcraw v" + dcraw_version + " (c) 1997-2016 Dave Coffin";
	}

	~infodialog()
	{
		open = false;
	}

	private void okBTN_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		Process.Start("http://sites.google.com/site/astropipp/");
	}

	private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
	{
		Process.Start("http://sites.google.com/site/astropipp/downloads/ffmpeg-2.2.2.tar.bz2");
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
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PIPP_Application.infodialog));
		this.okBTN = new System.Windows.Forms.Button();
		this.main_label = new System.Windows.Forms.Label();
		this.label2 = new System.Windows.Forms.Label();
		this.copyrightLAB = new System.Windows.Forms.Label();
		this.linkLabel1 = new System.Windows.Forms.LinkLabel();
		this.logoPB = new System.Windows.Forms.PictureBox();
		this.dcraw_version_lab = new System.Windows.Forms.Label();
		this.ffmpeg1LAB = new System.Windows.Forms.Label();
		this.ffmpeg2LAB = new System.Windows.Forms.Label();
		this.ffmpeg3LAB = new System.Windows.Forms.Label();
		this.linkLabel2 = new System.Windows.Forms.LinkLabel();
		this.thirdpartyLAB = new System.Windows.Forms.Label();
		this.panel1 = new System.Windows.Forms.Panel();
		this.neu_quant1LAB = new System.Windows.Forms.Label();
		this.neu_quant2LAB = new System.Windows.Forms.Label();
		((System.ComponentModel.ISupportInitialize)this.logoPB).BeginInit();
		this.panel1.SuspendLayout();
		base.SuspendLayout();
		this.okBTN.Location = new System.Drawing.Point(376, 13);
		this.okBTN.Name = "okBTN";
		this.okBTN.Size = new System.Drawing.Size(75, 23);
		this.okBTN.TabIndex = 0;
		this.okBTN.Text = "OK";
		this.okBTN.UseVisualStyleBackColor = true;
		this.okBTN.Click += new System.EventHandler(this.okBTN_Click);
		this.main_label.AutoSize = true;
		this.main_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.main_label.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.main_label.Location = new System.Drawing.Point(215, 13);
		this.main_label.Name = "main_label";
		this.main_label.Size = new System.Drawing.Size(42, 16);
		this.main_label.TabIndex = 1;
		this.main_label.Text = "PIPP";
		this.label2.AutoSize = true;
		this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label2.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.label2.Location = new System.Drawing.Point(215, 32);
		this.label2.Name = "label2";
		this.label2.Size = new System.Drawing.Size(157, 13);
		this.label2.TabIndex = 2;
		this.label2.Text = "Planetary Imaging PreProcessor";
		this.copyrightLAB.AutoSize = true;
		this.copyrightLAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.copyrightLAB.Location = new System.Drawing.Point(215, 60);
		this.copyrightLAB.Name = "copyrightLAB";
		this.copyrightLAB.Size = new System.Drawing.Size(174, 13);
		this.copyrightLAB.TabIndex = 3;
		this.copyrightLAB.Text = "Copyright (c) 2012-2017 Chris Garry";
		this.copyrightLAB.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.linkLabel1.AutoSize = true;
		this.linkLabel1.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.linkLabel1.Location = new System.Drawing.Point(215, 46);
		this.linkLabel1.Name = "linkLabel1";
		this.linkLabel1.Size = new System.Drawing.Size(191, 13);
		this.linkLabel1.TabIndex = 4;
		this.linkLabel1.TabStop = true;
		this.linkLabel1.Text = "http://sites.google.com/site/astropipp/";
		this.linkLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
		this.logoPB.Image = (System.Drawing.Image)resources.GetObject("logoPB.Image");
		this.logoPB.Location = new System.Drawing.Point(21, 15);
		this.logoPB.Name = "logoPB";
		this.logoPB.Size = new System.Drawing.Size(150, 150);
		this.logoPB.TabIndex = 5;
		this.logoPB.TabStop = false;
		this.dcraw_version_lab.AutoSize = true;
		this.dcraw_version_lab.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.dcraw_version_lab.Location = new System.Drawing.Point(217, 104);
		this.dcraw_version_lab.Name = "dcraw_version_lab";
		this.dcraw_version_lab.Size = new System.Drawing.Size(197, 13);
		this.dcraw_version_lab.TabIndex = 6;
		this.dcraw_version_lab.Text = "dcraw v0.0.0 (c) 1997-2016 Dave Coffin";
		this.ffmpeg1LAB.AutoSize = true;
		this.ffmpeg1LAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.ffmpeg1LAB.Location = new System.Drawing.Point(217, 166);
		this.ffmpeg1LAB.Name = "ffmpeg1LAB";
		this.ffmpeg1LAB.Size = new System.Drawing.Size(190, 13);
		this.ffmpeg1LAB.TabIndex = 7;
		this.ffmpeg1LAB.Text = "PIPP uses DLLs from FFmpeg licensed";
		this.ffmpeg2LAB.AutoSize = true;
		this.ffmpeg2LAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.ffmpeg2LAB.Location = new System.Drawing.Point(217, 180);
		this.ffmpeg2LAB.Name = "ffmpeg2LAB";
		this.ffmpeg2LAB.Size = new System.Drawing.Size(106, 13);
		this.ffmpeg2LAB.TabIndex = 8;
		this.ffmpeg2LAB.Text = "under the LGPLv2.1.";
		this.ffmpeg3LAB.AutoSize = true;
		this.ffmpeg3LAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.ffmpeg3LAB.Location = new System.Drawing.Point(215, 194);
		this.ffmpeg3LAB.Name = "ffmpeg3LAB";
		this.ffmpeg3LAB.Size = new System.Drawing.Size(222, 13);
		this.ffmpeg3LAB.TabIndex = 9;
		this.ffmpeg3LAB.Text = "The FFmpeg source can be downloaded from";
		this.linkLabel2.AutoSize = true;
		this.linkLabel2.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.linkLabel2.Location = new System.Drawing.Point(217, 208);
		this.linkLabel2.Name = "linkLabel2";
		this.linkLabel2.Size = new System.Drawing.Size(28, 13);
		this.linkLabel2.TabIndex = 10;
		this.linkLabel2.TabStop = true;
		this.linkLabel2.Text = "here";
		this.linkLabel2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.linkLabel2.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel2_LinkClicked);
		this.thirdpartyLAB.AutoSize = true;
		this.thirdpartyLAB.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.thirdpartyLAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.thirdpartyLAB.Location = new System.Drawing.Point(215, 90);
		this.thirdpartyLAB.Name = "thirdpartyLAB";
		this.thirdpartyLAB.Size = new System.Drawing.Size(144, 13);
		this.thirdpartyLAB.TabIndex = 11;
		this.thirdpartyLAB.Text = "Third-party components:";
		this.panel1.BackColor = System.Drawing.SystemColors.Control;
		this.panel1.Controls.Add(this.okBTN);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel1.Location = new System.Drawing.Point(0, 249);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(469, 48);
		this.panel1.TabIndex = 12;
		this.neu_quant1LAB.AutoSize = true;
		this.neu_quant1LAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.neu_quant1LAB.Location = new System.Drawing.Point(217, 127);
		this.neu_quant1LAB.Name = "neu_quant1LAB";
		this.neu_quant1LAB.Size = new System.Drawing.Size(178, 13);
		this.neu_quant1LAB.TabIndex = 13;
		this.neu_quant1LAB.Text = "NeuQuant (c) 1994 Anthony Dekker";
		this.neu_quant2LAB.AutoSize = true;
		this.neu_quant2LAB.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.neu_quant2LAB.Location = new System.Drawing.Point(217, 140);
		this.neu_quant2LAB.Name = "neu_quant2LAB";
		this.neu_quant2LAB.Size = new System.Drawing.Size(194, 13);
		this.neu_quant2LAB.TabIndex = 14;
		this.neu_quant2LAB.Text = "Neural-Net image quantization algorithm";
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.Cornsilk;
		base.ClientSize = new System.Drawing.Size(469, 297);
		base.Controls.Add(this.neu_quant2LAB);
		base.Controls.Add(this.neu_quant1LAB);
		base.Controls.Add(this.panel1);
		base.Controls.Add(this.thirdpartyLAB);
		base.Controls.Add(this.linkLabel2);
		base.Controls.Add(this.ffmpeg3LAB);
		base.Controls.Add(this.ffmpeg2LAB);
		base.Controls.Add(this.ffmpeg1LAB);
		base.Controls.Add(this.dcraw_version_lab);
		base.Controls.Add(this.logoPB);
		base.Controls.Add(this.linkLabel1);
		base.Controls.Add(this.copyrightLAB);
		base.Controls.Add(this.label2);
		base.Controls.Add(this.main_label);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Fixed3D;
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.MaximizeBox = false;
		base.Name = "infodialog";
		this.Text = "About PIPP";
		((System.ComponentModel.ISupportInitialize)this.logoPB).EndInit();
		this.panel1.ResumeLayout(false);
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
