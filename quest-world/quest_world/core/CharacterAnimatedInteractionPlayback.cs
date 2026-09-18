using AnimatedInteractionPlugin.Runtime;
using DummyCharacterPlugin;
using Godot;

/// <summary>Quest World adapter between generic choreography and the demo character rig.</summary>
[GlobalClass]
public partial class CharacterAnimatedInteractionPlayback : Node, IAnimatedInteractionPlayback
{
    [Export]
    public CharacterAnimationController? AnimationController { get; set; }

    public bool TryGetInteractionAnimationDuration(StringName animation, out double durationSeconds)
    {
        durationSeconds = 0.0;
        return AnimationController?.TryGetOneShotDuration(animation, out durationSeconds) == true;
    }

    public bool PlayInteractionAnimation(
        StringName animation,
        AnimatedInteractionPlayback playback
    ) => AnimationController?.PlayOneShot(animation) == true;

    public void StopInteractionAnimation()
    {
        AnimationController?.StopOneShot();
    }
}
