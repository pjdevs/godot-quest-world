using Godot;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Inventory;

public partial class Character : QuestWorld.Character.Character
{
    private InteractionInteractor _interactionInteractor = null!;
    private GameplayActionRunner _gameplayActionRunner = null!;
    private InventoryComponent _inventory = null!;
    private bool _wasPossessed;

    public InteractionInteractor InteractionInteractor => _interactionInteractor;

    public InventoryComponent Inventory => _inventory;

    public override void _Ready()
    {
        base._Ready();
        _interactionInteractor = GetNodeOrNull<InteractionInteractor>("InteractionInteractor")!;
        if (_interactionInteractor == null)
        {
            GD.PushError(
                $"{GetPath()}: project Character requires an InteractionInteractor child."
            );
            return;
        }

        _gameplayActionRunner = GetNodeOrNull<GameplayActionRunner>("GameplayActionRunner")!;
        if (_gameplayActionRunner == null)
        {
            GD.PushError($"{GetPath()}: project Character requires a GameplayActionRunner child.");
            return;
        }

        _inventory = GetNodeOrNull<InventoryComponent>("InventoryComponent")!;
        if (_inventory == null)
        {
            GD.PushError($"{GetPath()}: project Character requires an InventoryComponent child.");
            return;
        }

        _interactionInteractor.OwnerPeerId = OwnerPeerId;
        if (IsInsideTree())
        {
            _interactionInteractor.SetMultiplayerAuthority(_interactionInteractor.ServerPeerId);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        bool wasPossessed = _wasPossessed;
        base._PhysicsProcess(delta);

        if (!IsLocalNetworkAuthority || _interactionInteractor == null)
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
}
