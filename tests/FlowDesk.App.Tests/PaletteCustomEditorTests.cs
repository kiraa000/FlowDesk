using System.Xml.Linq;

namespace FlowDesk.App.Tests;

public sealed class PaletteCustomEditorTests
{
    private static string Source(string folder, string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", folder, file));
    [Fact] public void EditorInputsHaveNarratorNamesAndValidationIsLive()
    {
        var doc = XDocument.Parse(Source("Views", "CustomCommandEditor.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        foreach (var control in doc.Descendants().Where(e => e.Name == x + "TextBox" || e.Name == x + "ComboBox" || e.Name == x + "CheckBox"))
            Assert.False(string.IsNullOrWhiteSpace((string?)control.Attribute("AutomationProperties.Name")));
        Assert.Contains(doc.Descendants(), e => (string?)e.Attribute("AutomationProperties.LiveSetting") == "Polite");
        string source = Source("Views", "CustomCommandEditor.xaml.cs");
        Assert.Contains("CustomCommandRules.Validate(Value, others)", source);
        Assert.Contains("TextChanged += (_, _) => Validate()", source);
    }
    [Fact] public void SaveIsValidatedDeleteIsConfirmedAndScriptLaunchDoesNotOverridePolicyOrHideWindows()
    {
        string source = Source("Views", "SettingsPage.xaml.cs");
        Assert.Contains("if (!editor.Validate()) { args.Cancel = true; return; }", source);
        Assert.Contains("editor.ShowError(vm.CustomCommandsError); args.Cancel = true", source);
        Assert.Contains("Title = \"Delete custom command?\"", source);
        Assert.Contains("if (await dialog.ShowAsync() == ContentDialogResult.Primary) App.ViewModels.Settings.DeleteCustomCommand", source);
        string executor = Source("Services", "CustomCommandExecutor.cs");
        Assert.Contains("WindowStyle = ProcessWindowStyle.Normal", executor);
        Assert.Contains("command.RunAsAdmin ? \"runas\" : \"\"", executor);
        Assert.DoesNotContain("ExecutionPolicy", executor); Assert.DoesNotContain("Hidden", executor);
    }
}
