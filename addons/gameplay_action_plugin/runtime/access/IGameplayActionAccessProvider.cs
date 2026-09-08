using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Runner;

namespace GameplayActionPlugin.Runtime.Access;

/// <summary>Read-only context used to validate runner access to an externally owned action.</summary>
/// <param name="Runner">Runner requesting or sustaining access.</param>
/// <param name="Component">Component owning the external action.</param>
/// <param name="Action">Action whose domain-specific access is being checked.</param>
public readonly record struct GameplayActionAccessContext(
    GameplayActionRunner Runner,
    GameplayActionComponent Component,
    GameplayAction Action
);

/// <summary>Domain adapter used by a runner to validate access to externally owned actions.</summary>
public interface IGameplayActionAccessProvider
{
    /// <summary>Returns whether the runner currently has access to request the action.</summary>
    bool CanRequest(in GameplayActionAccessContext context);
}
