using Godot;

[GlobalClass]
public partial class QuestWorldWorld : Node3D, IWorldSystem
{
    [Export]
    public BatterySpawner? BatterySpawner { get; set; }

    [Export]
    public PlayerSpawner? PlayerSpawner { get; set; }

    public void InitializeAuthority()
    {
        IWorldSystem?[] systems = [BatterySpawner, PlayerSpawner];

        foreach (IWorldSystem? system in systems)
        {
            system?.InitializeAuthority();
        }
    }
}
