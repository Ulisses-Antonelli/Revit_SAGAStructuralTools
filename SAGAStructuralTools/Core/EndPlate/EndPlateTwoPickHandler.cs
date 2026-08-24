using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Modo 2 — duas peças (ex.: pilar e viga que se encontram): a chapa nasce
    /// na extremidade da PRIMEIRA peça mais próxima da segunda (ordem
    /// preservada). Se configurado, a segunda peça também recebe sua própria
    /// chapa na extremidade mais próxima da primeira — mesma interface de
    /// contato, dos dois lados.
    /// </summary>
    public class EndPlateTwoPickHandler : IExternalEventHandler
    {
        public event Action<EndPlatePlacement> MembersPicked;
        public event Action<string>            PickFailed;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc.Document;
                var filter = new EndPlateMemberFilter();

                var refA = uidoc.Selection.PickObject(
                    ObjectType.Element, filter,
                    "Clique na PRIMEIRA peça (a que recebe a chapa) — ESC para cancelar.");
                var instanceA = doc.GetElement(refA.ElementId) as FamilyInstance;

                var refB = uidoc.Selection.PickObject(
                    ObjectType.Element, filter,
                    "Clique na SEGUNDA peça (referência de contato) — ESC para cancelar.");
                var instanceB = doc.GetElement(refB.ElementId) as FamilyInstance;

                if (instanceA != null && instanceB != null && instanceA.Id == instanceB.Id)
                {
                    PickFailed?.Invoke("Selecione duas peças diferentes.");
                    return;
                }

                if (!EndPlateResolver.TryResolve(instanceA, refB.GlobalPoint, out var endA, out var errorA))
                {
                    PickFailed?.Invoke(errorA);
                    return;
                }

                // B usa a extremidade JÁ resolvida de A como referência de
                // proximidade — mais preciso que o clique bruto, é o ponto real
                // da interface de contato.
                if (!EndPlateResolver.TryResolve(instanceB, endA.EndPoint, out var endB, out var errorB))
                {
                    PickFailed?.Invoke(errorB);
                    return;
                }

                var placement = new EndPlatePlacement();
                placement.Members.Add(endA);
                placement.Members.Add(endB);
                MembersPicked?.Invoke(placement);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC — fluxo normal, nenhuma ação.
            }
            catch (Exception ex)
            {
                PickFailed?.Invoke($"Falha na seleção das peças: {ex.Message}");
            }
        }

        public string GetName() => "SAGAPickEndPlateMembers";
    }
}
