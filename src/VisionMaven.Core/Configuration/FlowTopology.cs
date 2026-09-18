using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Configuration;

/// <summary>流程拓扑校验：唯一性、next 引用、无环、单一起点。</summary>
public static class FlowTopology
{
    public static IReadOnlyList<string> Validate(FlowDefinitionConfig flow)
    {
        var errors = new List<string>();
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeMap = new Dictionary<string, FlowNodeConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in flow.Nodes)
        {
            if (!nodeIds.Add(node.NodeId))
            {
                errors.Add($"流程 {flow.FlowId} 存在重复 nodeId：{node.NodeId}");
                continue;
            }

            nodeMap[node.NodeId] = node;
        }

        foreach (var node in flow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.TypeKey))
            {
                errors.Add($"流程 {flow.FlowId} 节点 {node.NodeId} 缺少 typeKey");
            }

            foreach (var next in node.Next.Where(next => !nodeIds.Contains(next)))
            {
                errors.Add($"流程 {flow.FlowId} 节点 {node.NodeId} 的 next 引用了不存在的节点 {next}");
            }

            foreach (var next in node.Next.Where(next => string.Equals(next, node.NodeId, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"流程 {flow.FlowId} 节点 {node.NodeId} 的 next 指向自身");
            }
        }

        if (errors.Count > 0 || nodeIds.Count == 0)
        {
            return errors;
        }

        var indegree = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodeMap.Values)
        {
            foreach (var next in node.Next.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                indegree[next]++;
            }
        }

        var entryNodes = indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToArray();
        if (entryNodes.Length == 0)
        {
            errors.Add($"流程 {flow.FlowId} 无入度为 0 的起点节点");
        }
        else if (entryNodes.Length > 1)
        {
            errors.Add($"流程 {flow.FlowId} 存在多个起点节点：{string.Join(", ", entryNodes)}");
        }

        // Kahn 拓扑排序检测环路
        var remaining = new Dictionary<string, int>(indegree, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(entryNodes);
        var visited = 0;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var next in nodeMap[current].Next.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (--remaining[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (visited != nodeIds.Count)
        {
            errors.Add($"流程 {flow.FlowId} 存在环路");
        }

        return errors;
    }

    /// <summary>计算每个节点的前驱集合，供执行器判断汇合点。</summary>
    public static IReadOnlyDictionary<string, HashSet<string>> BuildPredecessors(FlowDefinitionConfig flow)
    {
        var map = flow.Nodes.ToDictionary(
            node => node.NodeId,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in flow.Nodes)
        {
            foreach (var next in node.Next.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (map.TryGetValue(next, out var set))
                {
                    set.Add(node.NodeId);
                }
            }
        }

        return map;
    }
}
