using Godot;

namespace QuestWorld.Inventory;

/// <summary>Replicates an inventory spawn snapshot and consolidated on-change quantity batches.</summary>
/// <remarks>
/// Add this optional node next to an <see cref="InventoryComponent"/> and assign <see cref="Inventory"/>.
/// The complete snapshot is read directly from the component when synchronization starts for a peer.
/// Existing peers receive one reliable dictionary of final per-item quantities per gameplay frame.
/// </remarks>
[GlobalClass]
public partial class InventoryReplicationSynchronizer : MultiplayerSynchronizer
{
    private int _serverPeerId = 1;

    /// <summary>Gets or sets the authoritative inventory observed by this transport.</summary>
    [Export]
    public InventoryComponent? Inventory { get; set; }

    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId
    {
        get => _serverPeerId;
        set
        {
            _serverPeerId = value;
            ApplyNetworkAuthority();
        }
    }

    [Export]
    private Godot.Collections.Dictionary<StringName, int> SpawnSnapshot
    {
        get => Inventory?.CaptureReplicationSnapshot() ?? new();
        set
        {
            if (!IsAuthoritative && Inventory is not null)
            {
                Inventory.ApplyReplicatedSnapshot(value);
            }
        }
    }

    [Export]
    private Godot.Collections.Dictionary<StringName, int> DeltaBatch
    {
        get => _deltaBatch;
        set
        {
            _deltaBatch = value;
            if (!IsAuthoritative && Inventory is not null)
            {
                Inventory.ApplyReplicatedChanges(value);
            }
        }
    }

    private Godot.Collections.Dictionary<StringName, int> _deltaBatch = new();
    private readonly Godot.Collections.Dictionary<StringName, int> _pendingChanges = new();

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    /// <summary>Creates the self-rooted spawn and on-change replication configuration.</summary>
    public InventoryReplicationSynchronizer()
    {
        RootPath = new NodePath(".");

        SceneReplicationConfig config = new();

        NodePath snapshotPath = new(".:SpawnSnapshot");
        config.AddProperty(snapshotPath);
        config.PropertySetSpawn(snapshotPath, true);
        config.PropertySetReplicationMode(
            snapshotPath,
            SceneReplicationConfig.ReplicationMode.Never
        );

        NodePath deltaPath = new(".:DeltaBatch");
        config.AddProperty(deltaPath);
        config.PropertySetSpawn(deltaPath, false);
        config.PropertySetReplicationMode(
            deltaPath,
            SceneReplicationConfig.ReplicationMode.OnChange
        );

        ReplicationConfig = config;
    }

    public override void _EnterTree()
    {
        ApplyNetworkAuthority();
    }

    private void ApplyNetworkAuthority()
    {
        if (
            !IsInsideTree()
            || ServerPeerId <= 0
            || GetMultiplayerAuthority() == ServerPeerId
        )
        {
            return;
        }

        SetMultiplayerAuthority(ServerPeerId, false);
    }

    /// <summary>Observes authoritative mutations that will form live replication batches.</summary>
    public override void _Ready()
    {
        if (Inventory is null)
        {
            GD.PushError($"{GetPath()}: InventoryReplicationSynchronizer requires an Inventory.");
            SetProcess(false);
            return;
        }

        if (!IsAuthoritative)
        {
            SetProcess(false);
            return;
        }

        Inventory.ItemQuantityChanged += OnItemQuantityChanged;
    }

    /// <summary>Publishes at most one consolidated delta property per gameplay frame.</summary>
    public override void _Process(double delta)
    {
        if (_pendingChanges.Count == 0)
        {
            return;
        }

        DeltaBatch = Clone(_pendingChanges);
        _pendingChanges.Clear();
    }

    /// <summary>Stops observing the inventory when this transport leaves the tree.</summary>
    public override void _ExitTree()
    {
        if (Inventory is not null && IsInstanceValid(Inventory))
        {
            Inventory.ItemQuantityChanged -= OnItemQuantityChanged;
        }
    }

    private void OnItemQuantityChanged(
        StringName itemId,
        int oldQuantity,
        int newQuantity,
        bool isSynchronization
    )
    {
        _pendingChanges[itemId] = newQuantity;
    }

    private static Godot.Collections.Dictionary<StringName, int> Clone(
        Godot.Collections.Dictionary<StringName, int> source
    )
    {
        Godot.Collections.Dictionary<StringName, int> clone = new();
        foreach ((StringName itemId, int quantity) in source)
        {
            clone[itemId] = quantity;
        }

        return clone;
    }
}
