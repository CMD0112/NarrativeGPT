using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ChatGPTWrapper.Views;

internal static class SourceSyncGridHelper
{
    public static void AddFileColumn(DataGrid grid) =>
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "File",
            Binding = new Binding(nameof(SourceSyncRowViewModel.FileName)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
        });

    public static void AddStateColumn(DataGrid grid) =>
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "State",
            Binding = new Binding(nameof(SourceSyncRowViewModel.StateLabel)),
            Width = 90,
            IsReadOnly = true,
        });

    public static void AddLocalHashColumn(DataGrid grid) =>
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Local",
            Binding = new Binding(nameof(SourceSyncRowViewModel.LocalHashShort)),
            Width = 70,
            IsReadOnly = true,
        });

    public static void AddRemoteHashColumn(DataGrid grid) =>
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Remote",
            Binding = new Binding(nameof(SourceSyncRowViewModel.RemoteHashShort)),
            Width = 70,
            IsReadOnly = true,
        });

    public static void AddActionColumn(DataGrid grid)
    {
        var template = Application.Current.FindResource("SourceSyncActionComboCellTemplate") as DataTemplate;
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Action",
            Width = new DataGridLength(132),
            CellTemplate = template,
        });
    }
}
