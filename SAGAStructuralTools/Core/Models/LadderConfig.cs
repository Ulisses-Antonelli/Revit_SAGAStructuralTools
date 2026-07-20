using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    public enum RingDistribution { Equidistant, FixedSpacing }

    /// <summary>
    /// Configuração completa de uma escada marinheiro. Todas as cotas em milímetros.
    /// Serializável por XmlSerializer (presets e ExtensibleStorage do conjunto lógico).
    /// </summary>
    public class LadderConfig
    {
        // ── Globais / referência superior ─────────────────────────────────
        public double TopLevelOffset      { get; set; } = LadderDefaults.TopLevelOffset;      // sobre a face superior da viga
        public double BaseOffset          { get; set; } = LadderDefaults.BaseOffset;          // sobre o nível-base
        public double WallOffset          { get; set; } = LadderDefaults.WallOffset;          // face da viga → eixo do degrau

        // ── Corpo (montantes e degraus) ───────────────────────────────────
        public double Width               { get; set; } = LadderDefaults.Width;               // largura útil face a face
        public double RungSpacing         { get; set; } = LadderDefaults.RungSpacing;         // passo vertical
        public string StringerFamilyPath  { get; set; }
        public string StringerFamilyType  { get; set; }
        public string RungFamilyPath      { get; set; }
        public string RungFamilyType      { get; set; }

        // ── Suportes de fixação ───────────────────────────────────────────
        public string SupportFamilyPath   { get; set; }
        public string SupportFamilyType   { get; set; }
        public double SupportMaxSpacing   { get; set; } = LadderDefaults.SupportMaxSpacing;   // vão máximo entre suportes

        // ── Gaiola de proteção ────────────────────────────────────────────
        public bool   HasCage             { get; set; }
        public double CageStartHeight     { get; set; } = LadderDefaults.CageStartHeight;     // base → primeiro anel
        public double CageProjection      { get; set; } = LadderDefaults.CageProjection;      // eixo do degrau → fundo do arco
        public RingDistribution RingMode  { get; set; } = RingDistribution.Equidistant;
        public double RingSpacing         { get; set; } = LadderDefaults.RingSpacing;         // usado nos dois modos
        public string RingFamilyPath      { get; set; }
        public string RingFamilyType      { get; set; }
        public int    StrapCount          { get; set; } = LadderDefaults.StrapCount;          // barras verticais da gaiola
        public string StrapFamilyPath     { get; set; }
        public string StrapFamilyType     { get; set; }

        // ── Linha de vida ─────────────────────────────────────────────────
        public bool   HasLifeline         { get; set; }
        public string LifelineFamilyPath  { get; set; }
        public string LifelineFamilyType  { get; set; }

        // ── Desembarque (prolongamento + alargamento) ─────────────────────
        public double ExtensionHeight     { get; set; } = LadderDefaults.ExtensionHeight;     // acima do desembarque
        public double ExitFlare           { get; set; } = LadderDefaults.ExitFlare;           // abertura por lado
    }
}
