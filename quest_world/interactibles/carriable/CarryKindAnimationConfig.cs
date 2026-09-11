using Godot;

[GlobalClass]
public partial class CarryKindAnimationConfig : Resource
{
    [Export]
    public StringName TakeAnimationName { get; set; } = "";

    [Export]
    public StringName DropAnimationName { get; set; } = "";

    [Export]
    public float TakeAnimationCommitTimeSec { get; set; } = 0f;

    [Export]
    public float TakeAnimationEndTimeSec { get; set; } = 0f;

    [Export]
    public float DropAnimationCommitTimeSec { get; set; } = 0f;

    [Export]
    public float DropAnimationEndTimeSec { get; set; } = 0f;
}
