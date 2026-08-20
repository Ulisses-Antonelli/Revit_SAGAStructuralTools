using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Persiste o "conjunto lógico" de uma escada metálica reta em todos os elementos
    /// criados (mesma mecânica do RailAssemblyStore/LadderAssemblyStore): AssemblyId +
    /// configuração + pontos de conexão. Qualquer membro permite localizar e reconstruir
    /// a escada inteira via Alt+clique.
    /// </summary>
    public static class StairAssemblyStore
    {
        private static readonly Guid SchemaGuid =
            new Guid("6B2E9F14-7A3D-4C5E-9F1A-2D8B5C6E4A73");

        private const string SchemaName = "SAGAStairAssemblyV1";
        private const string AssemblyIdField = "AssemblyId";
        private const string PayloadField = "PayloadXml";
        private const int CurrentPayloadVersion = 1;
        private const string InternalCoordinateSystem = "RevitInternalCoordinatesFeet";

        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(StairAssemblyData));
        private static readonly XmlSerializer ConfigSerializer =
            new XmlSerializer(typeof(StairConfig));

        public static StairAssemblyData Create(StairConfig config, XYZ lowerPoint, XYZ upperPoint,
                                                string lowerBeamName, string upperBeamName,
                                                string assemblyId = null, int revision = 0)
        {
            return new StairAssemblyData
            {
                Version = CurrentPayloadVersion,
                Revision = revision,
                CoordinateSystem = InternalCoordinateSystem,
                AssemblyId = string.IsNullOrWhiteSpace(assemblyId)
                    ? Guid.NewGuid().ToString("N")
                    : assemblyId,
                Config = SnapshotConfig(config),
                LowerPoint = RailPointData.FromXyz(lowerPoint),
                UpperPoint = RailPointData.FromXyz(upperPoint),
                LowerBeamName = lowerBeamName,
                UpperBeamName = upperBeamName
            };
        }

        public static void Attach(Document doc, IEnumerable<ElementId> elementIds, StairAssemblyData data)
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

        public static bool TryRead(Element element, out StairAssemblyData data)
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
                       string.Equals(data.CoordinateSystem, InternalCoordinateSystem, StringComparison.Ordinal) &&
                       data.Config != null &&
                       !string.IsNullOrWhiteSpace(data.AssemblyId) &&
                       string.Equals(data.AssemblyId, assemblyId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static List<ElementId> FindMemberIds(Document doc, StairEditContext context)
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
            builder.SetDocumentation("Configuração e pontos de conexão de uma escada metálica reta criada pelo SAGA.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField(AssemblyIdField, typeof(string));
            builder.AddSimpleField(PayloadField, typeof(string));
            return builder.Finish();
        }

        private static StairConfig SnapshotConfig(StairConfig config)
        {
            config = config ?? new StairConfig();
            using (var writer = new StringWriter())
            {
                ConfigSerializer.Serialize(writer, config);
                using (var reader = new StringReader(writer.ToString()))
                    return (StairConfig)ConfigSerializer.Deserialize(reader);
            }
        }

        private static string Serialize(StairAssemblyData data)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static StairAssemblyData Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return (StairAssemblyData)Serializer.Deserialize(reader);
        }
    }

    public class StairAssemblyData
    {
        public int Version { get; set; }
        public int Revision { get; set; }
        public string CoordinateSystem { get; set; }
        public string AssemblyId { get; set; }
        public StairConfig Config { get; set; } = new StairConfig();
        public RailPointData LowerPoint { get; set; } = new RailPointData();
        public RailPointData UpperPoint { get; set; } = new RailPointData();
        public string LowerBeamName { get; set; }
        public string UpperBeamName { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
    }

    public class StairEditContext
    {
        public string AssemblyId { get; set; }
        public int Revision { get; set; }
        public StairConfig Config { get; set; }
        public XYZ LowerPoint { get; set; }
        public XYZ UpperPoint { get; set; }
        public string LowerBeamName { get; set; }
        public string UpperBeamName { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
        public Document SourceDocument { get; set; }

        public bool MatchesDocument(Document document)
        {
            if (SourceDocument == null || document == null) return false;
            try { return SourceDocument.Equals(document); }
            catch { return false; }
        }

        public static StairEditContext FromStored(StairAssemblyData data)
        {
            if (data == null) return null;
            return new StairEditContext
            {
                AssemblyId = data.AssemblyId,
                Revision = data.Revision,
                Config = data.Config,
                LowerPoint = data.LowerPoint?.ToXyz(),
                UpperPoint = data.UpperPoint?.ToXyz(),
                LowerBeamName = data.LowerBeamName,
                UpperBeamName = data.UpperBeamName,
                MemberUniqueIds = data.MemberUniqueIds ?? new List<string>()
            };
        }
    }
}
