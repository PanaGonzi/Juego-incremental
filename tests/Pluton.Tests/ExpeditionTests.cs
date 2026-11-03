using Pluton.Networking;
using Pluton.Persistence;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Nueva expedición (B-7): opcional, solo del propietario, tras el contrato final y con ventajas acotadas.</summary>
public class ExpeditionTests
{
    private static (World w, PlayerState owner) Finished()
    {
        var w = Scenarios.Build(Fixture.Content, "small");
        var owner = Fixture.AddPlayer(w, "owner-key-0001");
        owner.Role = Role.Owner;
        w.OwnerKey = owner.Key;
        w.ContractsDone.Add(w.Content.Balance.Expedition.RequiresContract);
        w.Reindex();
        return (w, owner);
    }

    private static CommandResult Start(World w, PlayerState p, int expected) =>
        w.Apply(p, new Command { Type = CommandType.StartExpedition, A = expected });

    [Fact]
    public void Only_the_owner_after_the_final_contract_and_with_the_right_number_can_start()
    {
        var (w, owner) = Finished();
        var admin = Fixture.AddPlayer(w, "admin-key-0002");
        admin.Role = Role.Admin;
        var machines = w.Machines.Count;

        Assert.Equal("err_permission", Start(w, admin, 0).Error);
        Assert.Equal("err_expedition_confirm", Start(w, owner, 1).Error);
        w.ContractsDone.Remove(w.Content.Balance.Expedition.RequiresContract);
        Assert.Equal("err_expedition_locked", Start(w, owner, 0).Error);
        w.ContractsDone.Add(w.Content.Balance.Expedition.RequiresContract);
        admin.TransitTo = 1;
        Assert.Equal("err_expedition_transit", Start(w, owner, 0).Error);
        admin.TransitTo = -1;

        Assert.Equal(machines, w.Machines.Count);
        Assert.Equal(0, w.Expeditions);
        Assert.True(Start(w, owner, 0).Ok);
        // La misma orden repetida (otro clic, un reintento) ya no cuadra con el número y no reinicia otra vez.
        Assert.Equal("err_expedition_locked", Start(w, owner, 0).Error);
        Assert.Equal(1, w.Expeditions);
    }

    [Fact]
    public void Starting_resets_the_campaign_keeps_event_rewards_and_grants_the_bonus()
    {
        var (w, owner) = Finished();
        var guest = Fixture.AddPlayer(w, "guest-key-0003");
        guest.Bag.Add("parts", 10);
        guest.Planet = 1;
        w.Cosmetics.Add("spectral_train");
        w.ContractsDone.Add("c_shadow_express");
        var basePrice = w.Content.Items["parts"].Price;
        var fresh = World.CreateNew(Fixture.Content);
        var e = w.Content.Balance.Expedition;

        Assert.True(Start(w, owner, 0).Ok);

        Assert.Equal(fresh.Machines.Values.Select(m => (m.Def, m.Planet, m.X, m.Z)).OrderBy(x => x), w.Machines.Values.Select(m => (m.Def, m.Planet, m.X, m.Z)).OrderBy(x => x));
        Assert.Empty(w.Links);
        Assert.Empty(w.RailSegments);
        Assert.Empty(w.Trains);
        Assert.Empty(w.Upgrades);
        Assert.Equal(0, w.ObjectiveIndex);
        Assert.Equal(fresh.Credits + e.StartCreditsPerStack, w.Credits);
        Assert.Equal(fresh.Credits + 2 * e.StartCreditsPerStack, w.NextExpeditionCredits());
        Assert.DoesNotContain(e.RequiresContract, w.ContractsDone);
        Assert.Contains("c_shadow_express", w.ContractsDone);
        Assert.Contains("spectral_train", w.Cosmetics);
        Assert.Equal(basePrice * (1000 + e.Mods["sell_bonus"]) / 1000, w.SellPrice("parts"));
        Assert.All(w.Players.Values, p => Assert.Equal(0, p.Bag.Total));
        Assert.All(w.Players.Values, p => Assert.Equal(0, p.Planet));
        Assert.Equal(Role.Owner, owner.Role);
        // Los yacimientos vuelven a estar tapados y libres, como en un planeta nuevo.
        Assert.All(w.Nodes.Values, n => Assert.False(n.Revealed));
        Assert.All(w.Nodes.Values, n => Assert.Equal(-1, n.OccupiedBy));
        Assert.All(w.Nodes.Values, n => Assert.Contains(w.DebrisById.Values, d => d.CoversNode == n.Id));
        Assert.True(w.DebrisById.Count > 0);

        // La campaña vuelve a ser jugable: se puede cortar y construir desde el inicio.
        var signature = w.IndexSignature();
        w.Reindex();
        Assert.Equal(signature, w.IndexSignature());
        for (var i = 0; i < 50; i++) w.Step();
    }

    [Fact]
    public void Bonus_stops_at_the_cap()
    {
        var (w, owner) = Finished();
        var e = w.Content.Balance.Expedition;
        var fresh = World.CreateNew(Fixture.Content);
        for (var i = 0; i < e.MaxStacks + 2; i++)
        {
            w.ContractsDone.Add(e.RequiresContract);
            Assert.True(Start(w, owner, i).Ok);
        }
        Assert.Equal(e.MaxStacks + 2, w.Expeditions);
        Assert.Equal(e.MaxStacks, w.ExpeditionStacks);
        Assert.Equal(fresh.Credits + e.MaxStacks * e.StartCreditsPerStack, w.Credits);
        Assert.Equal(e.Mods["sell_bonus"] * e.MaxStacks, w.Mod("sell_bonus"));
    }

    [Fact]
    public void Real_v6_save_migrates_with_no_expeditions_and_can_start_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "pluton-expedition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore("test", root);
            var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "save-v6-dev-0.5.0-qa8-campaign.plsave"));
            Assert.Equal(6, BitConverter.ToInt32(original, 4));
            store.Write("real6", original);
            var w = store.Load("real6", Fixture.Content);
            Assert.Equal(original, File.ReadAllBytes(store.PathOf("real6") + ".v6.bak"));
            Assert.Equal(0, w.Expeditions);
            Assert.Contains("c_ninth_planet", w.ContractsDone);
            Assert.True(w.Machines.Count > 50);

            var owner = w.Players["owner-key-0001"];
            Assert.True(Start(w, owner, 0).Ok);
            store.Save("real6", w);
            Assert.Equal(WorldCodec.FormatVersion, BitConverter.ToInt32(File.ReadAllBytes(store.PathOf("real6")), 4));
            var again = store.Load("real6", Fixture.Content);
            Assert.Equal(1, again.Expeditions);
            Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(again));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Clients_follow_the_reset_without_divergence()
    {
        var (world, _) = Finished();
        world.Players.Clear();
        world.OwnerKey = "";
        using var h = new NetHarness(world: world);
        var owner = h.Join("client-identity-own1");
        var guest = h.Join("client-identity-gst2");
        Assert.Equal(Role.Owner, h.Server.World.Players["client-identity-own1"].Role);

        CommandResult? answer = null;
        owner.ResultReceived += (_, r) => answer = r;
        owner.Send(new Command { Type = CommandType.StartExpedition, A = 0 });
        h.Pump(() => answer != null);
        Assert.True(answer!.Value.Ok, answer.Value.Error);
        h.Pump(() => guest.World!.Expeditions == 1 && owner.World!.Expeditions == 1);
        h.Settle(400);

        foreach (var c in new[] { owner, guest })
        {
            Assert.Equal(WorldCodec.Hash(h.Server.World), WorldCodec.Hash(c.World!));
            Assert.Equal(h.Server.World.Machines.Keys, c.World!.Machines.Keys);
            Assert.Empty(c.World.Links);
            var signature = c.World.IndexSignature();
            c.World.Reindex();
            Assert.Equal(signature, c.World.IndexSignature());
        }
        h.Pump(() => owner.HashChecks >= 2 && guest.HashChecks >= 2, 15000);
        Assert.Equal(0, owner.HashMismatches);
        Assert.Equal(0, guest.HashMismatches);
    }
}
