using System;
using System.Linq;
using System.Threading.Tasks;
using Code2Create.MinecraftRemote.Client;
using Newtonsoft.Json.Linq;
using Xunit;
using static Code2Create.MinecraftRemote.Client.Tests.BootstrapFixtureTests;

namespace Code2Create.MinecraftRemote.Client.Tests;
public sealed class ReconnectBehaviorTests
{
    [Fact]
    public async Task LostPollContinuesSamePairingThroughApprovalAndHello()
    {
        var data=Find("F1-AU-12");var steps=(JArray)data["steps"]!;
        var continuation=(JArray)Find("F1-AU-11")["steps"]!;
        // AU-11 already describes a fresh poll TCP, pending, approval, ok, and token hello.
        foreach(var step in continuation.Skip(10))steps.Add(step.DeepClone());
        await using var peer=new ScriptedPeer(data);var server=peer.RunAsync();
        await using var client=await MinecraftClient.ConnectAsync(Options(peer.Port,data),onPairing:(p,ct)=>Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        await server.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(1,peer.PairingAttempts);Assert.True(client.IsConnected);
    }
    [Fact]
    public async Task ConcurrentThreadsShareOneReconnect()
    {
        // The fixture runner covers ordered overlapping calls; here both calls start on OS thread-pool workers.
        var test=new OperationsFixtureTests();await test.Run("F1-OP-10",true);
    }
    [Fact]
    public async Task RetryBackoffUsesInjectedJitterAndIncreasingWindows()
    {
        var original=Find("F1-BL-01");var first=original["steps"]!.Take(3).Select(s=>s.DeepClone()).ToArray();
        var steps=new JArray(first);steps.Add(new JObject{["op"]="peer_eof"});
        steps.Add(new JObject{["op"]="connect"});foreach(var step in first.Skip(1))steps.Add(step.DeepClone());
        foreach(var step in original["steps"]!.Skip(5))
        { var copy=step.DeepClone();if(copy["message"]?["id"]!=null)copy["message"]!["id"]=(int)copy["message"]!["id"]!-1;steps.Add(copy); }
        var data=new JObject{["steps"]=steps};
        await using var peer=new ScriptedPeer(data);var clock=new ManualClock();
        var options=Options(peer.Port,original);options.Clock=clock;
        int dials=0,randomCalls=0;
        var times=new System.Collections.Concurrent.ConcurrentQueue<TimeSpan>();
        options.RandomFraction=()=>System.Threading.Interlocked.Increment(ref randomCalls)==1?0:1;
        options.DialOverride=async ct=>
        {
            int n=System.Threading.Interlocked.Increment(ref dials);times.Enqueue(clock.Now);
            if(n==2 || n==3)throw new ConnectionFailedException();
            var socket=new System.Net.Sockets.TcpClient();await socket.ConnectAsync(options.Host,options.Port);return socket;
        };
        var store=new InMemoryCredentialStore();await SeedToken(original,options,store);
        var server=peer.RunAsync();await using var client=await MinecraftClient.ConnectAsync(options,store);
        for(int i=0;i<1000 && client.IsConnected;i++)await Task.Delay(1);
        var read=client.GetBlockAsync(0,0,0);
        async Task WaitFor(int n){for(int i=0;i<2000 && dials<n;i++)await Task.Delay(1);Assert.Equal(n,dials);await Task.Delay(10);}
        await WaitFor(2);clock.Advance(TimeSpan.FromMilliseconds(250));
        await WaitFor(3);clock.Advance(TimeSpan.FromMilliseconds(1500));
        await read.WaitAsync(TimeSpan.FromSeconds(5));await server.WaitAsync(TimeSpan.FromSeconds(5));
        var measured=times.ToArray();Assert.Equal(TimeSpan.FromMilliseconds(250),measured[2]-measured[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(1500),measured[3]-measured[2]);Assert.Equal(2,randomCalls);
    }
    [Fact]
    public async Task PerCallIdleLimitCanExtendConnectionDefault()
    {
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        try
        {
            var clock=new ManualClock();var options=new ClientOptions("127.0.0.1",((System.Net.IPEndPoint)listener.LocalEndpoint).Port){Clock=clock};
            var open=ProtocolSession.OpenAsync(options,new NoCredentialStore());using var peer=await listener.AcceptTcpClientAsync();await using var session=await open;
            var request=session.RequestAsync("world.getBlock",new JArray(0,0,0),requestOptions:new RequestOptions(TimeSpan.FromMinutes(30)));
            using var reader=new System.IO.StreamReader(peer.GetStream());Assert.NotNull(await reader.ReadLineAsync());
            clock.Advance(TimeSpan.FromMinutes(16));await Task.Delay(30);Assert.False(request.IsCompleted);Assert.True(session.IsOpen);
            clock.Advance(TimeSpan.FromMinutes(15));await Assert.ThrowsAsync<CompletionUnknownException>(()=>request.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(session.PendingIds);Assert.True(session.KeepAliveTuningSupported);
        }
        finally{listener.Stop();}
    }
}
