using Godot;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class GameplayActionDefinition : Resource
{
    [Export]
    public StringName Id { get; set; } = new(string.Empty);

    [Export]
    public string Label { get; set; } = string.Empty;

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;
}
