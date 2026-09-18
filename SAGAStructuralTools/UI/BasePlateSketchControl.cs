using SAGAStructuralTools.BasePlate.Domain;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SAGAStructuralTools.UI
{
    public class BasePlateSketchControl : FrameworkElement
    {
        public static readonly DependencyProperty PlateLengthXProperty =
            DependencyProperty.Register(nameof(PlateLengthX), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PlateLengthYProperty =
            DependencyProperty.Register(nameof(PlateLengthY), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProfileDepthProperty =
            DependencyProperty.Register(nameof(ProfileDepth), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProfileFlangeWidthProperty =
            DependencyProperty.Register(nameof(ProfileFlangeWidth), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AnchorDiameterProperty =
            DependencyProperty.Register(nameof(AnchorDiameter), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AnchorEdgeDistanceXProperty =
            DependencyProperty.Register(nameof(AnchorEdgeDistanceX), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AnchorEdgeDistanceYProperty =
            DependencyProperty.Register(nameof(AnchorEdgeDistanceY), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PlateThicknessProperty =
            DependencyProperty.Register(nameof(PlateThickness), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StiffenerHeightProperty =
            DependencyProperty.Register(nameof(StiffenerHeight), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StiffenerThicknessProperty =
            DependencyProperty.Register(nameof(StiffenerThickness), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BoltPointsProperty =
            DependencyProperty.Register(nameof(BoltPoints), typeof(IEnumerable), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty OrientationDegreesProperty =
            DependencyProperty.Register(nameof(OrientationDegrees), typeof(double), typeof(BasePlateSketchControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double PlateLengthX { get => (double)GetValue(PlateLengthXProperty); set => SetValue(PlateLengthXProperty, value); }
        public double PlateLengthY { get => (double)GetValue(PlateLengthYProperty); set => SetValue(PlateLengthYProperty, value); }
        public double ProfileDepth { get => (double)GetValue(ProfileDepthProperty); set => SetValue(ProfileDepthProperty, value); }
        public double ProfileFlangeWidth { get => (double)GetValue(ProfileFlangeWidthProperty); set => SetValue(ProfileFlangeWidthProperty, value); }
        public double AnchorDiameter { get => (double)GetValue(AnchorDiameterProperty); set => SetValue(AnchorDiameterProperty, value); }
        public double AnchorEdgeDistanceX { get => (double)GetValue(AnchorEdgeDistanceXProperty); set => SetValue(AnchorEdgeDistanceXProperty, value); }
        public double AnchorEdgeDistanceY { get => (double)GetValue(AnchorEdgeDistanceYProperty); set => SetValue(AnchorEdgeDistanceYProperty, value); }
        public double PlateThickness { get => (double)GetValue(PlateThicknessProperty); set => SetValue(PlateThicknessProperty, value); }
        public double StiffenerHeight { get => (double)GetValue(StiffenerHeightProperty); set => SetValue(StiffenerHeightProperty, value); }
        public double StiffenerThickness { get => (double)GetValue(StiffenerThicknessProperty); set => SetValue(StiffenerThicknessProperty, value); }
        public IEnumerable BoltPoints { get => (IEnumerable)GetValue(BoltPointsProperty); set => SetValue(BoltPointsProperty, value); }
        public double OrientationDegrees { get => (double)GetValue(OrientationDegreesProperty); set => SetValue(OrientationDegreesProperty, value); }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(760, 280);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
            if (PlateLengthX <= 0 || PlateLengthY <= 0) return;

            Brush black = Brushes.Black;
            Brush gray = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
            Brush red = new SolidColorBrush(Color.FromRgb(0xE0, 0x20, 0x20));
            var thin = new Pen(black, 1);
            var profilePen = new Pen(gray, 1);
            var axisPen = new Pen(red, 1.5);
            var dimPen = new Pen(black, 0.8);

            Rect planArea = new Rect(42, 34, Math.Max(260, ActualWidth * 0.46), Math.Max(190, ActualHeight - 62));
            Rect elevationArea = new Rect(planArea.Right + 56, 42, Math.Max(230, ActualWidth - planArea.Right - 78), Math.Max(180, ActualHeight - 72));

            DrawPlan(dc, planArea, thin, profilePen, axisPen, dimPen, black, red);
            DrawElevation(dc, elevationArea, thin, profilePen, dimPen, black);
        }

        private void DrawPlan(DrawingContext dc, Rect area, Pen thin, Pen profilePen, Pen axisPen, Pen dimPen, Brush text, Brush red)
        {
            double topRoom = 30;
            double leftRoom = 38;
            double rightRoom = 32;
            double bottomRoom = 58;
            bool isRotated = IsRotated90();
            double displayLengthX = isRotated ? PlateLengthY : PlateLengthX;
            double displayLengthY = isRotated ? PlateLengthX : PlateLengthY;
            double scale = Math.Min(
                (area.Width - leftRoom - rightRoom) / displayLengthX,
                (area.Height - topRoom - bottomRoom) / displayLengthY);
            Rect plate = new Rect(
                area.Left + leftRoom,
                area.Top + topRoom,
                displayLengthX * scale,
                displayLengthY * scale);
            Point center = new Point(plate.Left + plate.Width / 2, plate.Top + plate.Height / 2);

            dc.DrawRectangle(null, thin, plate);
            DrawProfilePlan(dc, center, scale, profilePen, isRotated);
            DrawBolts(dc, center, scale, thin, isRotated);
            DrawAxes(dc, center, plate, axisPen, red);

            DrawHorizontalDimension(dc, plate.Left, plate.Right, plate.Top - 22, isRotated ? "lx" : "ly", dimPen, text);
            DrawVerticalDimension(dc, plate.Left - 24, plate.Top, plate.Bottom, isRotated ? "ly" : "lx", dimPen, text);

            DrawHorizontalBoltChainDimensions(dc, plate, center, scale, plate.Top - 9, dimPen, text, isRotated);
            DrawVerticalBoltChainDimensions(dc, plate, center, scale, plate.Left - 10, dimPen, text, isRotated);
            DrawPlanSummary(dc, new Point(plate.Left, plate.Bottom + 10), text);

            Point calloutStart = new Point(plate.Right - 18, plate.Top + 18);
            Point calloutEnd = new Point(plate.Right + 36, plate.Top + 2);
            dc.DrawLine(dimPen, calloutStart, calloutEnd);
            DrawText(dc, "tpl", text, new Point(calloutEnd.X + 2, calloutEnd.Y - 7), 10);
        }

        private void DrawHorizontalBoltChainDimensions(
            DrawingContext dc,
            Rect plate,
            Point center,
            double scale,
            double y,
            Pen dimPen,
            Brush text,
            bool isRotated)
        {
            List<double> boltXCoordinates = GetUniqueBoltCoordinates(point => isRotated ? point.Y : point.X)
                .Select(x => center.X + x * scale)
                .Where(x => x > plate.Left && x < plate.Right)
                .OrderBy(x => x)
                .ToList();
            if (boltXCoordinates.Count == 0) return;

            var chain = new List<double> { plate.Left };
            chain.AddRange(boltXCoordinates);
            chain.Add(plate.Right);

            for (int i = 0; i < chain.Count - 1; i++)
            {
                string label = i == 0 || i == chain.Count - 2
                    ? (isRotated ? "b1" : "a1")
                    : (isRotated ? "b2" : "a2");
                DrawHorizontalDimension(dc, chain[i], chain[i + 1], y, label, dimPen, text);
            }
        }

        private void DrawVerticalBoltChainDimensions(
            DrawingContext dc,
            Rect plate,
            Point center,
            double scale,
            double x,
            Pen dimPen,
            Brush text,
            bool isRotated)
        {
            List<double> boltYCoordinates = GetUniqueBoltCoordinates(point => isRotated ? point.X : point.Y)
                .Select(y => center.Y - y * scale)
                .Where(y => y > plate.Top && y < plate.Bottom)
                .OrderBy(y => y)
                .ToList();
            if (boltYCoordinates.Count == 0) return;

            var chain = new List<double> { plate.Top };
            chain.AddRange(boltYCoordinates);
            chain.Add(plate.Bottom);

            for (int i = 0; i < chain.Count - 1; i++)
            {
                string label = i == 0 || i == chain.Count - 2
                    ? (isRotated ? "a1" : "b1")
                    : (isRotated ? "a2" : "b2");
                DrawVerticalDimension(dc, x, chain[i], chain[i + 1], label, dimPen, text);
            }
        }

        private List<double> GetUniqueBoltCoordinates(Func<BoltPoint, double> selector)
        {
            var coordinates = new List<double>();
            if (BoltPoints == null) return coordinates;

            foreach (object item in BoltPoints)
            {
                var point = item as BoltPoint;
                if (point == null) continue;

                double value = selector(point);
                if (coordinates.Any(existing => Math.Abs(existing - value) < 0.001)) continue;
                coordinates.Add(value);
            }

            return coordinates;
        }

        private void DrawPlanSummary(DrawingContext dc, Point origin, Brush text)
        {
            double a2 = GetBoltAxisSpacing(point => point.X);
            double b2 = GetBoltAxisSpacing(point => point.Y);
            DrawText(dc, $"lx = {FormatMm(PlateLengthX)} mm", text, origin, 10);
            DrawText(dc, $"ly = {FormatMm(PlateLengthY)} mm", text, new Point(origin.X + 96, origin.Y), 10);
            DrawText(dc, $"a1 = {FormatMm(AnchorEdgeDistanceX)} mm", text, new Point(origin.X, origin.Y + 16), 10);
            DrawText(dc, $"a2 = {FormatMm(a2)} mm", text, new Point(origin.X + 96, origin.Y + 16), 10);
            DrawText(dc, $"b1 = {FormatMm(AnchorEdgeDistanceY)} mm", text, new Point(origin.X, origin.Y + 32), 10);
            DrawText(dc, $"b2 = {FormatMm(b2)} mm", text, new Point(origin.X + 96, origin.Y + 32), 10);
        }

        private double GetBoltAxisSpacing(Func<BoltPoint, double> selector)
        {
            List<double> coordinates = GetUniqueBoltCoordinates(selector)
                .OrderBy(value => value)
                .ToList();
            if (coordinates.Count < 2) return 0;

            return coordinates
                .Zip(coordinates.Skip(1), (first, second) => second - first)
                .Where(spacing => spacing > 0.001)
                .DefaultIfEmpty(0)
                .Min();
        }

        private void DrawProfilePlan(DrawingContext dc, Point center, double scale, Pen pen, bool isRotated)
        {
            double depth = Math.Max(12, ProfileDepth * scale);
            double flange = Math.Max(12, ProfileFlangeWidth * scale);
            double web = Math.Max(4, flange * 0.14);
            double flangeThk = Math.Max(4, depth * 0.05);

            if (isRotated)
            {
                dc.PushTransform(new RotateTransform(90, center.X, center.Y));
            }

            dc.DrawRectangle(null, pen, new Rect(center.X - flange / 2, center.Y - depth / 2, flange, flangeThk));
            dc.DrawRectangle(null, pen, new Rect(center.X - flange / 2, center.Y + depth / 2 - flangeThk, flange, flangeThk));
            dc.DrawRectangle(null, pen, new Rect(center.X - web / 2, center.Y - depth / 2, web, depth));

            if (isRotated)
            {
                dc.Pop();
            }
        }

        private void DrawBolts(DrawingContext dc, Point center, double scale, Pen pen, bool isRotated)
        {
            if (BoltPoints == null) return;
            double boltRadius = Math.Max(2.5, AnchorDiameter * scale / 2.0);
            foreach (object item in BoltPoints)
            {
                var point = item as BoltPoint;
                if (point == null) continue;
                Point p = isRotated
                    ? new Point(center.X + point.Y * scale, center.Y - point.X * scale)
                    : new Point(center.X + point.X * scale, center.Y - point.Y * scale);
                dc.DrawEllipse(null, pen, p, boltRadius, boltRadius);
                dc.DrawLine(pen, new Point(p.X - boltRadius * 0.55, p.Y), new Point(p.X + boltRadius * 0.55, p.Y));
                dc.DrawLine(pen, new Point(p.X, p.Y - boltRadius * 0.55), new Point(p.X, p.Y + boltRadius * 0.55));
            }
        }

        private static void DrawAxes(DrawingContext dc, Point center, Rect plate, Pen pen, Brush red)
        {
            DrawArrow(dc, pen, center, new Point(Math.Min(plate.Right + 22, center.X + plate.Width * 0.44), center.Y));
            DrawArrow(dc, pen, center, new Point(center.X, Math.Max(plate.Top - 18, center.Y - plate.Height * 0.42)));
            DrawText(dc, "X", red, new Point(Math.Min(plate.Right + 24, center.X + plate.Width * 0.44 + 4), center.Y - 10), 12);
            DrawText(dc, "Y", red, new Point(center.X + 5, Math.Max(plate.Top - 22, center.Y - plate.Height * 0.42 - 16)), 12);
        }

        private void DrawElevation(DrawingContext dc, Rect area, Pen thin, Pen profilePen, Pen dimPen, Brush text)
        {
            double plateWidth = Math.Min(area.Width - 28, Math.Max(170, PlateLengthY * 0.42));
            double plateHeight = Math.Max(8, Math.Min(18, Math.Max(PlateThickness, 1) * 0.8));
            double baseY = area.Bottom - 36;
            double left = area.Left + (area.Width - plateWidth) / 2;
            Rect plate = new Rect(left, baseY - plateHeight, plateWidth, plateHeight);

            dc.DrawRectangle(null, thin, plate);

            double columnHeight = Math.Max(92, area.Height * 0.62);
            double columnTop = plate.Top - columnHeight;
            double columnW = Math.Min(50, plateWidth * 0.18);
            double columnX = plate.Left + plate.Width / 2 - columnW / 2;
            dc.DrawRectangle(null, profilePen, new Rect(columnX, columnTop, columnW, columnHeight));
            dc.DrawLine(profilePen, new Point(columnX + columnW / 2, columnTop), new Point(columnX + columnW / 2, plate.Top));

            double stiffenerHeight = StiffenerHeight > 0
                ? Math.Min(columnHeight * 0.78, Math.Max(35, StiffenerHeight * 0.34))
                : columnHeight * 0.58;
            double stiffenerTop = plate.Top - stiffenerHeight;
            dc.DrawLine(thin, new Point(columnX - 34, plate.Top), new Point(columnX - 6, stiffenerTop));
            dc.DrawLine(thin, new Point(columnX + columnW + 34, plate.Top), new Point(columnX + columnW + 6, stiffenerTop));

            DrawVerticalDimension(dc, area.Left + 10, stiffenerTop, plate.Top, "hn", dimPen, text);
            DrawVerticalDimension(dc, area.Left + 10, plate.Top, plate.Bottom, "tpl", dimPen, text);
        }

        private static void DrawHorizontalDimension(DrawingContext dc, double x1, double x2, double y, string label, Pen pen, Brush text)
        {
            dc.DrawLine(pen, new Point(x1, y - 4), new Point(x1, y + 4));
            dc.DrawLine(pen, new Point(x2, y - 4), new Point(x2, y + 4));
            DrawArrow(dc, pen, new Point(x1, y), new Point(x2, y));
            DrawArrow(dc, pen, new Point(x2, y), new Point(x1, y));
            DrawText(dc, label, text, new Point((x1 + x2) / 2 - 7, y - 17), 10);
        }

        private static void DrawVerticalDimension(DrawingContext dc, double x, double y1, double y2, string label, Pen pen, Brush text)
        {
            dc.DrawLine(pen, new Point(x - 4, y1), new Point(x + 4, y1));
            dc.DrawLine(pen, new Point(x - 4, y2), new Point(x + 4, y2));
            DrawArrow(dc, pen, new Point(x, y1), new Point(x, y2));
            DrawArrow(dc, pen, new Point(x, y2), new Point(x, y1));
            DrawRotatedText(dc, label, text, new Point(x - 18, (y1 + y2) / 2 + 8), 10);
        }

        private static void DrawArrow(DrawingContext dc, Pen pen, Point start, Point end)
        {
            dc.DrawLine(pen, start, end);
            Vector direction = start - end;
            if (direction.Length < 0.1) return;
            direction.Normalize();
            Vector normal = new Vector(-direction.Y, direction.X);
            dc.DrawLine(pen, end, end + direction * 6 + normal * 3);
            dc.DrawLine(pen, end, end + direction * 6 - normal * 3);
        }

        private static void DrawText(DrawingContext dc, string text, Brush brush, Point point, double size)
        {
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                1.0);
            dc.DrawText(formatted, point);
        }

        private static string FormatMm(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        }

        private bool IsRotated90()
        {
            return Math.Abs(OrientationDegrees - 90.0) < 0.001;
        }

        private static void DrawRotatedText(DrawingContext dc, string text, Brush brush, Point point, double size)
        {
            dc.PushTransform(new RotateTransform(-90, point.X, point.Y));
            DrawText(dc, text, brush, point, size);
            dc.Pop();
        }
    }
}
