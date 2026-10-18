using System;
using System.Collections;
using System.Windows.Forms;

namespace PIPP_Application;

public class cListViewColumnSorter : IComparer
{
	private int ColumnToSort;

	private SortOrder OrderOfSort;

	private CaseInsensitiveComparer ObjectCompare;

	public int SortColumn
	{
		get
		{
			return ColumnToSort;
		}
		set
		{
			ColumnToSort = value;
		}
	}

	public SortOrder Order
	{
		get
		{
			return OrderOfSort;
		}
		set
		{
			OrderOfSort = value;
		}
	}

	public cListViewColumnSorter()
	{
		ColumnToSort = 0;
		OrderOfSort = SortOrder.None;
		ObjectCompare = new CaseInsensitiveComparer();
	}

	public int Compare(object x, object y)
	{
		int num;
		switch (ColumnToSort)
		{
		case 4:
		{
			int.TryParse(((ListViewItem)x).SubItems[ColumnToSort].Text, out var result);
			int.TryParse(((ListViewItem)y).SubItems[ColumnToSort].Text, out var result2);
			num = result - result2;
			break;
		}
		case 2:
		{
			DateTime t = DateTime.Parse(((ListViewItem)x).SubItems[ColumnToSort].Text);
			DateTime t2 = DateTime.Parse(((ListViewItem)y).SubItems[ColumnToSort].Text);
			num = DateTime.Compare(t, t2);
			break;
		}
		case 7:
			num = ((Convert.ToInt64(((ListViewItem)x).SubItems[ColumnToSort].Tag) > Convert.ToInt64(((ListViewItem)y).SubItems[ColumnToSort].Tag)) ? 1 : ((Convert.ToInt64(((ListViewItem)x).SubItems[ColumnToSort].Tag) < Convert.ToInt64(((ListViewItem)y).SubItems[ColumnToSort].Tag)) ? (-1) : 0));
			break;
		default:
		{
			ListViewItem listViewItem = (ListViewItem)x;
			ListViewItem listViewItem2 = (ListViewItem)y;
			num = ObjectCompare.Compare(listViewItem.SubItems[ColumnToSort].Text, listViewItem2.SubItems[ColumnToSort].Text);
			break;
		}
		}
		if (OrderOfSort == SortOrder.Ascending)
		{
			return num;
		}
		if (OrderOfSort == SortOrder.Descending)
		{
			return -num;
		}
		return 0;
	}
}
