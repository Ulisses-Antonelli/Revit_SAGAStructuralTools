using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core.Ladder
{
    /// <summary>
    /// Referencial de implantação da escada marinheiro obtido no UC-01:
    /// ponto de inserção no eixo da viga superior, lado de instalação (lateral),
    /// elevação do desembarque (face superior da viga) e do nível-base.
    /// Coordenadas internas do Revit (pés); larguras em milímetros.
    /// </summary>
    public class LadderPlacement
    {
        public XYZ    InsertionPoint  { get; set; }   // projeção do clique no eixo da viga
        public XYZ    Lateral         { get; set; }   // unitário horizontal: viga → escada
        public double TopZFt          { get; set; }   // face superior da viga (desembarque)
        public double BaseZFt         { get; set; }   // elevação do nível-base
        public double BeamHalfWidthMm { get; set; }   // eixo da viga → face (medida real)

        public string    BeamName  { get; set; }
        public string    LevelName { get; set; }
        public ElementId BeamId    { get; set; }

        public double HeightMm => (TopZFt - BaseZFt) * 304.8;

        public bool IsValid =>
            InsertionPoint != null && Lateral != null &&
            Lateral.GetLength() > 0.9 && TopZFt > BaseZFt;

        public string Summary =>
            !IsValid
                ? "Nenhuma referência selecionada."
                : $"Viga '{BeamName}'  ·  desembarque em {TopZFt * 304.8:F0} mm  ·  " +
                  $"base '{LevelName}' ({BaseZFt * 304.8:F0} mm)  ·  altura {HeightMm:F0} mm";
    }
}
