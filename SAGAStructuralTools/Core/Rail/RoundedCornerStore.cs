using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Registra cantos manuais que envolvem membros editaveis do SAGA. O registro
    /// permite desfazer o acabamento com segurança antes de reconstruir qualquer um
    /// dos guarda-corpos envolvidos, sem deixar arcos órfãos no modelo.
    /// </summary>
    internal static class RoundedCornerStore
    {
        private static readonly Guid SchemaGuid =
            new Guid("1A067CB6-E375-4DCC-951E-0C610C54DF93");

        private const string SchemaName = "SAGARoundedCornerV1";
        private const string PayloadField = "PayloadXml";
        private const int CurrentVersion = 1;

        private static readonly XmlSerializer Serializer =
            new XmlSerializer(typeof(RoundedCornerData));

        internal static bool TryGetRegisteredAssemblyId(
            Element element,
            out string assemblyId)
        {
            assemblyId = null;
            if (!RailAssemblyStore.TryRead(element, out var data))
                return false;

            // Payloads antigos podem não conter a lista completa, mas o próprio
            // elemento ainda possui uma entidade válida com o AssemblyId.
            if (data.MemberUniqueIds != null && data.MemberUniqueIds.Count > 0 &&
                !data.MemberUniqueIds.Contains(element.UniqueId, StringComparer.Ordinal))
                return false;

            assemblyId = data.AssemblyId;
            return !string.IsNullOrWhiteSpace(assemblyId);
        }

        internal static void EnsureEndpointsAreAvailable(
            Document document,
            Element first,
            int firstCornerEnd,
            Element second,
            int secondCornerEnd)
        {
            if (first == null || second == null) return;

            foreach (var entry in ReadEntries(document))
            {
                if (UsesEndpoint(
                        entry.Data,
                        first.UniqueId,
                        firstCornerEnd) ||
                    UsesEndpoint(
                        entry.Data,
                        second.UniqueId,
                        secondCornerEnd))
                {
                    throw new InvalidOperationException(
                        "Uma das extremidades selecionadas já participa de outro canto arredondado.");
                }
            }
        }

        private static bool UsesEndpoint(
            RoundedCornerData data,
            string elementUniqueId,
            int cornerEnd)
        {
            return (string.Equals(
                        data.FirstElementUniqueId,
                        elementUniqueId,
                        StringComparison.Ordinal) &&
                    data.FirstCornerEnd == cornerEnd) ||
                   (string.Equals(
                        data.SecondElementUniqueId,
                        elementUniqueId,
                        StringComparison.Ordinal) &&
                    data.SecondCornerEnd == cornerEnd);
        }

        internal static void RegisterIfNeeded(
            Document document,
            RoundedCornerMember first,
            Line originalFirstLine,
            int firstCornerEnd,
            bool firstJoinWasAllowed,
            RoundedCornerMember second,
            Line originalSecondLine,
            int secondCornerEnd,
            bool secondJoinWasAllowed,
            FamilyInstance curved,
            double radiusMm,
            string operationId = null,
            bool forceRegistration = false,
            string ownedIntermediateElementUniqueId = null)
        {
            TryGetRegisteredAssemblyId(first.Instance, out string firstAssemblyId);
            TryGetRegisteredAssemblyId(second.Instance, out string secondAssemblyId);
            if (!forceRegistration &&
                string.IsNullOrWhiteSpace(firstAssemblyId) &&
                string.IsNullOrWhiteSpace(secondAssemblyId))
                return;

            var data = new RoundedCornerData
            {
                Version = CurrentVersion,
                CornerId = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                FirstAssemblyId = firstAssemblyId,
                SecondAssemblyId = secondAssemblyId,
                FirstElementUniqueId = first.Instance.UniqueId,
                SecondElementUniqueId = second.Instance.UniqueId,
                CurvedElementUniqueId = curved.UniqueId,
                OwnedIntermediateElementUniqueId = ownedIntermediateElementUniqueId,
                FirstMemberKind = first.StoredKind,
                SecondMemberKind = second.StoredKind,
                FirstStart = RailPointData.FromXyz(originalFirstLine.GetEndPoint(0)),
                FirstEnd = RailPointData.FromXyz(originalFirstLine.GetEndPoint(1)),
                SecondStart = RailPointData.FromXyz(originalSecondLine.GetEndPoint(0)),
                SecondEnd = RailPointData.FromXyz(originalSecondLine.GetEndPoint(1)),
                FirstTrimmedCornerPoint = GetCurrentCornerPoint(first, firstCornerEnd),
                SecondTrimmedCornerPoint = GetCurrentCornerPoint(second, secondCornerEnd),
                FirstCornerEnd = firstCornerEnd,
                SecondCornerEnd = secondCornerEnd,
                FirstJoinWasAllowed = firstJoinWasAllowed,
                SecondJoinWasAllowed = secondJoinWasAllowed,
                RadiusMm = radiusMm
            };

            var storage = DataStorage.Create(document);
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(schema.GetField(PayloadField), Serialize(data));
            storage.SetEntity(entity);
        }

        private static RailPointData GetCurrentCornerPoint(
            RoundedCornerMember member,
            int cornerEnd)
        {
            if (member == null || (cornerEnd != 0 && cornerEnd != 1))
            {
                throw new InvalidOperationException(
                    "Não foi possível registrar a geometria recortada do canto.");
            }

            var line = member.GetAxis();
            return RailPointData.FromXyz(line.GetEndPoint(cornerEnd));
        }

        internal static int RemoveForAssembly(Document document, string assemblyId)
        {
            if (document == null || string.IsNullOrWhiteSpace(assemblyId)) return 0;

            var allEntries = ReadEntries(document).ToList();
            var directlyMatching = allEntries
                .Where(entry =>
                    string.Equals(entry.Data.FirstAssemblyId, assemblyId,
                                  StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Data.SecondAssemblyId, assemblyId,
                                  StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (directlyMatching.Count == 0) return 0;

            var operationIds = new HashSet<string>(
                directlyMatching
                    .Select(entry => entry.Data.OperationId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            var matching = allEntries
                .Where(entry =>
                    directlyMatching.Contains(entry) ||
                    (!string.IsNullOrWhiteSpace(entry.Data.OperationId) &&
                     operationIds.Contains(entry.Data.OperationId)))
                .ToList();

            var ownedIntermediateIds = new HashSet<string>(
                matching
                    .Select(entry => entry.Data.OwnedIntermediateElementUniqueId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
            foreach (string uniqueId in ownedIntermediateIds)
                EnsureOwnedIntermediateUnchanged(document, uniqueId, matching);

            // Primeiro remove todos os arcos da operação. Isso evita que joins ou
            // a geometria de um segundo fillet interfiram na restauração das retas.
            foreach (var entry in matching)
            {
                var curved = document.GetElement(entry.Data.CurvedElementUniqueId);
                if (curved != null)
                    document.Delete(curved.Id);
            }
            document.Regenerate();

            // O patamar criado automaticamente pertence à operação composta. Ele
            // não deve sobreviver quando um dos guarda-corpos ligados for refeito.
            foreach (string uniqueId in ownedIntermediateIds)
            {
                var ownedIntermediate = document.GetElement(uniqueId);
                if (ownedIntermediate != null)
                    document.Delete(ownedIntermediate.Id);
            }
            document.Regenerate();

            // Uma união composta pode mencionar o mesmo membro central em dois
            // registros. A chave por elemento+extremidade permite restaurar as duas
            // pontas, mas impede repetir destrutivamente a mesma ponta.
            var restoredEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in matching)
            {
                // O membro do conjunto em edição será excluído e reconstruído logo
                // depois. Somente o lado externo (ou de outro conjunto) precisa voltar
                // à geometria reta original.
                if (!ownedIntermediateIds.Contains(entry.Data.FirstElementUniqueId) &&
                    !AssemblyMatches(entry.Data.FirstAssemblyId, assemblyId) &&
                    restoredEndpoints.Add(EndpointKey(
                        entry.Data.FirstElementUniqueId,
                        entry.Data.FirstCornerEnd)))
                {
                    RestoreProfile(
                        document,
                        entry.Data.FirstElementUniqueId,
                        entry.Data.FirstStart,
                        entry.Data.FirstEnd,
                        entry.Data.FirstTrimmedCornerPoint,
                        entry.Data.FirstCornerEnd,
                        entry.Data.FirstJoinWasAllowed,
                        entry.Data.FirstMemberKind);
                }
                if (!ownedIntermediateIds.Contains(entry.Data.SecondElementUniqueId) &&
                    !AssemblyMatches(entry.Data.SecondAssemblyId, assemblyId) &&
                    restoredEndpoints.Add(EndpointKey(
                        entry.Data.SecondElementUniqueId,
                        entry.Data.SecondCornerEnd)))
                {
                    RestoreProfile(
                        document,
                        entry.Data.SecondElementUniqueId,
                        entry.Data.SecondStart,
                        entry.Data.SecondEnd,
                        entry.Data.SecondTrimmedCornerPoint,
                        entry.Data.SecondCornerEnd,
                        entry.Data.SecondJoinWasAllowed,
                        entry.Data.SecondMemberKind);
                }
            }

            foreach (var entry in matching)
                document.Delete(entry.Storage.Id);

            return matching.Count;
        }

        private static void EnsureOwnedIntermediateUnchanged(
            Document document,
            string uniqueId,
            IReadOnlyCollection<CornerEntry> entries)
        {
            var element = document.GetElement(uniqueId);
            if (element == null) return;

            var instance = element as FamilyInstance;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "O trecho horizontal automático não é mais um perfil estrutural válido.");
            }

            var references = entries
                .Where(entry =>
                    string.Equals(
                        entry.Data.FirstElementUniqueId,
                        uniqueId,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        entry.Data.SecondElementUniqueId,
                        uniqueId,
                        StringComparison.Ordinal))
                .ToList();
            if (references.Count == 0)
            {
                throw new InvalidOperationException(
                    "O registro do trecho horizontal automático está incompleto.");
            }

            var firstReference = references[0].Data;
            string storedKind = string.Equals(
                firstReference.FirstElementUniqueId,
                uniqueId,
                StringComparison.Ordinal)
                ? firstReference.FirstMemberKind
                : firstReference.SecondMemberKind;
            var member = RoundedCornerMember.GetStored(
                instance,
                storedKind,
                "trecho horizontal automático");
            var axis = member.GetAxis();
            bool checkedEndpoint = false;
            double toleranceFt = Math.Max(
                1.0 / 304.8,
                document.Application.VertexTolerance);

            foreach (var entry in references)
            {
                var data = entry.Data;
                if (string.Equals(
                        data.FirstElementUniqueId,
                        uniqueId,
                        StringComparison.Ordinal))
                {
                    EnsureOwnedEndpointUnchanged(
                        axis,
                        data.FirstCornerEnd,
                        data.FirstTrimmedCornerPoint,
                        toleranceFt);
                    checkedEndpoint = true;
                }
                if (string.Equals(
                        data.SecondElementUniqueId,
                        uniqueId,
                        StringComparison.Ordinal))
                {
                    EnsureOwnedEndpointUnchanged(
                        axis,
                        data.SecondCornerEnd,
                        data.SecondTrimmedCornerPoint,
                        toleranceFt);
                    checkedEndpoint = true;
                }
            }

            if (!checkedEndpoint)
            {
                throw new InvalidOperationException(
                    "O registro do trecho horizontal automático não possui extremidades válidas.");
            }
        }

        private static void EnsureOwnedEndpointUnchanged(
            Line currentAxis,
            int cornerEnd,
            RailPointData expectedPoint,
            double toleranceFt)
        {
            if ((cornerEnd != 0 && cornerEnd != 1) || expectedPoint == null)
            {
                throw new InvalidOperationException(
                    "O registro do trecho horizontal automático está incompleto.");
            }

            double differenceFt = currentAxis
                .GetEndPoint(cornerEnd)
                .DistanceTo(expectedPoint.ToXyz());
            if (differenceFt > toleranceFt)
            {
                throw new InvalidOperationException(
                    "O trecho horizontal automático foi alterado depois da criação da união. " +
                    "Desfaça essa alteração antes de editar o guarda-corpo.");
            }
        }

        private static string EndpointKey(string elementUniqueId, int cornerEnd) =>
            $"{elementUniqueId ?? ""}\u001F{cornerEnd}";

        private static bool AssemblyMatches(string first, string second) =>
            !string.IsNullOrWhiteSpace(first) &&
            !string.IsNullOrWhiteSpace(second) &&
            string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

        private static void RestoreProfile(
            Document document,
            string uniqueId,
            RailPointData start,
            RailPointData end,
            RailPointData expectedTrimmedCorner,
            int cornerEnd,
            bool joinWasAllowed,
            string storedKind)
        {
            if (string.IsNullOrWhiteSpace(uniqueId) || start == null || end == null)
                throw new InvalidOperationException(
                    "O registro do canto está incompleto e não pode ser restaurado com segurança.");

            if (cornerEnd != 0 && cornerEnd != 1)
                throw new InvalidOperationException(
                    "O registro do canto possui uma extremidade inválida.");

            var instance = document.GetElement(uniqueId) as FamilyInstance;
            if (instance == null)
            {
                if (document.GetElement(uniqueId) == null) return;
                throw new InvalidOperationException(
                    "Um elemento ligado ao canto não é mais um perfil estrutural válido.");
            }
            var member = RoundedCornerMember.GetStored(
                instance,
                storedKind,
                "perfil ligado ao canto");
            var currentLine = member.GetAxis();

            if (expectedTrimmedCorner != null &&
                currentLine.GetEndPoint(cornerEnd)
                    .DistanceTo(expectedTrimmedCorner.ToXyz()) > 1.0 / 304.8)
            {
                throw new InvalidOperationException(
                    "A extremidade de um perfil foi alterada depois da criação do canto. " +
                    "Desfaça essa alteração antes de editar o guarda-corpo.");
            }

            XYZ originalCorner = cornerEnd == 0 ? start.ToXyz() : end.ToXyz();
            member.DisallowJoinAtEnd(cornerEnd);
            member.SetCornerEndpoint(cornerEnd, originalCorner);
            if (joinWasAllowed)
                member.AllowJoinAtEnd(cornerEnd);
        }

        private static IEnumerable<CornerEntry> ReadEntries(Document document)
        {
            if (document == null) yield break;
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) yield break;
            var field = schema.GetField(PayloadField);
            if (field == null) yield break;

            var storages = new FilteredElementCollector(document)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();

            foreach (var storage in storages)
            {
                RoundedCornerData data = null;
                try
                {
                    var entity = storage.GetEntity(schema);
                    if (!entity.IsValid()) continue;
                    data = Deserialize(entity.Get<string>(field));
                }
                catch
                {
                    data = null;
                }

                if (data != null && data.Version == CurrentVersion &&
                    !string.IsNullOrWhiteSpace(data.CornerId))
                {
                    yield return new CornerEntry { Storage = storage, Data = data };
                }
            }
        }

        private static Schema GetOrCreateSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation(
                "Vinculo temporario de um canto arredondado manual com guarda-corpos SAGA.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField(PayloadField, typeof(string));
            return builder.Finish();
        }

        private static string Serialize(RoundedCornerData data)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        private static RoundedCornerData Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return (RoundedCornerData)Serializer.Deserialize(reader);
        }

        private sealed class CornerEntry
        {
            internal DataStorage Storage { get; set; }
            internal RoundedCornerData Data { get; set; }
        }
    }

    public class RoundedCornerData
    {
        public int Version { get; set; }
        public string CornerId { get; set; }
        public string OperationId { get; set; }
        public string FirstAssemblyId { get; set; }
        public string SecondAssemblyId { get; set; }
        public string FirstElementUniqueId { get; set; }
        public string SecondElementUniqueId { get; set; }
        public string CurvedElementUniqueId { get; set; }
        public string OwnedIntermediateElementUniqueId { get; set; }
        public string FirstMemberKind { get; set; }
        public string SecondMemberKind { get; set; }
        public RailPointData FirstStart { get; set; }
        public RailPointData FirstEnd { get; set; }
        public RailPointData SecondStart { get; set; }
        public RailPointData SecondEnd { get; set; }
        public RailPointData FirstTrimmedCornerPoint { get; set; }
        public RailPointData SecondTrimmedCornerPoint { get; set; }
        public int FirstCornerEnd { get; set; }
        public int SecondCornerEnd { get; set; }
        public bool FirstJoinWasAllowed { get; set; }
        public bool SecondJoinWasAllowed { get; set; }
        public double RadiusMm { get; set; }
    }
}
