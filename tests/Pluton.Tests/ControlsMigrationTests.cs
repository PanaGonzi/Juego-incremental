using Pluton.Game;
using Xunit;

namespace Pluton.Tests;

public class ControlsMigrationTests
{
    [Fact]
    public void Old_defaults_saved_explicitly_free_the_new_keys()
    {
        var b = new Dictionary<string, long>
        {
            ["tech"] = ControlsMigration.KeyTab,
            ["cancel"] = ControlsMigration.KeyQ,
            ["interact_insert"] = ControlsMigration.KeyE,
        };

        ControlsMigration.Migrate(b, 0);

        Assert.Empty(b);
    }

    [Fact]
    public void Real_reassignments_survive()
    {
        var b = new Dictionary<string, long> { ["tech"] = 75, ["cancel"] = 90, ["rotate"] = ControlsMigration.KeyQ, ["tool_link"] = 49 };

        ControlsMigration.Migrate(b, 0);

        Assert.Equal(75, b["tech"]);
        Assert.Equal(90, b["cancel"]);
        Assert.Equal(ControlsMigration.KeyQ, b["rotate"]);
        Assert.Equal(49, b["tool_link"]);
    }

    [Fact]
    public void Migration_runs_only_once()
    {
        var b = new Dictionary<string, long> { ["tech"] = ControlsMigration.KeyTab };

        ControlsMigration.Migrate(b, ControlsMigration.Current);

        Assert.Equal(ControlsMigration.KeyTab, b["tech"]);
    }

    [Fact]
    public void Hotbar_normalizes_saved_data()
    {
        Assert.Equal(HotbarLayout.Default, HotbarLayout.Normalize(null));

        var fixedUp = HotbarLayout.Normalize(new[] { "build:smelter", "tool:link", "tool:link", "basura", "build:smelter" });

        Assert.Equal(HotbarLayout.Slots, fixedUp.Count);
        Assert.Equal(HotbarLayout.Cutter, fixedUp[0]);
        Assert.Equal("tool:link", fixedUp[1]);
        Assert.Equal("", fixedUp[2]);
        Assert.Equal("", fixedUp[3]);
        Assert.Equal("build:smelter", fixedUp[4]);
    }

    [Fact]
    public void Hotbar_assign_swaps_and_protects_the_tool()
    {
        var slots = HotbarLayout.Default.ToList();

        HotbarLayout.Assign(slots, 0, "build:smelter");
        Assert.Equal(HotbarLayout.Cutter, slots[0]);

        HotbarLayout.Assign(slots, 1, "build:smelter");
        Assert.Equal("build:smelter", slots[1]);
        Assert.Equal("tool:link", slots[6]);
        Assert.Equal(slots.Count, slots.Distinct().Count());

        HotbarLayout.Clear(slots, 0);
        Assert.Equal(HotbarLayout.Cutter, slots[0]);
    }

    [Fact]
    public void Wheel_skips_empty_slots_and_wraps()
    {
        var slots = new List<string> { HotbarLayout.Cutter, "", "tool:link", "", "", "", "", "", "" };

        Assert.Equal(2, HotbarLayout.Step(slots, 0, 1));
        Assert.Equal(0, HotbarLayout.Step(slots, 2, 1));
        Assert.Equal(2, HotbarLayout.Step(slots, 0, -1));
        Assert.Equal(0, HotbarLayout.Step(slots, -1, 1));
    }
}
