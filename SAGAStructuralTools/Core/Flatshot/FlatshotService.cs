using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAGAStructuralTools.Core.Flatshot
{
    /// <summary>
    /// "Congela" a vista ativa como uma Vista de Desenho nova e independente do
    /// modelo — mudanças futuras no modelo (perfil deslocado, chapa reposicionada)
    /// nunca mais afetam esse registro.
    ///
    /// A API do Revit não expõe remoção de linhas ocultas (HLR) como um serviço
    /// de geometria isolado — só existe dentro do pipeline de exportação/render.
    /// Por isso o único jeito real de obter o desenho 2D achatado exatamente como
    /// a vista aparece é: exportar a vista pra DWG (o Revit calcula o HLR de
    /// verdade) e reimportar como CAD import numa Vista de Desenho nova. O
    /// arquivo DWG é só um passo interno — criado e apagado dentro do mesmo
    /// comando, o usuário nunca precisa ver nem tocar nele.
    /// </summary>
    internal static class FlatshotService
    {
        public class FlatshotResult
        {
            public ElementId ViewId;
            public string ViewName;
        }

        public static FlatshotResult Create(Document doc, View sourceView, string desiredViewName)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SAGA_Flatshot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var exportOptions = new DWGExportOptions { MergedViews = false };
                var viewIds = new List<ElementId> { sourceView.Id };

                bool exported = doc.Export(tempDir, "flatshot", viewIds, exportOptions);
                if (!exported)
                    throw new InvalidOperationException("O Revit não conseguiu exportar a vista atual para DWG.");

                string dwgPath = Directory.GetFiles(tempDir, "*.dwg").FirstOrDefault();
                if (dwgPath == null)
                    throw new InvalidOperationException("A exportação não gerou nenhum arquivo DWG.");

                var draftingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
                if (draftingType == null)
                    throw new InvalidOperationException("Não foi encontrado nenhum tipo de Vista de Desenho neste projeto.");

                string uniqueName = MakeUniqueViewName(doc, desiredViewName);

                var newView = ViewDrafting.Create(doc, draftingType.Id);
                newView.Name = uniqueName;
                newView.Scale = sourceView.Scale;

                // ColorMode.Preserved grava a cor de cada linha/curva individualmente
                // no momento da importação (não "por categoria") - por isso mudar
                // Category.LineColor depois não sobrevive a explodir o import: a cor
                // já está gravada por objeto, não por referência à categoria.
                // BlackAndWhite resolve na raiz, no import, em vez de tentar corrigir depois.
                var importOptions = new DWGImportOptions
                {
                    ColorMode = ImportColorMode.BlackAndWhite,
                    OrientToView = true,
                    Placement = ImportPlacement.Origin,
                    ThisViewOnly = true,
                    VisibleLayersOnly = false
                };
                doc.Import(dwgPath, importOptions, newView, out _);

                return new FlatshotResult { ViewId = newView.Id, ViewName = uniqueName };
            }
            finally
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* limpeza de temporário é best-effort */ }
            }
        }

        private static string MakeUniqueViewName(Document doc, string desiredName)
        {
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(desiredName)) return desiredName;

            int counter = 2;
            string candidate;
            do { candidate = $"{desiredName} ({counter++})"; }
            while (existingNames.Contains(candidate));
            return candidate;
        }
    }
}
