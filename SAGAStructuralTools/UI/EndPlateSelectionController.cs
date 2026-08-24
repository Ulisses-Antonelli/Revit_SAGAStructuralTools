using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SAGAStructuralTools.Core.EndPlate;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Detecta a seleção (Alt+clique) de uma chapa de topo identificada pelo
    /// SAGA e abre, no próximo ciclo ocioso do Revit, a janela de edição —
    /// mesma mecânica de <see cref="StiffenerSelectionController"/>.
    /// SelectionChanged permanece estritamente somente-leitura.
    /// </summary>
    internal sealed class EndPlateSelectionController : IDisposable
    {
        private readonly UIControlledApplication _application;

        private EndPlateEditContext _pendingContext;
        private string _lastAssemblyId;
        private EndPlateWindow _window;

        private ExternalEvent _pickSingleEvent;
        private EndPlatePickHandler _pickSingleHandler;
        private ExternalEvent _pickTwoEvent;
        private EndPlateTwoPickHandler _pickTwoHandler;
        private ExternalEvent _createEvent;
        private EndPlateCreationHandler _createHandler;

        public EndPlateSelectionController(UIControlledApplication application)
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

            if (_window != null || EndPlateWindow.HasOpenWindow) return;

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

                if (!EndPlateAssemblyStore.TryRead(element, out var stored))
                {
                    ClearPendingSelection();
                    return;
                }

                bool isRegisteredMember = stored.MemberUniqueIds != null &&
                    stored.MemberUniqueIds.Contains(element.UniqueId, StringComparer.Ordinal);
                if (!isRegisteredMember)
                {
                    SagaLog.Write($"Edição de chapa de topo ignorada: o elemento {element.Id} é uma cópia não registrada.");
                    ClearPendingSelection();
                    return;
                }

                if (string.Equals(_lastAssemblyId, stored.AssemblyId,
                                  StringComparison.OrdinalIgnoreCase))
                    return;

                var context = EndPlateEditContext.FromStored(stored);
                if (context?.Config == null)
                {
                    ClearPendingSelection();
                    return;
                }

                context.SourceDocument = document;
                _pendingContext = context;
                _lastAssemblyId = stored.AssemblyId;
                SagaLog.Write($"Chapa de topo {stored.AssemblyId} selecionada para edição.");
            }
            catch (Exception ex)
            {
                ClearPendingSelection();
                SagaLog.Exception("EndPlateSelectionController.SelectionChanged", ex);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs args)
        {
            if (_pendingContext == null || _window != null) return;
            if (EndPlateWindow.HasOpenWindow)
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
                    SagaLog.Write($"Edição da chapa de topo {context.AssemblyId} cancelada: documento ativo mudou.");
                    _lastAssemblyId = null;
                    return;
                }

                int memberCount = EndPlateAssemblyStore.FindMemberIds(activeDocument, context).Count;
                if (memberCount == 0)
                {
                    SagaLog.Write($"Edição da chapa de topo {context.AssemblyId} cancelada: nenhum membro encontrado.");
                    _lastAssemblyId = null;
                    return;
                }

                _pickSingleHandler = new EndPlatePickHandler();
                _pickSingleEvent   = ExternalEvent.Create(_pickSingleHandler);
                _pickTwoHandler    = new EndPlateTwoPickHandler();
                _pickTwoEvent      = ExternalEvent.Create(_pickTwoHandler);
                _createHandler     = new EndPlateCreationHandler();
                _createEvent       = ExternalEvent.Create(_createHandler);

                var window = new EndPlateWindow(
                    _pickSingleEvent, _pickSingleHandler, _pickTwoEvent, _pickTwoHandler,
                    _createEvent, _createHandler, context);
                if (!EndPlateWindow.TryRegister(window))
                {
                    DisposeEvents();
                    ClearPendingSelection();
                    return;
                }
                _window = window;
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Closed += OnWindowClosed;

                window.Show();
                SagaLog.Write($"Janela de edição da chapa de topo aberta para {context.AssemblyId}.");
            }
            catch (Exception ex)
            {
                if (_window != null) _window.Closed -= OnWindowClosed;
                EndPlateWindow.Release(_window);
                _window = null;
                _lastAssemblyId = null;
                SagaLog.Exception("EndPlateSelectionController.OpenWindow", ex);
            }
        }

        private void OnWindowClosed(object sender, EventArgs args)
        {
            if (_window != null)
                _window.Closed -= OnWindowClosed;

            _window = null;
            _lastAssemblyId = null;
            DisposeEvents();
        }

        private void DisposeEvents()
        {
            _pickSingleEvent?.Dispose();
            _pickTwoEvent?.Dispose();
            _createEvent?.Dispose();
            _pickSingleEvent = null;
            _pickTwoEvent = null;
            _createEvent = null;
            _pickSingleHandler = null;
            _pickTwoHandler = null;
            _createHandler = null;
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
