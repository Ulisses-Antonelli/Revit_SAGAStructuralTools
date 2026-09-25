using System;
using System.Collections.Generic;
using System.IO;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Anexa linhas novas a um catálogo de tipos (.txt) real do usuário. Sempre faz uma cópia de
    /// segurança com timestamp do arquivo ORIGINAL antes da primeira escrita nele nesta execução -
    /// esse arquivo pertence ao usuário, não é gerado por nós, então qualquer escrita nele precisa
    /// de uma forma fácil de desfazer.
    /// </summary>
    public static class RobotCatalogWriter
    {
        private static readonly HashSet<string> BackedUpThisRun =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string AppendRow(string catalogTxtPath, string csvRow)
        {
            string backupPath = null;
            if (BackedUpThisRun.Add(catalogTxtPath))
            {
                backupPath = catalogTxtPath + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(catalogTxtPath, backupPath, overwrite: false);
            }

            var noBomUtf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using (var writer = new StreamWriter(catalogTxtPath, append: true, noBomUtf8))
                writer.WriteLine(csvRow);

            return backupPath;
        }
    }
}
