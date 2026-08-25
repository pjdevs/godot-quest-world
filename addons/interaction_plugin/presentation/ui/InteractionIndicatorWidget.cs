using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Default non-focused indication showing the name of a target that offers an action.</summary>
/// <remarks>
/// The indication is target-level by design: a single visual for the whole object. Whether it reads
/// as available comes from the indication scene chosen by the presenter, not from a reason string.
/// </remarks>
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
    public void Bind(in InteractionTargetPresentation presentation)
    {
        if (_label is not null)
        {
            _label.Text = presentation.DisplayName;
        }
    }
}
