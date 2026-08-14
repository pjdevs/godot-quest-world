# Godot 4.7 SkeletonModifier3D callback

- For custom C# `SkeletonModifier3D` nodes, override `_ProcessModificationWithDelta(double)`.
- `_ProcessModification()` is still available but obsolete in Godot 4.7 and produces warnings through the source generator.
- The modifier runs after `AnimationMixer`; apply the full additive pose and let `SkeletonModifier3D.Influence` handle blending.
