using Godot;
using QuestWorld.Interaction;

public partial class Character : QuestWorld.Character.Character
{
    [ExportGroup("Interaction")]
    [Export]
    public StringName InteractionActionName { get; set; } = "interact";

    private InteractionInteractor _interactionInteractor = null!;
    private bool _wasPossessed;

    public InteractionInteractor InteractionInteractor => _interactionInteractor;

    public override void _Ready()
    {
        base._Ready();
        _interactionInteractor = GetNodeOrNull<InteractionInteractor>("InteractionInteractor")!;
        if (_interactionInteractor == null)
        {
            GD.PushError($"{GetPath()}: project Character requires an InteractionInteractor child.");
            return;
        }

        _interactionInteractor.OwnerPeerId = OwnerPeerId;
        if (IsInsideTree())
        {
            _interactionInteractor.SetMultiplayerAuthority(OwnerPeerId);
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
                _interactionInteractor.TryEndInteractionInput();
            }

            _wasPossessed = false;
            return;
        }

        _wasPossessed = true;
        if (InteractionActionName.IsEmpty)
        {
            return;
        }

        if (Input.IsActionJustPressed(InteractionActionName))
        {
            _interactionInteractor.TryStartInteractionInput();
        }
        else if (Input.IsActionJustReleased(InteractionActionName))
        {
            _interactionInteractor.TryEndInteractionInput();
        }
    }

    public override void _ExitTree()
    {
        if (_interactionInteractor != null
            && GodotObject.IsInstanceValid(_interactionInteractor)
            && _interactionInteractor.IsInsideTree())
        {
            _interactionInteractor.TryEndInteractionInput();
        }
    }
}
