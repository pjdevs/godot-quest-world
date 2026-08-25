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
}
