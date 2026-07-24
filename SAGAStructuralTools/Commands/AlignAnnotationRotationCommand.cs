using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Linq;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignAnnotationRotationCommand : IExternalCommand
    {
        private const double DirectionTolerance = 1e-9;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            var view = document?.ActiveView;
            if (document == null || view == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            try
            {
                Element annotation = GetAnnotation(uiDocument);
                ValidateAnnotationView(annotation, view);

                var reference = uiDocument.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new LinearReferenceFilter(document),
                    "Selecione uma linha ou aresta reta de referência");

                XYZ referenceDirection = GetReferenceDirection(document, reference);
                XYZ targetDirection = ProjectToView(referenceDirection, view);
                targetDirection = KeepReadable(targetDirection, view);

                using (var transaction = new Transaction(
                    document,
                    "SAGA - Rotacionar anotação pela referência"))
                {
                    transaction.Start();
                    AlignAnnotation(document, annotation, targetDirection, view);
                    transaction.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("AlignAnnotationRotationCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Rotacionar anotação",
                    $"Não foi possível rotacionar a anotação:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static Element GetAnnotation(UIDocument uiDocument)
        {
            var selected = uiDocument.Selection
                .GetElementIds()
                .Select(id => uiDocument.Document.GetElement(id))
                .Where(IsSupportedAnnotation)
                .ToList();

            if (selected.Count == 1)
                return selected[0];
            if (selected.Count > 1)
                throw new InvalidOperationException(
                    "Deixe somente um texto ou uma tag selecionada antes de executar o comando.");

            var reference = uiDocument.Selection.PickObject(
                ObjectType.Element,
                new AnnotationFilter(),
                "Selecione um texto ou uma tag");
            return uiDocument.Document.GetElement(reference.ElementId);
        }

        private static bool IsSupportedAnnotation(Element element) =>
            element is TextNote || element is IndependentTag;

        private static void ValidateAnnotationView(Element annotation, View activeView)
        {
            ElementId ownerViewId = annotation.OwnerViewId;
            if (ownerViewId != ElementId.InvalidElementId &&
                !ownerViewId.Equals(activeView.Id))
            {
                throw new InvalidOperationException(
                    "A anotação selecionada não pertence à vista ativa.");
            }
        }

        private static void AlignAnnotation(
            Document document,
            Element annotation,
            XYZ targetDirection,
            View view)
        {
            if (annotation is IndependentTag tag)
            {
                tag.TagOrientation = TagOrientation.AnyModelDirection;
                tag.RotationAngle = Math.Atan2(
                    targetDirection.DotProduct(view.UpDirection),
                    targetDirection.DotProduct(view.RightDirection));
                return;
            }

            if (annotation is TextNote textNote)
            {
                XYZ currentDirection = ProjectToView(textNote.BaseDirection, view);
                double angle = SignedAngle(currentDirection, targetDirection, view.ViewDirection);
                if (Math.Abs(angle) <= DirectionTolerance)
                    return;

                var axis = Line.CreateUnbound(textNote.Coord, view.ViewDirection.Normalize());
                ElementTransformUtils.RotateElement(document, textNote.Id, axis, angle);
                return;
            }

            throw new InvalidOperationException(
                "O elemento selecionado não é um texto nem uma tag compatível.");
        }

        private static XYZ GetReferenceDirection(Document document, Reference reference)
        {
            Element element = document.GetElement(reference.ElementId);
            GeometryObject geometry = element?.GetGeometryObjectFromReference(reference);

            if (geometry is Edge edge && edge.AsCurve() is Line edgeLine)
                return edgeLine.Direction;
            if (geometry is Line geometryLine)
                return geometryLine.Direction;
            if (element?.Location is LocationCurve location && location.Curve is Line line)
                return line.Direction;

            throw new InvalidOperationException(
                "A referência precisa ser uma linha ou aresta reta.");
        }

        private static XYZ ProjectToView(XYZ direction, View view)
        {
            XYZ normal = view.ViewDirection.Normalize();
            XYZ projected = direction - normal * direction.DotProduct(normal);
            if (projected.GetLength() <= DirectionTolerance)
                throw new InvalidOperationException(
                    "A referência está perpendicular à vista e não define uma rotação visível.");
            return projected.Normalize();
        }

        private static XYZ KeepReadable(XYZ direction, View view)
        {
            double horizontal = direction.DotProduct(view.RightDirection);
            double vertical = direction.DotProduct(view.UpDirection);
            if (horizontal < -DirectionTolerance ||
                (Math.Abs(horizontal) <= DirectionTolerance && vertical < 0.0))
                return direction.Negate();
            return direction;
        }

        private static double SignedAngle(XYZ from, XYZ to, XYZ normal)
        {
            double sin = normal.Normalize().DotProduct(from.CrossProduct(to));
            double cos = Math.Max(-1.0, Math.Min(1.0, from.DotProduct(to)));
            return Math.Atan2(sin, cos);
        }

        private sealed class AnnotationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsSupportedAnnotation(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class LinearReferenceFilter : ISelectionFilter
        {
            private readonly Document _document;

            internal LinearReferenceFilter(Document document) => _document = document;

            public bool AllowElement(Element element)
            {
                if (element?.Location is LocationCurve location && location.Curve is Line)
                    return true;
                return true;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                Element element = _document.GetElement(reference.ElementId);
                GeometryObject geometry = element?.GetGeometryObjectFromReference(reference);
                return (geometry is Edge edge && edge.AsCurve() is Line) ||
                       geometry is Line ||
                       (element?.Location is LocationCurve location && location.Curve is Line);
            }
        }
    }
}
