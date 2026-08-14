# Quest World

Prototyping project for gameplay system.

## Project Context

### Goal

Port personal custom gameplay system from Unreal (Interaction, Quest, Dialog, ...).

### Prereq

Building a drop in tempplate dummy character like Unreal to prototype fast.

## Environment

### Architecture

Vertical, one folder per feature with subfolder `scripts` etc.

### Tools

- Godot CLI available in PATH : `godot`
- `dotnet`

### Mandatory

- After each code task run `dotnet format`, `dotnet build` and `dotnet test`
- After each task maintain a doc per feature in `docs/feature/<thedoc>.md`
- After each workflow pitfall or hard user correction on the implementation (not tweaking a feature or corrections),
  document it in `docs/memory/<desc_of_things>.md`
- Before each task, check if docs or memory can be useful
