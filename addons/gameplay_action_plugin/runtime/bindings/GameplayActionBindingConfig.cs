using Godot;

namespace QuestWorld.GameplayActions.Runtime.Bindings;

/// <summary>Authoring data copied into one local gameplay action input binding.</summary>
[GlobalClass]
public partial class GameplayActionBindingConfig : Resource
{
    /// <summary>Gets or sets the Input Map action used by this binding.</summary>
    [Export]
    public StringName InputActionName { get; set; } = new();

    /// <summary>Gets or sets the gesture edge or hold mode that selects the binding.</summary>
    [Export]
    public GameplayActionActivationMode ActivationMode { get; set; }

    /// <summary>Gets or sets the local hold threshold used when <see cref="ActivationMode"/> is Hold.</summary>
    [Export]
    public float HoldDuration { get; set; }

    /// <summary>Gets or sets the input state required to sustain an accepted request.</summary>
    [Export]
    public GameplayActionInputRequirement InputRequirement { get; set; }

    /// <summary>Gets or sets authored priority when several eligible bindings share an input.</summary>
    [Export]
    public int Priority { get; set; }
}
