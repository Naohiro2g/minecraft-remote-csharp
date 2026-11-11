using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Code2Create.MinecraftRemote.Client
{
    internal sealed class LineFramer
    {
        private readonly MemoryStream tail = new MemoryStream();
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal byte[] Tail => tail.ToArray();
        internal IReadOnlyList<JToken> Push(byte[] bytes)
        {
            var frames = new List<JToken>();
            foreach (var value in bytes)
            {
                if (value != 10) { tail.WriteByte(value); continue; }
                var line = tail.ToArray(); tail.SetLength(0);
                try
                {
                    using var text = new StringReader(Utf8.GetString(line));
                    using var reader = new StrictReader(text) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
                    var frame = JToken.ReadFrom(reader);
                    if (reader.Read()) continue;
                    frames.Add(frame);
                }
                catch (JsonException) { } // 非JSON行を破棄して、次のLF区切りへ進む。
                catch (DecoderFallbackException) { }
            }
            return frames;
        }
        private sealed class StrictReader : JsonTextReader
        {
            internal StrictReader(TextReader reader) : base(reader) { }
            public override bool Read()
            {
                bool found = base.Read();
                if (TokenType == JsonToken.Comment || TokenType == JsonToken.Undefined || TokenType == JsonToken.StartConstructor ||
                    ((TokenType == JsonToken.String || TokenType == JsonToken.PropertyName) && QuoteChar != '"'))
                    throw new JsonReaderException("Not a JSON value.");
                return found;
            }
        }
    }
    internal sealed class ProtocolSession : IAsyncDisposable
    {
        private readonly TcpClient socket;
        private readonly NetworkStream stream;
        private readonly ClientOptions options;
        private readonly ICredentialStore store;
        private readonly object sync = new object();
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private readonly Dictionary<long, TaskCompletionSource<JToken>> pending = new Dictionary<long, TaskCompletionSource<JToken>>();
        private readonly TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task readerTask;
        private bool isOpen = true;
        private long nextId;

        private ProtocolSession(TcpClient socket, ClientOptions options, ICredentialStore store)
        {
            this.socket = socket; stream = socket.GetStream(); this.options = options; this.store = store;
            readerTask = ReadLoopAsync();
        }
        internal static async Task<ProtocolSession> OpenAsync(ClientOptions options, ICredentialStore store, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new TcpClient { NoDelay = true };
            try
            {
                using (cancellationToken.Register(() => socket.Dispose()))
                    await socket.ConnectAsync(options.Host, options.Port).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new ProtocolSession(socket, options, store);
            }
            catch (Exception error) when (error is SocketException || error is IOException || error is ObjectDisposedException || error is OperationCanceledException)
            {
                socket.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                throw new ConnectionFailedException();
            }
        }
        internal bool IsOpen { get { lock (sync) return isOpen; } }
        internal IReadOnlyList<long> PendingIds { get { lock (sync) return pending.Keys.OrderBy(x => x).ToArray(); } }
        internal Task Closed => closed.Task;
        internal Action<long, bool>? ResponseObserved { get; set; }
        internal static bool InvalidToken(string? reason) => reason == "token_expired" || reason == "token_revoked" || reason == "token_not_found" || reason == "token_invalid";

        internal async Task<JToken> RequestAsync(string method, JToken parameters, CancellationToken cancellationToken = default)
        {
            JToken result;
            try { result = await SendAsync(method, parameters, cancellationToken).ConfigureAwait(false); }
            catch (RemoteErrorException error)
            {
                var rejectedToken = method == "hello" ? (string?)parameters["auth"]?["token"] : null;
                if (InvalidToken(error.Reason) && rejectedToken != null)
                    await store.DeleteAsync(options.CredentialTarget, rejectedToken, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            if (method == "hello")
            {
                string server = (string?)result["protocol"] ?? "";
                string required = (string?)parameters["protocol"] ?? MinecraftClient.Protocol;
                if (!Compatible(server, required))
                {
                    await DisposeAsync().ConfigureAwait(false);
                    throw new ProtocolCompatibilityException(server, required);
                }
            }
            else if (method == "auth.pairPoll" && (string?)result["status"] == "ok")
            {
                string token = (string?)result["token"] ?? throw new InvalidDataException("Pairing response has no credential.");
                await store.SaveAsync(options.CredentialTarget, token, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        private async Task<JToken> SendAsync(string method, JToken parameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("A method is required.", nameof(method));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            ValidateJson(parameters);
            await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
            TaskCompletionSource<JToken> response;
            long id;
            CancellationTokenRegistration registration = default;
            try
            {
                lock (sync)
                {
                    if (!isOpen) throw new RequestNotSentException();
                    id = nextId + 1;
                }
                var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters.DeepClone() };
                var bytes = Encoding.UTF8.GetBytes(message.ToString(Formatting.None) + "\n");
                lock (sync)
                {
                    if (!isOpen) throw new RequestNotSentException();
                    nextId = id;
                    response = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending.Add(id, response);
                }
                registration = cancellationToken.Register(() => { lock (sync) { if (pending.ContainsKey(id)) Finish(); } });
                try { await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is OperationCanceledException) { Finish(); }
            }
            catch { registration.Dispose(); throw; }
            finally { writer.Release(); }
            using (registration) return await response.Task.ConfigureAwait(false);
        }

        private async Task ReadLoopAsync()
        {
            var framer = new LineFramer();
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    int size = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (size == 0) break;
                    var bytes = new byte[size]; Array.Copy(buffer, bytes, size);
                    foreach (var token in framer.Push(bytes))
                    {
                        if (!(token is JObject message) || message["jsonrpc"]?.Type != JTokenType.String || (string?)message["jsonrpc"] != "2.0" ||
                            message["id"]?.Type != JTokenType.Integer ||
                            !long.TryParse(message["id"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long id)) continue;
                        var error = message["error"] as JObject;
                        if (error == null && message.Property("result") == null) continue;
                        if (error != null && (error["code"]?.Type != JTokenType.Integer || error["message"]?.Type != JTokenType.String ||
                            !int.TryParse(error["code"]!.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))) continue;
                        TaskCompletionSource<JToken>? response;
                        lock (sync) { pending.TryGetValue(id, out response); if (response != null) pending.Remove(id); }
                        ResponseObserved?.Invoke(id, response != null);
                        if (response == null) continue;
                        if (error != null) response.TrySetException(new RemoteErrorException(error));
                        else response.TrySetResult(message["result"]!.DeepClone());
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException) { }
            finally { Finish(); }
        }

        private void Finish()
        {
            TaskCompletionSource<JToken>[] requests;
            lock (sync)
            {
                if (!isOpen) return;
                isOpen = false; requests = pending.Values.ToArray(); pending.Clear();
            }
            socket.Dispose();
            foreach (var request in requests) request.TrySetException(new CompletionUnknownException());
            closed.TrySetResult(true);
        }

        public async ValueTask DisposeAsync() { Finish(); await readerTask.ConfigureAwait(false); }

        internal static void ValidateJson(JToken value)
        {
            switch (value.Type)
            {
                case JTokenType.Object:
                    foreach (var property in ((JObject)value).Properties()) ValidateJson(property.Value);
                    return;
                case JTokenType.Array:
                    foreach (var child in (JArray)value) ValidateJson(child);
                    return;
                case JTokenType.Float:
                    var number = ((JValue)value).Value;
                    if (number is double d && (double.IsNaN(d) || double.IsInfinity(d)) || number is float f && (float.IsNaN(f) || float.IsInfinity(f)))
                        throw new ArgumentException("JSON numbers must be finite.", nameof(value));
                    return;
                case JTokenType.Integer: case JTokenType.String: case JTokenType.Boolean: case JTokenType.Null: return;
                default: throw new ArgumentException("Only JSON values can be sent.", nameof(value));
            }
        }

        internal static bool Compatible(string server, string required)
        {
            bool Parse(string text, out int major, out int minor)
            {
                major = minor = 0;
                var parts = text.Split('.');
                return parts.Length == 3 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
                    int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor) &&
                    int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _);
            }
            return Parse(server, out int serverMajor, out int serverMinor) && Parse(required, out int clientMajor, out int clientMinor) && serverMajor == clientMajor && serverMinor >= clientMinor;
        }
    }
}
