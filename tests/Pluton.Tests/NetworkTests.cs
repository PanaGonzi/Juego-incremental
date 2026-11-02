using Pluton.Networking;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Sesiones reales por UDP en localhost, avanzadas de forma determinista desde el hilo de la prueba.</summary>
public sealed class NetHarness : IDisposable
{
    private static int _nextPort = 28100;
    public readonly int Port = Interlocked.Increment(ref _nextPort);
    public readonly GameServer Server;
    public readonly List<GameClient> Clients = new();

    public NetHarness(ServerOptions? options = null, World? world = null)
    {
        Server = new GameServer(world ?? World.CreateNew(Fixture.Content), LiteNetTransport.Listen(Port), options ?? new ServerOptions());
    }

    public GameClient Connect(string key, string name = "bot", string password = "")
    {
        var client = new GameClient(Fixture.Content, LiteNetTransport.Dial("127.0.0.1", Port), key, name, password);
        Clients.Add(client);
        return client;
    }

    public void Pump(Func<bool> until, int maxMs = 8000)
    {
        var limit = Environment.TickCount64 + maxMs;
        while (!until())
        {
            Assert.True(Environment.TickCount64 < limit, "Tiempo agotado esperando la condición");
            Step();
            Thread.Sleep(5);
        }
    }

    public void Step()
    {
        Server.Tick();
        foreach (var c in Clients) c.Poll();
    }

    public GameClient Join(string key)
    {
        var c = Connect(key);
        Pump(() => c.State == ClientState.InGame);
        return c;
    }

    /// <summary>Espera a que un cliente replique el estado actual del servidor.</summary>
    public void Settle(int ms = 400)
    {
        var limit = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < limit)
        {
            Step();
            Thread.Sleep(5);
        }
    }

    public void Dispose()
    {
        foreach (var c in Clients) c.Dispose();
        Server.Dispose();
    }
}

public class NetworkTests
{
    private const string KeyA = "client-identity-aaaa";
    private const string KeyB = "client-identity-bbbb";

    [Fact]
    public void Two_clients_share_a_server_validated_action()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        var b = h.Join(KeyB);
        var creditsBefore = h.Server.World.Credits;

        var debris = h.Server.World.DebrisById.Values.First(d => d.Def == "scrap_pile");
        var serverPlayer = h.Server.World.Players[KeyA];
        serverPlayer.X = debris.X * 2f;
        serverPlayer.Z = debris.Z * 2f;

        var hits = 0;
        h.Pump(() =>
        {
            if (!h.Server.World.DebrisById.ContainsKey(debris.Id)) return true;
            if (a.PendingCount == 0) { a.Send(new Command { Type = CommandType.CutDebris, A = debris.Id }); hits++; }
            return false;
        });
        h.Settle();

        Assert.False(b.World!.DebrisById.ContainsKey(debris.Id));
        Assert.Equal(h.Server.World.Credits, b.World.Credits);
        Assert.True(b.World.Credits > creditsBefore);
        Assert.True(b.World.Players[KeyA].Bag.Get("scrap") > 0);
        Assert.Equal(WorldCodec.Hash(h.Server.World), WorldCodec.Hash(a.World!));
        Assert.Equal(WorldCodec.Hash(h.Server.World), WorldCodec.Hash(b.World));
    }

    [Fact]
    public void Two_players_finishing_the_same_debris_in_one_flush_window_do_not_kill_the_server()
    {
        using var h = new NetHarness(new ServerOptions { FlushEveryTicks = 1000 });
        var a = h.Join(KeyA);
        var b = h.Join(KeyB);
        var world = h.Server.World;
        var d = world.DebrisById.Values.First(x => x.Def == "scrap_pile");
        d.Hp = 2;
        foreach (var key in new[] { KeyA, KeyB })
        {
            world.Players[key].X = d.X * 2f;
            world.Players[key].Z = d.Z * 2f;
        }
        var results = 0;
        a.ResultReceived += (_, _) => results++;
        b.ResultReceived += (_, _) => results++;
        a.Send(new Command { Type = CommandType.CutDebris, A = d.Id });
        b.Send(new Command { Type = CommandType.CutDebris, A = d.Id });
        h.Pump(() => results == 2);
        Assert.False(world.DebrisById.ContainsKey(d.Id));

        // Un cliente nuevo fuerza el vaciado de cambios con el escombro ya retirado.
        var late = h.Join("client-identity-cccc");
        h.Settle(200);
        Assert.False(late.World!.DebrisById.ContainsKey(d.Id));
    }

    [Fact]
    public void Client_cannot_credit_itself_with_invalid_commands()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        var before = h.Server.World.Credits;
        var results = new List<CommandResult>();
        a.ResultReceived += (_, r) => results.Add(r);
        var terminal = h.Server.World.Machines.Values.First(m => m.Def == "export_terminal");
        a.Send(new Command { Type = CommandType.Sell, A = terminal.Id, Text = "cryo_module", Amount = long.MaxValue });
        a.Send(new Command { Type = CommandType.Sell, A = terminal.Id, Text = "no_existe", Amount = 5 });
        a.Send(new Command { Type = CommandType.Place, Text = "depot", A = 0, B = -30 });
        h.Pump(() => results.Count == 3);
        Assert.All(results, r => Assert.False(r.Ok));
        Assert.Equal(before, h.Server.World.Credits);
    }

    [Fact]
    public void Thirteenth_player_is_rejected_without_affecting_others()
    {
        using var h = new NetHarness();
        var inside = new List<GameClient>();
        for (var i = 0; i < 12; i++) inside.Add(h.Join($"client-identity-{i:D4}"));
        Assert.Equal(12, h.Server.ConnectedCount);

        var late = h.Connect("client-identity-9999");
        h.Pump(() => late.State == ClientState.Closed);
        Assert.Equal("reject_full", late.CloseReason);
        h.Settle();
        Assert.Equal(12, h.Server.ConnectedCount);
        Assert.All(inside, c => Assert.Equal(ClientState.InGame, c.State));
    }

    [Fact]
    public void Reconnect_keeps_identity_inventory_and_slot()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        var player = h.Server.World.Players[KeyA];
        player.Bag.Add("scrap", 33);
        var num = player.Num;
        a.Disconnect();
        h.Pump(() => h.Server.ConnectedCount == 0);
        Assert.False(player.Connected);

        var again = h.Join(KeyA);
        Assert.Equal(num, again.LocalNum);
        Assert.Equal(33, again.LocalPlayer!.Bag.Get("scrap"));
        Assert.Single(h.Server.World.Players);
    }

    [Fact]
    public void Owner_can_kick_guest_but_guest_cannot_kick_owner()
    {
        using var h = new NetHarness();
        var owner = h.Join(KeyA);
        var guest = h.Join(KeyB);
        Assert.Equal(Role.Owner, h.Server.World.Players[KeyA].Role);
        Assert.Equal(Role.Builder, h.Server.World.Players[KeyB].Role);
        var denied = false;
        guest.ResultReceived += (_, r) => denied |= r.Error == "err_permission";
        guest.Send(new Command { Type = CommandType.Kick, Text = KeyA });
        h.Pump(() => denied);
        owner.Send(new Command { Type = CommandType.Kick, Text = KeyB });
        h.Pump(() => guest.State == ClientState.Closed);
        Assert.Equal("reject_kicked", guest.CloseReason);
        Assert.Equal(ClientState.InGame, owner.State);
        Assert.Equal(1, h.Server.ConnectedCount);
    }

    [Fact]
    public void Same_identity_replaces_previous_session_instead_of_taking_a_slot()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        var second = h.Join(KeyA);
        h.Pump(() => a.State == ClientState.Closed);
        Assert.Equal("reject_replaced", a.CloseReason);
        Assert.Equal(1, h.Server.ConnectedCount);
        Assert.Equal(ClientState.InGame, second.State);
    }

    /// <summary>El cliente numera desde 1 en cada sesión: la primera orden tras reconectar no puede recibir la respuesta guardada de la sesión anterior.</summary>
    [Fact]
    public void Cutting_debris_updates_the_client_index_without_a_structural_delta()
    {
        var world = World.CreateNew(Fixture.Content);
        var pile = world.DebrisById.Values.Where(d => d.CoversNode < 0).OrderBy(d => d.Id).First();
        var player = Fixture.AddPlayer(world, KeyA);
        player.Planet = pile.Planet;
        player.X = pile.X * 2;
        player.Z = pile.Z * 2;
        world.ClearReplicationState();
        using var h = new NetHarness(null, world);
        var client = h.Join(KeyA);
        h.Settle(100);
        h.Server.World.Players[KeyA].X = pile.X * 2;
        h.Server.World.Players[KeyA].Z = pile.Z * 2;
        Assert.Equal("err_debris", client.World!.CheckPlacement(pile.Planet, "storage_crate", pile.X, pile.Z, 0, -1, out _));
        var structural = client.StructuralDeltas;
        var version = client.PlacementVersion;

        for (var i = 0; i < 400 && client.World.DebrisById.ContainsKey(pile.Id); i++)
        {
            CommandResult? answer = null;
            void OnResult(uint _, CommandResult r) => answer = r;
            client.ResultReceived += OnResult;
            client.Send(new Command { Type = CommandType.CutDebris, A = pile.Id });
            h.Pump(() => answer != null);
            client.ResultReceived -= OnResult;
            h.Settle(120);
        }

        Assert.False(client.World.DebrisById.ContainsKey(pile.Id));
        Assert.NotEqual("err_debris", client.World.CheckPlacement(pile.Planet, "storage_crate", pile.X, pile.Z, 0, -1, out _));
        Assert.Equal(structural, client.StructuralDeltas);
        Assert.True(client.PlacementVersion > version);
    }

    [Fact]
    public void First_command_after_reconnecting_is_applied_not_answered_from_the_old_session()
    {
        var world = World.CreateNew(Fixture.Content);
        world.Upgrades.Add("tech_rail");
        world.Credits = 1000;
        foreach (var d in world.DebrisById.Values.Where(d => d.Z is > -40 and < -20 && d.X is > -10 and < 20).ToList()) world.DebrisById.Remove(d.Id);
        world.Reindex();
        world.ClearReplicationState();
        using var h = new NetHarness(null, world);
        var first = h.Join(KeyA);
        CommandResult? answer = null;
        first.ResultReceived += (_, r) => answer = r;
        first.Send(new Command { Type = CommandType.PlaceRail, A = 0, B = -30, C = 8, D = -30, Amount = 9 });
        h.Pump(() => answer != null);
        Assert.False(answer!.Value.Ok);

        first.Dispose();
        h.Clients.Remove(first);
        h.Settle(200);

        var again = h.Join(KeyA);
        var player = h.Server.World.Players[KeyA];
        player.X = 8;
        player.Z = -60;
        answer = null;
        again.ResultReceived += (_, r) => answer = r;
        again.Send(new Command { Type = CommandType.PlaceRail, A = 0, B = -30, C = 8, D = -30 });
        h.Pump(() => answer != null);
        Assert.True(answer!.Value.Ok, answer.Value.Error);
        Assert.Single(h.Server.World.RailSegments);
    }

    [Fact]
    public void Retried_request_is_applied_once()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        h.Server.World.Players[KeyA].Bag.Add("recovered", 20);
        var terminal = h.Server.World.Machines.Values.First(m => m.Def == "export_terminal");
        var world = h.Server.World;
        world.Players[KeyA].X = 0;
        world.Players[KeyA].Z = 0;
        var depot = world.Machines.Values.First(m => m.Def == "depot");
        depot.In.Add("ingot", 25);

        var results = 0;
        a.ResultReceived += (_, _) => results++;
        var raw = new PacketWriter();
        var transport = a.Transport;
        for (var i = 0; i < 3; i++)
        {
            var w = raw.Begin(MsgType.Command);
            w.Write(777u);
            CommandCodec.Write(w, new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" });
            raw.Send(transport, 0, Channel.Reliable);
        }
        h.Pump(() => results == 3);
        Assert.Equal(15, depot.In.Get("ingot"));
        Assert.Equal(1, world.Stats["contract.c_first_ingots"]);
        _ = terminal;
    }

    [Fact]
    public void Incompatible_protocol_gets_useful_rejection()
    {
        using var h = new NetHarness();
        var t = LiteNetTransport.Dial("127.0.0.1", h.Port);
        string? reason = null;
        t.Connected += peer =>
        {
            var pw = new PacketWriter();
            var w = pw.Begin(MsgType.Hello);
            w.Write(Protocol.Version + 99);
            w.Write("0.0.0");
            w.Write(Fixture.Content.Revision);
            w.Write(KeyA);
            w.Write("viejo");
            w.Write("");
            pw.Send(t, peer, Channel.Reliable);
        };
        t.Received += (_, data, length) =>
        {
            using var r = new BinaryReader(new MemoryStream(data, 0, length));
            if ((MsgType)r.ReadByte() == MsgType.Reject) reason = r.ReadString();
        };
        h.Pump(() => { t.Poll(); return reason != null; });
        Assert.Equal("reject_version", reason);
        t.Dispose();
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        using var h = new NetHarness(new ServerOptions { Password = "secreto" });
        var a = h.Connect(KeyA, password: "mal");
        h.Pump(() => a.State == ClientState.Closed);
        Assert.Equal("reject_password", a.CloseReason);
        var b = h.Connect(KeyB, password: "secreto");
        h.Pump(() => b.State == ClientState.InGame);
    }

    [Fact]
    public void Malformed_packets_kick_only_the_sender()
    {
        using var h = new NetHarness();
        var good = h.Join(KeyA);
        var bad = h.Join(KeyB);
        var garbage = new byte[] { (byte)MsgType.Command, 1, 2 };
        bad.Transport.Send(0, garbage, garbage.Length, Channel.Reliable);
        h.Pump(() => bad.State == ClientState.Closed);
        Assert.Equal(ClientState.InGame, good.State);
        Assert.Equal(1, h.Server.ConnectedCount);
    }

    [Fact]
    public void Speed_hack_is_corrected_by_server()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        var corrected = false;
        a.PositionCorrected += (_, _, _) => corrected = true;
        var p = h.Server.World.Players[KeyA];
        var startX = p.X;
        a.SendMove(startX + 500f, 1, p.Z, 0);
        h.Pump(() => corrected);
        Assert.Equal(startX, p.X);
        a.SendMove(float.NaN, 1, p.Z, 0);
        h.Settle(100);
        Assert.Equal(startX, p.X);
    }

    [Fact]
    public void Late_joiner_gets_consistent_state_while_world_advances()
    {
        using var h = new NetHarness();
        var a = h.Join(KeyA);
        var world = h.Server.World;
        var recycler = world.Machines.Values.First(m => m.Def == "recycler");
        recycler.In.Add("scrap", 8);
        world.DirtyMachines.Add(recycler.Id);
        h.Settle(300);

        var late = h.Join(KeyB);
        h.Pump(() => late.HashChecks >= 2 && a.HashChecks >= 2, 20000);
        Assert.Equal(0, late.HashMismatches);
        Assert.Equal(0, a.HashMismatches);
        Assert.Equal(ClientState.InGame, late.State);
    }

    [Fact]
    public void Periodic_hash_checks_match_with_activity()
    {
        using var h = new NetHarness(new ServerOptions { CheckEveryTicks = 5 });
        var a = h.Join(KeyA);
        var b = h.Join(KeyB);
        var recycler = h.Server.World.Machines.Values.First(m => m.Def == "recycler");
        recycler.In.Add("scrap", 20);
        h.Server.World.DirtyMachines.Add(recycler.Id);
        h.Pump(() => a.HashChecks >= 5 && b.HashChecks >= 5);
        Assert.Equal(0, a.HashMismatches);
        Assert.Equal(0, b.HashMismatches);
    }
}
