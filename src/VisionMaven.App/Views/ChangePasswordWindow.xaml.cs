using System.Windows;
using System.Windows.Controls;
using VisionMaven.App.ViewModels;

namespace VisionMaven.App.Views;

/// <summary>修改密码窗口。</summary>
public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _viewModel;

    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => CurrentPasswordInput.Focus();
    }

    private void OnCurrentPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.CurrentPassword = ((PasswordBox)sender).Password;

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.NewPassword = ((PasswordBox)sender).Password;

    private void OnConfirmPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.ConfirmPassword = ((PasswordBox)sender).Password;

    private void OnCloseRequested(object? sender, bool success)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        DialogResult = success;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
