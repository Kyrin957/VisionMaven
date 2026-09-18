using System.Collections.ObjectModel;
using System.Windows;
using Prism.Mvvm;
using VisionMaven.Core.Shell;

namespace VisionMaven.App.Services;

/// <summary>输入字段视图模型。</summary>
public sealed class TextInputFieldViewModel : BindableBase
{
    private string _value;

    public TextInputFieldViewModel(TextInputField field)
    {
        Field = field;
        _value = field.DefaultValue;
    }

    public TextInputField Field { get; }

    public string Label => Field.Label;

    public string Key => Field.Key;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

/// <summary>通用文本输入对话框视图模型。</summary>
public sealed class TextInputDialogViewModel : BindableBase
{
    public TextInputDialogViewModel(string title, IEnumerable<TextInputField> fields)
    {
        Title = title;
        foreach (var field in fields)
        {
            Fields.Add(new TextInputFieldViewModel(field));
        }
    }

    public string Title { get; }

    public ObservableCollection<TextInputFieldViewModel> Fields { get; } = new();

    public IReadOnlyDictionary<string, string> Collect()
        => Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.OrdinalIgnoreCase);
}

/// <summary>通用文本输入对话框服务实现。</summary>
public sealed class TextInputDialogService : ITextInputDialogService
{
    public IReadOnlyDictionary<string, string>? RequestInputs(string title, IReadOnlyList<TextInputField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Dispatcher.Invoke(() =>
        {
            var viewModel = new TextInputDialogViewModel(title, fields);
            var window = new Views.TextInputWindow(viewModel)
            {
                Owner = application.MainWindow
            };

            return window.ShowDialog() == true ? viewModel.Collect() : null;
        });
    }
}
