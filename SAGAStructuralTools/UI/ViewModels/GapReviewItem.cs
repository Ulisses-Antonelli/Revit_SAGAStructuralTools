using SAGAStructuralTools.Core.Domain;
using System;
using System.IO;

namespace SAGAStructuralTools.UI.ViewModels
{
    /// <summary>Uma linha da grade de revisão: uma barra cujo perfil não casou no catálogo, com a
    /// sugestão de linha nova a incluir (peso da família irmã, se existir, senão estimativa
    /// geométrica) - sempre editável e sempre exige confirmação explícita antes de escrever.</summary>
    public class GapReviewItem : ViewModelBase
    {
        public int BarId { get; }
        public string RawDesignation { get; }
        public string RoleLabel { get; }
        public string SourceLabel { get; }
        public string CatalogFileName { get; }
        public bool CanInclude { get; }

        internal RobotMember Member { get; }
        internal bool IsColumn { get; }
        internal UDobradoCatalogGap Gap { get; }

        private double _weightKgPerMeter;
        public double WeightKgPerMeter { get => _weightKgPerMeter; set => Set(ref _weightKgPerMeter, value); }

        private bool _include;
        public bool Include { get => _include; set => Set(ref _include, value); }

        public GapReviewItem(RobotMember member, bool isColumn, UDobradoCatalogGap gap)
        {
            Member = member;
            IsColumn = isColumn;
            Gap = gap;
            BarId = member.BarId;
            RawDesignation = member.Profile.RawDesignation;
            RoleLabel = isColumn ? "Pilar" : "Viga";

            if (gap != null)
            {
                CanInclude = true;
                Include = true;
                WeightKgPerMeter = Math.Round(gap.SuggestedWeightKgPerMeter, 2);
                SourceLabel = gap.SuggestionFromSibling
                    ? "Peso já publicado na família irmã"
                    : "Estimativa geométrica (~1%, revise antes de confirmar)";
                CatalogFileName = Path.GetFileName(gap.TargetCatalogTxtPath);
            }
            else
            {
                CanInclude = false;
                Include = false;
                SourceLabel = "Perfil não reconhecido (não é um U dobrado) — sem sugestão automática";
                CatalogFileName = "";
            }
        }
    }
}
