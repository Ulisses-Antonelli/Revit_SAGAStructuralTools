namespace SAGAStructuralTools.BasePlate.Domain
{
    public sealed class BoltPoint
    {
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public BoltSide Side { get; set; }
    }
}
