# Animated Interaction Plugin

Experimental runtime choreography for actor animations that accompany a Gameplay Action.

The addon sequences one authored `AnimatedInteractionProfile` at a time. Automatic phases advance
when their clip duration elapses; controlled phases wait for `AnimatedInteractionOperation.Advance()`.
A profile may expose one commit boundary, reached either by an authored phase offset or by gameplay
calling `ReachCommit()`.

The addon deliberately does not own Gameplay Action completion, gameplay progress, input, inventory,
door state or other domain mutations. Actor-specific discovery also stays outside the addon: Quest
World exposes `IAnimatedInteractor` from `quest_world/core`.

## Setup

Add an `AnimatedInteractionComponent` to the actor and point `PlaybackNode` at a node implementing
`IAnimatedInteractionPlayback`. Then author an `AnimatedInteractionProfile` resource containing
ordered `AnimatedInteractionPhase` resources.

Quest World provides a plug-and-play `AnimatedInteractionExecutor` in `quest_world/core`, outside this
generic addon. Assign a profile to it and optionally set `WorkDuration`:

```text
WorkDuration = 0
    automatic-only profile, or a controlled phase with an authored commit point

WorkDuration > 0
    timer starts when the single controlled phase is entered
    expiry -> commit -> controlled phase advances -> automatic tail -> action completes
```

The concrete spike executor has a no-op successful domain commit so the choreography can be tested
directly. A real domain executor may subclass it and override `TryCommit()` to perform its irreversible
mutation; the base class retains timer, requester-release, advancement, completion and cancellation
plumbing.

## Spike limitations

- one active operation per component;
- one semantic commit boundary per operation;
- no branching, named markers, root-motion warping, prediction, rollback or late-join recovery;
- phase duration comes from the actor playback adapter;
- multiplayer transports phase start/stop presentation only.
