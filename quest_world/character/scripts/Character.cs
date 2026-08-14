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
	private Camera3D _camera = null!;
	private AnimationTree _animationTree = null!;
	private AnimationNodeStateMachinePlayback _playback = null!;
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
		float targetSpeed = Input.IsActionPressed(SprintAction) ? RunSpeed : WalkSpeed;
		Vector3 targetVelocity = moveDirection * targetSpeed;
		float acceleration = IsOnFloor() ? Acceleration : AirAcceleration;

		Vector3 velocity = Velocity;
		velocity.X = Mathf.MoveToward(velocity.X, targetVelocity.X, acceleration * frameDelta);
		velocity.Z = Mathf.MoveToward(velocity.Z, targetVelocity.Z, acceleration * frameDelta);

		if (IsOnFloor())
		{
			if (Input.IsActionJustPressed(JumpAction))
			{
				velocity.Y = JumpVelocity;
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

		UpdateVisualOrientation(moveDirection, frameDelta);
		UpdateAnimationParameters();
	}

	public void SetViewMode(ViewMode mode)
	{
		CurrentViewMode = mode;
		if (_springArm == null || _camera == null)
		{
			return;
		}

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
		_camera = GetNodeOrNull<Camera3D>("CameraYaw/CameraPitch/SpringArm3D/Camera3D")!;
		_animationTree = GetNodeOrNull<AnimationTree>("AnimationTree")!;

		bool valid = true;
		valid &= RequireNode(_visual, "Visual");
		valid &= RequireNode(_cameraYaw, "CameraYaw");
		valid &= RequireNode(_cameraPitch, "CameraYaw/CameraPitch");
		valid &= RequireNode(_springArm, "CameraYaw/CameraPitch/SpringArm3D");
		valid &= RequireNode(_camera, "CameraYaw/CameraPitch/SpringArm3D/Camera3D");
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
