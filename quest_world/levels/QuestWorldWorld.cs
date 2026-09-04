using Godot;

[GlobalClass]
public partial class QuestWorldWorld : Node3D
{
    [Export]
    public BatterySpawner? BatterySpawner { get; set; }
}
