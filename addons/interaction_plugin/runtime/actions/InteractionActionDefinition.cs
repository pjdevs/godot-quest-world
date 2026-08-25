using Godot;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Reusable static description of one interaction action, shared by every target that offers it.
/// </summary>
/// <remarks>
/// The resource holds only shareable data. Everything specific to one occurrence, such as the rules
/// deciding availability, belongs to the <see cref="InteractionAction"/> node that references it.
/// </remarks>
[GlobalClass]
public partial class InteractionActionDefinition : Resource
{
    /// <summary>Gets or sets the stable gameplay and network identity, for example <c>open</c>.</summary>
    /// <remarks>The identifier is never a player-facing label and must stay stable across builds.</remarks>
    [Export]
    public StringName Id { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the player-facing label, for example <c>Open</c>.</summary>
    [Export]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional player-facing description of the action.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the project input action that requests this interaction action.</summary>
    /// <remarks>Several actions may share one input; local resolution picks one of them.</remarks>
    [Export]
    public StringName InputActionName { get; set; } = "interact";

    /// <summary>
    /// Gets or sets how long <see cref="InputActionName"/> must be held before this action is
    /// selected, or zero to select it as soon as the input is pressed.
    /// </summary>
    /// <remarks>
    /// This is a local gesture threshold, not a duration: it exists only to tell apart several
    /// actions sharing one input, such as tapping to open and holding to force. It is resolved
    /// entirely on the client and never reaches the authoritative command, which still carries only
    /// a target and an action identifier. An action alone on its input keeps a threshold of zero.
    /// <para>
    /// Do not confuse it with <see cref="InteractionAction.Duration"/>: "hold to hack" is a
    /// threshold of zero with a running execution the server times, so the bar the player watches is
    /// authoritative. Stacking both simply adds the two waits.
    /// </para>
    /// </remarks>
    [Export]
    public float HoldThreshold { get; set; }

    /// <summary>
    /// Gets or sets whether releasing <see cref="InputActionName"/> cancels the running execution.
    /// </summary>
    /// <remarks>
    /// This describes how the input sustains the action, so it travels with the definition: an action
    /// the player must stay engaged in is sustained everywhere it appears. It only affects an
    /// execution still running when the input is released; an instant action holds none.
    /// </remarks>
    [Export]
    public bool CancelOnInputReleased { get; set; }
}
