using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Rail
{
    // Stub v0.1 — travessas e fechamento em quadro serão implementados em v0.2
    public class InfillBuilder
    {
        private readonly Document _doc;
        public InfillBuilder(Document doc) => _doc = doc;

        public void Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd)
        {
            // TODO v0.2: criar travessas horizontais (HorizontalBars) ou quadro (FramePanel)
        }
    }
}
