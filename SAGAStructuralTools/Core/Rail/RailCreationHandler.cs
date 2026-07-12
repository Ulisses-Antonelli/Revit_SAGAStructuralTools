using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Rail
{
    public class RailCreationHandler : IExternalEventHandler
    {
        public RailDefinition  Definition  { get; set; }
        public RailConfig      Config      { get; set; }
        public List<ElementId> SegmentIds  { get; set; }

        public event Action<string> Completed;

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public void Execute(UIApplication app)
        {
            Log("=== RailCreationHandler.Execute ===");
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { Notify("Nenhum documento Revit aberto."); return; }

            try
            {
                using (var tx = new Transaction(doc, "SAGA — Gerar Guarda-Corpo Metálico"))
                {
                    tx.Start();
                    try
                    {
                        var postBuilder     = new PostBuilder(doc);
                        var handrailBuilder = new HandrailBuilder(doc);
                        var infillBuilder   = new InfillBuilder(doc);

                        for (int i = 0; i < Definition.Segments.Count; i++)
                        {
                            var seg    = Definition.Segments[i];
                            var lineEl = doc.GetElement(SegmentIds[i]);

                            if (!(lineEl?.Location is LocationCurve lc)) continue;
                            if (!(lc.Curve is Line line)) continue;

                            var start = line.GetEndPoint(0);
                            var end   = line.GetEndPoint(1);

                            // Corrimão primeiro: mede o eixo central real e repassa aos
                            // montantes (topo) e travessas (distribuição), evitando o chute
                            // de dimensão por nome de parâmetro.
                            double? railAxisZ = handrailBuilder.Build(seg, Config, start, end);
                            postBuilder.Build(seg, Config, start, end, railAxisZ);
                            infillBuilder.Build(seg, Config, start, end, railAxisZ);
                        }

                        tx.Commit();
                        Log("=== Concluído com sucesso ===");
                        Notify(null);
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        Log($"ERRO no Build: {ex.Message}\n{ex.StackTrace}");
                        Notify(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ERRO externo: {ex.Message}");
                Notify(ex.Message);
            }
        }

        public string GetName() => "SAGACreateRail";

        private void Notify(string error) => Completed?.Invoke(error);

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }
    }
}
