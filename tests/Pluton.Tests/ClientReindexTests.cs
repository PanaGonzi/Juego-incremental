using Pluton.Networking;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>
/// El cliente solo reindexa la parte del mundo que trae cada delta (H-4). Tras cada cambio real por red,
/// sus índices deben ser los mismos que con un reindexado completo, y construir no debe tocar vías ni cintas.
/// </summary>
public class ClientReindexTests
{
    private const string Key = "client-identity-reix";

    [Fact]
    public void Partial_reindex_on_the_client_matches_a_full_one()
    {
        var (world, p) = BeltTests.Open(Key);
        using var h = new NetHarness(world: world);
        var client = h.Join(Key);
        client.TrackChanges = true;
        var server = h.Server.World;

        CommandResult Run(Command c)
        {
            CommandResult? answer = null;
            void OnResult(uint _, CommandResult r) => answer = r;
            client.ResultReceived += OnResult;
            client.Send(c);
            h.Pump(() => answer != null);
            client.ResultReceived -= OnResult;
            h.Settle(250);
            return answer!.Value;
        }

        static void Ok(CommandResult r) => Assert.True(r.Ok, r.Error);

        void AssertIndexesMatch(string step)
        {
            var w = client.World!;
            var partial = w.IndexSignature();
            w.Reindex();
            Assert.True(partial == w.IndexSignature(), $"índices distintos tras: {step}");
        }

        var width = server.Content.Machines["storage_crate"].W;
        var belts = client.BeltStamp;
        var rail = client.RailStamp;
        var player = server.Players[Key];
        player.X = (width + 4) * 2f;
        player.Z = -80;
        Ok(Run(new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -40 }));
        Ok(Run(new Command { Type = CommandType.Place, Text = "storage_crate", A = width + 8, B = -40 }));
        AssertIndexesMatch("construir");
        Assert.Equal(belts, client.BeltStamp);
        Assert.Equal(rail, client.RailStamp);

        var a = server.Machines.Values.Single(m => m.Def == "storage_crate" && m.X == 0 && m.Z == -40);
        var b = server.Machines.Values.Single(m => m.Def == "storage_crate" && m.X == width + 8);
        Assert.Contains(a.Id, client.TouchedMachines);
        var route = Enumerable.Range(0, 8).Select(i => (width + i, -40)).ToList();
        var linked = Run(new Command { Type = CommandType.Link, A = a.Id, B = b.Id, Cells = route });
        Assert.True(linked.Ok, linked.Error);
        AssertIndexesMatch("tender cinta");
        Assert.NotEqual(belts, client.BeltStamp);
        Assert.Equal(rail, client.RailStamp);

        belts = client.BeltStamp;
        var moved = Run(new Command { Type = CommandType.Move, A = a.Id, B = 0, C = -46, D = 0 });
        Assert.True(moved.Ok, moved.Error);
        AssertIndexesMatch("mover");
        Assert.NotEqual(belts, client.BeltStamp);

        player.X = 8;
        player.Z = -60;
        var placed = Run(new Command { Type = CommandType.PlaceRail, A = 0, B = -30, C = 8, D = -30 });
        Assert.True(placed.Ok, placed.Error);
        AssertIndexesMatch("tender vía");
        Assert.NotEqual(rail, client.RailStamp);

        belts = client.BeltStamp;
        Ok(Run(new Command { Type = CommandType.Remove, A = b.Id }));
        AssertIndexesMatch("desmontar");
        Assert.NotEqual(belts, client.BeltStamp);
        Assert.Contains(b.Id, client.TouchedMachines);
        Assert.False(client.World!.Machines.ContainsKey(b.Id));
        Assert.Equal(0, client.HashMismatches);
    }

    [Fact]
    public void Headless_clients_do_not_collect_changes()
    {
        using var h = new NetHarness();
        var client = h.Join(Key);
        h.Settle(300);
        Assert.Empty(client.TouchedMachines);
        Assert.Empty(client.TouchedDebris);
    }
}
