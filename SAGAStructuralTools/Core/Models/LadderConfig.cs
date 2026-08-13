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

        // Quando true, o perfil do montante (Stringer) é usado também para
        // suporte, anel e barra vertical da gaiola (degrau nunca entra nesse
        // agrupamento). Tem prioridade sobre SameProfileCage.
        public bool   SameProfileAll      { get; set; }

        // ── Suportes de fixação ───────────────────────────────────────────
        public string SupportFamilyPath   { get; set; }
        public string SupportFamilyType   { get; set; }
        public double SupportMaxSpacing   { get; set; } = LadderDefaults.SupportMaxSpacing;   // vão máximo entre suportes

        // Quando true (e SameProfileAll = false), o perfil do suporte é usado
        // também para o anel e a barra vertical da gaiola.
        public bool   SameProfileCage     { get; set; }

        // ── Gaiola de proteção ────────────────────────────────────────────
        public bool   HasCage             { get; set; }
        public double CageStartHeight     { get; set; } = LadderDefaults.CageStartHeight;     // base → primeiro anel
        public double CageProjection      { get; set; } = LadderDefaults.CageProjection;      // eixo do degrau → fundo do arco
        public double RingSetback         { get; set; } = LadderDefaults.RingSetback;         // afastamento do anel em relação ao montante
        public RingDistribution RingMode  { get; set; } = RingDistribution.Equidistant;
        public double RingSpacing         { get; set; } = LadderDefaults.RingSpacing;         // usado nos dois modos
        public string RingFamilyPath      { get; set; }
        public string RingFamilyType      { get; set; }
        public double StrapAngleStepDeg   { get; set; } = LadderDefaults.StrapAngleStepDeg;   // espaçamento angular entre barras verticais
        public int    StrapCount          { get; set; } = LadderDefaults.StrapCount;          // teto de barras verticais (a partir do ápice)
        public string StrapFamilyPath     { get; set; }
        public string StrapFamilyType     { get; set; }

        // ── Linha de vida ─────────────────────────────────────────────────
        public bool   HasLifeline         { get; set; }
        public string LifelineFamilyPath  { get; set; }
        public string LifelineFamilyType  { get; set; }

        // ── Desembarque (prolongamento + alargamento) ─────────────────────
        public double ExtensionHeight     { get; set; } = LadderDefaults.ExtensionHeight;     // acima do desembarque (trecho quebrado + reto)
        public double ExitFlare           { get; set; } = LadderDefaults.ExitFlare;           // abertura por lado
        public double ExitKinkHeight      { get; set; } = LadderDefaults.ExitKinkHeight;       // altura do trecho quebrado (base do alargamento)
    }
}
