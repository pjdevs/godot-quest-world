namespace QuestWorld.Tests;

using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;

/// <summary>Detector whose source and tiers are written directly by a test.</summary>
/// <remarks>
/// The point of the replaceable detection layer: focus, presentation, and commands become testable
/// without a single area, body, or physics frame. The default scoring is kept, so a test that places
/// two targets still gets the focus the real pipeline would pick.
/// </remarks>
internal sealed partial class TestInteractionDetector : InteractionDetector
{
    private readonly Dictionary<InteractiveComponent, InteractionDetectionKind> _detected = new();
    private readonly List<InteractiveComponent> _candidates = new();

    /// <summary>Declares which tier one target reaches from now on.</summary>
    public void SetDetection(InteractiveComponent interactive, InteractionDetectionKind kind)
    {
        if (kind == InteractionDetectionKind.None)
        {
            _detected.Remove(interactive);
            return;
        }

        _detected[interactive] = kind;
    }

    /// <summary>Stops detecting one target at all.</summary>
    public void ClearDetection(InteractiveComponent interactive) => _detected.Remove(interactive);

    /// <inheritdoc />
    public override InteractionDetectionKind Detect(InteractiveComponent interactive) =>
        interactive is not null
        && IsInstanceValid(interactive)
        && _detected.TryGetValue(interactive, out InteractionDetectionKind kind)
            ? kind
            : InteractionDetectionKind.None;

    /// <inheritdoc />
    protected internal override IEnumerable<InteractiveComponent> GetCandidates()
    {
        _candidates.Clear();
        _candidates.AddRange(_detected.Keys);
        return _candidates;
    }

    /// <inheritdoc />
    protected internal override void Forget(InteractiveComponent interactive)
    {
        base.Forget(interactive);
        _detected.Remove(interactive);
    }
}
