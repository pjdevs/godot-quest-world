using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Detection;

/// <summary>Detects targets through the detection areas each target authors around itself.</summary>
/// <remarks>
/// This is the model the framework started from, the forgiving one: the designer draws the volume in
/// which an object may be interacted with, the window below only decides where the player must look.
/// An area cut by hand is also a legitimate authored visibility volume, which no automatic occlusion
/// test can express: a grate one may interact through is simply kept off the occlusion layer, and the
/// authored volume is then the only word on visibility.
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
    /// Being inside either area is what makes a target exist at all; the window then decides whether
    /// the player is also looking at it. Losing the window therefore demotes a target to
    /// <see cref="InteractionDetectionKind.Indicated"/> and never to
    /// <see cref="InteractionDetectionKind.None"/>: the focused object one turns away from is still
    /// there, and its indication must stay where it was instead of blinking out.
    /// <para>
    /// Occlusion is the one thing that does remove a target: losing the window means the player looks
    /// elsewhere, while losing the line of sight means there is nothing to look at. A wall therefore
    /// takes the indication away too, which is exactly what the predicate is for.
    /// </para>
    /// </remarks>
    public override InteractionDetectionKind Detect(InteractiveComponent interactive)
    {
        if (
            interactive is null
            || !IsInstanceValid(interactive)
            || (
                !_interactionOverlaps.Contains(interactive)
                && !_indicationOverlaps.Contains(interactive)
            )
        )
        {
            return InteractionDetectionKind.None;
        }

        if (!HasLineOfSight(interactive))
        {
            return InteractionDetectionKind.None;
        }

        return
            _interactionOverlaps.Contains(interactive)
            && IsWithinRange(interactive, MaxDistance, MaxAngleDegrees)
            ? InteractionDetectionKind.Interactible
            : InteractionDetectionKind.Indicated;
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
        base.Forget(interactive);
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
