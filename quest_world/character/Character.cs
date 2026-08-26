using Godot;
using QuestWorld.Interaction.Runtime.Interactor;

public partial class Character : QuestWorld.Character.Character
{
    private InteractionInteractor _interactionInteractor = null!;
    private bool _wasPossessed;

    public InteractionInteractor InteractionInteractor => _interactionInteractor;

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
        foreach (StringName inputActionName in _interactionInteractor.GetRelevantInputs())
        {
            if (Input.IsActionJustPressed(inputActionName))
            {
                _interactionInteractor.TryStartInteractionInput(inputActionName);
            }
            else if (Input.IsActionJustReleased(inputActionName))
            {
                _interactionInteractor.TryEndInteractionInput(inputActionName);
            }
        }
    }

    private void ReleaseInteractionInputs()
    {
        foreach (StringName inputActionName in _interactionInteractor.GetRelevantInputs())
        {
            _interactionInteractor.TryEndInteractionInput(inputActionName);
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
