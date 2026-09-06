using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Execution;

/// <summary>Replicates transient presentation for executions whose visibility is Replicated.</summary>
/// <remarks>
/// This node transports execution snapshots only. It does not execute actions, replicate dynamic
/// action grants, or replace persistent domain state.
/// </remarks>
[GlobalClass]
public partial class GameplayActionExecutionSynchronizer : MultiplayerSynchronizer
{
    private const string RevisionKey = "revision";
    private const string EntriesKey = "entries";

    /// <summary>Gets or sets the action component whose visible executions are synchronized.</summary>
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

    /// <summary>Creates the code-owned replication configuration for the transient snapshot.</summary>
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

    /// <summary>Starts observing the configured component and publishes authority's initial snapshot.</summary>
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

    /// <summary>Stops observing the configured component.</summary>
    public override void _ExitTree()
    {
        if (Component is not null && IsInstanceValid(Component))
        {
            Component.ExecutionPresentationChanged -= OnExecutionPresentationChanged;
        }
    }

    /// <summary>Hides the technical replicated snapshot from the Inspector.</summary>
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
        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries =
            Component?.BuildReplicatedExecutionEntries()
            ?? new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
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

        Godot.Collections.Array rawEntries = entriesValue.AsGodotArray();
        foreach (Variant entry in rawEntries)
        {
            if (entry.VariantType != Variant.Type.Dictionary)
            {
                return false;
            }
        }

        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries;
        try
        {
            entries = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>(
                rawEntries
            );
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }

        _lastAppliedRevision = revision;
        Component.ApplyReplicatedExecutionEntries(entries);
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
