using Godot;

namespace QuestWorld.GameplayActions.Runtime.Actions;

/// <summary>Reusable identity and player-facing metadata shared by gameplay action occurrences.</summary>
[GlobalClass]
public partial class GameplayActionDefinition : Resource
{
    /// <summary>Gets or sets the stable gameplay and network identity of the action.</summary>
    [Export]
    public StringName Id { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the player-facing action label.</summary>
    [Export]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional player-facing description.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;
}
