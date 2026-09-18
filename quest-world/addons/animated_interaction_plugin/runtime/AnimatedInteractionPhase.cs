using Godot;

namespace AnimatedInteractionPlugin.Runtime;

/// <summary>One authored presentation step in an animated interaction.</summary>
[GlobalClass]
public partial class AnimatedInteractionPhase : Resource
{
    [Export]
    public StringName Animation { get; set; } = new(string.Empty);

    [Export]
    public AnimatedInteractionPlayback Playback { get; set; }

    [Export]
    public AnimatedInteractionTransition Transition { get; set; }

    /// <summary>
    /// Optional presentation-driven commit time in seconds. A negative value disables it.
    /// </summary>
    [Export(PropertyHint.Range, "-1,60,0.01,or_greater")]
    public float CommitPointSeconds { get; set; } = -1.0f;

    public bool HasCommitPoint => float.IsFinite(CommitPointSeconds) && CommitPointSeconds >= 0.0f;
}
