using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EditRailCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            ExternalEvent pickEvent = null;
            ExternalEvent createEvent = null;
            RailWindow window = null;
            try
            {
                if (RailWindow.TryActivateCurrent())
                    return Result.Succeeded;

                var uiApplication = commandData.Application;
                var uiDocument = uiApplication.ActiveUIDocument;
                var document = uiDocument?.Document;
                if (document == null)
                {
                    message = "Nenhum documento Revit está aberto.";
                    return Result.Failed;
                }

                Element member = null;
                var selectedIds = uiDocument.Selection.GetElementIds();
                if (selectedIds.Count == 1)
                {
                    var selected = document.GetElement(selectedIds.First());
                    if (IsRegisteredMember(selected, out _))
                        member = selected;
                }

                if (member == null)
                {
                    var reference = uiDocument.Selection.PickObject(
                        ObjectType.Element,
                        new RegisteredRailMemberFilter(),
                        "Selecione um membro do guarda-corpo SAGA para editar (ESC para cancelar). ");
                    member = document.GetElement(reference.ElementId);
                }

                if (!IsRegisteredMember(member, out var stored))
                    throw new InvalidOperationException(
                        "O elemento selecionado não pertence a um guarda-corpo SAGA registrado.");

                var context = RailEditContext.FromStored(stored);
                if (context?.Start == null || context.End == null)
                    throw new InvalidOperationException(
                        "Os dados geométricos do guarda-corpo estão incompletos.");
                context.SourceDocument = document;

                bool isInclined = Math.Abs(context.End.Z - context.Start.Z) * 304.8 > 1.0;
                var pickHandler = new LinePickHandler(
                    isInclined ? RailLinePickMode.Inclined : RailLinePickMode.Standard,
                    singleSelection: true);
                var createHandler = new RailCreationHandler
                {
                    IsInclinedRun = isInclined
                };
                pickEvent = ExternalEvent.Create(pickHandler);
                createEvent = ExternalEvent.Create(createHandler);
                window = new RailWindow(
                    pickEvent,
                    pickHandler,
                    createEvent,
                    createHandler,
                    context,
                    isInclined);
                if (!RailWindow.TryRegister(window))
                {
                    pickEvent.Dispose();
                    createEvent.Dispose();
                    RailWindow.TryActivateCurrent();
                    return Result.Succeeded;
                }

                var ownedPickEvent = pickEvent;
                var ownedCreateEvent = createEvent;
                window.Closed += (sender, args) =>
                {
                    ownedPickEvent.Dispose();
                    ownedCreateEvent.Dispose();
                };
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                RailWindow.Release(window);
                pickEvent?.Dispose();
                createEvent?.Dispose();
                SagaLog.Exception("EditRailCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool IsRegisteredMember(Element element, out RailAssemblyData stored)
        {
            stored = null;
            return RailAssemblyStore.TryRead(element, out stored) &&
                   stored?.MemberUniqueIds != null &&
                   stored.MemberUniqueIds.Contains(
                       element.UniqueId,
                       StringComparer.Ordinal);
        }

        private sealed class RegisteredRailMemberFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                IsRegisteredMember(element, out _);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
