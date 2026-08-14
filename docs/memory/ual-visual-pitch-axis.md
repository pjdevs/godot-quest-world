# UAL visual pitch axis

- `Character.tscn` rotates `UALCharacter` by 180 degrees around Y.
- The UAL spine/head bones pitch around local X, so camera pitch must use the opposite sign (`PitchRotationSign = -1`) for both FPS and TPS.
- Bone names are `spine_01`, `spine_02`, `spine_03`, `neck_01`, and `Head`.
