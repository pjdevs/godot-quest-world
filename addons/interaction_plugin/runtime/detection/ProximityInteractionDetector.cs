using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Detection;

/// <summary>Detects every target within a radius, without any physics at all.</summary>
/// <remarks>
/// <b>Spike, not a delivered contract</b>: written to be looked at and felt in a scene, and kept or
/// dropped on that basis. It has no tests of its own.
/// <para>
/// The model: no area, no collider, no overlap event — the range is a number the target authors, and
/// discovery is a walk over the registry. Cheaper to author than a volume, and it makes an object
/// interactible the moment it exists. What it cannot express is a <b>shape</b>: for that the target
/// keeps the area detector, which is made for it.
/// </para>
/// </remarks>
[GlobalClass]
public partial class ProximityInteractionDetector : InteractionDetector
{
    /// <summary>Gets or sets the interaction distance used by a target that authors none.</summary>
    [Export]
    public float DefaultInteractionRadius { get; set; } = 2.5f;

    /// <summary>Gets or sets the indication distance used by a target that authors none.</summary>
    [Export]
    public float DefaultIndicationRadius { get; set; } = 12.0f;

    /// <summary>Gets or sets the widest view angle accepted for the interactible tier.</summary>
    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxAngleDegrees { get; set; } = 30.0f;

    private readonly List<InteractiveComponent> _candidates = new();

    /// <inheritdoc />
    /// <remarks>
    /// The indication tier is deliberately omnidirectional: knowing something is around you does not
    /// require looking at it, and losing the window must cost the focus rather than the indication.
    /// <para>
    /// The line of sight is what makes this model usable at all: without it a radius reaches straight
    /// through a wall, and the difference between range and <i>useful</i> range is the predicate.
    /// </para>
    /// </remarks>
    public override InteractionDetectionKind Detect(InteractiveComponent interactive)
    {
        if (interactive is null || !IsInstanceValid(interactive) || !HasLineOfSight(interactive))
        {
            return InteractionDetectionKind.None;
        }

        float interactionRadius =
            interactive.InteractionRadius > 0.0f
                ? interactive.InteractionRadius
                : DefaultInteractionRadius;
        if (IsWithinRange(interactive, interactionRadius, MaxAngleDegrees))
        {
            return InteractionDetectionKind.Interactible;
        }

        float indicationRadius =
            interactive.IndicationRadius > 0.0f
                ? interactive.IndicationRadius
                : DefaultIndicationRadius;
        return IsWithinRange(interactive, indicationRadius, 180.0f)
            ? InteractionDetectionKind.Indicated
            : InteractionDetectionKind.None;
    }

    /// <inheritdoc />
    protected internal override IEnumerable<InteractiveComponent> GetCandidates()
    {
        _candidates.Clear();
        _candidates.AddRange(InteractiveComponent.Registered);
        return _candidates;
    }
}
