using Godot;

public partial class InteractionPresenter : CanvasLayer
{
    [Export]
    public NodePath InteractorPath { get; set; } = new();

    [Export]
    public NodePath CameraPath { get; set; } = new();

    private InteractionInteractor _interactor = null!;
    private Camera3D _camera = null!;
    private Control _prompt = null!;
    private Control _indication = null!;

    public override void _Ready()
    {
        _interactor = ResolveNode<InteractionInteractor>(InteractorPath);
        _camera = ResolveNode<Camera3D>(CameraPath);
        if (_interactor == null || _camera == null)
        {
            GD.PushError($"{GetPath()}: InteractionPresenter requires an Interactor and a Camera3D.");
            SetProcess(false);
            return;
        }

        _interactor.FocusedInteractiveChanged += OnFocusedInteractiveChanged;
        _interactor.InteractionStatusChanged += OnInteractionStatusChanged;
        Refresh();
    }

    public override void _Process(double delta)
    {
        UpdateProjection(_prompt);
        UpdateProjection(_indication);
    }

    private void OnFocusedInteractiveChanged(Node interactive) => Refresh();

    private void OnInteractionStatusChanged(Node interactive, bool isAllowed, string reason) => Refresh();

    private void Refresh()
    {
        InteractiveComponent focused = _interactor?.FocusedInteractive!;
        if (focused == null)
        {
            FreeWidget(ref _prompt);
            FreeWidget(ref _indication);
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

        PackedScene indicationScene = presentation.IsAllowed
            ? focused.IndicationScene
            : focused.BlockedIndicationScene;
        ReplaceWidget(ref _indication, indicationScene, presentation);
    }

    private void ReplaceWidget(ref Control current, PackedScene scene, in InteractionPresentation presentation)
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

    private void UpdateProjection(Control control)
    {
        if (control == null || _interactor?.FocusedInteractive == null)
        {
            return;
        }

        Vector3 worldPosition = _interactor.FocusedInteractive.GetInteractionPosition();
        control.Visible = !_camera.IsPositionBehind(worldPosition);
        if (control.Visible)
        {
            control.Position = _camera.UnprojectPosition(worldPosition) - control.Size / 2.0f;
        }
    }

    private T ResolveNode<T>(NodePath path) where T : Node
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

        return GetParent()?.GetNodeOrNull<T>(typeof(T) == typeof(Camera3D) ? "Camera" : "Interactor")!;
    }

    private static void FreeWidget(ref Control widget)
    {
        if (widget != null)
        {
            widget.QueueFree();
            widget = null!;
        }
    }
}
