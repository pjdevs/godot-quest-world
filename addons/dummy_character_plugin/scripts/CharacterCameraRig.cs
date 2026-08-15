using Godot;

namespace QuestWorld.Character;

public partial class CharacterCameraRig : Node3D
{
    [ExportGroup("Look")]
    [Export]
    public float MouseSensitivity { get; set; } = 0.002f;

    [Export]
    public float PitchMinDegrees { get; set; } = -70.0f;

    [Export]
    public float PitchMaxDegrees { get; set; } = 70.0f;

    [ExportGroup("View")]
    [Export]
    public float ThirdPersonDistance { get; set; } = 4.0f;

    [Export]
    public Vector3 FirstPersonCameraOffset { get; set; } = new(0.0f, 0.0f, -0.2f);

    private Node3D _cameraPitch = null!;
    private SpringArm3D _springArm = null!;
    private Camera3D _camera = null!;
    private bool _configurationValid;

    public Node3D CameraPitch => _cameraPitch;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _configurationValid = ResolveNodes();
    }

    public float ApplyLook(CharacterInputFrame inputFrame)
    {
        Vector2 lookDelta = inputFrame.LookDelta;
        if (!_configurationValid || lookDelta == Vector2.Zero)
        {
            return 0.0f;
        }

        float yawDelta = -lookDelta.X * MouseSensitivity;
        RotateY(yawDelta);
        _cameraPitch.RotateX(-lookDelta.Y * MouseSensitivity);
        Vector3 pitch = _cameraPitch.Rotation;
        pitch.X = Mathf.Clamp(
            pitch.X,
            Mathf.DegToRad(PitchMinDegrees),
            Mathf.DegToRad(PitchMaxDegrees)
        );
        _cameraPitch.Rotation = pitch;
        return yawDelta;
    }

    public void SetViewMode(Character.ViewMode mode)
    {
        if (!_configurationValid)
        {
            return;
        }

        if (mode == Character.ViewMode.FirstPerson)
        {
            _springArm.SpringLength = 0.0f;
            _springArm.Position = FirstPersonCameraOffset;
        }
        else
        {
            _springArm.SpringLength = ThirdPersonDistance;
            _springArm.Position = Vector3.Zero;
        }

        _camera.Position = Vector3.Zero;
    }

    public void SetActive(bool active)
    {
        if (_camera != null)
        {
            _camera.Current = active;
        }
    }

    private bool ResolveNodes()
    {
        _cameraPitch = GetNodeOrNull<Node3D>("CameraPitch")!;
        _springArm = GetNodeOrNull<SpringArm3D>("CameraPitch/SpringArm3D")!;
        _camera = GetNodeOrNull<Camera3D>(
            "CameraPitch/SpringArm3D/CameraAnchor/CameraEffects/Camera3D"
        )!;

        bool valid = true;
        valid &= RequireNode(_cameraPitch, "CameraPitch");
        valid &= RequireNode(_springArm, "CameraPitch/SpringArm3D");
        valid &= RequireNode(
            _camera,
            "CameraPitch/SpringArm3D/CameraAnchor/CameraEffects/Camera3D"
        );
        return valid;
    }

    private bool RequireNode(Node node, string path)
    {
        if (node != null)
        {
            return true;
        }

        GD.PushError($"{Name}: camera rig is missing required node '{path}'.");
        return false;
    }
}
