namespace GameWarden.Core.Metrics;

/// <summary>A game's process tree (docs/11 §13: CPU and RAM per game process tree), from the machine's process list.</summary>
internal static class ProcessTree
{
    /// <summary><paramref name="root"/> and every process it started, directly or through others, given each process's parent.</summary>
    public static IReadOnlyList<int> WithDescendants(int root, IEnumerable<(int Pid, int Parent)> processes)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in processes)
        {
            if (pid != parent)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(pid);
            }
        }

        var tree = new List<int> { root };
        for (var i = 0; i < tree.Count; i++)
        {
            foreach (var child in children.GetValueOrDefault(tree[i]) ?? [])
            {
                if (!tree.Contains(child))
                {
                    tree.Add(child);
                }
            }
        }

        return tree;
    }
}
