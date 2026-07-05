using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Resultado imutável do cálculo geométrico da escada.
    /// Não depende da Revit API — totalmente testável de forma isolada.
    /// </summary>
    public class StairDefinition
    {
        public int    StepCount            { get; set; }
        public double RiserHeight          { get; set; }  // mm — calculado
        public double TreadDepth           { get; set; }  // mm — conforme config
        public double InclinationDeg       { get; set; }  // graus
        public double TotalRun             { get; set; }  // mm — desenvolvimento horizontal (sem ILL)
        public double TotalRise            { get; set; }  // mm — desnível entre vigas
        public double BeamDistance         { get; set; }  // mm — distância entre vigas
        public double LowerLandingDepth    { get; set; }  // mm
        public double UpperLandingDepth    { get; set; }  // mm
        public bool   HasIntermediateLanding { get; set; }
        public int    IntermediateLandingStep  { get; set; } // último degrau da marcha inferior (1-indexed)
        public double IntermediateLandingLength { get; set; } // mm
        public double IntermediateLandingAt    { get; set; } // mm desde a base (altura da superfície do patamar)
        public string HasIntermediateLandingText => HasIntermediateLanding ? "Sim" : "Não";
        public List<string> Warnings       { get; set; } = new List<string>();
        public bool   IsValid              { get; set; } = true;
    }
}
