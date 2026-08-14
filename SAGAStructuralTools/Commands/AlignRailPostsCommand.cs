using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignRailPostsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            try
            {
                var filter = new SagaPostFilter();
                var referencePick = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    filter,
                    "Selecione o montante de referência");
                var movingPick = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    filter,
                    "Selecione o montante que será alinhado");

                if (referencePick.ElementId.Equals(movingPick.ElementId))
                    throw new InvalidOperationException(
                        "Selecione dois montantes diferentes.");

                var referencePost = document.GetElement(referencePick.ElementId);
                var movingPost = document.GetElement(movingPick.ElementId);
                if (!RailAssemblyStore.TryRead(referencePost, out var referenceData) ||
                    !RailAssemblyStore.TryRead(movingPost, out var movingData))
                    throw new InvalidOperationException(
                        "Os dois montantes precisam pertencer a guarda-corpos criados pelo SAGA.");

                var run = RailRunGeometry.Create(
                    movingData.Start.ToXyz(),
                    movingData.End.ToXyz());
                var referencePoint = GetPostPoint(referencePost);
                var movingPoint = GetPostPoint(movingPost);
                double horizontalRatio = run.HorizontalLengthFt / run.LengthFt;
                if (horizontalRatio <= 1e-9)
                    throw new InvalidOperationException(
                        "O trecho do montante a mover não possui direção horizontal definida.");

                double horizontalDelta =
                    (referencePoint - movingPoint).DotProduct(run.HorizontalDirection);
                double movementAlongRun = horizontalDelta / horizontalRatio;
                XYZ movement = run.Direction * movementAlongRun;
                double movementMm = movement.GetLength() * 304.8;
                if (movementMm <= 0.1)
                {
                    TaskDialog.Show(
                        "SAGA - Alinhar montantes",
                        "Os montantes selecionados já estão alinhados.");
                    return Result.Succeeded;
                }
                double currentStationMm =
                    (movingPoint - run.Start).DotProduct(run.Direction) * 304.8;
                double targetStationMm = currentStationMm + movementAlongRun * 304.8;
                if (movementMm > 5000.0)
                    throw new InvalidOperationException(
                        $"O deslocamento calculado é muito grande ({movementMm:F1} mm). " +
                        "Confirme se os dois montantes selecionados pertencem ao mesmo encontro.");

                if (targetStationMm < -0.5 || targetStationMm > run.LengthMm + 0.5)
                {
                    var confirmation = new TaskDialog("SAGA - Alinhar montantes")
                    {
                        MainInstruction =
                            "O alinhamento ultrapassa a linha-base original do guarda-corpo.",
                        MainContent =
                            $"Isso pode ser normal em montantes finais junto a uma união editada. " +
                            $"O poste será deslocado {movementMm:F1} mm ao longo do seu eixo.\n\n" +
                            "Deseja continuar?",
                        CommonButtons = TaskDialogCommonButtons.Yes |
                                        TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No
                    };
                    if (confirmation.Show() != TaskDialogResult.Yes)
                        return Result.Cancelled;
                }

                using (var transaction = new Transaction(
                    document,
                    "SAGA - Alinhar montantes finais"))
                {
                    transaction.Start();
                    MovePostAlongRun(document, movingPost, movement);
                    transaction.Commit();
                }

                TaskDialog.Show(
                    "SAGA - Alinhar montantes",
                    $"Montante alinhado ao plano transversal da referência.\n\n" +
                    $"Deslocamento ao longo do guarda-corpo: {movementMm:F1} mm.\n" +
                    $"Horizontal: {horizontalDelta * 304.8:F1} mm | " +
                    $"Vertical: {movement.Z * 304.8:F1} mm.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("AlignRailPostsCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Alinhar montantes",
                    $"Não foi possível alinhar os montantes:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static XYZ GetPostPoint(Element element)
        {
            if (element?.Location is LocationCurve curveLocation &&
                curveLocation.Curve is Line line)
            {
                var first = line.GetEndPoint(0);
                var second = line.GetEndPoint(1);
                return first.Z <= second.Z ? first : second;
            }
            if (element?.Location is LocationPoint pointLocation)
                return pointLocation.Point;
            throw new InvalidOperationException(
                "O elemento selecionado não possui um eixo vertical reconhecível.");
        }

        private static void MovePostAlongRun(
            Document document,
            Element post,
            XYZ movement)
        {
            if (post?.Location is LocationCurve curveLocation &&
                curveLocation.Curve is Line line)
            {
                curveLocation.Curve = Line.CreateBound(
                    line.GetEndPoint(0) + movement,
                    line.GetEndPoint(1) + movement);
                return;
            }

            if (post?.Location is LocationPoint)
            {
                var horizontalMovement = new XYZ(movement.X, movement.Y, 0.0);
                if (horizontalMovement.GetLength() > 1e-9)
                    ElementTransformUtils.MoveElement(
                        document,
                        post.Id,
                        horizontalMovement);

                bool baseAdjusted = AddToParameter(
                    post,
                    BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM,
                    movement.Z);
                bool topAdjusted = AddToParameter(
                    post,
                    BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM,
                    movement.Z);
                if (!baseAdjusted && !topAdjusted && Math.Abs(movement.Z) > 1e-9)
                    ElementTransformUtils.MoveElement(
                        document,
                        post.Id,
                        XYZ.BasisZ * movement.Z);
                return;
            }

            throw new InvalidOperationException(
                "O tipo do montante selecionado não permite deslocamento ao longo do guarda-corpo.");
        }

        private static bool AddToParameter(
            Element element,
            BuiltInParameter parameterId,
            double increment)
        {
            var parameter = element?.get_Parameter(parameterId);
            if (parameter == null || parameter.IsReadOnly ||
                parameter.StorageType != StorageType.Double)
                return false;
            parameter.Set(parameter.AsDouble() + increment);
            return true;
        }

        private sealed class SagaPostFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (!RailAssemblyStore.TryRead(element, out _))
                    return false;
                if (element?.Location is LocationPoint)
                    return true;
                if (element?.Location is LocationCurve location &&
                    location.Curve is Line line)
                    return Math.Abs(line.Direction.Z) >= 0.99;
                return false;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
