using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Rules;
using Godot;
using StatefulPlugin;

namespace InteractionPlugin.Integration.Stateful;

/// <summary>
/// Makes one action available only while a <see cref="StatefulComponent"/> holds an expected state.
/// </summary>
/// <remarks>
/// This is the generic bridge between world state and availability: the interaction core never
/// interprets a state value, and this rule only reads one. Several ordered rules describe one action
/// completely, for example a first rule hiding <c>Open</c> outside the closed and opening phases, and
/// a second one blocking it with a reason while the door is still opening.
/// <para>
/// <see cref="StatefulPath"/> is resolved relative to the invocation target when one is supplied, or
/// the gameplay host/action otherwise. Because rules are shareable resources, a path crossing scene
/// boundaries belongs to the level that wires both objects together, exactly like the node reference
/// of an executor.
/// </para>
/// </remarks>
[GlobalClass]
public partial class StatefulStateInteractionRule : GameplayActionRule
{
    private const string NotConfiguredReason = "Interaction is not configured.";

    /// <summary>Gets or sets the path to the observed component, relative to the interaction target.</summary>
    [Export]
    public NodePath StatefulPath { get; set; } = new();

    /// <summary>Gets or sets the states making the action available, for example <c>closed</c>.</summary>
    /// <remarks>
    /// Several values express one phase of an object rather than one instant: <c>closed</c> and
    /// <c>opening</c> together describe every state in which opening is still the relevant choice.
    /// </remarks>
    [Export]
    public Godot.Collections.Array<StringName> ExpectedStates { get; set; } = new();

    /// <summary>Gets or sets whether the expected states are the ones making the action unavailable.</summary>
    [Export]
    public bool Invert { get; set; }

    /// <summary>Gets or sets the availability returned when the observed state does not match.</summary>
    /// <remarks>
    /// Hidden removes the action from the offered choices, which is the right answer for a choice
    /// that makes no sense yet. Blocked keeps it presentable and explains itself.
    /// </remarks>
    [Export]
    public GameplayActionUnavailableKind MismatchAvailability { get; set; } =
        GameplayActionUnavailableKind.Hidden;

    /// <summary>Gets or sets the reason displayed when the mismatch is blocked.</summary>
    [Export]
    public string BlockReason { get; set; } = "Interaction unavailable.";

    /// <inheritdoc />
    /// <remarks>
    /// The rule reads the state and never changes it. An unresolvable path or an empty state list is
    /// a configuration error, reported as blocked instead of silently allowing the action.
    /// </remarks>
    public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
    {
        StatefulComponent? stateful = ResolveStateful(context);
        if (stateful is null || ExpectedStates.Count == 0)
        {
            return new GameplayActionBlocked(NotConfiguredReason);
        }

        return ExpectedStates.Contains(stateful.State) != Invert
            ? new GameplayActionAllowed()
            : MismatchAvailability.ToAvailability(BlockReason);
    }

    private StatefulComponent? ResolveStateful(in GameplayActionContext context)
    {
        if (StatefulPath.IsEmpty)
        {
            return null;
        }

        Node? root = context.Target ?? context.Host ?? context.Action;
        if (root is null)
        {
            return null;
        }

        return root.GetNodeOrNull<StatefulComponent>(StatefulPath);
    }
}
