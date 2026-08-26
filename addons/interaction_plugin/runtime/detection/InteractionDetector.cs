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
    protected internal virtual void Forget(InteractiveComponent interactive) { }

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
}
