using System;

namespace PocketOrbits.Core.Simulation
{
    /// <summary>
    /// Double-precision 2D vector used by the gameplay simulation. The simulation never
    /// touches UnityEngine types so it can be tested (and replayed) outside the engine.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public double LengthSquared => X * X + Y * Y;
        public double Length => Math.Sqrt(X * X + Y * Y);

        public Vec2 Normalized
        {
            get
            {
                double len = Length;
                return len > 1e-12 ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        /// <summary>Counter-clockwise perpendicular.</summary>
        public Vec2 Perpendicular => new Vec2(-Y, X);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(double s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, double s) => new Vec2(a.X / s, a.Y / s);

        public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static double DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;
        public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public Vec2 ClampLength(double max)
        {
            double sq = LengthSquared;
            if (sq <= max * max || sq <= 0) return this;
            double scale = max / Math.Sqrt(sq);
            return new Vec2(X * scale, Y * scale);
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
        public override string ToString() => "(" + X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", " + Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Axis-aligned rectangle on the gameplay plane.</summary>
    public readonly struct Rect2
    {
        public readonly double MinX, MinY, MaxX, MaxY;

        public Rect2(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public Vec2 Center => new Vec2((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

        public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

        public Rect2 Expanded(double margin) => new Rect2(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

        public Vec2 ClosestPoint(Vec2 p) => new Vec2(Math.Max(MinX, Math.Min(MaxX, p.X)), Math.Max(MinY, Math.Min(MaxY, p.Y)));
    }
}
