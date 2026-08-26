using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Quantitativo
{
    /// <summary>
    /// ExternalEvent que grava os parâmetros SAGA_* calculados em cada peça
    /// selecionada e cria/atualiza a Tabela nativa de quantitativo — tudo numa
    /// transação, igual ao resto do projeto.
    /// </summary>
    public class QuantitativoScheduleHandler : IExternalEventHandler
    {
        public List<ElementId>  SelectedIds  { get; set; }
        public QuantitativoConfig Config     { get; set; }
        public string             ScheduleName { get; set; }

        public event Action<string> Completed;

        public void Execute(UIApplication app)
        {
            SagaLog.Write("=== QuantitativoScheduleHandler.Execute ===");
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { Notify("Nenhum documento Revit aberto."); return; }

            try
            {
                using (var tx = new Transaction(doc, "SAGA — Gerar Quantitativo de Perfis"))
                {
                    tx.Start();
                    try
                    {
                        var collected = QuantitativoCollector.Collect(doc, SelectedIds);
                        if (collected.Entries.Count == 0)
                            throw new InvalidOperationException("Nenhuma viga/pilar de aço válido na seleção.");

                        QuantitativoParameterBinder.EnsureBound(
                            doc, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns);

                        string lote = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        double marginFactor = 1.0 + (Config?.MarginPercent ?? 0) / 100.0;

                        foreach (var entry in collected.Entries)
                        {
                            var element = doc.GetElement(entry.Id);
                            if (element == null) continue;

                            var m = entry.Measurement;
                            double weightKg = m.UnitWeightKgM.HasValue ? m.LengthM * m.UnitWeightKgM.Value : 0;

                            SetText(element, "SAGA_Descricao", m.Description);
                            SetText(element, "SAGA_Material_Qtv", m.Material);
                            SetText(element, "SAGA_Lote_Quantitativo", lote);
                            // Arredonda em 1 casa decimal aqui — "Number" (sem
                            // unidade) não aceita FormatOptions/Accuracy customizado
                            // na Tabela nativa do Revit.
                            SetNumber(element, "SAGA_Comprimento_m", Math.Round(m.LengthM, 1));
                            SetNumber(element, "SAGA_Peso_Unit_kgm", Math.Round(m.UnitWeightKgM ?? 0, 1));
                            SetNumber(element, "SAGA_Peso_kg", Math.Round(weightKg, 1));
                            SetNumber(element, "SAGA_Peso_Margem_kg", Math.Round(weightKg * marginFactor, 1));
                        }

                        var (schedule, scheduleError) = QuantitativoScheduleBuilder.CreateOrUpdate(doc, lote, ScheduleName);
                        if (schedule == null)
                            throw new InvalidOperationException(scheduleError);

                        var commitStatus = tx.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                            throw new InvalidOperationException("O Revit não confirmou a criação da tabela.");

                        SagaLog.Write($"Quantitativo: tabela '{schedule.Name}' pronta, lote {lote}, {collected.Entries.Count} peça(s).");
                        Notify(null);
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        SagaLog.Exception("QuantitativoScheduleHandler.Build", ex);
                        Notify(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                SagaLog.Exception("QuantitativoScheduleHandler.Execute", ex);
                Notify(ex.Message);
            }
        }

        private static void SetText(Element element, string paramName, string value)
        {
            var p = element.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly) p.Set(value ?? "");
        }

        private static void SetNumber(Element element, string paramName, double value)
        {
            var p = element.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly) p.Set(value);
        }

        public string GetName() => "SAGAGenerateQuantitativo";

        private void Notify(string error) => Completed?.Invoke(error);
    }
}
