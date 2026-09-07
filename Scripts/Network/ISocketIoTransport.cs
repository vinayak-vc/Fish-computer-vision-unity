using System;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Everything the aquarium needs from a Socket.IO client, with no library types crossing the boundary.
    /// Swapping BestHTTP for another client, or stubbing the socket in a test, is a matter of writing one
    /// more implementation of this interface.
    /// </summary>
    public interface ISocketIoTransport : IDisposable {
        bool IsConnected { get; }

        /// <summary> Raised on the Unity main thread once the namespace is joined. </summary>
        event Action Connected;

        /// <summary> Raised on the Unity main thread with the reason, if the library reported one. </summary>
        event Action<string> Disconnected;

        event Action<string> Error;

        /// <summary>
        /// Raw JSON of one capture event, raised on the Unity main thread. It is deliberately still text:
        /// parsing a megabyte of payload here would stall the frame, so that work belongs on a worker.
        /// </summary>
        event Action<string> FishPayloadReceived;

        void Connect(SocketIoConnectionSettings settings);

        void Disconnect();
    }

    /// <summary> Connection parameters, resolved from AquariumConfig so the transport never reads the config itself. </summary>
    public struct SocketIoConnectionSettings {
        public string Url { get; set; }
        public string NamespaceName { get; set; }
        public string EventName { get; set; }
        public bool PreferWebSocketOnly { get; set; }
        public int ReconnectionAttempts { get; set; }
        public float ReconnectionDelaySeconds { get; set; }
        public float ReconnectionDelayMaxSeconds { get; set; }
        public float ConnectTimeoutSeconds { get; set; }
    }
}
