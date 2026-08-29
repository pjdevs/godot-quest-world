using Godot;

namespace QuestWorld.Interaction.Runtime.Interactive;

/// <summary>Replicates the current presentation state of world-observable executions.</summary>
/// <remarks>
/// Add this node as a child of an <see cref="InteractiveComponent"/> that authors at least one
/// <see cref="InteractionExecutionVisibility.Replicated"/> action and assign <see cref="Interactive"/>.
/// Native synchronizer visibility remains the interest-management boundary.
/// </remarks>
[GlobalClass]
public partial class InteractionExecutionSynchronizer : MultiplayerSynchronizer
{
    private const string RevisionKey = "revision";
    private const string EntriesKey = "entries";

    /// <summary>Gets or sets the target whose execution read model this node transports.</summary>
    [Export]
    public InteractiveComponent? Interactive { get; set; }

    [Export]
    private Godot.Collections.Dictionary ReplicatedSnapshot
    {
        get => _replicatedSnapshot;
        set
        {
            _replicatedSnapshot = value;
            if (!IsAuthoritative)
            {
                ApplySnapshot(value);
            }
        }
    }

    private Godot.Collections.Dictionary _replicatedSnapshot = new();
    private long _outgoingRevision;
    private long _lastAppliedRevision;

    /// <summary>Creates the self-rooted replication configuration before the node enters a tree.</summary>
    public InteractionExecutionSynchronizer()
    {
        RootPath = new NodePath(".");
        SceneReplicationConfig config = new();
        NodePath property = new(".:ReplicatedSnapshot");
        config.AddProperty(property);
        config.PropertySetSpawn(property, true);
        config.PropertySetReplicationMode(
            property,
            SceneReplicationConfig.ReplicationMode.OnChange
        );
        ReplicationConfig = config;
    }

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    /// <summary>Initializes the self-rooted replication property and observes sparse target changes.</summary>
    public override void _Ready()
    {
        if (Interactive is null)
        {
            GD.PushError($"{GetPath()}: InteractionExecutionSynchronizer requires an Interactive.");
            return;
        }

        Interactive.ExecutionPresentationChanged += OnExecutionPresentationChanged;
        if (IsAuthoritative)
        {
            PublishSnapshot();
        }
    }

    /// <summary>Disconnects the observed target when the synchronizer leaves the tree.</summary>
    public override void _ExitTree()
    {
        if (Interactive is not null && IsInstanceValid(Interactive))
        {
            Interactive.ExecutionPresentationChanged -= OnExecutionPresentationChanged;
        }
    }

    internal Godot.Collections.Dictionary CaptureSnapshot()
    {
        Godot.Collections.Array entries =
            Interactive?.BuildReplicatedExecutionEntries() ?? new Godot.Collections.Array();
        return new Godot.Collections.Dictionary
        {
            [RevisionKey] = ++_outgoingRevision,
            [EntriesKey] = entries,
        };
    }

    internal bool ApplySnapshot(Godot.Collections.Dictionary snapshot)
    {
        if (
            Interactive is null
            || !snapshot.TryGetValue(RevisionKey, out Variant revisionValue)
            || !snapshot.TryGetValue(EntriesKey, out Variant entriesValue)
            || revisionValue.VariantType != Variant.Type.Int
            || entriesValue.VariantType != Variant.Type.Array
        )
        {
            return false;
        }

        long revision = revisionValue.AsInt64();
        if (revision <= _lastAppliedRevision)
        {
            return false;
        }

        _lastAppliedRevision = revision;
        Interactive.ApplyReplicatedExecutionEntries(entriesValue.AsGodotArray());
        return true;
    }

    private void OnExecutionPresentationChanged(StringName actionId)
    {
        if (IsAuthoritative)
        {
            PublishSnapshot();
        }
    }

    private void PublishSnapshot()
    {
        ReplicatedSnapshot = CaptureSnapshot();
    }
}
