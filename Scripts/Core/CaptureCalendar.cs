using System;
using System.Globalization;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Counts how many drawings arrived today, which is design point 29's "created today" figure.
    ///
    /// Kept free of Unity and of the clock: the caller passes the current date in, so rollover at
    /// midnight can be tested without waiting for it. That matters because an installation runs
    /// unattended for days and midnight is the one moment this can get wrong.
    ///
    /// Parsing is deliberately invariant-culture. The capture timestamp is ISO 8601 with no timezone
    /// suffix - contract section 3 - and parsing it against a machine's local format would work on the
    /// developer's laptop and fail on an exhibition machine set to a different locale.
    /// </summary>
    public sealed class CaptureCalendar {
        private const string DateFormat = "yyyy-MM-dd";

        private string trackedDate = string.Empty;
        private int capturesToday;
        private int latestSequence;

        /// <summary> Drawings whose timestamp falls on the tracked date. </summary>
        public int CapturesToday {
            get { return capturesToday; }
        }

        /// <summary> The date the count refers to, as yyyy-MM-dd, or empty before anything has been counted. </summary>
        public string TrackedDate {
            get { return trackedDate; }
        }

        /// <summary> Highest capture sequence seen. Monotonic within the capture station's output directory. </summary>
        public int LatestSequence {
            get { return latestSequence; }
        }

        /// <summary>
        /// Records one capture. A timestamp that will not parse still advances the sequence but does not
        /// count towards today: a malformed date is not worth losing the whole arrival over.
        /// </summary>
        public void RecordCapture(string timestamp, int sequence, DateTime now) {
            if (sequence > latestSequence) {
                latestSequence = sequence;
            }

            DateTime captured;
            if (!TryParseTimestamp(timestamp, out captured)) {
                return;
            }

            string capturedDate = captured.ToString(DateFormat, CultureInfo.InvariantCulture);
            string currentDate = now.ToString(DateFormat, CultureInfo.InvariantCulture);

            // Only today's drawings count, and the tally resets when the date moves on.
            if (!string.Equals(capturedDate, currentDate, StringComparison.Ordinal)) {
                return;
            }

            if (!string.Equals(trackedDate, currentDate, StringComparison.Ordinal)) {
                trackedDate = currentDate;
                capturesToday = 0;
            }

            capturesToday++;
        }

        /// <summary>
        /// Reads the count back after a restart. A count saved yesterday is discarded rather than carried
        /// forward, so the first visitor of the morning does not see the previous day's total.
        /// </summary>
        public void Restore(string savedDate, int savedCount, int savedSequence, DateTime now) {
            latestSequence = Math.Max(0, savedSequence);

            string currentDate = now.ToString(DateFormat, CultureInfo.InvariantCulture);

            if (string.IsNullOrEmpty(savedDate) || !string.Equals(savedDate, currentDate, StringComparison.Ordinal)) {
                trackedDate = string.Empty;
                capturesToday = 0;
                return;
            }

            trackedDate = savedDate;
            capturesToday = Math.Max(0, savedCount);
        }

        /// <summary> Rolls the tally over when the date changes with no capture to trigger it. </summary>
        public void RefreshDate(DateTime now) {
            string currentDate = now.ToString(DateFormat, CultureInfo.InvariantCulture);

            if (string.IsNullOrEmpty(trackedDate) || string.Equals(trackedDate, currentDate, StringComparison.Ordinal)) {
                return;
            }

            trackedDate = string.Empty;
            capturesToday = 0;
        }

        /// <summary> ISO 8601, no timezone suffix, invariant culture. Contract section 3. </summary>
        public static bool TryParseTimestamp(string timestamp, out DateTime parsed) {
            parsed = default(DateTime);

            if (string.IsNullOrWhiteSpace(timestamp)) {
                return false;
            }

            return DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
        }
    }
}
