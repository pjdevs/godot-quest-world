# Interaction instigator resolution

`InteractionInteractor.ResolveForInstigator()` is intentionally the single resolution seam between a generic gameplay instigator and the Interaction-specific component it exposes.

V1 resolves the interactor by traversing the instigator descendants. This keeps the integration compatible with ordinary Godot scene composition and language-agnostic: the gameplay actor does not need to implement a C# interface or otherwise depend on Interaction-specific CLR contracts.

The traversal strategy is an implementation choice, not a long-term API contract. If it becomes a meaningful cost or stronger explicitness is needed, the resolver may later use an interface/provider fast path, explicit registration, caching, or another lookup strategy without changing rules, executors, or other consumers.

The intended scene invariant is that a gameplay instigator resolves to at most one `InteractionInteractor`. Multiple matching descendants should be considered an invalid or ambiguous setup rather than meaningful scene-tree ordering.
