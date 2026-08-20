using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Calcula o contorno da(s) chapa(s) de nervura a partir das dimensões do
    /// perfil W (h, bf, tf, tw) e do raio de concordância mesa-alma. Sem
    /// dependência da Revit API — totalmente testável de forma isolada.
    ///
    /// A chapa ocupa o vão livre entre mesas (altura = h - 2·tf) e vai da face
    /// da alma até a ponta da mesa (largura = (bf - tw)/2). Os dois cantos que
    /// encostam na alma recebem um chanfro a 45° para não colidir com o raio de
    /// concordância mesa-alma — os cantos do lado da ponta da mesa não precisam,
    /// pois ali não há concordância.
    /// </summary>
    public static class StiffenerCalculator
    {
        public static StiffenerDefinition Calculate(
            double hMm, double bfMm, double tfMm, double twMm, double? filletMm,
            double preferredSide, StiffenerConfig config)
        {
            var def = new StiffenerDefinition();

            if (config == null)
            {
                def.IsValid = false;
                def.Warnings.Add("Configuração indisponível.");
                return def;
            }

            if (hMm <= 0 || bfMm <= 0 || tfMm <= 0 || twMm <= 0)
            {
                def.IsValid = false;
                def.Warnings.Add("Dimensões do perfil W não puderam ser lidas (h, bf, tf ou tw ausentes).");
                return def;
            }

            double clearHeight    = hMm - 2 * tfMm;
            double clearHalfWidth = (bfMm - twMm) / 2.0;
            if (clearHeight <= 0 || clearHalfWidth <= 0)
            {
                def.IsValid = false;
                def.Warnings.Add("Vão livre do perfil insuficiente para a nervura — verifique h, bf, tf e tw.");
                return def;
            }

            double chamfer = config.ChamferOverride > 0.1
                ? config.ChamferOverride
                : filletMm.HasValue ? filletMm.Value + StiffenerDefaults.ChamferMargin
                                     : StiffenerDefaults.FallbackChamfer;

            double chamferCeiling = Math.Min(clearHeight, clearHalfWidth) / 2.0 - 0.1;
            if (chamfer > chamferCeiling) chamfer = chamferCeiling;

            if (chamfer < 0.1)
            {
                def.IsValid = false;
                def.Warnings.Add("Vão livre pequeno demais para aplicar o chanfro calculado — verifique as dimensões do perfil.");
                return def;
            }

            def.ChamferMm = chamfer;
            def.ChamferSource = filletMm.HasValue
                ? $"raio de concordância do perfil ({filletMm.Value:F1}mm) + {StiffenerDefaults.ChamferMargin:F0}mm de folga"
                : $"perfil não expõe raio de concordância — valor padrão ({StiffenerDefaults.FallbackChamfer:F0}mm)";

            var sides = config.Symmetric
                ? new[] { -1.0, 1.0 }
                : new[] { preferredSide >= 0 ? 1.0 : -1.0 };

            foreach (var side in sides)
                def.Plates.Add(BuildPlate(side, twMm, clearHalfWidth, clearHeight, chamfer));

            return def;
        }

        // Contorno hexagonal: aresta encostada na alma (com chanfro nos 2 cantos),
        // aresta na ponta da mesa (canto vivo). Construído em ordem CCW para o
        // lado positivo; para o lado negativo (espelhado), a ordem precisa ser
        // invertida — espelhar um eixo inverte o sentido do percurso.
        private static StiffenerPlate BuildPlate(
            double side, double twMm, double clearHalfWidth, double clearHeight, double chamfer)
        {
            double yWeb        = side * (twMm / 2.0);
            double yTip        = side * (twMm / 2.0 + clearHalfWidth);
            double yChamferEnd = side * (twMm / 2.0 + chamfer);
            double zTop        = clearHeight / 2.0;
            double zBot        = -clearHeight / 2.0;

            var points = new List<(double Y, double Z)>
            {
                (yWeb,        zBot + chamfer),
                (yWeb,        zTop - chamfer),
                (yChamferEnd, zTop),
                (yTip,        zTop),
                (yTip,        zBot),
                (yChamferEnd, zBot),
            };

            if (side < 0) points.Reverse();

            var plate = new StiffenerPlate { Side = side };
            plate.OutlineMm.AddRange(points);
            return plate;
        }
    }
}
