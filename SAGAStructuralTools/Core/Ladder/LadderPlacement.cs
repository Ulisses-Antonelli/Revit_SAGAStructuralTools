using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Ladder
{
    /// <summary>
    /// Um nível do documento disponível para o usuário escolher manualmente como
    /// base da escada (modo "Nível existente"), evitando depender só da heurística
    /// automática de LadderPickHandler.
    /// </summary>
    public class LadderLevelOption
    {
        public string Name        { get; set; }
        public double ElevationFt { get; set; }
    }

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

        // Todos os níveis do documento, pra permitir escolha manual na UI (modo
        // "Nível existente"). Nulo/vazio no contexto de edição de escadas antigas.
        public List<LadderLevelOption> AvailableLevels { get; set; }

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
