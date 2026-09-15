using Godot;

[GlobalClass]
public partial class CarryAnimationConfig : Resource
{
    [Export]
    public Godot.Collections.Dictionary<
        CarryKind,
        CarryKindAnimationConfig
    > AnimationByKind { get; set; } = [];

    public CarryKindAnimationConfig? GetConfigForKind(CarryKind kind) =>
        AnimationByKind.TryGetValue(kind, out CarryKindAnimationConfig? config) ? config : null;
}
