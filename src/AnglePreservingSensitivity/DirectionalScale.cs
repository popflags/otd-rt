using System;
using System.Numerics;

namespace AnglePreservingSensitivity
{
    /// <summary>
    /// Direction-dependent speed that keeps the direction of every movement. No OTD dependency, allocation-free.
    /// </summary>
    public static class DirectionalScale
    {
        /// <summary>
        /// Scales a movement so that it keeps its direction, while its length changes as if the vertical part were
        /// multiplied by <paramref name="verticalRatio"/>: horizontal movement keeps its speed, vertical movement
        /// gets <paramref name="verticalRatio"/> of it, and diagonals get exactly the speed a per-axis X:Y ratio
        /// would give them, without bending their angle.
        /// </summary>
        public static Vector2 Apply(Vector2 delta, float verticalRatio)
        {
            float xx = delta.X * delta.X;
            float yy = delta.Y * delta.Y;
            float lengthSquared = xx + yy;
            if (!(lengthSquared > 0))
                return delta;

            float scale = MathF.Sqrt((xx + verticalRatio * verticalRatio * yy) / lengthSquared);
            return delta * scale;
        }
    }
}
