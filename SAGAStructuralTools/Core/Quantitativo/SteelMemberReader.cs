using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core.EndPlate;
using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Quantitativo
{
    /// <summary>
    /// Lê uma viga/pilar de aço como uma medição para o quantitativo: descrição,
    /// material e peso nominal vêm do TIPO da família (idealmente já presentes
    /// nela — não inventamos descrição própria); o comprimento vem da geometria
    /// real (face a face), reaproveitando <see cref="MemberOrientationReader"/>
    /// já validado na chapa de topo, em vez do comprimento nominal da linha de
    /// desenho (que pode não bater com peças recortadas/unidas na conexão).
    /// </summary>
    internal static class SteelMemberReader
    {
        // Campos customizados de descrição (Modelo/Model/Type Mark/Descrição)
        // usados só como reforço — na prática se mostraram menos confiáveis
        // que o próprio Nome do Tipo: em duplicações de tipo (comum ao criar
        // uma variante rápida a partir de outra), o Revit exige um Nome do
        // Tipo novo e único, mas parâmetros customizados como "Model" só
        // copiam o valor antigo e ficam esquecidos — já vimos isso causar
        // dois tipos W diferentes lendo a MESMA descrição errada.
        private static readonly string[] DescriptionParams   = { "Modelo", "Model" };
        private static readonly string[] DescriptionFallback = { "Descrição", "Description" };

        private static readonly string[] MaterialParams =
            { "Gerdau_Material", "SGA_MATERIAL", "Material estrutural", "Structural Material", "Material" };

        private static readonly string[] WeightParams =
            { "Peso nominal", "Nominal Weight" };

        /// <summary>
        /// true = elemento é viga/pilar de aço válido para o quantitativo (mesmo
        /// que sem peso nominal — nesse caso <paramref name="warning"/> avisa).
        /// false = elemento foi ignorado silenciosamente (não é viga/pilar de
        /// aço — ex.: chapa, concreto, qualquer outra categoria).
        /// </summary>
        public static bool TryRead(FamilyInstance instance, out QuantitativoMeasurement measurement, out string warning)
        {
            measurement = null;
            warning = null;

            if (instance == null) return false;

            var catId = instance.Category?.Id.GetId();
            bool isMember = catId == (int)BuiltInCategory.OST_StructuralFraming ||
                            catId == (int)BuiltInCategory.OST_StructuralColumns;
            if (!isMember) return false;

            if (instance.StructuralMaterialType != StructuralMaterialType.Steel) return false;

            if (!MemberOrientationReader.TryGetEnds(instance, out var end0, out var end1, out _))
            {
                warning = $"'{instance.Name}': não foi possível medir o comprimento real — ignorado.";
                return false;
            }

            double lengthM = end0.DistanceTo(end1) * 304.8 / 1000.0;

            // Nome do Tipo primeiro: é garantido pelo próprio Revit (único
            // dentro da família, sempre preenchido) — os campos customizados
            // só entram como reforço se ele vier vazio (na prática, nunca).
            string description = instance.Symbol.Name;
            if (string.IsNullOrWhiteSpace(description))
                description = ReadString(instance.Symbol, DescriptionParams)
                    ?? ReadTypeMark(instance.Symbol)
                    ?? ReadString(instance.Symbol, DescriptionFallback)
                    ?? "(sem descrição)";
            string material     = ReadString(instance.Symbol, MaterialParams) ?? "(sem material)";
            double? unitWeight   = ReadWeightKgM(instance.Symbol);

            measurement = new QuantitativoMeasurement
            {
                Description   = description,
                Material      = material,
                LengthM       = lengthM,
                UnitWeightKgM = unitWeight
            };

            if (!unitWeight.HasValue)
                warning = $"'{description}': o tipo não tem peso nominal cadastrado — entra na lista sem peso.";

            return true;
        }

        private static string ReadString(FamilySymbol sym, string[] names)
        {
            if (sym == null) return null;
            foreach (var name in names)
            {
                var p = sym.LookupParameter(name);
                if (p?.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString()))
                    return p.AsString();
            }
            return null;
        }

        // BuiltInParameter em vez do nome de exibição ("Type Mark"/"Marca do
        // Tipo") — independe do idioma da interface do Revit.
        private static string ReadTypeMark(FamilySymbol sym)
        {
            var p = sym?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            return p?.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString())
                ? p.AsString()
                : null;
        }

        private static double? ReadWeightKgM(FamilySymbol sym)
        {
            if (sym == null) return null;
            foreach (var name in WeightParams)
            {
                var p = sym.LookupParameter(name);
                if (p?.StorageType == StorageType.Double && p.AsDouble() > 1e-9)
                    return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.KilogramsForcePerMeter);
            }
            return null;
        }
    }
}
