using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class StageModelTests
{
    private static WindowSnapshot W(
        long id,
        string app,
        string monitor = "M1",
        bool minimized = false,
        int? processId = null) =>
        new(new WindowId(id), processId ?? (int)id, app, app, $"window {id}", null,
            new WindowBounds(0, 0, 800, 600), monitor,
            IsVisible: true, IsMinimized: minimized, IsCloaked: false, IsFullscreen: false);

    private static WindowKey K(long id, int? processId = null) =>
        new(new WindowId(id), processId ?? (int)id);

    private static WindowId[] FrontFirst(StageSet set) =>
        set.Members.Reverse().Select(key => key.Window).ToArray();

    private static WindowId[] StripWindows(MonitorState state) =>
        state.StripSets
            .SelectMany(set => set.Members.Reverse())
            .Select(key => key.Window)
            .ToArray();

    private static void AssertActive(StageModel model, string monitor, params long[] frontFirst) =>
        Assert.Equal(
            frontFirst,
            FrontFirst(model.Monitors[monitor].ActiveSet!).Select(id => id.Value).ToArray());

    private static void AssertStrip(StageModel model, string monitor, params long[] windows) =>
        Assert.Equal(
            windows,
            StripWindows(model.Monitors[monitor]).Select(id => id.Value).ToArray());

    private static StageSet Set(string id, params long[] ids)
    {
        var keys = ids.Select(id => K(id)).ToArray();
        return new StageSet(id, keys, keys[^1], new Dictionary<WindowKey, WindowGeometry>());
    }

    [Fact]
    public void Reset_WithoutForeground_SeedsSingletonStripSetsInInputOrder()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") });

        var state = model.Monitors["M1"];
        Assert.Null(state.ActiveSet);
        AssertStrip(model, "M1", 1, 2);
        Assert.All(state.StripSets, set => Assert.Single(set.Members));
        Assert.Equal(2, state.StripSets.Select(set => set.Id).Distinct().Count());
    }

    [Fact]
    public void Reset_WithForeground_StagesOnlyThatWindow()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2);
        Assert.Equal(K(1), model.Monitors["M1"].ActiveSet!.Foreground);
    }

    [Fact]
    public void Reset_AllAtOnce_StagesEveryAppWindowBackToFront()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "A", "M2"), W(4, "B") }, new WindowId(1));

        AssertActive(model, "M1", 1, 2);
        AssertStrip(model, "M1", 4);
        AssertStrip(model, "M2", 3);
    }

    [Fact]
    public void Reset_Automatic_BehavesLikeOneAtATime()
    {
        var model = new StageModel(WindowMode.Automatic);
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2);
    }

    [Fact]
    public void SetForeground_ActiveMember_MovesItToTheFront()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        Assert.True(model.SetForeground(new WindowId(2)));

        AssertActive(model, "M1", 2, 1);
        Assert.Equal(K(2), model.Monitors["M1"].ActiveSet!.Foreground);
        AssertStrip(model, "M1", 3);
    }

    [Fact]
    public void SetForeground_StripMember_IsPassiveNoOp()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        Assert.False(model.SetForeground(new WindowId(2)));

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2);
    }

    [Fact]
    public void SetForeground_UnknownWindow_ReturnsFalse()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A") });

        Assert.False(model.SetForeground(new WindowId(99)));
    }

    [Fact]
    public void Activate_ActiveMember_MovesItToTheFront()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));
        var setId = model.Monitors["M1"].ActiveSet!.Id;

        var result = model.Activate(new WindowId(2), ActivationSource.User);

        Assert.Equal(ActivationOutcome.Changed, result.Outcome);
        Assert.Equal(setId, result.SetId);
        AssertActive(model, "M1", 2, 1);
    }

    [Fact]
    public void Activate_StripMember_PromotesSetAndDemotesActive()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));
        var activeId = model.Monitors["M1"].ActiveSet!.Id;
        var promotedId = model.Monitors["M1"].StripSets[0].Id;

        var result = model.Activate(new WindowId(2), ActivationSource.User);

        Assert.Equal(ActivationOutcome.PromotedSet, result.Outcome);
        Assert.Equal(promotedId, result.SetId);
        Assert.Equal(activeId, result.DemotedSetId);
        AssertActive(model, "M1", 2);
        AssertStrip(model, "M1", 3, 1);
        Assert.Equal(promotedId, model.Monitors["M1"].ActiveSet!.Id);
    }

    [Fact]
    public void Activate_StripMember_WithRestoreAlsoPromotes()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        var result = model.Activate(new WindowId(2), ActivationSource.Restore);

        Assert.Equal(ActivationOutcome.PromotedSet, result.Outcome);
        AssertActive(model, "M1", 2);
        AssertStrip(model, "M1", 1);
    }

    [Fact]
    public void Activate_Passive_ChangesNothing()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        var result = model.Activate(new WindowId(2), ActivationSource.Passive);

        Assert.Equal(ActivationOutcome.Failure, result.Outcome);
        AssertActive(model, "M1", 1, 2);
        AssertStrip(model, "M1", 3);
    }

    [Fact]
    public void Activate_UserOnUnassigned_CreatesAllAtOnceSet()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(3));
        var active = model.Monitors["M1"].ActiveSet!;
        model.Restore(new MonitorState("M1", active, Array.Empty<StageSet>()));

        var result = model.Activate(new WindowId(1), ActivationSource.User);

        Assert.Equal(ActivationOutcome.CreatedSet, result.Outcome);
        Assert.NotNull(result.SetId);
        Assert.Equal(active.Id, result.DemotedSetId);
        AssertActive(model, "M1", 1, 2);
        Assert.Single(model.Monitors["M1"].StripSets);
        Assert.Equal(active.Id, model.Monitors["M1"].StripSets[0].Id);
        Assert.Equal(new[] { K(3) }, model.Monitors["M1"].StripSets[0].Members);
    }

    [Fact]
    public void Activate_UserOnUnassigned_OneAtATimeCreatesSingleton()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(3));
        var active = model.Monitors["M1"].ActiveSet!;
        model.Restore(new MonitorState("M1", active, Array.Empty<StageSet>()));

        var result = model.Activate(new WindowId(1), ActivationSource.User);

        Assert.Equal(ActivationOutcome.CreatedSet, result.Outcome);
        AssertActive(model, "M1", 1);
        Assert.DoesNotContain(K(2), model.Monitors["M1"].StripSets.SelectMany(set => set.Members));
    }

    [Fact]
    public void Activate_RestoreOnUnassigned_Fails()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
        var active = model.Monitors["M1"].ActiveSet!;
        model.Restore(new MonitorState("M1", active, Array.Empty<StageSet>()));

        var result = model.Activate(new WindowId(2), ActivationSource.Restore);

        Assert.Equal(ActivationOutcome.Failure, result.Outcome);
        AssertActive(model, "M1", 1);
        Assert.Empty(model.Monitors["M1"].StripSets);
    }

    [Fact]
    public void ShiftAdd_FromStrip_MovesToActiveSetAndDeletesEmptySet()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));

        var result = model.ShiftAdd(new WindowId(2));

        Assert.Equal(ActivationOutcome.Changed, result.Outcome);
        AssertActive(model, "M1", 2, 1);
        AssertStrip(model, "M1", 3);
    }

    [Fact]
    public void ShiftAdd_WithoutActiveSet_CreatesSingleton()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") });

        var result = model.ShiftAdd(new WindowId(1));

        Assert.Equal(ActivationOutcome.CreatedSet, result.Outcome);
        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2);
    }

    [Fact]
    public void ShiftAdd_ActiveMember_MovesToFront()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        var result = model.ShiftAdd(new WindowId(2));

        Assert.Equal(ActivationOutcome.Changed, result.Outcome);
        AssertActive(model, "M1", 2, 1);
    }

    [Fact]
    public void Minimize_ActiveSingleton_PromotesStripAndRemovesMember()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));
        model.CaptureGeometry(new WindowId(1), new WindowGeometry(
            new RectD(10, 20, 800, 600), "M1", 96, false, null));

        model.Minimize(new WindowId(1));

        AssertActive(model, "M1", 2);
        AssertStrip(model, "M1", 3);
        Assert.True(model.Windows[new WindowId(1)].IsMinimized);
        Assert.DoesNotContain(K(1), model.Monitors["M1"].StripSets.SelectMany(set => set.Members));
    }

    [Fact]
    public void Minimize_ActiveMember_KeepsSetAndForeground()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        model.Minimize(new WindowId(1));

        AssertActive(model, "M1", 2);
        Assert.Equal(K(2), model.Monitors["M1"].ActiveSet!.Foreground);
        AssertStrip(model, "M1", 3);
        Assert.DoesNotContain(K(1), model.Monitors["M1"].StripSets.SelectMany(set => set.Members));
    }

    [Fact]
    public void Minimize_StripMember_KeepsMembership()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Minimize(new WindowId(2));

        var state = model.Monitors["M1"];
        Assert.Single(state.StripSets);
        Assert.True(state.StripSets[0].Contains(K(2)));
        Assert.True(model.Windows[new WindowId(2)].IsMinimized);
    }

    [Fact]
    public void Remove_ActiveForeground_PromotesStripAndDiscardsGeometry()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
        model.CaptureGeometry(new WindowId(1), new WindowGeometry(
            new RectD(10, 20, 800, 600), "M1", 96, false, null));

        model.Remove(new WindowId(1));

        AssertActive(model, "M1", 2);
        Assert.Empty(model.Monitors["M1"].StripSets);
        Assert.False(model.Windows.ContainsKey(new WindowId(1)));
        Assert.DoesNotContain(
            model.Monitors.Values.SelectMany(state => state.StripSets),
            set => set.Geometry.ContainsKey(K(1)));
    }

    [Fact]
    public void Remove_NonForegroundActiveMember_UpdatesForeground()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        model.Remove(new WindowId(1));

        AssertActive(model, "M1", 2);
        Assert.Equal(K(2), model.Monitors["M1"].ActiveSet!.Foreground);
        AssertStrip(model, "M1", 3);
    }

    [Fact]
    public void Remove_LastWindow_RemovesMonitor()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A") }, new WindowId(1));

        model.Remove(new WindowId(1));

        Assert.Empty(model.Monitors);
        Assert.Empty(model.Windows);
    }

    [Fact]
    public void Update_MonitorChange_PromotesOldStripAndCreatesDestinationActive()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A", "M1"), W(2, "B", "M1"), W(3, "C", "M2") }, new WindowId(1));

        var change = model.Update(W(1, "A", "M2"));

        Assert.Equal(new MonitorChange(K(1), "M1", "M2"), change);
        AssertActive(model, "M1", 2);
        Assert.Empty(model.Monitors["M1"].StripSets);
        AssertActive(model, "M2", 1);
        Assert.Equal(K(1), model.Monitors["M2"].ActiveSet!.Foreground);
    }

    [Fact]
    public void Update_MonitorChange_AppendsToExistingDestinationActive()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A", "M1"), W(2, "B", "M2"), W(3, "C", "M2") }, new WindowId(1));
        model.Activate(new WindowId(2), ActivationSource.User);

        model.Update(W(1, "A", "M2"));

        Assert.False(model.Monitors.ContainsKey("M1"));
        var state = model.Monitors["M2"];
        Assert.Equal(K(2), state.ActiveSet!.Members[^1]);
        Assert.Equal(K(2), state.ActiveSet.Foreground);
    }

    [Fact]
    public void Update_ProcessIdChanged_ReplacesMembership()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Update(W(1, "A", processId: 999));

        AssertActive(model, "M1", 2);
        Assert.Single(model.Monitors["M1"].StripSets);
        Assert.Contains(K(1, 999), model.Monitors["M1"].StripSets[0].Members);
        Assert.DoesNotContain(K(1), model.Monitors["M1"].StripSets[0].Members);
        Assert.Equal(999, model.Windows[new WindowId(1)].ProcessId);
    }

    [Fact]
    public void Add_NewWindow_AppendsToStripEvenInAllAtOnce()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Add(W(3, "A"));

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2, 3);
    }

    [Fact]
    public void SwitchToSet_PromotesAndDemotes()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
        var activeId = model.Monitors["M1"].ActiveSet!.Id;
        var stripId = model.Monitors["M1"].StripSets[0].Id;

        var promoted = model.SwitchToSet(stripId);
        var same = model.SwitchToSet(stripId);
        var unknown = model.SwitchToSet("missing");

        Assert.Equal(ActivationOutcome.PromotedSet, promoted.Outcome);
        Assert.Equal(stripId, promoted.SetId);
        Assert.Equal(activeId, promoted.DemotedSetId);
        Assert.Equal(ActivationOutcome.Changed, same.Outcome);
        Assert.Equal(ActivationOutcome.Failure, unknown.Outcome);
        AssertActive(model, "M1", 2);
        AssertStrip(model, "M1", 1);
    }

    [Fact]
    public void ApplyWorkspace_SeedsActiveAndSingletonStrips()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C"), W(4, "D") }, new WindowId(1));

        model.ApplyWorkspace(
            "M1",
            new[] { new WindowId(3), new WindowId(1) },
            new[] { new WindowId(2), new WindowId(4) },
            new WindowId(3));

        AssertActive(model, "M1", 3, 1);
        AssertStrip(model, "M1", 2, 4);
        Assert.All(model.Monitors["M1"].StripSets, set => Assert.Single(set.Members));
        Assert.Equal(K(3), model.Monitors["M1"].ActiveSet!.Foreground);
    }

    [Fact]
    public void ApplyWorkspace_EmptyStageHasNoActiveSet()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.ApplyWorkspace(
            "M1",
            Array.Empty<WindowId>(),
            new[] { new WindowId(1), new WindowId(2) },
            null);

        Assert.Null(model.Monitors["M1"].ActiveSet);
        AssertStrip(model, "M1", 1, 2);
    }

    [Fact]
    public void ApplyWorkspace_AppendsUnlistedWindowsToStrip()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));

        model.ApplyWorkspace(
            "M1",
            new[] { new WindowId(1) },
            new[] { new WindowId(2) },
            new WindowId(1));

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2, 3);
    }

    [Fact]
    public void ApplyWorkspace_UsesFirstStageWindowWhenForegroundIsOutsideStage()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));

        model.ApplyWorkspace(
            "M1",
            new[] { new WindowId(2), new WindowId(1) },
            Array.Empty<WindowId>(),
            new WindowId(3));

        AssertActive(model, "M1", 2, 1);
        Assert.Equal(K(2), model.Monitors["M1"].ActiveSet!.Foreground);
    }

    [Fact]
    public void CycleOrder_ActiveThenStripFrontFirst()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));
        model.Activate(new WindowId(3), ActivationSource.User);

        var order = model.CycleOrder("M1");

        Assert.Equal(new[] { K(3), K(1), K(2) }.Select(key => key.Window), order);
        Assert.Empty(model.CycleOrder("M9"));
    }

    [Fact]
    public void CaptureGeometry_StoresInOwningSet()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A") }, new WindowId(1));
        var geometry = new WindowGeometry(new RectD(10, 20, 800, 600), "M1", 96, false, null);

        model.CaptureGeometry(new WindowId(1), geometry);
        model.CaptureGeometry(new WindowId(99), geometry);

        Assert.Equal(geometry, model.Monitors["M1"].ActiveSet!.Geometry[K(1)]);
        Assert.Single(model.Monitors["M1"].ActiveSet!.Geometry);
    }

    [Fact]
    public void SetMode_DoesNotRegroupExistingSets()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        model.SetMode(WindowMode.AllAtOnce);

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2);

        var all = new StageModel(WindowMode.AllAtOnce);
        all.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));
        all.SetMode(WindowMode.OneAtATime);

        AssertActive(all, "M1", 1, 2);
        Assert.Empty(all.Monitors["M1"].StripSets);
    }

    [Fact]
    public void Restore_MonitorState_ReplacesSets()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));
        var previous = model.Monitors["M1"];
        model.Activate(new WindowId(2), ActivationSource.User);

        model.Restore(previous);

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2, 3);
        Assert.Equal(previous.ActiveSet!.Id, model.Monitors["M1"].ActiveSet!.Id);
    }

    [Fact]
    public void Restore_MonitorState_DropsUnknownMembersAndEmptySets()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
        var previous = model.Monitors["M1"];

        model.Restore(new MonitorState(
            "M1",
            previous.ActiveSet,
            new[] { Set("ghost", 99), previous.StripSets[0] }));

        AssertActive(model, "M1", 1);
        AssertStrip(model, "M1", 2);
    }
}
