using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>Extremidade resolvida de uma peça (viga ou pilar) que recebe uma chapa.</summary>
    public class EndPlateMemberEnd
    {
        public ElementId ElementId { get; set; }
        public string    Name      { get; set; }

        public XYZ EndPoint  { get; set; }   // face de corte na extremidade
        public XYZ AxisDir   { get; set; }   // pra FORA da peça — sentido de extrusão da chapa
        public XYZ WidthDir  { get; set; }
        public XYZ HeightDir { get; set; }

        public double HeightMm { get; set; } // h do perfil
        public double WidthMm  { get; set; } // bf do perfil

        public bool IsValid =>
            ElementId != null && EndPoint != null && AxisDir != null &&
            WidthDir != null && HeightDir != null && HeightMm > 0 && WidthMm > 0;
    }

    /// <summary>
    /// Uma peça (Modo 1) ou duas peças na interface de contato (Modo 2, ordem
    /// preservada: Members[0] = primeira selecionada).
    /// </summary>
    public class EndPlatePlacement
    {
        public List<EndPlateMemberEnd> Members { get; } = new List<EndPlateMemberEnd>();

        public bool IsValid => Members.Count > 0 && Members.All(m => m.IsValid);
    }
}
