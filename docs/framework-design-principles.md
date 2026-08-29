# Framework design principles

QuestWorld's micro-frameworks are foundations, not feature collections.

The goal of each framework is not to implement every possible specialized use case. The goal is to expose a small, coherent set of primitives that can support the major archetypes of its domain without hacks, core rewrites, or closed extension paths.

## Design rule

For every important family of use cases, we should be able to answer:

- Which core primitives express it?
- Which specialized behavior belongs in an integration or higher layer?
- Where does authority/state live?
- How does cancellation/failure work?
- How does multiplayer or replication affect it?
- Can a game-specific implementation be built without bypassing the framework?

If the answer requires modifying the core for every new game pattern, the framework is not generic enough.

## What to do with hypothetical features

Explore them far enough to classify them:

1. **Already expressible cleanly** — do nothing.
2. **Needs a clean external integration** — validate the extension seam, but do not necessarily implement it yet.
3. **Needs a tiny generic hook** — consider adding that hook to the core.
4. **Needs to break or bypass a core invariant** — treat this as an architecture problem worth solving early.
5. **Needs a large specialized system** — keep it outside the micro-framework.

The objective is to **keep doors open, not build every room behind them**.

## Framework quality bar

A framework should remain:

- small enough to understand;
- explicit about ownership and authority;
- composable with the other QuestWorld frameworks;
- independent from presentation and game-specific policy when possible;
- extensible through stable seams rather than hidden conventions;
- testable against representative real-game archetypes;
- usable by custom game code without requiring privileged access to internals.

Real game prototypes should be used to pressure-test these assumptions. When a new archetype exposes a missing seam, fix the seam. When it only suggests a convenience layer, prefer an integration/helper over expanding the core.

## Practical review question

Before calling a micro-framework foundationally complete, pick the major archetypes of its domain and verify that each can be explained from core primitives plus optional integrations without resorting to hacks.

For Interaction, examples include instant actions, holds, pickups, animated contact, persistent sessions such as sitting/searching/pushing, contextual stations or dashboards, inventory/state dependencies, shared/co-op interactions, replicated execution, and fully custom gameplay executors.

Other frameworks should build their own equivalent archetype checklist as they mature.