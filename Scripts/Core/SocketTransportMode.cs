namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Which Engine.IO transport the Socket.IO client opens with. Polling is the safe default:
    /// it handshakes over HTTP and then upgrades to WebSocket, so a blocked WebSocket still works.
    /// </summary>
    public enum SocketTransportMode {
        PollingThenUpgradeToWebSocket = 0,
        WebSocketOnly = 1
    }
}
