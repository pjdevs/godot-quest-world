# GdUnit shared-runtime shutdown crash after timed tests

## Symptom

Running the complete `GameplayActionExecutionTest` class can report every test as passed and then
terminate Godot with Windows exit code `-1073741819`. The same class still crashes when the newly
added typed-codec test is excluded.

## Isolation evidence

- the typed codec test passes alone with exit code zero;
- the basic execution group passes together with exit code zero;
- the replication/presentation group passes together with exit code zero;
- every timed test passes alone with exit code zero;
- the crash appears only when the wider class shares one Godot runtime and happens after its test
  results have already been reported.

This establishes a shared-runtime/teardown interaction but does not identify its exact engine or
adapter cause. Do not describe the class-wide run as green merely because all assertions were printed
as passing.

## Workflow

During focused TDD, run the affected groups or timed cases separately and require exit code zero.
Still run the mandatory full project gate once at completion. If that gate hits the same shutdown,
report the non-zero exit honestly alongside the isolated passing evidence instead of repeatedly
rerunning the slow suite.
