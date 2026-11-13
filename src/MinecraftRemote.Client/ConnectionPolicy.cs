using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Code2Create.MinecraftRemote.Client
{
    internal interface IClientClock
    {
        TimeSpan Now { get; }
        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }
    internal sealed class SystemClientClock : IClientClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public TimeSpan Now => watch.Elapsed;
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }
    public sealed class RequestOptions
    {
        public RequestOptions(TimeSpan responseIdleTimeout)
        {
            if (responseIdleTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(responseIdleTimeout));
            ResponseIdleTimeout = responseIdleTimeout;
        }
        public TimeSpan ResponseIdleTimeout { get; }
    }
    public sealed class ConnectionRestoredEventArgs : EventArgs
    {
        internal ConnectionRestoredEventArgs(HelloInfo previous, HelloInfo current, long generation)
        { Previous = previous; Current = current; Generation = generation; }
        public HelloInfo Previous { get; }
        public HelloInfo Current { get; }
        public long Generation { get; }
        public bool EventsLost => true;
        public bool ServerChanged => !Newtonsoft.Json.Linq.JToken.DeepEquals(Previous.Json["server"], Current.Json["server"]);
        public bool SessionChanged => !Newtonsoft.Json.Linq.JToken.DeepEquals(Previous.Json["session"], Current.Json["session"]);
        public bool CatalogChanged => !Newtonsoft.Json.Linq.JToken.DeepEquals(Previous.Json["catalogHash"], Current.Json["catalogHash"]);
    }
    public enum NotSentCause { ConnectionFailed, PairingRequired, IncompatibleProtocol, ReconnectDisabled, Closed }
}
