using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class BarConfigVm : ViewModelBase
    {
        private int          _rowIndex;
        private string       _familyPath;
        private string       _familyType;
        private BarAlignment _alignment  = BarAlignment.Axis;
        private double       _distance;

        public int    RowIndex    { get => _rowIndex;    set => Set(ref _rowIndex,    value); }
        public string FamilyPath  { get => _familyPath;  set { if (Set(ref _familyPath, value)) OnPropertyChanged(nameof(FamilyDisplay)); } }
        public string FamilyType  { get => _familyType;  set { if (Set(ref _familyType, value)) OnPropertyChanged(nameof(FamilyDisplay)); } }
        public BarAlignment Alignment { get => _alignment; set => Set(ref _alignment, value); }
        public double Distance    { get => _distance;    set => Set(ref _distance,    value); }

        public string FamilyDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FamilyPath)) return "(sem perfil)";
                var name = System.IO.Path.GetFileNameWithoutExtension(FamilyPath);
                return string.IsNullOrWhiteSpace(FamilyType) ? name : $"{name} · {FamilyType}";
            }
        }

        public BarConfig ToModel() => new BarConfig
        {
            FamilyPath = FamilyPath,
            FamilyType = FamilyType,
            Alignment  = Alignment,
            Distance   = Distance
        };
    }
}
