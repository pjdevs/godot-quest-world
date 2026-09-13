# GameplayAction presenter bindings use action identity

`GameplayActionPresenter` selects a binding when its resolved `GameplayAction` is also the binding's
`Source`. This is the ownership/presentation contract for locally authored action bindings; do not
replace it with an `InputGameplayAction` type check or a component comparison.

Integration bindings, such as focused Interaction offers, keep their integration node as `Source` so
they are reconciled and removed with that context and remain with the integration-specific presenter.
Tests that create owned presenter bindings must therefore pass the action occurrence as `Source`, while
tests for targeted bindings should use a distinct integration node.
