using Godot;

namespace AnimatedInteractionPlugin.Runtime;

/// <summary>Determines how the current presentation phase is played.</summary>
public enum AnimatedInteractionPlayback
{
    OneShot,
    Loop,
}

/// <summary>Determines what releases the current presentation phase.</summary>
public enum AnimatedInteractionTransition
{
    Automatic,
    Controlled,
}

/// <summary>Actor-specific bridge used by the generic choreography component.</summary>
public interface IAnimatedInteractionPlayback
{
    bool TryGetInteractionAnimationDuration(StringName animation, out double durationSeconds);

    bool PlayInteractionAnimation(StringName animation, AnimatedInteractionPlayback playback);

    void StopInteractionAnimation();
}
