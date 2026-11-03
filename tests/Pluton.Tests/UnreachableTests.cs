using Pluton.Networking;
using Xunit;

namespace Pluton.Tests;

/// <summary>Conectar a una dirección sin servidor: el cliente debe cerrarse con motivo, no caerse (T-030, servidor Linux sin arrancar).</summary>
public class UnreachableTests
{
    [Fact]
    public void Dialing_a_port_with_no_server_closes_with_a_reason_instead_of_crashing()
    {
        var client = new GameClient(Fixture.Content, LiteNetTransport.Dial("127.0.0.1", 28099), "client-identity-unr1", "bot");
        var limit = Environment.TickCount64 + 12000;
        while (client.State != ClientState.Closed)
        {
            Assert.True(Environment.TickCount64 < limit, "El cliente no se cerró");
            client.Poll();
            Thread.Sleep(5);
        }
        Assert.Equal("reject_unreachable", client.CloseReason);
    }
}

public class InvalidHostTests
{
    [Theory]
    [InlineData("inet 172.17.0.1")]
    [InlineData("no-existe.invalid")]
    public void Dialing_an_invalid_host_closes_with_a_reason_instead_of_crashing(string host)
    {
        var client = new GameClient(Fixture.Content, LiteNetTransport.Dial(host, 28098), "client-identity-inv1", "bot");
        var limit = Environment.TickCount64 + 12000;
        while (client.State != ClientState.Closed)
        {
            Assert.True(Environment.TickCount64 < limit, "El cliente no se cerró");
            client.Poll();
            Thread.Sleep(5);
        }
        Assert.Equal("reject_unreachable", client.CloseReason);
    }
}
