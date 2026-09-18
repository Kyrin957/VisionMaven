using System.Globalization;
using System.Reflection;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Flow.Nodes;

/// <summary>
/// 流程节点工厂：扫描当前程序集中标注 <see cref="FlowNodeAttribute"/> 的实现，
/// 新增节点只需新增一个类并标注特性。
/// </summary>
public sealed class FlowNodeFactory : IFlowNodeFactory
{
    private readonly Func<Type, object> _resolver;
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FlowNodeDescriptor> _descriptors = new();

    public FlowNodeFactory(Func<Type, object> resolver)
    {
        _resolver = resolver;
        Discover();
    }

    public IReadOnlyList<FlowNodeDescriptor> Descriptors => _descriptors;

    public FlowNodeDescriptor? Find(string typeKey)
        => _descriptors.FirstOrDefault(
            descriptor => string.Equals(descriptor.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));

    public IFlowNode Create(string typeKey)
    {
        if (!_types.TryGetValue(typeKey, out var type))
        {
            throw new ConfigurationException(ErrorCodes.FlowTypeKeyUnknown, $"未注册的流程节点类型：{typeKey}");
        }

        return (IFlowNode)_resolver(type);
    }

    private void Discover()
    {
        var assembly = typeof(FlowNodeFactory).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(IFlowNode).IsAssignableFrom(type))
            {
                continue;
            }

            var attribute = type.GetCustomAttribute<FlowNodeAttribute>();
            if (attribute is null)
            {
                continue;
            }

            if (_types.ContainsKey(attribute.TypeKey))
            {
                continue;
            }

            IFlowNode instance;
            try
            {
                instance = (IFlowNode)_resolver(type);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue;
            }

            _types[attribute.TypeKey] = type;
            _descriptors.Add(new FlowNodeDescriptor(
                attribute.TypeKey,
                attribute.DisplayName,
                attribute.Group,
                attribute.Order,
                instance.InputPorts,
                instance.OutputPorts,
                instance is FlowNodeBase basis ? basis.Schema : new ParameterSchema()));
        }

        _descriptors.Sort((left, right) =>
        {
            var group = string.Compare(left.Group, right.Group, StringComparison.CurrentCulture);
            return group != 0 ? group : left.Order.CompareTo(right.Order);
        });
    }

    /// <summary>供界面显示的分组顺序。</summary>
    public static IReadOnlyList<string> GroupOrder { get; } = new[]
    {
        "输入", "预处理", "分割", "特征", "匹配", "定位", "测量", "标定", "推理", "判定", "输出", "脚本", "通用"
    };

    /// <summary>按分组顺序排序的分组名。</summary>
    public static IEnumerable<string> OrderGroups(IEnumerable<string> groups)
        => groups
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(group =>
            {
                var index = GroupOrder.ToList().FindIndex(item =>
                    string.Equals(item, group, StringComparison.CurrentCulture));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(group => group, StringComparer.CurrentCulture);
}
