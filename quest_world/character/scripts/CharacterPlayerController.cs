using Godot;

public partial class CharacterPlayerController : Node
{
	[ExportGroup("Possession")]
	[Export]
	public NodePath InitialPawnPath { get; set; } = new();

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

	private Character _controlledCharacter = null!;
	private Vector2 _lookAccumulator;
	private bool _configurationValid;

	public Character ControlledCharacter => _controlledCharacter;

	public override void _Ready()
	{
		ProcessPhysicsPriority = -100;
		_configurationValid = ValidateInputActions();
		if (!_configurationValid)
		{
			SetPhysicsProcess(false);
			return;
		}

		if (!InitialPawnPath.IsEmpty)
		{
			Character initialPawn = GetNodeOrNull<Character>(InitialPawnPath)!;
			if (initialPawn == null)
			{
				GD.PushError($"{Name}: initial pawn path '{InitialPawnPath}' does not resolve to a Character.");
			}
			else
			{
				Possess(initialPawn);
			}
		}
	}

	public override void _UnhandledInput(InputEvent inputEvent)
	{
		if (!_configurationValid || !IsInstanceValid(_controlledCharacter))
		{
			return;
		}

		if (
			inputEvent is InputEventKey keyEvent
			&& keyEvent.Pressed
			&& !keyEvent.Echo
		)
		{
			if (keyEvent.Keycode == Key.V)
			{
				_controlledCharacter.SetViewMode(
					_controlledCharacter.CurrentViewMode == Character.ViewMode.FirstPerson
						? Character.ViewMode.ThirdPerson
						: Character.ViewMode.FirstPerson
				);
			}
			else if (keyEvent.Keycode == Key.Escape)
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}
			return;
		}

		if (inputEvent is InputEventMouseButton mouseButton
			&& mouseButton.Pressed
			&& mouseButton.ButtonIndex == MouseButton.Left)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		if (Input.MouseMode == Input.MouseModeEnum.Captured && inputEvent is InputEventMouseMotion motion)
		{
			_lookAccumulator += motion.Relative;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_configurationValid || !IsInstanceValid(_controlledCharacter))
		{
			_lookAccumulator = Vector2.Zero;
			return;
		}

		Vector2 move = Input.GetVector(
			MoveLeftAction,
			MoveRightAction,
			MoveForwardAction,
			MoveBackwardAction);
		CharacterInputFrame frame = new(
			move,
			_lookAccumulator,
			Input.IsActionJustPressed(JumpAction),
			Input.IsActionPressed(SprintAction));
		_lookAccumulator = Vector2.Zero;
		_controlledCharacter.SubmitInputFrame(this, frame);
	}

	public void Possess(Character character)
	{
		if (!IsInstanceValid(character) || character == _controlledCharacter)
		{
			return;
		}

		CharacterPlayerController previousController = character.PossessingController;
		if (IsInstanceValid(previousController) && previousController != this)
		{
			previousController.Unpossess();
		}

		Unpossess();
		_controlledCharacter = character;
		_controlledCharacter.TakePossession(this);
		_lookAccumulator = Vector2.Zero;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public void Unpossess()
	{
		Character previousCharacter = _controlledCharacter;
		bool hadControlledCharacter = IsInstanceValid(previousCharacter);
		_controlledCharacter = null!;
		if (hadControlledCharacter)
		{
			previousCharacter.ReleasePossession(this);
		}

		_lookAccumulator = Vector2.Zero;
		if (hadControlledCharacter)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
	}

	public override void _ExitTree()
	{
		Unpossess();
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
}
