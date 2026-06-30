using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Executa a criação da escada dentro de um IExternalEventHandler,
    /// garantindo que todas as chamadas à Revit API ocorram no contexto correto.
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

            try
            {
                // Se os pontos de clique não foram capturados, usa centro das vigas como fallback
                var lower  = doc.GetElement(LowerBeamId);
                var upper  = doc.GetElement(UpperBeamId);
                var loPt   = LowerPoint ?? Midpoint(lower);
                var upPt   = UpperPoint ?? Midpoint(upper);

                Log($"loPt={Fmt(loPt)}  upPt={Fmt(upPt)}");

                using (var tx = new Transaction(doc, "SAGA — Gerar Escada Metálica"))
                {
                    tx.Start();
                    try
                    {
                        var builder = new StringerBuilder(doc);
                        builder.Build(Definition, Config, loPt, upPt);
                        tx.Commit();
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
            return bb != null ? (bb.Min + bb.Max) * 0.5 : XYZ.Zero;
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
