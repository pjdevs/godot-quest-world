using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Presentation.UI;

public partial class InteractionPresenter : CanvasLayer
{
    [ExportGroup("Projection")]
    [Export]
    public NodePath InteractorPath { get; set; } = new();

    [Export]
    public NodePath CameraPath { get; set; } = new();

    private InteractionInteractor? _interactor = null!;
    private Camera3D? _camera = null!;
    private Control? _prompt = null!;
    private readonly Dictionary<InteractiveComponent, Control> _indications = new();

    public override void _Ready()
    {
        _interactor = ResolveNode<InteractionInteractor>(InteractorPath);
        _camera = ResolveNode<Camera3D>(CameraPath);
        if (_interactor == null || _camera == null)
        {
            GD.PushError(
                $"{GetPath()}: InteractionPresenter requires an Interactor and a Camera3D (interactor path: '{InteractorPath}', camera path: '{CameraPath}')."
            );
            SetProcess(false);
            return;
        }

        _interactor.FocusedInteractiveChanged += OnFocusedInteractiveChanged;
        _interactor.InteractionStatusChanged += OnInteractionStatusChanged;
        Refresh();
    }

    public override void _Process(double delta)
    {
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

    private void Refresh()
    {
        if (_interactor == null)
        {
            return;
        }

        InteractiveComponent focused = _interactor.FocusedInteractive!;
        if (focused == null)
        {
            FreeWidget(ref _prompt);
            return;
        }

        InteractionPresentation presentation = _interactor.GetInteractionPresentation();
        if (focused.AutomaticInteraction)
        {
            FreeWidget(ref _prompt);
        }
        else
        {
            ReplaceWidget(ref _prompt, focused.PromptScene, presentation);
        }

        RefreshIndications();
    }

    private void RefreshIndications()
    {
        if (_interactor == null)
        {
            return;
        }

        HashSet<InteractiveComponent> indicated = new(_interactor.IndicatedInteractives);
        List<InteractiveComponent> removed = new();
        foreach (InteractiveComponent interactive in _indications.Keys)
        {
            if (!indicated.Contains(interactive) || !IsInstanceValid(interactive))
            {
                removed.Add(interactive);
            }
        }

        foreach (InteractiveComponent interactive in removed)
        {
            Control? widget = _indications[interactive];
            FreeWidget(ref widget);
            _indications.Remove(interactive);
        }

        foreach (InteractiveComponent interactive in indicated)
        {
            if (interactive == _interactor.FocusedInteractive)
            {
                RemoveIndication(interactive);
                continue;
            }

            InteractionPresentation presentation = interactive.GetPresentation(_interactor, false);
            PackedScene? scene = presentation.IsAllowed
                ? interactive.IndicationScene
                : interactive.BlockedIndicationScene;

            if (!_indications.TryGetValue(interactive, out Control? widget))
            {
                widget = null!;
            }

            ReplaceWidget(ref widget, scene, presentation);
            if (widget == null)
            {
                _indications.Remove(interactive);
            }
            else
            {
                _indications[interactive] = widget;
            }
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

    private void ReplaceWidget(
        ref Control? current,
        PackedScene? scene,
        in InteractionPresentation presentation
    )
    {
        if (scene == null)
        {
            FreeWidget(ref current);
            return;
        }

        if (current != null && current.SceneFilePath == scene.ResourcePath)
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
        if (_interactor?.FocusedInteractive == null)
        {
            return;
        }

        UpdateProjection(control, _interactor.FocusedInteractive);
    }

    private void UpdateProjection(Control? control, InteractiveComponent interactive)
    {
        if (control == null || _camera == null)
        {
            return;
        }

        Vector3 worldPosition = interactive.GetInteractionPosition();
        control.Visible = !_camera.IsPositionBehind(worldPosition);
        if (control.Visible)
        {
            control.Position = _camera.UnprojectPosition(worldPosition) - control.Size / 2.0f;
        }
    }

    private T ResolveNode<T>(NodePath path)
        where T : Node
    {
        if (path != null && !path.IsEmpty)
        {
            T direct = GetNodeOrNull<T>(path);
            if (direct != null)
            {
                return direct;
            }

            T fromParent = GetParent()?.GetNodeOrNull<T>(path)!;
            if (fromParent != null)
            {
                return fromParent;
            }
        }

        T sibling = GetParent()
            ?.GetNodeOrNull<T>(typeof(T) == typeof(Camera3D) ? "Camera" : "Interactor")!;
        return sibling ?? FindFirstNode<T>(GetParent())!;
    }

    private static void FreeWidget(ref Control? widget)
    {
        if (widget != null)
        {
            widget.QueueFree();
            widget = null!;
        }
    }

    private static T FindFirstNode<T>(Node root)
        where T : Node
    {
        if (root == null)
        {
            return null!;
        }

        if (root is T match)
        {
            return match;
        }

        foreach (Node child in root.GetChildren())
        {
            T nested = FindFirstNode<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null!;
    }
}
