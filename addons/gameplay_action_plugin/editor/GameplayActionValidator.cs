#if TOOLS

using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Integration.Stateful;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Runner;

namespace QuestWorld.GameplayActions.Editor;

public static class GameplayActionValidator
{
    public static bool CanHandle(GodotObject obj) =>
        obj
            is GameplayActionComponent
                or GameplayAction
                or GameplayActionDefinition
                or GameplayActionBindingConfig
                or GameplayActionRunner
                or GameplayActionExecutionSynchronizer
                or SetStateGameplayActionExecutor
                or TransitionStateGameplayActionExecutor;

    public static IEnumerable<string> Validate(GodotObject obj)
    {
        switch (obj)
        {
            case GameplayActionComponent component:
                return ValidateComponent(component);
            case InputGameplayAction inputAction:
                return ValidateInputAction(inputAction);
            case GameplayAction action:
                return ValidateAction(action);
            case GameplayActionDefinition definition:
                return ValidateDefinition(definition);
            case GameplayActionBindingConfig binding:
                return ValidateBinding(binding);
            case GameplayActionRunner runner:
                return ValidateRunner(runner);
            case GameplayActionExecutionSynchronizer synchronizer:
                return ValidateSynchronizer(synchronizer);
            case TimedTransitionStateGameplayActionExecutor timed:
                return ValidateTimedTransition(timed);
            case TransitionStateGameplayActionExecutor transition:
                return ValidateTransition(transition);
            case SetStateGameplayActionExecutor setState:
                return ValidateSetState(setState);
            default:
                return System.Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ValidateComponent(GameplayActionComponent component)
    {
        if (component.Actions.Count == 0)
            yield return "Actions must declare at least one action.";

        HashSet<StringName> ids = new();
        bool hasReplicatedAction = false;
        for (int index = 0; index < component.Actions.Count; index++)
        {
            GameplayAction? action = component.Actions[index];
            if (action is null)
            {
                yield return $"Actions[{index}] must not be null.";
                continue;
            }

            foreach (string warning in ValidateAction(action))
                yield return $"Actions[{index}]: {warning}";

            StringName id = action.Definition?.Id ?? new StringName();
            if (!id.IsEmpty && !ids.Add(id))
                yield return $"Actions declare the action id '{id}' more than once.";

            if (action.Component is not null && action.Component != component)
                yield return $"Actions[{index}] is already owned by another component.";

            hasReplicatedAction |=
                action.ExecutionVisibility == GameplayActionExecutionVisibility.Replicated;
        }

        if (hasReplicatedAction && !HasMatchingSynchronizer(component))
            yield return "Replicated actions require a GameplayActionExecutionSynchronizer targeting this component.";
    }

    private static IEnumerable<string> ValidateAction(GameplayAction action)
    {
        if (action.Definition is null)
            yield return "Definition must be assigned.";
        else if (action.Definition.Id.IsEmpty)
            yield return "Definition Id must be assigned.";

        if (action.Executor is null)
            yield return "Executor must be assigned.";

        if (action.HostConcurrencyGroup is null || action.HostConcurrencyGroup.IsEmpty)
            yield return "HostConcurrencyGroup must not be empty.";

        for (int index = 0; index < action.Rules.Count; index++)
        {
            if (action.Rules[index] is null)
                yield return $"Rules[{index}] must not be null.";
        }
    }

    private static IEnumerable<string> ValidateInputAction(InputGameplayAction action)
    {
        foreach (string warning in ValidateAction(action))
            yield return warning;

        if (action.DefaultBindingConfig is not null)
        {
            foreach (string warning in ValidateBinding(action.DefaultBindingConfig))
                yield return $"DefaultBindingConfig: {warning}";
        }
    }

    private static IEnumerable<string> ValidateDefinition(GameplayActionDefinition definition)
    {
        if (definition.Id.IsEmpty)
            yield return "Id must be assigned.";
    }

    private static IEnumerable<string> ValidateBinding(GameplayActionBindingConfig binding)
    {
        if (binding.ActivationMode == GameplayActionActivationMode.Automatic)
        {
            if (binding.InputRequirement != GameplayActionInputRequirement.None)
                yield return "Automatic bindings must not require pressed input.";
        }
        else if (binding.InputActionName.IsEmpty)
        {
            yield return "InputActionName must be assigned for non-automatic bindings.";
        }
        else if (!InputMap.HasAction(binding.InputActionName))
        {
            yield return $"InputActionName '{binding.InputActionName}' is not declared in the project input map.";
        }

        if (binding.ActivationMode == GameplayActionActivationMode.Hold)
        {
            if (!float.IsFinite(binding.HoldDuration) || binding.HoldDuration <= 0.0f)
                yield return "HoldDuration must be finite and greater than zero for Hold bindings.";
        }
        else if (binding.HoldDuration != 0.0f)
        {
            yield return "HoldDuration is only used by Hold bindings.";
        }

        if (
            binding.ActivationMode == GameplayActionActivationMode.Release
            && binding.InputRequirement == GameplayActionInputRequirement.Pressed
        )
            yield return "Release bindings cannot require an input that is already released.";
    }

    private static IEnumerable<string> ValidateRunner(GameplayActionRunner runner)
    {
        if (runner.OwnedActionComponent is null)
            yield return "OwnedActionComponent must be assigned.";
    }

    private static IEnumerable<string> ValidateSynchronizer(
        GameplayActionExecutionSynchronizer synchronizer
    )
    {
        if (synchronizer.Component is null)
            yield return "Component must be assigned.";
    }

    private static IEnumerable<string> ValidateSetState(SetStateGameplayActionExecutor executor)
    {
        if (executor.Stateful is null)
        {
            yield return "Stateful must be assigned.";
            yield break;
        }

        if (executor.TargetState.IsEmpty)
        {
            yield return "TargetState must be assigned.";
            yield break;
        }

        List<string>? states = GetSchemaStates(executor.Stateful);
        if (states is not null && !states.Contains(executor.TargetState.ToString()))
            yield return $"TargetState '{executor.TargetState}' is absent from the assigned StateSchema.";
    }

    private static IEnumerable<string> ValidateTransition(
        TransitionStateGameplayActionExecutor executor
    )
    {
        if (executor.Stateful is null)
        {
            yield return "Stateful must be assigned.";
            yield break;
        }

        List<string>? states = GetSchemaStates(executor.Stateful);
        (string Property, StringName State)[] declared =
        {
            ("RunningState", executor.RunningState),
            ("CompletedState", executor.CompletedState),
            ("CancelledState", executor.CancelledState),
        };

        foreach ((string property, StringName state) in declared)
        {
            if (state.IsEmpty)
            {
                yield return $"{property} must be assigned.";
                continue;
            }

            if (states is not null && !states.Contains(state.ToString()))
                yield return $"{property} '{state}' is absent from the assigned StateSchema.";
        }
    }

    /// <summary>Lists the states a Stateful component declares, or null when it declares no schema.</summary>
    private static List<string>? GetSchemaStates(GodotObject stateful)
    {
        if (stateful.Get("Schema").AsGodotObject() is not GodotObject schema)
            return null;

        List<string> states = new();
        foreach (Variant state in schema.Get("States").AsGodotArray())
            states.Add(state.AsStringName().ToString());

        return states;
    }

    private static IEnumerable<string> ValidateTimedTransition(
        TimedTransitionStateGameplayActionExecutor executor
    )
    {
        if (!float.IsFinite(executor.Duration) || executor.Duration <= 0.0f)
            yield return "Duration must be finite and greater than zero.";
        foreach (string warning in ValidateTransition(executor))
            yield return warning;
    }

    private static bool HasMatchingSynchronizer(GameplayActionComponent component)
    {
        Node? parent = component.GetParent();
        if (parent is null)
            return false;

        foreach (Node child in parent.GetChildren())
        {
            if (
                child is GameplayActionExecutionSynchronizer synchronizer
                && synchronizer.Component == component
            )
                return true;
        }

        return false;
    }
}

#endif
