#if TOOLS

using System.Collections.Generic;
using System.Linq;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.State;

namespace InteractionPlugin.Editor;

public static class InteractionValidator
{
    private enum InspectableType
    {
        None,
        InteractiveComponent,
        InteractionArea3D,
        IndicationArea3D,
        InteractionAnchor3D,
        InteractionInteractor,
        InteractionDetector,
        AreaInteractionDetector,
        InteractionPresenter,
        InteractionActionExecutor,
        InteractionAction,
        InteractionExecutionSynchronizer,
        InteractionActionDefinition,
        SetStateInteractionExecutor,
        TransitionStateInteractionExecutor,
        TimedTransitionStateInteractionExecutor,
        StatefulStateInteractionRule,
        StatefulAvailabilityInteractionRule,
        StatefulTransitionAction,
        StatefulRunningTransitionAction,
        StatefulTimedTransitionAction,
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
            case InspectableType.InteractionExecutionSynchronizer:
                return ValidateExecutionSynchronizer(obj);
            case InspectableType.InteractionActionDefinition:
                return ValidateActionDefinition(obj);
            case InspectableType.SetStateInteractionExecutor:
                return ValidateSetStateExecutor(obj);
            case InspectableType.TransitionStateInteractionExecutor:
                return ValidateTransitionStateExecutor(obj);
            case InspectableType.TimedTransitionStateInteractionExecutor:
                return ValidateTimedTransitionStateExecutor(obj);
            case InspectableType.StatefulStateInteractionRule:
                return ValidateStatefulRule(obj);
            case InspectableType.StatefulAvailabilityInteractionRule:
                return ValidateStatefulAvailabilityRule(obj);
            case InspectableType.StatefulTransitionAction:
                return ValidateStatefulTransitionAction(obj);
            case InspectableType.StatefulRunningTransitionAction:
                return ValidateStatefulRunningTransitionAction(obj);
            case InspectableType.StatefulTimedTransitionAction:
                return ValidateStatefulTimedTransitionAction(obj);
            default:
                return System.Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ValidateInteractive(GodotObject obj)
    {
        List<GodotObject?> composedAreas = GetDirectChildrenOfType(
            obj,
            InspectableType.InteractionArea3D
        );
        if (GetObject(obj, "InteractionArea") is null && composedAreas.Count == 0)
            yield return "InteractionArea must be assigned.";
        else if (GetObject(obj, "InteractionArea") is null && composedAreas.Count > 1)
            yield return "InteractionArea composition is ambiguous: expected exactly one direct InteractionArea3D child.";

        List<GodotObject?> composedIndicationAreas = GetDirectChildrenOfType(
            obj,
            InspectableType.IndicationArea3D
        );
        if (GetObject(obj, "IndicationArea") is null && composedIndicationAreas.Count > 1)
            yield return "IndicationArea composition is ambiguous: expected at most one direct IndicationArea3D child.";

        List<GodotObject?> composedAnchors = GetDirectChildrenOfType(
            obj,
            InspectableType.InteractionAnchor3D
        );
        if (GetObject(obj, "InteractionAnchor") is null && composedAnchors.Count == 0)
            yield return "InteractionAnchor must be assigned.";
        else if (GetObject(obj, "InteractionAnchor") is null && composedAnchors.Count > 1)
            yield return "InteractionAnchor composition is ambiguous: expected exactly one direct InteractionAnchor3D child.";

        Godot.Collections.Array actions = GetArray(obj, "Actions");
        List<GodotObject?> composedActions = GetDirectChildrenOfType(
            obj,
            InspectableType.InteractionAction
        );
        List<GodotObject?> actionObjects =
            actions.Count > 0 ? GetObjects(actions) : composedActions;
        if (actions.Count == 0)
        {
            if (composedActions.Count == 0)
                yield return "Actions must declare at least one action.";
        }

        HashSet<string> ids = new();
        Dictionary<string, string> inputs = new();
        bool hasReplicatedAction = false;

        for (int index = 0; index < actionObjects.Count; index++)
        {
            GodotObject? action = actionObjects[index];
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

            if (!HasResolvedExecutor(action))
                yield return $"Actions[{index}] has no Executor.";

            hasReplicatedAction |=
                GetInt(action, "ExecutionVisibility")
                == (int)InteractionExecutionVisibility.Replicated;

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
            yield return "Replicated actions require a child InteractionExecutionSynchronizer targeting this InteractiveComponent.";
        }

        foreach (string warning in ValidateRules(obj, obj, "TargetRules"))
        {
            yield return warning;
        }

        for (int index = 0; index < actionObjects.Count; index++)
        {
            if (actionObjects[index] is not GodotObject action)
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

        if (!HasResolvedExecutor(obj))
            yield return "Executor must be assigned.";

        if (GetName(obj, "ConcurrencyGroup").Length == 0)
            yield return "ConcurrencyGroup must not be empty.";

        // The rule paths are relative to the owning action, which only that one can resolve.
        foreach (string warning in ValidateRules(obj, null, "Rules"))
        {
            yield return warning;
        }
    }

    private static IEnumerable<string> ValidateExecutionSynchronizer(GodotObject obj)
    {
        InteractionExecutionSynchronizer? synchronizer = obj as InteractionExecutionSynchronizer;
        if (
            GetObject(obj, "Interactive") is null
            && (synchronizer is null || synchronizer.ResolveInteractive() is null)
        )
            yield return "Interactive must be assigned.";
    }

    private static bool HasMatchingExecutionSynchronizer(GodotObject interactive)
    {
        if (interactive is not Node node)
        {
            return false;
        }

        foreach (Node child in node.GetChildren())
        {
            if (
                ResolveType(child) == InspectableType.InteractionExecutionSynchronizer
                && (
                    GetObject(child, "Interactive") == interactive
                    || child.GetParent() == interactive
                )
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
        if (input.Length > 0 && !IsProjectInputActionDeclared(input))
            yield return $"InputActionName '{input}' is not declared in the project input map.";

        if (GetFloat(obj, "HoldThreshold") < 0.0f)
            yield return "HoldThreshold must not be negative.";
    }

    private static IEnumerable<string> ValidateSetStateExecutor(GodotObject obj)
    {
        GodotObject? stateful = GetObject(obj, "Stateful") ?? ResolveLocalStateful(obj);
        if (stateful is null)
        {
            yield return "Stateful must be assigned.";
            yield break;
        }

        string state = GetName(obj, "TargetState");
        if (state.Length == 0)
        {
            yield return "TargetState must be assigned.";
            yield break;
        }

        List<string>? states = GetSchemaStates(stateful);
        if (states is not null && !states.Contains(state))
            yield return $"TargetState '{state}' is absent from the assigned StateSchema.";
    }

    private static IEnumerable<string> ValidateTransitionStateExecutor(GodotObject obj)
    {
        GodotObject? stateful = GetObject(obj, "Stateful") ?? ResolveLocalStateful(obj);
        if (stateful is null)
        {
            yield return "Stateful must be assigned.";
            yield break;
        }

        List<string>? states = GetSchemaStates(stateful);

        foreach (string property in new[] { "RunningState", "CompletedState", "CancelledState" })
        {
            string state = GetName(obj, property);
            if (state.Length == 0)
            {
                yield return $"{property} must be assigned.";
                continue;
            }

            if (states is not null && !states.Contains(state))
                yield return $"{property} '{state}' is absent from the assigned StateSchema.";
        }
    }

    private static IEnumerable<string> ValidateTimedTransitionStateExecutor(GodotObject obj)
    {
        float duration = GetFloat(obj, "Duration");
        if (!float.IsFinite(duration) || duration <= 0.0f)
            yield return "Duration must be finite and greater than zero.";

        foreach (string warning in ValidateTransitionStateExecutor(obj))
        {
            yield return warning;
        }
    }

    private static IEnumerable<string> ValidateStatefulRule(GodotObject obj)
    {
        if (GetObject(obj, "Stateful") is null && obj.Get("StatefulPath").AsNodePath().IsEmpty)
            yield return "StatefulPath must be assigned.";

        if (GetArray(obj, "ExpectedStates").Count == 0)
            yield return "ExpectedStates must declare at least one state.";
    }

    private static IEnumerable<string> ValidateStatefulAvailabilityRule(GodotObject obj)
    {
        GodotObject? stateful = GetObject(obj, "Stateful") ?? ResolveLocalStateful(obj);
        if (stateful is null && obj.Get("StatefulPath").AsNodePath().IsEmpty)
            yield return "StatefulPath must be assigned.";

        Godot.Collections.Array available = GetArray(obj, "AvailableStates");
        Godot.Collections.Array blocked = GetArray(obj, "BlockedStates");
        if (available.Count == 0 && blocked.Count == 0)
            yield return "AvailableStates or BlockedStates must declare at least one state.";

        HashSet<string> availableNames = GetNames(available);
        foreach (string state in GetNames(blocked))
        {
            if (availableNames.Contains(state))
                yield return $"State '{state}' cannot be both available and blocked.";
        }

        if (stateful is null)
            yield break;

        List<string>? states = GetSchemaStates(stateful);
        if (states is null)
            yield break;

        foreach (string state in availableNames)
        {
            if (!states.Contains(state))
                yield return $"Available state '{state}' is absent from the assigned StateSchema.";
        }

        foreach (string state in GetNames(blocked))
        {
            if (!states.Contains(state))
                yield return $"Blocked state '{state}' is absent from the assigned StateSchema.";
        }
    }

    private static IEnumerable<string> ValidateStatefulTransitionAction(GodotObject obj)
    {
        if (GetObject(obj, "Definition") is null)
            yield return "Definition must be assigned.";

        foreach (string warning in ValidateStatefulReference(obj))
            yield return warning;

        Godot.Collections.Array from = GetArray(obj, "From");
        StringName to = obj.Get("To").AsStringName();
        if (from.Count == 0)
            yield return "From must be assigned.";
        if (to.IsEmpty)
            yield return "To must be assigned.";

        foreach (
            string warning in ValidateStatesAgainstLocalSchema(obj, GetStringNames(from).Append(to))
        )
            yield return warning;
    }

    private static IEnumerable<string> ValidateStatefulRunningTransitionAction(GodotObject obj)
    {
        if (GetObject(obj, "Definition") is null)
            yield return "Definition must be assigned.";

        foreach (string warning in ValidateStatefulReference(obj))
            yield return warning;

        Godot.Collections.Array from = GetArray(obj, "From");
        StringName running = obj.Get("Running").AsStringName();
        StringName completed = obj.Get("Completed").AsStringName();
        StringName cancelled = obj.Get("Cancelled").AsStringName();
        if (from.Count == 0)
            yield return "From must be assigned.";
        foreach (string property in new[] { "Running", "Completed", "Cancelled" })
        {
            if (obj.Get(property).AsStringName().IsEmpty)
                yield return $"{property} must be assigned.";
        }

        foreach (
            string warning in ValidateStatesAgainstLocalSchema(
                obj,
                GetStringNames(from).Append(running).Append(completed).Append(cancelled)
            )
        )
            yield return warning;
    }

    private static IEnumerable<string> ValidateStatefulTimedTransitionAction(GodotObject obj)
    {
        float duration = GetFloat(obj, "Duration");
        if (!float.IsFinite(duration) || duration <= 0.0f)
            yield return "Duration must be finite and greater than zero.";

        foreach (string warning in ValidateStatefulRunningTransitionAction(obj))
            yield return warning;
    }

    private static IEnumerable<string> ValidateStatefulReference(GodotObject obj)
    {
        if (GetObject(obj, "Stateful") is not null)
            yield break;

        List<GodotObject?> candidates = GetLocalStatefulCandidates(obj);
        if (candidates.Count > 1)
            yield return "Stateful composition is ambiguous: expected exactly one local StatefulComponent.";
        else if (candidates.Count == 0)
            yield return "Stateful must be assigned.";
    }

    private static IEnumerable<string> ValidateStatesAgainstLocalSchema(
        GodotObject obj,
        IEnumerable<StringName> authoredStates
    )
    {
        GodotObject? stateful = GetObject(obj, "Stateful") ?? ResolveLocalStateful(obj);
        if (stateful is null)
            yield break;

        List<string>? states = GetSchemaStates(stateful);
        if (states is null)
            yield break;

        foreach (StringName state in authoredStates)
        {
            if (!state.IsEmpty && !states.Contains(state.ToString()))
                yield return $"State '{state}' is absent from the assigned StateSchema.";
        }
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

            if (
                ResolveType(rule) != InspectableType.StatefulStateInteractionRule
                && ResolveType(rule) != InspectableType.StatefulAvailabilityInteractionRule
            )
                continue;

            if (ResolveType(rule) == InspectableType.StatefulAvailabilityInteractionRule)
            {
                foreach (string warning in ValidateStatefulAvailabilityRule(rule))
                    yield return $"{propertyName}[{index}]: {warning}";
                continue;
            }

            string prefix = $"{propertyName}[{index}]";

            GodotObject? explicitStateful = GetObject(rule, "Stateful");
            NodePath path = rule.Get("StatefulPath").AsNodePath();
            if (explicitStateful is null && path.IsEmpty && resolutionRoot is not Node)
            {
                yield return $"{prefix}: StatefulPath must be assigned.";
                continue;
            }

            Godot.Collections.Array expected = GetArray(rule, "ExpectedStates");
            if (expected.Count == 0)
                yield return $"{prefix}: ExpectedStates must declare at least one state.";

            GodotObject? target = explicitStateful;
            if (target is null && !path.IsEmpty && resolutionRoot is Node node)
            {
                target = node.GetNodeOrNull(path);
            }

            if (target is null && path.IsEmpty && resolutionRoot is Node localRoot)
            {
                target = ResolveLocalStateful(localRoot);
            }

            if (target is null)
            {
                if (path.IsEmpty)
                    yield return $"{prefix}: no unique StatefulComponent exists in the local Interactive scope.";
                else
                    yield return $"{prefix}: StatefulPath '{path}' does not resolve to a node.";
                continue;
            }

            // Only the owning node resolves a path; a rule inspected alone cannot.
            if (resolutionRoot is not Node && explicitStateful is null)
                continue;

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
            InteractionArea3D => InspectableType.InteractionArea3D,
            IndicationArea3D => InspectableType.IndicationArea3D,
            InteractionAnchor3D => InspectableType.InteractionAnchor3D,
            InteractionInteractor => InspectableType.InteractionInteractor,
            AreaInteractionDetector => InspectableType.AreaInteractionDetector,
            ProximityInteractionDetector => InspectableType.InteractionDetector,
            AimInteractionDetector => InspectableType.InteractionDetector,
            InteractionDetector => InspectableType.InteractionDetector,
            InteractionPresenter => InspectableType.InteractionPresenter,
            StatefulTimedTransitionAction => InspectableType.StatefulTimedTransitionAction,
            StatefulRunningTransitionAction => InspectableType.StatefulRunningTransitionAction,
            StatefulTransitionAction => InspectableType.StatefulTransitionAction,
            InteractionAction => InspectableType.InteractionAction,
            InteractionExecutionSynchronizer => InspectableType.InteractionExecutionSynchronizer,
            InteractionActionDefinition => InspectableType.InteractionActionDefinition,
            SetStateInteractionExecutor => InspectableType.SetStateInteractionExecutor,
            TimedTransitionStateInteractionExecutor =>
                InspectableType.TimedTransitionStateInteractionExecutor,
            TransitionStateInteractionExecutor =>
                InspectableType.TransitionStateInteractionExecutor,
            InteractionActionExecutor => InspectableType.InteractionActionExecutor,
            StatefulAvailabilityInteractionRule =>
                InspectableType.StatefulAvailabilityInteractionRule,
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
            nameof(InteractionArea3D) => InspectableType.InteractionArea3D,
            nameof(IndicationArea3D) => InspectableType.IndicationArea3D,
            nameof(InteractionAnchor3D) => InspectableType.InteractionAnchor3D,
            nameof(InteractionInteractor) => InspectableType.InteractionInteractor,
            nameof(AreaInteractionDetector) => InspectableType.AreaInteractionDetector,
            nameof(ProximityInteractionDetector) => InspectableType.InteractionDetector,
            nameof(AimInteractionDetector) => InspectableType.InteractionDetector,
            nameof(InteractionDetector) => InspectableType.InteractionDetector,
            nameof(InteractionPresenter) => InspectableType.InteractionPresenter,
            nameof(StatefulTimedTransitionAction) => InspectableType.StatefulTimedTransitionAction,
            nameof(StatefulRunningTransitionAction) =>
                InspectableType.StatefulRunningTransitionAction,
            nameof(StatefulTransitionAction) => InspectableType.StatefulTransitionAction,
            nameof(InteractionActionExecutor) => InspectableType.InteractionActionExecutor,
            nameof(InteractionAction) => InspectableType.InteractionAction,
            nameof(InteractionExecutionSynchronizer) =>
                InspectableType.InteractionExecutionSynchronizer,
            nameof(InteractionActionDefinition) => InspectableType.InteractionActionDefinition,
            nameof(SetStateInteractionExecutor) => InspectableType.SetStateInteractionExecutor,
            nameof(TimedTransitionStateInteractionExecutor) =>
                InspectableType.TimedTransitionStateInteractionExecutor,
            nameof(TransitionStateInteractionExecutor) =>
                InspectableType.TransitionStateInteractionExecutor,
            nameof(StatefulAvailabilityInteractionRule) =>
                InspectableType.StatefulAvailabilityInteractionRule,
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
            "res://addons/interaction_plugin/runtime/interactive/InteractionArea3D.cs" =>
                InspectableType.InteractionArea3D,
            "res://addons/interaction_plugin/runtime/interactive/IndicationArea3D.cs" =>
                InspectableType.IndicationArea3D,
            "res://addons/interaction_plugin/runtime/interactive/InteractionAnchor3D.cs" =>
                InspectableType.InteractionAnchor3D,
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
            "res://addons/interaction_plugin/integration/stateful/StatefulTransitionAction.cs" =>
                InspectableType.StatefulTransitionAction,
            "res://addons/interaction_plugin/integration/stateful/StatefulRunningTransitionAction.cs" =>
                InspectableType.StatefulRunningTransitionAction,
            "res://addons/interaction_plugin/integration/stateful/StatefulTimedTransitionAction.cs" =>
                InspectableType.StatefulTimedTransitionAction,
            "res://addons/interaction_plugin/runtime/interactive/InteractionExecutionSynchronizer.cs" =>
                InspectableType.InteractionExecutionSynchronizer,
            "res://addons/interaction_plugin/runtime/actions/InteractionActionDefinition.cs" =>
                InspectableType.InteractionActionDefinition,
            "res://addons/interaction_plugin/integration/stateful/SetStateInteractionExecutor.cs" =>
                InspectableType.SetStateInteractionExecutor,
            "res://addons/interaction_plugin/integration/stateful/TimedTransitionStateInteractionExecutor.cs" =>
                InspectableType.TimedTransitionStateInteractionExecutor,
            "res://addons/interaction_plugin/integration/stateful/TransitionStateInteractionExecutor.cs" =>
                InspectableType.TransitionStateInteractionExecutor,
            "res://addons/interaction_plugin/integration/stateful/StatefulStateInteractionRule.cs" =>
                InspectableType.StatefulStateInteractionRule,
            "res://addons/interaction_plugin/integration/stateful/StatefulAvailabilityInteractionRule.cs" =>
                InspectableType.StatefulAvailabilityInteractionRule,
            _ => InspectableType.None,
        };
    }

    private static Godot.Collections.Array GetArray(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsGodotArray();

    private static List<GodotObject?> GetObjects(Godot.Collections.Array values)
    {
        List<GodotObject?> objects = new();
        foreach (Variant value in values)
        {
            objects.Add(value.AsGodotObject());
        }

        return objects;
    }

    private static HashSet<string> GetNames(Godot.Collections.Array values)
    {
        HashSet<string> names = new();
        foreach (Variant value in values)
        {
            names.Add(value.AsStringName().ToString());
        }

        return names;
    }

    private static IEnumerable<StringName> GetStringNames(Godot.Collections.Array values)
    {
        foreach (Variant value in values)
        {
            yield return value.AsStringName();
        }
    }

    private static List<GodotObject?> GetDirectChildrenOfType(GodotObject obj, InspectableType type)
    {
        List<GodotObject?> children = new();
        if (obj is not Node node)
        {
            return children;
        }

        foreach (Node child in node.GetChildren())
        {
            if (MatchesInspectableType(ResolveType(child), type))
            {
                children.Add(child);
            }
        }

        return children;
    }

    private static bool MatchesInspectableType(InspectableType actual, InspectableType expected)
    {
        if (actual == expected)
        {
            return true;
        }

        return expected switch
        {
            InspectableType.InteractionAction => actual == InspectableType.StatefulTransitionAction
                || actual == InspectableType.StatefulRunningTransitionAction
                || actual == InspectableType.StatefulTimedTransitionAction,
            InspectableType.InteractionActionExecutor => actual
                == InspectableType.SetStateInteractionExecutor
                || actual == InspectableType.TransitionStateInteractionExecutor
                || actual == InspectableType.TimedTransitionStateInteractionExecutor,
            _ => false,
        };
    }

    private static bool HasResolvedExecutor(GodotObject obj)
    {
        if (obj is InteractionAction action)
        {
            return action.ResolveExecutor() is not null;
        }

        InspectableType type = ResolveType(obj);
        if (
            type == InspectableType.StatefulTransitionAction
            || type == InspectableType.StatefulRunningTransitionAction
            || type == InspectableType.StatefulTimedTransitionAction
        )
        {
            return true;
        }

        return GetObject(obj, "Executor") is not null
            || GetDirectChildrenOfType(obj, InspectableType.InteractionActionExecutor).Count == 1;
    }

    private static GodotObject? ResolveLocalStateful(GodotObject obj)
    {
        List<GodotObject?> candidates = GetLocalStatefulCandidates(obj);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static List<GodotObject?> GetLocalStatefulCandidates(GodotObject obj)
    {
        if (obj is not Node node)
        {
            return new List<GodotObject?>();
        }

        for (Node? current = node; current is not null; current = current.GetParent())
        {
            if (IsInteractiveComponent(current) && current.GetParent() is Node scope)
            {
                List<GodotObject?> candidates = new();
                foreach (Node child in scope.GetChildren())
                {
                    if (IsStatefulComponent(child))
                    {
                        candidates.Add(child);
                    }
                }

                return candidates;
            }
        }

        return new List<GodotObject?>();
    }

    private static bool IsInteractiveComponent(GodotObject obj) =>
        obj is InteractiveComponent || ResolveType(obj) == InspectableType.InteractiveComponent;

    private static bool IsStatefulComponent(GodotObject obj)
    {
        if (obj is StatefulComponent)
        {
            return true;
        }

        Script? script = GetAttachedScript(obj);
        return script?.GetGlobalName().ToString() == nameof(StatefulComponent)
            || script?.ResourcePath == "res://addons/stateful_plugin/runtime/StatefulComponent.cs";
    }

    private static bool GetBool(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsBool();

    private static int GetInt(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsInt32();

    private static float GetFloat(GodotObject obj, StringName propertyName) =>
        (float)obj.Get(propertyName).AsDouble();

    private static string GetName(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsStringName().ToString();

    private static bool IsProjectInputActionDeclared(string inputActionName) =>
        InputMap.HasAction(inputActionName)
        || ProjectSettings.HasSetting($"input/{inputActionName}");

    private static GodotObject? GetObject(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsGodotObject();

    private static Script? GetAttachedScript(GodotObject obj) =>
        obj.GetScript().AsGodotObject() as Script;
}

#endif
