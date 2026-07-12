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

        // Preenchido por PostBuilder: posições reais dos eixos após o recuo de W/2 nas pontas
        // (L_eixos = L − W). InfillBuilder usa estes para alinhar travessas/quadros aos montantes.
        public List<double> AxisOffsets   { get; set; }

        public string DisplayText => $"{Index + 1}  —  comprimento: {Length:F0} mm";
    }
}
