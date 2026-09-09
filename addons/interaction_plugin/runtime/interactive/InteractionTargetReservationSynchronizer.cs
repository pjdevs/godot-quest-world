using GameplayActionPlugin.Runtime.Actions;
using Godot;

namespace InteractionPlugin.Runtime.Interactive;

/// <summary>Replicates transient target-side Interaction reservation state.</summary>
/// <remarks>
/// This transport carries only the claim snapshot needed for remote availability and late join. It
/// never executes an action and never replaces persistent gameplay state.
/// </remarks>
[GlobalClass]
public partial class InteractionTargetReservationSynchronizer : MultiplayerSynchronizer
{
    private const string RevisionKey = "revision";
    private const string EntriesKey = "entries";

    /// <summary>Gets or sets the Interactive target whose reservations are synchronized.</summary>
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

    public InteractionTargetReservationSynchronizer()
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
        Interactive ??= GetParent() as InteractiveComponent;
        if (Interactive is null)
        {
            GD.PushError(
                $"{GetPath()}: InteractionTargetReservationSynchronizer requires an Interactive."
            );
            return;
        }

        Interactive.TargetReservationChanged += OnTargetReservationChanged;
        if (IsAuthoritative)
        {
            PublishSnapshot();
        }
    }

    public override void _ExitTree()
    {
        if (Interactive is not null && IsInstanceValid(Interactive))
        {
            Interactive.TargetReservationChanged -= OnTargetReservationChanged;
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
        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries =
            Interactive?.BuildTargetReservationEntries()
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
        Interactive.ApplyTargetReservationEntries(entries);
        return true;
    }

    private void OnTargetReservationChanged()
    {
        if (IsAuthoritative)
        {
            PublishSnapshot();
        }
    }

    private void PublishSnapshot() => ReplicatedSnapshot = CaptureSnapshot();
}
