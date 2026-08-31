using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.Quantitativo
{
    /// <summary>
    /// Cria ou atualiza uma Tabela nativa de quantitativo — multicategoria
    /// (viga + pilar), filtrada pelo lote gerado, agrupada sem itemizar
    /// instância pra mostrar uma linha por perfil+material com os totais
    /// somados pelo próprio Revit.
    ///
    /// Regra de nome: o usuário escolhe o nome da tabela. Nome igual a uma
    /// tabela já criada por ESTA ferramenta → atualiza. Nome igual a uma
    /// tabela qualquer que NÃO foi criada por esta ferramenta → recusa (nunca
    /// sobrescreve algo que pode ter sido feito à mão). Nome novo → cria.
    /// Marca a tabela como "nossa" via ExtensibleStorage, igual ao padrão de
    /// *AssemblyStore já usado no resto do projeto.
    /// </summary>
    internal static class QuantitativoScheduleBuilder
    {
        public const string DefaultName = "SAGA — Lista de Perfis";

        private static readonly Guid SchemaGuid = new Guid("7A2F4E93-6B1D-4C8A-9E3F-2D5B8A1C6F47");
        private const string SchemaName = "SAGAQuantitativoScheduleV1";

        public static (ViewSchedule Schedule, string Error) CreateOrUpdate(Document doc, string lote, string desiredName)
        {
            string name = string.IsNullOrWhiteSpace(desiredName) ? DefaultName : desiredName.Trim();

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => v.Name == name && !v.IsTemplate);

            ViewSchedule schedule;
            if (existing == null)
            {
                schedule = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
                schedule.Name = name;
                MarkAsOwned(schedule);
            }
            else if (IsOwned(existing))
            {
                schedule = existing;
            }
            else
            {
                return (null, $"Já existe uma tabela chamada \"{name}\" que não foi criada por esta ferramenta — escolha outro nome pra não sobrescrever o que já existe.");
            }

            BuildFields(doc, schedule.Definition, lote);

            var textTypeId = GetOrCreateTextType(doc, "SAGA - Arial 2mm", "Arial", sizeMm: 2.0);
            if (textTypeId != ElementId.InvalidElementId)
                schedule.BodyTextTypeId = textTypeId;

            return (schedule, null);
        }

        private static void BuildFields(Document doc, ScheduleDefinition definition, string lote)
        {
            while (definition.GetFieldCount() > 0)
                definition.RemoveField(definition.GetField(0).FieldId);

            var schedulable = definition.GetSchedulableFields();

            var descField = AddField(doc, definition, schedulable, "SAGA_Descricao", "DESCRIÇÃO", columnWidthFt: 0.30);
            var matField  = AddField(doc, definition, schedulable, "SAGA_Material_Qtv", "MATERIAL", columnWidthFt: 0.20);
            AddField(doc, definition, schedulable, "SAGA_Comprimento_m", "COMPRIMENTO (M)", totals: true, columnWidthFt: 0.12);
            AddField(doc, definition, schedulable, "SAGA_Peso_Unit_kgm", "PESO UNIT. (KG/M)", columnWidthFt: 0.15);
            AddField(doc, definition, schedulable, "SAGA_Peso_kg", "PESO (KG)", totals: true, columnWidthFt: 0.13);
            AddField(doc, definition, schedulable, "SAGA_Peso_Margem_kg", "PESO + MARGEM (KG)", totals: true, columnWidthFt: 0.17);

            var loteField = AddField(doc, definition, schedulable, "SAGA_Lote_Quantitativo", null);
            if (loteField != null)
            {
                loteField.IsHidden = true;
                definition.AddFilter(new ScheduleFilter(loteField.FieldId, ScheduleFilterType.Equal, lote));
            }

            // "Não itemizar instância" só colapsa linhas idênticas DENTRO de um
            // grupo — sem dizer por quais campos agrupar, o Revit não sabe
            // separar por Descrição/Material e colapsa tudo numa linha só
            // (por isso o "<varies>"). Agrupa explicitamente pelos dois.
            // ShowBlankLine=false porque o padrão do Revit insere uma linha em
            // branco a cada troca de grupo (inclusive logo após o cabeçalho).
            if (descField != null)
                definition.AddSortGroupField(new ScheduleSortGroupField(descField.FieldId) { ShowBlankLine = false });
            if (matField != null)
                definition.AddSortGroupField(new ScheduleSortGroupField(matField.FieldId) { ShowBlankLine = false });

            definition.IsItemized = false;
            definition.ShowGrandTotal = true;
            definition.ShowGrandTotalTitle = true;
            definition.ShowGrandTotalCount = false;
            definition.GrandTotalTitle = "TOTAL";
        }

        // "Number" (sem unidade) não aceita FormatOptions/Accuracy customizado
        // no Revit — essa configuração é pensada pra campos COM unidade
        // (comprimento, massa). Em vez de brigar com essa API, o valor já
        // chega arredondado em 1 casa decimal (QuantitativoScheduleHandler
        // arredonda antes de gravar o parâmetro).
        private static ScheduleField AddField(Document doc, ScheduleDefinition definition,
                                               IList<SchedulableField> schedulable,
                                               string paramName, string columnHeading,
                                               bool totals = false, double columnWidthFt = 0)
        {
            var match = schedulable.FirstOrDefault(f => f.GetName(doc) == paramName);
            if (match == null) return null;

            var field = definition.AddField(match);
            if (columnHeading != null) field.ColumnHeading = columnHeading;
            if (totals) field.DisplayType = ScheduleFieldDisplayType.Totals;

            // GridColumnWidth (vista de tabela) e SheetColumnWidth (quando colada
            // numa folha) são independentes - sem fixar as duas com o mesmo valor,
            // a tabela fica ok na vista e com colunas erradas/gigantes na folha.
            if (columnWidthFt > 0)
            {
                field.GridColumnWidth = columnWidthFt;
                field.SheetColumnWidth = columnWidthFt;
            }

            return field;
        }

        // TextNoteType não tem propriedades C# próprias - fonte e tamanho são
        // parâmetros embutidos. ViewSchedule não tem tamanho de texto próprio:
        // ele aponta pra um TextNoteType do projeto via BodyTextTypeId, então
        // precisa existir um. A API tem dois pares de parâmetro candidatos
        // (TEXT_FONT/TEXT_SIZE e TEXT_STYLE_FONT/TEXT_STYLE_SIZE) - sem um
        // documento real pra confirmar qual vale aqui, tenta os dois e loga
        // qual funcionou, em vez de arriscar um que falha calado.
        private static readonly BuiltInParameter[] FontParamCandidates =
            { BuiltInParameter.TEXT_FONT, BuiltInParameter.TEXT_STYLE_FONT };
        private static readonly BuiltInParameter[] SizeParamCandidates =
            { BuiltInParameter.TEXT_SIZE, BuiltInParameter.TEXT_STYLE_SIZE };

        private static ElementId GetOrCreateTextType(Document doc, string name, string fontName, double sizeMm)
        {
            double sizeFt = UnitUtils.ConvertToInternalUnits(sizeMm, UnitTypeId.Millimeters);

            var allTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();

            var match = allTypes.FirstOrDefault(t =>
            {
                var font = FindParam(t, FontParamCandidates)?.AsString();
                var size = FindParam(t, SizeParamCandidates)?.AsDouble();
                return string.Equals(font, fontName, StringComparison.OrdinalIgnoreCase)
                    && size.HasValue && Math.Abs(size.Value - sizeFt) < 1e-6;
            });
            if (match != null) return match.Id;

            var target = allTypes.FirstOrDefault(t => t.Name == name)
                ?? (allTypes.FirstOrDefault() is TextNoteType baseType
                    ? (TextNoteType)baseType.Duplicate(name)
                    : null);
            if (target == null) return ElementId.InvalidElementId;

            bool fontSet = FindParam(target, FontParamCandidates) is Parameter fp && !fp.IsReadOnly && fp.Set(fontName);
            bool sizeSet = FindParam(target, SizeParamCandidates) is Parameter sp && !sp.IsReadOnly && sp.Set(sizeFt);
            SagaLog.Write($"Quantitativo: TextNoteType '{name}' - fonte definida={fontSet}, tamanho definido={sizeSet}.");

            return target.Id;
        }

        private static Parameter FindParam(Element element, BuiltInParameter[] candidates)
        {
            foreach (var bip in candidates)
            {
                var p = element.get_Parameter(bip);
                if (p != null) return p;
            }
            return null;
        }

        private static void MarkAsOwned(ViewSchedule schedule)
        {
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(schema.GetField("Owned"), true);
            schedule.SetEntity(entity);
        }

        private static bool IsOwned(ViewSchedule schedule)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return false;
            var entity = schedule.GetEntity(schema);
            return entity.IsValid();
        }

        private static Schema GetOrCreateSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation("Marca uma Tabela como criada pelo quantitativo de perfis do SAGA — só tabelas marcadas são atualizadas por essa ferramenta.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.SetVendorId("SAGA");
            builder.AddSimpleField("Owned", typeof(bool));
            return builder.Finish();
        }
    }
}
