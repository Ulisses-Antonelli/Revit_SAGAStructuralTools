using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.BasePlate
{
    public class BasePlateGeometryBuilder
    {
        private readonly Document _document;

        public BasePlateGeometryBuilder(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public DirectShape CreateBasePlate(
            FamilyInstance column,
            double lxMm,
            double lyMm,
            double plateThicknessMm,
            double plateFyMpa,
            double plateFuMpa)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            if (lxMm <= 0) throw new ArgumentOutOfRangeException(nameof(lxMm), "lx deve ser maior que zero.");
            if (lyMm <= 0) throw new ArgumentOutOfRangeException(nameof(lyMm), "ly deve ser maior que zero.");
            if (plateThicknessMm <= 0) throw new ArgumentOutOfRangeException(nameof(plateThicknessMm), "tpl deve ser maior que zero.");

            Transform transform = column.GetTransform();
            XYZ basisX = NormalizeOrDefault(transform.BasisX, XYZ.BasisX);
            XYZ basisY = NormalizeOrDefault(transform.BasisY, XYZ.BasisY);
            XYZ basisZ = NormalizeOrDefault(basisX.CrossProduct(basisY), NormalizeOrDefault(transform.BasisZ, XYZ.BasisZ));
            if (basisZ.DotProduct(NormalizeOrDefault(transform.BasisZ, XYZ.BasisZ)) < 0)
            {
                basisZ = basisZ.Negate();
            }

            XYZ origin = GetColumnBaseOrigin(column, transform);
            double halfLx = UnitUtils.ConvertToInternalUnits(lxMm / 2.0, UnitTypeId.Millimeters);
            double halfLy = UnitUtils.ConvertToInternalUnits(lyMm / 2.0, UnitTypeId.Millimeters);
            double thickness = UnitUtils.ConvertToInternalUnits(plateThicknessMm, UnitTypeId.Millimeters);

            XYZ p1 = origin - basisX * halfLx - basisY * halfLy;
            XYZ p2 = origin + basisX * halfLx - basisY * halfLy;
            XYZ p3 = origin + basisX * halfLx + basisY * halfLy;
            XYZ p4 = origin - basisX * halfLx + basisY * halfLy;

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            ElementId materialId = GetOrCreateSteelMaterial(plateFyMpa, plateFuMpa);
            var solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                basisZ.Negate(),
                thickness,
                solidOptions);

            DirectShape shape = DirectShape.CreateElement(
                _document,
                new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(new GeometryObject[] { solid });
            shape.SetName("SAGA - Placa de Base");
            shape.ApplicationId = "SAGAStructuralTools";
            shape.ApplicationDataId = $"BasePlate:{column.Id.Value}";

            return shape;
        }

        private static XYZ GetColumnBaseOrigin(FamilyInstance column, Transform transform)
        {
            if (column.Location is LocationCurve locationCurve)
            {
                Curve curve = locationCurve.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                return start.Z <= end.Z ? start : end;
            }

            if (column.Location is LocationPoint locationPoint)
            {
                BoundingBoxXYZ box = column.get_BoundingBox(null);
                if (box != null)
                {
                    return new XYZ(locationPoint.Point.X, locationPoint.Point.Y, box.Min.Z);
                }

                return locationPoint.Point;
            }

            return transform.Origin;
        }

        private ElementId GetOrCreateSteelMaterial(double fyMpa, double fuMpa)
        {
            const string materialName = "SAGA - Aco Placa de Base";
            Material existing = new FilteredElementCollector(_document)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(material => material.Name == materialName);
            if (existing != null) return existing.Id;

            ElementId materialId = Material.Create(_document, materialName);
            Material material = _document.GetElement(materialId) as Material;
            if (material != null)
            {
                material.Color = new Color(90, 96, 104);
                material.Shininess = 35;
                material.Smoothness = 45;
                Parameter comments = material.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && !comments.IsReadOnly)
                {
                    comments.Set($"fy,pl={fyMpa:0.##} MPa; fu,pl={fuMpa:0.##} MPa");
                }
            }

            return materialId;
        }

        private static XYZ NormalizeOrDefault(XYZ vector, XYZ fallback)
        {
            if (vector == null || vector.GetLength() < 1e-9)
            {
                return fallback;
            }

            return vector.Normalize();
        }
    }
}
