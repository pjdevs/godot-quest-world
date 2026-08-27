using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Detection;

/// <summary>Detects the targets a widened cast from the view sweeps through.</summary>
/// <remarks>
/// <b>Spike, not a delivered contract</b>: written to be looked at and felt in a scene, and kept or
/// dropped on that basis. It has no tests of its own.
/// <para>
/// The model: pointing decides, and pointing wins over being close — which is what
/// <see cref="Score"/> encodes by ranking on angle instead of distance. The cast is a
/// <b>source</b> and never a filter: it runs on the owning client only, in the physics frame, and the
/// authoritative peer never replays it. A cast is binary, and a player turning a mouse at a normal
/// speed has moved several degrees in one ping — replaying it server-side would refuse commands for no
/// reason other than the latency, invisibly. <see cref="Detect"/> therefore stays a tolerant window,
/// which the server can evaluate on its own.
/// </para>
/// <para>
/// The cast collides with the interaction areas the targets already author, so switching a character to
/// this detector needs no new collider anywhere. <see cref="AimRadius"/> is the forgiveness: at zero it
/// is a single precise ray, and widening it sweeps a cylinder that still touches what the crosshair
/// misses by a little. The sweep stops at its first impact, so what it reports is the <b>nearest</b>
/// area it meets and whatever overlaps it there, not a list of objects at different depths.
/// </para>
/// <para>
/// The cast reports areas and <b>not</b> bodies, so a wall does not stop it: the line of sight predicate
/// is what keeps this model from aiming through one, for the focus as much as for the indicated set.
/// </para>
/// </remarks>
[GlobalClass]
public partial class AimInteractionDetector : InteractionDetector
{
    /// <summary>Gets or sets how far the cast reaches.</summary>
    [Export]
    public float MaxDistance { get; set; } = 10.0f;

    /// <summary>Gets or sets the radius of the swept sphere, or zero for a single ray.</summary>
    /// <remarks>
    /// It forgives the aim without moving what wins: the tier and the focus are decided by the window
    /// and the score, both measured on the anchor.
    /// </remarks>
    [Export]
    public float AimRadius
    {
        get => _aimRadius;
        set
        {
            _aimRadius = value;
            ApplyCastParameters();
        }
    }

    /// <summary>Gets or sets the widest view angle accepted for the interactible tier.</summary>
    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxAngleDegrees { get; set; } = 12.0f;

    /// <summary>Gets or sets the physics layers the cast reports.</summary>
    [Export(PropertyHint.Layers3DPhysics)]
    public uint CollisionMask
    {
        get => _collisionMask;
        set
        {
            _collisionMask = value;
            ApplyCastParameters();
        }
    }

    /// <summary>Gets or sets how many hits one cast may report.</summary>
    [Export]
    public int MaxHits
    {
        get => _maxHits;
        set
        {
            _maxHits = value;
            ApplyCastParameters();
        }
    }

    private readonly List<InteractiveComponent> _hits = new();
    private float _aimRadius = 0.35f;
    private uint _collisionMask = 1;
    private int _maxHits = 8;
    private ShapeCast3D? _cast;

    /// <summary>Godot callback that builds the cast this detector sweeps with.</summary>
    /// <remarks>
    /// A detector is a Node rather than a Resource precisely so it may own children like this one, and
    /// the cast is created here rather than authored so a character needs nothing but the detector.
    /// </remarks>
    public override void _Ready()
    {
        base._Ready();
        _cast = new ShapeCast3D
        {
            Name = "AimCast",
            Shape = new SphereShape3D(),
            CollideWithAreas = true,
            CollideWithBodies = false,
            Enabled = true,
        };
        ApplyCastParameters();
        AddChild(_cast);
    }

    // The parameters live in the setters and not only here: they are the feel of this detector, and
    // tuning a radius or a mask on a running scene is how one finds the right value. MaxDistance is
    // already re-read every frame, being the length of the sweep itself.
    private void ApplyCastParameters()
    {
        if (_cast is null)
        {
            return;
        }

        if (_cast.Shape is SphereShape3D sphere)
        {
            sphere.Radius = Mathf.Max(AimRadius, 0.001f);
        }

        _cast.CollisionMask = CollisionMask;
        _cast.MaxResults = Mathf.Max(MaxHits, 1);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A window, never the cast: the authoritative peer must be able to answer this too. The line of
    /// sight belongs here rather than to the source for the same reason — the server evaluates the
    /// predicate itself, while it never replays the cast.
    /// </remarks>
    public override InteractionDetectionKind Detect(InteractiveComponent interactive)
    {
        if (interactive is null || !IsInstanceValid(interactive) || !HasLineOfSight(interactive))
        {
            return InteractionDetectionKind.None;
        }

        return IsWithinRange(interactive, MaxDistance, MaxAngleDegrees)
            ? InteractionDetectionKind.Interactible
            : InteractionDetectionKind.Indicated;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pointing at something is a stronger statement of intent than standing next to it, so the score
    /// is the angle rather than a blend with distance. It is negated because a greater score wins.
    /// </remarks>
    protected internal override float Score(InteractiveComponent interactive)
    {
        if (ViewOrigin is null)
        {
            return float.MinValue;
        }

        Vector3 offset = interactive.GetInteractionPosition() - ViewOrigin.GlobalPosition;
        return offset.LengthSquared() <= Mathf.Epsilon
            ? 0.0f
            : -(-ViewOrigin.GlobalBasis.Z).AngleTo(offset);
    }

    /// <inheritdoc />
    protected internal override IEnumerable<InteractiveComponent> GetCandidates() => _hits;

    /// <summary>Godot callback that re-aims the cast and collects what it swept through.</summary>
    /// <remarks>
    /// In the physics frame because that is the only place a physics query is sound once the physics is
    /// threaded, and the pipeline above reads the result on the next process frame.
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        _hits.Clear();

        // A cast is a physics query, and this character exists on every peer while only its owner
        // reads what the cast reports.
        if (_cast is null || ViewOrigin is null || !IsCandidateSourceActive)
        {
            return;
        }

        _cast.GlobalTransform = ViewOrigin.GlobalTransform;
        _cast.TargetPosition = new Vector3(0.0f, 0.0f, -Mathf.Max(MaxDistance, 0.0f));
        _cast.ForceShapecastUpdate();

        for (int index = 0; index < _cast.GetCollisionCount(); index++)
        {
            InteractiveComponent? hit = InteractiveComponent.FindByArea(_cast.GetCollider(index));
            if (hit is not null && !_hits.Contains(hit))
            {
                _hits.Add(hit);
            }
        }
    }
}
