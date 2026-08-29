# Timed execution clock and contract

When a timed helper and remote presentation both derive a deadline, they must use the same clock
semantics. Mixing authoritative process delta with receiver monotonic time lets a disabled or paused
authority node freeze completion while remote progress keeps advancing.

For Interaction V4, `TimedExecution` and linear presentation extrapolation use monotonic real time.
Godot frame callbacks only provide opportunities to observe the deadline; node `CanProcess()` is not
part of the clock. If a future feature needs game-time timers, pause state must become synchronized
timing data rather than a local node decision.

Timed executors also require a positive finite duration. Open-ended execution is a separate generic
executor contract, not a magic `Duration == 0` mode. Composable helpers should return a discriminated
start result so callers never guess why setup failed.
