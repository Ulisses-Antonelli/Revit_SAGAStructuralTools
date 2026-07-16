using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Rail
{
    public enum RailLinePickMode
    {
        Standard,
        Inclined
    }

    /// <summary>
    /// Dados geométricos normalizados apenas para apresentação. O ElementId continua
    /// apontando para o elemento original; nenhuma curva é invertida ou modificada.
    /// </summary>
    public sealed class RailLinePickResult
    {
        public ElementId ElementId { get; set; }
        public double LengthMm { get; set; }
        public double HorizontalLengthMm { get; set; }
        public double ElevationChangeMm { get; set; }
        public double AngleDegrees { get; set; }
        public bool WasReversedForSummary { get; set; }
    }

    /// <summary>
    /// Vetor horizontal que leva o eixo de justificação da primeira longarina
    /// ao eixo da segunda. Os componentes ficam em milímetros e em coordenadas
    /// globais do modelo para poderem ser persistidos diretamente no RailConfig.
    /// </summary>
    public sealed class MirrorAxisPickResult
    {
        public ElementId SourceElementId { get; set; }
        public ElementId TargetElementId { get; set; }
        public double TranslationXmm { get; set; }
        public double TranslationYmm { get; set; }
        public double DistanceMm { get; set; }
    }

    /// <summary>
    /// No modo padrão seleciona um eixo reto por vez e preserva o comportamento
    /// histórico de aceitar CurveElement. No modo inclinado aceita a seleção múltipla
    /// de linhas e vigas estruturais retas, validando cada uma antes de publicá-la.
    /// </summary>
    public class LinePickHandler : IExternalEventHandler
    {
        private const double MinimumDimensionMm = 1.0;
        private const double ParallelToleranceDegrees = 1.0;
        private readonly RailLinePickMode _mode;
        private bool _mirrorAxisPickQueued;

        public LinePickHandler(RailLinePickMode mode = RailLinePickMode.Standard)
        {
            _mode = mode;
        }

        public event Action<RailLinePickResult> LinePicked;
        public event Action<MirrorAxisPickResult> MirrorAxisPicked;

        /// <summary>
        /// Faz o próximo ExternalEvent deste handler solicitar os dois eixos que
        /// definem o espelho. Retorna false fora do comando inclinado.
        /// </summary>
        public bool QueueMirrorAxisPick()
        {
            if (_mode != RailLinePickMode.Inclined) return false;
            _mirrorAxisPickQueued = true;
            return true;
        }

        public void CancelMirrorAxisPick() => _mirrorAxisPickQueued = false;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (_mode == RailLinePickMode.Inclined)
                {
                    if (_mirrorAxisPickQueued)
                    {
                        _mirrorAxisPickQueued = false;
                        PickMirrorAxes(uidoc);
                        return;
                    }

                    PickInclinedLines(uidoc);
                    return;
                }

                var reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new RailLineSelectionFilter(_mode),
                    "Clique em UMA linha do perímetro (ESC para cancelar). ");

                var element = uidoc.Document.GetElement(reference.ElementId);
                if (!TryGetBoundLine(element, out var line))
                {
                    TaskDialog.Show(
                        "SAGA — Guarda-Corpo",
                        "O elemento selecionado não possui um eixo reto válido.");
                    return;
                }

                var result = BuildResult(reference.ElementId, line);
                if (result.LengthMm > MinimumDimensionMm)
                    LinePicked?.Invoke(result);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC cancela a seleção atual; no modo múltiplo, nada é publicado antes de Concluir.
            }
            catch (Exception ex)
            {
                SagaLog.Exception("LinePickHandler.Execute", ex);
                TaskDialog.Show(
                    "SAGA — Guarda-Corpo",
                    $"Não foi possível selecionar o eixo:\n\n{ex.Message}");
            }
        }

        private void PickInclinedLines(UIDocument uidoc)
        {
            var references = uidoc.Selection.PickObjects(
                ObjectType.Element,
                new RailLineSelectionFilter(RailLinePickMode.Inclined),
                "Selecione linhas 3D ou vigas retas inclinadas e clique em Concluir (ESC para cancelar). ");

            if (references == null || references.Count == 0)
                return;

            int validCount = 0;
            int invalidAxisCount = 0;
            int shortAxisCount = 0;
            int verticalAxisCount = 0;
            int levelAxisCount = 0;

            foreach (var reference in references)
            {
                var element = uidoc.Document.GetElement(reference.ElementId);
                if (!TryGetBoundLine(element, out var line))
                {
                    invalidAxisCount++;
                    continue;
                }

                var result = BuildResult(reference.ElementId, line);
                if (result.LengthMm <= MinimumDimensionMm)
                {
                    shortAxisCount++;
                    continue;
                }

                if (result.HorizontalLengthMm <= MinimumDimensionMm)
                {
                    verticalAxisCount++;
                    continue;
                }

                if (result.ElevationChangeMm <= MinimumDimensionMm)
                {
                    levelAxisCount++;
                    continue;
                }

                LinePicked?.Invoke(result);
                validCount++;
            }

            int invalidCount = invalidAxisCount + shortAxisCount + verticalAxisCount + levelAxisCount;
            if (invalidCount <= 0)
                return;

            var reasons = new List<string>();
            if (invalidAxisCount > 0)
                reasons.Add($"{invalidAxisCount} sem eixo reto válido");
            if (shortAxisCount > 0)
                reasons.Add($"{shortAxisCount} com comprimento menor ou igual a 1 mm");
            if (verticalAxisCount > 0)
                reasons.Add($"{verticalAxisCount} praticamente vertical");
            if (levelAxisCount > 0)
                reasons.Add($"{levelAxisCount} sem desnível maior que 1 mm");

            TaskDialog.Show(
                "SAGA — Guarda-Corpo Inclinado",
                $"Seleção concluída: {validCount} eixo(s) inclinado(s) válido(s) processado(s).\n\n" +
                $"{invalidCount} item(ns) ignorado(s):\n• {string.Join("\n• ", reasons)}");
        }

        private void PickMirrorAxes(UIDocument uidoc)
        {
            if (uidoc?.Document == null)
                throw new InvalidOperationException("Nenhum documento Revit está ativo.");

            var filter = new MirrorAxisSelectionFilter();
            var sourceReference = uidoc.Selection.PickObject(
                ObjectType.Element,
                filter,
                "1/2 — Clique na PRIMEIRA longarina (eixo de origem do espelho). ");
            var targetReference = uidoc.Selection.PickObject(
                ObjectType.Element,
                filter,
                "2/2 — Clique na SEGUNDA longarina (eixo de destino do espelho). ");

            if (sourceReference.ElementId.GetId() == targetReference.ElementId.GetId())
                throw new InvalidOperationException(
                    "Selecione duas longarinas diferentes para definir o espelho.");

            var sourceElement = uidoc.Document.GetElement(sourceReference.ElementId);
            var targetElement = uidoc.Document.GetElement(targetReference.ElementId);
            if (!TryGetBoundLine(sourceElement, out var sourceLine) ||
                !TryGetBoundLine(targetElement, out var targetLine))
                throw new InvalidOperationException(
                    "Uma das longarinas selecionadas não possui eixo reto válido.");

            var sourceDirection = HorizontalDirection(sourceLine);
            var targetDirection = HorizontalDirection(targetLine);
            double parallelism = Math.Abs(sourceDirection.DotProduct(targetDirection));
            double minimumParallelism = Math.Cos(
                ParallelToleranceDegrees * Math.PI / 180.0);
            if (parallelism < minimumParallelism)
                throw new InvalidOperationException(
                    $"Os eixos selecionados não são paralelos em planta " +
                    $"(tolerância: {ParallelToleranceDegrees:F0}°). Selecione as duas longarinas correspondentes.");

            // Usa o ponto do primeiro clique apenas para escolher a estação da
            // longarina. A origem real é sempre projetada sobre seu LocationCurve,
            // ou seja, sobre o eixo de justificação e nunca sobre uma face do perfil.
            var sourceFallback = Midpoint(sourceLine);
            var clickedPoint = sourceReference.GlobalPoint ?? sourceFallback;
            var sourceAxisPoint = ProjectToHorizontalAxis(
                clickedPoint, sourceLine.GetEndPoint(0), sourceDirection);
            var targetAxisPoint = ProjectToHorizontalAxis(
                sourceAxisPoint, targetLine.GetEndPoint(0), targetDirection);

            var sourceLateral = new XYZ(
                -sourceDirection.Y,
                sourceDirection.X,
                0.0);
            double signedDistanceFt = (targetAxisPoint - sourceAxisPoint)
                .DotProduct(sourceLateral);
            if (Math.Abs(signedDistanceFt) * 304.8 <= MinimumDimensionMm)
                throw new InvalidOperationException(
                    "A distância entre os eixos selecionados deve ser maior que 1 mm.");

            var translation = sourceLateral * signedDistanceFt;
            MirrorAxisPicked?.Invoke(new MirrorAxisPickResult
            {
                SourceElementId = sourceReference.ElementId,
                TargetElementId = targetReference.ElementId,
                TranslationXmm = translation.X * 304.8,
                TranslationYmm = translation.Y * 304.8,
                DistanceMm = Math.Abs(signedDistanceFt) * 304.8
            });
        }

        private static XYZ HorizontalDirection(Line line)
        {
            var vector = line.GetEndPoint(1) - line.GetEndPoint(0);
            var horizontal = new XYZ(vector.X, vector.Y, 0.0);
            if (horizontal.GetLength() * 304.8 <= MinimumDimensionMm)
                throw new InvalidOperationException(
                    "A longarina selecionada não possui projeção horizontal válida.");
            return horizontal.Normalize();
        }

        private static XYZ Midpoint(Line line) =>
            (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;

        private static XYZ ProjectToHorizontalAxis(
            XYZ point,
            XYZ axisOrigin,
            XYZ horizontalDirection)
        {
            var horizontalDelta = new XYZ(
                point.X - axisOrigin.X,
                point.Y - axisOrigin.Y,
                0.0);
            double station = horizontalDelta.DotProduct(horizontalDirection);
            return new XYZ(axisOrigin.X, axisOrigin.Y, 0.0) +
                   horizontalDirection * station;
        }

        private static RailLinePickResult BuildResult(ElementId id, Line line)
        {
            var originalStart = line.GetEndPoint(0);
            var originalEnd = line.GetEndPoint(1);
            bool reversed = originalStart.Z > originalEnd.Z;
            var low = reversed ? originalEnd : originalStart;
            var high = reversed ? originalStart : originalEnd;

            double dx = high.X - low.X;
            double dy = high.Y - low.Y;
            double horizontalFeet = Math.Sqrt(dx * dx + dy * dy);
            double elevationFeet = Math.Abs(high.Z - low.Z);

            return new RailLinePickResult
            {
                ElementId = id,
                LengthMm = line.Length * 304.8,
                HorizontalLengthMm = horizontalFeet * 304.8,
                ElevationChangeMm = elevationFeet * 304.8,
                AngleDegrees = Math.Atan2(elevationFeet, horizontalFeet) * 180.0 / Math.PI,
                WasReversedForSummary = reversed
            };
        }

        internal static bool TryGetBoundLine(Element element, out Line line)
        {
            line = null;
            if (element?.Location is LocationCurve location &&
                location.Curve is Line locationLine && locationLine.IsBound)
            {
                line = locationLine;
                return true;
            }

            if (element is CurveElement curveElement &&
                curveElement.GeometryCurve is Line geometryLine && geometryLine.IsBound)
            {
                line = geometryLine;
                return true;
            }

            return false;
        }

        public string GetName() => _mode == RailLinePickMode.Inclined
            ? "SAGAPickInclinedRailLine"
            : "SAGAPickRailLine";
    }

    internal sealed class RailLineSelectionFilter : ISelectionFilter
    {
        private readonly RailLinePickMode _mode;

        public RailLineSelectionFilter(RailLinePickMode mode)
        {
            _mode = mode;
        }

        public bool AllowElement(Element element)
        {
            if (element == null) return false;
            if (_mode == RailLinePickMode.Standard) return element is CurveElement;
            if (!LinePickHandler.TryGetBoundLine(element, out _)) return false;
            if (element is CurveElement) return true;

            return element is FamilyInstance instance &&
                   instance.StructuralType == StructuralType.Beam &&
                   element.Category?.Id.GetId() == (int)BuiltInCategory.OST_StructuralFraming;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal sealed class MirrorAxisSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            if (!(element is FamilyInstance instance) ||
                instance.StructuralType != StructuralType.Beam ||
                element.Category?.Id.GetId() != (int)BuiltInCategory.OST_StructuralFraming)
                return false;

            return LinePickHandler.TryGetBoundLine(element, out _);
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
