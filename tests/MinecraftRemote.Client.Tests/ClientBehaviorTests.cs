using System;
using System.IO;
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

namespace Code2Create.MinecraftRemote.Client.Tests
{
    public sealed class ClientBehaviorTests
    {
        [Theory]
        [InlineData("F1-BL-01")]
        [InlineData("F1-BL-02")]
        [InlineData("F1-BL-03")]
        [InlineData("F1-BL-04")]
        [InlineData("F1-BL-05")]
        public async Task TypedBlockApiSendsPartialStateAndReturnsServerFullState(string id)
        {
            var data = Find(id);
            await using var peer = new ScriptedPeer(data);
            var options = Options(peer.Port, data); var store = new CountingStore();
            await SeedToken(data, options, store);
            var server = peer.RunAsync();
            await using var client = await MinecraftClient.ConnectAsync(options, store).WaitAsync(TimeSpan.FromSeconds(5));
            var request = (JObject)data["steps"]![3]!["message"]!["params"]![3]!;
            var block = new BlockSpec((string)request["block_id"]!, (JObject)request["state"]!);
            await client.SetBlockAsync(0, 0, 0, block).WaitAsync(TimeSpan.FromSeconds(5));
            var actual = await client.GetBlockAsync(0, 0, 0).WaitAsync(TimeSpan.FromSeconds(5));
            Equal(data["expected"]!["result"]!, actual.ToJson());
            Equal(actual.ToJson(), actual.ToSpec().ToJson());
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Theory]
        [InlineData("F1-HE-11")]
        [InlineData("F1-HE-12")]
        public async Task IncompatibleHelloClosesBeforeAnyOtherRequestAndPreservesToken(string id)
        {
            var data = Find(id);
            await using var peer = new ScriptedPeer(data);
            var options = Options(peer.Port, data); var store = new CountingStore();
            await SeedToken(data, options, store);
            var server = peer.RunAsync();
            var failure = await Assert.ThrowsAsync<ProtocolCompatibilityException>(() => MinecraftClient.ConnectAsync(options, store).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("23.2.1", failure.ClientRequires);
            Assert.Equal(0, store.Deletes);
            await server.WaitAsync(TimeSpan.FromSeconds(5)); // EOFを検査し、追加requestが無いことも確かめる。
        }

        [Fact]
        public async Task ClientDetectedPairingExpiryIsNotARemoteError()
        {
            var data = Find("F1-AU-01"); var steps = (JArray)data["steps"]!;
            while (steps.Count > 7) steps.RemoveAt(steps.Count - 1);
            steps[6]!["message"]!["result"]!["expires_in"] = 0;
            steps.Add(new JObject { ["op"] = "client_close" });
            await using var peer = new ScriptedPeer(data);
            var server = peer.RunAsync();
            int prompts = 0;
            await Assert.ThrowsAsync<PairingExpiredException>(() => MinecraftClient.ConnectAsync(Options(peer.Port, data),
                onPairing: (p, ct) => { prompts++; return Task.CompletedTask; }).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, prompts);
            Assert.Equal(1, peer.PairingAttempts);
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ClosingOrCancellingFailsEveryPendingRequestAndDoesNotResend(bool cancel)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try
            {
                var options = new ClientOptions(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port);
                var open = ProtocolSession.OpenAsync(options, new NoCredentialStore());
                using var peer = await listener.AcceptTcpClientAsync();
                await using var session = await open;
                using var cancellation = new CancellationTokenSource();
                var first = session.RequestAsync("world.getBlock", new JArray(0, 0, 0), cancellation.Token);
                var second = session.RequestAsync("world.getBlock", new JArray(1, 0, 0));
                using var reader = new StreamReader(peer.GetStream(), Encoding.UTF8, false, 1024, true);
                Assert.NotNull(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.NotNull(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                if (cancel) cancellation.Cancel();
                else peer.Dispose();
                await Assert.ThrowsAsync<CompletionUnknownException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
                await Assert.ThrowsAsync<CompletionUnknownException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Empty(session.PendingIds);
                await Assert.ThrowsAsync<RequestNotSentException>(() => session.RequestAsync("world.getBlock", new JArray(0, 0, 0)));
                Assert.False(listener.Pending());
            }
            finally { listener.Stop(); }
        }

        [Fact]
        public void CrLfIsAcceptedWithoutChangingJsonStrings()
        {
            var framer = new LineFramer();
            var frames = framer.Push(Encoding.UTF8.GetBytes("{\"id\":1,\"result\":\"a\\rb\"}\r\n"));
            Assert.Single(frames);
            Assert.Equal("a\rb", (string)frames[0]["result"]!);
            Assert.Empty(framer.Tail);
        }

        [Fact]
        public async Task CredentialsAreIsolatedByApplicationSessionAndEndpoint()
        {
            var store = new InMemoryCredentialStore();
            var key = new CredentialTarget("fixture-app", "session-a", "fixture.invalid", 1);
            await store.SaveAsync(key, "fixture-only-not-a-real-credential");
            Assert.NotNull(await store.LoadAsync(new CredentialTarget("fixture-app", "session-a", "FIXTURE.INVALID", 1)));
            Assert.Null(await store.LoadAsync(new CredentialTarget("other", "session-a", "fixture.invalid", 1)));
            Assert.Null(await store.LoadAsync(new CredentialTarget("fixture-app", "session-b", "fixture.invalid", 1)));
            Assert.Null(await store.LoadAsync(new CredentialTarget("fixture-app", "session-a", "fixture.invalid", 2)));
            var none = new NoCredentialStore(); await none.SaveAsync(key, "fixture-only-not-a-real-credential");
            Assert.Null(await none.LoadAsync(key));
            Assert.DoesNotContain("fixture.invalid", key.ToString());
            Assert.DoesNotContain("fixture-only", store.ToString());
        }

        [Fact]
        public void NonFiniteStateCannotSilentlyBecomeAJsonString()
        {
            Assert.Throws<ArgumentException>(() => new BlockSpec("fixture:block", new JObject { ["number"] = double.NaN }));
        }

        [Fact]
        public async Task RejectedOldCredentialCannotDeleteItsReplacement()
        {
            var store = new InMemoryCredentialStore();
            var target = new CredentialTarget("fixture", "fixture", "fixture.invalid", 1);
            await store.SaveAsync(target, "fixture-old");
            await store.SaveAsync(target, "fixture-new");
            await store.DeleteAsync(target, "fixture-old");
            Assert.Equal("fixture-new", await store.LoadAsync(target));
            await store.DeleteAsync(target, "fixture-new");
            Assert.Null(await store.LoadAsync(target));
        }

        [Fact]
        public async Task PairingCanContinueWhenThePreAuthTcpClosesWhileHumanApproves()
        {
            var data = Find("F1-AU-01"); var steps = (JArray)data["steps"]!;
            for (int i = 7; i <= 11; i++)
                if (steps[i]!["message"]?["id"] != null) steps[i]!["message"]!["id"] = (int)steps[i]!["message"]!["id"]! - 1;
            steps.Insert(7, new JObject { ["op"] = "peer_eof" });
            steps.Insert(8, new JObject { ["op"] = "connect", ["connection"] = "poll-connection" });
            await using var peer = new ScriptedPeer(data);
            var server = peer.RunAsync();
            await using var client = await MinecraftClient.ConnectAsync(Options(peer.Port, data),
                onPairing: async (p, ct) => await Task.Delay(100, ct)).WaitAsync(TimeSpan.FromSeconds(5));
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, peer.PairingAttempts);
            Assert.True(client.IsConnected);
        }
    }
}
