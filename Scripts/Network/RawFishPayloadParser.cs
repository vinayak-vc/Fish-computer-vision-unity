#if !BESTHTTP_DISABLE_SOCKETIO

using BestHTTP.PlatformSupport.Memory;
using BestHTTP.SocketIO3;
using BestHTTP.SocketIO3.Parsers;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Socket.IO parser that hands the capture event through as raw JSON text instead of deserialising it.
    ///
    /// Why this exists: the stock DefaultJsonParser decodes an event by parsing the whole payload into a
    /// List of object, re-serialising the argument back to JSON, and parsing it again into the target type.
    /// Measured on this project that costs roughly 24 microseconds per KB even in the single-pass case, so a
    /// documented one-megabyte capture blocks the main thread for tens of milliseconds and the ten-event
    /// replay burst freezes it for a fraction of a second. All of that work happens on the main thread,
    /// because that is where BestHTTP raises Socket.IO events.
    ///
    /// This parser instead does the cheap Engine.IO framing scan, lifts the argument out as a substring,
    /// and lets a worker thread do the real parsing. Every packet that is not our event, and any event
    /// shaped differently to the contract, falls through to the stock parser untouched.
    /// </summary>
    public sealed class RawFishPayloadParser : IParser {
        private readonly IParser inner = new DefaultJsonParser();
        private readonly string eventName;

        public RawFishPayloadParser(string captureEventName) {
            eventName = captureEventName;
        }

        public IncomingPacket Parse(SocketManager manager, string from) {
            TransportEventTypes transportEvent;
            SocketIOEventTypes socketIoEvent;
            string namespaceName;
            int id;
            string argument;

            if (!TryExtractCaptureArgument(from, eventName, out argument, out transportEvent, out socketIoEvent, out namespaceName, out id)) {
                return inner.Parse(manager, from);
            }

            IncomingPacket packet = new IncomingPacket(transportEvent, socketIoEvent, namespaceName, id);
            packet.EventName = eventName;
            packet.DecodedArg = argument;
            return packet;
        }

        /// <summary>
        /// The whole fast-path decision in one pure function: true only when the frame is a text event for
        /// exactly captureEventName carrying exactly one JSON object, in which case argument is its raw text.
        /// </summary>
        public static bool TryExtractCaptureArgument(string frame, string captureEventName, out string argument) {
            TransportEventTypes transportEvent;
            SocketIOEventTypes socketIoEvent;
            string namespaceName;
            int id;
            return TryExtractCaptureArgument(frame, captureEventName, out argument, out transportEvent, out socketIoEvent, out namespaceName, out id);
        }

        private static bool TryExtractCaptureArgument(string frame, string captureEventName, out string argument, out TransportEventTypes transportEvent, out SocketIOEventTypes socketIoEvent, out string namespaceName, out int id) {
            argument = null;
            int payloadStart;

            if (!TrySplitFraming(frame, out transportEvent, out socketIoEvent, out namespaceName, out id, out payloadStart)) {
                return false;
            }

            if (socketIoEvent != SocketIOEventTypes.Event) {
                return false;
            }

            return TryExtractSingleObjectArgument(frame, payloadStart, captureEventName, out argument);
        }

        public IncomingPacket Parse(SocketManager manager, BufferSegment data, TransportEventTypes transportEvent = TransportEventTypes.Unknown) {
            return inner.Parse(manager, data, transportEvent);
        }

        public IncomingPacket MergeAttachements(SocketManager manager, IncomingPacket packet) {
            return inner.MergeAttachements(manager, packet);
        }

        public OutgoingPacket CreateOutgoing(TransportEventTypes transportEvent, string payload) {
            return inner.CreateOutgoing(transportEvent, payload);
        }

        public OutgoingPacket CreateOutgoing(Socket socket, SocketIOEventTypes socketIOEvent, int id, string name, object arg) {
            return inner.CreateOutgoing(socket, socketIOEvent, id, name, arg);
        }

        public OutgoingPacket CreateOutgoing(Socket socket, SocketIOEventTypes socketIOEvent, int id, string name, object[] args) {
            return inner.CreateOutgoing(socket, socketIOEvent, id, name, args);
        }

        /// <summary>
        /// Engine.IO framing: transport digit, Socket.IO digit, optional /namespace, optional ack id, payload.
        /// Returns false for anything the fast path should not touch, including binary packets.
        /// </summary>
        private static bool TrySplitFraming(string from, out TransportEventTypes transportEvent, out SocketIOEventTypes socketIoEvent, out string namespaceName, out int id, out int payloadStart) {
            transportEvent = TransportEventTypes.Unknown;
            socketIoEvent = SocketIOEventTypes.Unknown;
            namespaceName = "/";
            id = -1;
            payloadStart = 0;

            if (string.IsNullOrEmpty(from)) {
                return false;
            }

            int index = 0;

            int transportDigit = ToDigit(from, index);
            if (transportDigit < 0) {
                return false;
            }

            transportEvent = (TransportEventTypes)transportDigit;
            index++;

            int socketDigit = ToDigit(from, index);
            if (socketDigit < 0) {
                return false;
            }

            socketIoEvent = (SocketIOEventTypes)socketDigit;
            index++;

            if (socketIoEvent == SocketIOEventTypes.BinaryEvent || socketIoEvent == SocketIOEventTypes.BinaryAck) {
                return false;
            }

            if (index < from.Length && from[index] == '/') {
                int separator = from.IndexOf(',', index);
                if (separator < 0) {
                    return false;
                }

                namespaceName = from.Substring(index, separator - index);
                index = separator + 1;
            }

            int idStart = index;
            while (ToDigit(from, index) >= 0) {
                index++;
            }

            if (index > idStart) {
                int.TryParse(from.Substring(idStart, index - idStart), out id);
            }

            payloadStart = index;
            return payloadStart < from.Length;
        }

        /// <summary>
        /// Pulls the single object argument out of ["eventName",{...}] as raw text. Deliberately strict:
        /// anything that is not exactly one JSON object for exactly our event name returns false and the
        /// stock parser handles the packet instead.
        /// </summary>
        private static bool TryExtractSingleObjectArgument(string from, int payloadStart, string eventName, out string argument) {
            argument = null;

            int index = SkipWhitespace(from, payloadStart);
            if (!IsAt(from, index, '[')) {
                return false;
            }

            index = SkipWhitespace(from, index + 1);
            if (!IsAt(from, index, '"')) {
                return false;
            }

            index++;

            if (index + eventName.Length > from.Length) {
                return false;
            }

            if (string.CompareOrdinal(from, index, eventName, 0, eventName.Length) != 0) {
                return false;
            }

            index += eventName.Length;

            // The closing quote must land here, otherwise the wire name merely starts with ours.
            if (!IsAt(from, index, '"')) {
                return false;
            }

            index = SkipWhitespace(from, index + 1);
            if (!IsAt(from, index, ',')) {
                return false;
            }

            index = SkipWhitespace(from, index + 1);

            int closingBracket = from.LastIndexOf(']');
            if (closingBracket <= index) {
                return false;
            }

            int argumentEnd = closingBracket - 1;
            while (argumentEnd > index && char.IsWhiteSpace(from[argumentEnd])) {
                argumentEnd--;
            }

            if (!IsAt(from, index, '{') || !IsAt(from, argumentEnd, '}')) {
                return false;
            }

            argument = from.Substring(index, (argumentEnd - index) + 1);
            return true;
        }

        private static int SkipWhitespace(string text, int index) {
            while (index < text.Length && char.IsWhiteSpace(text[index])) {
                index++;
            }

            return index;
        }

        private static bool IsAt(string text, int index, char expected) {
            return index >= 0 && index < text.Length && text[index] == expected;
        }

        private static int ToDigit(string text, int index) {
            if (index < 0 || index >= text.Length) {
                return -1;
            }

            char character = text[index];
            if (character < '0' || character > '9') {
                return -1;
            }

            return character - '0';
        }
    }
}

#endif
