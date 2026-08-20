using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Lê as dimensões de seção de um perfil W (h, bf, tf, tw) e, se disponível,
    /// o raio de concordância mesa-alma — por nome de parâmetro candidato, mesmo
    /// idioma de <see cref="Rail.SectionSize"/> e <see cref="Stair.ProfileGeometryReader"/>.
    /// </summary>
    public static class WProfileDimensions
    {
        private static readonly string[] HeightParams =
            { "Gerdau_d", "Gerdau_h", "d", "h", "H", "Altura", "Height", "Depth" };

        private static readonly string[] WidthParams =
            { "Gerdau_bf", "bf", "b", "B", "b1", "Aba", "FlangeWidth", "Width", "Largura" };

        private static readonly string[] FlangeThicknessParams =
            { "Gerdau_tf", "tf", "t", "Espessura da Mesa", "FlangeThickness", "tf1" };

        private static readonly string[] WebThicknessParams =
            { "Gerdau_tw", "tw", "s", "Espessura da Alma", "WebThickness", "tw1" };

        private static readonly string[] FilletRadiusParams =
            { "Gerdau_r", "Gerdau_k", "r", "r1", "k", "k1", "Raio de Concordância", "FilletRadius" };

        /// <summary>
        /// Tenta ler h, bf, tf e tw (mm). Retorna false se alguma dimensão
        /// obrigatória não foi encontrada — a família não é um W reconhecível.
        /// </summary>
        public static bool TryRead(FamilySymbol symbol, out double hMm, out double bfMm,
                                    out double tfMm, out double twMm, out double? filletMm)
        {
            hMm  = Read(symbol, HeightParams);
            bfMm = Read(symbol, WidthParams);
            tfMm = Read(symbol, FlangeThicknessParams);
            twMm = Read(symbol, WebThicknessParams);

            double fillet = Read(symbol, FilletRadiusParams);
            filletMm = fillet > 1e-6 ? (double?)fillet : null;

            return hMm > 1e-6 && bfMm > 1e-6 && tfMm > 1e-6 && twMm > 1e-6;
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
