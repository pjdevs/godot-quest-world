using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Small running action that composes a three-state Stateful transition.</summary>
[GlobalClass]
public partial class StatefulRunningTransitionAction : StatefulTransitionActionBase
{
    private StatefulAvailabilityInteractionRule? _availabilityRule;

    /// <summary>Gets or sets the state applied while the external process runs.</summary>
    [Export]
    public StringName Running { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the state applied when the external process completes.</summary>
    [Export]
    public StringName Completed { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the state restored when the external process is cancelled.</summary>
    [Export]
    public StringName Cancelled { get; set; } = new(string.Empty);

    /// <summary>Gets or sets states that keep this action visible but blocked while it runs.</summary>
    [Export]
    public Godot.Collections.Array<StringName> BlockedStates { get; set; } = new();

    /// <summary>Gets or sets the reason shown while the action is in a blocked state.</summary>
    [Export]
    public string BlockReason { get; set; } = "Interaction unavailable.";

    /// <inheritdoc />
    public override IEnumerable<InteractionRule> ResolveRules()
    {
        if (BlockedStates.Count == 0)
        {
            foreach (InteractionRule rule in base.ResolveRules())
            {
                yield return rule;
            }

            yield break;
        }

        _availabilityRule ??= new StatefulAvailabilityInteractionRule();
        _availabilityRule.StatefulOverride = Stateful;
        _availabilityRule.AvailableStates.Clear();
        foreach (StringName state in From)
        {
            _availabilityRule.AvailableStates.Add(state);
        }

        _availabilityRule.BlockedStates.Clear();
        foreach (StringName state in BlockedStates)
        {
            _availabilityRule.BlockedStates.Add(state);
        }

        _availabilityRule.BlockReason = BlockReason;
        yield return _availabilityRule;

        foreach (InteractionRule rule in Rules)
        {
            yield return rule;
        }
    }

    /// <inheritdoc />
    protected override InteractionActionExecutor CreateComposedExecutor() =>
        new TransitionStateInteractionExecutor();

    /// <inheritdoc />
    protected override void ConfigureComposedExecutor(InteractionActionExecutor executor)
    {
        TransitionStateInteractionExecutor transition =
            (TransitionStateInteractionExecutor)executor;
        transition.Stateful = Stateful;
        transition.RunningState = Running;
        transition.CompletedState = Completed;
        transition.CancelledState = Cancelled;
    }
}
