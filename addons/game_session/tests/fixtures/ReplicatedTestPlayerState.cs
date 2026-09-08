using Godot;
using QuestWorld.GameSession;

namespace QuestWorld.Tests.GameSessionTests.Fixtures;

public partial class ReplicatedTestPlayerState : PlayerState
{
    [Export]
    public string SelectionId { get; set; } = "default";
}
