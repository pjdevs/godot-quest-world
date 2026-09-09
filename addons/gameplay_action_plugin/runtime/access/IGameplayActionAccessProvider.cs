using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Runner;
using Godot;

namespace GameplayActionPlugin.Runtime.Access;

/// <summary>Read-only context used to validate runner access to one requested action.</summary>
/// <param name="Runner">Runner requesting or sustaining access.</param>
/// <param name="Component">Component owning the requested action.</param>
/// <param name="Action">Action whose domain-specific access is being checked.</param>
/// <param name="AccessSource">Object used by the access provider to justify the request.</param>
/// <param name="Target">Optional invocation target carried by the request.</param>
/// <param name="Sustained">Whether an already-running request is re-validating its access.</param>
public readonly record struct GameplayActionAccessContext(
    GameplayActionRunner Runner,
    GameplayActionComponent Component,
    GameplayAction Action,
    Node? AccessSource = null,
    Node? Target = null,
    bool Sustained = false
);

/// <summary>Authority-side access reservation attached to one requested execution.</summary>
/// <remarks>
/// The request pipeline calls <see cref="BindExecution"/> once the action owner has allocated its
/// execution identity. Implementations must make both methods idempotent so every rollback path can
/// safely release the lease.
/// </remarks>
public interface IGameplayActionRequestReservation
{
    /// <summary>Associates this reservation with the allocated action execution.</summary>
    void BindExecution(ulong executionId);

    /// <summary>Releases the reservation; repeated calls have no effect.</summary>
    void Release();
}

/// <summary>Domain adapter used by a runner to validate request access to gameplay actions.</summary>
public interface IGameplayActionAccessProvider
{
    /// <summary>Returns whether the runner currently has access to request the action.</summary>
    bool CanRequest(in GameplayActionAccessContext context);

    /// <summary>
    /// Tries to acquire an optional authority-side lease for the request after access validation.
    /// </summary>
    /// <remarks>
    /// Returning <see langword="true"/> with a null reservation means this access domain needs no
    /// transient claim. Returning <see langword="false"/> refuses the request.
    /// </remarks>
    bool TryAcquireRequestReservation(
        in GameplayActionAccessContext context,
        out IGameplayActionRequestReservation? reservation
    );
}
