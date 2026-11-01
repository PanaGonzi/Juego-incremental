using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

public class PortIndicatorTests
{
    private static World NewWorld() => World.CreateNew(ContentDb.Load(ContentDb.LocateDirectory()));

    private static List<BeltPort> AllPorts(World w, string def, int rot = 0)
    {
        const int X = -40, Z = -40;
        return w.BeltPorts(0, def, X, Z, rot, true).Concat(w.BeltPorts(0, def, X, Z, rot, false)).ToList();
    }

    [Fact]
    public void Large_piece_shows_one_mark_per_face_and_direction()
    {
        var w = NewWorld();
        var ports = AllPorts(w, "cargo_pad");
        Assert.Equal(32, ports.Count); // el caso de INC-20
        var marks = PortIndicators.ForPlacement(ports);
        Assert.Equal(8, marks.Count);
        foreach (var dir in Enumerable.Range(0, 4))
        {
            Assert.Single(marks, m => m.Dir == dir && m.Output);
            Assert.Single(marks, m => m.Dir == dir && !m.Output);
        }
    }

    [Theory]
    [InlineData("smelter")]
    [InlineData("assembler")]
    [InlineData("storage_crate")]
    [InlineData("cargo_pad")]
    public void No_piece_exceeds_eight_marks_and_every_direction_stays_visible(string def)
    {
        var w = NewWorld();
        var ports = AllPorts(w, def);
        var marks = PortIndicators.ForPlacement(ports);
        Assert.InRange(marks.Count, 1, 8);
        Assert.Equal(ports.Select(p => (p.Dir, p.Output)).Distinct().Count(), marks.Select(p => (p.Dir, p.Output)).Distinct().Count());
        Assert.All(marks, m => Assert.Contains(m, ports));
    }

    [Fact]
    public void Marks_never_change_which_connectors_exist()
    {
        var w = NewWorld();
        var before = AllPorts(w, "cargo_pad");
        PortIndicators.ForPlacement(before);
        Assert.Equal(before, AllPorts(w, "cargo_pad"));
    }

    [Fact]
    public void Tool_always_includes_the_selected_connector_and_its_free_neighbours()
    {
        var w = NewWorld();
        var outputs = w.BeltPorts(0, "cargo_pad", -40, -40, 0, true).ToList();
        var chosen = outputs.First(p => p.Dir == 1);
        var marks = PortIndicators.ForTool(outputs, (chosen.X, chosen.Z));
        Assert.Contains(chosen, marks);
        Assert.Equal(4, marks.Count(m => m.Dir == 1));
        Assert.Equal(3, marks.Count(m => m.Dir != 1)); // una por cada otra cara
        Assert.True(marks.Count < outputs.Count);
    }

    [Fact]
    public void Tool_shows_every_connector_of_small_pieces()
    {
        var w = NewWorld();
        var outputs = w.BeltPorts(0, "smelter", -40, -40, 0, true).ToList();
        Assert.True(outputs.Count <= PortIndicators.ToolShowAllLimit);
        Assert.Equal(outputs, PortIndicators.ForTool(outputs, null));
    }

    [Fact]
    public void Occupied_connectors_are_only_marked_when_a_whole_face_is_occupied()
    {
        var ports = new[]
        {
            new BeltPort(0, 0, 0, true, false, false),
            new BeltPort(1, 0, 0, true, false, true),
            new BeltPort(2, 0, 0, true, false, false),
        };
        var free = PortIndicators.ForPlacement(ports);
        Assert.True(Assert.Single(free).Free);
        var full = PortIndicators.ForPlacement(ports.Select(p => p with { Free = false }));
        Assert.False(Assert.Single(full).Free);
    }

    [Fact]
    public void The_selection_is_deterministic()
    {
        var w = NewWorld();
        var ports = AllPorts(w, "cargo_pad");
        var reversed = Enumerable.Reverse(ports).ToList();
        Assert.Equal(PortIndicators.ForPlacement(ports).OrderBy(p => (p.Dir, p.Output)), PortIndicators.ForPlacement(reversed).OrderBy(p => (p.Dir, p.Output)));
    }
}
