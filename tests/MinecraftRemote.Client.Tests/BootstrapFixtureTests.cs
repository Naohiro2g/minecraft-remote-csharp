using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Code2Create.MinecraftRemote.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Code2Create.MinecraftRemote.Client.Tests
{
    public sealed class BootstrapFixtureTests
    {
        internal static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        internal static IEnumerable<JObject> ReadCases() => Directory.GetFiles(DirectoryPath, "client-bootstrap-*-v23.2.1.json")
            .OrderBy(x => x).SelectMany(f => JObject.Parse(File.ReadAllText(f))["cases"]!.Cast<JObject>());
        public static IEnumerable<object[]> Cases() => ReadCases().Select(c => new object[] { (string)c["id"]! });
        internal static JObject Find(string id) => ReadCases().Single(c => (string)c["id"]! == id);

        [Fact]
        public void FixtureCopiesMatchPinnedSourceManifest()
        {
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "PROVENANCE.json")));
            Assert.Equal("36ebe9e63e4f0afcf949eae998832f68209b8119", (string)manifest["commit"]!);
            foreach (var file in manifest["files"]!)
            {
                var data = File.ReadAllBytes(Path.Combine(DirectoryPath, (string)file["file"]!));
                Assert.Equal((long)file["bytes"]!, data.LongLength);
                Assert.Equal((string)file["sha256"]!, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
            }
            Assert.Equal(48, ReadCases().Count());
            Assert.Equal(48, ReadCases().Select(c => (string)c["id"]!).Distinct().Count());
            foreach (var reference in ReadCases().SelectMany(c => c["value_refs"] ?? new JArray()))
            {
                JToken value = JToken.Parse(File.ReadAllText(Path.Combine(DirectoryPath, (string)reference["file"]!)));
                foreach (var part in ((string)reference["pointer"]!).Split('/').Skip(1))
                    value = value[part.Replace("~1", "/").Replace("~0", "~")]!;
                Assert.IsType<JObject>(value);
                Assert.NotEmpty(value);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task SharedBootstrapCase(string id)
        {
            var data = Find(id);
            var expected = (JObject)data["expected"]!;
            if (expected["frames"] != null)
            {
                var decoder = new LineFramer();
                var frames = new List<JToken>();
                foreach (var step in data["steps"]!) frames.AddRange(decoder.Push(Convert.FromHexString((string)step["bytes_hex"]!)));
                Equal(expected["frames"]!, new JArray(frames));
                Assert.Equal((string)expected["tail_hex"]!, Convert.ToHexString(decoder.Tail).ToLowerInvariant());
                return;
            }

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = Options(port, data);
            var store = new CountingStore();
            await SeedToken(data, options, store);
            ProtocolSession? session = null;
            TcpClient? peer = null;
            StreamReader? reader = null;
            var requests = new Dictionary<long, Task<JToken>>();
            var completed = new Dictionary<long, JToken>();
            var failures = new Dictionary<long, Exception>();
            var matched = new List<long>();
            var ignored = new List<long>();
            Exception? lastError = null;
            JToken? lastResult = null;
            int pairingAttempts = 0;

            async Task Observe(long requestId)
            {
                if (!requests.TryGetValue(requestId, out var task) || completed.ContainsKey(requestId) || failures.ContainsKey(requestId)) return;
                try { lastResult = await task.WaitAsync(TimeSpan.FromSeconds(5)); completed[requestId] = lastResult; }
                catch (Exception error) { failures[requestId] = error; lastError = error; }
            }

            try
            {
                foreach (var step in data["steps"]!)
                {
                    switch ((string)step["op"]!)
                    {
                        case "connect":
                            if (session != null) await session.DisposeAsync();
                            peer?.Dispose();
                            requests.Clear(); completed.Clear(); failures.Clear();
                            var open = ProtocolSession.OpenAsync(options, store);
                            peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                            session = await open.WaitAsync(TimeSpan.FromSeconds(5));
                            session.ResponseObserved = (requestId, found) => { lock (matched) { if (found) matched.Add(requestId); else ignored.Add(requestId); } };
                            reader = new StreamReader(peer.GetStream(), new UTF8Encoding(false, true), false, 1024, true);
                            break;
                        case "connect_error":
                            listener.Stop();
                            await Assert.ThrowsAsync<ConnectionFailedException>(() => ProtocolSession.OpenAsync(options, store));
                            break;
                        case "client_send":
                            var message = (JObject)step["message"]!;
                            long requestId = (long)message["id"]!;
                            string method = (string)message["method"]!;
                            if (method == "auth.pairBegin") pairingAttempts++;
                            requests[requestId] = session!.RequestAsync(method, message["params"]!);
                            string? line = await reader!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                            Assert.NotNull(line);
                            var actual = JObject.Parse(line!);
                            Assert.Equal(actual.ToString(Formatting.None), line);
                            Equal(message, actual);
                            break;
                        case "peer_send":
                            var response = step["message"]!;
                            await Write(peer!, Encoding.UTF8.GetBytes(response.ToString(Formatting.None) + "\n"));
                            await Observe((long)response["id"]!);
                            break;
                        case "peer_bytes":
                            await Write(peer!, Convert.FromHexString((string)step["bytes_hex"]!));
                            break;
                        case "peer_eof":
                            peer!.Dispose();
                            await session!.Closed.WaitAsync(TimeSpan.FromSeconds(5));
                            foreach (var pending in requests.Keys) await Observe(pending);
                            break;
                        case "client_close":
                            await session!.DisposeAsync();
                            foreach (var pending in requests.Keys) await Observe(pending);
                            break;
                        case "advance_time": // peerのfixture時計。AU-02はserverのpair_expired応答を検査する。
                        case "approve_pairing":
                            break;
                        default: throw new InvalidDataException("Unknown fixture operation.");
                    }
                }

                if (expected["result"] != null) Equal(expected["result"]!, lastResult!);
                if (expected["compatible"] != null)
                    Assert.Equal((bool)expected["compatible"]!, lastError is not ProtocolCompatibilityException);
                if (expected["reason"] != null)
                    Assert.Equal((string)expected["reason"]!, lastError is ProtocolCompatibilityException ? "protocol_mismatch" : (lastError as RemoteErrorException)?.Reason);
                if (expected["error"] != null)
                {
                    var error = Assert.IsType<RemoteErrorException>(lastError);
                    Equal(expected["error"]!, new JObject { ["code"] = error.Code, ["message"] = error.Message, ["data"] = error.ErrorData });
                }
                if (expected["pending_ids"] != null) Equal(expected["pending_ids"]!, new JArray(session?.PendingIds ?? Array.Empty<long>()));
                if (expected["completed_ids"] != null) Equal(expected["completed_ids"]!, new JArray(completed.Keys.OrderBy(x => x)));
                if (expected["matched_ids"] != null)
                {
                    var scope = expected["matched_ids"]!.Values<long>().ToHashSet();
                    Equal(expected["matched_ids"]!, new JArray(matched.Where(scope.Contains)));
                }
                if (expected["ignored_ids"] != null) Equal(expected["ignored_ids"]!, new JArray(ignored));
                if (expected["unanswered_ids"] != null) Equal(expected["unanswered_ids"]!, new JArray(failures.Keys.OrderBy(x => x)));
                foreach (var failure in expected["local_failures"] ?? new JArray())
                {
                    Assert.Equal("completion_unknown", (string)failure["outcome"]!);
                    Assert.IsType<CompletionUnknownException>(failures[(long)failure["id"]!]);
                }
                if (expected["connection"] != null && (string)expected["connection"]! != "failed")
                    Assert.Equal((string)expected["connection"]! == "open", session!.IsOpen);
                if (expected["token"] != null) Assert.Equal((string)expected["token"]!, await store.LoadAsync(options.CredentialTarget));
                if (expected["discard_token"] != null) Assert.Equal((bool)expected["discard_token"]!, store.Deletes > 0);
                if (expected["pairing_attempts"] != null) Assert.Equal((int)expected["pairing_attempts"]!, pairingAttempts);
                if (expected["pairing_required"] != null) Assert.Equal((bool)expected["pairing_required"]!, pairingAttempts > 0);
            }
            finally
            {
                peer?.Dispose(); listener.Stop();
                if (session != null) await session.DisposeAsync();
            }
        }

        [Theory]
        [InlineData("F1-AU-01")]
        [InlineData("F1-AU-05")]
        [InlineData("F1-AU-06")]
        [InlineData("F1-AU-08")]
        [InlineData("F1-AU-09")]
        [InlineData("F1-AU-10")]
        public async Task PublicConnectDrivesAuthenticationAndCredentialPolicy(string id)
        {
            var data = Find(id);
            await using var peer = new ScriptedPeer(data);
            var options = Options(peer.Port, data);
            var store = new CountingStore();
            await SeedToken(data, options, store);
            var server = peer.RunAsync();
            int prompts = 0;
            MinecraftClient? client = null;
            Exception? failure = null;
            try
            {
                int attempts = id == "F1-AU-06" ? 2 : 1;
                for (int i = 0; i < attempts; i++)
                {
                    if (client != null) await client.DisposeAsync();
                    client = await MinecraftClient.ConnectAsync(options, store, (prompt, ct) => { prompts++; Assert.Equal("/mcremote pair 123-456", prompt.Command); return Task.CompletedTask; }).WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception error) { failure = error; }
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(5));
                var expected = data["expected"]!;
                if (expected["reason"] != null) Assert.Equal((string)expected["reason"]!, Assert.IsType<RemoteErrorException>(failure).Reason);
                else { Assert.Null(failure); Assert.NotNull(client); }
                if (expected["result"] != null) Equal(expected["result"]!, client!.Hello.Json);
                if (expected["token"] != null) Assert.Equal((string)expected["token"]!, await store.LoadAsync(options.CredentialTarget));
                if (expected["discard_token"] != null) Assert.Equal((bool)expected["discard_token"]!, store.Deletes > 0);
                Assert.Equal(peer.PairingAttempts, prompts);
                Assert.True(prompts <= 1);
            }
            finally { if (client != null) await client.DisposeAsync(); }
        }

        internal static ClientOptions Options(int port, JObject data)
        {
            var hello = data["steps"]!.FirstOrDefault(s => (string?)s["message"]?["method"] == "hello")?["message"]?["params"];
            var identity = hello?["client"];
            var build = hello?["build"];
            return new ClientOptions(IPAddress.Loopback.ToString(), port,
                identity == null ? null : new ClientIdentity((string)identity["name"]!, (string)identity["version"]!, (string)identity["locale"]!),
                build == null ? null : new BuildContext((string)build["dimension"]!, (int)build["origin"]![0]!, (int)build["origin"]![1]!, (int)build["origin"]![2]!),
                sessionId: "fixture-session-scope", pairingPollInterval: TimeSpan.Zero);
        }
        internal static Task SeedToken(JObject data, ClientOptions options, ICredentialStore store)
        {
            var token = data["steps"]!.FirstOrDefault(s => (string?)s["message"]?["method"] == "hello")?["message"]?["params"]?["auth"]?["token"];
            return token == null ? Task.CompletedTask : store.SaveAsync(options.CredentialTarget, (string)token!);
        }
        internal static void Equal(JToken expected, JToken actual) => Assert.True(JToken.DeepEquals(expected, actual), "Decoded JSON differs from fixture.");
        internal static async Task Write(TcpClient peer, byte[] bytes) => await peer.GetStream().WriteAsync(bytes);

        internal sealed class CountingStore : ICredentialStore
        {
            private readonly InMemoryCredentialStore inner = new InMemoryCredentialStore();
            internal int Deletes { get; private set; }
            public Task<string?> LoadAsync(CredentialTarget target, CancellationToken cancellationToken = default) => inner.LoadAsync(target, cancellationToken);
            public Task SaveAsync(CredentialTarget target, string token, CancellationToken cancellationToken = default) => inner.SaveAsync(target, token, cancellationToken);
            public Task DeleteAsync(CredentialTarget target, string rejectedToken, CancellationToken cancellationToken = default) { Deletes++; return inner.DeleteAsync(target, rejectedToken, cancellationToken); }
        }

        internal sealed class ScriptedPeer : IAsyncDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly JObject data;
            private TcpClient? socket;
            private StreamReader? reader;
            internal ScriptedPeer(JObject data) { this.data = data; listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; }
            internal int Port { get; }
            internal int PairingAttempts { get; private set; }
            internal async Task RunAsync()
            {
                foreach (var step in data["steps"]!)
                {
                    switch ((string)step["op"]!)
                    {
                        case "connect":
                            socket?.Dispose(); socket = await listener.AcceptTcpClientAsync();
                            reader = new StreamReader(socket.GetStream(), new UTF8Encoding(false, true), false, 1024, true);
                            break;
                        case "client_send":
                            var expected = step["message"]!;
                            var line = await reader!.ReadLineAsync(); Assert.NotNull(line);
                            Equal(expected, JObject.Parse(line!));
                            if ((string)expected["method"]! == "auth.pairBegin") PairingAttempts++;
                            break;
                        case "peer_send": await Write(socket!, Encoding.UTF8.GetBytes(step["message"]!.ToString(Formatting.None) + "\n")); break;
                        case "peer_bytes": await Write(socket!, Convert.FromHexString((string)step["bytes_hex"]!)); break;
                        case "peer_eof": socket!.Dispose(); break;
                        case "client_close": Assert.Null(await reader!.ReadLineAsync()); break;
                        case "approve_pairing": case "advance_time": break;
                        default: throw new InvalidDataException("Unsupported scripted peer operation.");
                    }
                }
            }
            public ValueTask DisposeAsync() { socket?.Dispose(); listener.Stop(); return ValueTask.CompletedTask; }
        }
    }
}
