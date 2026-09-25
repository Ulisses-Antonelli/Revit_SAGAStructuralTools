using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.Robot
{
    public class RobotMemberResult
    {
        public int BarId;
        public string RawDesignation;
        public bool Success;
        public string Message;
    }

    public class ResolvedRobotMember
    {
        public RobotMember Member;
        public ProfileMapping Mapping;
        public bool IsColumn;
    }

    /// <summary>
    /// Cria as famílias nativas (viga/pilar) a partir de RobotMember JÁ RESOLVIDOS (perfil casado no
    /// catálogo, papel viga/pilar já decidido) — a resolução em si (RobotProfileResolver, análise de
    /// lacuna no catálogo) fica na camada de orquestração (ViewModel), não aqui, porque essa decisão
    /// pode depender de revisão do usuário antes de qualquer coisa ser criada no modelo.
    ///
    /// Cada elemento é processado em transação própria (uma falha isolada não derruba o lote inteiro,
    /// igual ao ElementConverter do conversor de IFC).
    /// </summary>
    internal static class RobotMemberPlacementService
    {
        public static List<RobotMemberResult> PlaceAll(
            Document hostDoc,
            IEnumerable<ResolvedRobotMember> resolvedMembers,
            RobotPlacementAnchor anchor,
            Action<string> progress)
        {
            var results = new List<RobotMemberResult>();

            foreach (var resolved in resolvedMembers)
            {
                var member = resolved.Member;
                var mapping = resolved.Mapping;
                bool isColumn = resolved.IsColumn;

                var startFt = anchor.ToRevit(member.Start);
                var endFt = anchor.ToRevit(member.End);

                // Pilar classificado como "quase vertical" (RobotMemberClassifier tolera até ~0,6°)
                // precisa ficar EXATAMENTE vertical - muita família de pilar do Revit (inclusive as
                // usadas aqui) só calcula os planos de corte das extremidades pra coluna vertical
                // exata, e quebra com "slanted column without any geometry" mesmo pra um resíduo de
                // XY minúsculo (ruído de arredondamento do próprio arquivo do Robot, não inclinação
                // real de projeto).
                if (isColumn)
                    endFt = new XYZ(startFt.X, startFt.Y, endFt.Z);

                if (startFt.DistanceTo(endFt) < 1e-6)
                {
                    results.Add(new RobotMemberResult { BarId = member.BarId, RawDesignation = member.Profile.RawDesignation, Success = false, Message = "Barra com comprimento nulo (nós coincidentes)." });
                    continue;
                }

                using (var tx = new Transaction(hostDoc, $"Robot -> Revit: barra {member.BarId}"))
                {
                    try
                    {
                        tx.Start();

                        // Suprime avisos geométricos do Revit (ex.: "Beam or Brace is slightly off
                        // axis" - inofensivo, comum em barras posicionadas por coordenadas calculadas
                        // em vez de encaixadas numa referência do Revit; o próprio Revit já rotula
                        // como "may be ignored"). Sem isso, o popup interrompe a importação em lote a
                        // cada barra.
                        var failOpts = tx.GetFailureHandlingOptions();
                        failOpts.SetFailuresPreprocessor(new RevitWarningSuppressor());
                        tx.SetFailureHandlingOptions(failOpts);

                        var searchCat = isColumn ? BuiltInCategory.OST_StructuralColumns : BuiltInCategory.OST_StructuralFraming;
                        var symbol = GetOrLoadFamilySymbol(hostDoc, mapping, searchCat);
                        if (!symbol.IsActive) symbol.Activate();

                        var curve = isColumn && startFt.Z > endFt.Z
                            ? Line.CreateBound(endFt, startFt)
                            : Line.CreateBound(startFt, endFt);

                        var level = GetNearestLevel(hostDoc, curve.GetEndPoint(0).Z);
                        var structType = isColumn ? StructuralType.Column : StructuralType.Beam;
                        var instance = hostDoc.Create.NewFamilyInstance(curve, symbol, level, structType);

                        if (member.Profile.GammaDegrees.HasValue && Math.Abs(member.Profile.GammaDegrees.Value) > 1e-6)
                        {
                            var axisLine = Line.CreateBound(curve.GetEndPoint(0), curve.GetEndPoint(1));
                            double angleRad = member.Profile.GammaDegrees.Value * Math.PI / 180.0;
                            ElementTransformUtils.RotateElement(hostDoc, instance.Id, axisLine, angleRad);
                        }

                        tx.Commit();
                        results.Add(new RobotMemberResult { BarId = member.BarId, RawDesignation = member.Profile.RawDesignation, Success = true, Message = $"Criado -> '{mapping.GerdauName}' {mapping.FamilyType}" });
                        progress?.Invoke($"[OK] barra {member.BarId}: {member.Profile.RawDesignation} -> {mapping.GerdauName}");
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        results.Add(new RobotMemberResult { BarId = member.BarId, RawDesignation = member.Profile.RawDesignation, Success = false, Message = ex.Message });
                        progress?.Invoke($"[ERRO] barra {member.BarId}: {ex.Message}");
                    }
                }
            }

            return results;
        }

        private static FamilySymbol GetOrLoadFamilySymbol(Document doc, ProfileMapping mapping, BuiltInCategory searchCat)
        {
            var typeName = mapping.FamilyType ?? mapping.GerdauName;

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(searchCat)
                .Cast<FamilySymbol>()
                .FirstOrDefault(sym =>
                    sym.Family.Name.Equals(mapping.GerdauName, StringComparison.OrdinalIgnoreCase) &&
                    sym.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            if (!doc.LoadFamilySymbol(mapping.FamilyPath, typeName, out var loaded))
                throw new InvalidOperationException($"Falha ao carregar '{mapping.FamilyPath}' tipo '{typeName}'.");
            return loaded;
        }

        private static Level GetNearestLevel(Document doc, double elevationFt)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - elevationFt))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Nenhum Level encontrado no documento.");
        }
    }
}
