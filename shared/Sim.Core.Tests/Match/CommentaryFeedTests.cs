using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Match.Broadcast;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The commentary panel's cursor (spec watchable-match-engine R15): which lines are on show at a
    /// playback frame, which ones the panel has to draw after a step, and which pooled row each sits in.
    /// </summary>
    [TestFixture]
    public class CommentaryFeedTests
    {
        private static IReadOnlyList<CommentaryLine> Lines(params int[] frames) =>
            frames.Select(f => new CommentaryLine(f, f / 10 + 1, f / 10 + 1, CommentaryIcon.Miss, false,
                Array.Empty<CommentaryClause>(), null)).ToArray();

        [Test]
        public void ALine_IsShownOnceItsFrameIsReached_AndNotBefore()
        {
            var feed = new CommentaryFeed(Lines(10, 20, 20, 35), capacity: 8);

            Assert.That(feed.AdvanceTo(9), Is.EqualTo(0));
            Assert.That(feed.Shown, Is.EqualTo(0));

            Assert.That(feed.AdvanceTo(10), Is.EqualTo(0), "draw from the first line");
            Assert.That(feed.Shown, Is.EqualTo(1));

            Assert.That(feed.AdvanceTo(34), Is.EqualTo(1), "both lines on frame 20 come in together");
            Assert.That(feed.Shown, Is.EqualTo(3));

            Assert.That(feed.AdvanceTo(1000), Is.EqualTo(3));
            Assert.That(feed.Shown, Is.EqualTo(4));
        }

        [Test]
        public void AStepThatRevealsNothing_AsksForNoDraw()
        {
            var feed = new CommentaryFeed(Lines(10, 20), capacity: 8);
            feed.AdvanceTo(15);

            int from = feed.AdvanceTo(16);

            Assert.That(from, Is.EqualTo(feed.Shown), "from == Shown means an empty draw range");
            Assert.That(feed.Shown, Is.EqualTo(1));
        }

        [Test]
        public void PlaybackNeverTakesALineBack()
        {
            var feed = new CommentaryFeed(Lines(10, 20, 30), capacity: 8);
            feed.AdvanceTo(25);

            int from = feed.AdvanceTo(5);

            Assert.That(feed.Shown, Is.EqualTo(2));
            Assert.That(from, Is.EqualTo(2));
        }

        [Test]
        public void ABigJump_DrawsOnlyTheLinesThePoolCanHold()
        {
            // A resumed re-simulation seeks straight to minute 60: a hundred lines arrive at once.
            int[] frames = Enumerable.Range(0, 100).Select(i => i * 5).ToArray();
            var feed = new CommentaryFeed(Lines(frames), capacity: 12);

            int from = feed.AdvanceTo(frames[99]);

            Assert.That(feed.Shown, Is.EqualTo(100));
            Assert.That(from, Is.EqualTo(88), "only the newest twelve get a row");
            Assert.That(feed.FirstKept, Is.EqualTo(88));
        }

        [Test]
        public void Rows_AreRecycledOldestFirst_SoTheKeptLinesNeverShareARow()
        {
            const int capacity = 5;
            int[] frames = Enumerable.Range(0, 23).ToArray();
            var feed = new CommentaryFeed(Lines(frames), capacity);

            var rowHolder = new int[capacity];
            for (int r = 0; r < capacity; r++) rowHolder[r] = -1;

            foreach (int frame in frames)
            {
                int from = feed.AdvanceTo(frame);
                for (int i = from; i < feed.Shown; i++)
                {
                    int row = feed.RowOf(i);
                    Assert.That(rowHolder[row], Is.EqualTo(i < capacity ? -1 : i - capacity),
                        $"line {i} must take the row of the oldest kept line");
                    rowHolder[row] = i;
                }

                int[] kept = Enumerable.Range(feed.FirstKept, feed.Shown - feed.FirstKept).ToArray();
                Assert.That(kept.Select(feed.RowOf).Distinct().Count(), Is.EqualTo(kept.Length));
                Assert.That(kept.Length, Is.LessThanOrEqualTo(capacity));
            }

            Assert.That(rowHolder.OrderBy(i => i), Is.EqualTo(new[] { 18, 19, 20, 21, 22 }));
        }

        [Test]
        public void AnEmptyCommentary_ShowsNothing()
        {
            var feed = new CommentaryFeed(Array.Empty<CommentaryLine>(), capacity: 4);

            Assert.That(feed.AdvanceTo(int.MaxValue), Is.EqualTo(0));
            Assert.That(feed.Shown, Is.EqualTo(0));
            Assert.That(feed.Count, Is.EqualTo(0));
        }

        [Test]
        public void ANonPositiveCapacity_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CommentaryFeed(Lines(1), capacity: 0));
            Assert.Throws<ArgumentNullException>(() => new CommentaryFeed(null!, capacity: 3));
        }
    }
}
