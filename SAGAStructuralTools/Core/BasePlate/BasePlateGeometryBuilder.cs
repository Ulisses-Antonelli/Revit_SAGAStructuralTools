using Autodesk.Revit.DB;
using SAGAStructuralTools.BasePlate.Domain;
using System;
using System.Collections.Generic;
using System.IO;
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
            double plateFuMpa,
            double anchorDiameterMm,
            int anchorsX,
            int anchorsY,
            double anchorEdgeDistanceXmm,
            double anchorEdgeDistanceYmm)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            if (lxMm <= 0) throw new ArgumentOutOfRangeException(nameof(lxMm), "lx deve ser maior que zero.");
            if (lyMm <= 0) throw new ArgumentOutOfRangeException(nameof(lyMm), "ly deve ser maior que zero.");
            if (plateThicknessMm <= 0) throw new ArgumentOutOfRangeException(nameof(plateThicknessMm), "tpl deve ser maior que zero.");
            if (anchorDiameterMm <= 0) throw new ArgumentOutOfRangeException(nameof(anchorDiameterMm), "db deve ser maior que zero.");

            EnsureBasePlateProjectParameters();

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
            solid = CutAnchorHoles(
                solid,
                origin,
                basisX,
                basisY,
                basisZ,
                thickness,
                lxMm,
                lyMm,
                anchorDiameterMm,
                anchorsX,
                anchorsY,
                anchorEdgeDistanceXmm,
                anchorEdgeDistanceYmm);

            DirectShape shape = DirectShape.CreateElement(
                _document,
                new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(new GeometryObject[] { solid });
            shape.SetName("SAGA - Placa de Base");
            shape.ApplicationId = "SAGAStructuralTools";
            shape.ApplicationDataId = $"BasePlate:{column.Id.Value}";
            SetBasePlateParameters(
                shape,
                column,
                lxMm,
                lyMm,
                plateThicknessMm,
                plateFyMpa,
                plateFuMpa);

            return shape;
        }

        private static void SetBasePlateParameters(
            DirectShape shape,
            FamilyInstance column,
            double lxMm,
            double lyMm,
            double plateThicknessMm,
            double plateFyMpa,
            double plateFuMpa)
        {
            string mark = $"PB-{column.Id.Value}";
            string comments = $"CHAPA DE {GetNominalPlateThickness(plateThicknessMm)}";

            SetTextParameter(shape, BuiltInParameter.ALL_MODEL_MARK, mark);
            SetTextParameter(shape, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, comments);
            SetTextParameter(shape, "Comentários", comments);
            SetTextParameter(shape, "Comments", comments);
            SetNumberParameter(shape, "SGA_LX", lxMm);
            SetNumberParameter(shape, "SGA_LY", lyMm);
            SetNumberParameter(shape, "SGA_TPL", plateThicknessMm);
            SetNumberParameter(shape, "SGA_FY_PL", plateFyMpa);
            SetNumberParameter(shape, "SGA_FU_PL", plateFuMpa);
            SetNumberParameter(shape, "SGA_COLUMN_ID", column.Id.Value);
            SetTextParameter(shape, "SGA_COLUMN_UID", column.UniqueId);
        }

        private static Solid CutAnchorHoles(
            Solid plateSolid,
            XYZ origin,
            XYZ basisX,
            XYZ basisY,
            XYZ basisZ,
            double plateThicknessFt,
            double lxMm,
            double lyMm,
            double anchorDiameterMm,
            int anchorsX,
            int anchorsY,
            double anchorEdgeDistanceXmm,
            double anchorEdgeDistanceYmm)
        {
            if (anchorsX < 2 || anchorsY < 2) return plateSolid;
            if (lxMm <= 2.0 * anchorEdgeDistanceXmm) return plateSolid;
            if (lyMm <= 2.0 * anchorEdgeDistanceYmm) return plateSolid;

            IReadOnlyList<BoltPoint> boltPoints = new BoltLayoutService()
                .GeneratePerimeterLayout(
                    lxMm,
                    lyMm,
                    anchorEdgeDistanceXmm,
                    anchorEdgeDistanceYmm,
                    anchorsX,
                    anchorsY);
            double holeRadiusFt = UnitUtils.ConvertToInternalUnits(anchorDiameterMm / 2.0, UnitTypeId.Millimeters);
            double cutDepthFt = plateThicknessFt * 1.20;
            XYZ cutDirection = basisZ.Negate();

            Solid current = plateSolid;
            foreach (BoltPoint boltPoint in boltPoints)
            {
                XYZ holeCenter = origin +
                    basisX * UnitUtils.ConvertToInternalUnits(boltPoint.X, UnitTypeId.Millimeters) +
                    basisY * UnitUtils.ConvertToInternalUnits(boltPoint.Y, UnitTypeId.Millimeters) +
                    basisZ * (plateThicknessFt * 0.10);
                Solid cutter = CreateCylindricalCutter(holeCenter, basisX, basisY, cutDirection, holeRadiusFt, cutDepthFt);
                current = BooleanOperationsUtils.ExecuteBooleanOperation(
                    current,
                    cutter,
                    BooleanOperationsType.Difference);
            }

            return current;
        }

        private static Solid CreateCylindricalCutter(
            XYZ center,
            XYZ basisX,
            XYZ basisY,
            XYZ direction,
            double radius,
            double depth)
        {
            var loop = new CurveLoop();
            loop.Append(Arc.Create(
                center + basisX * radius,
                center - basisX * radius,
                center + basisY * radius));
            loop.Append(Arc.Create(
                center - basisX * radius,
                center + basisX * radius,
                center - basisY * radius));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                direction,
                depth);
        }

        private void EnsureBasePlateProjectParameters()
        {
            Category category = _document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
            CategorySet categories = _document.Application.Create.NewCategorySet();
            categories.Insert(category);

            EnsureProjectParameter("SGA_LX", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_LY", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_TPL", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_FY_PL", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_FU_PL", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_COLUMN_ID", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_COLUMN_UID", SpecTypeId.String.Text, categories);
        }

        private void EnsureProjectParameter(
            string name,
            ForgeTypeId specTypeId,
            CategorySet categories)
        {
            Definition definition = FindBoundDefinition(name) ??
                CreateSharedDefinition(name, specTypeId);
            InstanceBinding binding = _document.Application.Create.NewInstanceBinding(categories);
            BindingMap bindings = _document.ParameterBindings;

            if (!bindings.Insert(definition, binding, GroupTypeId.Structural))
            {
                bindings.ReInsert(definition, binding, GroupTypeId.Structural);
            }
        }

        private Definition FindBoundDefinition(string name)
        {
            DefinitionBindingMapIterator iterator = _document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                if (definition != null && definition.Name == name)
                {
                    return definition;
                }
            }

            return null;
        }

        private Definition CreateSharedDefinition(string name, ForgeTypeId specTypeId)
        {
            Autodesk.Revit.ApplicationServices.Application application = _document.Application;
            string previousSharedParameterFile = application.SharedParametersFilename;

            try
            {
                application.SharedParametersFilename = EnsureSharedParameterFile();
                DefinitionFile definitionFile = application.OpenSharedParameterFile();
                DefinitionGroup group = definitionFile.Groups.get_Item("SAGA") ??
                    definitionFile.Groups.Create("SAGA");
                Definition existing = group.Definitions.get_Item(name);
                if (existing != null)
                {
                    return existing;
                }

                var options = new ExternalDefinitionCreationOptions(name, specTypeId)
                {
                    Visible = true
                };
                return group.Definitions.Create(options);
            }
            finally
            {
                application.SharedParametersFilename = previousSharedParameterFile;
            }
        }

        private static string EnsureSharedParameterFile()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SAGAStructuralTools");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "SAGA_BasePlate_SharedParameters.txt");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "# SAGA Structural Tools shared parameters");
            }

            return path;
        }

        private static void SetTextParameter(Element element, BuiltInParameter builtInParameter, string value)
        {
            Parameter parameter = element.get_Parameter(builtInParameter);
            SetTextParameter(parameter, value);
        }

        private static void SetTextParameter(Element element, string parameterName, string value)
        {
            Parameter parameter = element.LookupParameter(parameterName);
            SetTextParameter(parameter, value);
        }

        private static void SetTextParameter(Parameter parameter, string value)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                return;
            }

            parameter.Set(value);
        }

        private static void SetNumberParameter(Element element, string parameterName, double value)
        {
            Parameter parameter = element.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly)
            {
                return;
            }

            if (parameter.StorageType == StorageType.Double)
            {
                parameter.Set(value);
            }
            else if (parameter.StorageType == StorageType.Integer)
            {
                parameter.Set(Convert.ToInt32(value));
            }
            else if (parameter.StorageType == StorageType.String)
            {
                parameter.Set(FormatMm(value));
            }
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

        private static string FormatMm(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        }

        private static string GetNominalPlateThickness(double thicknessMm)
        {
            PlateThicknessOption option = PlateThicknessCatalog.FindByThickness(thicknessMm);
            if (option == null) return $"{FormatMm(thicknessMm)} mm";

            int parenthesisIndex = option.DisplayName.IndexOf(" (", StringComparison.Ordinal);
            return parenthesisIndex > 0
                ? option.DisplayName.Substring(0, parenthesisIndex)
                : option.DisplayName;
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
