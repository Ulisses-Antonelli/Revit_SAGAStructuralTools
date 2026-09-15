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
            double anchorLengthMm,
            bool hasHook,
            int anchorsX,
            int anchorsY,
            double anchorEdgeDistanceXmm,
            double anchorEdgeDistanceYmm,
            double orientationDegrees = 0.0)
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

            ApplyPlanRotation(ref basisX, ref basisY, orientationDegrees);

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
                solid,
                column,
                lxMm,
                lyMm,
                plateThicknessMm,
                plateFyMpa,
                plateFuMpa);
            CreateAnchorShapes(
                column,
                origin,
                basisX,
                basisY,
                basisZ,
                materialId,
                lxMm,
                lyMm,
                plateThicknessMm,
                anchorDiameterMm,
                anchorLengthMm,
                hasHook,
                anchorsX,
                anchorsY,
                anchorEdgeDistanceXmm,
                anchorEdgeDistanceYmm);

            return shape;
        }

        private static void SetBasePlateParameters(
            DirectShape shape,
            Solid solid,
            FamilyInstance column,
            double lxMm,
            double lyMm,
            double plateThicknessMm,
            double plateFyMpa,
            double plateFuMpa)
        {
            string mark = $"PB-{column.Id.Value}";
            string comments = $"CHAPA DE {GetNominalPlateThickness(plateThicknessMm)}";
            string dimensions = $"{FormatMm(lxMm)} x {FormatMm(lyMm)} x {FormatMm(plateThicknessMm)} mm";
            double areaM2 = ConvertSquareMillimetersToSquareMeters(lxMm * lyMm);
            double weightKg = CalculatePlateWeightKg(solid);

            SetTextParameter(shape, BuiltInParameter.ALL_MODEL_MARK, mark);
            SetTextParameter(shape, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, comments);
            SetTextParameter(shape, "Comentários", comments);
            SetTextParameter(shape, "Comments", comments);
            SetTextParameter(shape, "SGA_DIMENSAO", dimensions);
            SetParameterWithUnit(shape, "SGA_LX", lxMm, SpecTypeId.Length, UnitTypeId.Millimeters, "mm");
            SetParameterWithUnit(shape, "SGA_LY", lyMm, SpecTypeId.Length, UnitTypeId.Millimeters, "mm");
            SetParameterWithUnit(shape, "SGA_TPL", plateThicknessMm, SpecTypeId.Length, UnitTypeId.Millimeters, "mm");
            SetNumberParameter(shape, "SGA_AREA", areaM2, SpecTypeId.Area, UnitTypeId.SquareMeters);
            SetNumberParameter(shape, "SGA_PESO", weightKg, SpecTypeId.Mass, UnitTypeId.Kilograms);
            SetParameterWithUnit(shape, "SGA_FY_PL", plateFyMpa, SpecTypeId.Number, null, "MPa");
            SetParameterWithUnit(shape, "SGA_FU_PL", plateFuMpa, SpecTypeId.Number, null, "MPa");
            SetTextParameter(shape, "SGA_LX_MM", $"{FormatMm(lxMm)} mm");
            SetTextParameter(shape, "SGA_LY_MM", $"{FormatMm(lyMm)} mm");
            SetTextParameter(shape, "SGA_TPL_MM", $"{FormatMm(plateThicknessMm)} mm");
            SetTextParameter(shape, "SGA_AREA_M2", $"{FormatArea(areaM2)} m²");
            SetTextParameter(shape, "SGA_PESO_KG", $"{FormatWeight(weightKg)} kg");
            SetTextParameter(shape, "SGA_FY_PL_MPA", $"{FormatMm(plateFyMpa)} MPa");
            SetTextParameter(shape, "SGA_FU_PL_MPA", $"{FormatMm(plateFuMpa)} MPa");
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

            IReadOnlyList<XYZ> anchorCenters = GetAnchorCenters(
                origin,
                basisX,
                basisY,
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
            foreach (XYZ anchorCenter in anchorCenters)
            {
                XYZ holeCenter = anchorCenter + basisZ * (plateThicknessFt * 0.10);
                Solid cutter = CreateCylindricalCutter(holeCenter, basisX, basisY, cutDirection, holeRadiusFt, cutDepthFt);
                current = BooleanOperationsUtils.ExecuteBooleanOperation(
                    current,
                    cutter,
                    BooleanOperationsType.Difference);
            }

            return current;
        }

        private void CreateAnchorShapes(
            FamilyInstance column,
            XYZ origin,
            XYZ basisX,
            XYZ basisY,
            XYZ basisZ,
            ElementId materialId,
            double lxMm,
            double lyMm,
            double plateThicknessMm,
            double anchorDiameterMm,
            double anchorLengthMm,
            bool hasHook,
            int anchorsX,
            int anchorsY,
            double anchorEdgeDistanceXmm,
            double anchorEdgeDistanceYmm)
        {
            if (anchorsX < 2 || anchorsY < 2) return;
            if (anchorLengthMm <= 0 || anchorDiameterMm <= 0) return;
            if (lxMm <= 2.0 * anchorEdgeDistanceXmm) return;
            if (lyMm <= 2.0 * anchorEdgeDistanceYmm) return;

            IReadOnlyList<XYZ> anchorCenters = GetAnchorCenters(
                origin,
                basisX,
                basisY,
                lxMm,
                lyMm,
                anchorEdgeDistanceXmm,
                anchorEdgeDistanceYmm,
                anchorsX,
                anchorsY);

            double radiusFt = UnitUtils.ConvertToInternalUnits(anchorDiameterMm / 2.0, UnitTypeId.Millimeters);
            double embedmentFt = UnitUtils.ConvertToInternalUnits(anchorLengthMm, UnitTypeId.Millimeters);
            double washerThicknessFt = UnitUtils.ConvertToInternalUnits(Math.Max(6.0, anchorDiameterMm * 0.25), UnitTypeId.Millimeters);
            double washerHalfSideFt = UnitUtils.ConvertToInternalUnits(anchorDiameterMm * 1.25, UnitTypeId.Millimeters);
            double nutHeightFt = UnitUtils.ConvertToInternalUnits(anchorDiameterMm * 0.85, UnitTypeId.Millimeters);
            double nutRadiusFt = UnitUtils.ConvertToInternalUnits(anchorDiameterMm * 0.85, UnitTypeId.Millimeters);
            double doubleNutProjectionMm = Math.Max(90.0, anchorDiameterMm * 5.5);
            double projectionFt = UnitUtils.ConvertToInternalUnits(doubleNutProjectionMm, UnitTypeId.Millimeters);
            double hookLengthFt = UnitUtils.ConvertToInternalUnits(anchorDiameterMm * 6.0, UnitTypeId.Millimeters);
            var solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);

            for (int i = 0; i < anchorCenters.Count; i++)
            {
                XYZ anchorOrigin = anchorCenters[i];

                var solids = new List<GeometryObject>();
                XYZ topCenter = anchorOrigin + basisZ * projectionFt;
                solids.Add(CreateCylinder(topCenter, basisX, basisY, basisZ.Negate(), radiusFt, projectionFt + embedmentFt, solidOptions));

                solids.Add(CreateBox(
                    anchorOrigin + basisZ * (projectionFt * 0.15 + washerThicknessFt),
                    basisX,
                    basisY,
                    basisZ.Negate(),
                    washerHalfSideFt,
                    washerHalfSideFt,
                    washerThicknessFt,
                    solidOptions));

                XYZ nutTopCenter = anchorOrigin + basisZ * (projectionFt * 0.15 + washerThicknessFt + nutHeightFt);
                solids.Add(CreateHexPrism(nutTopCenter, basisX, basisY, basisZ.Negate(), nutRadiusFt, nutHeightFt, solidOptions));
                XYZ lockNutTopCenter = nutTopCenter + basisZ * nutHeightFt;
                solids.Add(CreateHexPrism(lockNutTopCenter, basisX, basisY, basisZ.Negate(), nutRadiusFt, nutHeightFt, solidOptions));

                if (hasHook)
                {
                    XYZ hookStartCenter = anchorOrigin - basisZ * (embedmentFt - radiusFt);
                    solids.Add(CreateCylinder(
                        hookStartCenter - basisX * (hookLengthFt / 2.0),
                        basisY,
                        basisZ,
                        basisX,
                        radiusFt,
                        hookLengthFt,
                        solidOptions));
                }

                DirectShape anchorShape = DirectShape.CreateElement(
                    _document,
                    new ElementId(BuiltInCategory.OST_GenericModel));
                anchorShape.SetShape(solids);
                anchorShape.SetName("SAGA - Chumbador");
                anchorShape.ApplicationId = "SAGAStructuralTools";
                anchorShape.ApplicationDataId = $"BasePlateAnchor:{column.Id.Value}:{i + 1}";
                SetAnchorParameters(anchorShape, column, i + 1, anchorDiameterMm, anchorLengthMm, hasHook);
            }
        }

        private static IReadOnlyList<XYZ> GetAnchorCenters(
            XYZ origin,
            XYZ basisX,
            XYZ basisY,
            double lxMm,
            double lyMm,
            double anchorEdgeDistanceXmm,
            double anchorEdgeDistanceYmm,
            int anchorsX,
            int anchorsY)
        {
            IReadOnlyList<BoltPoint> boltPoints = new BoltLayoutService()
                .GeneratePerimeterLayout(
                    lxMm,
                    lyMm,
                    anchorEdgeDistanceXmm,
                    anchorEdgeDistanceYmm,
                    anchorsX,
                    anchorsY);

            return boltPoints
                .Select(boltPoint => origin +
                    basisX * UnitUtils.ConvertToInternalUnits(boltPoint.X, UnitTypeId.Millimeters) +
                    basisY * UnitUtils.ConvertToInternalUnits(boltPoint.Y, UnitTypeId.Millimeters))
                .ToList();
        }

        private static void SetAnchorParameters(
            DirectShape shape,
            FamilyInstance column,
            int index,
            double anchorDiameterMm,
            double anchorLengthMm,
            bool hasHook)
        {
            string comments = hasHook ? "CHUMBADOR COM GANCHO" : "CHUMBADOR RETO";

            SetTextParameter(shape, BuiltInParameter.ALL_MODEL_MARK, $"CH-{column.Id.Value}-{index}");
            SetTextParameter(shape, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, comments);
            SetTextParameter(shape, "Comentários", comments);
            SetTextParameter(shape, "Comments", comments);
            SetTextParameter(shape, "SGA_DIMENSAO", $"Ø {FormatMm(anchorDiameterMm)} x {FormatMm(anchorLengthMm)} mm");
            SetParameterWithUnit(shape, "SGA_LX", anchorDiameterMm, SpecTypeId.Length, UnitTypeId.Millimeters, "mm");
            SetParameterWithUnit(shape, "SGA_LY", anchorLengthMm, SpecTypeId.Length, UnitTypeId.Millimeters, "mm");
            SetNumberParameter(shape, "SGA_COLUMN_ID", column.Id.Value);
            SetTextParameter(shape, "SGA_COLUMN_UID", column.UniqueId);
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

        private static Solid CreateCylinder(
            XYZ center,
            XYZ basisA,
            XYZ basisB,
            XYZ direction,
            double radius,
            double depth,
            SolidOptions solidOptions)
        {
            var loop = new CurveLoop();
            loop.Append(Arc.Create(
                center + basisA * radius,
                center - basisA * radius,
                center + basisB * radius));
            loop.Append(Arc.Create(
                center - basisA * radius,
                center + basisA * radius,
                center - basisB * radius));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                direction,
                depth,
                solidOptions);
        }

        private static Solid CreateBox(
            XYZ topCenter,
            XYZ basisX,
            XYZ basisY,
            XYZ direction,
            double halfX,
            double halfY,
            double depth,
            SolidOptions solidOptions)
        {
            var loop = new CurveLoop();
            XYZ p1 = topCenter - basisX * halfX - basisY * halfY;
            XYZ p2 = topCenter + basisX * halfX - basisY * halfY;
            XYZ p3 = topCenter + basisX * halfX + basisY * halfY;
            XYZ p4 = topCenter - basisX * halfX + basisY * halfY;
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                direction,
                depth,
                solidOptions);
        }

        private static Solid CreateHexPrism(
            XYZ topCenter,
            XYZ basisX,
            XYZ basisY,
            XYZ direction,
            double radius,
            double depth,
            SolidOptions solidOptions)
        {
            var loop = new CurveLoop();
            XYZ first = null;
            XYZ previous = null;
            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI / 6.0 + i * Math.PI / 3.0;
                XYZ point = topCenter +
                    basisX * (Math.Cos(angle) * radius) +
                    basisY * (Math.Sin(angle) * radius);
                if (first == null)
                {
                    first = point;
                }
                else
                {
                    loop.Append(Line.CreateBound(previous, point));
                }

                previous = point;
            }

            loop.Append(Line.CreateBound(previous, first));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                direction,
                depth,
                solidOptions);
        }

        private void EnsureBasePlateProjectParameters()
        {
            Category category = _document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
            CategorySet categories = _document.Application.Create.NewCategorySet();
            categories.Insert(category);

            EnsureProjectParameter("SGA_DIMENSAO", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_AREA", SpecTypeId.Area, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_PESO", SpecTypeId.Mass, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_LX", SpecTypeId.Length, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_LY", SpecTypeId.Length, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_TPL", SpecTypeId.Length, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_LX_MM", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_LY_MM", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_TPL_MM", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_AREA_M2", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_PESO_KG", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_FY_PL", SpecTypeId.Number, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_FU_PL", SpecTypeId.Number, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_FY_PL_MPA", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_FU_PL_MPA", SpecTypeId.String.Text, categories, GroupTypeId.Geometry);
            EnsureProjectParameter("SGA_COLUMN_ID", SpecTypeId.Number, categories);
            EnsureProjectParameter("SGA_COLUMN_UID", SpecTypeId.String.Text, categories);
        }

        private void EnsureProjectParameter(
            string name,
            ForgeTypeId specTypeId,
            CategorySet categories)
        {
            EnsureProjectParameter(name, specTypeId, categories, GroupTypeId.Structural);
        }

        private void EnsureProjectParameter(
            string name,
            ForgeTypeId specTypeId,
            CategorySet categories,
            ForgeTypeId groupTypeId)
        {
            Definition definition = FindBoundDefinition(name) ??
                CreateSharedDefinition(name, specTypeId);
            InstanceBinding binding = _document.Application.Create.NewInstanceBinding(categories);
            BindingMap bindings = _document.ParameterBindings;

            if (!bindings.Insert(definition, binding, groupTypeId))
            {
                bindings.ReInsert(definition, binding, groupTypeId);
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

        private static void SetNumberParameter(
            Element element,
            string parameterName,
            double value,
            ForgeTypeId expectedSpecTypeId,
            ForgeTypeId displayUnitTypeId)
        {
            Parameter parameter = element.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly)
            {
                return;
            }

            if (parameter.StorageType == StorageType.String)
            {
                parameter.Set(FormatValueWithUnit(value, displayUnitTypeId));
                return;
            }

            if (parameter.StorageType != StorageType.Double)
            {
                SetNumberParameter(element, parameterName, value);
                return;
            }

            ForgeTypeId parameterSpecTypeId = parameter.Definition.GetDataType();
            if (parameterSpecTypeId == expectedSpecTypeId)
            {
                parameter.Set(UnitUtils.ConvertToInternalUnits(value, displayUnitTypeId));
                return;
            }

            parameter.Set(value);
        }

        private static void SetParameterWithUnit(
            Element element,
            string parameterName,
            double value,
            ForgeTypeId expectedSpecTypeId,
            ForgeTypeId displayUnitTypeId,
            string unit)
        {
            Parameter parameter = element.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly)
            {
                return;
            }

            if (parameter.StorageType == StorageType.String)
            {
                parameter.Set($"{FormatMm(value)} {unit}");
                return;
            }

            if (parameter.StorageType != StorageType.Double)
            {
                SetNumberParameter(element, parameterName, value);
                return;
            }

            ForgeTypeId parameterSpecTypeId = parameter.Definition.GetDataType();
            if (displayUnitTypeId != null && parameterSpecTypeId == expectedSpecTypeId)
            {
                parameter.Set(UnitUtils.ConvertToInternalUnits(value, displayUnitTypeId));
                return;
            }

            parameter.Set(value);
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

        private static string FormatArea(double value)
        {
            return value.ToString("0.000000", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        }

        private static string FormatWeight(double value)
        {
            return value.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        }

        private static string FormatValueWithUnit(double value, ForgeTypeId displayUnitTypeId)
        {
            if (displayUnitTypeId == UnitTypeId.SquareMeters)
            {
                return $"{FormatArea(value)} m²";
            }

            if (displayUnitTypeId == UnitTypeId.Kilograms)
            {
                return $"{FormatWeight(value)} kg";
            }

            if (displayUnitTypeId == UnitTypeId.Millimeters)
            {
                return $"{FormatMm(value)} mm";
            }

            return FormatMm(value);
        }

        private static double CalculatePlateWeightKg(Solid solid)
        {
            const double steelDensityKgM3 = 7850.0;
            const double cubicFeetToCubicMeters = 0.028316846592;

            if (solid == null || solid.Volume <= 0)
            {
                return 0.0;
            }

            return solid.Volume * cubicFeetToCubicMeters * steelDensityKgM3;
        }

        private static double ConvertSquareMillimetersToSquareMeters(double value)
        {
            return value / 1000000.0;
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

        private static void ApplyPlanRotation(ref XYZ basisX, ref XYZ basisY, double degrees)
        {
            if (Math.Abs(degrees) < 1e-9)
            {
                return;
            }

            double angle = degrees * Math.PI / 180.0;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            XYZ originalX = basisX;
            XYZ originalY = basisY;
            basisX = NormalizeOrDefault(originalX * cos + originalY * sin, originalX);
            basisY = NormalizeOrDefault(originalY * cos - originalX * sin, originalY);
        }
    }
}
