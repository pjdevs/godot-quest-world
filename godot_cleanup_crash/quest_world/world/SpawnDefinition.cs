using Godot;

[GlobalClass]
public partial class SpawnDefinition : Resource
{
    [Export]
    public StringName Id { get; set; } = new(string.Empty);
}
