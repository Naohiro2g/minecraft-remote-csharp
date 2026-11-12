using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Code2Create.MinecraftRemote.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using static Code2Create.MinecraftRemote.Client.Tests.BootstrapFixtureTests;

namespace Code2Create.MinecraftRemote.Client.Tests;

internal sealed class ManualClock : IClientClock
{
    private readonly object sync = new();
    private TimeSpan now;
    private readonly List<(TimeSpan due, TaskCompletionSource<bool> task, CancellationTokenRegistration registration)> waiting = new();
    public TimeSpan Now { get { lock(sync) return now; } }
    public Task Delay(TimeSpan delay, CancellationToken ct)
    {
        lock(sync)
        {
            ct.ThrowIfCancellationRequested();
            var t = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = ct.Register(() => t.TrySetCanceled(ct));
            waiting.Add((now + delay, t, registration)); return t.Task;
        }
    }
    public void Advance(TimeSpan delta)
    {
        lock(sync)
        {
            now += delta;
            foreach (var w in waiting.Where(w => w.due <= now || w.task.Task.IsCompleted).ToArray())
            { waiting.Remove(w); w.registration.Dispose(); w.task.TrySetResult(true); }
        }
    }
}

public sealed class OperationsFixtureTests
{
    public static IEnumerable<object[]> Cases() => JObject.Parse(File.ReadAllText(Path.Combine(DirectoryPath,"client-bootstrap-operations-v23.2.1.json")))["cases"]!
        .Select(c => new object[]{(string)c["id"]!}).Where(c => (string)c[0] != "F1-OP-06" && (string)c[0] != "F1-OP-11");
    [Theory, MemberData(nameof(Cases))]
    public async Task OperationsCase(string id)
        => await Run(id, false);
    internal async Task Run(string id, bool concurrentThreads)
    {
        using var barrier = new Barrier(2);
        var concurrentParams = new HashSet<string>();
        var data = JObject.Parse(File.ReadAllText(Path.Combine(DirectoryPath,"client-bootstrap-operations-v23.2.1.json")))["cases"]!.Single(c => (string)c["id"]! == id);
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        var clock = new ManualClock();
        var options = new ClientOptions(IPAddress.Loopback.ToString(),((IPEndPoint)listener.LocalEndpoint).Port,
            new ClientIdentity("fixture-client","2321.1.0"), new BuildContext("minecraft:overworld",0,0,0), reconnectTimeout:TimeSpan.Zero) { Clock = clock };
        var store = new InMemoryCredentialStore();
        if(id != "F1-OP-08") await store.SaveAsync(options.CredentialTarget,"mcrs_fixture_only_not_a_real_credential");
        var sessions = new List<ProtocolSession>();
        options.SessionOpened = s => { lock(sessions) sessions.Add(s); };
        var peers = new Dictionary<string,TcpClient>();
        var readers = new Dictionary<string,StreamReader>();
        var received = new Dictionary<string,ConcurrentQueue<JObject>>();
        var sends = new Dictionary<string,ConcurrentQueue<JObject>>();
        var signals = new Dictionary<string,SemaphoreSlim>();
        var pumps = new List<Task>();
        var named = new Dictionary<string,ProtocolSession>();
        var calls = new Dictionary<string,Task>();
        var callConnections = new Dictionary<string,string>();
        var events = new List<JObject>();
        var negotiations = new List<JObject>();
        MinecraftClient? client = null;
        int attempts=0;
        var failedDial = new TaskCompletionSource<TcpClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        options.DialOverride = async ct =>
        {
            Interlocked.Increment(ref attempts);
            if(id=="F1-OP-05" && attempts==2) return await failedDial.Task;
            var socket=new TcpClient();await socket.ConnectAsync(options.Host,options.Port);return socket;
        };
        async Task Open()
        {
            client=await MinecraftClient.ConnectAsync(options,store,(p,ct)=>Task.CompletedTask);
            client.ConnectionRestored += (_,e) =>
            {
                lock(events)
                {
                    string current="c"+e.Generation, previous="c"+(e.Generation-1);
                    void Add(string kind)=>events.Add(new JObject{["kind"]=kind,["connection"]=current,["previous_connection"]=previous});
                    Add("reconnected");if(e.EventsLost)Add("events_lost");if(e.ServerChanged)Add("server_changed");if(e.SessionChanged)Add("session_changed");
                }
            };
        }
        async Task Settle()
        {
            // Wait only for network/task scheduling; virtual policy time never advances here.
            for(int i=0;i<20;i++) await Task.Delay(1);
        }
        async Task Observe(JToken expected)
        {
            await Settle();
            foreach(var row in expected["calls"] ?? new JArray())
            {
                string key=(string)row["call"]!;var task=calls[key];
                if((string)row["outcome"]! != "pending") try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }catch { }
                var error=task.Exception?.InnerException;
                string outcome=!task.IsCompleted?"pending":error switch
                { CompletionUnknownException=>"completion_unknown",RequestNotSentException=>"not_sent",PairingExpiredException=>"pairing_expired",RemoteErrorException=>"server_error",null=>"success",_=>throw new Exception("Unexpected failure",error) };
                var actual=new JObject{["call"]=key,["outcome"]=outcome,["source"]=(outcome=="server_error" || outcome=="success" && key!="open")?"server":"client"};
                if(callConnections.TryGetValue(key,out var connection))actual["connection"]=connection;
                if(error is RequestNotSentException ns)actual["cause"]=ns.Cause switch {NotSentCause.ConnectionFailed=>"connection_failed",NotSentCause.IncompatibleProtocol=>"incompatible_protocol",NotSentCause.PairingRequired=>"pairing_required",_=>"unknown"};
                if(error is PairingExpiredException)actual["equivalent_to"]="pair_expired";
                if(error is RemoteErrorException re)actual["error"]=new JObject{["code"]=re.Code,["message"]=re.Message,["data"]=re.ErrorData};
                Match(row,actual);
            }
            foreach(var row in expected["connections"] ?? new JArray())
            {
                string name=(string)row["connection"]!;
                var session=named.GetValueOrDefault(name);
                var actual=new JObject{["connection"]=name,["state"]=session==null?"failed":session.IsOpen?"open":"closed",["sent_count"]=received[name].Count,["pending_ids"]=new JArray(session?.PendingIds??Array.Empty<long>())};
                Match(row,actual);
            }
            if(expected["connect_attempts"]!=null)Assert.Equal((int)expected["connect_attempts"]!,attempts);
            foreach(var row in expected["events"]??new JArray())lock(events)Assert.Contains(events,e=>Contains(row,e));
            foreach(var row in expected["negotiations"]??new JArray())Assert.Contains(negotiations,e=>Contains(row,e));
        }
        try
        {
            foreach(var step in data["steps"]!)
            {
                string op=(string)step["op"]!,name=(string?)step["connection"]??"";
                switch(op)
                {
                    case "app_connect":calls[(string)step["call"]!]=Open();break;
                    case "app_call":
                        string key=(string)step["call"]!;
                        var operation=step;
                        calls[key]=concurrentThreads && id=="F1-OP-10" ? Task.Run(async () =>
                        { Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5))); await client!.RequestAsync((string)operation["method"]!,operation["params"]!); })
                        : client!.RequestAsync((string)step["method"]!,step["params"]!);break;
                    case "expect_connect":
                        if(id=="F1-OP-05" && name=="c2") {received[name]=new();break;}
                        var peer=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));peers[name]=peer;
                        readers[name]=new StreamReader(peer.GetStream(),Encoding.UTF8,false,1024,true);received[name]=new(); sends[name]=new(); signals[name]=new SemaphoreSlim(0);
                        string captured = name;
                        pumps.Add(Task.Run(async () =>
                        {
                            try { string? text; while((text=await readers[captured].ReadLineAsync())!=null)
                            { var message=JObject.Parse(text);received[captured].Enqueue(message);sends[captured].Enqueue(message);signals[captured].Release(); } }
                            catch(Exception error) when(error is IOException || error is ObjectDisposedException || error is SocketException) { }
                        }));
                        await Settle();lock(sessions)named[name]=sessions.Last();break;
                    case "expect_send":
                        Assert.True(await signals[name].WaitAsync(TimeSpan.FromSeconds(5)));
                        Assert.True(sends[name].TryDequeue(out var message));
                        if(concurrentThreads && id=="F1-OP-10" && (string)message!["method"]! == "world.getBlock")
                        { Assert.Equal((long)step["message"]!["id"]!, (long)message["id"]!); Assert.True(concurrentParams.Add(message["params"]!.ToString(Formatting.None))); }
                        else Equal(step["message"]!,message!);
                        string method=(string)message["method"]!;
                        if(method!="hello" && method!="auth.pairBegin" && method!="auth.pairPoll")
                        {
                            // Correlate actual request params to the application operation.
                            var candidate=data["steps"]!.First(s=>(string)s["op"]! == "app_call" && !callConnections.ContainsKey((string)s["call"]!) && (string)s["method"]! == method && JToken.DeepEquals(s["params"],message["params"]));
                            callConnections[(string)candidate["call"]!]=name;
                        }
                        break;
                    case "peer_send":
                        var response=step["message"]!;
                        if(response["result"]?["protocol"]!=null)negotiations.Add(new JObject{["connection"]=name,["compatible"]=ProtocolSession.Compatible((string)response["result"]!["protocol"]!,MinecraftClient.Protocol)});
                        await Write(peers[name],Encoding.UTF8.GetBytes(response.ToString(Formatting.None)+"\n"));await Settle();break;
                    case "peer_eof":peers[name].Dispose();await named[name].Closed.WaitAsync(TimeSpan.FromSeconds(5));break;
                    case "socket_error":case "liveness_lost":named[name].TransportLost();await Settle();break;
                    case "connect_error":failedDial.TrySetException(new ConnectionFailedException());await Settle();break;
                    case "advance_clock":
                        var duration=(string)step["timer"]! == "pairing_ttl"?TimeSpan.FromSeconds(120):options.ResponseIdleTimeout;
                        clock.Advance(TimeSpan.FromTicks(duration.Ticks*(long)step["fraction"]!["numerator"]!/(long)step["fraction"]!["denominator"]!));await Settle();break;
                    case "observe":await Observe(step["expected"]!);break;
                    default:throw new Exception("Unsupported op: "+op);
                }
            }
            await Observe(data["expected"]!);
        }
        finally {if(client!=null)await client.DisposeAsync();foreach(var peer in peers.Values)peer.Dispose();listener.Stop();await Task.WhenAll(pumps).WaitAsync(TimeSpan.FromSeconds(5));}
    }
    private static bool Contains(JToken expected,JToken actual)=> expected is JObject obj?obj.Properties().All(p=>actual[p.Name]!=null&&Contains(p.Value,actual[p.Name]!)):JToken.DeepEquals(expected,actual);
    private static void Match(JToken expected,JToken actual)
    {
        foreach(var p in ((JObject)expected).Properties())
        {
            Assert.NotNull(actual[p.Name]);
            if(p.Name=="pending_ids") Assert.Equal(p.Value.Values<long>().OrderBy(x=>x),actual[p.Name]!.Values<long>().OrderBy(x=>x));
            else if(p.Name=="error") Equal(p.Value,actual[p.Name]!);
            else Assert.True(Contains(p.Value,actual[p.Name]!),$"Expected {expected}; actual {actual}");
        }
    }
}
