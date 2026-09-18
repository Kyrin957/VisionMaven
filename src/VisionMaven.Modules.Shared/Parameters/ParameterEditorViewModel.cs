using System.Collections.ObjectModel;
using Prism.Mvvm;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Modules.Shared.Parameters;

/// <summary>单个参数字段的编辑视图模型：按 Schema 决定控件类型与校验。</summary>
public sealed class ParameterFieldViewModel : BindableBase
{
    private readonly ParameterElement _element;
    private string _value;

    public ParameterFieldViewModel(ParameterElement element, string? value)
    {
        _element = element;
        _value = value ?? element.DefaultText ?? string.Empty;

        if (element is EnumParameter enumeration)
        {
            foreach (var option in enumeration.Options)
            {
                SelectorItems.Add(option);
            }
        }
    }

    public string Key => _element.Key;

    public string DisplayName => _element.DisplayName;

    public ParameterValueKind Kind => _element.Kind;

    public string? Group => _element.Group;

    public string? Unit => _element.Unit;

    public bool IsRequired => _element.Required;

    public IReadOnlyList<string> Options => _element is EnumParameter enumeration
        ? enumeration.Options
        : Array.Empty<string>();

    /// <summary>下拉候选集合：枚举取值 + 引用类参数的候选对象。</summary>
    public ObservableCollection<string> SelectorItems { get; } = new();

    public bool UseCheckBox => Kind == ParameterValueKind.Boolean;

    public bool UseComboBox => !UseCheckBox && SelectorItems.Count > 0;

    public bool UseFilePicker => Kind == ParameterValueKind.File;

    public bool UseFolderPicker => Kind == ParameterValueKind.Directory;

    public bool UseTextBox => !UseCheckBox && !UseComboBox && !UseFilePicker && !UseFolderPicker;

    public double Minimum => _element switch
    {
        IntegerParameter integer => integer.Min,
        NumberParameter number => number.Min,
        _ => 0d
    };

    public double Maximum => _element switch
    {
        IntegerParameter integer => integer.Max,
        NumberParameter number => number.Max,
        _ => 0d
    };

    /// <summary>可选值的下拉列表（设备 / 模型 / 流程等的候选集合）。</summary>
    public ObservableCollection<string> Candidates { get; } = new();

    public string Filter => _element is FileParameter file ? file.Filter : "所有文件|*.*";

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                Validate();
            }
        }
    }

    public string? Error { get; private set; }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>按 Schema 校验并裁剪，返回是否合法。</summary>
    public bool Validate()
    {
        Error = null;

        if (IsRequired && string.IsNullOrWhiteSpace(Value))
        {
            Error = $"{DisplayName} 不能为空";
        }
        else if (!string.IsNullOrWhiteSpace(Value))
        {
            if (!ParameterReader.TryCoerce(_element, Value, out var coerced, out var error))
            {
                Error = error;
                ReplaceValue(coerced);
            }
            else if (!string.Equals(coerced, Value, StringComparison.Ordinal))
            {
                ReplaceValue(coerced);
            }
        }

        RaisePropertyChanged(nameof(HasError));
        return !HasError;
    }

    private void ReplaceValue(string value)
    {
        _value = value;
        RaisePropertyChanged(nameof(Value));
    }

    /// <summary>取最终写入配置的值。</summary>
    public string ResolvedValue => _element switch
    {
        IntegerParameter integer => ParameterReader.GetLong(new Dictionary<string, string> { [Key] = Value }, Key, integer.DefaultValue)
            .ToString(System.Globalization.CultureInfo.InvariantCulture),
        NumberParameter number => ParameterReader.GetDouble(new Dictionary<string, string> { [Key] = Value }, Key, number.DefaultValue)
            .ToString(System.Globalization.CultureInfo.InvariantCulture),
        BooleanParameter boolean => ParameterReader.GetBool(new Dictionary<string, string> { [Key] = Value }, Key, boolean.DefaultValue)
            .ToString(),
        _ => Value
    };

    /// <summary>应用外部值（如工程配置回填）。</summary>
    public void SetValue(string? value)
    {
        Value = value ?? _element.DefaultText ?? string.Empty;
    }
}

/// <summary>
/// 参数表单视图模型：由 <see cref="ParameterSchema"/> 驱动自动生成，
/// 新增算子 / 节点 / 驱动无需手写界面。
/// </summary>
public sealed class ParameterEditorViewModel : BindableBase
{
    private ParameterSchema _schema = new();

    public ObservableCollection<ParameterFieldViewModel> Fields { get; } = new();

    public bool HasFields => Fields.Count > 0;

    public ParameterFieldViewModel? Find(string key)
        => Fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 Schema 重建表单，并回填现有取值。</summary>
    public void Load(ParameterSchema schema, IReadOnlyDictionary<string, string>? values)
    {
        _schema = schema ?? new ParameterSchema();
        Fields.Clear();

        foreach (var element in _schema.OrderBy(element => element.Order))
        {
            var field = new ParameterFieldViewModel(
                element,
                values is not null && values.TryGetValue(element.Key, out var value) ? value : null);
            field.Validate();
            Fields.Add(field);
        }

        foreach (var field in Fields)
        {
            field.PropertyChanged += (_, _) => RaisePropertyChanged(nameof(HasErrors));
        }

        RaisePropertyChanged(nameof(HasFields));
        RaisePropertyChanged(nameof(HasErrors));
    }

    /// <summary>为引用类参数提供候选项。</summary>
    public void SetCandidates(string key, IEnumerable<string> candidates)
    {
        var field = Find(key);
        if (field is null)
        {
            return;
        }

        field.Candidates.Clear();
        foreach (var candidate in candidates)
        {
            field.Candidates.Add(candidate);
        }
    }

    public bool HasErrors => Fields.Any(field => field.HasError);

    /// <summary>按 Schema 生成默认参数。</summary>
    public Dictionary<string, string> CreateDefaults() => _schema.CreateDefaults();

    /// <summary>校验全部字段，返回错误信息；全部通过返回 null。</summary>
    public string? ValidateAll()
    {
        foreach (var field in Fields)
        {
            if (!field.Validate())
            {
                return field.Error;
            }
        }

        return null;
    }

    /// <summary>导出为参数字典。</summary>
    public Dictionary<string, string> Collect()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            result[field.Key] = field.ResolvedValue;
        }

        return result;
    }

    /// <summary>合并到目标字典（保留目标中表单未覆盖的键）。</summary>
    public void ApplyTo(IDictionary<string, string> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var pair in Collect())
        {
            target[pair.Key] = pair.Value;
        }
    }
}
