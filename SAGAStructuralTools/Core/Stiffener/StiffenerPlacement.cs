using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Resultado da seleção: viga W escolhida, ponto de inserção ao longo do
    /// eixo e o referencial local da seção transversal naquele ponto.
    /// </summary>
    public class StiffenerPlacement
    {
        public ElementId BeamId   { get; set; }
        public string    BeamName { get; set; }

        public XYZ InsertionPoint { get; set; }   // no eixo da viga, projeção do clique
        public XYZ AxisDir        { get; set; }   // ao longo da viga
        public XYZ Up             { get; set; }   // altura da seção (h), perpendicular ao eixo
        public XYZ Lateral        { get; set; }   // largura da seção (bf), perpendicular ao eixo

        /// <summary>+1 ou -1 — lado da alma onde o usuário clicou (usado quando não simétrico).</summary>
        public double PreferredSide { get; set; }

        public double  HeightMm  { get; set; }
        public double  WidthMm   { get; set; }
        public double  FlangeThicknessMm { get; set; }
        public double  WebThicknessMm    { get; set; }
        public double? FilletRadiusMm    { get; set; }

        public bool IsValid =>
            InsertionPoint != null && AxisDir != null && Up != null && Lateral != null &&
            HeightMm > 0 && WidthMm > 0 && FlangeThicknessMm > 0 && WebThicknessMm > 0;
    }
}
