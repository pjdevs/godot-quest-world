# Facility Slice — Cognitive Consequences

## Purpose

This note refines the ending of the first Facility playable slice.

The station systems are the player's mechanical vocabulary during the slice. They are **not** the vocabulary of the final diagnostic. The Prototype Lab reveal is that the infrastructure the player has been preserving, cutting and rerouting was supporting a recovered human cognitive instance.

The ending should therefore expose **human consequences**, not a technical scorecard.

## Core Rule

> The player manipulates station systems, but experiences the result as the condition of a person.

The final diagnostic must not explain the mapping back to Cooling, Security, Archives or Water. It should remain terse and clinical enough to establish that an instance exists and that something may be wrong.

For example:

```text
COGNITIVE PROCESS RECOVERY
INSTANCE RESPONSIVE
ANOMALIES DETECTED
```

The meaningful diagnosis happens immediately afterward through the recovered person's behavior, speech and ability to interact with the players.

The player should be able to infer *what their choices did* without the game saying `COOLING FAILURE -> SPEECH IMPAIRMENT`.

## Consequence Dimensions

These are design directions, not a request for four exact meters or four global state variables.

### Archives — autobiographical memory

Archives support retained memory and personal continuity.

A compromised instance can still perceive the present and hold a coherent conversation, but may no longer know who they are, who other people are, or what happened before recovery.

Possible expressions:

- uncertainty about their own identity;
- missing personal or procedural memories;
- recognition without context;
- a simple question such as `Who are you?` or `I don't remember anything before this.`

The important distinction is that the person can remain cognitively functional while their past has been damaged.

### Security — perception and spatial awareness

Security telemetry becomes part of the instance's effective sensory relationship with the facility.

A compromised instance may have incomplete, stale or absent awareness of the people and environment around it.

Possible expressions:

- inability to locate a player who is speaking;
- blind spots or missing sensory channels;
- reacting to an outdated position or event;
- uncertainty about whether anyone is present.

This should feel like impaired perception, not like a UI saying `SECURITY OFF`.

### Cooling — cognitive processing quality

Cooling affects the quality and speed of the active process itself.

A compromised instance may remain recognizably human while struggling to think or express itself cleanly.

Possible expressions:

- abnormal response latency;
- hesitation or word-finding difficulty;
- repeated syllables or interrupted sentences;
- slow reasoning and visible effort to answer a simple question.

Avoid turning this into a robotic glitch effect. The disturbing part is that a person notices that thinking has become difficult.

### Water — short-term continuity

Water-system disruption affects the stability of the running process rather than its stored past.

A compromised instance may retain identity and long-term memories but fail to preserve the immediate conversational thread reliably.

Possible expressions:

- losing the subject halfway through a sentence;
- repeating a recent question;
- forgetting that a player just answered;
- short discontinuities that resemble small resets rather than total amnesia.

This creates an important contrast with Archives: **lost past** and **unstable present** are different consequences.

## Combination Rule

Consequences should compose naturally, but the ending must not become a combinatorial dialogue matrix.

When several dimensions are compromised:

- select the one or two most legible symptoms for the current exchange;
- let other damage appear through timing, animation, later lines or follow-up interactions;
- do not enumerate every damaged subsystem;
- do not generate a moral grade or an optimal-route score.

A player who preserved memory but lost perception might meet someone coherent who knows exactly who they are but cannot tell where the players are.

A player who preserved perception but lost Archives might be seen clearly by someone who has no idea who they themselves are.

Cooling and Water together can make a conversation especially unstable without requiring a bespoke `Cooling + Water` ending.

## Persistence Rule

Restoring a station system does not necessarily erase consequences already inflicted on the cognitive instance.

At the same time, the game should **not** record a complete history of every switch transition. Persistent consequences exist only when the disruption became significant enough to leave a meaningful human effect.

The implementation therefore needs only coarse gameplay truth sufficient to answer questions such as:

- Is autobiographical memory meaningfully compromised?
- Is perception meaningfully compromised?
- Is active processing meaningfully compromised?
- Is short-term continuity meaningfully compromised?

The exact thresholds and representation should stay as simple as the slice allows. They are consequences of play, not simulation metrics to expose to the player.

Some world states remain repairable. Some consequences do not. The distinction should be justified by what happened physically and narratively rather than by an arbitrary rule that every action is either fully reversible or permanently latched.

## Diagnostic Contract

The diagnostic console exists to reveal that the target is a cognitive instance and to establish whether recovery is broadly normal or anomalous.

It must **not**:

- name sacrificed station circuits as causes;
- list a detailed technical history;
- reveal hidden consequence counters;
- tell the player which route was correct;
- replace the recovered person's behavior with exposition.

The recovered person is the primary feedback surface.

The strongest version of the reveal is retrospective: until entering the Lab, the player believes they have been deciding which parts of a failing facility deserve power and resources. The first conversation makes them understand that those choices were also deciding which parts of a person would still function when they arrived.