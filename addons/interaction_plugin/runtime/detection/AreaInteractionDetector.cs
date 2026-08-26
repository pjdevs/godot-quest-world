using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Detection;

/// <summary>Detects targets through the detection areas each target authors around itself.</summary>
/// <remarks>
/// This is the model the framework started from, the forgiving one: the designer draws the volume in
/// which an object may be interacted with, the window below only decides where the player must look.
/// An area cut by hand is also a legitimate authored visibility volume — it can allow an interaction
/// through a grate — which no automatic occlusion test can express.
/// <para>
/// The detector owns its source: overlap events push targets in and out of two sets, and it has no
/// opinion at all about an object it never saw enter. Because those events are pushed on every peer,
/// the authoritative peer validates a command against the very same sets without ever needing the
/// per-frame loop.
/// </para>
/// </remarks>
[GlobalClass]
public partial class AreaInteractionDetector : InteractionDetector
{
    /// <summary>Gets or sets the longest distance at which a target may be interacted with.</summary>
    [Export]
    public float MaxDistance { get; set; } = 10.0f;

    /// <summary>Gets or sets the widest view angle accepted for focus and authoritative validation.</summary>
    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxAngleDegrees { get; set; } = 30.0f;

    private readonly HashSet<InteractiveComponent> _interactionOverlaps = new();
    private readonly HashSet<InteractiveComponent> _indicationOverlaps = new();
    private readonly List<InteractiveComponent> _candidates = new();

    /// <inheritdoc />
    /// <remarks>
    /// Reaching the interaction area is what makes a target eligible, and the window then decides
    /// whether the player is looking at it. The wider indication area is the only thing that makes a
    /// target merely indicated: a target inside the interaction area but outside the window stays
    /// silent unless its owner authored an indication volume around it.
    /// </remarks>
    public override InteractionDetectionKind Detect(InteractiveComponent interactive)
    {
        if (interactive is null || !IsInstanceValid(interactive))
        {
            return InteractionDetectionKind.None;
        }

        if (
            _interactionOverlaps.Contains(interactive)
            && IsWithinRange(interactive, MaxDistance, MaxAngleDegrees)
        )
        {
            return InteractionDetectionKind.Interactible;
        }

        return _indicationOverlaps.Contains(interactive)
            ? InteractionDetectionKind.Indicated
            : InteractionDetectionKind.None;
    }

    /// <inheritdoc />
    protected internal override IEnumerable<InteractiveComponent> GetCandidates()
    {
        _interactionOverlaps.RemoveWhere(interactive => !IsInstanceValid(interactive));
        _indicationOverlaps.RemoveWhere(interactive => !IsInstanceValid(interactive));

        _candidates.Clear();
        foreach (InteractiveComponent interactive in _interactionOverlaps)
        {
            _candidates.Add(interactive);
        }

        foreach (InteractiveComponent interactive in _indicationOverlaps)
        {
            if (!_interactionOverlaps.Contains(interactive))
            {
                _candidates.Add(interactive);
            }
        }

        return _candidates;
    }

    /// <inheritdoc />
    protected internal override void OnEnteredTargetArea(
        InteractiveComponent interactive,
        InteractionDetectionKind kind
    )
    {
        SelectOverlaps(kind)?.Add(interactive);
    }

    /// <inheritdoc />
    protected internal override void OnExitedTargetArea(
        InteractiveComponent interactive,
        InteractionDetectionKind kind
    )
    {
        SelectOverlaps(kind)?.Remove(interactive);
    }

    /// <inheritdoc />
    protected internal override void Forget(InteractiveComponent interactive)
    {
        _interactionOverlaps.Remove(interactive);
        _indicationOverlaps.Remove(interactive);
    }

    private HashSet<InteractiveComponent>? SelectOverlaps(InteractionDetectionKind kind) =>
        kind switch
        {
            InteractionDetectionKind.Interactible => _interactionOverlaps,
            InteractionDetectionKind.Indicated => _indicationOverlaps,
            _ => null,
        };
}
