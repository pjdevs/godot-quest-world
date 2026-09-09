# GdUnit suites must run sequentially

On this macOS Godot 4.7.2 Mono checkout, launching two `task test:suite` or `task test:*` commands
against the same project at once can make the GdUnit Godot process abort natively with exit code 134,
before any testcase is reported. The broad `FullyQualifiedName~Interaction` filter can also trigger the
same bootstrap abort even though an individual Interaction suite passes.

Run the smallest impacted suites one at a time. If the aggregate filter aborts, use the explicit suite
class names and report the harness limitation separately from test failures. The per-suite commands
still use the platform-specific Godot binary configured by `Taskfile.yml`.
