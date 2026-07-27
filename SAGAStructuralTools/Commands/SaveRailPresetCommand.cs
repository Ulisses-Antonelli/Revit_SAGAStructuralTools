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
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveRailPresetCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
                return Result.Failed;

            try
            {
                Element member = null;
                var selectedIds = uiDocument.Selection.GetElementIds();
                if (selectedIds.Count == 1)
                {
                    var selected = document.GetElement(selectedIds.First());
                    if (RailAssemblyStore.TryRead(selected, out _))
                        member = selected;
                }

                if (member == null)
                {
                    var picked = uiDocument.Selection.PickObject(
                        ObjectType.Element,
                        new RegisteredRailFilter(),
                        "Selecione um membro do guarda-corpo SAGA que servirá como padrão");
                    member = document.GetElement(picked.ElementId);
                }

                if (!RailAssemblyStore.TryRead(member, out var stored))
                    throw new InvalidOperationException(
                        "O elemento selecionado não pertence a um guarda-corpo SAGA.");

                var dialog = new SaveRailPresetWindow();
                new WindowInteropHelper(dialog).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                string presetName = dialog.PresetName;
                bool exists = RailPresetStore.List().Any(
                    name => string.Equals(
                        name,
                        presetName,
                        StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    var confirmation = new TaskDialog("SAGA - Salvar padrão")
                    {
                        MainInstruction = $"O padrão '{presetName}' já existe.",
                        MainContent = "Deseja substituir a configuração existente?",
                        CommonButtons = TaskDialogCommonButtons.Yes |
                                        TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No
                    };
                    if (confirmation.Show() != TaskDialogResult.Yes)
                        return Result.Cancelled;
                }

                RailPresetStore.Save(
                    presetName,
                    RailAssemblyStore.CloneConfig(stored.Config));
                TaskDialog.Show(
                    "SAGA - Salvar padrão",
                    $"Padrão '{presetName}' salvo com sucesso.\n\n" +
                    "Ele estará disponível nos demais projetos deste usuário.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("SaveRailPresetCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private sealed class RegisteredRailFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                RailAssemblyStore.TryRead(element, out _);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
