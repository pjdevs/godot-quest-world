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

    /// <inheritdoc />
    public void Bind(in InteractionTargetPresentation presentation)
    {
        if (_label is not null)
        {
            _label.Text = presentation.DisplayName;
        }
    }
}
