using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    public class BridgeTriangle
    {
        public double[] v0 { get; set; }
        public double[] v1 { get; set; }
        public double[] v2 { get; set; }
    }

    public class BridgeItem
    {
        public string origem { get; set; }
        public string item { get; set; }
        public string unidade { get; set; }
        public List<BridgeTriangle> malha { get; set; } = new List<BridgeTriangle>();
    }
}
