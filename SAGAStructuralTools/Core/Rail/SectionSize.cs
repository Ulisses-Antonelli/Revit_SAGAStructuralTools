using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Lê dimensões da seção transversal de um FamilySymbol por nome de parâmetro.
    /// Cobre perfis abertos (bf/b, altura d/h) e tubulares (diâmetro D).
    ///
    /// É baseado em nomes de parâmetro (padrão do projeto, ver ProfileGeometryReader) —
    /// portanto depende da família expor um parâmetro de dimensão com nome conhecido.
    /// Quando não encontra, retorna 0 e o chamador degrada graciosamente (sem recuo /
    /// topo no eixo do corrimão), sem quebrar a criação.
    /// </summary>
    public static class SectionSize
    {
        private static readonly string[] DiameterParams =
            { "Gerdau_D", "D", "Diâmetro", "Diametro", "Diameter", "OD", "DN", "Ø" };

        private static readonly string[] HeightParams =
            { "Gerdau_d", "Gerdau_h", "d", "h", "H", "Altura", "Height", "Depth", "Profundidade" };

        private static readonly string[] WidthParams =
            { "Gerdau_bf", "bf", "b", "B", "b1", "Aba", "FlangeWidth", "Width", "Largura" };

        /// <summary>Largura da seção (mm) — perpendicular ao caminho. 0 se não encontrada.</summary>
        public static double SectionWidthMm(FamilySymbol sym)
        {
            var w = Read(sym, WidthParams);        // perfis abertos: bf/b
            if (w > 1e-6) return w;
            return Read(sym, DiameterParams);      // tubular: diâmetro
        }

        /// <summary>Altura da seção (mm) — vertical. 0 se não encontrada.</summary>
        public static double SectionHeightMm(FamilySymbol sym)
        {
            var d = Read(sym, DiameterParams);     // tubular: diâmetro = altura
            if (d > 1e-6) return d;
            return Read(sym, HeightParams);        // perfis abertos: d/h
        }

        private static double Read(FamilySymbol sym, string[] names)
        {
            if (sym == null) return 0;
            foreach (var name in names)
            {
                var p = sym.LookupParameter(name);
                if (p?.StorageType == StorageType.Double && p.AsDouble() > 1e-6)
                    return p.AsDouble() * 304.8; // pés → mm
            }
            return 0;
        }
    }
}
