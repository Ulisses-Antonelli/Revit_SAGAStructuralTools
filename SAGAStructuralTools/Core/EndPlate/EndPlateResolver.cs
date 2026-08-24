using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Resolve qual extremidade de uma peça recebe a chapa e o referencial local
    /// dessa extremidade. Usado tanto no Modo 1 (peça única) quanto no Modo 2
    /// (duas peças) — a única diferença entre os modos é o que se passa como
    /// <paramref name="proximityPoint"/>: o próprio clique (Modo 1, viga) ou um
    /// ponto da outra peça (Modo 2).
    /// </summary>
    internal static class EndPlateResolver
    {
        public static bool TryResolve(FamilyInstance instance, XYZ proximityPoint,
                                      out EndPlateMemberEnd end, out string error)
        {
            end = null;
            error = null;

            if (instance == null)
            {
                error = "Elemento inválido.";
                return false;
            }

            if (!ProfileEnvelopeDimensions.TryRead(instance.Symbol, out var hMm, out var bfMm))
            {
                error = $"'{instance.Name}' não expõe dimensões reconhecidas (altura/largura, abas ou diâmetro).";
                return false;
            }

            if (!MemberOrientationReader.TryGetEnds(instance, out var end0, out var end1, out var rawAxis))
            {
                error = $"Não foi possível localizar a geometria de '{instance.Name}'.";
                return false;
            }

            // Qual extremidade recebe a chapa é sempre decidido pela referência
            // de proximidade — o próprio clique (Modo 1) ou a extremidade já
            // resolvida da outra peça (Modo 2) — não importa o tipo de
            // elemento. Antes isso era forçado pra "sempre topo" em pilares,
            // mas isso tirava a possibilidade de escolher a base.
            bool useEnd1 = proximityPoint == null
                ? true
                : end1.DistanceTo(proximityPoint) <= end0.DistanceTo(proximityPoint);

            var endPoint = useEnd1 ? end1 : end0;
            var outward  = useEnd1 ? rawAxis : rawAxis.Negate();

            if (!MemberOrientationReader.TryGetCrossSectionFrame(instance, rawAxis, out var widthDir, out var heightDir))
            {
                error = $"Não é possível orientar a chapa em '{instance.Name}' (seção degenerada).";
                return false;
            }

            // A linha/ponto de desenho não é o centro real da seção (Justificação
            // y/z) — recentra achando uma face real de cada lado (largura e
            // altura) e andando metade da dimensão CONHECIDA do perfil pra
            // dentro, em vez de confiar na linha ou na bounding box inteira.
            endPoint = MemberOrientationReader.RecenterOnCrossSection(
                instance, endPoint, widthDir, bfMm, heightDir, hMm);

            end = new EndPlateMemberEnd
            {
                ElementId  = instance.Id,
                Name       = instance.Name,
                EndPoint   = endPoint,
                AxisDir    = outward,
                WidthDir   = widthDir,
                HeightDir  = heightDir,
                HeightMm   = hMm,
                WidthMm    = bfMm
            };
            return true;
        }
    }
}
