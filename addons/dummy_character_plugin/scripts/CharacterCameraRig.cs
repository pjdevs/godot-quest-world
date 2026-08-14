using Godot;

namespace QuestWorld.Character;

public partial class CharacterCameraRig : Node3D
{
    private float _mouseSensitivity = 0.002f;
    private float _pitchMinDegrees = -70.0f;
    private float _pitchMaxDegrees = 70.0f;
    private float _thirdPersonDistance = 4.0f;
    private Vector3 _firstPersonCameraOffset = new(0.0f, 0.0f, -0.2f);
    private Node3D _cameraPitch = null!;
    private SpringArm3D _springArm = null!;
    private Camera3D _camera = null!;
    private bool _configurationValid;

    public Node3D CameraPitch => _cameraPitch;

    public Camera3D Camera => _camera;

    internal void ConfigureView(
        Vector3 firstPersonCameraOffset,
        float mouseSensitivity,
        float pitchMinDegrees,
        float pitchMaxDegrees,
        float thirdPersonDistance)
    {
        _firstPersonCameraOffset = firstPersonCameraOffset;
        _mouseSensitivity = mouseSensitivity;
        _pitchMinDegrees = pitchMinDegrees;
        _pitchMaxDegrees = pitchMaxDegrees;
        _thirdPersonDistance = thirdPersonDistance;
    }

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

        float yawDelta = -lookDelta.X * _mouseSensitivity;
        RotateY(yawDelta);
        _cameraPitch.RotateX(-lookDelta.Y * _mouseSensitivity);
        Vector3 pitch = _cameraPitch.Rotation;
        pitch.X = Mathf.Clamp(
            pitch.X,
            Mathf.DegToRad(_pitchMinDegrees),
            Mathf.DegToRad(_pitchMaxDegrees));
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
            _springArm.Position = _firstPersonCameraOffset;
        }
        else
        {
            _springArm.SpringLength = _thirdPersonDistance;
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
        _camera = GetNodeOrNull<Camera3D>("CameraPitch/SpringArm3D/CameraAnchor/CameraEffects/Camera3D")!;

        bool valid = true;
        valid &= RequireNode(_cameraPitch, "CameraPitch");
        valid &= RequireNode(_springArm, "CameraPitch/SpringArm3D");
        valid &= RequireNode(_camera, "CameraPitch/SpringArm3D/CameraAnchor/CameraEffects/Camera3D");
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
