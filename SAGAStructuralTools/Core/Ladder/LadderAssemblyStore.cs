using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.Ladder
{
    /// <summary>
    /// Persiste o "conjunto lógico" de uma escada marinheiro em todos os elementos
    /// criados (mesma mecânica do RailAssemblyStore): AssemblyId + configuração +
    /// referencial de implantação. Qualquer membro permite localizar e reconstruir
    /// a escada inteira via Alt+clique.
    /// </summary>
    public static class LadderAssemblyStore
    {
        private static readonly Guid SchemaGuid =
            new Guid("3F8B5A17-6C42-4E9D-A15B-84D0F7C29E61");

        private const string SchemaName = "SAGALadderAssemblyV1";
        private const string AssemblyIdField = "AssemblyId";
        private const string PayloadField = "PayloadXml";
        private const int CurrentPayloadVersion = 1;
        private const string InternalCoordinateSystem = "RevitInternalCoordinatesFeet";

        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(LadderAssemblyData));
        private static readonly XmlSerializer ConfigSerializer =
            new XmlSerializer(typeof(LadderConfig));

        public static LadderAssemblyData Create(LadderConfig config, LadderPlacement placement,
                                                string assemblyId = null, int revision = 0)
        {
            return new LadderAssemblyData
            {
                Version = CurrentPayloadVersion,
                Revision = revision,
                CoordinateSystem = InternalCoordinateSystem,
                AssemblyId = string.IsNullOrWhiteSpace(assemblyId)
                    ? Guid.NewGuid().ToString("N")
                    : assemblyId,
                Config = SnapshotConfig(config),
                Insertion = RailPointData.FromXyz(placement.InsertionPoint),
                Lateral = RailPointData.FromXyz(placement.Lateral),
                TopZFt = placement.TopZFt,
                BaseZFt = placement.BaseZFt,
                BeamHalfWidthMm = placement.BeamHalfWidthMm,
                BeamName = placement.BeamName,
                LevelName = placement.LevelName
            };
        }

        public static void Attach(Document doc, IEnumerable<ElementId> elementIds,
                                  LadderAssemblyData data)
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

        public static bool TryRead(Element element, out LadderAssemblyData data)
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
                       data.Config != null && data.Insertion != null && data.Lateral != null &&
                       !string.IsNullOrWhiteSpace(data.AssemblyId) &&
                       string.Equals(data.AssemblyId, assemblyId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static List<ElementId> FindMemberIds(Document doc, LadderEditContext context)
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
            builder.SetDocumentation("Configuração e referencial de implantação de uma escada marinheiro criada pelo SAGA.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField(AssemblyIdField, typeof(string));
            builder.AddSimpleField(PayloadField, typeof(string));
            return builder.Finish();
        }

        private static LadderConfig SnapshotConfig(LadderConfig config)
        {
            config = config ?? new LadderConfig();
            using (var writer = new StringWriter())
            {
                ConfigSerializer.Serialize(writer, config);
                using (var reader = new StringReader(writer.ToString()))
                    return (LadderConfig)ConfigSerializer.Deserialize(reader);
            }
        }

        private static string Serialize(LadderAssemblyData data)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static LadderAssemblyData Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return (LadderAssemblyData)Serializer.Deserialize(reader);
        }
    }

    public class LadderAssemblyData
    {
        public int Version { get; set; }
        public int Revision { get; set; }
        public string CoordinateSystem { get; set; }
        public string AssemblyId { get; set; }
        public LadderConfig Config { get; set; } = new LadderConfig();
        public RailPointData Insertion { get; set; } = new RailPointData();
        public RailPointData Lateral { get; set; } = new RailPointData();
        public double TopZFt { get; set; }
        public double BaseZFt { get; set; }
        public double BeamHalfWidthMm { get; set; }
        public string BeamName { get; set; }
        public string LevelName { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
    }

    public class LadderEditContext
    {
        public string AssemblyId { get; set; }
        public int Revision { get; set; }
        public LadderConfig Config { get; set; }
        public LadderPlacement Placement { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
        public Document SourceDocument { get; set; }

        public bool MatchesDocument(Document document)
        {
            if (SourceDocument == null || document == null) return false;
            try { return SourceDocument.Equals(document); }
            catch { return false; }
        }

        public static LadderEditContext FromStored(LadderAssemblyData data)
        {
            if (data == null) return null;
            return new LadderEditContext
            {
                AssemblyId = data.AssemblyId,
                Revision = data.Revision,
                Config = data.Config,
                Placement = new LadderPlacement
                {
                    InsertionPoint  = data.Insertion?.ToXyz(),
                    Lateral         = data.Lateral?.ToXyz(),
                    TopZFt          = data.TopZFt,
                    BaseZFt         = data.BaseZFt,
                    BeamHalfWidthMm = data.BeamHalfWidthMm,
                    BeamName        = data.BeamName,
                    LevelName       = data.LevelName,
                    BeamId          = ElementId.InvalidElementId
                },
                MemberUniqueIds = data.MemberUniqueIds ?? new List<string>()
            };
        }
    }
}
