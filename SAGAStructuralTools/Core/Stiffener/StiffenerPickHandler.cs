using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core.EndPlate;
using System;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Seleção em um único clique: o usuário clica na viga ou pilar W, no ponto
    /// ao longo do eixo onde a nervura deve ficar. O ponto do clique também
    /// define, pelo lado da alma em que caiu, o lado preferencial (usado quando
    /// a nervura não é simétrica). Mesmo padrão de <see cref="Ladder.LadderPickHandler"/>.
    ///
    /// Reaproveita <see cref="MemberOrientationReader"/> (criado pra chapa de
    /// topo) pra achar eixo/referencial em qualquer orientação — inclusive
    /// pilar vertical, que antes era rejeitado aqui.
    /// </summary>
    public class StiffenerPickHandler : IExternalEventHandler
    {
        public event Action<StiffenerPlacement> PlacementPicked;
        public event Action<string>             PickFailed;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc.Document;

                var reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new EndPlateMemberFilter(),
                    "Clique na viga ou pilar W, no ponto ao longo do eixo onde a nervura será inserida (ESC para cancelar).");

                var member     = doc.GetElement(reference.ElementId) as FamilyInstance;
                var clickPoint = reference.GlobalPoint;

                if (member == null)
                {
                    PickFailed?.Invoke("Elemento inválido.");
                    return;
                }

                if (!WProfileDimensions.TryRead(member.Symbol, out var hMm, out var bfMm,
                        out var tfMm, out var twMm, out var filletMm))
                {
                    PickFailed?.Invoke(
                        "O perfil selecionado não expõe as dimensões (h, bf, tf, tw) esperadas de um perfil W. " +
                        "Verifique os parâmetros de tipo da família.");
                    return;
                }

                if (!MemberOrientationReader.TryGetEnds(member, out var end0, out var end1, out var axisDir))
                {
                    PickFailed?.Invoke("Não foi possível localizar a geometria da peça selecionada.");
                    return;
                }

                if (!MemberOrientationReader.TryGetCrossSectionFrame(member, axisDir, out var lateral, out var up))
                {
                    PickFailed?.Invoke("Não é possível orientar a nervura nessa peça (seção degenerada).");
                    return;
                }

                var axisLine    = Line.CreateBound(end0, end1);
                var projection  = axisLine.Project(clickPoint);
                var insertionRaw = projection?.XYZPoint ?? clickPoint;

                // A linha/ponto de desenho não é o centro real da seção
                // (Justificação y/z) — recentra achando uma face real de cada
                // lado (largura e altura) e andando metade da dimensão
                // conhecida do perfil pra dentro, mesma técnica da chapa de topo.
                var insertion = MemberOrientationReader.RecenterOnCrossSection(
                    member, insertionRaw, lateral, bfMm, up, hMm);

                var offset          = clickPoint - insertion;
                double lateralComp  = offset.DotProduct(lateral);
                double preferredSide = Math.Abs(lateralComp) > 1e-6 ? Math.Sign(lateralComp) : 1.0;

                PlacementPicked?.Invoke(new StiffenerPlacement
                {
                    BeamId          = member.Id,
                    BeamName        = member.Name,
                    InsertionPoint  = insertion,
                    AxisDir         = axisDir,
                    Up              = up,
                    Lateral         = lateral,
                    PreferredSide   = preferredSide,
                    HeightMm        = hMm,
                    WidthMm         = bfMm,
                    FlangeThicknessMm = tfMm,
                    WebThicknessMm    = twMm,
                    FilletRadiusMm    = filletMm
                });
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC — fluxo normal, nenhuma ação.
            }
            catch (Exception ex)
            {
                PickFailed?.Invoke($"Falha na seleção da peça: {ex.Message}");
            }
        }

        public string GetName() => "SAGAPickStiffenerBeam";
    }
}
