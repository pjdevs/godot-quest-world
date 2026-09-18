using Godot;

namespace AnimatedInteractionPlugin.Runtime;

/// <summary>Ordered actor-presentation choreography.</summary>
[GlobalClass]
public partial class AnimatedInteractionProfile : Resource
{
    [Export]
    public Godot.Collections.Array<AnimatedInteractionPhase> Phases { get; set; } = [];
}
