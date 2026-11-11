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
        private readonly ProtocolSession session;
        private MinecraftClient(ProtocolSession session, HelloInfo hello) { this.session = session; Hello = hello; }

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
                    return new MinecraftClient(connection, new HelloInfo((JObject)hello));
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
                var elapsed = Stopwatch.StartNew();
                await prompt(new PairingPrompt(code, expiresIn), cancellationToken).ConfigureAwait(false);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (elapsed.Elapsed.TotalSeconds >= expiresIn) throw new PairingExpiredException();
                    if (!connection.IsOpen)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                        connection = await ProtocolSession.OpenAsync(options, store, cancellationToken).ConfigureAwait(false);
                    }
                    JToken result;
                    try { result = await connection.RequestAsync("auth.pairPoll", new JObject { ["pairing_id"] = pairingId }, cancellationToken).ConfigureAwait(false); }
                    catch (RequestNotSentException)
                    {
                        // §6.5: authはsticky TCPを要求しない。未送信と確定したpollだけ、新しい接続で送る。
                        // CompletionUnknownは捕捉せず、送信済みrequestを再送しない。
                        await connection.DisposeAsync().ConfigureAwait(false);
                        connection = await ProtocolSession.OpenAsync(options, store, cancellationToken).ConfigureAwait(false);
                        if (elapsed.Elapsed.TotalSeconds >= expiresIn) throw new PairingExpiredException();
                        result = await connection.RequestAsync("auth.pairPoll", new JObject { ["pairing_id"] = pairingId }, cancellationToken).ConfigureAwait(false);
                    }
                    if ((string?)result["status"] == "ok")
                        return (string?)result["token"] ?? throw new InvalidDataException("Pairing response has no credential.");
                    if ((string?)result["status"] != "pending") throw new InvalidDataException("Unknown pairing status.");
                    await Task.Delay(options.PairingPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }

        public HelloInfo Hello { get; }
        public bool IsConnected => session.IsOpen;
        public async Task SetBlockAsync(int x, int y, int z, BlockSpec block, CancellationToken cancellationToken = default)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            var result = await session.RequestAsync("world.setBlock", new JArray(x, y, z, block.ToJson()), cancellationToken).ConfigureAwait(false);
            if (result.Type != JTokenType.Null) throw new InvalidDataException("The setter response must be null.");
        }
        public async Task<BlockValue> GetBlockAsync(int x, int y, int z, CancellationToken cancellationToken = default)
        {
            var result = await session.RequestAsync("world.getBlock", new JArray(x, y, z), cancellationToken).ConfigureAwait(false);
            if (!(result is JObject value) || value["block_id"]?.Type != JTokenType.String || !(value["state"] is JObject))
                throw new InvalidDataException("The block response is not a structured block value.");
            return new BlockValue(value);
        }
        public Task<JToken> RequestAsync(string method, JToken parameters, CancellationToken cancellationToken = default) => session.RequestAsync(method, parameters, cancellationToken);
        public ValueTask CloseAsync() => session.DisposeAsync();
        public ValueTask DisposeAsync() => CloseAsync();
    }
}
