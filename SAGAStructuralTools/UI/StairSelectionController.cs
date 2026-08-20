using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SAGAStructuralTools.Core.Stair;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Detecta a seleção (Alt+clique) de um membro de escada metálica identificado
    /// pelo SAGA e abre, no próximo ciclo ocioso do Revit, a janela de edição —
    /// mesma mecânica do RailSelectionController/LadderSelectionController.
    /// SelectionChanged permanece estritamente somente-leitura.
    /// </summary>
    internal sealed class StairSelectionController : IDisposable
    {
        private readonly UIControlledApplication _application;

        private StairEditContext _pendingContext;
        private string _lastAssemblyId;
        private StairWindow _window;

        public StairSelectionController(UIControlledApplication application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _application.SelectionChanged += OnSelectionChanged;
            _application.Idling += OnIdling;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
            {
                ClearPendingSelection();
                return;
            }

            if (_window != null || StairWindow.HasOpenWindow) return;

            try
            {
                var references = args.GetReferences();
                if (references == null || references.Count != 1)
                {
                    ClearPendingSelection();
                    return;
                }

                Document document = args.GetDocument();
                var reference = references.First();
                Element element = document?.GetElement(reference.ElementId);

                if (!StairAssemblyStore.TryRead(element, out var stored))
                {
                    ClearPendingSelection();
                    return;
                }

                // Cópias herdadas pelo Revit recebem outro UniqueId; não pertencem ao original.
                bool isRegisteredMember = stored.MemberUniqueIds != null &&
                    stored.MemberUniqueIds.Contains(element.UniqueId, StringComparer.Ordinal);
                if (!isRegisteredMember)
                {
                    SagaLog.Write($"Edição de escada ignorada: o elemento {element.Id} é uma cópia não registrada.");
                    ClearPendingSelection();
                    return;
                }

                if (string.Equals(_lastAssemblyId, stored.AssemblyId,
                                  StringComparison.OrdinalIgnoreCase))
                    return;

                var context = StairEditContext.FromStored(stored);
                if (context?.Config == null)
                {
                    ClearPendingSelection();
                    return;
                }

                context.SourceDocument = document;
                _pendingContext = context;
                _lastAssemblyId = stored.AssemblyId;
                SagaLog.Write($"Escada {stored.AssemblyId} selecionada para edição.");
            }
            catch (Exception ex)
            {
                ClearPendingSelection();
                SagaLog.Exception("StairSelectionController.SelectionChanged", ex);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs args)
        {
            if (_pendingContext == null || _window != null) return;
            if (StairWindow.HasOpenWindow)
            {
                ClearPendingSelection();
                return;
            }

            var context = _pendingContext;
            _pendingContext = null;

            try
            {
                var uiApplication = sender as UIApplication;
                var activeDocument = uiApplication?.ActiveUIDocument?.Document;
                if (activeDocument == null || !context.MatchesDocument(activeDocument))
                {
                    SagaLog.Write($"Edição da escada {context.AssemblyId} cancelada: documento ativo mudou.");
                    _lastAssemblyId = null;
                    return;
                }

                int memberCount = StairAssemblyStore.FindMemberIds(activeDocument, context).Count;
                if (memberCount == 0)
                {
                    SagaLog.Write($"Edição da escada {context.AssemblyId} cancelada: nenhum membro encontrado.");
                    _lastAssemblyId = null;
                    return;
                }

                var window = new StairWindow(uiApplication, context);
                if (!StairWindow.TryRegister(window))
                {
                    ClearPendingSelection();
                    return;
                }
                _window = window;
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Closed += OnWindowClosed;

                window.Show();
                SagaLog.Write($"Janela de edição da escada aberta para {context.AssemblyId}.");
            }
            catch (Exception ex)
            {
                if (_window != null) _window.Closed -= OnWindowClosed;
                StairWindow.Release(_window);
                _window = null;
                _lastAssemblyId = null;
                SagaLog.Exception("StairSelectionController.OpenWindow", ex);
            }
        }

        private void OnWindowClosed(object sender, EventArgs args)
        {
            if (_window != null)
                _window.Closed -= OnWindowClosed;

            _window = null;
            _lastAssemblyId = null;
        }

        private void ClearPendingSelection()
        {
            _pendingContext = null;
            _lastAssemblyId = null;
        }

        public void Dispose()
        {
            _application.SelectionChanged -= OnSelectionChanged;
            _application.Idling -= OnIdling;

            if (_window != null)
                _window.Close();

            _pendingContext = null;
            _lastAssemblyId = null;
        }
    }
}
