namespace SAGAStructuralTools.Strap.Domain
{
    public sealed class ForceMoment
    {
        public ForceMoment(
            decimal fx,
            decimal fy,
            decimal fz,
            decimal mx,
            decimal my,
            decimal mz)
            : this(
                new Vector3D(fx, fy, fz),
                new Vector3D(mx, my, mz))
        {
        }

        public ForceMoment(Vector3D force, Vector3D moment)
        {
            Force = force;
            Moment = moment;
        }

        public Vector3D Force { get; }
        public Vector3D Moment { get; }

        public decimal Fx => Force.X;
        public decimal Fy => Force.Y;
        public decimal Fz => Force.Z;
        public decimal Mx => Moment.X;
        public decimal My => Moment.Y;
        public decimal Mz => Moment.Z;

        public ForceMoment Rotate90()
            => new ForceMoment(
                new Vector3D(Fy, Fx, Fz),
                new Vector3D(My, Mx, Mz));

        public ForceMoment Add(ForceMoment other)
        {
            if (other == null)
                throw new DomainValidationException("A reação a somar não pode ser nula.");

            return new ForceMoment(Force + other.Force, Moment + other.Moment);
        }

        public ForceMoment Transport(Vector3D supportPosition, Vector3D referencePoint)
        {
            Vector3D leverArm = supportPosition - referencePoint;
            return new ForceMoment(Force, Moment + Vector3D.Cross(leverArm, Force));
        }
    }
}
