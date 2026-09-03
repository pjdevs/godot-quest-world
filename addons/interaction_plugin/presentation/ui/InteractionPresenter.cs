using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>
/// Creates and projects prompt and indication widgets for one locally controlled interactor.
/// </summary>
/// <remarks>
/// This node is presentation-only. It clears itself for remote characters and never runs gameplay
/// decisions or authoritative state changes. The focused target is presented as one target-level
/// container holding one widget per presented action; indications stay one widget per target.
/// </remarks>
[GlobalClass]
public partial class InteractionPresenter : CanvasLayer
{
    /// <summary>Gets or sets the interactor whose local focus and indication signals are presented.</summary>
    [ExportGroup("Projection")]
    [Export]
    public InteractionInteractor? Interactor
    {
        get => _interactor;
        set
        {
            if (_interactor == value)
            {
                return;
            }

            _interactor = value;
        }
    }

    /// <summary>Gets or sets the local camera used to project world anchors onto the screen.</summary>
    [Export]
    public Camera3D? Camera
    {
        get => _camera;
        set
        {
            if (_camera == value)
            {
                return;
            }

            _camera = value;
        }
    }

    /// <summary>
    /// Gets or sets the optional target-level frame stacking the action prompts of the focused target.
    /// </summary>
    /// <remarks>
    /// The scene root should implement <see cref="IInteractionPromptContainer"/> to receive the target
    /// data and to expose where action widgets are added. Without a scene, a bare
    /// <see cref="VBoxContainer"/> stacks them instead.
    /// </remarks>
    [ExportGroup("Widgets")]
    [Export]
    public PackedScene? PromptContainerScene { get; set; }

    private InteractionInteractor? _interactor;
    private Camera3D? _camera;
    private Control? _prompt;
    private InteractiveComponent? _promptTarget;
    private string _promptContainerKey = string.Empty;
    private string _promptActionKey = string.Empty;
    private readonly List<Control> _promptActions = new();
    private readonly List<InteractionActionPresentation> _promptedActions = new();
    private readonly Dictionary<InteractiveComponent, Control> _indications = new();
    private readonly List<InteractiveComponent> _staleIndications = new();
    private readonly HashSet<InteractiveComponent> _indicatedInteractives = new();

    /// <summary>Godot callback that validates references, connects interactor signals, and refreshes UI.</summary>
    public override void _Ready()
    {
        if (Interactor is null)
        {
            GD.PushError($"{GetPath()}: InteractionPresenter requires an Interactor.");
        }

        if (Camera is null)
        {
            GD.PushError($"{GetPath()}: InteractionPresenter requires a Camera3D.");
        }

        if (Interactor is null || Camera is null)
        {
            SetProcess(false);
            return;
        }

        // Focus and status are pulled, not listened to: the frame loop rebinds them anyway because a
        // hold or an execution progress varies continuously, and a refresh on the signal too would run
        // the whole presentation twice on the frames where something actually changed. What stays
        // event-driven is the two indication signals, and only for what they alone carry — the set of
        // indicated targets, which is bookkeeping rather than a refresh.
        Interactor.InteractiveIndicationAdded += OnInteractiveIndicationAdded;
        Interactor.InteractiveIndicationRemoved += OnInteractiveIndicationRemoved;
        Refresh();
    }

    /// <summary>Godot callback that disconnects all interactor presentation signals.</summary>
    public override void _ExitTree()
    {
        if (Interactor is null)
        {
            return;
        }

        Interactor.InteractiveIndicationAdded -= OnInteractiveIndicationAdded;
        Interactor.InteractiveIndicationRemoved -= OnInteractiveIndicationRemoved;
    }

    /// <summary>Godot callback that rebinds every widget and projects it each frame.</summary>
    public override void _Process(double delta)
    {
        if (Interactor is null || !Interactor.IsLocallyControlled)
        {
            ClearPresentation();
            return;
        }

        // The prompt is rebound from the frame loop and never from a signal: a field that varies
        // continuously — a hold or an execution progress — would otherwise be fresh for the
        // indications and stale for the prompt, which is a field that works depending on the widget
        // reading it. Rebinding never re-instantiates a widget, so the cost is one snapshot per frame
        // for the focused target, when one is already paid for every indicated one.
        Refresh();
        UpdateProjection(_prompt, _promptTarget);
        foreach (KeyValuePair<InteractiveComponent, Control> indication in _indications)
        {
            UpdateProjection(indication.Value, indication.Key);
        }
    }

    private void OnInteractiveIndicationAdded(Node interactive)
    {
        if (interactive is InteractiveComponent component)
        {
            _indicatedInteractives.Add(component);
        }
    }

    // The widget is freed here rather than left to the next frame: the target is out of the set the
    // frame loop walks, so nothing would ever come back to it and it would stay on screen forever.
    private void OnInteractiveIndicationRemoved(Node interactive)
    {
        if (interactive is InteractiveComponent component)
        {
            _indicatedInteractives.Remove(component);
            RemoveIndication(component);
        }
    }

    private void Refresh()
    {
        if (Interactor is null || !Interactor.IsLocallyControlled)
        {
            ClearPresentation();
            return;
        }

        InteractiveComponent? focused = Interactor.FocusedInteractive;
        InteractionTargetPresentation? presentation = Interactor.GetInteractionPresentation();
        if (focused is null || presentation is null || !presentation.Value.HasPromptableAction)
        {
            ClearPrompt();
            RefreshIndications();
            return;
        }

        UpdatePrompt(presentation.Value);
        RefreshIndications();
    }

    private void UpdatePrompt(in InteractionTargetPresentation presentation)
    {
        string containerKey = PromptContainerScene?.ResourcePath ?? string.Empty;
        string actionKey = presentation.Interactive.ActionPromptScene?.ResourcePath ?? string.Empty;
        if (
            _prompt is null
            || _promptTarget != presentation.Interactive
            || _promptContainerKey != containerKey
            || _promptActionKey != actionKey
        )
        {
            ClearPrompt();
            _prompt = InstantiatePromptContainer();
            if (_prompt is null)
            {
                return;
            }

            AddChild(_prompt);
            _promptTarget = presentation.Interactive;
            _promptContainerKey = containerKey;
            _promptActionKey = actionKey;
        }

        (_prompt as IInteractionWidget)?.Bind(presentation);
        UpdatePromptActions(presentation);
    }

    private void UpdatePromptActions(in InteractionTargetPresentation presentation)
    {
        if (_prompt is null)
        {
            return;
        }

        CollectPromptedActions(presentation);
        PackedScene? scene = presentation.Interactive.ActionPromptScene;
        Control container = (_prompt as IInteractionPromptContainer)?.ActionsContainer ?? _prompt;
        int expectedCount = scene is null ? 0 : _promptedActions.Count;
        while (_promptActions.Count > expectedCount)
        {
            int lastIndex = _promptActions.Count - 1;
            _promptActions[lastIndex].QueueFree();
            _promptActions.RemoveAt(lastIndex);
        }

        while (_promptActions.Count < expectedCount)
        {
            Control? widget = InstantiateWidget(scene!);
            if (widget is null)
            {
                break;
            }

            container.AddChild(widget);
            _promptActions.Add(widget);
        }

        for (int index = 0; index < _promptActions.Count; index++)
        {
            InteractionActionPresentation action = _promptedActions[index];
            GameplayActionExecutionPresentation? execution =
                presentation.Interactive.TryGetExecutionPresentation(
                    action.ActionId,
                    out GameplayActionExecutionPresentation currentExecution
                )
                    ? currentExecution
                    : null;
            (_promptActions[index] as IInteractionActionWidget)?.Bind(action, execution);
        }
    }

    private void CollectPromptedActions(in InteractionTargetPresentation presentation)
    {
        _promptedActions.Clear();
        if (presentation.Actions is null)
        {
            return;
        }

        foreach (InteractionActionPresentation action in presentation.Actions)
        {
            if (!action.IsAutomatic)
            {
                _promptedActions.Add(action);
            }
        }
    }

    private Control? InstantiatePromptContainer()
    {
        return PromptContainerScene is null
            ? new VBoxContainer { Name = "InteractionPrompt" }
            : InstantiateWidget(PromptContainerScene);
    }

    private void RefreshIndications()
    {
        if (Interactor is null || !Interactor.IsLocallyControlled)
        {
            ClearPresentation();
            return;
        }

        // Reused rather than allocated: this runs every frame, including the frames where there is
        // nothing indicated at all.
        _staleIndications.Clear();
        foreach (InteractiveComponent interactive in _indicatedInteractives)
        {
            if (!IsInstanceValid(interactive))
            {
                _staleIndications.Add(interactive);
                continue;
            }

            if (interactive == Interactor.FocusedInteractive)
            {
                RemoveIndication(interactive);
                continue;
            }

            InteractionTargetPresentation presentation = interactive.GetPresentation(
                Interactor,
                false
            );
            if (presentation.Actions.Count == 0)
            {
                RemoveIndication(interactive);
                continue;
            }

            _indications.TryGetValue(interactive, out Control? widget);
            ReplaceWidget(ref widget, interactive.IndicationScene, presentation);
            if (widget is null)
            {
                _indications.Remove(interactive);
            }
            else
            {
                _indications[interactive] = widget;
            }
        }

        foreach (InteractiveComponent interactive in _staleIndications)
        {
            _indicatedInteractives.Remove(interactive);
            RemoveIndication(interactive);
        }
    }

    private void RemoveIndication(InteractiveComponent interactive)
    {
        if (!_indications.TryGetValue(interactive, out Control? widget))
        {
            return;
        }

        FreeWidget(ref widget);
        _indications.Remove(interactive);
    }

    private void ClearPrompt()
    {
        foreach (Control widget in _promptActions)
        {
            widget.QueueFree();
        }

        _promptActions.Clear();
        FreeWidget(ref _prompt);
        _promptTarget = null;
        _promptContainerKey = string.Empty;
        _promptActionKey = string.Empty;
    }

    private void ClearPresentation()
    {
        ClearPrompt();
        foreach (Control indication in _indications.Values)
        {
            Control? widget = indication;
            FreeWidget(ref widget);
        }

        _indications.Clear();
        _indicatedInteractives.Clear();
    }

    private void ReplaceWidget(
        ref Control? current,
        PackedScene? scene,
        in InteractionTargetPresentation presentation
    )
    {
        if (scene is null)
        {
            FreeWidget(ref current);
            return;
        }

        if (current is not null && current.SceneFilePath == scene.ResourcePath)
        {
            (current as IInteractionWidget)?.Bind(presentation);
            return;
        }

        FreeWidget(ref current);
        current = InstantiateWidget(scene);
        if (current is null)
        {
            return;
        }

        AddChild(current);
        (current as IInteractionWidget)?.Bind(presentation);
    }

    private static Control? InstantiateWidget(PackedScene scene)
    {
        Node instance = scene.Instantiate();
        if (instance is Control control)
        {
            return control;
        }

        instance.QueueFree();
        GD.PushError($"{scene.ResourcePath}: interaction widget scene root must be a Control.");
        return null;
    }

    private void UpdateProjection(Control? control, InteractiveComponent? interactive)
    {
        if (
            control is null
            || Camera is null
            || interactive is null
            || !IsInstanceValid(interactive)
        )
        {
            return;
        }

        Vector3 worldPosition = interactive.GetInteractionPosition();
        control.Visible = !Camera.IsPositionBehind(worldPosition);
        if (control.Visible)
        {
            control.Position = Camera.UnprojectPosition(worldPosition) - control.Size / 2.0f;
        }
    }

    private static void FreeWidget(ref Control? widget)
    {
        if (widget is null)
        {
            return;
        }

        widget.QueueFree();
        widget = null;
    }
}
