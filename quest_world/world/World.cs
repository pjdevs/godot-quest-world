using Godot;

[GlobalClass]
public partial class World : Node3D
{
    [Export]
    public BatterySpawner? BatterySpawner { get; set; }

    [Export]
    public PlayerSpawner? PlayerSpawner { get; set; }

    [Export]
    public NetworkSession? NetworkSession { get; set; }

    public override void _Ready()
    {
        if (NetworkSession is null)
        {
            GD.PushError("QuestWorldWorld: QuestWorldWorld.NetworkSession is required.");
            return;
        }

        NetworkSession.Initialize();

        if (NetworkSession.IsServer)
        {
            InitializeAuthority();
        }
    }

    public void InitializeAuthority()
    {
        IWorldSystem?[] systems = [BatterySpawner, PlayerSpawner];

        foreach (IWorldSystem? system in systems)
        {
            system?.InitializeAuthority();
        }
    }
}
