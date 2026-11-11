using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Code2Create.MinecraftRemote.Client
{
    public sealed class RemoteErrorException : Exception
    {
        private readonly JToken? errorData;
        internal RemoteErrorException(JObject error) : base((string?)error["message"] ?? "Remote error")
        { Code = (int)error["code"]!; errorData = error["data"]?.DeepClone(); }
        public int Code { get; }
        public JToken? ErrorData => errorData?.DeepClone();
        public string? Reason => (errorData as JObject)?["reason"]?.Type == JTokenType.String ? (string?)errorData!["reason"] : null;
        public bool IsProtocolMismatch => Reason == "protocol_mismatch";
        public string? ServerProtocol => (string?)(errorData as JObject)?["server"];
        public string? ClientRequires => (string?)(errorData as JObject)?["client_requires"];
    }
    public sealed class ProtocolCompatibilityException : Exception
    {
        internal ProtocolCompatibilityException(string server, string required) : base("Server protocol is incompatible with the client.") { ServerProtocol = server; ClientRequires = required; }
        public string ServerProtocol { get; }
        public string ClientRequires { get; }
    }
    public sealed class CompletionUnknownException : IOException
    { internal CompletionUnknownException() : base("The connection closed before a complete response. The outcome is unknown; the request was not retried.") { } }
    public sealed class RequestNotSentException : IOException
    { internal RequestNotSentException() : base("The connection is unavailable. The request was not sent.") { } }
    public sealed class ConnectionFailedException : IOException
    { internal ConnectionFailedException() : base("The TCP connection could not be established. No request was sent.") { } }
    public sealed class PairingRequiredException : Exception
    { internal PairingRequiredException() : base("Minecraft approval is required. Supply a pairing prompt handler to connect.") { } }
    public sealed class PairingExpiredException : Exception
    { internal PairingExpiredException() : base("Pairing was not approved or its time limit elapsed (client decision).") { } }
}
