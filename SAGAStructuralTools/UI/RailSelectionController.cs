using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Detecta a seleção de um membro identificado pelo SAGA e abre, no próximo
    /// ciclo ocioso do Revit, a janela para reconstrução do trecho existente.
    /// SelectionChanged permanece estritamente somente-leitura.
    /// </summary>
    internal sealed class RailSelectionController : IDisposable
    {
        private readonly UIControlledApplication _application;

        private RailEditContext _pendingContext;
        private string _lastAssemblyId;
        private RailWindow _window;
        private ExternalEvent _pickEvent;
        private ExternalEvent _createEvent;

        public RailSelectionController(UIControlledApplication application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _application.SelectionChanged += OnSelectionChanged;
            _application.Idling += OnIdling;
            RailToolSession.CornerCommandStarted += OnCornerCommandStarted;
        }

        private void OnCornerCommandStarted() => ClearPendingSelection();

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (RailToolSession.IsCornerCommandActive)
            {
                ClearPendingSelection();
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
            {
                ClearPendingSelection();
                return;
            }

            if (_window != null || RailWindow.HasOpenWindow) return;

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

                if (!RailAssemblyStore.TryRead(element, out var stored))
                {
                    ClearPendingSelection();
                    return;
                }

                // Uma cópia feita pelo Revit herda a entidade, mas recebe outro UniqueId.
                // Não a tratamos como parte do original para evitar exclusão indevida.
                bool isRegisteredMember = stored.MemberUniqueIds != null &&
                    stored.MemberUniqueIds.Contains(element.UniqueId, StringComparer.Ordinal);
                if (!isRegisteredMember)
                {
                    SagaLog.Write($"Edição ignorada: o elemento {element.Id} é uma cópia não registrada.");
                    ClearPendingSelection();
                    return;
                }

                if (string.Equals(_lastAssemblyId, stored.AssemblyId,
                                  StringComparison.OrdinalIgnoreCase))
                    return;

                var context = RailEditContext.FromStored(stored);
                if (context?.Start == null || context.End == null)
                {
                    ClearPendingSelection();
                    return;
                }

                context.SourceDocument = document;
                _pendingContext = context;
                _lastAssemblyId = stored.AssemblyId;
                SagaLog.Write($"Guarda-corpo {stored.AssemblyId} selecionado para edição.");
            }
            catch (Exception ex)
            {
                ClearPendingSelection();
                SagaLog.Exception("RailSelectionController.SelectionChanged", ex);
            }
        }

        private void OnIdling(object sender, IdlingEventArgs args)
        {
            if (RailToolSession.IsCornerCommandActive)
            {
                ClearPendingSelection();
                return;
            }

            if (_pendingContext == null || _window != null) return;
            if (RailWindow.HasOpenWindow)
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
                if (activeDocument == null)
                {
                    SagaLog.Write(
                        $"Edição {context.AssemblyId} cancelada: Idling sem documento ativo " +
                        $"(sender={sender?.GetType().FullName ?? "null"}).");
                    _lastAssemblyId = null;
                    return;
                }

                if (!context.MatchesDocument(activeDocument))
                {
                    SagaLog.Write(
                        $"Edição {context.AssemblyId} cancelada: o documento ativo mudou " +
                        $"('{context.SourceDocument?.Title}' → '{activeDocument.Title}').");
                    _lastAssemblyId = null;
                    return;
                }

                int memberCount = RailAssemblyStore.FindMemberIds(activeDocument, context).Count;
                if (memberCount == 0)
                {
                    SagaLog.Write(
                        $"Edição {context.AssemblyId} cancelada: nenhum membro registrado foi encontrado.");
                    _lastAssemblyId = null;
                    return;
                }

                SagaLog.Write(
                    $"Edição {context.AssemblyId}: documento confirmado e {memberCount} membros encontrados.");

                bool isInclined = context.Start != null && context.End != null &&
                                  Math.Abs(context.End.Z - context.Start.Z) * 304.8 > 1.0;
                var pickHandler = new LinePickHandler(
                    isInclined ? RailLinePickMode.Inclined : RailLinePickMode.Standard,
                    singleSelection: true);
                var createHandler = new RailCreationHandler
                {
                    IsInclinedRun = isInclined
                };
                _pickEvent = ExternalEvent.Create(pickHandler);
                _createEvent = ExternalEvent.Create(createHandler);

                var window = new RailWindow(
                    _pickEvent, pickHandler, _createEvent, createHandler, context, isInclined);
                if (!RailWindow.TryRegister(window))
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

                SagaLog.Write($"Janela de edição aberta para {context.AssemblyId}.");
            }
            catch (Exception ex)
            {
                if (_window != null) _window.Closed -= OnWindowClosed;
                RailWindow.Release(_window);
                _window = null;
                _pickEvent?.Dispose();
                _createEvent?.Dispose();
                _pickEvent = null;
                _createEvent = null;
                _lastAssemblyId = null;
                SagaLog.Exception("RailSelectionController.OpenWindow", ex);
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
            RailToolSession.CornerCommandStarted -= OnCornerCommandStarted;

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
