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
    /// No modo padrão seleciona um eixo reto por vez e preserva o comportamento
    /// histórico de aceitar CurveElement. No modo inclinado aceita a seleção múltipla
    /// de linhas e vigas estruturais retas, validando cada uma antes de publicá-la.
    /// </summary>
    public class LinePickHandler : IExternalEventHandler
    {
        private const double MinimumDimensionMm = 1.0;
        private readonly RailLinePickMode _mode;

        public LinePickHandler(RailLinePickMode mode = RailLinePickMode.Standard)
        {
            _mode = mode;
        }

        public event Action<RailLinePickResult> LinePicked;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (_mode == RailLinePickMode.Inclined)
                {
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
}
