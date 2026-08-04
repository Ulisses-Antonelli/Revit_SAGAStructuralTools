using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.Strap.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.UI.ViewModels.Strap
{
    internal sealed class StrapNodePreviewItem : ViewModelBase
    {
        private bool _isSelected;
        private bool _rotate90;

        internal StrapNodePreviewItem(
            string nodeId,
            long? elementId,
            int? sourceLine,
            ConsolidatedReaction reaction,
            IEnumerable<string> errors,
            ReactionValueSnapshot currentValues = null)
        {
            NodeId = nodeId ?? string.Empty;
            ElementId = elementId;
            SourceLine = sourceLine;
            OriginalReaction = reaction;
            CurrentValues = currentValues;
            Errors = (errors ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            _isSelected = IsPending;
        }

        internal ConsolidatedReaction OriginalReaction { get; }
        internal ReactionValueSnapshot CurrentValues { get; }
        internal IReadOnlyCollection<string> Errors { get; }
        public bool IsValid =>
            OriginalReaction != null && ElementId.HasValue &&
            CurrentValues != null && Errors.Count == 0;

        public string NodeId { get; }
        public long? ElementId { get; }
        public int? SourceLine { get; }
        public bool IsPending => IsValid &&
            !CurrentValues.IsEquivalentTo(DesiredValues);
        public bool IsAlreadyUpdated => IsValid && !IsPending;
        public bool CanSelect => IsPending;
        public string Classification => !IsValid
            ? "Com erro"
            : IsPending ? "Será atualizada" : "Já está atualizada";
        public string Status => IsValid ? Classification : string.Join(" ", Errors);
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (!CanSelect)
                    value = false;
                if (Set(ref _isSelected, value))
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool Rotate90
        {
            get => _rotate90;
            set
            {
                if (!IsValid)
                    value = false;
                if (Set(ref _rotate90, value))
                {
                    if (!IsPending)
                        IsSelected = false;
                    RaiseReactionProperties();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private ConsolidatedReaction DisplayReaction =>
            OriginalReaction == null
                ? null
                : Rotate90 ? OriginalReaction.Rotate90() : OriginalReaction;

        private ReactionValueSnapshot DesiredValues =>
            DisplayReaction == null ? null : ReactionValueSnapshot.FromReaction(DisplayReaction);

        public decimal? X1 => DisplayReaction?.X1;
        public decimal? X2 => DisplayReaction?.X2;
        public decimal? X3Max => DisplayReaction?.X3Max;
        public decimal? X3Min => DisplayReaction?.X3Min;
        public decimal? X4 => DisplayReaction?.X4;
        public decimal? X5 => DisplayReaction?.X5;
        public decimal? X6 => DisplayReaction?.X6;
        public IReadOnlyCollection<ReactionParameterComparison> ParameterDetails =>
            !IsValid ? Array.Empty<ReactionParameterComparison>() :
            CurrentValues.CompareTo(DesiredValues);

        internal event EventHandler SelectionChanged;
        internal event EventHandler StateChanged;

        internal ReactionWritePlanItem ToPlanItem()
            => new ReactionWritePlanItem(
                ElementId.Value,
                NodeId,
                DisplayReaction,
                CurrentValues);

        private void RaiseReactionProperties()
        {
            OnPropertyChanged(nameof(X1));
            OnPropertyChanged(nameof(X2));
            OnPropertyChanged(nameof(X3Max));
            OnPropertyChanged(nameof(X3Min));
            OnPropertyChanged(nameof(X4));
            OnPropertyChanged(nameof(X5));
            OnPropertyChanged(nameof(X6));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsAlreadyUpdated));
            OnPropertyChanged(nameof(CanSelect));
            OnPropertyChanged(nameof(Classification));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(ParameterDetails));
        }
    }
}
