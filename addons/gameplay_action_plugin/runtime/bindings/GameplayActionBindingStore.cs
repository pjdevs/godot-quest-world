using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Bindings;

internal readonly record struct GameplayActionBindingCandidate(
    GameplayActionBinding Binding,
    GameplayActionAvailability Availability
);

internal sealed class GameplayActionBindingStore(
    Func<GameplayActionBinding, GameplayActionAvailability> evaluate
)
{
    private readonly Dictionary<ulong, BindingState> _bindings = new();
    private ulong _nextBindingId = 1;

    public GameplayActionBinding? Add(
        GameplayActionComponent component,
        StringName actionId,
        GodotObject source,
        GameplayActionBindingConfig config,
        Variant presentationContext,
        out bool automaticEdge
    )
    {
        automaticEdge = false;
        if (!IsValid(component, actionId, source, config) || _nextBindingId > (ulong)long.MaxValue)
        {
            return null;
        }

        GameplayActionBinding binding = new(
            _nextBindingId++,
            component,
            actionId,
            source,
            config.InputActionName,
            config.ActivationMode,
            config.HoldDuration,
            config.InputRequirement,
            config.Priority,
            presentationContext
        );
        GameplayActionAvailability availability = evaluate(binding);
        bool eligible = availability is GameplayActionAllowed;
        _bindings.Add(binding.Id, new BindingState(binding, availability, eligible));
        automaticEdge =
            binding.ActivationMode == GameplayActionActivationMode.Automatic && eligible;
        return binding;
    }

    public bool Remove(ulong bindingId) => _bindings.Remove(bindingId);

    public int RemoveSource(GodotObject source)
    {
        List<ulong> removed = new();
        foreach (BindingState state in _bindings.Values)
        {
            if (state.Binding.Source == source)
            {
                removed.Add(state.Binding.Id);
            }
        }

        foreach (ulong bindingId in removed)
        {
            _bindings.Remove(bindingId);
        }

        return removed.Count;
    }

    public IReadOnlyList<GameplayActionBinding> GetBindings()
    {
        List<GameplayActionBinding> bindings = new(_bindings.Count);
        foreach (BindingState state in _bindings.Values)
        {
            bindings.Add(state.Binding);
        }

        return bindings;
    }

    public bool TryGet(ulong bindingId, out GameplayActionBinding? binding)
    {
        if (_bindings.TryGetValue(bindingId, out BindingState? state))
        {
            binding = state.Binding;
            return true;
        }

        binding = null;
        return false;
    }

    public bool TryGet(
        GameplayActionComponent component,
        StringName actionId,
        GodotObject source,
        out GameplayActionBinding? binding
    )
    {
        foreach (BindingState state in _bindings.Values)
        {
            if (
                state.Binding.Component == component
                && state.Binding.ActionId == actionId
                && state.Binding.Source == source
            )
            {
                binding = state.Binding;
                return true;
            }
        }

        binding = null;
        return false;
    }

    public GameplayActionAvailability GetAvailability(ulong bindingId) =>
        _bindings.TryGetValue(bindingId, out BindingState? state)
            ? state.Availability
            : new GameplayActionHidden();

    public IReadOnlyList<GameplayActionBindingCandidate> GetInputCandidates(StringName input)
    {
        List<GameplayActionBindingCandidate> candidates = new();
        foreach (BindingState state in _bindings.Values)
        {
            if (
                state.Binding.ActivationMode != GameplayActionActivationMode.Automatic
                && state.Binding.InputActionName == input
                && state.Availability is not GameplayActionHidden
            )
            {
                candidates.Add(
                    new GameplayActionBindingCandidate(state.Binding, state.Availability)
                );
            }
        }

        return candidates;
    }

    public IReadOnlyList<GameplayActionBindingCandidate> InvalidateBinding(ulong bindingId)
    {
        return _bindings.TryGetValue(bindingId, out BindingState? state)
            ? Reevaluate(new[] { state })
            : System.Array.Empty<GameplayActionBindingCandidate>();
    }

    public IReadOnlyList<GameplayActionBindingCandidate> InvalidateSource(GodotObject source)
    {
        List<BindingState> affected = new();
        foreach (BindingState state in _bindings.Values)
        {
            if (state.Binding.Source == source)
            {
                affected.Add(state);
            }
        }

        return Reevaluate(affected);
    }

    public IReadOnlyList<GameplayActionBindingCandidate> InvalidateAction(
        GameplayActionComponent component,
        StringName actionId
    )
    {
        List<BindingState> affected = new();
        foreach (BindingState state in _bindings.Values)
        {
            if (state.Binding.Component == component && state.Binding.ActionId == actionId)
            {
                affected.Add(state);
            }
        }

        return Reevaluate(affected);
    }

    private IReadOnlyList<GameplayActionBindingCandidate> Reevaluate(
        IEnumerable<BindingState> affected
    )
    {
        List<GameplayActionBindingCandidate> automaticEdges = new();
        foreach (BindingState state in affected)
        {
            GameplayActionAvailability availability = evaluate(state.Binding);
            bool eligible = availability is GameplayActionAllowed;
            bool automaticEdge =
                state.Binding.ActivationMode == GameplayActionActivationMode.Automatic
                && eligible
                && !state.WasEligible;
            state.Availability = availability;
            state.WasEligible = eligible;
            if (automaticEdge)
            {
                automaticEdges.Add(new GameplayActionBindingCandidate(state.Binding, availability));
            }
        }

        return automaticEdges;
    }

    private static bool IsValid(
        GameplayActionComponent? component,
        StringName? actionId,
        GodotObject? source,
        GameplayActionBindingConfig? config
    )
    {
        if (
            component is null
            || actionId is null
            || actionId.IsEmpty
            || source is null
            || config is null
            || component.ResolveAction(actionId) is null
            || !Enum.IsDefined(config.ActivationMode)
            || !Enum.IsDefined(config.InputRequirement)
        )
        {
            return false;
        }

        if (config.ActivationMode == GameplayActionActivationMode.Automatic)
        {
            return (config.InputActionName is null || config.InputActionName.IsEmpty)
                && config.InputRequirement == GameplayActionInputRequirement.None
                && config.HoldDuration == 0.0f;
        }

        if (config.InputActionName is null || config.InputActionName.IsEmpty)
        {
            return false;
        }

        if (config.ActivationMode == GameplayActionActivationMode.Hold)
        {
            return float.IsFinite(config.HoldDuration) && config.HoldDuration > 0.0f;
        }

        return config.HoldDuration == 0.0f
            && !(
                config.ActivationMode == GameplayActionActivationMode.Release
                && config.InputRequirement == GameplayActionInputRequirement.Pressed
            );
    }

    private sealed class BindingState(
        GameplayActionBinding binding,
        GameplayActionAvailability availability,
        bool wasEligible
    )
    {
        public GameplayActionBinding Binding { get; } = binding;

        public GameplayActionAvailability Availability { get; set; } = availability;

        public bool WasEligible { get; set; } = wasEligible;
    }
}
