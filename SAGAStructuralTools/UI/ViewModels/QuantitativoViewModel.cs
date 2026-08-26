using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Quantitativo;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class QuantitativoViewModel : ViewModelBase
    {
        private readonly Dispatcher                   _dispatcher;
        private readonly List<ElementId>               _selectedIds;
        private readonly List<QuantitativoMeasurement> _measurements;
        private readonly ExternalEvent                 _scheduleEvent;
        private readonly QuantitativoScheduleHandler    _scheduleHandler;
        private bool   _isSubmitting;
        private double _marginPercent = QuantitativoDefaults.MarginPercent;
        private string _statusText;
        private string _scheduleName = QuantitativoScheduleBuilder.DefaultName;

        public QuantitativoViewModel(List<ElementId> selectedIds, List<QuantitativoMeasurement> measurements,
                                     IEnumerable<string> warnings, int skippedCount,
                                     ExternalEvent scheduleEvent, QuantitativoScheduleHandler scheduleHandler)
        {
            _dispatcher       = Dispatcher.CurrentDispatcher;
            _selectedIds      = selectedIds;
            _measurements     = measurements;
            _scheduleEvent    = scheduleEvent;
            _scheduleHandler  = scheduleHandler;
            _scheduleHandler.Completed += OnScheduleCompleted;

            foreach (var w in warnings ?? Enumerable.Empty<string>()) Warnings.Add(w);
            if (skippedCount > 0)
                Warnings.Add($"{skippedCount} elemento(s) da seleção não são viga/pilar de aço e foram ignorados.");

            ExportCsvCommand      = new RelayCommand(_ => ExportCsv());
            CreateScheduleCommand = new RelayCommand(_ => CreateSchedule(), _ => !_isSubmitting && Items.Count > 0);

            Recalculate();
        }

        public double MarginPercent
        {
            get => _marginPercent;
            set { if (Set(ref _marginPercent, value)) Recalculate(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        /// <summary>Nome igual a uma tabela já criada por esta ferramenta → atualiza. Nome novo → cria. Nome de uma tabela alheia → recusa.</summary>
        public string ScheduleName
        {
            get => _scheduleName;
            set => Set(ref _scheduleName, value);
        }

        public ObservableCollection<QuantitativoLineItem> Items     { get; } = new ObservableCollection<QuantitativoLineItem>();
        public ObservableCollection<string>                Warnings { get; } = new ObservableCollection<string>();

        public string TotalLengthText       { get; private set; }
        public string TotalWeightText       { get; private set; }
        public string TotalWeightMarginText { get; private set; }

        private QuantitativoConfig BuildConfig() => new QuantitativoConfig { MarginPercent = MarginPercent };

        private void Recalculate()
        {
            var config = BuildConfig();
            var items = QuantitativoCalculator.Group(_measurements, config);

            Items.Clear();
            foreach (var i in items) Items.Add(i);

            var (lengthM, weightKg, weightMarginKg) = QuantitativoCalculator.Totals(items);
            TotalLengthText       = $"{lengthM:F1} m";
            TotalWeightText       = $"{weightKg:F1} kg";
            TotalWeightMarginText = $"{weightMarginKg:F1} kg";
            OnPropertyChanged(nameof(TotalLengthText));
            OnPropertyChanged(nameof(TotalWeightText));
            OnPropertyChanged(nameof(TotalWeightMarginText));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExportCsv()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (separado por ponto e vírgula)|*.csv",
                FileName = "Lista_de_Perfis.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                QuantitativoCsvExporter.Export(dlg.FileName, Items.ToList(), BuildConfig());
                StatusText = $"Exportado: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = null;
                System.Windows.MessageBox.Show(
                    $"Erro ao exportar CSV:\n\n{ex.Message}",
                    "SAGA Structural Tools",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void CreateSchedule()
        {
            _scheduleHandler.SelectedIds  = _selectedIds;
            _scheduleHandler.Config       = BuildConfig();
            _scheduleHandler.ScheduleName = ScheduleName;

            _isSubmitting = true;
            StatusText = null;
            CommandManager.InvalidateRequerySuggested();
            _scheduleEvent.Raise();
        }

        private void OnScheduleCompleted(string error)
        {
            _dispatcher.Invoke(() =>
            {
                _isSubmitting = false;
                CommandManager.InvalidateRequerySuggested();

                if (error == null)
                {
                    StatusText = $"Tabela \"{ScheduleName}\" criada/atualizada no navegador de projeto.";
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Erro ao criar a tabela:\n\n{error}",
                        "SAGA Structural Tools",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            });
        }

        public RelayCommand ExportCsvCommand      { get; }
        public RelayCommand CreateScheduleCommand { get; }
    }
}
