using Godot;

public partial class Character : CharacterBody3D
{
	public enum ViewMode
	{
		ThirdPerson,
		FirstPerson
	}

	private const string LocomotionState = "Locomotion";
	private const string JumpState = "Jump";
	private const string FallState = "Fall";
	private const string BlendPositionPath = "parameters/Locomotion/blend_position";

	private static readonly string[] RequiredAnimations =
	{
		"Idle",
		"Jog_Fwd",
		"Jog_Bwd",
		"Jog_Left",
		"Jog_Right",
		"Sprint",
		"Jump_Start",
		"Jump"
	};

	[ExportGroup("View")]
	[Export]
	public ViewMode CurrentViewMode { get; set; } = ViewMode.ThirdPerson;

	[Export]
	public float MouseSensitivity { get; set; } = 0.002f;

	[Export]
	public float PitchMinDegrees { get; set; } = -70.0f;

	[Export]
	public float PitchMaxDegrees { get; set; } = 70.0f;

	[Export]
	public float ThirdPersonDistance { get; set; } = 4.0f;

	[Export]
	public Vector3 FirstPersonCameraOffset { get; set; } = new(0.0f, 0.0f, -0.2f);

	[Export]
	public float ThirdPersonForwardAlignmentThreshold { get; set; } = 0.75f;

	[ExportGroup("Camera Effects")]
	[Export]
	public bool CameraEffectsEnabled { get; set; } = true;

	[Export]
	public bool HeadBobEnabled { get; set; } = true;

	[Export]
	public float HeadBobWalkAmplitude { get; set; } = 0.025f;

	[Export]
	public float HeadBobSprintAmplitude { get; set; } = 0.045f;

	[Export]
	public float HeadBobFrequency { get; set; } = 8.0f;

	[Export]
	public float ThirdPersonCameraEffectsScale { get; set; } = 0.35f;

	[Export]
	public float CameraSwayStrengthDegrees { get; set; } = 1.0f;

	[Export]
	public float CameraSwaySmoothSpeed { get; set; } = 10.0f;

	[Export]
	public float DefaultFov { get; set; } = 75.0f;

	[Export]
	public float SprintFov { get; set; } = 82.0f;

	[Export]
	public float FovTransitionSpeed { get; set; } = 8.0f;

	[Export]
	public float JumpCameraOffset { get; set; } = 0.025f;

	[Export]
	public float LandingCameraOffset { get; set; } = 0.04f;

	[Export]
	public float JumpCameraPitchDegrees { get; set; } = 1.0f;

	[Export]
	public float LandingCameraPitchDegrees { get; set; } = -1.0f;

	[Export]
	public float CameraImpulseResponseSpeed { get; set; } = 14.0f;

	[Export]
	public float CameraImpulseRecoverySpeed { get; set; } = 7.0f;

	[ExportGroup("Movement")]
	[Export]
	public float WalkSpeed { get; set; } = 3.0f;

	[Export]
	public float RunSpeed { get; set; } = 6.0f;

	[Export]
	public float Acceleration { get; set; } = 15.0f;

	[Export]
	public float AirAcceleration { get; set; } = 5.0f;

	[Export]
	public float JumpVelocity { get; set; } = 5.0f;

	[Export]
	public float RotationSpeed { get; set; } = 10.0f;

	[ExportGroup("Input Actions")]
	[Export]
	public string MoveForwardAction { get; set; } = "move_forward";

	[Export]
	public string MoveBackwardAction { get; set; } = "move_backward";

	[Export]
	public string MoveLeftAction { get; set; } = "move_left";

	[Export]
	public string MoveRightAction { get; set; } = "move_right";

	[Export]
	public string JumpAction { get; set; } = "jump";

	[Export]
	public string SprintAction { get; set; } = "sprint";

	private Node3D _visual = null!;
	private Node3D _cameraYaw = null!;
	private Node3D _cameraPitch = null!;
	private SpringArm3D _springArm = null!;
	private Node3D _cameraAnchor = null!;
	private Node3D _cameraEffects = null!;
	private Camera3D _camera = null!;
	private AnimationTree _animationTree = null!;
	private AnimationNodeStateMachinePlayback _playback = null!;
	private Vector2 _mouseMotionAccumulator;
	private float _headBobTime;
	private Vector3 _headBobOffset;
	private Vector3 _cameraImpulseOffset;
	private Vector3 _cameraImpulseTargetOffset;
	private float _cameraImpulsePitch;
	private float _cameraImpulseTargetPitch;
	private float _cameraSwayRoll;
	private bool _wasOnFloor;
	private string _lastRequestedState = string.Empty;
	private bool _configurationValid;

	public override void _Ready()
	{
		_configurationValid = ResolveNodes() && ValidateInputActions() && ValidateAnimations();
		if (!_configurationValid)
		{
			SetPhysicsProcess(false);
			return;
		}

		_animationTree.Active = true;
		_playback = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		if (_playback == null)
		{
			GD.PushError($"{Name}: AnimationTree is missing the state-machine playback parameter.");
			_configurationValid = false;
			SetPhysicsProcess(false);
			return;
		}

		SetViewMode(CurrentViewMode);
		_camera.Fov = DefaultFov;
		_wasOnFloor = IsOnFloor();
		RequestAnimationState(LocomotionState);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (!_configurationValid)
		{
			return;
		}

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			return;
		}

		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		if (Input.MouseMode != Input.MouseModeEnum.Captured || @event is not InputEventMouseMotion motion)
		{
			return;
		}

		_cameraYaw.RotateY(-motion.Relative.X * MouseSensitivity);
		_cameraPitch.RotateX(-motion.Relative.Y * MouseSensitivity);
		_mouseMotionAccumulator += motion.Relative;
		Vector3 pitch = _cameraPitch.Rotation;
		pitch.X = Mathf.Clamp(pitch.X, Mathf.DegToRad(PitchMinDegrees), Mathf.DegToRad(PitchMaxDegrees));
		_cameraPitch.Rotation = pitch;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_configurationValid)
		{
			return;
		}

		float frameDelta = (float)delta;
		Vector2 inputVector = Input.GetVector(MoveLeftAction, MoveRightAction, MoveForwardAction, MoveBackwardAction);
		Vector3 moveDirection = GetCameraRelativeDirection(inputVector);
		bool sprinting = Input.IsActionPressed(SprintAction) && inputVector.LengthSquared() > 0.0001f;
		float targetSpeed = sprinting ? RunSpeed : WalkSpeed;
		Vector3 targetVelocity = moveDirection * targetSpeed;
		float acceleration = IsOnFloor() ? Acceleration : AirAcceleration;
		bool jumped = false;

		Vector3 velocity = Velocity;
		velocity.X = Mathf.MoveToward(velocity.X, targetVelocity.X, acceleration * frameDelta);
		velocity.Z = Mathf.MoveToward(velocity.Z, targetVelocity.Z, acceleration * frameDelta);

		if (IsOnFloor())
		{
			if (Input.IsActionJustPressed(JumpAction))
			{
				velocity.Y = JumpVelocity;
				jumped = true;
			}
			else if (velocity.Y < 0.0f)
			{
				velocity.Y = -0.1f;
			}
		}
		else
		{
			velocity += GetGravity() * frameDelta;
		}

		Velocity = velocity;
		MoveAndSlide();

		bool onFloor = IsOnFloor();
		if (jumped)
		{
			TriggerCameraImpulse(JumpCameraOffset, JumpCameraPitchDegrees);
		}
		else if (!_wasOnFloor && onFloor)
		{
			TriggerCameraImpulse(-LandingCameraOffset, LandingCameraPitchDegrees);
		}
		_wasOnFloor = onFloor;

		UpdateVisualOrientation(moveDirection, frameDelta);
		UpdateAnimationParameters();
		UpdateCameraEffects(frameDelta, inputVector, sprinting);
	}

	public void SetViewMode(ViewMode mode)
	{
		CurrentViewMode = mode;
		if (_springArm == null || _cameraEffects == null || _camera == null)
		{
			return;
		}

		_cameraEffects.Position = Vector3.Zero;
		_cameraEffects.Rotation = Vector3.Zero;

		if (mode == ViewMode.FirstPerson)
		{
			_springArm.SpringLength = 0.0f;
			_springArm.Position = FirstPersonCameraOffset;
			_camera.Position = Vector3.Zero;
		}
		else
		{
			_springArm.SpringLength = ThirdPersonDistance;
			_springArm.Position = Vector3.Zero;
			_camera.Position = Vector3.Zero;
		}
	}

	private bool ResolveNodes()
	{
		_visual = GetNodeOrNull<Node3D>("Visual")!;
		_cameraYaw = GetNodeOrNull<Node3D>("CameraYaw")!;
		_cameraPitch = GetNodeOrNull<Node3D>("CameraYaw/CameraPitch")!;
		_springArm = GetNodeOrNull<SpringArm3D>("CameraYaw/CameraPitch/SpringArm3D")!;
		_cameraAnchor = GetNodeOrNull<Node3D>("CameraYaw/CameraPitch/SpringArm3D/CameraAnchor")!;
		_cameraEffects = GetNodeOrNull<Node3D>("CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects")!;
		_camera = GetNodeOrNull<Camera3D>("CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects/Camera3D")!;
		_animationTree = GetNodeOrNull<AnimationTree>("AnimationTree")!;

		bool valid = true;
		valid &= RequireNode(_visual, "Visual");
		valid &= RequireNode(_cameraYaw, "CameraYaw");
		valid &= RequireNode(_cameraPitch, "CameraYaw/CameraPitch");
		valid &= RequireNode(_springArm, "CameraYaw/CameraPitch/SpringArm3D");
		valid &= RequireNode(_cameraAnchor, "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor");
		valid &= RequireNode(_cameraEffects, "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects");
		valid &= RequireNode(_camera, "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects/Camera3D");
		valid &= RequireNode(_animationTree, "AnimationTree");
		return valid;
	}

	private bool ValidateInputActions()
	{
		string[] actions =
		{
			MoveForwardAction,
			MoveBackwardAction,
			MoveLeftAction,
			MoveRightAction,
			JumpAction,
			SprintAction
		};

		bool valid = true;
		foreach (string action in actions)
		{
			if (string.IsNullOrWhiteSpace(action) || !InputMap.HasAction(action))
			{
				GD.PushError($"{Name}: required InputMap action '{action}' is missing or empty.");
				valid = false;
			}
		}

		return valid;
	}

	private bool ValidateAnimations()
	{
		AnimationPlayer animationPlayer = GetNodeOrNull<AnimationPlayer>("Visual/UALCharacter/AnimationPlayer");
		if (animationPlayer == null)
		{
			GD.PushError($"{Name}: expected UAL AnimationPlayer at 'Visual/UALCharacter/AnimationPlayer'.");
			return false;
		}

		bool valid = true;
		foreach (string animationName in RequiredAnimations)
		{
			if (!animationPlayer.HasAnimation(animationName))
			{
				GD.PushError($"{Name}: UAL AnimationPlayer is missing required animation '{animationName}'.");
				valid = false;
			}
		}

		return valid;
	}

	private static bool RequireNode(Node node, string path)
	{
		if (node != null)
		{
			return true;
		}

		GD.PushError($"Character scene is missing required node '{path}'.");
		return false;
	}

	private Vector3 GetCameraRelativeDirection(Vector2 inputVector)
	{
		Vector3 forward = -_cameraYaw.GlobalBasis.Z;
		forward.Y = 0.0f;
		forward = forward.Normalized();

		Vector3 right = _cameraYaw.GlobalBasis.X;
		right.Y = 0.0f;
		right = right.Normalized();

		Vector3 direction = right * inputVector.X + forward * -inputVector.Y;
		return direction.LengthSquared() > 1.0f ? direction.Normalized() : direction;
	}

	private void UpdateVisualOrientation(Vector3 moveDirection, float delta)
	{
		float targetYaw = _visual.Rotation.Y;
		if (CurrentViewMode == ViewMode.FirstPerson)
		{
			targetYaw = _cameraYaw.GlobalRotation.Y;
		}
		else if (moveDirection.LengthSquared() > 0.0001f)
		{
			Vector3 cameraForward = -_cameraYaw.GlobalBasis.Z;
			cameraForward.Y = 0.0f;
			cameraForward = cameraForward.Normalized();
			float forwardAlignment = moveDirection.Dot(cameraForward);
			targetYaw = forwardAlignment >= ThirdPersonForwardAlignmentThreshold
				? Mathf.Atan2(-moveDirection.X, -moveDirection.Z)
				: _cameraYaw.GlobalRotation.Y;
		}

		float turnWeight = 1.0f - Mathf.Exp(-Mathf.Max(RotationSpeed, 0.0f) * delta);
		Vector3 visualRotation = _visual.Rotation;
		visualRotation.Y = Mathf.LerpAngle(visualRotation.Y, targetYaw, turnWeight);
		_visual.Rotation = visualRotation;
	}

	private void UpdateCameraEffects(float delta, Vector2 inputVector, bool sprinting)
	{
		if (!CameraEffectsEnabled)
		{
			_headBobOffset = Vector3.Zero;
			_cameraImpulseOffset = Vector3.Zero;
			_cameraImpulseTargetOffset = Vector3.Zero;
			_cameraImpulsePitch = 0.0f;
			_cameraImpulseTargetPitch = 0.0f;
			_cameraSwayRoll = 0.0f;
			_cameraEffects.Position = Vector3.Zero;
			_cameraEffects.Rotation = Vector3.Zero;
			_camera.Fov = DefaultFov;
			_mouseMotionAccumulator = Vector2.Zero;
			return;
		}

		float effectScale = CurrentViewMode == ViewMode.FirstPerson ? 1.0f : ThirdPersonCameraEffectsScale;
		Vector3 realVelocity = GetRealVelocity();
		float horizontalSpeed = new Vector2(realVelocity.X, realVelocity.Z).Length();
		float speedReference = Mathf.Max(sprinting ? RunSpeed : WalkSpeed, 0.001f);
		float speedFactor = Mathf.Clamp(horizontalSpeed / speedReference, 0.0f, 1.0f);
		float smoothing = 1.0f - Mathf.Exp(-12.0f * delta);

		Vector3 targetHeadBob = Vector3.Zero;
		if (HeadBobEnabled && IsOnFloor() && inputVector.LengthSquared() > 0.0001f && horizontalSpeed > 0.1f)
		{
			float amplitude = (sprinting ? HeadBobSprintAmplitude : HeadBobWalkAmplitude) * effectScale * speedFactor;
			float frequency = HeadBobFrequency * (sprinting ? 1.15f : 1.0f);
			_headBobTime += delta * frequency * Mathf.Lerp(0.65f, 1.0f, speedFactor);
			targetHeadBob = new Vector3(
				Mathf.Sin(_headBobTime * 0.5f) * amplitude * 0.5f,
				Mathf.Sin(_headBobTime) * amplitude,
				0.0f);
		}

		_headBobOffset = _headBobOffset.Lerp(targetHeadBob, smoothing);
		float swayLimit = Mathf.DegToRad(Mathf.Max(CameraSwayStrengthDegrees, 0.0f)) * effectScale;
		float targetSway = Mathf.Clamp(-_mouseMotionAccumulator.X * MouseSensitivity, -swayLimit, swayLimit);
		float swayWeight = 1.0f - Mathf.Exp(-Mathf.Max(CameraSwaySmoothSpeed, 0.0f) * delta);
		_cameraSwayRoll = Mathf.Lerp(_cameraSwayRoll, targetSway, swayWeight);
		_mouseMotionAccumulator = Vector2.Zero;

		float impulseResponseWeight = 1.0f - Mathf.Exp(-Mathf.Max(CameraImpulseResponseSpeed, 0.0f) * delta);
		_cameraImpulseOffset = _cameraImpulseOffset.Lerp(_cameraImpulseTargetOffset, impulseResponseWeight);
		_cameraImpulsePitch = Mathf.Lerp(_cameraImpulsePitch, _cameraImpulseTargetPitch, impulseResponseWeight);
		float impulseRecoveryWeight = 1.0f - Mathf.Exp(-Mathf.Max(CameraImpulseRecoverySpeed, 0.0f) * delta);
		_cameraImpulseTargetOffset = _cameraImpulseTargetOffset.Lerp(Vector3.Zero, impulseRecoveryWeight);
		_cameraImpulseTargetPitch = Mathf.Lerp(_cameraImpulseTargetPitch, 0.0f, impulseRecoveryWeight);

		_cameraEffects.Position = _headBobOffset + _cameraImpulseOffset;
		_cameraEffects.Rotation = new Vector3(_cameraImpulsePitch, 0.0f, _cameraSwayRoll);

		float targetFov = sprinting ? SprintFov : DefaultFov;
		float fovWeight = 1.0f - Mathf.Exp(-Mathf.Max(FovTransitionSpeed, 0.0f) * delta);
		_camera.Fov = Mathf.Lerp(_camera.Fov, targetFov, fovWeight);
	}

	private void TriggerCameraImpulse(float verticalOffset, float pitchDegrees)
	{
		_cameraImpulseTargetOffset.Y += verticalOffset;
		_cameraImpulseTargetPitch += Mathf.DegToRad(pitchDegrees);
	}

	private void UpdateAnimationParameters()
	{
		Vector3 horizontalVelocity = new(Velocity.X, 0.0f, Velocity.Z);
		Vector3 localVelocity = _visual.GlobalBasis.Inverse() * horizontalVelocity;
		float speedRadius = Mathf.Max(RunSpeed, 0.001f);
		Vector2 blendPosition = new(localVelocity.X / speedRadius, -localVelocity.Z / speedRadius);
		blendPosition = blendPosition.LimitLength(1.0f);
		_animationTree.Set(BlendPositionPath, blendPosition);

		string state = IsOnFloor()
			? LocomotionState
			: Velocity.Y > 0.0f ? JumpState : FallState;
		RequestAnimationState(state);
	}

	private void RequestAnimationState(string state)
	{
		if (_lastRequestedState == state)
		{
			return;
		}

		_playback.Travel(state);
		_lastRequestedState = state;
	}
}
