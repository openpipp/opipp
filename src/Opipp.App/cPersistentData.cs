using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace PIPP_Application;

public static class cPersistentData
{
	public static string source_directory;

	public static string dark_directory;

	public static string flat_directory;

	public static string flat_dark_directory;

	public static int source_filter_index;

	public static string options_directory;

	public static string output_directory;

	public static string custom_date_time_filter1;

	public static string custom_date_time_filter2;

	public static string custom_date_time_filter3;

	public static bool winjupos_remember;

	public static int inputdatetimeformat;

	public static string raw_file_extensions;

	public static bool check_for_updates;

	public static bool use_ram_buffers;

	public static bool open_output_folder;

	public static void init()
	{
		source_directory = string.Empty;
		dark_directory = string.Empty;
		flat_directory = string.Empty;
		flat_dark_directory = string.Empty;
		source_filter_index = 0;
		options_directory = string.Empty;
		output_directory = string.Empty;
		custom_date_time_filter1 = "<Enter Data/Time Format>";
		custom_date_time_filter2 = "<Enter Data/Time Format>";
		custom_date_time_filter3 = "<Enter Data/Time Format>";
		winjupos_remember = false;
		inputdatetimeformat = 0;
		raw_file_extensions = string.Empty;
		check_for_updates = true;
		use_ram_buffers = true;
		open_output_folder = false;
	}

	public static void save()
	{
		List<string> list = new List<string>();
		list.Add("[PIPP PERSISTENT DATA FILE]");
		list.Add("source_directory " + source_directory);
		list.Add("dark_directory " + dark_directory);
		list.Add("flat_directory " + flat_directory);
		list.Add("flat_dark_directory " + flat_dark_directory);
		list.Add("source_filter_index " + source_filter_index);
		list.Add("options_directory " + options_directory);
		list.Add("output_directory " + output_directory);
		list.Add("custom_date_time_filter1 " + custom_date_time_filter1);
		list.Add("custom_date_time_filter2 " + custom_date_time_filter2);
		list.Add("custom_date_time_filter3 " + custom_date_time_filter3);
		list.Add("winjupos_remember " + winjupos_remember);
		list.Add("inputdatetimeformat " + inputdatetimeformat);
		list.Add("raw_file_extensions " + raw_file_extensions);
		list.Add("check_for_updates " + check_for_updates);
		list.Add("use_ram_buffers " + use_ram_buffers);
		list.Add("open_output_folder " + open_output_folder);
		File.WriteAllLines(Application.LocalUserAppDataPath + "\\pipp.data", list);
	}

	public static int load()
	{
		string[] array;
		try
		{
			array = File.ReadAllLines(Application.LocalUserAppDataPath + "\\pipp.data");
		}
		catch
		{
			return -1;
		}
		if (array.Length == 0 || array[0] != "[PIPP PERSISTENT DATA FILE]")
		{
			return -1;
		}
		for (int i = 1; i < array.Length; i++)
		{
			string[] array2 = array[i].Split(' ');
			string text = string.Join(" ", array2, 1, array2.Length - 1);
			bool flag = !bool.TryParse(text, out var result);
			bool flag2 = !int.TryParse(text, out var result2);
			decimal.TryParse(text, out var _);
			switch (array2[0])
			{
			case "source_directory":
				source_directory = text;
				break;
			case "dark_directory":
				dark_directory = text;
				break;
			case "flat_directory":
				flat_directory = text;
				break;
			case "flat_dark_directory":
				flat_dark_directory = text;
				break;
			case "source_filter_index":
				source_filter_index = result2;
				break;
			case "options_directory":
				options_directory = text;
				break;
			case "output_directory":
				output_directory = text;
				break;
			case "custom_date_time_filter1":
				custom_date_time_filter1 = text;
				break;
			case "custom_date_time_filter2":
				custom_date_time_filter2 = text;
				break;
			case "custom_date_time_filter3":
				custom_date_time_filter3 = text;
				break;
			case "winjupos_remember":
				winjupos_remember = result;
				break;
			case "inputdatetimeformat":
				inputdatetimeformat = result2;
				break;
			case "raw_file_extensions":
				raw_file_extensions = text;
				break;
			case "check_for_updates":
				check_for_updates = result;
				break;
			case "use_ram_buffers":
				use_ram_buffers = result;
				break;
			case "open_output_folder":
				open_output_folder = result;
				break;
			}
		}
		return 0;
	}
}
