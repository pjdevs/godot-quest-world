using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Detection;

/// <summary>Replaceable source and spatial filter deciding what one interactor may interact with.</summary>
/// <remarks>
/// How a game decides "which object can I interact with" belongs to the game, not to the framework:
/// a single view raycast and a forgiving proximity window are both legitimate, and the choice is made
/// per scene and even per interactor. Only the <b>source of candidates</b> varies between those
/// models; the predicates and the selection above it do not, which is why this layer is small.
/// <para>
/// Assign one to <see cref="Interactor.InteractionInteractor.Detector"/>. It is a Node rather than a
/// Resource because a detector legitimately needs children, signals, and per-frame work, and it is an
/// abstract Node rather than an interface to stay retranscriptible in GDExtension.
/// </para>
/// <para>
/// The owning client runs the whole pipeline every frame; the authoritative peer calls
/// <see cref="Detect"/> alone, on one target, to validate a command and to keep validating a sustained
/// execution. Two rhythms, one code path, so a client and a server cannot disagree by construction.
/// That is also why <see cref="Detect"/> must stay a tolerant window rather than a binary hit test:
/// the server sees a transform that is one ping old.
/// </para>
/// </remarks>
[GlobalClass]
public abstract partial class InteractionDetector : Node
{
    /// <summary>Gets or sets the required view transform used for angle, alignment, and scoring.</summary>
    [Export]
    public Node3D? ViewOrigin { get; set; }

    /// <summary>
    /// Gets or sets the optional physical origin used for distance. Defaults to the nearest Node3D ancestor.
    /// </summary>
    /// <remarks>
    /// Distance is measured from the body and angle from the view, because a third person character
    /// reaches with its body while it aims with its camera.
    /// </remarks>
    [Export]
    public Node3D? InteractionOrigin { get; set; }

    /// <summary>Gets or sets how strongly distance reduces the default focus score relative to alignment.</summary>
    /// <remarks>
    /// This parameterises <see cref="Score"/> only. A detector that overrides the scoring with a
    /// quantity of its own — an aim detector scores by angle — does not read it.
    /// </remarks>
    [Export]
    public float DistanceScoreCoefficient { get; set; } = 0.5f;

    /// <summary>Gets or sets the physics layers whose geometry hides a target from the view.</summary>
    /// <remarks>
    /// A dedicated occlusion layer rather than everything physical: level geometry must block a line
    /// of sight, while a crate in a loot pile must not hide the anchor of its neighbour — physically
    /// right, gameplay wrong. Occluding is therefore a property of the <b>occluder</b>: a wall carries
    /// the layer, a grate one may interact through simply does not, and no target declares anything.
    /// Zero disables the predicate altogether.
    /// </remarks>
    [Export(PropertyHint.Layers3DPhysics)]
    public uint OcclusionMask { get; set; } = 2;

    /// <summary>Gets or sets how long a lost line of sight must persist before it is reported.</summary>
    /// <remarks>
    /// The hysteresis is deliberately one-sided: regaining sight is immediate, losing it waits, so an
    /// indication does not blink while the player runs past a pole and a target never has to reappear.
    /// It also absorbs the transform lag between the client press and the server validation, exactly
    /// like the angle window does.
    /// </remarks>
    [Export]
    public float LineOfSightLossGrace { get; set; } = 0.15f;

    private const float LineOfSightRetention = 0.5f;

    private readonly Dictionary<InteractiveComponent, LineOfSightSample> _lineOfSight = new();
    private readonly List<InteractiveComponent> _forgottenLineOfSight = new();
    private Godot.Collections.Array<Rid>? _occlusionExclusions;
    private Node3D? _resolvedInteractionOrigin;

    /// <summary>Gets the origin distance is measured from, resolved on first use.</summary>
    public Node3D? ResolvedInteractionOrigin =>
        InteractionOrigin ?? (_resolvedInteractionOrigin ??= FindNearestSpatialAncestor());

    /// <summary>Godot callback that reports a detector unable to evaluate a single predicate.</summary>
    public override void _Ready()
    {
        if (ViewOrigin is null)
        {
            GD.PushError($"{GetPath()}: {GetType().Name} requires a ViewOrigin.");
        }

        if (ResolvedInteractionOrigin is null)
        {
            GD.PushError(
                $"{GetPath()}: {GetType().Name} requires an InteractionOrigin or a Node3D ancestor."
            );
        }
    }

    /// <summary>Godot callback that re-casts the line of sight of every target still being asked about.</summary>
    /// <remarks>
    /// The rays live in the physics frame because reading the direct space state anywhere else is
    /// fragile as soon as the physics runs on its own thread. A detector that overrides this callback
    /// has to call the base implementation, or its line of sight stops being refreshed.
    /// </remarks>
    /// <param name="delta">Seconds since the previous physics frame.</param>
    public override void _PhysicsProcess(double delta)
    {
        RefreshLineOfSight((float)delta);
    }

    /// <summary>Decides which detection tier one target reaches for this interactor right now.</summary>
    /// <remarks>
    /// The only mandatory member. Called once per candidate per frame on the owning client, and on a
    /// single target on the authoritative peer, both to accept a command and to keep validating an
    /// execution that requires the interactor to stay present. It must be side-effect free and must
    /// stay a tolerant window: a binary test would refuse commands for no reason other than the ping.
    /// </remarks>
    /// <param name="interactive">Target being evaluated.</param>
    /// <returns>The tier reached, or <see cref="InteractionDetectionKind.None"/>.</returns>
    public abstract InteractionDetectionKind Detect(InteractiveComponent interactive);

    /// <summary>Lists the targets worth calling <see cref="Detect"/> on this frame.</summary>
    /// <remarks>
    /// This is the part that actually varies between detection models: an area detector holds a set
    /// fed by overlap events and has no opinion on an object it never saw enter, while a cast detector
    /// returns the hits of its cast. The authoritative peer never calls this, so a source that only
    /// exists on the owning client stays perfectly validatable.
    /// <para>
    /// The returned sequence may be a buffer reused between calls, and the caller must not keep it.
    /// </para>
    /// </remarks>
    /// <returns>Distinct candidates, in no particular order.</returns>
    protected internal abstract IEnumerable<InteractiveComponent> GetCandidates();

    /// <summary>Ranks one eligible target so the interactor can pick a single focus.</summary>
    /// <remarks>
    /// The default favours what the player looks at, softened by distance. A detector whose model has
    /// a better notion of intent overrides it — aiming should win over merely being close — and the
    /// score is deliberately never exposed to presentation, because its unit changes with the
    /// detector.
    /// </remarks>
    /// <param name="interactive">Target reaching <see cref="InteractionDetectionKind.Interactible"/>.</param>
    /// <returns>Comparable score where a greater value wins the focus.</returns>
    protected internal virtual float Score(InteractiveComponent interactive)
    {
        Node3D? interactionOrigin = ResolvedInteractionOrigin;
        if (ViewOrigin is null || interactionOrigin is null)
        {
            return float.MinValue;
        }

        Vector3 interactionPosition = interactive.GetInteractionPosition();
        Vector3 viewOffset = interactionPosition - ViewOrigin.GlobalPosition;
        float distance = interactionPosition.DistanceTo(interactionOrigin.GlobalPosition);
        if (distance <= Mathf.Epsilon)
        {
            return 1.0f;
        }

        float alignment = Mathf.Max(0.0f, (-ViewOrigin.GlobalBasis.Z).Dot(viewOffset.Normalized()));
        return alignment / (1.0f + distance * Mathf.Max(DistanceScoreCoefficient, 0.0f));
    }

    /// <summary>Measures how far one target is from the body of this interactor.</summary>
    /// <remarks>
    /// Measured from the interaction origin and not from the view, so it is the very quantity the range
    /// window applies: a widget that animates on it agrees with the moment the interaction actually
    /// becomes possible. It exists because the presentation needs a <b>named physical quantity</b> —
    /// the raw score is deliberately never exposed, its unit changing with the detector.
    /// </remarks>
    /// <param name="interactive">Target being measured.</param>
    /// <returns>Distance in world units, or zero when no origin is resolved.</returns>
    public float GetInteractionDistance(InteractiveComponent interactive)
    {
        Node3D? interactionOrigin = ResolvedInteractionOrigin;
        return interactive is null || !IsInstanceValid(interactive) || interactionOrigin is null
            ? 0.0f
            : interactive.GetInteractionPosition().DistanceTo(interactionOrigin.GlobalPosition);
    }

    /// <summary>Tests one target against a distance and a view angle window.</summary>
    /// <remarks>
    /// Shared by every detector so that the tolerance a client applies and the one a server applies
    /// are the same code. The angle is a window on purpose: it absorbs the transform lag between the
    /// client press and the server validation.
    /// </remarks>
    /// <param name="interactive">Target being tested.</param>
    /// <param name="maxDistance">Longest accepted distance in world units.</param>
    /// <param name="maxAngleDegrees">Widest accepted angle from the view direction.</param>
    /// <returns>Whether the target sits inside the window.</returns>
    protected bool IsWithinRange(
        InteractiveComponent interactive,
        float maxDistance,
        float maxAngleDegrees
    )
    {
        Node3D? interactionOrigin = ResolvedInteractionOrigin;
        if (ViewOrigin is null || interactionOrigin is null)
        {
            return false;
        }

        Vector3 interactionPosition = interactive.GetInteractionPosition();
        Vector3 viewOffset = interactionPosition - ViewOrigin.GlobalPosition;
        float distance = interactionPosition.DistanceTo(interactionOrigin.GlobalPosition);
        if (distance > Mathf.Max(maxDistance, 0.0f))
        {
            return false;
        }

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        float alignment = (-ViewOrigin.GlobalBasis.Z).Dot(viewOffset.Normalized());
        float minimumAlignment = Mathf.Cos(
            Mathf.DegToRad(Mathf.Clamp(maxAngleDegrees, 0.0f, 180.0f))
        );
        return alignment >= minimumAlignment;
    }

    /// <summary>Tests whether the view reaches one target instead of the geometry in front of it.</summary>
    /// <remarks>
    /// A predicate of the base class rather than a detector of its own, because every model needs it:
    /// even a cast-based one, whose indicated set is wider than what its cast reports. The ray goes
    /// from the view to the target anchor, on the occlusion layers alone, ignoring the interactor's own
    /// body and the target's own geometry — reaching the object one aims at is not being occluded by
    /// it.
    /// <para>
    /// What it returns is the hysteresis-filtered result refreshed in
    /// <see cref="_PhysicsProcess(double)"/>, so asking per candidate per frame costs a dictionary
    /// lookup. A target nobody has asked about yet is cast for on the spot: the authoritative peer
    /// validates a one-shot command outside any physics frame, and answering "occluded" until the next
    /// one would refuse a legitimate command for a reason no player can see.
    /// </para>
    /// </remarks>
    /// <param name="interactive">Target being tested.</param>
    /// <returns>Whether the target is visible from the view origin.</returns>
    protected bool HasLineOfSight(InteractiveComponent interactive)
    {
        if (interactive is null || !IsInstanceValid(interactive) || OcclusionMask == 0)
        {
            return true;
        }

        if (ViewOrigin is null)
        {
            return false;
        }

        if (_lineOfSight.TryGetValue(interactive, out LineOfSightSample? sample))
        {
            sample.IdleTime = 0.0f;
            return sample.Visible;
        }

        LineOfSightSample fresh = new() { Visible = CastLineOfSight(interactive) };
        _lineOfSight[interactive] = fresh;
        return fresh.Visible;
    }

    /// <summary>Reports that this interactor entered one of a target's own detection areas.</summary>
    /// <remarks>
    /// The framework's per-target area model pushes overlap events here on every peer, because only
    /// the target owns its areas. A detector with a source of its own ignores them, which is why the
    /// hook is a virtual no-op rather than a member of the mandatory contract.
    /// </remarks>
    /// <param name="interactive">Target whose area was entered.</param>
    /// <param name="kind">Tier that area authors.</param>
    protected internal virtual void OnEnteredTargetArea(
        InteractiveComponent interactive,
        InteractionDetectionKind kind
    ) { }

    /// <summary>Reports that this interactor left one of a target's own detection areas.</summary>
    /// <param name="interactive">Target whose area was left.</param>
    /// <param name="kind">Tier that area authors.</param>
    protected internal virtual void OnExitedTargetArea(
        InteractiveComponent interactive,
        InteractionDetectionKind kind
    ) { }

    /// <summary>Drops every reference this detector holds to a target leaving the tree.</summary>
    /// <remarks>
    /// Called on every peer by the target itself, because an area does not report an overlap it loses
    /// by being freed. A detector whose source holds no reference of its own has nothing to do here.
    /// </remarks>
    /// <param name="interactive">Target being torn down.</param>
    protected internal virtual void Forget(InteractiveComponent interactive)
    {
        _lineOfSight.Remove(interactive);
    }

    private void RefreshLineOfSight(float delta)
    {
        if (_lineOfSight.Count == 0)
        {
            return;
        }

        _forgottenLineOfSight.Clear();
        foreach ((InteractiveComponent interactive, LineOfSightSample sample) in _lineOfSight)
        {
            sample.IdleTime += delta;
            if (!IsInstanceValid(interactive) || sample.IdleTime > LineOfSightRetention)
            {
                // Nobody has asked about this target for a while, so it left the pipeline: stop
                // paying a ray for it, and let the next query pay for a fresh answer rather than
                // read a stale one.
                _forgottenLineOfSight.Add(interactive);
                continue;
            }

            if (OcclusionMask == 0 || CastLineOfSight(interactive))
            {
                sample.Visible = true;
                sample.PendingLoss = 0.0f;
                continue;
            }

            if (!sample.Visible)
            {
                continue;
            }

            sample.PendingLoss += delta;
            if (sample.PendingLoss >= Mathf.Max(LineOfSightLossGrace, 0.0f))
            {
                sample.Visible = false;
                sample.PendingLoss = 0.0f;
            }
        }

        foreach (InteractiveComponent interactive in _forgottenLineOfSight)
        {
            _lineOfSight.Remove(interactive);
        }
    }

    private bool CastLineOfSight(InteractiveComponent interactive)
    {
        PhysicsDirectSpaceState3D? space = ViewOrigin?.GetWorld3D()?.DirectSpaceState;
        if (space is null)
        {
            return true;
        }

        Godot.Collections.Dictionary hit = space.IntersectRay(
            PhysicsRayQueryParameters3D.Create(
                ViewOrigin!.GlobalPosition,
                interactive.GetInteractionPosition(),
                OcclusionMask,
                ResolveOcclusionExclusions()
            )
        );

        // Stopping on the target's own geometry is reaching it: an object never occludes itself, and
        // an anchor authored inside the mesh that carries it would otherwise never be visible.
        return !hit.TryGetValue("collider", out Variant collider)
            || interactive.OwnsCollider(collider.AsGodotObject());
    }

    private Godot.Collections.Array<Rid> ResolveOcclusionExclusions()
    {
        if (_occlusionExclusions is not null)
        {
            return _occlusionExclusions;
        }

        // The interactor's own body sits on the ray it casts, so it would occlude everything.
        _occlusionExclusions = new();
        for (Node? ancestor = GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is CollisionObject3D body)
            {
                _occlusionExclusions.Add(body.GetRid());
                break;
            }
        }

        return _occlusionExclusions;
    }

    private Node3D? FindNearestSpatialAncestor()
    {
        for (Node? ancestor = GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is Node3D spatial)
            {
                return spatial;
            }
        }

        return null;
    }

    /// <summary>Line of sight of one target, as reported to the pipeline rather than as last cast.</summary>
    /// <remarks>
    /// The reported value and the raw one are deliberately different things: the pending loss is what
    /// keeps a pole from making an indication blink, and the idle time is what makes the cache follow
    /// the pipeline instead of growing with everything ever seen.
    /// </remarks>
    private sealed class LineOfSightSample
    {
        public bool Visible;

        public float PendingLoss;

        public float IdleTime;
    }
}
