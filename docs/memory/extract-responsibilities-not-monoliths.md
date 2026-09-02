# Extract responsibilities, not source monoliths

When extracting a generic system from a large integration component, preserving ownership does not mean
copying every owned algorithm into the same destination class. The public component may remain the lifecycle
owner while delegating focused internal responsibilities.

During Gameplay Action tranche 2, adding progress slots, extrapolation, stale-sample handling, and snapshot
serialization directly to `GameplayActionComponent` grew it from roughly 400 to more than 1,000 lines. That
recreated the shape the extraction was meant to dismantle. Moving those mechanics into an internal execution
presentation store kept the component as the public authority without mixing registry/dispatch with read-model
and transport algorithms.

Treat sharp class growth inside an early slice as an architecture signal. Stop, identify the extra
responsibility, and arbitrate a boundary before later slices build on the monolith.
