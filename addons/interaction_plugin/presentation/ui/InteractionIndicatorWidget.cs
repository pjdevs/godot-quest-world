using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Default non-focused indication showing the target name or blocked reason.</summary>
public partial class InteractionIndicatorWidget : PanelContainer, IInteractionWidget
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
    public void Bind(in InteractionPresentation presentation)
    {
        if (_label is not null)
        {
            _label.Text = presentation.IsAllowed
                ? presentation.DisplayName
                : presentation.BlockReason;
        }
    }
}
