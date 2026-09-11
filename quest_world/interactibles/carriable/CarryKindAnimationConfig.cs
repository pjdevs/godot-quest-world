using Godot;

[GlobalClass]
public partial class CarryKindAnimationConfig : Resource
{
    [Export]
    public StringName TakeAnimationName { get; set; } = "PickUp_Kneeling";

    [Export]
    public StringName DropAnimationName { get; set; } = "PickUp_Kneeling";

    [Export]
    public float TakeAnimationCommitTimeSec { get; set; } = 0.7f;

    [Export]
    public float TakeAnimationEndTimeSec { get; set; } = 0.5f;

    [Export]
    public float DropAnimationCommitTimeSec { get; set; } = 0.7f;

    [Export]
    public float DropAnimationEndTimeSec { get; set; } = 0.5f;
}
