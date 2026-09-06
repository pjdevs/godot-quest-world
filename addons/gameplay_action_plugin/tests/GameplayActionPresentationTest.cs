namespace QuestWorld.Tests;

using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class GameplayActionPresentationTest
{
    [TestCase]
    public void PresentationDerivesAvailabilityAndActivationState()
    {
        GameplayActionPresentation allowedHold = new(
            new StringName("open"),
            "Open",
            "Open the door",
            new StringName("interact"),
            new GameplayActionAllowed(),
            GameplayActionActivationMode.Hold,
            0.5f,
            0.25f
        );
        GameplayActionPresentation blockedPress = new(
            new StringName("open"),
            "Open",
            "Open the door",
            new StringName("interact"),
            new GameplayActionBlocked("Locked"),
            GameplayActionActivationMode.Press
        );

        AssertThat(allowedHold.IsAllowed).IsTrue();
        AssertThat(allowedHold.IsAutomatic).IsFalse();
        AssertThat(allowedHold.IsHoldable).IsTrue();
        AssertThat(allowedHold.BlockReason).IsEqual(string.Empty);
        AssertThat(blockedPress.IsAllowed).IsFalse();
        AssertThat(blockedPress.IsAutomatic).IsFalse();
        AssertThat(blockedPress.IsHoldable).IsFalse();
        AssertThat(blockedPress.BlockReason).IsEqual("Locked");
    }

    [TestCase]
    public void AutomaticPresentationIsNotHoldable()
    {
        GameplayActionPresentation automatic = new(
            new StringName("refresh"),
            "Refresh",
            string.Empty,
            new StringName(),
            new GameplayActionHidden(),
            GameplayActionActivationMode.Automatic
        );

        AssertThat(automatic.IsAllowed).IsFalse();
        AssertThat(automatic.IsAutomatic).IsTrue();
        AssertThat(automatic.IsHoldable).IsFalse();
        AssertThat(automatic.BlockReason).IsEqual(string.Empty);
    }
}
