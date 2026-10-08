namespace SAGAStructuralTools.Strap.Domain
{
    public readonly struct Vector3D
    {
        public static Vector3D Zero => new Vector3D(0m, 0m, 0m);

        public Vector3D(decimal x, decimal y, decimal z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public decimal X { get; }
        public decimal Y { get; }
        public decimal Z { get; }

        public static Vector3D operator +(Vector3D left, Vector3D right)
            => new Vector3D(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Vector3D operator -(Vector3D left, Vector3D right)
            => new Vector3D(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3D Cross(Vector3D left, Vector3D right)
            => new Vector3D(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);
    }
}
