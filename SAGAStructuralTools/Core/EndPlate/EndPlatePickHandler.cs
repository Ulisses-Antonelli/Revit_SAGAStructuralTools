using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Modo 1 — peça única: clique numa viga ou pilar. Em pilar (LocationPoint),
    /// a chapa nasce sempre no topo. Em viga, o ponto do clique escolhe qual das
    /// duas extremidades recebe a chapa.
    /// </summary>
    public class EndPlatePickHandler : IExternalEventHandler
    {
        public event Action<EndPlatePlacement> MemberPicked;
        public event Action<string>            PickFailed;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc.Document;

                var reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new EndPlateMemberFilter(),
                    "Clique na viga ou pilar que vai receber a chapa de topo (ESC para cancelar).");

                var instance = doc.GetElement(reference.ElementId) as FamilyInstance;
                if (!EndPlateResolver.TryResolve(instance, reference.GlobalPoint, out var end, out var error))
                {
                    PickFailed?.Invoke(error);
                    return;
                }

                var placement = new EndPlatePlacement();
                placement.Members.Add(end);
                MemberPicked?.Invoke(placement);
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

        public string GetName() => "SAGAPickEndPlateMember";
    }
}
