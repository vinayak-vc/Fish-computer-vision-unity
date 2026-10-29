using System;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The "created today" tally, design point 29.
    ///
    /// Midnight is the interesting case and the one that cannot be tested by waiting for it, which is why
    /// the calendar takes the current time as an argument rather than reading the clock. An installation
    /// runs unattended for days, so a rollover bug would show up as yesterday's total greeting the first
    /// visitor of the morning.
    /// </summary>
    public sealed class CaptureCalendarTests {
        private static readonly DateTime Today = new DateTime(2026, 9, 2, 14, 30, 0);
        private static readonly DateTime Tomorrow = new DateTime(2026, 9, 3, 9, 15, 0);

        [Test]
        public void ANewCalendarHasCountedNothing() {
            CaptureCalendar calendar = new CaptureCalendar();

            Assert.AreEqual(0, calendar.CapturesToday);
            Assert.AreEqual(0, calendar.LatestSequence);
            Assert.IsEmpty(calendar.TrackedDate);
        }

        [Test]
        public void TodaysCapturesAreCounted() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.RecordCapture("2026-09-02T09:00:00", 1, Today);
            calendar.RecordCapture("2026-09-02T11:20:00", 2, Today);

            Assert.AreEqual(2, calendar.CapturesToday);
            Assert.AreEqual("2026-09-02", calendar.TrackedDate);
        }

        [Test]
        public void ACaptureFromAnotherDayIsNotCounted() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.RecordCapture("2026-09-01T23:59:00", 1, Today);

            Assert.AreEqual(0, calendar.CapturesToday, "A replayed capture from yesterday is not a drawing made today.");
        }

        [Test]
        public void TheSequenceAdvancesEvenForACaptureThatIsNotCountedToday() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.RecordCapture("2026-09-01T23:59:00", 42, Today);

            Assert.AreEqual(42, calendar.LatestSequence);
            Assert.AreEqual(0, calendar.CapturesToday);
        }

        [Test]
        public void TheSequenceOnlyEverRises() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.RecordCapture("2026-09-02T09:00:00", 10, Today);
            calendar.RecordCapture("2026-09-02T09:01:00", 3, Today);

            Assert.AreEqual(10, calendar.LatestSequence, "A replay burst arrives out of order; the counter must not go backwards.");
        }

        [Test]
        public void AMalformedTimestampDoesNotLoseTheArrival() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.RecordCapture("not a date", 7, Today);

            Assert.AreEqual(7, calendar.LatestSequence, "A bad date is not worth discarding the whole capture over.");
            Assert.AreEqual(0, calendar.CapturesToday);
        }

        [Test]
        public void AnEmptyTimestampIsSurvivable() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.RecordCapture(null, 1, Today);
            calendar.RecordCapture(string.Empty, 2, Today);

            Assert.AreEqual(0, calendar.CapturesToday);
        }

        [Test]
        public void TheTallyResetsWhenTheDateMovesOn() {
            CaptureCalendar calendar = new CaptureCalendar();
            calendar.RecordCapture("2026-09-02T09:00:00", 1, Today);

            calendar.RecordCapture("2026-09-03T08:00:00", 2, Tomorrow);

            Assert.AreEqual(1, calendar.CapturesToday, "The new day starts from that day's first drawing, not from yesterday's total.");
            Assert.AreEqual("2026-09-03", calendar.TrackedDate);
        }

        [Test]
        public void TheTallyRollsOverWithoutACaptureToTriggerIt() {
            // An unattended installation sits idle through midnight. The count has to roll over anyway.
            CaptureCalendar calendar = new CaptureCalendar();
            calendar.RecordCapture("2026-09-02T09:00:00", 1, Today);

            calendar.RefreshDate(Tomorrow);

            Assert.AreEqual(0, calendar.CapturesToday);
        }

        [Test]
        public void RefreshingWithinTheSameDayChangesNothing() {
            CaptureCalendar calendar = new CaptureCalendar();
            calendar.RecordCapture("2026-09-02T09:00:00", 1, Today);

            calendar.RefreshDate(new DateTime(2026, 9, 2, 23, 59, 0));

            Assert.AreEqual(1, calendar.CapturesToday);
        }

        [Test]
        public void ACountSavedTodayIsRestored() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.Restore("2026-09-02", 17, 99, Today);

            Assert.AreEqual(17, calendar.CapturesToday);
            Assert.AreEqual(99, calendar.LatestSequence);
        }

        [Test]
        public void ACountSavedYesterdayIsDiscardedButTheSequenceIsKept() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.Restore("2026-09-01", 17, 99, Today);

            Assert.AreEqual(0, calendar.CapturesToday, "Yesterday's total must not greet this morning's first visitor.");
            Assert.AreEqual(99, calendar.LatestSequence, "The sequence is monotonic forever, not per day.");
        }

        [Test]
        public void RestoringFromAnEmptyOrCorruptSaveIsSurvivable() {
            CaptureCalendar calendar = new CaptureCalendar();

            calendar.Restore(null, -5, -1, Today);

            Assert.AreEqual(0, calendar.CapturesToday);
            Assert.AreEqual(0, calendar.LatestSequence, "A hand-edited save file must not produce a negative counter.");
        }

        [Test]
        public void RestoringThenRecordingContinuesTheTally() {
            CaptureCalendar calendar = new CaptureCalendar();
            calendar.Restore("2026-09-02", 4, 10, Today);

            calendar.RecordCapture("2026-09-02T15:00:00", 11, Today);

            Assert.AreEqual(5, calendar.CapturesToday);
        }

        [Test]
        public void TimestampsAreParsedInvariantlyRatherThanByMachineLocale() {
            // Contract section 3: ISO 8601, no timezone suffix. Parsing against a machine's locale would
            // work on the developer's laptop and quietly fail on an exhibition machine set elsewhere.
            DateTime parsed;

            Assert.IsTrue(CaptureCalendar.TryParseTimestamp("2026-09-02T18:23:46", out parsed));
            Assert.AreEqual(2026, parsed.Year);
            Assert.AreEqual(9, parsed.Month);
            Assert.AreEqual(2, parsed.Day);
            Assert.AreEqual(18, parsed.Hour);
        }

        [Test]
        public void AnUnparseableTimestampIsReported() {
            DateTime parsed;

            Assert.IsFalse(CaptureCalendar.TryParseTimestamp(null, out parsed));
            Assert.IsFalse(CaptureCalendar.TryParseTimestamp("   ", out parsed));
            Assert.IsFalse(CaptureCalendar.TryParseTimestamp("yesterday", out parsed));
        }
    }
}
