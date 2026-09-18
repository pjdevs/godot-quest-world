# Animated Interaction

## Status

Animated Interaction is an experimental spike. Its current purpose is to validate that a small actor
presentation layer can remain separate from Gameplay Action lifecycle and domain commits. The original
probe and its reference cases remain in [spike.md](./spike.md).

## Runtime boundary

`addons/animated_interaction_plugin` owns reusable presentation choreography:

- `AnimatedInteractionProfile` contains an ordered array of phases;
- `AnimatedInteractionPhase` selects an animation, one-shot/loop playback, automatic/controlled
  transition and an optional commit offset;
- `AnimatedInteractionComponent` authoritatively sequences at most one operation and replicates local
  phase start/stop presentation through RPC;
- `AnimatedInteractionOperation` exposes `Advance`, `ReachCommit`, `Cancel` and the phase, commit and
  terminal notifications;
- `IAnimatedInteractionPlayback` is the generic actor-presentation adapter used by the component.

The addon does not reference Gameplay Action or Quest World domain types. In particular,
`IAnimatedInteractor` belongs to `quest_world/core`, and `QuestWorldCharacter` implements it by exposing
its child component. `CharacterAnimatedInteractionPlayback`, also in game core, adapts the demo
character's `CharacterAnimationController` to the generic playback contract.

`AnimatedInteractionExecutor` is the game-side, plug-and-play Gameplay Action adapter. It deliberately
lives in `quest_world/core` because resolving `IAnimatedInteractor` is game-specific. The executor owns
the wiring between one action execution and one animated operation: cancellation/failure cleanup,
completion after the presentation tail and requester release after commit.

## Lifecycle

`TryStart(profile)` succeeds only on authority, when no operation is active and every authored
animation can be resolved by the playback adapter. The first phase enters on the following process
frame so an executor can subscribe to operation events immediately after `TryStart` returns.

An automatic phase advances after the actor-reported clip duration. A controlled phase remains active
until gameplay calls `Advance()`. Loop phases are controlled and are retriggered at the clip duration.
An authored commit offset emits the operation's single `CommitReached` boundary; gameplay can emit the
same idempotent boundary with `ReachCommit()`.

Finishing or cancelling stops presentation and releases the component slot. Neither path completes,
cancels or fails a Gameplay Action: the owning executor performs the corresponding lifecycle call.

## Timed gameplay work

`TimedExecution` is now a composable clock. Expiry freezes published progress at `1.0` and emits
`Expired`; it no longer completes its Gameplay Action directly. Existing `TimedGameplayActionExecutor`
and `TimedTransitionStateGameplayActionExecutor` preserve their prior completion behavior by handling
that event themselves.

An animated domain executor may instead handle expiry by calling `operation.ReachCommit()`, perform its
domain mutation from `CommitReached`, advance the presentation tail, and complete the Gameplay Action
only when `Finished` fires.

The provided `AnimatedInteractionExecutor` packages that flow for the spike. With a positive
`WorkDuration`, its timer starts when the profile's single controlled phase is entered; expiry reaches
commit and advances that phase. With zero duration, profiles may be fully automatic or put an authored
commit point on their single controlled phase. The default `TryCommit()` is a successful no-op for rapid
visual testing; a domain-specific subclass overrides it for a real mutation.

## Current limitations

The spike has no prediction, late-join recovery, rollback, branching, arbitrary markers, simultaneous
channels or root-motion warping. Carry has not been migrated; it remains the presentation-driven
reference case used to evaluate whether this layer should become a durable framework API.
