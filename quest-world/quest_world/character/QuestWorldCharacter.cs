using System.Linq;
using DummyCharacterPlugin;
using GameplayActionPlugin.Runtime.Runner;
using Godot;
using InteractionPlugin.Runtime.Interactor;
using InventoryPlugin;

public partial class QuestWorldCharacter : Character, IOriented, IInventoryOwner, ICarrier
{
    [ExportGroup("Carry")]
    [Export]
    public StringName TakeAction { get; set; } = "take";

    [Export]
    public StringName DropAction { get; set; } = "drop";

    [ExportGroup("Movement")]
    [Export]
    public Godot.Collections.Array<StringName> LockingActions { get; set; } = ["take", "drop"];

    private InteractionInteractor? _interactionInteractor = null;
    private GameplayActionRunner? _gameplayActionRunner = null;
    private InventoryComponent? _inventory = null;
    private CarryComponent? _carryComponent = null;
    private bool _wasPossessed;
    private bool _wasGrounded;

    public InventoryComponent Inventory => _inventory!;
    public CarryComponent? CarryComponent => _carryComponent;

    public new int OwnerPeerId
    {
        get => base.OwnerPeerId;
        set
        {
            base.OwnerPeerId = value;
            if (_gameplayActionRunner != null)
            {
                _gameplayActionRunner.OwnerPeerId = value;
            }
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        _interactionInteractor = GetNodeOrNull<InteractionInteractor>("InteractionInteractor");
        if (_interactionInteractor == null)
        {
            GD.PushError(
                $"{GetPath()}: project Character requires an InteractionInteractor child."
            );
            return;
        }

        _gameplayActionRunner = GetNodeOrNull<GameplayActionRunner>("GameplayActionRunner");
        if (_gameplayActionRunner == null)
        {
            GD.PushError($"{GetPath()}: project Character requires a GameplayActionRunner child.");
            return;
        }

        _inventory = GetNodeOrNull<InventoryComponent>("InventoryComponent");
        if (_inventory == null)
        {
            GD.PushError($"{GetPath()}: project Character requires an InventoryComponent child.");
            return;
        }

        _carryComponent = GetNodeOrNull<CarryComponent>("CarryComponent");
        if (_carryComponent == null)
        {
            GD.PushError($"{GetPath()}: project Character requires a CarryComponent child.");
            return;
        }
    }

    public override void _Ready()
    {
        base._Ready();

        _carryComponent?.Carrier = this;
        _carryComponent?.WorldSpawner = FindWorldSpawner();
        _carryComponent?.CarriedItemChanged += OnCarriedItemChanged;

        _gameplayActionRunner?.OwnerPeerId = OwnerPeerId;
    }

    public override void _PhysicsProcess(double delta)
    {
        bool wasPossessed = _wasPossessed;
        base._PhysicsProcess(delta);

        if (!IsLocalNetworkAuthority)
        {
            return;
        }

        if (!IsPossessed)
        {
            if (wasPossessed)
            {
                ReleaseInteractionInputs();
            }

            _wasPossessed = false;
            return;
        }

        _wasPossessed = true;

        if (_gameplayActionRunner is null || _interactionInteractor is null)
        {
            return;
        }

        if (_wasGrounded != NetworkIsGrounded)
        {
            _wasGrounded = NetworkIsGrounded;

            if (_carryComponent?.IsCarrying == true)
            {
                _gameplayActionRunner.InvalidateOwnedAction(DropAction);
            }
        }

        IsMovementLocked = LockingActions.Any(actionId =>
            _gameplayActionRunner?.IsOwnedActionExecuting(actionId) == true
        );

        // The focused target decides which inputs matter, so binding an action to another key in a
        // scene needs no change here. What the interactor reports is information, not a command:
        // arbitrating between interacting and anything else sharing a key stays this class's job.
        foreach (StringName inputActionName in _gameplayActionRunner.GetRelevantInputs())
        {
            if (Input.IsActionJustPressed(inputActionName))
            {
                _interactionInteractor.RefreshFocusedBindings();
                _gameplayActionRunner.TryStartActionInput(inputActionName);
            }
            else if (Input.IsActionJustReleased(inputActionName))
            {
                _gameplayActionRunner.TryEndActionInput(inputActionName);
            }
        }
    }

    private void ReleaseInteractionInputs()
    {
        if (_gameplayActionRunner is null)
        {
            return;
        }

        foreach (StringName inputActionName in _gameplayActionRunner.GetRelevantInputs())
        {
            _gameplayActionRunner.TryEndActionInput(inputActionName);
        }
    }

    public override void _ExitTree()
    {
        if (
            _interactionInteractor != null
            && IsInstanceValid(_interactionInteractor)
            && _interactionInteractor.IsInsideTree()
        )
        {
            ReleaseInteractionInputs();
        }
    }

    private IWorldSpawner? FindWorldSpawner()
    {
        Node? current = this;
        while (current is not null)
        {
            if (current is IWorldSpawner worldSpawner)
            {
                return worldSpawner;
            }

            current = current.GetParent();
        }

        return null;
    }

    private void OnCarriedItemChanged()
    {
        if (_gameplayActionRunner is not null)
        {
            _gameplayActionRunner.InvalidateOwnedAction(TakeAction);
            _gameplayActionRunner.InvalidateOwnedAction(DropAction);
        }
    }
}
