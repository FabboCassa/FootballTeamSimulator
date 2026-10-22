using System;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// <c>MovementGeometry.Sqrt</c> (internal, reached by reflection) is the hottest routine of a
    /// watched match, so R15's timing budget leans on it. Whatever it is built from, it must keep
    /// returning the exact floor square root the digit-by-digit method returned, or every match
    /// (and the golden master) moves.
    /// </summary>
    [TestFixture]
    public class IntegerSqrtTests
    {
        private static readonly Func<int, int> Sqrt = (Func<int, int>)typeof(MatchEngine).Assembly
            .GetType("Sim.Core.Match.Movement.MovementGeometry", throwOnError: true)!
            .GetMethod("Sqrt")!.CreateDelegate(typeof(Func<int, int>));

        [Test]
        public void EverySmallValue_IsTheExactFloorRoot()
        {
            for (int v = -5; v <= 1 << 22; v++)
                if (Sqrt(v) != DigitByDigit(v))
                    Assert.Fail($"Sqrt({v}) = {Sqrt(v)}, expected {DigitByDigit(v)}");
        }

        [Test]
        public void AroundEveryPerfectSquare_IsTheExactFloorRoot()
        {
            for (long r = 1; r <= 46341; r++)
            {
                foreach (long v in new[] { r * r - 1, r * r, r * r + 1 })
                {
                    if (v > int.MaxValue) continue;
                    int value = (int)v;
                    if (Sqrt(value) != DigitByDigit(value))
                        Assert.Fail($"Sqrt({value}) = {Sqrt(value)}, expected {DigitByDigit(value)}");
                }
            }

            Assert.That(Sqrt(int.MaxValue), Is.EqualTo(46340));
        }

        [Test]
        public void RandomValues_AreTheExactFloorRoot()
        {
            var rng = new Pcg32(20261022);
            for (int i = 0; i < 2_000_000; i++)
            {
                int value = (int)(rng.NextUInt() >> 1);
                if (Sqrt(value) != DigitByDigit(value))
                    Assert.Fail($"Sqrt({value}) = {Sqrt(value)}, expected {DigitByDigit(value)}");
            }
        }

        /// <summary>The engine's original shift-and-add floor square root, kept here as the reference.</summary>
        private static int DigitByDigit(int value)
        {
            if (value <= 0) return 0;

            uint n = (uint)value;
            uint result = 0;
            uint bit = 1u << 30;
            while (bit > n) bit >>= 2;

            while (bit != 0)
            {
                uint step = result + bit;
                if (n >= step)
                {
                    n -= step;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }

                bit >>= 2;
            }

            return (int)result;
        }
    }
}
