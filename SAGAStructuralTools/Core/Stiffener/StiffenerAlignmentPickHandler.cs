using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Seleção opcional de uma referência de alinhamento (pilar ou linha) — o
    /// ponto clicado numa face plana perpendicular ao eixo da viga (ex.: a mesa
    /// de um pilar W apoiado sobre a viga) já é, por si só, a posição dessa face
    /// ao longo do eixo: não precisa de nenhuma outra geometria do elemento.
    /// A projeção no eixo e o cálculo de qual lado da chapa encosta na face
    /// ficam a cargo do <see cref="StiffenerBuilder"/>, de forma reativa à
    /// espessura configurada.
    /// </summary>
    public class StiffenerAlignmentPickHandler : IExternalEventHandler
    {
        public event Action<XYZ, string> ReferencePicked;
        public event Action<string>      PickFailed;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;

                var reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new AlignmentReferenceFilter(),
                    "Clique na FACE do pilar (ou numa linha) com a qual a nervura deve ficar alinhada (ESC para cancelar).");

                var element = uidoc.Document.GetElement(reference.ElementId);
                ReferencePicked?.Invoke(reference.GlobalPoint, element?.Name ?? "(sem nome)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC — fluxo normal, nenhuma ação.
            }
            catch (Exception ex)
            {
                PickFailed?.Invoke($"Falha na seleção da referência de alinhamento: {ex.Message}");
            }
        }

        public string GetName() => "SAGAPickStiffenerAlignmentReference";

        private sealed class AlignmentReferenceFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element is CurveElement) return true;
                var catId = element?.Category?.Id.GetId();
                return catId == (int)BuiltInCategory.OST_StructuralColumns ||
                       catId == (int)BuiltInCategory.OST_StructuralFraming;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
