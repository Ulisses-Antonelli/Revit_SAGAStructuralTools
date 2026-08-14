using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SAGAStructuralTools.Core.Ladder;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Detecta a seleção (Alt+clique) de um membro de escada marinheiro identificado
    /// pelo SAGA e abre, no próximo ciclo ocioso do Revit, a janela de edição —
    /// mesma mecânica do RailSelectionController. SelectionChanged permanece
    /// estritamente somente-leitura.
    /// </summary>
    internal sealed class LadderSelectionController : IDisposable
    {
        private readonly UIControlledApplication _application;

        private LadderEditContext _pendingContext;
        private string _lastAssemblyId;
        private LadderWindow _window;
        private ExternalEvent _pickEvent;
        private ExternalEvent _createEvent;

        public LadderSelectionController(UIControlledApplication application)
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

            if (_window != null || LadderWindow.HasOpenWindow) return;

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

                if (!LadderAssemblyStore.TryRead(element, out var stored))
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

                var context = LadderEditContext.FromStored(stored);
                if (context?.Placement == null || !context.Placement.IsValid)
                {
                    ClearPendingSelection();
                    return;
                }

                context.SourceDocument = document;
                _pendingContext = context;
                _lastAssemblyId = stored.AssemblyId;
                SagaLog.Write($"Escada marinheiro {stored.AssemblyId} selecionada para edição.");
            }
            catch (Exception ex)
            {
                ClearPendingSelection();
                SagaLog.Exception("LadderSelectionController.SelectionChanged", ex);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs args)
        {
            if (_pendingContext == null || _window != null) return;
            if (LadderWindow.HasOpenWindow)
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

                int memberCount = LadderAssemblyStore.FindMemberIds(activeDocument, context).Count;
                if (memberCount == 0)
                {
                    SagaLog.Write($"Edição da escada {context.AssemblyId} cancelada: nenhum membro encontrado.");
                    _lastAssemblyId = null;
                    return;
                }

                var pickHandler = new LadderPickHandler();
                var createHandler = new LadderCreationHandler();
                _pickEvent = ExternalEvent.Create(pickHandler);
                _createEvent = ExternalEvent.Create(createHandler);

                var window = new LadderWindow(_pickEvent, pickHandler, _createEvent, createHandler, context);
                if (!LadderWindow.TryRegister(window))
                {
                    _pickEvent.Dispose();
                    _createEvent.Dispose();
                    _pickEvent = null;
                    _createEvent = null;
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
                LadderWindow.Release(_window);
                _window = null;
                _pickEvent?.Dispose();
                _createEvent?.Dispose();
                _pickEvent = null;
                _createEvent = null;
                _lastAssemblyId = null;
                SagaLog.Exception("LadderSelectionController.OpenWindow", ex);
            }
        }

        private void OnWindowClosed(object sender, EventArgs args)
        {
            if (_window != null)
                _window.Closed -= OnWindowClosed;

            _window = null;
            _pickEvent?.Dispose();
            _createEvent?.Dispose();
            _pickEvent = null;
            _createEvent = null;
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
            else
            {
                _pickEvent?.Dispose();
                _createEvent?.Dispose();
            }

            _pendingContext = null;
            _lastAssemblyId = null;
        }
    }
}
