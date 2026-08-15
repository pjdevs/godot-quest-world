using Godot;

namespace QuestWorld.Interaction;

public abstract partial class InteractionRule : Resource
{
    public abstract InteractionStatus Evaluate(in InteractionContext context);
}

public partial class AlwaysBlockedInteractionRule : InteractionRule
{
    [Export]
    public string Reason { get; set; } = "Interaction unavailable.";

    public override InteractionStatus Evaluate(in InteractionContext context) =>
        new InteractionBlocked(Reason);
}

public partial class InteractorGroupInteractionRule : InteractionRule
{
    [Export]
    public StringName? RequiredGroup { get; set; }

    [Export]
    public string MissingGroupReason { get; set; } = "You cannot use this yet.";

    public override InteractionStatus Evaluate(in InteractionContext context)
    {
        if (string.IsNullOrEmpty(RequiredGroup) || context.Interactor.IsInGroup(RequiredGroup))
        {
            return new InteractionAllowed();
        }

        return new InteractionBlocked(MissingGroupReason);
    }
}
