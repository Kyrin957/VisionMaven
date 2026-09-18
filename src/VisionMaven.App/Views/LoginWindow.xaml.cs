using System.Windows;
using VisionMaven.App.ViewModels;

namespace VisionMaven.App.Views;

/// <summary>登录窗口。</summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox box)
        {
            _viewModel.Password = box.Password;
        }
    }

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
