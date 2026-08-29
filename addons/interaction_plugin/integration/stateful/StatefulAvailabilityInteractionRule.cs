using Godot;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>
/// Exposes a Stateful value as allowed, blocked, or hidden without stacking repetitive rules.
/// </summary>
[GlobalClass]
public partial class StatefulAvailabilityInteractionRule : InteractionRule
{
    private const string NotConfiguredReason = "Interaction is not configured.";

    /// <summary>Gets the runtime target supplied by a composed action helper.</summary>
    internal StatefulComponent? StatefulOverride { get; set; }

    /// <summary>Gets or sets the optional explicit path to the inspected component.</summary>
    [ExportGroup("Overrides")]
    [Export]
    public NodePath StatefulPath { get; set; } = new();

    /// <summary>Gets or sets the states that keep the action available.</summary>
    [Export]
    public Godot.Collections.Array<StringName> AvailableStates { get; set; } = new();

    /// <summary>Gets or sets the states that keep the action visible but blocked.</summary>
    [Export]
    public Godot.Collections.Array<StringName> BlockedStates { get; set; } = new();

    /// <summary>Gets or sets the reason displayed for a blocked state.</summary>
    [Export]
    public string BlockReason { get; set; } = "Interaction unavailable.";

    /// <inheritdoc />
    public override InteractionAvailability Evaluate(in InteractionContext context)
    {
        StatefulComponent? stateful = ResolveStateful(context);
        if (stateful is null || (AvailableStates.Count == 0 && BlockedStates.Count == 0))
        {
            return new InteractionBlocked(NotConfiguredReason);
        }

        if (AvailableStates.Contains(stateful.State))
        {
            return new InteractionAllowed();
        }

        return BlockedStates.Contains(stateful.State)
            ? new InteractionBlocked(BlockReason)
            : new InteractionHidden();
    }

    private StatefulComponent? ResolveStateful(in InteractionContext context)
    {
        if (StatefulOverride is not null)
        {
            return StatefulOverride;
        }

        if (!StatefulPath.IsEmpty)
        {
            return context.Action.GetNodeOrNull<StatefulComponent>(StatefulPath);
        }

        return StatefulComposition.ResolveLocal(context.Interactive);
    }
}
