#if !BESTHTTP_DISABLE_SOCKETIO

using System;

using BestHTTP.SocketIO3;
using BestHTTP.SocketIO3.Transports;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Socket.IO transport backed by BestHTTP/2 v2.7.0, using the SocketIO3 namespace because the capture
    /// station runs python-socketio 5.x, which speaks Socket.IO v4 over Engine.IO 4. The older
    /// BestHTTP.SocketIO namespace in the same plugin only speaks Engine.IO 3 and will not negotiate.
    ///
    /// This is the only file in the project that references BestHTTP types.
    /// </summary>
    public sealed class BestHttpSocketIoTransport : ISocketIoTransport {
        private SocketManager manager;
        private Socket socket;
        private string eventName = string.Empty;
        private bool disposed;

        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<string> Error;
        public event Action<string> FishPayloadReceived;

        public bool IsConnected {
            get { return manager != null && manager.State == SocketManager.States.Open; }
        }

        /// <summary> Library-reported state, surfaced for the debug overlay. </summary>
        public string StateDescription {
            get { return manager == null ? "Closed" : manager.State.ToString(); }
        }

        public void Connect(SocketIoConnectionSettings settings) {
            Disconnect();

            if (string.IsNullOrWhiteSpace(settings.Url)) {
                RaiseError("no Socket.IO url configured");
                return;
            }

            eventName = settings.EventName;

            SocketOptions options = new SocketOptions();
            options.AutoConnect = false;
            options.Reconnection = true;

            // BestHTTP treats attempts as a hard count, so an unattended installation asks for int.MaxValue.
            options.ReconnectionAttempts = settings.ReconnectionAttempts > 0 ? settings.ReconnectionAttempts : int.MaxValue;
            options.ReconnectionDelay = TimeSpan.FromSeconds(settings.ReconnectionDelaySeconds);
            options.ReconnectionDelayMax = TimeSpan.FromSeconds(settings.ReconnectionDelayMaxSeconds);
            options.Timeout = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds);

            // Polling handshakes over plain HTTP and upgrades to WebSocket, which survives environments
            // where a direct WebSocket open is blocked. WebSocketOnly skips the upgrade dance.
            options.ConnectWith = settings.PreferWebSocketOnly ? TransportTypes.WebSocket : TransportTypes.Polling;

            try {
                // The custom parser keeps the capture payload as text so it can be parsed off the main thread.
                manager = new SocketManager(new Uri(settings.Url), new RawFishPayloadParser(eventName), options);
                socket = string.IsNullOrWhiteSpace(settings.NamespaceName) || settings.NamespaceName == "/"
                    ? manager.Socket
                    : manager.GetSocket(settings.NamespaceName);

                socket.On(SocketIOEventTypes.Connect, HandleConnected);
                socket.On(SocketIOEventTypes.Disconnect, HandleDisconnected);
                socket.On<BestHTTP.SocketIO3.Error>(SocketIOEventTypes.Error, HandleSocketError);
                socket.On<string>(eventName, HandleFishPayloadReceived);

                manager.Open();
            } catch (Exception exception) {
                RaiseError("could not open " + settings.Url + " - " + exception.Message);
                Disconnect();
            }
        }

        public void Disconnect() {
            if (manager == null) {
                return;
            }

            try {
                manager.Close();
            } catch (Exception exception) {
                Debug.LogWarning("BestHttpSocketIoTransport: error while closing - " + exception.Message);
            }

            socket = null;
            manager = null;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Disconnect();

            Connected = null;
            Disconnected = null;
            Error = null;
            FishPayloadReceived = null;
        }

        private void HandleConnected() {
            Action handler = Connected;
            if (handler != null) {
                handler();
            }
        }

        private void HandleDisconnected() {
            Action<string> handler = Disconnected;
            if (handler != null) {
                handler("socket closed");
            }
        }

        private void HandleSocketError(BestHTTP.SocketIO3.Error error) {
            RaiseError(error != null ? error.message : "unknown socket error");
        }

        /// <summary>
        /// Main thread, but all that has happened so far is a framing scan and one substring. The JSON is
        /// still unparsed and the base64 is still encoded.
        /// </summary>
        private void HandleFishPayloadReceived(string rawJson) {
            Action<string> handler = FishPayloadReceived;
            if (handler != null) {
                handler(rawJson);
            }
        }

        private void RaiseError(string message) {
            Action<string> handler = Error;
            if (handler != null) {
                handler(message);
            }
        }
    }
}

#endif
