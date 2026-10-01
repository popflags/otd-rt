using System;
using System.Numerics;
using AnglePreservingSensitivity;
using Xunit;

namespace RapidTrigger.Tests
{
    public class DirectionalScaleTests
    {
        private const float Ratio = 20.617332f / 28.58895f;

        private static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X) * 180 / MathF.PI;

        [Fact]
        public void HorizontalMovementKeepsItsSpeed()
        {
            Assert.Equal(new Vector2(10, 0), DirectionalScale.Apply(new Vector2(10, 0), Ratio));
            Assert.Equal(new Vector2(-3, 0), DirectionalScale.Apply(new Vector2(-3, 0), Ratio));
        }

        [Fact]
        public void VerticalMovementGetsTheRatio()
        {
            var result = DirectionalScale.Apply(new Vector2(0, 10), Ratio);
            Assert.Equal(0, result.X, 5);
            Assert.Equal(10 * Ratio, result.Y, 4);
        }

        [Theory]
        [InlineData(45)]
        [InlineData(-30)]
        [InlineData(120)]
        [InlineData(200)]
        public void DirectionIsKept(float degrees)
        {
            var delta = new Vector2(MathF.Cos(degrees * MathF.PI / 180), MathF.Sin(degrees * MathF.PI / 180)) * 7;
            var result = DirectionalScale.Apply(delta, Ratio);

            float difference = Angle(result) - Angle(delta);
            difference = (difference + 540) % 360 - 180;
            Assert.Equal(0, difference, 3);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, -2)]
        [InlineData(-0.4f, 5)]
        public void SpeedMatchesAPerAxisRatio(float x, float y)
        {
            // Same length as OTD's X:Y sensitivity would give, but without bending the direction.
            var perAxis = new Vector2(x, y * Ratio);
            Assert.Equal(perAxis.Length(), DirectionalScale.Apply(new Vector2(x, y), Ratio).Length(), 4);
        }

        [Fact]
        public void PerAxisRatioBendsA45DegreeStroke()
        {
            // Why the plugin exists: X:Y = 28.589:20.617 turns 45 degrees into ~36.
            Assert.Equal(35.8, Angle(new Vector2(1, Ratio)), 1);
            Assert.Equal(45, Angle(DirectionalScale.Apply(new Vector2(1, 1), Ratio)), 3);
        }

        [Fact]
        public void NoMovementAndRatioOneAreUnchanged()
        {
            Assert.Equal(Vector2.Zero, DirectionalScale.Apply(Vector2.Zero, Ratio));
            Assert.Equal(new Vector2(3, 4), DirectionalScale.Apply(new Vector2(3, 4), 1));
        }
    }
}
