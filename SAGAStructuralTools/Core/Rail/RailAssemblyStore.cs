using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Persiste um "conjunto lógico" de guarda-corpo em todos os elementos criados.
    /// Os elementos continuam soltos no Revit, mas compartilham AssemblyId, configuração
    /// e linha-base; assim qualquer membro pode localizar e reconstruir o trecho inteiro.
    /// </summary>
    public static class RailAssemblyStore
    {
        private static readonly Guid SchemaGuid =
            new Guid("D9A1BB2B-4D8A-4C83-9807-23B2369B47E4");

        private const string SchemaName = "SAGARailAssemblyV1";
        private const string AssemblyIdField = "AssemblyId";
        private const string PayloadField = "PayloadXml";
        private const int CurrentPayloadVersion = 1;
        private const string InternalCoordinateSystem = "RevitInternalCoordinatesFeet";

        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(RailAssemblyData));

        public static RailAssemblyData Create(RailConfig config, XYZ start, XYZ end,
                                              string assemblyId = null, int revision = 0)
        {
            return new RailAssemblyData
            {
                Version = CurrentPayloadVersion,
                Revision = revision,
                CoordinateSystem = InternalCoordinateSystem,
                AssemblyId = string.IsNullOrWhiteSpace(assemblyId)
                    ? Guid.NewGuid().ToString("N")
                    : assemblyId,
                Config = config ?? new RailConfig(),
                Start = RailPointData.FromXyz(start),
                End = RailPointData.FromXyz(end)
            };
        }

        public static void Attach(Document doc, IEnumerable<ElementId> elementIds,
                                  RailAssemblyData data)
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

        public static bool TryRead(Element element, out RailAssemblyData data)
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
                       data.Revision >= 0 &&
                       string.Equals(data.CoordinateSystem, InternalCoordinateSystem,
                                     StringComparison.Ordinal) &&
                       data.Config != null && data.Start != null && data.End != null &&
                       !string.IsNullOrWhiteSpace(data.AssemblyId) &&
                       string.Equals(data.AssemblyId, assemblyId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static List<ElementId> FindMemberIds(Document doc, RailEditContext context)
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

            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return result;

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
            builder.SetDocumentation("Configuração e geometria-base de um guarda-corpo lógico criado pelo SAGA.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField(AssemblyIdField, typeof(string));
            builder.AddSimpleField(PayloadField, typeof(string));
            return builder.Finish();
        }

        private static string Serialize(RailAssemblyData data)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static RailAssemblyData Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return (RailAssemblyData)Serializer.Deserialize(reader);
        }
    }

    public class RailAssemblyData
    {
        public int Version { get; set; }
        public int Revision { get; set; }
        public string CoordinateSystem { get; set; }
        public string AssemblyId { get; set; }
        public RailConfig Config { get; set; } = new RailConfig();
        public RailPointData Start { get; set; } = new RailPointData();
        public RailPointData End { get; set; } = new RailPointData();
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
    }

    public class RailPointData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public XYZ ToXyz() => new XYZ(X, Y, Z);

        public static RailPointData FromXyz(XYZ point)
        {
            return point == null
                ? new RailPointData()
                : new RailPointData { X = point.X, Y = point.Y, Z = point.Z };
        }
    }

    public class RailEditContext
    {
        public string AssemblyId { get; set; }
        public int Revision { get; set; }
        public RailConfig Config { get; set; }
        public XYZ Start { get; set; }
        public XYZ End { get; set; }
        public List<string> MemberUniqueIds { get; set; } = new List<string>();
        public Document SourceDocument { get; set; }

        public static RailEditContext FromStored(RailAssemblyData data)
        {
            if (data == null) return null;
            return new RailEditContext
            {
                AssemblyId = data.AssemblyId,
                Revision = data.Revision,
                Config = data.Config,
                Start = data.Start?.ToXyz(),
                End = data.End?.ToXyz(),
                MemberUniqueIds = data.MemberUniqueIds ?? new List<string>()
            };
        }
    }
}
