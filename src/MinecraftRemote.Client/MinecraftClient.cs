using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Code2Create.MinecraftRemote.Client
{
    public sealed class MinecraftClient : IAsyncDisposable
    {
        public const string Protocol = "23.2.1";
        private ProtocolSession session;
        private readonly ClientOptions options;
        private readonly ICredentialStore store;
        private string? token;
        private BuildContext build;
        private readonly object sync = new object();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task<ProtocolSession>? reconnect;
        private bool disposed;
        private Task sendTail = Task.CompletedTask;
        private long generation = 1;
        private MinecraftClient(ProtocolSession session, HelloInfo hello, ClientOptions options, ICredentialStore store, string? token)
        { this.session = session; Hello = hello; this.options = options; this.store = store; this.token = token;
          build = new BuildContext(hello.Dimension, hello.Origin[0], hello.Origin[1], hello.Origin[2]); }
        public event EventHandler<ConnectionRestoredEventArgs>? ConnectionRestored;
        public long ConnectionGeneration { get { lock (sync) return generation; } }

        public static async Task<MinecraftClient> ConnectAsync(ClientOptions options, ICredentialStore? credentialStore = null,
            Func<PairingPrompt, CancellationToken, Task>? onPairing = null, CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var store = credentialStore ?? new NoCredentialStore();
            string? token = await store.LoadAsync(options.CredentialTarget, cancellationToken).ConfigureAwait(false);
            bool pairingUsed = false;
            while (true)
            {
                var connection = await ProtocolSession.OpenAsync(options, store, cancellationToken).ConfigureAwait(false);
                try
                {
                    var hello = await connection.RequestAsync("hello", options.HelloParams(token), cancellationToken).ConfigureAwait(false);
                    return new MinecraftClient(connection, new HelloInfo((JObject)hello), options, store, token);
                }
                catch (RemoteErrorException error) when (ProtocolSession.InvalidToken(error.Reason) && token != null && !pairingUsed)
                {
                    // Deleteはhelloの処理で行う。認証reasonの後も、必ずtokenなしhelloを先に試す。
                    await connection.DisposeAsync().ConfigureAwait(false);
                    token = null;
                }
                catch (RemoteErrorException error) when (error.Reason == "auth_required" && !pairingUsed)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    if (!options.AllowPairing || onPairing == null) throw new PairingRequiredException();
                    pairingUsed = true;
                    token = await PairAsync(options, store, onPairing, cancellationToken).ConfigureAwait(false);
                }
                catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
            }
        }

        private static async Task<string> PairAsync(ClientOptions options, ICredentialStore store,
            Func<PairingPrompt, CancellationToken, Task> prompt, CancellationToken cancellationToken)
        {
            var connection = await ProtocolSession.OpenAsync(options, store, cancellationToken).ConfigureAwait(false);
            try
            {
                var begin = await connection.RequestAsync("auth.pairBegin", new JObject
                { ["token_type"] = "session", ["client"] = options.Identity.ToJson() }, cancellationToken).ConfigureAwait(false);
                string pairingId = (string?)begin["pairing_id"] ?? throw new InvalidDataException("Pairing response is incomplete.");
                string code = (string?)begin["pair_code"] ?? throw new InvalidDataException("Pairing response is incomplete.");
                double expiresIn = (double?)begin["expires_in"] ?? throw new InvalidDataException("Pairing response is incomplete.");
                var started = options.Clock.Now;
                await prompt(new PairingPrompt(code, expiresIn), cancellationToken).ConfigureAwait(false);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((options.Clock.Now - started).TotalSeconds >= expiresIn) throw new PairingExpiredException();
                    if (!connection.IsOpen)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                        connection = await ProtocolSession.OpenAsync(options, store, cancellationToken).ConfigureAwait(false);
                    }
                    if ((options.Clock.Now - started).TotalSeconds >= expiresIn) throw new PairingExpiredException();
                    JToken result;
                    try { result = await connection.RequestAsync("auth.pairPoll", new JObject { ["pairing_id"] = pairingId }, cancellationToken).ConfigureAwait(false); }
                    catch (CompletionUnknownException)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                        await options.Clock.Delay(options.PairingPollInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    catch (RequestNotSentException)
                    {
                        // §6.5: authはsticky TCPを要求しない。未送信と確定したpollだけ、新しい接続で送る。
                        // CompletionUnknownは捕捉せず、送信済みrequestを再送しない。
                        await connection.DisposeAsync().ConfigureAwait(false);
                        connection = await ProtocolSession.OpenAsync(options, store, cancellationToken).ConfigureAwait(false);
                        if ((options.Clock.Now - started).TotalSeconds >= expiresIn) throw new PairingExpiredException();
                        result = await connection.RequestAsync("auth.pairPoll", new JObject { ["pairing_id"] = pairingId }, cancellationToken).ConfigureAwait(false);
                    }
                    if ((string?)result["status"] == "ok")
                        return (string?)result["token"] ?? throw new InvalidDataException("Pairing response has no credential.");
                    if ((string?)result["status"] != "pending") throw new InvalidDataException("Unknown pairing status.");
                    await options.Clock.Delay(options.PairingPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }

        public HelloInfo Hello { get; private set; }
        public bool KeepAliveTuningSupported => session.KeepAliveTuningSupported;
        public bool IsConnected => session.IsOpen;
        public async Task SetBlockAsync(int x, int y, int z, BlockSpec block, CancellationToken cancellationToken = default)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            var result = await RequestAsync("world.setBlock", new JArray(x, y, z, block.ToJson()), cancellationToken).ConfigureAwait(false);
            if (result.Type != JTokenType.Null) throw new InvalidDataException("The setter response must be null.");
        }
        public async Task<BlockValue> GetBlockAsync(int x, int y, int z, CancellationToken cancellationToken = default)
        {
            var result = await RequestAsync("world.getBlock", new JArray(x, y, z), cancellationToken).ConfigureAwait(false);
            if (!(result is JObject value) || value["block_id"]?.Type != JTokenType.String || !(value["state"] is JObject))
                throw new InvalidDataException("The block response is not a structured block value.");
            return new BlockValue(value);
        }
        public Task<JToken> RequestAsync(string method, JToken parameters, CancellationToken cancellationToken = default)
            => RequestAsync(method, parameters, null, cancellationToken);
        public async Task<JToken> RequestAsync(string method, JToken parameters, RequestOptions? requestOptions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProtocolSession active;
            Task preceding;
            var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ProtocolSession>? restoring = null;
            lock (sync)
            {
                if (disposed) throw new RequestNotSentException(NotSentCause.Closed);
                active = session;
                if (!active.IsOpen)
                {
                    if (!options.AutoReconnectEnabled) throw new RequestNotSentException(NotSentCause.ReconnectDisabled);
                    if (reconnect == null || reconnect.IsCompleted) reconnect = ReconnectAsync();
                    restoring = reconnect;
                }
                preceding = sendTail; sendTail = sent.Task;
            }
            Task<JToken> response;
            try
            {
                if (restoring != null) active = await AwaitWithCancellation(restoring, cancellationToken).ConfigureAwait(false);
                await preceding.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                response = active.RequestAsync(method, parameters, cancellationToken, requestOptions);
            }
            finally { sent.TrySetResult(true); }
            var result = await response.ConfigureAwait(false);
            if ((method == "build.setDimension" || method == "build.setOrigin") && result is JObject context &&
                context["dimension"]?.Type == JTokenType.String && context["origin"] is JArray origin)
                lock (sync) if (ReferenceEquals(session, active)) build = new BuildContext((string)context["dimension"]!, (int)origin[0]!, (int)origin[1]!, (int)origin[2]!);
            return result;
        }
        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled) return await task.ConfigureAwait(false);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelled.TrySetResult(true)))
            { await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false); ct.ThrowIfCancellationRequested(); return await task.ConfigureAwait(false); }
        }
        private async Task<ProtocolSession> ReconnectAsync()
        {
            // Yield so that concurrent callers can share the task before any dial completes.
            await Task.Yield();
            var started = options.Clock.Now;
            int attempt = 0;
            bool tokenRetried = false;
            while (true)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                ProtocolSession? next = null;
                string? saved = null;
                try
                {
                    saved = await store.LoadAsync(options.CredentialTarget, lifetime.Token).ConfigureAwait(false) ?? token;
                    next = await ProtocolSession.OpenAsync(options, store, lifetime.Token).ConfigureAwait(false);
                    JObject parameters = options.HelloParams(saved);
                    lock (sync) parameters["build"] = build.ToJson();
                    var result = await next.RequestAsync("hello", parameters, lifetime.Token).ConfigureAwait(false);
                    ConnectionRestoredEventArgs notice;
                    lock (sync)
                    {
                        if (disposed) throw new OperationCanceledException();
                        var previous = Hello;
                        Hello = new HelloInfo((JObject)result); session = next; token = saved;
                        notice = new ConnectionRestoredEventArgs(previous, Hello, ++generation);
                    }
                    // Application observers must not break transport state or leak a successful connection.
                    foreach (EventHandler<ConnectionRestoredEventArgs> handler in ConnectionRestored?.GetInvocationList() ?? Array.Empty<Delegate>())
                        try { handler(this, notice); } catch { }
                    return next;
                }
                catch (ProtocolCompatibilityException error)
                { if (next != null) await next.DisposeAsync().ConfigureAwait(false); throw new RequestNotSentException(NotSentCause.IncompatibleProtocol, error); }
                catch (RemoteErrorException error)
                {
                    if (next != null) await next.DisposeAsync().ConfigureAwait(false);
                    if (ProtocolSession.InvalidToken(error.Reason) && saved != null && !tokenRetried) { token = null; tokenRetried = true; continue; }
                    throw new RequestNotSentException(error.Reason == "auth_required" ? NotSentCause.PairingRequired : NotSentCause.ConnectionFailed, error);
                }
                catch (Exception error) when (error is ConnectionFailedException || error is CompletionUnknownException || error is RequestNotSentException)
                {
                    if (next != null) await next.DisposeAsync().ConfigureAwait(false);
                    var remaining = options.ReconnectTimeout - (options.Clock.Now - started);
                    if (remaining <= TimeSpan.Zero) throw new RequestNotSentException(NotSentCause.ConnectionFailed);
                    var delay = TimeSpan.FromMilliseconds(Math.Min(30000, 500 * Math.Pow(2, Math.Min(attempt++, 16))) * (0.5 + options.RandomFraction()));
                    await options.Clock.Delay(delay < remaining ? delay : remaining, lifetime.Token).ConfigureAwait(false);
                }
                catch { if (next != null) await next.DisposeAsync().ConfigureAwait(false); throw; }
            }
        }
        public async ValueTask CloseAsync()
        {
            Task<ProtocolSession>? pending;
            lock (sync) { disposed = true; lifetime.Cancel(); pending = reconnect; }
            await session.DisposeAsync().ConfigureAwait(false);
            if (pending != null) try { await pending.ConfigureAwait(false); } catch { }
        }
        public ValueTask DisposeAsync() => CloseAsync();
    }
}
