using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SAGAStructuralTools.Core.Rail
{
    public class HandrailBuilder
    {
        private readonly Document _doc;
        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public HandrailBuilder(Document doc) => _doc = doc;

        /// <summary>
        /// Cria o corrimão e retorna seu eixo central. HandrailHeight é um deslocamento
        /// vertical global em relação à linha-base, inclusive nos trechos inclinados.
        /// Retorna null se não há corrimão configurado.
        /// </summary>
        public RailRunGeometry Build(RailSegment seg, RailConfig config, RailRunGeometry run,
                                     ICollection<ElementId> createdIds = null)
        {
            if (string.IsNullOrWhiteSpace(config.HandrailFamilyPath)) return null;

            var symbol = GetOrLoadSymbol(config.HandrailFamilyPath, config.HandrailFamilyType);
            if (symbol == null)
                throw new InvalidOperationException("Família de corrimão não encontrada.");

            var catId = symbol.Family.FamilyCategory?.Id.GetId();
            if (catId != (int)BuiltInCategory.OST_StructuralFraming)
                throw new InvalidOperationException(
                    $"Família de corrimão '{symbol.Family.Name}' deve ser de Quadro Estrutural (Viga).\n" +
                    $"Categoria atual: {symbol.Family.FamilyCategory?.Name}");

            if (!symbol.IsActive) symbol.Activate();

            double heightFt  = config.HandrailHeight / 304.8;
            double axisOffFt = config.HandrailAxisOffset / 304.8;

            var axisRun = run.Offset(heightFt, axisOffFt);
            var start = axisRun.Start;
            var end = axisRun.End;

            var level = GetNearestLevel((start.Z + end.Z) / 2.0);
            var line  = Line.CreateBound(start, end);
            var inst  = _doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
            createdIds?.Add(inst.Id);

            // Estabilidade sob rotação: justificação travada no CENTRO (imune a flip de
            // perfil assimétrico) + posicionamento 100% por vetor global (MoveElement).
            LockCenterJustification(inst);
            SetCrossSectionRotation(inst, config.HandrailRotation);
            _doc.Regenerate();

            // A extensão Z inclui o desnível do próprio eixo quando o trecho é inclinado;
            // serve apenas para diagnóstico. A largura lateral continua sendo a seção real.
            double extentZMm = GeometryMeasure.ExtentAlongMm(_doc, inst.Id, XYZ.BasisZ);
            double wMm = GeometryMeasure.ExtentAlongMm(_doc, inst.Id, run.Lateral);
            // Z: a altura informada corresponde diretamente ao EIXO CENTRAL. Como a
            // justificação Z está travada no centro, não há compensação pela meia seção.
            // Y: justificativa por movimento lateral ±W/2 (Esquerda/Direita), 0 no Centro.
            double lateralJustFt = config.Justification == HandrailJustification.Left  ? -(wMm / 2.0) / 304.8
                                 : config.Justification == HandrailJustification.Right ? +(wMm / 2.0) / 304.8
                                 :                                                        0.0;
            var move = run.Lateral * lateralJustFt;
            if (move.GetLength() > 1e-9) ElementTransformUtils.MoveElement(_doc, inst.Id, move);

            axisRun = axisRun.Offset(0, lateralJustFt);

            Log($"HandrailBuilder: segmento {seg.Index + 1} | extensãoZ={extentZMm:F1}mm w={wMm:F1}mm | " +
                $"altura_eixo={config.HandrailHeight:F0}mm | inclinado={run.IsInclined} | just={config.Justification}");
            return axisRun;
        }

        /// <summary>Trava a justificação no Centro (Y e Z, YZ Uniforme) — estável sob rotação/flip.</summary>
        private static void LockCenterJustification(FamilyInstance inst)
        {
            if (inst == null) return;

            var yz = inst.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION);
            if (yz != null && !yz.IsReadOnly) yz.Set(0); // 0 = Uniforme

            var y = inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
            if (y != null && !y.IsReadOnly) y.Set((int)YJustification.Center);

            var z = inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            if (z != null && !z.IsReadOnly) z.Set((int)ZJustification.Center);
        }

        /// <summary>Rotação do corte transversal (graus → radianos) via STRUCTURAL_BEND_DIR_ANGLE.</summary>
        private static void SetCrossSectionRotation(FamilyInstance inst, double degrees)
        {
            if (inst == null || Math.Abs(degrees) < 1e-9) return;
            double rad = degrees * Math.PI / 180.0;
            var p = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)
                    ?? inst.LookupParameter("Rotação do corte transversal");
            if (p != null && !p.IsReadOnly) p.Set(rad);
        }

        private FamilySymbol GetOrLoadSymbol(string path, string typeName)
        {
            var familyName = Path.GetFileNameWithoutExtension(path);
            typeName       = typeName ?? "";

            var existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            if (!_doc.LoadFamilySymbol(path, typeName, out var loaded) || loaded == null)
                throw new InvalidOperationException($"Não foi possível carregar '{familyName}' tipo '{typeName}'.");

            return loaded;
        }

        private Level GetNearestLevel(double zFt)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.ProjectElevation - zFt))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Nenhum Level encontrado no documento.");
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }
    }
}
