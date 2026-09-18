using System.IO;
using System.Windows;
using Microsoft.Win32;
using VisionMaven.Core.Shell;
using IDialogService = VisionMaven.Core.Shell.IDialogService;

namespace VisionMaven.App.Services;

/// <summary>对话框服务实现，统一在 UI 线程呈现。</summary>
public sealed class DialogService : IDialogService
{
    public void ShowInfo(string message, string title = "提示")
        => Show(message, title, MessageBoxImage.Information);

    public void ShowWarning(string message, string title = "警告")
        => Show(message, title, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "错误")
        => Show(message, title, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "确认")
    {
        var application = Application.Current;
        if (application is null)
        {
            return false;
        }

        return application.Dispatcher.Invoke(() =>
            MessageBox.Show(
                application.MainWindow,
                message,
                title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) == MessageBoxResult.OK);
    }

    public string? OpenFile(string filter, string? title = null, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title ?? "选择文件",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string filter, string? defaultFileName = null, string? title = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title ?? "保存文件",
            FileName = defaultFileName ?? string.Empty,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectFolder(string? title = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title ?? "选择目录",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static void Show(string message, string title, MessageBoxImage icon)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.Dispatcher.Invoke(() => MessageBox.Show(
            application.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            icon));
    }
}
