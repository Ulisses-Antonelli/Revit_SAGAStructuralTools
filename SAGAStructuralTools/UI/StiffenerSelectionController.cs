using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SAGAStructuralTools.Core.Stiffener;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Detecta a seleção (Alt+clique) de uma chapa de nervura identificada pelo
    /// SAGA e abre, no próximo ciclo ocioso do Revit, a janela de edição — mesma
    /// mecânica de <see cref="StairSelectionController"/>. SelectionChanged
    /// permanece estritamente somente-leitura.
    /// </summary>
    internal sealed class StiffenerSelectionController : IDisposable
    {
        private readonly UIControlledApplication _application;

        private StiffenerEditContext _pendingContext;
        private string _lastAssemblyId;
        private StiffenerWindow _window;

        private ExternalEvent          _pickEvent;
        private StiffenerPickHandler   _pickHandler;
        private ExternalEvent          _createEvent;
        private StiffenerCreationHandler _createHandler;

        public StiffenerSelectionController(UIControlledApplication application)
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

            if (_window != null || StiffenerWindow.HasOpenWindow) return;

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

                if (!StiffenerAssemblyStore.TryRead(element, out var stored))
                {
                    ClearPendingSelection();
                    return;
                }

                bool isRegisteredMember = stored.MemberUniqueIds != null &&
                    stored.MemberUniqueIds.Contains(element.UniqueId, StringComparer.Ordinal);
                if (!isRegisteredMember)
                {
                    SagaLog.Write($"Edição de nervura ignorada: o elemento {element.Id} é uma cópia não registrada.");
                    ClearPendingSelection();
                    return;
                }

                if (string.Equals(_lastAssemblyId, stored.AssemblyId,
                                  StringComparison.OrdinalIgnoreCase))
                    return;

                var context = StiffenerEditContext.FromStored(stored);
                if (context?.Config == null)
                {
                    ClearPendingSelection();
                    return;
                }

                context.SourceDocument = document;
                _pendingContext = context;
                _lastAssemblyId = stored.AssemblyId;
                SagaLog.Write($"Nervura {stored.AssemblyId} selecionada para edição.");
            }
            catch (Exception ex)
            {
                ClearPendingSelection();
                SagaLog.Exception("StiffenerSelectionController.SelectionChanged", ex);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs args)
        {
            if (_pendingContext == null || _window != null) return;
            if (StiffenerWindow.HasOpenWindow)
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
                    SagaLog.Write($"Edição da nervura {context.AssemblyId} cancelada: documento ativo mudou.");
                    _lastAssemblyId = null;
                    return;
                }

                int memberCount = StiffenerAssemblyStore.FindMemberIds(activeDocument, context).Count;
                if (memberCount == 0)
                {
                    SagaLog.Write($"Edição da nervura {context.AssemblyId} cancelada: nenhum membro encontrado.");
                    _lastAssemblyId = null;
                    return;
                }

                _pickHandler   = new StiffenerPickHandler();
                _pickEvent     = ExternalEvent.Create(_pickHandler);
                _createHandler = new StiffenerCreationHandler();
                _createEvent   = ExternalEvent.Create(_createHandler);

                var window = new StiffenerWindow(_pickEvent, _pickHandler, _createEvent, _createHandler, context);
                if (!StiffenerWindow.TryRegister(window))
                {
                    _pickEvent.Dispose();
                    _createEvent.Dispose();
                    ClearPendingSelection();
                    return;
                }
                _window = window;
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Closed += OnWindowClosed;

                window.Show();
                SagaLog.Write($"Janela de edição da nervura aberta para {context.AssemblyId}.");
            }
            catch (Exception ex)
            {
                if (_window != null) _window.Closed -= OnWindowClosed;
                StiffenerWindow.Release(_window);
                _window = null;
                _lastAssemblyId = null;
                SagaLog.Exception("StiffenerSelectionController.OpenWindow", ex);
            }
        }

        private void OnWindowClosed(object sender, EventArgs args)
        {
            if (_window != null)
                _window.Closed -= OnWindowClosed;

            _window = null;
            _lastAssemblyId = null;

            _pickEvent?.Dispose();
            _createEvent?.Dispose();
            _pickEvent = null;
            _createEvent = null;
            _pickHandler = null;
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
