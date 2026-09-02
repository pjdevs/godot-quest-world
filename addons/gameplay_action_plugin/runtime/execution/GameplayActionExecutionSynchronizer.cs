using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Execution;

[GlobalClass]
public partial class GameplayActionExecutionSynchronizer : MultiplayerSynchronizer
{
    private const string RevisionKey = "revision";
    private const string EntriesKey = "entries";

    [Export]
    public GameplayActionComponent? Component { get; set; }

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

    public GameplayActionExecutionSynchronizer()
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

    public override void _Ready()
    {
        if (Component is null)
        {
            GD.PushError($"{GetPath()}: GameplayActionExecutionSynchronizer requires a Component.");
            return;
        }

        Component.ExecutionPresentationChanged += OnExecutionPresentationChanged;
        if (IsAuthoritative)
        {
            PublishSnapshot();
        }
    }

    public override void _ExitTree()
    {
        if (Component is not null && IsInstanceValid(Component))
        {
            Component.ExecutionPresentationChanged -= OnExecutionPresentationChanged;
        }
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsString() != nameof(ReplicatedSnapshot))
        {
            return;
        }

        PropertyUsageFlags usage = property["usage"].As<PropertyUsageFlags>();
        property["usage"] = (int)(usage & ~PropertyUsageFlags.Editor);
    }

    internal Godot.Collections.Dictionary CaptureSnapshot()
    {
        Godot.Collections.Array entries =
            Component?.BuildReplicatedExecutionEntries() ?? new Godot.Collections.Array();
        return new Godot.Collections.Dictionary
        {
            [RevisionKey] = ++_outgoingRevision,
            [EntriesKey] = entries,
        };
    }

    internal bool ApplySnapshot(Godot.Collections.Dictionary snapshot)
    {
        if (
            Component is null
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
        Component.ApplyReplicatedExecutionEntries(entriesValue.AsGodotArray());
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
