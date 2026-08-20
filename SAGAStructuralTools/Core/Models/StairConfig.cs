using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    public class StairConfig
    {
        public double Width                     { get; set; } = StairDefaults.Width;
        public double TreadDepth                { get; set; } = StairDefaults.TreadDepth;
        public double TreadThickness            { get; set; } = StairDefaults.TreadThickness;
        public bool   ApplyBlondel              { get; set; } = StairDefaults.ApplyBlondel;
        public bool   CenterStair               { get; set; } = StairDefaults.CenterStair;
        public bool   HasIntermediateLanding    { get; set; } = StairDefaults.HasIntermediateLanding;
        public double IntermediateLandingLength { get; set; } = StairDefaults.IntermediateLandingLength;
        public double MaxHeightWithoutLanding   { get; set; } = StairDefaults.MaxHeightWithoutLanding;
        public bool   IncludeTreads             { get; set; } = StairDefaults.IncludeTreads;
        public string StringerFamilyPath        { get; set; }
        public string StringerFamilyType        { get; set; }

        // Só relevante para perfis U/Canal.
        // false (padrão): Largura Útil = distância livre entre as faces externas das almas.
        // true           : Largura Útil = distância entre os eixos neutros (centroide-a-centroide).
        public bool   UseAxis                   { get; set; } = false;

        // Desloca o conjunto inteiro (as duas vigas de conexão) perpendicularmente ao
        // sentido do lance, a partir do eixo de uma viga/pilar de referência escolhido
        // na hora de gerar — sem referência, funciona como um ajuste manual simples.
        public double LateralOffsetMm           { get; set; } = StairDefaults.LateralOffsetMm;
    }
}
