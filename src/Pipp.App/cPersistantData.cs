namespace PIPP_Application;

public class cPersistantData
{
	public string source_directory;

	public int source_filter_index;

	public string options_directory;

	public string output_directory;

	public string custom_date_time_filter1;

	public string custom_date_time_filter2;

	public string custom_date_time_filter3;

	public bool winjupos_remember;

	public int inputdatetimeformat;

	public string raw_file_extensions;

	public cPersistantData()
	{
		source_directory = string.Empty;
		source_filter_index = 0;
		options_directory = string.Empty;
		output_directory = string.Empty;
		custom_date_time_filter1 = "<Enter Data/Time Format>";
		custom_date_time_filter2 = "<Enter Data/Time Format>";
		custom_date_time_filter3 = "<Enter Data/Time Format>";
		winjupos_remember = false;
		inputdatetimeformat = 0;
		raw_file_extensions = string.Empty;
	}
}
