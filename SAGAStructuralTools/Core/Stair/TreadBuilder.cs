using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Cria os degraus como DirectShape (sólido BIM quantificável, sem dependência de família externa).
    /// Cada degrau é uma placa retangular horizontal: Largura × Pisada × Espessura.
    /// </summary>
    public class TreadBuilder
    {
        private readonly Document _doc;
        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_StairLog.txt");

        public TreadBuilder(Document doc) => _doc = doc;

        /// <param name="def">Geometria calculada da escada (RiserHeight, TreadDepth, StepCount).</param>
        /// <param name="config">Configuração do usuário (Width, TreadThickness).</param>
        /// <param name="stringerBottom">Início do trecho inclinado (após patamar inferior).</param>
        /// <param name="horizDir">Direção horizontal unitária da marcha.</param>
        /// <param name="lateral">Direção lateral unitária (perpendicular à marcha, no plano XY).</param>
        public void Build(StairDefinition def, StairConfig config,
                          XYZ stringerBottom, XYZ horizDir, XYZ lateral)
        {
            Log($"  [treads] {def.StepCount} degraus | espessura={config.TreadThickness:F0}mm | largura={config.Width:F0}mm");

            double rFt     = def.RiserHeight      / 304.8;
            double tFt     = def.TreadDepth       / 304.8;
            double halfWFt = config.Width   / 2.0  / 304.8;
            double thickFt = config.TreadThickness / 304.8;

            for (int i = 0; i < def.StepCount; i++)
            {
                // Superfície do degrau: após o (i+1)-ésimo espelho
                double zSurf = stringerBottom.Z + (i + 1) * rFt;
                double zBot  = zSurf - thickFt;

                // O vértice "da frente" deve estar sobre o eixo da longarina
                // (run=(i+1)*tFt → altura exata da superfície do degrau na reta inclinada).
                // O vértice "de trás" avança para dentro, ficando embutido no perfil acima.
                double runFront = (i + 1) * tFt;  // frente: alinhado com a longarina
                double runBack  = (i + 2) * tFt;  // trás: embutido (um passo à frente)

                // Quatro cantos da face inferior do degrau
                var p0 = Pt(stringerBottom, horizDir, lateral, runFront, -halfWFt, zBot);
                var p1 = Pt(stringerBottom, horizDir, lateral, runBack,  -halfWFt, zBot);
                var p2 = Pt(stringerBottom, horizDir, lateral, runBack,  +halfWFt, zBot);
                var p3 = Pt(stringerBottom, horizDir, lateral, runFront, +halfWFt, zBot);

                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));

                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, XYZ.BasisZ, thickFt);

                var shape = DirectShape.CreateElement(
                    _doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.SetShape(new GeometryObject[] { solid });
                shape.SetName($"Degrau {i + 1}");

                Log($"  [tread {i + 1:D2}] z={zSurf * 304.8:F0}mm  run=[frente={runFront * 304.8:F0}–trás={runBack * 304.8:F0}]mm");
            }
        }

        private static XYZ Pt(XYZ o, XYZ dir, XYZ lat, double run, double latOff, double z)
            => new XYZ(
                o.X + dir.X * run + lat.X * latOff,
                o.Y + dir.Y * run + lat.Y * latOff,
                z);

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }
    }
}
