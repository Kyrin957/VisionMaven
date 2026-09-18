using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Vision.Operators;

/// <summary>算子注册表：算法页与流程页据此列出可用算子并自动生成参数表单。</summary>
public sealed class OperatorRegistry : IOperatorRegistry
{
    private readonly Dictionary<string, OperatorBase> _map;
    private readonly List<OperatorDescriptor> _descriptors;

    public OperatorRegistry(IEnumerable<IImageOperator> operators)
    {
        _map = new Dictionary<string, OperatorBase>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in operators.OfType<OperatorBase>())
        {
            _map[instance.TypeKey] = instance;
        }

        _descriptors = _map.Values
            .OrderBy(instance => instance.Category)
            .ThenBy(instance => instance.Order)
            .Select(instance => new OperatorDescriptor(
                instance.TypeKey,
                instance.DisplayName,
                instance.Category,
                instance.Order,
                instance.Schema))
            .ToList();
    }

    public IReadOnlyList<OperatorDescriptor> Operators => _descriptors;

    public OperatorDescriptor? Find(string typeKey)
        => _descriptors.FirstOrDefault(
            descriptor => string.Equals(descriptor.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));

    public IImageOperator Create(string typeKey)
        => _map.TryGetValue(typeKey, out var instance)
            ? instance
            : throw new ConfigurationException(ErrorCodes.FlowTypeKeyUnknown, $"未注册的算子：{typeKey}");
}
