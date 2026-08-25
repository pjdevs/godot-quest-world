using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Default prompt of one action, showing its input when allowed or its reason when blocked.</summary>
public partial class InteractionActionPromptWidget : PanelContainer, IInteractionActionWidget
{
    private Label? _label;

    /// <summary>Godot callback that resolves or creates the child label used by the default widget.</summary>
    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        if (_label is null)
        {
            _label = new Label { Name = "Label" };
            AddChild(_label);
        }
    }

    /// <inheritdoc />
    public void Bind(in InteractionActionPresentation presentation)
    {
        if (_label is not null)
        {
            _label.Text = presentation.IsAllowed
                ? $"[{presentation.InputActionName}] {presentation.Label}"
                : $"{presentation.Label}: {presentation.BlockReason}";
        }
    }
}
