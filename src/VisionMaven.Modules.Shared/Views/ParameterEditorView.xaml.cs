using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VisionMaven.Modules.Shared.Parameters;

namespace VisionMaven.Modules.Shared.Views;

/// <summary>Schema 驱动的参数表单：按参数字段类型自动切换控件。</summary>
public partial class ParameterEditorView : UserControl
{
    public ParameterEditorView()
    {
        InitializeComponent();
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        if (ResolveField(sender) is not { } field)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = field.Filter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            field.Value = dialog.FileName;
        }
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (ResolveField(sender) is not { } field)
        {
            return;
        }

        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            field.Value = dialog.FolderName;
        }
    }

    private static ParameterFieldViewModel? ResolveField(object sender)
        => (sender as FrameworkElement)?.DataContext as ParameterFieldViewModel;
}
