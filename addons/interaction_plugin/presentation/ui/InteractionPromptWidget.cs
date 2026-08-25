using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Default target-level prompt frame showing the target name above its action prompts.</summary>
/// <remarks>
/// The scene may provide <c>Content/Label</c> and <c>Content/Actions</c>; anything missing is created
/// so the widget also works when instantiated directly from code.
/// </remarks>
public partial class InteractionPromptWidget : PanelContainer, IInteractionPromptContainer
{
    private Label? _label;
    private VBoxContainer? _actions;

    /// <inheritdoc />
    public Control ActionsContainer => (Control?)_actions ?? this;

    /// <summary>Godot callback that resolves or creates the name label and the action container.</summary>
    public override void _Ready()
    {
        VBoxContainer content =
            GetNodeOrNull<VBoxContainer>("Content") ?? AddContainer(this, "Content");
        _label = content.GetNodeOrNull<Label>("Label");
        if (_label is null)
        {
            _label = new Label { Name = "Label" };
            content.AddChild(_label);
        }

        _actions =
            content.GetNodeOrNull<VBoxContainer>("Actions") ?? AddContainer(content, "Actions");
    }

    /// <inheritdoc />
    public void Bind(in InteractionTargetPresentation presentation)
    {
        if (_label is not null)
        {
            _label.Text = presentation.DisplayName;
        }
    }

    private static VBoxContainer AddContainer(Node parent, string name)
    {
        VBoxContainer container = new() { Name = name };
        parent.AddChild(container);
        return container;
    }
}
