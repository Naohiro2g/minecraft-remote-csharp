using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Code2Create.MinecraftRemote.Client
{
    public sealed class ClientIdentity
    {
        public ClientIdentity(string name, string version, string locale = "ja")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Locale = locale ?? throw new ArgumentNullException(nameof(locale));
        }
        public string Name { get; }
        public string Version { get; }
        public string Locale { get; }
        internal JObject ToJson() => new JObject { ["name"] = Name, ["version"] = Version, ["locale"] = Locale };
    }

    public sealed class BuildContext
    {
        public BuildContext(string dimension, int x, int y, int z)
        {
            Dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
            X = x; Y = y; Z = z;
        }
        public string Dimension { get; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        internal JObject ToJson() => new JObject { ["dimension"] = Dimension, ["origin"] = new JArray(X, Y, Z) };
    }

    public sealed class ClientOptions
    {
        private static readonly string ProcessSession = Guid.NewGuid().ToString("N");
        public ClientOptions(string host, int port, ClientIdentity? identity = null, BuildContext? build = null,
            string? applicationId = null, string? sessionId = null, bool allowPairing = true, TimeSpan? pairingPollInterval = null, TimeSpan? responseIdleTimeout = null, bool autoReconnectEnabled = true,
            TimeSpan? reconnectTimeout = null, TimeSpan? keepAliveTime = null, TimeSpan? keepAliveInterval = null, int keepAliveRetryCount = 10)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("A host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            ResponseIdleTimeout = Positive(responseIdleTimeout ?? TimeSpan.FromMinutes(15));
            ReconnectTimeout = reconnectTimeout ?? TimeSpan.FromMinutes(5);
            if (ReconnectTimeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reconnectTimeout));
            KeepAliveTime = Positive(keepAliveTime ?? TimeSpan.FromMinutes(2));
            KeepAliveInterval = Positive(keepAliveInterval ?? TimeSpan.FromSeconds(30));
            if (keepAliveRetryCount < 1) throw new ArgumentOutOfRangeException(nameof(keepAliveRetryCount));
            KeepAliveRetryCount = keepAliveRetryCount;
            AutoReconnectEnabled = autoReconnectEnabled;
            Host = host; Port = port;
            Identity = identity ?? new ClientIdentity("minecraft-remote-csharp", "development");
            Build = build;
            AllowPairing = allowPairing;
            PairingPollInterval = pairingPollInterval ?? TimeSpan.FromSeconds(2);
            if (PairingPollInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pairingPollInterval));
            CredentialTarget = new CredentialTarget(applicationId ?? Identity.Name, sessionId ?? ProcessSession, host, port);
        }
        private static TimeSpan Positive(TimeSpan value) => value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
        public TimeSpan ResponseIdleTimeout { get; }
        public bool AutoReconnectEnabled { get; }
        public TimeSpan ReconnectTimeout { get; }
        public TimeSpan KeepAliveTime { get; }
        public TimeSpan KeepAliveInterval { get; }
        public int KeepAliveRetryCount { get; }
        internal IClientClock Clock { get; set; } = new SystemClientClock();
        internal Func<CancellationToken, Task<System.Net.Sockets.TcpClient>>? DialOverride { get; set; }
        internal Action<ProtocolSession>? SessionOpened { get; set; }
        internal Func<double> RandomFraction { get; set; } = NextRandom;
        private static readonly Random Random = new Random();
        private static double NextRandom() { lock (Random) return Random.NextDouble(); }
        public string Host { get; }
        public int Port { get; }
        public ClientIdentity Identity { get; }
        public BuildContext? Build { get; }
        public bool AllowPairing { get; }
        public TimeSpan PairingPollInterval { get; }
        public CredentialTarget CredentialTarget { get; }
        public override string ToString() => "McRemote TCP client options";
        internal JObject HelloParams(string? token)
        {
            var result = new JObject { ["protocol"] = MinecraftClient.Protocol, ["client"] = Identity.ToJson() };
            if (Build != null) result["build"] = Build.ToJson();
            if (token != null) result["auth"] = new JObject { ["token"] = token };
            return result;
        }
    }

    public sealed class CredentialTarget : IEquatable<CredentialTarget>
    {
        public CredentialTarget(string applicationId, string sessionId, string host, int port)
        {
            ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Host = (host ?? throw new ArgumentNullException(nameof(host))).ToLowerInvariant(); Port = port;
        }
        public string ApplicationId { get; }
        public string SessionId { get; }
        public string Transport => "tcp";
        public string Host { get; }
        public int Port { get; }
        public bool Equals(CredentialTarget? other) => other != null && ApplicationId == other.ApplicationId && SessionId == other.SessionId && Host == other.Host && Port == other.Port;
        public override bool Equals(object? obj) => Equals(obj as CredentialTarget);
        public override int GetHashCode() => HashCode.Combine(ApplicationId, SessionId, Transport, Host, Port);
        public override string ToString() => "McRemote credential target";
    }

    public interface ICredentialStore
    {
        Task<string?> LoadAsync(CredentialTarget target, CancellationToken cancellationToken = default);
        Task SaveAsync(CredentialTarget target, string token, CancellationToken cancellationToken = default);
        Task DeleteAsync(CredentialTarget target, string rejectedToken, CancellationToken cancellationToken = default);
    }

    public sealed class NoCredentialStore : ICredentialStore
    {
        public Task<string?> LoadAsync(CredentialTarget target, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<string?>(null); }
        public Task SaveAsync(CredentialTarget target, string token, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task DeleteAsync(CredentialTarget target, string rejectedToken, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public override string ToString() => "No credential storage";
    }

    public sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly ConcurrentDictionary<CredentialTarget, string> tokens = new ConcurrentDictionary<CredentialTarget, string>();
        public Task<string?> LoadAsync(CredentialTarget target, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(tokens.TryGetValue(target, out var token) ? token : null); }
        public Task SaveAsync(CredentialTarget target, string token, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); tokens[target] = token ?? throw new ArgumentNullException(nameof(token)); return Task.CompletedTask; }
        public Task DeleteAsync(CredentialTarget target, string rejectedToken, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 古いhelloの失敗で、別の接続試行が保存した新しいcredentialを消さない。
            ((ICollection<KeyValuePair<CredentialTarget, string>>)tokens).Remove(new KeyValuePair<CredentialTarget, string>(target, rejectedToken));
            return Task.CompletedTask;
        }
        public override string ToString() => "In-memory credential storage (redacted)";
    }

    public sealed class PairingPrompt
    {
        internal PairingPrompt(string code, double expiresInSeconds) { PairCode = code; ExpiresInSeconds = expiresInSeconds; }
        public string PairCode { get; }
        public double ExpiresInSeconds { get; }
        public string Command => "/mcremote pair " + (PairCode.Length == 6 ? PairCode.Substring(0, 3) + "-" + PairCode.Substring(3) : PairCode);
        public override string ToString() => Command;
    }

    public sealed class HelloInfo
    {
        private readonly JObject json;
        internal HelloInfo(JObject value) { json = (JObject)value.DeepClone(); }
        public string Protocol => (string)json["protocol"]!;
        public string MinecraftVersion => (string)json["mc_version"]!;
        public string Dimension => (string)json["dimension"]!;
        public IReadOnlyList<int> Origin => json["origin"]!.ToObject<int[]>()!;
        public JObject Json => (JObject)json.DeepClone();
        public override string ToString() => "McRemote hello result";
    }

    public class BlockSpec
    {
        private readonly JObject state;
        public BlockSpec(string blockId, JObject? state = null)
        {
            BlockId = blockId ?? throw new ArgumentNullException(nameof(blockId));
            this.state = (JObject)(state ?? new JObject()).DeepClone();
            foreach (var field in this.state.Properties())
            {
                if (field.Value.Type != JTokenType.Boolean && field.Value.Type != JTokenType.String && field.Value.Type != JTokenType.Integer && field.Value.Type != JTokenType.Float)
                    throw new ArgumentException("Block state values must be JSON boolean, number or string.", nameof(state));
                ProtocolSession.ValidateJson(field.Value);
            }
        }
        public string BlockId { get; }
        public JObject State => (JObject)state.DeepClone();
        public JObject ToJson() => new JObject { ["block_id"] = BlockId, ["state"] = state.DeepClone() };
    }

    public sealed class BlockValue : BlockSpec
    {
        internal BlockValue(JObject json) : base((string)json["block_id"]!, (JObject)json["state"]!) { }
        public BlockSpec ToSpec() => new BlockSpec(BlockId, State);
    }
}
