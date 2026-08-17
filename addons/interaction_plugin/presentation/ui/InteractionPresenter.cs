using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Presentation.UI;

[GlobalClass]
public partial class InteractionPresenter : CanvasLayer
{
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

    private InteractionInteractor? _interactor;
    private Camera3D? _camera;
    private Control? _prompt;
    private readonly Dictionary<InteractiveComponent, Control> _indications = new();
    private readonly HashSet<InteractiveComponent> _indicatedInteractives = new();

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

        Interactor.FocusedInteractiveChanged += OnFocusedInteractiveChanged;
        Interactor.InteractionStatusChanged += OnInteractionStatusChanged;
        Interactor.InteractiveIndicationAdded += OnInteractiveIndicationAdded;
        Interactor.InteractiveIndicationRemoved += OnInteractiveIndicationRemoved;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Interactor is null)
        {
            return;
        }

        Interactor.FocusedInteractiveChanged -= OnFocusedInteractiveChanged;
        Interactor.InteractionStatusChanged -= OnInteractionStatusChanged;
        Interactor.InteractiveIndicationAdded -= OnInteractiveIndicationAdded;
        Interactor.InteractiveIndicationRemoved -= OnInteractiveIndicationRemoved;
    }

    public override void _Process(double delta)
    {
        if (Interactor is null || !Interactor.IsLocallyControlled)
        {
            ClearPresentation();
            return;
        }

        RefreshIndications();
        UpdateProjection(_prompt);
        foreach (KeyValuePair<InteractiveComponent, Control> indication in _indications)
        {
            UpdateProjection(indication.Value, indication.Key);
        }
    }

    private void OnFocusedInteractiveChanged(Node interactive) => Refresh();

    private void OnInteractionStatusChanged(Node interactive, bool isAllowed, string reason) =>
        Refresh();

    private void OnInteractiveIndicationAdded(Node interactive)
    {
        if (interactive is InteractiveComponent component)
        {
            _indicatedInteractives.Add(component);
            RefreshIndications();
        }
    }

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
        if (focused is null)
        {
            FreeWidget(ref _prompt);
            RefreshIndications();
            return;
        }

        InteractionPresentation? presentation = Interactor.GetInteractionPresentation();
        if (presentation is null)
        {
            FreeWidget(ref _prompt);
            RefreshIndications();
            return;
        }

        if (focused.AutomaticInteraction)
        {
            FreeWidget(ref _prompt);
        }
        else
        {
            ReplaceWidget(ref _prompt, focused.PromptScene, presentation.Value);
        }

        RefreshIndications();
    }

    private void RefreshIndications()
    {
        if (Interactor is null || !Interactor.IsLocallyControlled)
        {
            ClearPresentation();
            return;
        }

        List<InteractiveComponent> removed = new();
        foreach (InteractiveComponent interactive in _indicatedInteractives)
        {
            if (!IsInstanceValid(interactive))
            {
                removed.Add(interactive);
                continue;
            }

            if (interactive == Interactor.FocusedInteractive)
            {
                RemoveIndication(interactive);
                continue;
            }

            InteractionPresentation presentation = interactive.GetPresentation(Interactor, false);
            PackedScene? scene = presentation.IsAllowed
                ? interactive.IndicationScene
                : interactive.BlockedIndicationScene;

            _indications.TryGetValue(interactive, out Control? widget);
            ReplaceWidget(ref widget, scene, presentation);
            if (widget is null)
            {
                _indications.Remove(interactive);
            }
            else
            {
                _indications[interactive] = widget;
            }
        }

        foreach (InteractiveComponent interactive in removed)
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

    private void ClearPresentation()
    {
        FreeWidget(ref _prompt);
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
        in InteractionPresentation presentation
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
        Node instance = scene.Instantiate();
        if (instance is not Control control)
        {
            instance.QueueFree();
            GD.PushError($"{scene.ResourcePath}: interaction widget scene root must be a Control.");
            return;
        }

        AddChild(control);
        current = control;
        (current as IInteractionWidget)?.Bind(presentation);
    }

    private void UpdateProjection(Control? control)
    {
        if (Interactor?.FocusedInteractive is null)
        {
            return;
        }

        UpdateProjection(control, Interactor.FocusedInteractive);
    }

    private void UpdateProjection(Control? control, InteractiveComponent interactive)
    {
        if (control is null || Camera is null)
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
