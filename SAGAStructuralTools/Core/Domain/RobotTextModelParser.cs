using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Lê o arquivo texto estruturado exportado pelo Robot (formato "ROBOT97", visto em exportações
    /// do Robot Structural Analysis) e extrai só o que interessa pra recriar o modelo no Revit: nós
    /// (coordenadas), barras (conectividade) e perfis atribuídos (seção PROperties).
    ///
    /// Só validado contra UM arquivo de exemplo até agora — o usuário não tem certeza se outras
    /// exportações do Robot mantêm exatamente esse formato. Por isso o parser é sequencial e para
    /// assim que sai da seção PROperties (não tenta entender SUPports/RELeases/RIGid links/LOAds/
    /// COMbination, que têm suas próprias sub-seções "ELEments"/"NODes" com significado DIFERENTE —
    /// ex.: dentro de um CASe de carga, "NODes" lista força por nó, não coordenada). E qualquer barra
    /// ou linha que não bater com o esperado vira aviso em vez de ser ignorada silenciosamente, pra
    /// dar sinal claro se um arquivo de outro projeto tiver um formato diferente.
    /// </summary>
    public static class RobotTextModelParser
    {
        public static RobotParseResult Parse(string filePath)
        {
            // File.ReadAllText (sem Encoding explícito) detecta o BOM automaticamente — o arquivo do
            // Robot é UTF-16, não UTF-8/ASCII.
            var lines = File.ReadAllText(filePath)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            var result = new RobotParseResult();
            var nodes = new Dictionary<int, RobotNode>();
            var bars = new Dictionary<int, RobotBar>();
            var profilesByBarId = new Dictionary<int, RobotProfileAssignment>();

            int i = 0;

            // ── UNIts ──
            while (i < lines.Length && !IsExactKeyword(lines[i], "UNIts")) i++;
            if (i < lines.Length)
            {
                i++;
                if (i < lines.Length)
                {
                    var unitsLine = lines[i].Trim();
                    var lengthMatch = System.Text.RegularExpressions.Regex.Match(
                        unitsLine, @"LEN\w*\s*=\s*(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (lengthMatch.Success) result.LengthUnit = lengthMatch.Groups[1].Value;
                }
            }

            // ── NODes (tabela de coordenadas) ──
            // Match exato da linha (não StartsWith): o cabeçalho de resumo "NODes 1312
            // ELEments 1768", perto do topo do arquivo, também começa com "NODes" e
            // enganaria uma busca por prefixo, achando a tabela de coordenadas na hora
            // errada (confirmado testando contra uma amostra real do arquivo).
            i = 0;
            while (i < lines.Length && !IsExactKeyword(lines[i], "NODes")) i++;
            if (i >= lines.Length)
            {
                result.Warnings.Add("Não encontrei a seção NODes no arquivo — formato inesperado.");
                return result;
            }
            i++;
            for (; i < lines.Length && !IsExactKeyword(lines[i], "ELEments"); i++)
            {
                var line = lines[i];
                if (IsBlankOrComment(line)) continue;

                var tokens = Tokenize(line);
                if (tokens.Length != 4)
                {
                    result.Warnings.Add($"Linha de nó não reconhecida (esperava 4 campos): '{line.Trim()}'");
                    continue;
                }
                if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                    !TryParseDouble(tokens[1], out double x) ||
                    !TryParseDouble(tokens[2], out double y) ||
                    !TryParseDouble(tokens[3], out double z))
                {
                    result.Warnings.Add($"Não consegui ler a linha de nó: '{line.Trim()}'");
                    continue;
                }
                nodes[id] = new RobotNode { Id = id, X = x, Y = y, Z = z };
            }

            // ── ELEments (conectividade das barras) ──
            if (i >= lines.Length || !IsExactKeyword(lines[i], "ELEments"))
            {
                result.Warnings.Add("Não encontrei a seção ELEments logo após NODes — formato inesperado.");
                return result;
            }
            i++;
            for (; i < lines.Length && !IsExactKeyword(lines[i], "BOUndaries") && !IsExactKeyword(lines[i], "PROperties"); i++)
            {
                var line = lines[i];
                if (IsBlankOrComment(line)) continue;

                var tokens = Tokenize(line);
                if (tokens.Length != 3)
                {
                    result.Warnings.Add($"Linha de barra não reconhecida (esperava 3 campos): '{line.Trim()}'");
                    continue;
                }
                if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                    !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int startId) ||
                    !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int endId))
                {
                    result.Warnings.Add($"Não consegui ler a linha de barra: '{line.Trim()}'");
                    continue;
                }
                bars[id] = new RobotBar { Id = id, StartNodeId = startId, EndNodeId = endId };
            }

            // ── (pula BOUndaries/malha de piso — não tem perfil de aço) até achar PROperties ──
            while (i < lines.Length && !IsExactKeyword(lines[i], "PROperties")) i++;
            if (i >= lines.Length)
            {
                result.Warnings.Add("Não encontrei a seção PROperties no arquivo — não há como saber os perfis.");
                return result;
            }
            i++;

            // ── PROperties (perfil por barra, agrupado por material) ──
            string currentMaterial = null;
            var stopKeywords = new[] { "SUPports", "RELeases", "RIGid", "LOAds", "ANAlysis", "COMbination", "END" };
            for (; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsBlankOrComment(line)) continue;
                if (stopKeywords.Any(k => IsSectionStart(line, k))) break;

                var trimmed = line.Trim();
                if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
                {
                    currentMaterial = trimmed.Substring(1, trimmed.Length - 2);
                    continue;
                }

                var tokens = Tokenize(line);
                if (tokens.Length == 0) continue;

                int idCount = 0;
                while (idCount < tokens.Length && int.TryParse(tokens[idCount], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    idCount++;

                if (idCount == 0 || idCount == tokens.Length)
                {
                    result.Warnings.Add($"Linha de PROperties não reconhecida (sem perfil ao final): '{trimmed}'");
                    continue;
                }

                var remaining = tokens.Skip(idCount).ToArray();
                // remaining[0] = prefixo (W, HP, U, L...), remaining[1] = dimensões (ex.: "460x52")
                if (remaining.Length < 2)
                {
                    result.Warnings.Add($"Não consegui ler a designação do perfil em: '{trimmed}'");
                    continue;
                }
                string designation = $"{remaining[0]} {remaining[1]}";

                double? gamma = null;
                int gammaIdx = Array.FindIndex(remaining, t => t.Equals("GAmma", StringComparison.OrdinalIgnoreCase) || t.Equals("Gamma", StringComparison.OrdinalIgnoreCase));
                if (gammaIdx >= 0)
                {
                    // espera "GAmma" "=" "<valor>" — mas aceita "GAmma=<valor>" colado também
                    var gammaToken = remaining[gammaIdx];
                    var eqIdx = gammaToken.IndexOf('=');
                    if (eqIdx >= 0 && eqIdx < gammaToken.Length - 1)
                    {
                        TryParseDouble(gammaToken.Substring(eqIdx + 1), out double gVal);
                        gamma = gVal;
                    }
                    else if (gammaIdx + 2 < remaining.Length && remaining[gammaIdx + 1] == "=")
                    {
                        if (TryParseDouble(remaining[gammaIdx + 2], out double gVal)) gamma = gVal;
                    }
                }

                var assignment = new RobotProfileAssignment
                {
                    Material = currentMaterial,
                    RawDesignation = designation,
                    GammaDegrees = gamma
                };

                for (int k = 0; k < idCount; k++)
                {
                    int barId = int.Parse(tokens[k], CultureInfo.InvariantCulture);
                    profilesByBarId[barId] = assignment;
                }
            }

            // ── Junta barras + nós + perfis. Só vira RobotMember quem tem perfil atribuído — o
            // resto (elementos de malha, etc.) fica de fora por decisão explícita, não por omissão. ──
            foreach (var bar in bars.Values)
            {
                if (!profilesByBarId.TryGetValue(bar.Id, out var profile))
                    continue; // sem perfil — não é uma barra de aço catalogável, ignora sem aviso

                if (!nodes.TryGetValue(bar.StartNodeId, out var start))
                {
                    result.Warnings.Add($"Barra {bar.Id}: nó inicial {bar.StartNodeId} não encontrado.");
                    continue;
                }
                if (!nodes.TryGetValue(bar.EndNodeId, out var end))
                {
                    result.Warnings.Add($"Barra {bar.Id}: nó final {bar.EndNodeId} não encontrado.");
                    continue;
                }

                result.Members.Add(new RobotMember { BarId = bar.Id, Start = start, End = end, Profile = profile });
            }

            return result;
        }

        private static bool IsSectionStart(string line, string keyword)
            => line.Trim().StartsWith(keyword, StringComparison.OrdinalIgnoreCase);

        /// <summary>Como IsSectionStart, mas exige a linha inteira — usado nas âncoras onde a
        /// palavra-chave também aparece como prefixo de uma linha de resumo diferente
        /// (ex.: "NODes 1312 ELEments 1768" perto do topo do arquivo).</summary>
        private static bool IsExactKeyword(string line, string keyword)
            => line.Trim().Equals(keyword, StringComparison.OrdinalIgnoreCase);

        private static bool IsBlankOrComment(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length == 0 || trimmed.StartsWith(";");
        }

        private static string[] Tokenize(string line)
            => line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        private static bool TryParseDouble(string token, out double value)
            => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
