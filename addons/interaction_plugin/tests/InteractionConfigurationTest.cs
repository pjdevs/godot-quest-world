namespace QuestWorld.Tests;

using System.Linq;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionConfigurationTest
{
    [TestCase]
    public void InteractiveComponentRequiresExplicitAreaAndHandler()
    {
        InteractiveComponent interactive = new();

        string[] warnings = interactive._GetConfigurationWarnings();

        AssertThat(warnings.Contains("InteractionArea must be assigned.")).IsTrue();
        AssertThat(warnings.Contains("InteractionOwner must be assigned.")).IsTrue();
    }

    [TestCase]
    public void InteractiveComponentRejectsAnOwnerWithoutInteractionHandler()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionOwner = new Node3D(),
        };

        string[] warnings = interactive._GetConfigurationWarnings();

        AssertThat(warnings.Contains("InteractionOwner must implement IInteractionHandler."))
            .IsTrue();
    }

    [TestCase]
    public void InteractiveComponentAllowsOptionalReferencesToRemainUnset()
    {
        InteractiveComponent interactive = new()
        {
            InteractionArea = new Area3D(),
            InteractionOwner = new TestInteractionOwner(),
        };

        string[] warnings = interactive._GetConfigurationWarnings();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void InteractorRequiresAnExplicitViewOriginOnly()
    {
        InteractionInteractor interactor = new();

        string[] warnings = interactor._GetConfigurationWarnings();

        AssertThat(warnings.Contains("ViewOrigin must be assigned.")).IsTrue();
        AssertThat(warnings.Any(warning => warning.Contains("InteractionOrigin"))).IsFalse();
    }

    [TestCase]
    public void StatefulRequiresAnExplicitInteractiveReference()
    {
        InteractionStateful stateful = new();

        string[] warnings = stateful._GetConfigurationWarnings();

        AssertThat(warnings.Contains("Interactive must be assigned.")).IsTrue();
    }

    [TestCase]
    public void PresenterRequiresExplicitInteractorAndCamera()
    {
        InteractionPresenter presenter = new();

        string[] warnings = presenter._GetConfigurationWarnings();

        AssertThat(warnings.Contains("Interactor must be assigned.")).IsTrue();
        AssertThat(warnings.Contains("Camera must be assigned.")).IsTrue();
    }

    [TestCase]
    public void MissingFocusIsRepresentedByAnAbsentPresentation()
    {
        InteractionInteractor interactor = new();

        InteractionPresentation? presentation = interactor.GetInteractionPresentation();

        AssertThat(presentation == null).IsTrue();
    }

    [TestCase]
    public void InteractorDoesNotExposeDetectionCollectionsOrConfigurationState()
    {
        AssertThat(typeof(InteractionInteractor).GetProperty("IndicatedInteractives") == null)
            .IsTrue();
        AssertThat(typeof(InteractionInteractor).GetProperty("InteractiveCandidates") == null)
            .IsTrue();
        AssertThat(typeof(InteractionInteractor).GetProperty("IsConfigurationValid") == null)
            .IsTrue();
    }

    [TestCase]
    public void StatefulDoesNotExposeItsActiveInteractorAsPublicApi()
    {
        AssertThat(
                typeof(InteractionStateful).GetProperty("ActiveInteractor")?.GetMethod?.IsPublic
                    == true
            )
            .IsFalse();
    }

    private sealed partial class TestInteractionOwner : Node3D, IInteractionHandler
    {
        public InteractionStatus EvaluateCustomInteractionStatus(in InteractionContext context) =>
            new InteractionAllowed();

        public void OnStartInteractionInput(in InteractionContext context) { }

        public void OnEndInteractionInput(in InteractionContext context) { }
    }
}
