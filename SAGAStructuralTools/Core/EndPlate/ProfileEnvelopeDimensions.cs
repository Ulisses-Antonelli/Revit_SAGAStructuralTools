using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Lê largura × altura do ENVELOPE de qualquer perfil estrutural — é só o
    /// que a chapa de topo precisa (um retângulo), diferente do
    /// <see cref="Stiffener.WProfileDimensions"/> da nervura, que exige mesa e
    /// alma separadas (h, bf, tf, tw) porque encaixa a chapa DENTRO do perfil.
    /// Tenta, em ordem: W/U (h × bf) → cantoneira (aba1 × aba2) → tubo
    /// (diâmetro × diâmetro).
    /// </summary>
    public static class ProfileEnvelopeDimensions
    {
        // W/U — altura total e largura da mesa.
        private static readonly string[] HeightParams =
            { "Gerdau_d", "Gerdau_h", "d", "h", "H", "Altura", "Height", "Depth" };
        private static readonly string[] WidthParams =
            { "Gerdau_bf", "bf", "b", "B", "b1", "Aba", "FlangeWidth", "Width", "Largura" };

        // Cantoneira (L) — duas abas, sem mesa/alma separadas.
        private static readonly string[] Leg1Params =
            { "Gerdau_a", "a", "Aba1", "Aba 1", "L1", "Leg1", "Perna1" };
        private static readonly string[] Leg2Params =
            { "Gerdau_b_L", "b2", "Aba2", "Aba 2", "L2", "Leg2", "Perna2" };

        // Tubo (redondo) — diâmetro vira envelope quadrado D×D.
        private static readonly string[] DiameterParams =
            { "Gerdau_D", "D", "Diâmetro", "Diametro", "Diameter", "OD", "DN", "Ø" };

        public static bool TryRead(FamilySymbol symbol, out double heightMm, out double widthMm)
        {
            heightMm = Read(symbol, HeightParams);
            widthMm  = Read(symbol, WidthParams);
            if (heightMm > 1e-6 && widthMm > 1e-6) return true;

            double leg1 = Read(symbol, Leg1Params);
            double leg2 = Read(symbol, Leg2Params);
            if (leg1 > 1e-6 && leg2 > 1e-6)
            {
                heightMm = leg1;
                widthMm  = leg2;
                return true;
            }

            double diameter = Read(symbol, DiameterParams);
            if (diameter > 1e-6)
            {
                heightMm = diameter;
                widthMm  = diameter;
                return true;
            }

            return false;
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
