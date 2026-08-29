using System.Collections.Generic;
using Godot;

namespace QuestWorld.Interaction.Runtime.Interactive;

/// <summary>Resolves composition candidates without crossing a node's direct-child boundary.</summary>
internal static class InteractionComposition
{
    public static List<T> FindDirectChildren<T>(Node owner)
        where T : Node
    {
        List<T> candidates = new();
        foreach (Node child in owner.GetChildren())
        {
            if (child is T candidate)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    public static T? FindUniqueDirectChild<T>(Node owner)
        where T : Node
    {
        List<T> candidates = FindDirectChildren<T>(owner);
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
