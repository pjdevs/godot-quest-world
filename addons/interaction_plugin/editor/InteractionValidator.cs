#if TOOLS

using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace InteractionPlugin.Editor;

public static class InteractionValidator
{
    private enum InspectableType
    {
        None,
        InteractiveComponent,
        InteractionInteractor,
        InteractionDetector,
        AreaInteractionDetector,
        InteractionPresenter,
        InteractionAction,
        InteractionActionDefinition,
        StatefulStateInteractionRule,
    }

    public static bool CanHandle(GodotObject obj) => ResolveType(obj) != InspectableType.None;

    public static IEnumerable<string> Validate(GodotObject obj)
    {
        switch (ResolveType(obj))
        {
            case InspectableType.InteractiveComponent:
                return ValidateInteractive(obj);
            case InspectableType.InteractionInteractor:
                return ValidateInteractor(obj);
            case InspectableType.InteractionDetector:
                return ValidateDetector(obj);
            case InspectableType.AreaInteractionDetector:
                return ValidateAreaDetector(obj);
            case InspectableType.InteractionPresenter:
                return ValidatePresenter(obj);
            case InspectableType.InteractionAction:
                return ValidateAction(obj);
            case InspectableType.InteractionActionDefinition:
                return ValidateActionDefinition(obj);
            case InspectableType.StatefulStateInteractionRule:
                return ValidateStatefulRule(obj);
            default:
                return System.Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ValidateInteractive(GodotObject obj)
    {
        if (GetObject(obj, "InteractionArea") is null)
            yield return "InteractionArea must be assigned.";

        if (GetObject(obj, "InteractionAnchor") is null)
            yield return "InteractionAnchor must be assigned.";

        Godot.Collections.Array actions = GetArray(obj, "Actions");
        GodotObject? actionComponent = GetObject(obj, "ActionComponent");
        if (actionComponent is null)
            yield return "ActionComponent must be assigned.";

        if (actions.Count == 0)
            yield return "Actions must declare at least one action.";

        HashSet<string> ids = new();
        Dictionary<string, string> inputs = new();
        bool hasReplicatedAction = false;

        for (int index = 0; index < actions.Count; index++)
        {
            GodotObject? action = actions[index].AsGodotObject();
            if (action is null)
            {
                yield return $"Actions[{index}] must not be null.";
                continue;
            }

            GodotObject? definition = GetObject(action, "Definition");
            if (definition is null)
            {
                yield return $"Actions[{index}] has no Definition.";
                continue;
            }

            if (GetObject(action, "Executor") is null)
                yield return $"Actions[{index}] has no Executor.";

            if (
                actionComponent is GameplayActionComponent component
                && action is InteractionAction interactionAction
                && interactionAction.Component != component
            )
                yield return $"Actions[{index}] must be hosted by the assigned ActionComponent.";

            hasReplicatedAction |=
                GetInt(action, "ExecutionVisibility")
                == (int)GameplayActionExecutionVisibility.Replicated;

            string id = GetName(definition, "Id");
            if (id.Length == 0)
                yield return $"Actions[{index}] uses a Definition with an empty Id.";
            else if (!ids.Add(id))
                yield return $"Actions declare the action id '{id}' more than once.";

            if (GetBool(action, "Automatic"))
                continue;

            string input = GetName(definition, "InputActionName");
            if (input.Length == 0)
            {
                yield return $"Actions[{index}] ('{id}') is not automatic but declares no input.";
                continue;
            }

            // Sharing an input and a threshold is legitimate: the resolver separates such actions by
            // availability first, and a rule is what makes "open" and "unlock" alternate on one key.
            // Priority is the last discriminator an author can express, so it belongs to the key —
            // below it the resolver falls back on identifier order, which nobody authored on purpose.
            string trigger =
                $"{input}|{GetFloat(definition, "HoldThreshold")}|{GetInt(action, "Priority")}";
            if (inputs.TryGetValue(trigger, out string? other))
            {
                yield return $"Actions '{other}' and '{id}' share the input '{input}', the same hold "
                    + "threshold and the same priority: whenever both are available, the identifier "
                    + "order decides. Give one a higher Priority, or a rule that hides it.";
            }
            else
            {
                inputs[trigger] = id;
            }
        }

        if (hasReplicatedAction && !HasMatchingExecutionSynchronizer(obj))
        {
            yield return "Replicated actions require a GameplayActionExecutionSynchronizer targeting the assigned ActionComponent.";
        }

        foreach (string warning in ValidateRules(obj, obj, "TargetRules"))
        {
            yield return warning;
        }

        for (int index = 0; index < actions.Count; index++)
        {
            if (actions[index].AsGodotObject() is not GodotObject action)
                continue;

            foreach (string warning in ValidateRules(action, action, "Rules"))
            {
                yield return $"Actions[{index}]: {warning}";
            }
        }
    }

    private static IEnumerable<string> ValidateInteractor(GodotObject obj)
    {
        // Guessing a detection model is exactly what the replaceable layer refuses to do, so a
        // missing detector is a configuration error rather than a silent fallback.
        if (GetObject(obj, "Detector") is null)
            yield return "Detector must be assigned.";

        if (GetObject(obj, "Runner") is null)
            yield return "Runner must be assigned.";
    }

    private static IEnumerable<string> ValidateDetector(GodotObject obj)
    {
        if (GetObject(obj, "ViewOrigin") is null)
            yield return "ViewOrigin must be assigned.";

        if (GetFloat(obj, "DistanceScoreCoefficient") < 0.0f)
            yield return "DistanceScoreCoefficient must not be negative.";

        if (GetFloat(obj, "LineOfSightLossGrace") < 0.0f)
            yield return "LineOfSightLossGrace must not be negative.";
    }

    private static IEnumerable<string> ValidateAreaDetector(GodotObject obj)
    {
        foreach (string warning in ValidateDetector(obj))
        {
            yield return warning;
        }

        if (GetFloat(obj, "MaxDistance") < 0.0f)
            yield return "MaxDistance must not be negative.";
    }

    private static IEnumerable<string> ValidatePresenter(GodotObject obj)
    {
        if (GetObject(obj, "Interactor") is null)
            yield return "Interactor must be assigned.";

        if (GetObject(obj, "Camera") is null)
            yield return "Camera must be assigned.";
    }

    private static IEnumerable<string> ValidateAction(GodotObject obj)
    {
        if (GetObject(obj, "Definition") is null)
            yield return "Definition must be assigned.";

        if (GetObject(obj, "Executor") is null)
            yield return "Executor must be assigned.";

        if (GetName(obj, "HostConcurrencyGroup").Length == 0)
            yield return "HostConcurrencyGroup must not be empty.";

        if (obj is InteractionAction action && action.Interactive is null)
            yield return "InteractionAction must be hosted by a matching InteractiveComponent.";

        // The rule paths are relative to the owning action, which only that one can resolve.
        foreach (string warning in ValidateRules(obj, null, "Rules"))
        {
            yield return warning;
        }
    }

    private static bool HasMatchingExecutionSynchronizer(GodotObject interactive)
    {
        if (
            interactive is not Node node
            || GetObject(interactive, "ActionComponent") is not GameplayActionComponent component
            || node.GetParent() is not Node parent
        )
        {
            return false;
        }

        foreach (Node child in parent.GetChildren())
        {
            if (
                child is GameplayActionExecutionSynchronizer synchronizer
                && synchronizer.Component == component
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ValidateActionDefinition(GodotObject obj)
    {
        if (GetName(obj, "Id").Length == 0)
            yield return "Id must be assigned.";

        string input = GetName(obj, "InputActionName");
        if (input.Length > 0 && !InputMap.HasAction(input))
            yield return $"InputActionName '{input}' is not declared in the project input map.";

        if (GetFloat(obj, "HoldThreshold") < 0.0f)
            yield return "HoldThreshold must not be negative.";
    }

    private static IEnumerable<string> ValidateStatefulRule(GodotObject obj)
    {
        if (obj.Get("StatefulPath").AsNodePath().IsEmpty)
            yield return "StatefulPath must be assigned.";

        if (GetArray(obj, "ExpectedStates").Count == 0)
            yield return "ExpectedStates must declare at least one state.";
    }

    /// <summary>Validates a rule array of <paramref name="owner"/> against its resolution root.</summary>
    private static IEnumerable<string> ValidateRules(
        GodotObject owner,
        GodotObject? resolutionRoot,
        StringName propertyName
    )
    {
        Godot.Collections.Array rules = GetArray(owner, propertyName);

        for (int index = 0; index < rules.Count; index++)
        {
            GodotObject? rule = rules[index].AsGodotObject();
            if (rule is null)
            {
                yield return $"{propertyName}[{index}] must not be null.";
                continue;
            }

            if (ResolveType(rule) != InspectableType.StatefulStateInteractionRule)
                continue;

            string prefix = $"{propertyName}[{index}]";

            NodePath path = rule.Get("StatefulPath").AsNodePath();
            if (path.IsEmpty)
            {
                yield return $"{prefix}: StatefulPath must be assigned.";
                continue;
            }

            Godot.Collections.Array expected = GetArray(rule, "ExpectedStates");
            if (expected.Count == 0)
                yield return $"{prefix}: ExpectedStates must declare at least one state.";

            // Only the owning node resolves the path; a rule inspected alone cannot.
            if (resolutionRoot is not Node node || !node.IsInsideTree())
                continue;

            Node? target = node.GetNodeOrNull(path);
            if (target is null)
            {
                yield return $"{prefix}: StatefulPath '{path}' does not resolve to a node.";
                continue;
            }

            List<string>? states = GetSchemaStates(target);
            if (states is null)
                continue;

            foreach (Variant state in expected)
            {
                string name = state.AsStringName().ToString();
                if (!states.Contains(name))
                    yield return $"{prefix}: state '{name}' is absent from the StateSchema of "
                        + $"'{path}'.";
            }
        }
    }

    private static List<string>? GetSchemaStates(GodotObject stateful)
    {
        if (GetObject(stateful, "Schema") is not GodotObject schema)
        {
            return null;
        }

        List<string> states = new();

        foreach (Variant state in schema.Get("States").AsGodotArray())
        {
            states.Add(state.AsStringName().ToString());
        }

        return states;
    }

    private static InspectableType ResolveType(GodotObject obj)
    {
        InspectableType managedType = obj switch
        {
            InteractiveComponent => InspectableType.InteractiveComponent,
            InteractionInteractor => InspectableType.InteractionInteractor,
            AreaInteractionDetector => InspectableType.AreaInteractionDetector,
            ProximityInteractionDetector => InspectableType.InteractionDetector,
            AimInteractionDetector => InspectableType.InteractionDetector,
            InteractionDetector => InspectableType.InteractionDetector,
            InteractionPresenter => InspectableType.InteractionPresenter,
            InteractionAction => InspectableType.InteractionAction,
            InteractionActionDefinition => InspectableType.InteractionActionDefinition,
            StatefulStateInteractionRule => InspectableType.StatefulStateInteractionRule,
            _ => InspectableType.None,
        };
        if (managedType != InspectableType.None)
        {
            return managedType;
        }

        Script? script = GetAttachedScript(obj);
        string globalName = script?.GetGlobalName().ToString() ?? string.Empty;
        return globalName switch
        {
            nameof(InteractiveComponent) => InspectableType.InteractiveComponent,
            nameof(InteractionInteractor) => InspectableType.InteractionInteractor,
            nameof(AreaInteractionDetector) => InspectableType.AreaInteractionDetector,
            nameof(ProximityInteractionDetector) => InspectableType.InteractionDetector,
            nameof(AimInteractionDetector) => InspectableType.InteractionDetector,
            nameof(InteractionDetector) => InspectableType.InteractionDetector,
            nameof(InteractionPresenter) => InspectableType.InteractionPresenter,
            nameof(InteractionAction) => InspectableType.InteractionAction,
            nameof(InteractionActionDefinition) => InspectableType.InteractionActionDefinition,
            nameof(StatefulStateInteractionRule) => InspectableType.StatefulStateInteractionRule,
            _ => ResolveTypeFromPath(script?.ResourcePath),
        };
    }

    private static InspectableType ResolveTypeFromPath(string? path)
    {
        return path switch
        {
            "res://addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs" =>
                InspectableType.InteractiveComponent,
            "res://addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs" =>
                InspectableType.InteractionInteractor,
            "res://addons/interaction_plugin/runtime/detection/AreaInteractionDetector.cs" =>
                InspectableType.AreaInteractionDetector,
            "res://addons/interaction_plugin/runtime/detection/InteractionDetector.cs" =>
                InspectableType.InteractionDetector,
            "res://addons/interaction_plugin/runtime/detection/ProximityInteractionDetector.cs" =>
                InspectableType.InteractionDetector,
            "res://addons/interaction_plugin/runtime/detection/AimInteractionDetector.cs" =>
                InspectableType.InteractionDetector,
            "res://addons/interaction_plugin/presentation/ui/InteractionPresenter.cs" =>
                InspectableType.InteractionPresenter,
            "res://addons/interaction_plugin/runtime/actions/InteractionAction.cs" =>
                InspectableType.InteractionAction,
            "res://addons/interaction_plugin/runtime/actions/InteractionActionDefinition.cs" =>
                InspectableType.InteractionActionDefinition,
            "res://addons/interaction_plugin/integration/stateful/StatefulStateInteractionRule.cs" =>
                InspectableType.StatefulStateInteractionRule,
            _ => InspectableType.None,
        };
    }

    private static Godot.Collections.Array GetArray(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsGodotArray();

    private static bool GetBool(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsBool();

    private static int GetInt(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsInt32();

    private static float GetFloat(GodotObject obj, StringName propertyName) =>
        (float)obj.Get(propertyName).AsDouble();

    private static string GetName(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsStringName().ToString();

    private static GodotObject? GetObject(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsGodotObject();

    private static Script? GetAttachedScript(GodotObject obj) =>
        obj.GetScript().AsGodotObject() as Script;
}

#endif
