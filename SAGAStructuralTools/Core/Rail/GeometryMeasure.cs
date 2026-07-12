using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Mede a geometria REAL de uma instância projetando seus vértices numa direção.
    /// Diferente da BoundingBox alinhada ao mundo, isto é exato em qualquer ângulo (linha
    /// diagonal) e reflete a seção já rotacionada — base para posicionamento por vetor global.
    /// </summary>
    internal static class GeometryMeasure
    {
        /// <summary>Extensão (mm) da geometria da instância projetada em <paramref name="unitDir"/>.</summary>
        public static double ExtentAlongMm(Document doc, ElementId id, XYZ unitDir)
        {
            var geom = doc.GetElement(id)?.get_Geometry(new Options());
            if (geom == null) return 0;

            double min = double.MaxValue, max = double.MinValue;
            foreach (var v in Vertices(geom))
            {
                double p = v.DotProduct(unitDir);
                if (p < min) min = p;
                if (p > max) max = p;
            }
            return max >= min ? (max - min) * 304.8 : 0;
        }

        private static IEnumerable<XYZ> Vertices(GeometryElement geom)
        {
            foreach (var obj in geom)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    // Tessela cada aresta (não só as pontas): arestas CURVAS de perfis
                    // redondos (tubo) só têm o ponto extremo no meio do arco, que não é
                    // vértice. Tessellate devolve pontos ao longo da curva, capturando o Ø.
                    foreach (Edge e in solid.Edges)
                        foreach (var p in e.Tessellate())
                            yield return p;
                }
                else if (obj is GeometryInstance gi)
                {
                    foreach (var v in Vertices(gi.GetInstanceGeometry()))
                        yield return v;
                }
            }
        }
    }
}
