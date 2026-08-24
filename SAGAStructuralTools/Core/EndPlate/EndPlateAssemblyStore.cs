using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Persiste o "conjunto lógico" de uma chapa de topo (uma ou duas chapas,
    /// Modo 1/Modo 2) em todos os elementos criados — mesma mecânica de
    /// <see cref="Stiffener.StiffenerAssemblyStore"/>. Qualquer membro permite
    /// localizar e reconstruir o conjunto via Alt+clique.
    /// </summary>
    public static class EndPlateAssemblyStore
    {
        private static readonly Guid SchemaGuid =
            new Guid("2C6A8E15-4D9B-4F3A-8E7C-1B5D9A6F3C82");

        private const string SchemaName = "SAGAEndPlateAssemblyV1";
        private const string AssemblyIdField = "AssemblyId";
        private const string PayloadField = "PayloadXml";
        private const int CurrentPayloadVersion = 1;
        private const string InternalCoordinateSystem = "RevitInternalCoordinatesFeet";

        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(EndPlateAssemblyData));
        private static readonly XmlSerializer ConfigSerializer =
            new XmlSerializer(typeof(EndPlateConfig));

        public static EndPlateAssemblyData Create(EndPlateConfig config, EndPlatePlacement placement,
                                                   string assemblyId = null, int revision = 0)
        {
            var data = new EndPlateAssemblyData
            {
                Version = CurrentPayloadVersion,
                Revision = revision,
                CoordinateSystem = InternalCoordinateSystem,
                AssemblyId = string.IsNullOrWhiteSpace(assemblyId)
                    ? Guid.NewGuid().ToString("N")
                    : assemblyId,
                Config = SnapshotConfig(config)
            };

            foreach (var member in placement.Members)
            {
                data.Members.Add(new EndPlateMemberData
                {
                    Name       = member.Name,
                    EndPoint   = RailPointData.FromXyz(member.EndPoint),
                    AxisDir    = RailPointData.FromXyz(member.AxisDir),
                    WidthDir   = RailPointData.FromXyz(member.WidthDir),
                    HeightDir  = RailPointData.FromXyz(member.HeightDir),
                    HeightMm   = member.HeightMm,
                    WidthMm    = member.WidthMm
                });
            }

            return data;
        }

        public static void Attach(Document doc, IEnumerable<ElementId> elementIds,
                                  EndPlateAssemblyData data)
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

        public static bool TryRead(Element element, out EndPlateAssemblyData data)
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
                       data.Config != null && data.Members != null && data.Members.Count > 0 &&
                       !string.IsNullOrWhiteSpace(data.AssemblyId) &&
                       string.Equals(data.AssemblyId, assemblyId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static List<ElementId> FindMemberIds(Document doc, EndPlateEditContext context)
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
            builder.SetDocumentation("Configuração e referencial de implantação de uma chapa de topo criada pelo SAGA.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField(AssemblyIdField, typeof(string));
            builder.AddSimpleField(PayloadField, typeof(string));
            return builder.Finish();
        }

        private static EndPlateConfig SnapshotConfig(EndPlateConfig config)
        {
            config = config ?? new EndPlateConfig();
            using (var writer = new StringWriter())
            {
                ConfigSerializer.Serialize(writer, config);
                using (var reader = new StringReader(writer.ToString()))
                    return (EndPlateConfig)ConfigSerializer.Deserialize(reader);
            }
        }

        private static string Serialize(EndPlateAssemblyData data)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static EndPlateAssemblyData Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return (EndPlateAssemblyData)Serializer.Deserialize(reader);
        }
    }

    public class EndPlateMemberData
    {
        public string Name { get; set; }
        public RailPointData EndPoint  { get; set; } = new RailPointData();
        public RailPointData AxisDir   { get; set; } = new RailPointData();
        public RailPointData WidthDir  { get; set; } = new RailPointData();
        public RailPointData HeightDir { get; set; } = new RailPointData();
        public double HeightMm { get; set; }
        public double WidthMm  { get; set; }
    }

    public class EndPlateAssemblyData
    {
        public int    Version { get; set; }
        public int    Revision { get; set; }
        public string CoordinateSystem { get; set; }
        public string AssemblyId { get; set; }
        public EndPlateConfig Config { get; set; } = new EndPlateConfig();
        public List<EndPlateMemberData> Members { get; set; } = new List<EndPlateMemberData>();
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
    }

    public class EndPlateEditContext
    {
        public string AssemblyId { get; set; }
        public int Revision { get; set; }
        public EndPlateConfig Config { get; set; }
        public EndPlatePlacement Placement { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
        public Document SourceDocument { get; set; }

        public bool MatchesDocument(Document document)
        {
            if (SourceDocument == null || document == null) return false;
            try { return SourceDocument.Equals(document); }
            catch { return false; }
        }

        public static EndPlateEditContext FromStored(EndPlateAssemblyData data)
        {
            if (data == null) return null;

            var placement = new EndPlatePlacement();
            foreach (var m in data.Members)
            {
                placement.Members.Add(new EndPlateMemberEnd
                {
                    ElementId  = ElementId.InvalidElementId,
                    Name       = m.Name,
                    EndPoint   = m.EndPoint?.ToXyz(),
                    AxisDir    = m.AxisDir?.ToXyz(),
                    WidthDir   = m.WidthDir?.ToXyz(),
                    HeightDir  = m.HeightDir?.ToXyz(),
                    HeightMm   = m.HeightMm,
                    WidthMm    = m.WidthMm
                });
            }

            return new EndPlateEditContext
            {
                AssemblyId = data.AssemblyId,
                Revision = data.Revision,
                Config = data.Config,
                Placement = placement,
                MemberUniqueIds = data.MemberUniqueIds ?? new List<string>()
            };
        }
    }
}
