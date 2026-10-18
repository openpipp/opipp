using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PIPP_Application;

public class cFileInfoBox : Form
{
	private string filename;

	private IContainer components;

	private RichTextBox file_infoRTB;

	private Button button1;

	private SaveFileDialog saveInfoDialog;

	private Button closeBUT;

	private Panel panel1;

	public cFileInfoBox(string filename)
	{
		this.filename = filename;
		InitializeComponent();
		file_infoRTB.Font = new Font("Courier New", 8f);
		file_infoRTB.MaxLength = int.MaxValue;
		string extension = Path.GetExtension(filename).ToLower();
		Show();
		if (extension == ".ser")
		{
			file_infoRTB.Text = "Please Wait: Analysing SER File";
		}
		else if (extension == ".avi")
		{
			file_infoRTB.Text = "Please Wait: Analysing AVI File";
		}
		else
		{
			file_infoRTB.Text = "Please Wait: Analysing FITS File";
		}
		Invalidate();
		Update();
		try
		{
			// Managed replacement for c_dll.ser_info/avi_info/fits_info: the report is built from
			// Opipp.Core's metadata probes instead of being written to a temporary file by the DLL.
			file_infoRTB.Text = ManagedEngineApi.DescribeFile(filename);
		}
		catch (Exception ex)
		{
			file_infoRTB.Text = "Unable to read file information:\r\n" + ex.Message;
		}
	}

	private void button1_Click(object sender, EventArgs e)
	{
		if (Path.GetExtension(filename).ToLower() == ".ser")
		{
			saveInfoDialog.Title = "Save Ser File Info";
		}
		else if (Path.GetExtension(filename).ToLower() == ".avi")
		{
			saveInfoDialog.Title = "Save AVI File Info";
		}
		else
		{
			saveInfoDialog.Title = "Save FITS File Info";
		}
		saveInfoDialog.Filter = "Text File|*.txt";
		saveInfoDialog.FileName = Path.GetFileNameWithoutExtension(filename) + "_info.txt";
		saveInfoDialog.InitialDirectory = Path.GetDirectoryName(filename);
		DialogResult dialogResult = saveInfoDialog.ShowDialog();
		if (dialogResult == DialogResult.OK && saveInfoDialog.FileName.Length > 0)
		{
			file_infoRTB.SaveFile(saveInfoDialog.FileName, RichTextBoxStreamType.UnicodePlainText);
		}
	}

	private void closeBUT_Click(object sender, EventArgs e)
	{
		Close();
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
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PIPP_Application.cFileInfoBox));
		this.file_infoRTB = new System.Windows.Forms.RichTextBox();
		this.button1 = new System.Windows.Forms.Button();
		this.saveInfoDialog = new System.Windows.Forms.SaveFileDialog();
		this.closeBUT = new System.Windows.Forms.Button();
		this.panel1 = new System.Windows.Forms.Panel();
		this.panel1.SuspendLayout();
		base.SuspendLayout();
		this.file_infoRTB.BackColor = System.Drawing.Color.Cornsilk;
		this.file_infoRTB.Dock = System.Windows.Forms.DockStyle.Fill;
		this.file_infoRTB.Location = new System.Drawing.Point(0, 0);
		this.file_infoRTB.Name = "file_infoRTB";
		this.file_infoRTB.ReadOnly = true;
		this.file_infoRTB.Size = new System.Drawing.Size(477, 442);
		this.file_infoRTB.TabIndex = 1;
		this.file_infoRTB.Text = "";
		this.button1.Location = new System.Drawing.Point(3, 3);
		this.button1.Name = "button1";
		this.button1.Size = new System.Drawing.Size(75, 23);
		this.button1.TabIndex = 0;
		this.button1.Text = "Save Info";
		this.button1.UseVisualStyleBackColor = true;
		this.button1.Click += new System.EventHandler(this.button1_Click);
		this.saveInfoDialog.DefaultExt = "txt";
		this.closeBUT.Location = new System.Drawing.Point(84, 3);
		this.closeBUT.Name = "closeBUT";
		this.closeBUT.Size = new System.Drawing.Size(75, 23);
		this.closeBUT.TabIndex = 1;
		this.closeBUT.Text = "Close";
		this.closeBUT.UseVisualStyleBackColor = true;
		this.closeBUT.Click += new System.EventHandler(this.closeBUT_Click);
		this.panel1.AutoSize = true;
		this.panel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panel1.Controls.Add(this.button1);
		this.panel1.Controls.Add(this.closeBUT);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel1.Location = new System.Drawing.Point(0, 442);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(477, 29);
		this.panel1.TabIndex = 2;
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(477, 471);
		base.Controls.Add(this.file_infoRTB);
		base.Controls.Add(this.panel1);
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.Name = "cFileInfoBox";
		this.Text = "PIPP - File Info";
		this.panel1.ResumeLayout(false);
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
