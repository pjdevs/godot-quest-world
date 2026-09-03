using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Runtime.Interactive
{
    /// <summary>
    /// Temporary V1 migration bridge for Interaction scenes that have not yet authored an explicit
    /// GameplayActionComponent. Task 5 removes this once every scene owns the final topology.
    /// </summary>
    public partial class InteractiveComponent
    {
        public override void _Notification(int what)
        {
            base._Notification(what);
            if (what == NotificationPostEnterTree && ActionComponent is null)
            {
                GameplayActionComponent component = new() { Name = "GameplayActions" };
                ActionComponent = component;
                Callable.From(InstallMigrationActionComponent).CallDeferred();
            }
        }

        private void InstallMigrationActionComponent()
        {
            if (
                ActionComponent is not GameplayActionComponent component
                || component.GetParent() is not null
                || GetParent() is not Node parent
            )
            {
                return;
            }

            parent.AddChild(component);
            foreach (InteractionAction action in Actions)
            {
                if (action is null || action.Component is not null)
                {
                    continue;
                }

                PrepareAction(action);
                action.Reparent(component);
                component.AddAction(action);
            }

            foreach (InteractionInteractor interactor in _presentInteractors)
            {
                interactor.RefreshFocusedBindings(this);
            }
        }
    }
}

namespace QuestWorld.Interaction.Runtime.Interactor
{
    /// <summary>
    /// Temporary V1 migration bridge for Interaction scenes that have not yet authored an explicit
    /// GameplayActionRunner. The runner is still the normal Interaction request/execution pipeline;
    /// this bridge only supplies the missing node until Task 5 migrates scenes.
    /// </summary>
    public partial class InteractionInteractor
    {
        public override void _Notification(int what)
        {
            base._Notification(what);
            if (what == NotificationPostEnterTree && Runner is null)
            {
                GameplayActionRunner runner = new()
                {
                    Name = "GameplayActionRunner",
                    ServerPeerId = ServerPeerId,
                    OwnerPeerId = OwnerPeerId,
                    Instigator = this,
                };
                Runner = runner;
                Callable.From(InstallMigrationRunner).CallDeferred();
            }
        }

        private void InstallMigrationRunner()
        {
            if (Runner is not GameplayActionRunner runner || runner.GetParent() is not null)
            {
                return;
            }

            AddChild(runner);
        }
    }
}
