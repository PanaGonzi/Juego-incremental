using Pluton.Networking;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Transferencias parciales entre mochila y máquina: cantidad exacta, conservación, permisos y alcance.</summary>
public class TransferTests
{
    private static long Total(PlayerState[] players, Machine m, string item) =>
        players.Sum(p => p.Bag.Get(item)) + m.In.Get(item) + m.Out.Get(item);

    [Fact]
    public void Insert_moves_the_exact_amount_requested()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, crate);
        p.Bag.Add("scrap", 10);
        var before = crate.In.Get("scrap");

        Assert.True(w.Apply(p, new Command { Type = CommandType.Insert, A = crate.Id, Text = "scrap", Amount = 3 }).Ok);

        Assert.Equal(7, p.Bag.Get("scrap"));
        Assert.Equal(before + 3, crate.In.Get("scrap"));
        Assert.Contains(p.Key, w.DirtyPlayers);
        Assert.Contains(crate.Id, w.DirtyMachines);
    }

    [Fact]
    public void Extract_moves_the_exact_amount_requested()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, crate);
        crate.In.Add("ingot", 20);
        var before = crate.In.Get("ingot");

        Assert.True(w.Apply(p, new Command { Type = CommandType.Extract, A = crate.Id, Text = "ingot", Amount = 5 }).Ok);

        Assert.Equal(5, p.Bag.Get("ingot"));
        Assert.Equal(before - 5, crate.In.Get("ingot"));
    }

    [Fact]
    public void Without_amount_keeps_the_old_whole_stack_behaviour()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, crate);
        p.Bag.Add("scrap", 10);
        p.Bag.Add("ore", 4);

        Assert.True(w.Apply(p, new Command { Type = CommandType.Insert, A = crate.Id }).Ok);

        Assert.Equal(0, p.Bag.Kinds);
    }

    [Fact]
    public void Asking_for_more_than_exists_is_clamped_and_conserved()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, crate);
        p.Bag.Add("scrap", 4);
        var total = Total(new[] { p }, crate, "scrap");

        Assert.True(w.Apply(p, new Command { Type = CommandType.Insert, A = crate.Id, Text = "scrap", Amount = long.MaxValue }).Ok);
        Assert.Equal(total, Total(new[] { p }, crate, "scrap"));
        Assert.Equal(0, p.Bag.Get("scrap"));

        Assert.True(w.Apply(p, new Command { Type = CommandType.Extract, A = crate.Id, Text = "scrap", Amount = 9_000_000 }).Ok);
        Assert.Equal(total, Total(new[] { p }, crate, "scrap"));
        Assert.Equal(total, p.Bag.Get("scrap"));
    }

    [Fact]
    public void Extract_respects_the_personal_stack_limit()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, crate);
        var limit = w.Content.Balance.PersonalStackLimit;
        p.Bag.Add("ingot", limit - 2);
        crate.In.Add("ingot", 100);
        var total = Total(new[] { p }, crate, "ingot");

        Assert.True(w.Apply(p, new Command { Type = CommandType.Extract, A = crate.Id, Text = "ingot", Amount = 50 }).Ok);

        Assert.Equal(limit, p.Bag.Get("ingot"));
        Assert.Equal(total, Total(new[] { p }, crate, "ingot"));
        Assert.Equal("err_nothing", w.Apply(p, new Command { Type = CommandType.Extract, A = crate.Id, Text = "ingot", Amount = 1 }).Error);
    }

    [Fact]
    public void Insert_respects_machine_capacity()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, depot);
        var room = w.InputRoom(depot, "scrap");
        depot.In.Add("scrap", room - 3);
        p.Bag.Add("scrap", 10);

        Assert.True(w.Apply(p, new Command { Type = CommandType.Insert, A = depot.Id, Text = "scrap", Amount = 8 }).Ok);

        Assert.Equal(7, p.Bag.Get("scrap"));
        Assert.Equal(0, w.InputRoom(depot, "scrap"));
    }

    [Theory]
    [InlineData(-1, "scrap")]
    [InlineData(long.MinValue, "scrap")]
    [InlineData(5, "")]
    public void Invalid_amounts_are_rejected_without_changes(long amount, string item)
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, depot);
        p.Bag.Add("scrap", 10);
        depot.In.Add("scrap", 10);
        var inside = depot.In.Get("scrap");

        Assert.Equal("err_invalid", w.Apply(p, new Command { Type = CommandType.Insert, A = depot.Id, Text = item, Amount = amount }).Error);
        Assert.Equal("err_invalid", w.Apply(p, new Command { Type = CommandType.Extract, A = depot.Id, Text = item, Amount = amount }).Error);

        Assert.Equal(10, p.Bag.Get("scrap"));
        Assert.Equal(inside, depot.In.Get("scrap"));
    }

    [Fact]
    public void Visitors_cannot_transfer()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, depot);
        p.Role = Role.Visitor;
        p.Bag.Add("scrap", 10);
        depot.In.Add("ingot", 10);

        Assert.Equal("err_permission", w.Apply(p, new Command { Type = CommandType.Insert, A = depot.Id, Text = "scrap", Amount = 1 }).Error);
        Assert.Equal("err_permission", w.Apply(p, new Command { Type = CommandType.Extract, A = depot.Id, Text = "ingot", Amount = 1 }).Error);

        Assert.Equal(10, p.Bag.Get("scrap"));
        Assert.Equal(0, p.Bag.Get("ingot"));
    }

    [Fact]
    public void Out_of_reach_transfers_are_rejected()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        var (cx, cz) = w.MachineCenterMm(depot);
        p.X = (cx + w.Content.Balance.InteractRangeMm + 500) / 1000f;
        p.Z = cz / 1000f;
        p.Bag.Add("scrap", 10);
        depot.In.Add("ingot", 10);

        Assert.Equal("err_range", w.Apply(p, new Command { Type = CommandType.Insert, A = depot.Id, Text = "scrap", Amount = 1 }).Error);
        Assert.Equal("err_range", w.Apply(p, new Command { Type = CommandType.Extract, A = depot.Id, Text = "ingot", Amount = 1 }).Error);
        Assert.Equal(10, p.Bag.Get("scrap"));
    }

    [Fact]
    public void Two_players_competing_for_a_stack_cannot_duplicate_it()
    {
        var (w, a) = Fixture.NewWorld("player-a");
        var b = Fixture.AddPlayer(w, "player-b");
        var depot = Fixture.Find(w, "depot");
        Fixture.StandAt(w, a, depot);
        Fixture.StandAt(w, b, depot);
        var inStock = depot.In.Get("wafer");
        depot.In.Add("wafer", 10 - inStock);

        // Los dos ven 10 y piden 10 en el mismo tick: el segundo solo recibe lo que quede.
        var first = w.Apply(a, new Command { Type = CommandType.Extract, A = depot.Id, Text = "wafer", Amount = 7 });
        var second = w.Apply(b, new Command { Type = CommandType.Extract, A = depot.Id, Text = "wafer", Amount = 7 });
        var third = w.Apply(b, new Command { Type = CommandType.Extract, A = depot.Id, Text = "wafer", Amount = 7 });

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal("err_nothing", third.Error);
        Assert.Equal(7, a.Bag.Get("wafer"));
        Assert.Equal(3, b.Bag.Get("wafer"));
        Assert.Equal(0, depot.In.Get("wafer"));
    }

    [Fact]
    public void Transfers_over_the_real_transport_are_conserved_with_two_clients()
    {
        using var h = new NetHarness();
        var a = h.Join("transfer-client-aaaa");
        var b = h.Join("transfer-client-bbbb");
        h.Settle(200);
        var world = h.Server.World;
        var depot = Fixture.Find(world, "depot");
        var pa = world.Players["transfer-client-aaaa"];
        var pb = world.Players["transfer-client-bbbb"];
        Fixture.StandAt(world, pa, depot);
        Fixture.StandAt(world, pb, depot);
        depot.In.Add("wafer", 40 - depot.In.Get("wafer"));

        a.Send(new Command { Type = CommandType.Extract, A = depot.Id, Text = "wafer", Amount = 30 });
        b.Send(new Command { Type = CommandType.Extract, A = depot.Id, Text = "wafer", Amount = 30 });
        h.Pump(() => pa.Bag.Get("wafer") + pb.Bag.Get("wafer") == 40);
        h.Settle(200);

        Assert.Equal(40, pa.Bag.Get("wafer") + pb.Bag.Get("wafer"));
        Assert.Equal(0, depot.In.Get("wafer"));
        Assert.Equal(pa.Bag.Get("wafer"), a.LocalPlayer!.Bag.Get("wafer"));
        Assert.Equal(pb.Bag.Get("wafer"), b.LocalPlayer!.Bag.Get("wafer"));
    }

    [Fact]
    public void Action_state_is_replicated_to_other_players()
    {
        using var h = new NetHarness();
        var a = h.Join("action-client-aaaa");
        var b = h.Join("action-client-bbbb");
        h.Settle(100);
        var pa = h.Server.World.Players["action-client-aaaa"];
        byte seen = 0;
        var seenNum = -1;
        b.RemoteAction += (num, action) => { seenNum = num; seen = action; };

        a.SendMove(pa.X, 1, pa.Z, 0, PlayerAction.Pack(PlayerAction.Cutter, true));
        h.Pump(() => seen == PlayerAction.Pack(PlayerAction.Cutter, true));
        Assert.Equal(pa.Num, seenNum);

        // Un valor fuera de rango se normaliza: el servidor no reenvía bits que no significan nada.
        a.SendMove(pa.X, 1, pa.Z, 0, 0x7F);
        h.Pump(() => seen == PlayerAction.Device);
        Assert.False(PlayerAction.IsWorking(seen));
    }
}
