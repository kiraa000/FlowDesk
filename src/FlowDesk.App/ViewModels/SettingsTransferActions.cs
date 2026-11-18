namespace FlowDesk.App.ViewModels;

public partial class SettingsViewModel
{
    /// <summary>Set by the transfer service. Opens the export flow; the page and palette both invoke it.</summary>
    internal Func<Task>? ExportTransfer { get; set; }
    /// <summary>Set by the transfer service. Opens the import flow; the page and palette both invoke it.</summary>
    internal Func<Task>? ImportTransfer { get; set; }
}
