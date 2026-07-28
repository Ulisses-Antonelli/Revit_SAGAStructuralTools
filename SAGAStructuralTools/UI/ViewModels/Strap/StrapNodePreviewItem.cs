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
            IEnumerable<string> errors)
        {
            NodeId = nodeId ?? string.Empty;
            ElementId = elementId;
            SourceLine = sourceLine;
            OriginalReaction = reaction;
            Errors = (errors ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            _isSelected = IsValid;
        }

        internal ConsolidatedReaction OriginalReaction { get; }
        internal IReadOnlyCollection<string> Errors { get; }
        internal bool IsValid =>
            OriginalReaction != null && ElementId.HasValue && Errors.Count == 0;

        public string NodeId { get; }
        public long? ElementId { get; }
        public int? SourceLine { get; }
        public bool CanSelect => IsValid;
        public string Status => IsValid ? "Pronta" : string.Join(" ", Errors);
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
                if (!CanSelect)
                    value = false;
                if (Set(ref _rotate90, value))
                    RaiseReactionProperties();
            }
        }

        private ConsolidatedReaction DisplayReaction =>
            OriginalReaction == null
                ? null
                : Rotate90 ? OriginalReaction.Rotate90() : OriginalReaction;

        public decimal? X1 => DisplayReaction?.X1;
        public decimal? X2 => DisplayReaction?.X2;
        public decimal? X3Max => DisplayReaction?.X3Max;
        public decimal? X3Min => DisplayReaction?.X3Min;
        public decimal? X4 => DisplayReaction?.X4;
        public decimal? X5 => DisplayReaction?.X5;
        public decimal? X6 => DisplayReaction?.X6;

        internal event EventHandler SelectionChanged;

        internal ReactionWritePlanItem ToPlanItem()
            => new ReactionWritePlanItem(
                ElementId.Value,
                NodeId,
                DisplayReaction);

        private void RaiseReactionProperties()
        {
            OnPropertyChanged(nameof(X1));
            OnPropertyChanged(nameof(X2));
            OnPropertyChanged(nameof(X3Max));
            OnPropertyChanged(nameof(X3Min));
            OnPropertyChanged(nameof(X4));
            OnPropertyChanged(nameof(X5));
            OnPropertyChanged(nameof(X6));
        }
    }
}
