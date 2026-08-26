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
            return (schedule, null);
        }

        private static void BuildFields(Document doc, ScheduleDefinition definition, string lote)
        {
            while (definition.GetFieldCount() > 0)
                definition.RemoveField(definition.GetField(0).FieldId);

            var schedulable = definition.GetSchedulableFields();

            var descField = AddField(doc, definition, schedulable, "SAGA_Descricao", "DESCRIÇÃO");
            var matField  = AddField(doc, definition, schedulable, "SAGA_Material_Qtv", "MATERIAL");
            AddField(doc, definition, schedulable, "SAGA_Comprimento_m", "COMPRIMENTO (M)", totals: true);
            AddField(doc, definition, schedulable, "SAGA_Peso_Unit_kgm", "PESO UNIT. (KG/M)");
            AddField(doc, definition, schedulable, "SAGA_Peso_kg", "PESO (KG)", totals: true);
            AddField(doc, definition, schedulable, "SAGA_Peso_Margem_kg", "PESO + MARGEM (KG)", totals: true);

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
            if (descField != null) definition.AddSortGroupField(new ScheduleSortGroupField(descField.FieldId));
            if (matField  != null) definition.AddSortGroupField(new ScheduleSortGroupField(matField.FieldId));

            definition.IsItemized = false;
            definition.ShowGrandTotal = true;
            definition.ShowGrandTotalTitle = true;
            definition.ShowGrandTotalCount = false;
        }

        // "Number" (sem unidade) não aceita FormatOptions/Accuracy customizado
        // no Revit — essa configuração é pensada pra campos COM unidade
        // (comprimento, massa). Em vez de brigar com essa API, o valor já
        // chega arredondado em 1 casa decimal (QuantitativoScheduleHandler
        // arredonda antes de gravar o parâmetro).
        private static ScheduleField AddField(Document doc, ScheduleDefinition definition,
                                               IList<SchedulableField> schedulable,
                                               string paramName, string columnHeading,
                                               bool totals = false)
        {
            var match = schedulable.FirstOrDefault(f => f.GetName(doc) == paramName);
            if (match == null) return null;

            var field = definition.AddField(match);
            if (columnHeading != null) field.ColumnHeading = columnHeading;
            if (totals) field.DisplayType = ScheduleFieldDisplayType.Totals;

            return field;
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
