using System;

namespace RaidPrototype
{
    /// <summary>A lightweight, Unity-independent two-dimensional vector.</summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static Vec2 Zero => new Vec2(0, 0);
        public static Vec2 One => new Vec2(1, 1);
        public static Vec2 UnitX => new Vec2(1, 0);
        public static Vec2 UnitY => new Vec2(0, 1);
        public float LengthSquared() => X * X + Y * Y;
        public float Length() => (float)Math.Sqrt((double)X * X + (double)Y * Y);
        public static Vec2 Normalize(Vec2 value)
        {
            double length = Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y);
            return new Vec2((float)(value.X / length), (float)(value.Y / length));
        }
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared();
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length();
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float b) => new Vec2(a.X * b, a.Y * b);
        public static Vec2 operator *(float a, Vec2 b) => b * a;
        public static Vec2 operator /(Vec2 a, float b) => new Vec2(a.X / b, a.Y / b);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);
        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public override string ToString() => $"({X}, {Y})";
    }
}
