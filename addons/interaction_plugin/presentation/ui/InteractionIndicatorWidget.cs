using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

public partial class InteractionIndicatorWidget : PanelContainer, IInteractionWidget
{
    private Label? _label;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        if (_label is null)
        {
            _label = new Label { Name = "Label" };
            AddChild(_label);
        }
    }

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
