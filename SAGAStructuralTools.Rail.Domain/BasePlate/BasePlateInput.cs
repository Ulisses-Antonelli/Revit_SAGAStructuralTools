namespace SAGAStructuralTools.BasePlate.Domain
{
    public class BasePlateInput
    {
        public double DepthMm { get; set; }
        public double FlangeWidthMm { get; set; }
        public double WebThicknessMm { get; set; }
        public double FlangeThicknessMm { get; set; }

        public double CompressionForceTf { get; set; }
        public double TensionForceTf { get; set; }
        public double MomentX_TfM { get; set; }
        public double MomentY_TfM { get; set; }
        public double ShearX_Tf { get; set; }
        public double ShearY_Tf { get; set; }

        public double PlateFyMpa { get; set; }
        public double PlateFuMpa { get; set; }
        public double ConcreteFckMpa { get; set; }
        public double ConcreteAreaRatioA2A1 { get; set; }
        public double ConcreteEdgeDistanceXmm { get; set; }
        public double ConcreteEdgeDistanceYmm { get; set; }
        public double AnchorFyMpa { get; set; }
        public double AnchorFuMpa { get; set; }
        public double ColumnFyMpa { get; set; }
        public double ColumnFuMpa { get; set; }

        public double PlateLengthXmm { get; set; }
        public double PlateLengthYmm { get; set; }
        public double PlateThicknessMm { get; set; }

        public double AnchorDiameterMm { get; set; }
        public double AnchorLengthMm { get; set; }
        public double HoleDiameterMm { get; set; }
        public double EmbedmentLengthMm { get; set; }
        public bool HasHook { get; set; }
        public double CorrosionAllowanceMm { get; set; }
        public int TotalAnchors { get; set; }
        public int AnchorsX { get; set; }
        public int AnchorsY { get; set; }
        public double AnchorEdgeDistanceXmm { get; set; }
        public double AnchorEdgeDistanceYmm { get; set; }

        public double StiffenerThicknessMm { get; set; }
        public double StiffenerHeightMm { get; set; }
        public bool HasMiddleStiffener { get; set; }
    }
}
