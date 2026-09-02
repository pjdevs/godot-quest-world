using Godot;

namespace QuestWorld.GameplayActions.Runtime.Bindings;

[GlobalClass]
public partial class GameplayActionBindingConfig : Resource
{
    [Export]
    public StringName InputActionName { get; set; } = new();

    [Export]
    public GameplayActionActivationMode ActivationMode { get; set; }

    [Export]
    public float HoldDuration { get; set; }

    [Export]
    public GameplayActionInputRequirement InputRequirement { get; set; }

    [Export]
    public int Priority { get; set; }
}
