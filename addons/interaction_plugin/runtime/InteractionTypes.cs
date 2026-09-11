using System.Collections.Generic;
using Godot;
using GameplayActionPlugin;
using InteractionPlugin.Runtime.Interactive;
using InteractionPlugin.Runtime.Offers;

namespace InteractionPlugin;

/// <summary>Tier one target reaches for one interactor, decided by its detector.</summary>
/// <remarks>
/// The tiers are cumulative: an interactible target is also indicated, because a widget saying "there
/// is something over there" must not disappear the moment the target becomes usable. A new tier is
/// only worth adding when the interactor itself behaves differently, which is why "close / medium /
/// far" is not one: that is visual, it belongs to the widget, and it is fed by data.
/// </remarks>
public enum InteractionDetectionKind
{
    /// <summary>The target is not detected at all and takes no part in presentation.</summary>
    None,

    /// <summary>The target is worth pointing at, but no action of it may be requested.</summary>
    Indicated,

    /// <summary>The target is eligible for focus, for a command, and for continued validation.</summary>
    Interactible
}

/// <summary>Snapshot consumed by local prompt and indication presentation.</summary>
/// <remarks>
/// Hidden actions are absent from <paramref name="Actions"/>; blocked ones stay present so a prompt
/// can explain them. A target offering no presentable action is neither focused nor indicated.
/// <para>
/// Only named physical quantities are exposed here, never the raw score of the detection layer: the
/// score of an aim detector is an angle and that of a proximity detector a ratio, so a widget reading
/// it would break the day the detector changes. <paramref name="Distance"/> means the same thing
/// everywhere.
/// </para>
/// </remarks>
/// <param name="Interactive">Interactive component represented by the snapshot.</param>
/// <param name="DisplayName">Name of the target shown to the player.</param>
/// <param name="Description">Optional descriptive text supplied by the interactive.</param>
/// <param name="Actions">Presentable actions, in target declaration order.</param>
/// <param name="IsFocused">Whether this interactive is the current focus target.</param>
/// <param name="Distance">
/// World units between the interactor's interaction origin and this target's anchor.
/// </param>
/// <param name="Offers">Offer aligned with each action for targeted execution presentation lookup.</param>
public readonly record struct InteractionTargetPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    IReadOnlyList<GameplayActionPresentation> Actions,
    bool IsFocused,
    float Distance = 0.0f,
    IReadOnlyList<InteractionOffer?>? Offers = null
)
{
    /// <summary>Gets whether at least one presented action can currently be requested.</summary>
    /// <remarks>
    /// Reserved for the target-level indication, which is a single visual for the whole object.
    /// Prompts must read the availability of each action instead of this aggregate.
    /// </remarks>
    public bool HasAllowedAction
    {
        get
        {
            if (Actions is null)
            {
                return false;
            }

            foreach (GameplayActionPresentation action in Actions)
            {
                if (action.Availability is GameplayActionAllowed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets whether at least one presented action is requested by player input.</summary>
    /// <remarks>
    /// Automatic actions stay in <see cref="Actions"/> so focus and indication keep seeing them, but
    /// a prompt showing an input the player cannot press would be misleading.
    /// </remarks>
    public bool HasPromptableAction
    {
        get
        {
            if (Actions is null)
            {
                return false;
            }

            foreach (GameplayActionPresentation action in Actions)
            {
                if (!action.IsAutomatic)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
