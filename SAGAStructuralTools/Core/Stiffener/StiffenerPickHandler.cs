using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Seleção em um único clique: o usuário clica na viga W, no ponto ao longo
    /// do eixo onde a nervura deve ficar. O ponto do clique também define, pelo
    /// lado da alma em que caiu, o lado preferencial (usado quando a nervura não
    /// é simétrica). Mesmo padrão de <see cref="Ladder.LadderPickHandler"/>.
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
                    new StructuralFramingFilter(),
                    "Clique na viga W, no ponto ao longo do eixo onde a nervura será inserida (ESC para cancelar).");

                var beam       = doc.GetElement(reference.ElementId) as FamilyInstance;
                var clickPoint = reference.GlobalPoint;

                if (beam == null || !(beam.Location is LocationCurve lc) || !(lc.Curve is Line axis))
                {
                    PickFailed?.Invoke("A viga selecionada não possui um eixo reto válido.");
                    return;
                }

                if (!WProfileDimensions.TryRead(beam.Symbol, out var hMm, out var bfMm,
                        out var tfMm, out var twMm, out var filletMm))
                {
                    PickFailed?.Invoke(
                        "O perfil selecionado não expõe as dimensões (h, bf, tf, tw) esperadas de um perfil W. " +
                        "Verifique os parâmetros de tipo da família.");
                    return;
                }

                var axisDir = (axis.GetEndPoint(1) - axis.GetEndPoint(0)).Normalize();

                // "Up" = vertical do mundo projetada no plano perpendicular ao eixo —
                // a altura (h) do perfil segue a alma vertical, mesma convenção usada
                // no restante do projeto (escada/gaiola) para vigas W horizontais.
                var upRaw = XYZ.BasisZ - axisDir * axisDir.DotProduct(XYZ.BasisZ);
                if (upRaw.GetLength() < 1e-6)
                {
                    PickFailed?.Invoke("Não é possível orientar a nervura em elementos verticais (pilares).");
                    return;
                }
                var up      = upRaw.Normalize();
                var lateral = up.CrossProduct(axisDir).Normalize();

                var projection = axis.Project(clickPoint);
                var insertion  = projection?.XYZPoint ?? clickPoint;

                // O LocationCurve é a linha de referência/analítica da viga, não o
                // centro geométrico da seção — a posição real depende da Justificação
                // z (Origem/Topo/Centro/Base) com que a viga foi desenhada. Usamos o
                // centro vertical da bounding box real (mesma técnica já usada em
                // LadderPickHandler para achar o topo da viga) para que a nervura
                // fique centrada no vão livre de verdade, e não na linha de desenho.
                var bb = beam.get_BoundingBox(null);
                if (bb == null)
                {
                    PickFailed?.Invoke("Não foi possível ler a geometria da viga selecionada.");
                    return;
                }
                double centerZ = (bb.Max.Z + bb.Min.Z) / 2.0;
                insertion = new XYZ(insertion.X, insertion.Y, centerZ);

                var offset       = clickPoint - insertion;
                double lateralComp = offset.DotProduct(lateral);
                double preferredSide = Math.Abs(lateralComp) > 1e-6 ? Math.Sign(lateralComp) : 1.0;

                PlacementPicked?.Invoke(new StiffenerPlacement
                {
                    BeamId          = beam.Id,
                    BeamName        = beam.Name,
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
                PickFailed?.Invoke($"Falha na seleção da viga: {ex.Message}");
            }
        }

        public string GetName() => "SAGAPickStiffenerBeam";

        private sealed class StructuralFramingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.GetId() == (int)BuiltInCategory.OST_StructuralFraming;

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
