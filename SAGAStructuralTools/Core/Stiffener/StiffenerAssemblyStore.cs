using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Persiste o "conjunto lógico" de uma nervura (uma ou duas chapas) em todos os
    /// elementos criados — mesma mecânica de <see cref="Ladder.LadderAssemblyStore"/>.
    /// Qualquer membro permite localizar e reconstruir a nervura via Alt+clique.
    /// </summary>
    public static class StiffenerAssemblyStore
    {
        private static readonly Guid SchemaGuid =
            new Guid("9D3E7F42-1B8A-4C6D-9E2F-5A8D3C7B4F19");

        private const string SchemaName = "SAGAStiffenerAssemblyV1";
        private const string AssemblyIdField = "AssemblyId";
        private const string PayloadField = "PayloadXml";
        private const int CurrentPayloadVersion = 1;
        private const string InternalCoordinateSystem = "RevitInternalCoordinatesFeet";

        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(StiffenerAssemblyData));
        private static readonly XmlSerializer ConfigSerializer =
            new XmlSerializer(typeof(StiffenerConfig));

        public static StiffenerAssemblyData Create(StiffenerConfig config, StiffenerPlacement placement,
                                                    string assemblyId = null, int revision = 0)
        {
            return new StiffenerAssemblyData
            {
                Version = CurrentPayloadVersion,
                Revision = revision,
                CoordinateSystem = InternalCoordinateSystem,
                AssemblyId = string.IsNullOrWhiteSpace(assemblyId)
                    ? Guid.NewGuid().ToString("N")
                    : assemblyId,
                Config            = SnapshotConfig(config),
                Insertion         = RailPointData.FromXyz(placement.InsertionPoint),
                AxisDir           = RailPointData.FromXyz(placement.AxisDir),
                Up                = RailPointData.FromXyz(placement.Up),
                Lateral           = RailPointData.FromXyz(placement.Lateral),
                PreferredSide     = placement.PreferredSide,
                HeightMm          = placement.HeightMm,
                WidthMm           = placement.WidthMm,
                FlangeThicknessMm = placement.FlangeThicknessMm,
                WebThicknessMm    = placement.WebThicknessMm,
                FilletRadiusMm    = placement.FilletRadiusMm,
                BeamName          = placement.BeamName,
                HasAlignFace      = placement.AlignFacePoint != null,
                AlignFace         = RailPointData.FromXyz(placement.AlignFacePoint),
                AlignReferenceName = placement.AlignReferenceName
            };
        }

        public static void Attach(Document doc, IEnumerable<ElementId> elementIds,
                                  StiffenerAssemblyData data)
        {
            if (doc == null || data == null) return;

            var schema = GetOrCreateSchema();
            var elements = (elementIds ?? Enumerable.Empty<ElementId>())
                .Distinct()
                .Select(doc.GetElement)
                .Where(e => e != null)
                .ToList();

            data.MemberUniqueIds = elements.Select(e => e.UniqueId).ToList();
            string payload = Serialize(data);

            foreach (var element in elements)
            {
                var entity = new Entity(schema);
                entity.Set(schema.GetField(AssemblyIdField), data.AssemblyId);
                entity.Set(schema.GetField(PayloadField), payload);
                element.SetEntity(entity);
            }
        }

        public static bool TryRead(Element element, out StiffenerAssemblyData data)
        {
            data = null;
            if (element == null) return false;

            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return false;

            var entity = element.GetEntity(schema);
            if (!entity.IsValid()) return false;

            try
            {
                string assemblyId = entity.Get<string>(schema.GetField(AssemblyIdField));
                string payload = entity.Get<string>(schema.GetField(PayloadField));
                data = Deserialize(payload);
                return data != null &&
                       data.Version == CurrentPayloadVersion &&
                       string.Equals(data.CoordinateSystem, InternalCoordinateSystem,
                                     StringComparison.Ordinal) &&
                       data.Config != null && data.Insertion != null &&
                       data.AxisDir != null && data.Up != null && data.Lateral != null &&
                       !string.IsNullOrWhiteSpace(data.AssemblyId) &&
                       string.Equals(data.AssemblyId, assemblyId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static List<ElementId> FindMemberIds(Document doc, StiffenerEditContext context)
        {
            var result = new List<ElementId>();
            if (doc == null || context == null || string.IsNullOrWhiteSpace(context.AssemblyId))
                return result;

            if (context.MemberUniqueIds != null && context.MemberUniqueIds.Count > 0)
            {
                foreach (string uniqueId in context.MemberUniqueIds.Distinct())
                {
                    var element = doc.GetElement(uniqueId);
                    if (TryRead(element, out var data) &&
                        string.Equals(data.AssemblyId, context.AssemblyId, StringComparison.OrdinalIgnoreCase))
                        result.Add(element.Id);
                }
                return result;
            }

            var collector = new FilteredElementCollector(doc)
                .WherePasses(new ExtensibleStorageFilter(SchemaGuid))
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (TryRead(element, out var data) &&
                    string.Equals(data.AssemblyId, context.AssemblyId, StringComparison.OrdinalIgnoreCase))
                    result.Add(element.Id);
            }

            return result;
        }

        private static Schema GetOrCreateSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation("Configuração e referencial de implantação de uma nervura criada pelo SAGA.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField(AssemblyIdField, typeof(string));
            builder.AddSimpleField(PayloadField, typeof(string));
            return builder.Finish();
        }

        private static StiffenerConfig SnapshotConfig(StiffenerConfig config)
        {
            config = config ?? new StiffenerConfig();
            using (var writer = new StringWriter())
            {
                ConfigSerializer.Serialize(writer, config);
                using (var reader = new StringReader(writer.ToString()))
                    return (StiffenerConfig)ConfigSerializer.Deserialize(reader);
            }
        }

        private static string Serialize(StiffenerAssemblyData data)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static StiffenerAssemblyData Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return (StiffenerAssemblyData)Serializer.Deserialize(reader);
        }
    }

    public class StiffenerAssemblyData
    {
        public int    Version { get; set; }
        public int    Revision { get; set; }
        public string CoordinateSystem { get; set; }
        public string AssemblyId { get; set; }
        public StiffenerConfig Config { get; set; } = new StiffenerConfig();
        public RailPointData Insertion { get; set; } = new RailPointData();
        public RailPointData AxisDir   { get; set; } = new RailPointData();
        public RailPointData Up        { get; set; } = new RailPointData();
        public RailPointData Lateral   { get; set; } = new RailPointData();
        public double  PreferredSide { get; set; }
        public double  HeightMm { get; set; }
        public double  WidthMm { get; set; }
        public double  FlangeThicknessMm { get; set; }
        public double  WebThicknessMm { get; set; }
        public double? FilletRadiusMm { get; set; }
        public string BeamName { get; set; }
        public bool   HasAlignFace { get; set; }
        public RailPointData AlignFace { get; set; } = new RailPointData();
        public string AlignReferenceName { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
    }

    public class StiffenerEditContext
    {
        public string AssemblyId { get; set; }
        public int Revision { get; set; }
        public StiffenerConfig Config { get; set; }
        public StiffenerPlacement Placement { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
        public Document SourceDocument { get; set; }

        public bool MatchesDocument(Document document)
        {
            if (SourceDocument == null || document == null) return false;
            try { return SourceDocument.Equals(document); }
            catch { return false; }
        }

        public static StiffenerEditContext FromStored(StiffenerAssemblyData data)
        {
            if (data == null) return null;
            return new StiffenerEditContext
            {
                AssemblyId = data.AssemblyId,
                Revision = data.Revision,
                Config = data.Config,
                Placement = new StiffenerPlacement
                {
                    InsertionPoint    = data.Insertion?.ToXyz(),
                    AxisDir           = data.AxisDir?.ToXyz(),
                    Up                = data.Up?.ToXyz(),
                    Lateral           = data.Lateral?.ToXyz(),
                    PreferredSide     = data.PreferredSide,
                    HeightMm          = data.HeightMm,
                    WidthMm           = data.WidthMm,
                    FlangeThicknessMm = data.FlangeThicknessMm,
                    WebThicknessMm    = data.WebThicknessMm,
                    FilletRadiusMm    = data.FilletRadiusMm,
                    BeamName          = data.BeamName,
                    BeamId            = ElementId.InvalidElementId,
                    AlignFacePoint    = data.HasAlignFace ? data.AlignFace?.ToXyz() : null,
                    AlignReferenceName = data.AlignReferenceName
                },
                MemberUniqueIds = data.MemberUniqueIds ?? new List<string>()
            };
        }
    }
}
