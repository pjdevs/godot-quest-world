using Godot;

public interface IOriented
{
    public Transform3D VisualTransform { get; }
    public Vector3 ForwardVector { get; }
}
