using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    public class RailSegment
    {
        public ElementId    LineId        { get; set; }
        public int          Index         { get; set; }
        public double       Length        { get; set; }  // mm

        // Preenchido por RailCalculator: distâncias desde o Start do segmento (mm)
        public List<double> PostOffsets   { get; set; } = new List<double>();
        public double       ActualSpacing { get; set; }  // mm entre eixos
        public int          PostCount     => PostOffsets?.Count ?? 0;

        public string DisplayText => $"{Index + 1}  —  comprimento: {Length:F0} mm";
    }
}
