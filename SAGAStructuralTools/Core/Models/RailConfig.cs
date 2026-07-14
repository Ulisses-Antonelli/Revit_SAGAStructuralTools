using System.Collections.Generic;
using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    public enum DistributionMode     { ByCount, MaxSpan, FixedAxis }
    public enum InfillMode           { HorizontalBars, FramePanel }
    public enum FrameType            { AngleIron, HorizontalOnly }
    public enum FrameAlignment       { ExternalFace, InternalFace, Center }
    public enum HandrailJustification{ Left, Center, Right }
    public enum TerminalType         { Sharp, Rounded }

    public class RailConfig
    {
        // ── Distribuição ────────────────────────────────────────────────
        public DistributionMode DistMode         { get; set; } = DistributionMode.ByCount;
        public int    PostCount                  { get; set; } = RailDefaults.PostCount;
        public double MaxPostSpan                { get; set; } = RailDefaults.MaxPostSpan;
        public double FixedAxisSpacing           { get; set; } = RailDefaults.FixedAxisSpacing;
        public double EndPostInset               { get; set; } = RailDefaults.EndPostInset;

        // ── Montante ─────────────────────────────────────────────────────
        public string PostFamilyPath             { get; set; }
        public string PostFamilyType             { get; set; }
        public double PostRotation               { get; set; } = RailDefaults.PostRotation;
        public double PostTopOffset              { get; set; } = RailDefaults.PostTopOffset;
        public double PostBaseOffset             { get; set; } = RailDefaults.PostBaseOffset;
        public double PostAxisOffset             { get; set; } = RailDefaults.PostAxisOffset;

        // ── Corrimão ─────────────────────────────────────────────────────
        public string HandrailFamilyPath         { get; set; }
        public string HandrailFamilyType         { get; set; }
        public double HandrailRotation           { get; set; } = RailDefaults.HandrailRotation;
        public double HandrailAxisOffset         { get; set; } = RailDefaults.HandrailAxisOffset;
        public HandrailJustification Justification { get; set; } = HandrailJustification.Center;
        public double HandrailHeight             { get; set; } = RailDefaults.HandrailHeight;

        // ── Fechamento ────────────────────────────────────────────────────
        public InfillMode       InfillMode       { get; set; } = InfillMode.HorizontalBars;
        public List<BarConfig>  HorizontalBars   { get; set; } = new List<BarConfig>();
        public BarConfig        HorizontalBarCommon { get; set; } = new BarConfig();  // usado quando SameProfileAll
        public bool             SameProfileAll   { get; set; } = true;
        public bool             EquidistantBars  { get; set; } = true;
        public BarConfig        Rodape           { get; set; } = new BarConfig();
        public FrameType        FrameType        { get; set; } = FrameType.AngleIron;
        public string           FrameFamilyPath  { get; set; }   // perfil HORIZONTAL (topo/base)
        public string           FrameFamilyType  { get; set; }
        public string           FrameVertFamilyPath { get; set; } // perfil VERTICAL (laterais, só AngleIron)
        public string           FrameVertFamilyType { get; set; }
        public FrameAlignment   FrameAlignment   { get; set; } = FrameAlignment.ExternalFace;
        public double           FrameOffset      { get; set; } = RailDefaults.FrameOffset;   // lateral (+ fora / − dentro)
        public double           FrameBaseOffset  { get; set; } = 0.0;                        // desloc. vertical da base
        public double           FrameHeight      { get; set; } = RailDefaults.FrameHeight;
        public double           FrameRotation    { get; set; } = 0.0;                        // rotação base da cantoneira (graus)
        public double           FrameFaceOffset  { get; set; } = 0.0;                        // eixo montante → cantoneira (mm; 0 = auto W/2 medida)

        // ── Terminais ─────────────────────────────────────────────────────
        public TerminalType TerminalType         { get; set; } = TerminalType.Sharp;
        public double       TerminalRadius       { get; set; } = 0.0;
    }
}
