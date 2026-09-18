using System.Windows;
using VisionMaven.App.Services;

namespace VisionMaven.App.Views;

/// <summary>通用文本输入窗口。</summary>
public partial class TextInputWindow : Window
{
    public TextInputWindow(TextInputDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
