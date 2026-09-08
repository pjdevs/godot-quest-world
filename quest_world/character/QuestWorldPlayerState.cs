using GameSessionPlugin;
using Godot;

namespace QuestWorld.Network;

[GlobalClass]
public partial class QuestWorldPlayerState : PlayerState
{
    [Export]
    public string AvatarId { get; set; } = "default";
}
