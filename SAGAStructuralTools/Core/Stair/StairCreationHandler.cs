using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Executa a criação (ou recriação, em edição) da escada dentro de um
    /// IExternalEventHandler, garantindo que todas as chamadas à Revit API ocorram
    /// no contexto correto. Na edição, os membros antigos do conjunto lógico são
    /// removidos e a escada é reconstruída com a configuração atualizada — mesma
    /// mecânica do LadderCreationHandler/RailCreationHandler.
    ///
    /// Janelas WPF não-modais (Show) não possuem contexto de API válido —
    /// cliques de botão não são eventos Revit. O ExternalEvent resolve isso.
    /// </summary>
    public class StairCreationHandler : IExternalEventHandler
    {
        // Dados preenchidos pelo ViewModel antes de Raise()
        public StairDefinition Definition { get; set; }
        public StairConfig     Config     { get; set; }
        public ElementId       LowerBeamId { get; set; }
        public ElementId       UpperBeamId { get; set; }
        public XYZ             LowerPoint  { get; set; }
        public XYZ             UpperPoint  { get; set; }
        public string          LowerBeamName { get; set; }
        public string          UpperBeamName { get; set; }
        public StairEditContext EditContext { get; set; }

        // Resultado devolvido ao ViewModel após Execute
        public event Action<string> Completed;  // null = sucesso; string = mensagem de erro

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_StairLog.txt");

        public void Execute(UIApplication app)
        {
            Log("=== StairCreationHandler.Execute iniciado ===");

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                Notify("Nenhum documento Revit aberto.");
                return;
            }

            if (EditContext?.SourceDocument != null && !EditContext.MatchesDocument(doc))
            {
                Notify("O documento ativo mudou. Volte ao arquivo da escada e tente novamente.");
                return;
            }

            try
            {
                // Se os pontos de clique não foram capturados, usa centro das vigas como fallback
                var lower = LowerBeamId != null ? doc.GetElement(LowerBeamId) : null;
                var upper = UpperBeamId != null ? doc.GetElement(UpperBeamId) : null;
                var loPt  = LowerPoint ?? Midpoint(lower) ?? EditContext?.LowerPoint;
                var upPt  = UpperPoint ?? Midpoint(upper) ?? EditContext?.UpperPoint;

                Log($"loPt={Fmt(loPt)}  upPt={Fmt(upPt)}");

                string transactionName = EditContext == null
                    ? "SAGA — Gerar Escada Metálica"
                    : "SAGA — Atualizar Escada Metálica";

                using (var tx = new Transaction(doc, transactionName))
                {
                    tx.Start();
                    try
                    {
                        if (EditContext != null)
                        {
                            var previousIds = StairAssemblyStore.FindMemberIds(doc, EditContext);
                            if (previousIds.Count == 0)
                                throw new InvalidOperationException(
                                    "Os elementos da escada selecionada não foram encontrados.");
                            doc.Delete(previousIds);
                            Log($"Edição: removidos {previousIds.Count} elementos do conjunto {EditContext.AssemblyId}.");
                        }

                        var createdIds = new List<ElementId>();
                        var builder = new StringerBuilder(doc);
                        builder.Build(Definition, Config, loPt, upPt, createdIds);

                        if (createdIds.Count == 0)
                            throw new InvalidOperationException(
                                "Nenhum elemento foi criado. Verifique as famílias configuradas.");

                        var stored = StairAssemblyStore.Create(
                            Config, loPt, upPt,
                            LowerBeamName ?? EditContext?.LowerBeamName,
                            UpperBeamName ?? EditContext?.UpperBeamName,
                            EditContext?.AssemblyId,
                            (EditContext?.Revision ?? -1) + 1);
                        StairAssemblyStore.Attach(doc, createdIds, stored);
                        Log($"Conjunto lógico {stored.AssemblyId}: {createdIds.Count} elementos identificados.");

                        var commitStatus = tx.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                            throw new InvalidOperationException(
                                "O Revit não confirmou a criação da escada.");

                        // Contexto de edição só é atualizado após o commit; num rollback
                        // ele continua apontando para os membros antigos.
                        if (EditContext != null)
                        {
                            EditContext.Revision = stored.Revision;
                            EditContext.Config = stored.Config;
                            EditContext.LowerPoint = loPt;
                            EditContext.UpperPoint = upPt;
                            EditContext.MemberUniqueIds = new List<string>(stored.MemberUniqueIds);
                        }

                        Log("=== Concluído com sucesso ===");
                        Notify(null); // sucesso
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();
                        Log($"ERRO no Build: {ex.Message}");
                        Notify(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ERRO externo: {ex.Message}\n{ex.StackTrace}");
                Notify(ex.Message);
            }
        }

        public string GetName() => "SAGACreateStair";

        private void Notify(string error) => Completed?.Invoke(error);

        private static XYZ Midpoint(Element beam)
        {
            if (beam?.Location is LocationCurve lc)
                return lc.Curve.Evaluate(0.5, true);
            var bb = beam?.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) * 0.5 : null;
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }

        private static string Fmt(XYZ p) =>
            p == null ? "null" : $"({p.X * 304.8:F0},{p.Y * 304.8:F0},{p.Z * 304.8:F0})mm";
    }
}
