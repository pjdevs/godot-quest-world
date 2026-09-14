# Concurrency unavailability reasons

> **Status: planned.** Follow-up to native requester concurrency. This proposal keeps concurrency
> availability authorable without introducing dynamic formatting or a generic localization/message
> subsystem into the gameplay-action core.

## Motivation

Concurrency policies already decide whether an unavailable action is `Blocked` or `Hidden`, but the
blocked reason is currently hardcoded by framework/integration code.

For example, Interaction currently turns host occupancy into fixed messages such as `This is already
in use.` or `Someone else is using this.` even though the authored action already owns
`WhenExecutingBySelf` / `WhenExecutingByOther`.

The planned requester-concurrency axis would have the same problem if it only added
`WhenRequesterBusy`: games would still need custom rules solely to produce domain-specific text such
as:

- `Your hands are not free.`
- `You cannot talk to multiple people.`
- `You are already performing another action.`

Custom rules can already return arbitrary `GameplayActionBlocked(reason)` values, but using a rule only
to restate a native concurrency conflict duplicates framework state and defeats the purpose of making
concurrency a first-class primitive.

## Goals

- Let native host, requester and target concurrency produce authored static blocked reasons.
- Keep `Hidden | Blocked` policy separate from the blocked reason text.
- Make requester-concurrency reasons group-aware so actions in the same requester group do not repeat
  the same message.
- Preserve the current default behavior when no custom reason is authored.
- Keep V1 simple and static.

## Non-goals

- No runtime template interpolation such as `{player}`.
- No automatic use of `Node.Name`, player names or game-specific identity data.
- No structured/localized message system in V1.
- No generic registry for host or target concurrency groups.
- No new gameplay rules solely for presentation text.

## Existing availability model

`GameplayActionUnavailableKind` already separates the authored presentation policy from the produced
availability result:

```text
Hidden  -> GameplayActionHidden
Blocked -> GameplayActionBlocked(reason)
```

The existing `ToAvailability(kind, reason)` helper already accepts the reason separately, so this
proposal only changes where native concurrency obtains that reason.

## Host concurrency reasons

Host concurrency remains action-authored because `WhenExecutingBySelf` and `WhenExecutingByOther`
already live on `GameplayAction` and describe how that candidate action should be presented.

`GameplayAction` gains static reason fields paired with those policies, conceptually:

```text
WhenExecutingBySelf = Blocked
ExecutingBySelfReason = "This is already in use."

WhenExecutingByOther = Blocked
ExecutingByOtherReason = "Someone else is using this."
```

The defaults preserve the current visible behavior.

When the corresponding policy is `Hidden`, the reason is ignored.

This allows a specific action to expose domain wording without a custom rule, for example:

```text
WhenExecutingBySelf = Blocked
ExecutingBySelfReason = "You are already forcing this door."
```

## Target reservation reasons

Target reservation presentation remains offer-authored because `InteractionOffer` owns
`TargetConcurrencyGroup`, `WhenReservedBySelf` and `WhenReservedByOther`.

`InteractionOffer` gains static reason fields paired with those policies, conceptually:

```text
WhenReservedBySelf = Blocked
ReservedBySelfReason = "This target is already in use."

WhenReservedByOther = Blocked
ReservedByOtherReason = "Someone else is using this target."
```

The defaults preserve the current Interaction messages.

This proposal does not move target-group configuration into Gameplay Action: target reservations remain
an Interaction concern.

## Requester concurrency reasons

Requester concurrency is different from host and target concurrency because its group describes an
abstract capability/resource of one `GameplayActionRunner`, not one action host or one interaction
target.

Examples:

```text
requester group "hands"    -> "Your hands are not free."
requester group "dialogue" -> "You cannot talk to multiple people."
requester group "default"  -> "You are already performing another action."
```

Repeating the same reason on every action in `hands` or `dialogue` would make the group name only half
of the abstraction and would create duplicated authoring.

Therefore V1 stores requester-group reason configuration on `GameplayActionRunner`.

The exact Godot authoring shape may be a small Resource/entry collection, but its contract is simply:

```text
Requester concurrency group name -> static blocked reason
```

For example:

```text
GameplayActionRunner
  RequesterConcurrencyGroups:
    default  -> "You are already performing another action."
    hands    -> "Your hands are not free."
    dialogue -> "You cannot talk to multiple people."
```

`GameplayAction` continues to own only:

```text
RequesterConcurrencyGroup = "hands"
WhenRequesterBusy = Blocked
```

The candidate action decides whether requester occupancy is `Hidden` or `Blocked`; the runner's group
configuration provides the static reason when it is blocked.

### Missing group configuration

A non-empty requester group with no explicit runner entry must still work.

It falls back to a generic framework reason such as:

```text
You are already performing another action.
```

Missing presentation configuration must never disable requester concurrency or make the authoritative
request check depend on presentation data.

## Why requester reasons live on the runner

Requester concurrency itself is runner-local request arbitration. The runner is the object that sees
pending and active requests across multiple `GameplayActionComponent` hosts.

Putting requester-group wording on the runner keeps the ownership aligned:

- the action declares which requester capability it consumes;
- the action declares `Hidden | Blocked` when that capability is busy;
- the runner describes the meaning of its requester groups for presentation.

This also lets one game configure `hands` / `dialogue` semantics once per runner setup rather than once
per action occurrence.

## Static V1 boundary

V1 reasons are plain authored strings.

The framework does not attempt to resolve runtime values such as the blocking player, target name or
current execution into those strings.

In particular, V1 does not support:

```text
"{player} is already using this."
```

by parsing placeholders.

The gameplay-action core intentionally does not know how a game resolves a player-facing actor name,
and `Node.Name` is not a reliable substitute for game identity.

## Future dynamic reasons

A future need for messages such as:

```text
Pierre is already using this.
Alex is talking to this NPC.
The left lever is held by Sam.
```

should extend the availability/presentation boundary with structured blocker context rather than add a
string-template parser to the core.

Conceptually, a future blocked result could expose information such as:

```text
reason key / semantic kind
blocking instigator or requester
blocking action/execution
target/resource
```

A game-specific presenter/localization layer could then turn that context into player-facing text.

This is intentionally deferred until a concrete use case requires dynamic identity or localization.
The V1 static API should not prevent this evolution, but it should not implement it speculatively.

## Reason precedence

Each native concurrency axis supplies the reason for the conflict it actually detects:

- host conflict -> candidate action's host-concurrency reason;
- requester conflict -> runner configuration for the candidate action's requester group;
- target reservation conflict -> candidate offer's target-reservation reason.

Existing availability evaluation order remains responsible for deciding which conflict wins when more
than one condition is unavailable. This proposal does not introduce reason aggregation or concatenate
multiple failures.

## Implementation boundaries

Expected implementation remains small:

- `GameplayAction`: static reasons for `WhenExecutingBySelf` and `WhenExecutingByOther`;
- `InteractionOffer`: static reasons for `WhenReservedBySelf` and `WhenReservedByOther`;
- `GameplayActionRunner`: authored requester-group reason lookup with a generic fallback;
- requester-concurrency availability: use the runner group reason when `WhenRequesterBusy == Blocked`;
- existing `ToAvailability(kind, reason)` remains the conversion point;
- tests verify custom reasons, defaults, hidden behavior and missing requester-group configuration.

No concurrency ownership, reservation lifetime or authority semantics change as part of this proposal.

## Architecture decision

Concurrency state and concurrency presentation remain separate concerns, but native concurrency owns
its own default refusal wording instead of forcing games to recreate native conflicts as custom rules.

Host and target reasons stay local to the authored candidate that owns their existing availability
policy. Requester reasons are group-aware and configured on the runner because requester groups model
runner-local capabilities shared by actions across hosts.

Dynamic blocker-aware text remains a future structured-presentation concern, not a V1 string-template
feature.