using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Runner;
using Godot;

namespace GameplayActionPlugin.Runtime.Access;

/// <summary>Read-only context used to validate runner access to one requested action.</summary>
/// <param name="Runner">Runner requesting or sustaining access.</param>
/// <param name="Component">Component owning the requested action.</param>
/// <param name="Action">Action whose domain-specific access is being checked.</param>
/// <param name="Target">Optional invocation target carried by the request.</param>
public readonly record struct GameplayActionAccessContext(
    GameplayActionRunner Runner,
    GameplayActionComponent Component,
    GameplayAction Action,
    Node? Target = null
);

/// <summary>Domain adapter used by a runner to validate request access to gameplay actions.</summary>
public interface IGameplayActionAccessProvider
{
    /// <summary>Returns whether the runner currently has access to request the action.</summary>
    bool CanRequest(in GameplayActionAccessContext context);
}
