using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MoveToCoordinateCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDocument = commandData?.Application?.ActiveUIDocument;
            Document document = uiDocument?.Document;
            if (document == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            try
            {
                Reference selected = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    new MovableElementFilter(),
                    "Selecione o elemento que deseja mover");
                Element element = document.GetElement(selected);

                XYZ referencePoint = uiDocument.Selection.PickPoint(
                    ObjectSnapTypes.Endpoints |
                    ObjectSnapTypes.Midpoints |
                    ObjectSnapTypes.Centers |
                    ObjectSnapTypes.Intersections |
                    ObjectSnapTypes.Nearest,
                    "Indique o ponto de referência do elemento (use os snaps)");

                Transform internalToShared = CreateInternalToSharedTransform(document);
                var dialog = new MoveToCoordinateWindow(
                    Describe(element),
                    referencePoint,
                    internalToShared);
                new WindowInteropHelper(dialog).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;

                if (dialog.ShowDialog() != true) return Result.Cancelled;

                XYZ targetInternal = dialog.UsesSharedCoordinates
                    ? internalToShared.Inverse.OfPoint(dialog.TargetCoordinate)
                    : dialog.TargetCoordinate;
                XYZ displacement = targetInternal - referencePoint;

                if (displacement.GetLength() < 1e-9)
                {
                    TaskDialog.Show(
                        "SAGA - Mover para coordenada",
                        "O ponto de referência já está na coordenada informada.");
                    return Result.Succeeded;
                }

                using (var transaction = new Transaction(
                    document,
                    "SAGA - Mover para coordenada"))
                {
                    transaction.Start();
                    bool restorePinnedState = element.Pinned;
                    if (restorePinnedState) element.Pinned = false;

                    ElementTransformUtils.MoveElement(document, element.Id, displacement);

                    if (restorePinnedState) element.Pinned = true;
                    transaction.Commit();
                }

                SagaLog.Write(
                    $"MoveToCoordinate: elemento {element.Id} movido " +
                    $"por ({displacement.X}, {displacement.Y}, {displacement.Z}) pés.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("MoveToCoordinateCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Mover para coordenada",
                    "Não foi possível mover o elemento.\n\n" + ex.Message +
                    "\n\nVerifique se ele está fixado, agrupado ou controlado por restrições.");
                return Result.Cancelled;
            }
        }

        private static string Describe(Element element)
        {
            if (element is RevitLinkInstance)
                return $"Vínculo: {element.Name} — a instância inteira do vínculo será movida.";

            if (element is ImportInstance)
                return $"Vínculo CAD: {element.Name} — a instância inteira do DWG será movida.";

            string category = element?.Category?.Name ?? "Sem categoria";
            string name = element?.Name ?? "Elemento";
            return $"{category}: {name} — escolha as coordenadas do ponto indicado.";
        }

        /// <summary>
        /// Constrói a transformação usando a leitura oficial de ProjectPosition.
        /// ProjectLocation.GetTotalTransform() é uma transformação de instância e
        /// não deve ser usada diretamente como conversão interno -> compartilhado.
        /// </summary>
        private static Transform CreateInternalToSharedTransform(Document document)
        {
            ProjectLocation location = document.ActiveProjectLocation;
            if (location == null) return Transform.Identity;

            XYZ origin = ToSharedPoint(location, XYZ.Zero);
            XYZ xPoint = ToSharedPoint(location, XYZ.BasisX);
            XYZ yPoint = ToSharedPoint(location, XYZ.BasisY);
            XYZ zPoint = ToSharedPoint(location, XYZ.BasisZ);

            Transform transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = xPoint - origin;
            transform.BasisY = yPoint - origin;
            transform.BasisZ = zPoint - origin;
            return transform;
        }

        private static XYZ ToSharedPoint(ProjectLocation location, XYZ internalPoint)
        {
            ProjectPosition position = location.GetProjectPosition(internalPoint);
            if (position == null)
                throw new InvalidOperationException(
                    "Não foi possível calcular as coordenadas compartilhadas do projeto.");

            return new XYZ(
                position.EastWest,
                position.NorthSouth,
                position.Elevation);
        }

        private sealed class MovableElementFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return element != null &&
                       element.Category != null &&
                       (!element.ViewSpecific || element is ImportInstance) &&
                       (!element.Pinned ||
                        element is RevitLinkInstance ||
                        element is ImportInstance);
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
