using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Resolves a StatefulComponent from the local scope of an Interactive target.</summary>
internal static class StatefulComposition
{
    public static List<StatefulComponent> FindLocalCandidates(InteractiveComponent interactive)
    {
        List<StatefulComponent> candidates = new();
        Node? scope = interactive.GetParent();
        if (scope is null)
        {
            return candidates;
        }

        foreach (Node child in scope.GetChildren())
        {
            if (child is StatefulComponent stateful)
            {
                candidates.Add(stateful);
            }
        }

        return candidates;
    }

    public static StatefulComponent? ResolveLocal(InteractiveComponent interactive)
    {
        List<StatefulComponent> candidates = FindLocalCandidates(interactive);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public static StatefulComponent? ResolveLocalFrom(Node source)
    {
        for (Node? current = source; current is not null; current = current.GetParent())
        {
            if (current is InteractiveComponent interactive)
            {
                return ResolveLocal(interactive);
            }
        }

        return null;
    }
}
