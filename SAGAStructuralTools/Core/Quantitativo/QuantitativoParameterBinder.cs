using Autodesk.Revit.DB;
using System;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Quantitativo
{
    /// <summary>
    /// Cria (se ainda não existir) e vincula à categoria os parâmetros
    /// compartilhados SAGA_* usados pelo quantitativo — dados calculados
    /// (descrição/material unificados, comprimento e peso reais) gravados em
    /// cada instância pra a Tabela nativa do Revit poder agrupar/somar.
    ///
    /// O arquivo de parâmetros compartilhados é próprio do plugin (ao lado do
    /// .dll, mesmo local dos logs) — troca Application.SharedParametersFilename
    /// só durante a operação e restaura o valor original em seguida, pra não
    /// atrapalhar um arquivo de parâmetros compartilhados que o usuário já tenha
    /// configurado pra outra finalidade.
    /// </summary>
    internal static class QuantitativoParameterBinder
    {
        private const string GroupName = "SAGA";

        public static readonly string[] TextParams =
            { "SAGA_Descricao", "SAGA_Material_Qtv", "SAGA_Lote_Quantitativo" };

        public static readonly string[] NumberParams =
            { "SAGA_Comprimento_m", "SAGA_Peso_Unit_kgm", "SAGA_Peso_kg", "SAGA_Peso_Margem_kg" };

        /// <summary>Garante que todos os parâmetros SAGA_* existam e estejam vinculados às categorias informadas.</summary>
        public static void EnsureBound(Document doc, params BuiltInCategory[] categories)
        {
            var app = doc.Application;
            string originalFile = SafeGet(() => app.SharedParametersFilename);

            try
            {
                app.SharedParametersFilename = ResolveOwnFilePath();
                var defFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException("Não foi possível abrir/criar o arquivo de parâmetros compartilhados do SAGA.");

                var group = defFile.Groups.get_Item(GroupName) ?? defFile.Groups.Create(GroupName);

                var catSet = app.Create.NewCategorySet();
                foreach (var bic in categories)
                {
                    var cat = doc.Settings.Categories.get_Item(bic);
                    if (cat != null) catSet.Insert(cat);
                }

                foreach (var name in TextParams)
                    EnsureBinding(app, doc, group, name, SpecTypeId.String.Text, catSet);

                foreach (var name in NumberParams)
                    EnsureBinding(app, doc, group, name, SpecTypeId.Number, catSet);
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        private static void EnsureBinding(Autodesk.Revit.ApplicationServices.Application app, Document doc,
                                          DefinitionGroup group, string name, ForgeTypeId spec, CategorySet catSet)
        {
            var definition = group.Definitions.get_Item(name) as ExternalDefinition;
            if (definition == null)
            {
                var options = new ExternalDefinitionCreationOptions(name, spec);
                definition = group.Definitions.Create(options) as ExternalDefinition;
            }
            if (definition == null) return;

            if (doc.ParameterBindings.Contains(definition)) return;

            var binding = app.Create.NewInstanceBinding(catSet);
            doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data);
        }

        private static string ResolveOwnFilePath()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var path = Path.Combine(dir, "SAGA_SharedParameters.txt");
            if (!File.Exists(path)) File.WriteAllText(path, "# This is a Revit shared parameter file.\r\n# Do not edit manually.\r\n\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
            return path;
        }

        private static string SafeGet(Func<string> get)
        {
            try { return get(); } catch { return null; }
        }
    }
}
