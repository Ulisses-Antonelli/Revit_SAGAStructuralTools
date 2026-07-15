using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoundRailCornerCommand : IExternalCommand
    {
        private static double _lastRadiusMm = 100.0;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            if (RailWindow.HasOpenWindow)
            {
                TaskDialog.Show(
                    "SAGA Structural Tools",
                    "Feche primeiro a janela de configuração do guarda-corpo e tente novamente.");
                return Result.Cancelled;
            }

            try
            {
                using (RailToolSession.BeginCornerCommand())
                    return RunContinuous(uiDocument, document);
            }
            catch (Exception ex)
            {
                SagaLog.Exception("RoundRailCornerCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Arredondar canto",
                    $"A ferramenta foi encerrada por um erro inesperado:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static Result RunContinuous(UIDocument uiDocument, Document document)
        {
            var filter = new StraightMemberSelectionFilter();
            double activeRadiusMm = _lastRadiusMm;
            bool radiusConfirmed = false;
            bool sagaWarningAcknowledged = false;
            int createdCorners = 0;

            try
            {
                while (true)
                {
                    Reference firstReference;
                    Reference secondReference;
                    try
                    {
                        string radiusHint = radiusConfirmed
                            ? $" (R={activeRadiusMm:F3} mm; {createdCorners} canto(s); Esc encerra)"
                            : " (Esc encerra)";
                        firstReference = uiDocument.Selection.PickObject(
                            ObjectType.Element,
                            filter,
                            "Selecione a primeira viga ou pilar reto do canto" + radiusHint);
                        secondReference = uiDocument.Selection.PickObject(
                            ObjectType.Element,
                            filter,
                            "Selecione a segunda viga ou pilar reto do canto" + radiusHint);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception("RoundRailCornerCommand.PickNext", ex);
                        TaskDialog.Show(
                            "SAGA - Arredondar canto",
                            $"A seleção foi encerrada por um erro inesperado:\n\n{ex.Message}");
                        return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;
                    }

                    try
                    {
                        if (firstReference.ElementId.Equals(secondReference.ElementId))
                            throw new InvalidOperationException("Selecione dois membros diferentes.");

                        var first = document.GetElement(firstReference.ElementId) as FamilyInstance;
                        var second = document.GetElement(secondReference.ElementId) as FamilyInstance;
                        RoundedCornerService.ValidateSelection(
                            document,
                            firstReference.ElementId,
                            secondReference.ElementId);

                        bool hasSagaMember =
                            RoundedCornerStore.TryGetRegisteredAssemblyId(first, out _) ||
                            RoundedCornerStore.TryGetRegisteredAssemblyId(second, out _);
                        bool mustShowDialog =
                            !radiusConfirmed ||
                            (hasSagaMember && !sagaWarningAcknowledged);

                        if (mustShowDialog)
                        {
                            double? selectedRadius = PromptForValidRadius(
                                document,
                                firstReference.ElementId,
                                secondReference.ElementId,
                                first,
                                second,
                                activeRadiusMm,
                                hasSagaMember && !sagaWarningAcknowledged);
                            if (!selectedRadius.HasValue)
                                return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;

                            activeRadiusMm = selectedRadius.Value;
                            radiusConfirmed = true;
                            if (hasSagaMember) sagaWarningAcknowledged = true;
                        }
                        else
                        {
                            try
                            {
                                RoundedCornerService.ValidateRadius(
                                    document,
                                    firstReference.ElementId,
                                    secondReference.ElementId,
                                    activeRadiusMm);
                            }
                            catch (InvalidOperationException)
                            {
                                double? adjustedRadius = PromptForValidRadius(
                                    document,
                                    firstReference.ElementId,
                                    secondReference.ElementId,
                                    first,
                                    second,
                                    activeRadiusMm,
                                    false);
                                if (!adjustedRadius.HasValue)
                                    return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;
                                activeRadiusMm = adjustedRadius.Value;
                            }
                        }

                        var result = ApplyCorner(
                            document,
                            firstReference.ElementId,
                            secondReference.ElementId,
                            activeRadiusMm);

                        _lastRadiusMm = activeRadiusMm;
                        createdCorners++;
                        SagaLog.Write(
                            $"Canto arredondado criado: elemento {result.CurvedElementId.GetId()} | " +
                            $"R={result.RadiusMm:F3}mm | deflexão={result.TurnAngleDegrees:F1} graus | " +
                            $"sessão={createdCorners}.");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception("RoundRailCornerCommand.CreateNext", ex);
                        TaskDialog.Show(
                            "SAGA - Arredondar canto",
                            $"Não foi possível criar este canto:\n\n{ex.Message}\n\n" +
                            "Selecione outro par ou pressione Esc para encerrar.");
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("RoundRailCornerCommand.RunContinuous", ex);
                try
                {
                    TaskDialog.Show(
                        "SAGA - Arredondar canto",
                        $"A ferramenta foi encerrada por um erro inesperado:\n\n{ex.Message}");
                }
                catch
                {
                    // A preservação dos cantos já confirmados tem prioridade.
                }
                return createdCorners > 0 ? Result.Succeeded : Result.Cancelled;
            }
        }

        private static double? PromptForValidRadius(
            Document document,
            ElementId firstId,
            ElementId secondId,
            FamilyInstance first,
            FamilyInstance second,
            double initialRadiusMm,
            bool showSagaWarning)
        {
            double requestedRadiusMm = initialRadiusMm;
            while (true)
            {
                var dialog = new RoundedCornerWindow(
                    Describe(first, "Perfil 1"),
                    Describe(second, "Perfil 2"),
                    requestedRadiusMm,
                    showSagaWarning);
                new WindowInteropHelper(dialog).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;

                if (dialog.ShowDialog() != true)
                    return null;

                requestedRadiusMm = dialog.RadiusMm;
                try
                {
                    RoundedCornerService.ValidateRadius(
                        document,
                        firstId,
                        secondId,
                        requestedRadiusMm);
                    return requestedRadiusMm;
                }
                catch (InvalidOperationException ex)
                {
                    TaskDialog.Show("SAGA - Arredondar canto", ex.Message);
                }
            }
        }

        private static RoundedCornerResult ApplyCorner(
            Document document,
            ElementId firstId,
            ElementId secondId,
            double radiusMm)
        {
            using (var transaction = new Transaction(
                document,
                "SAGA - Arredondar canto de perfis"))
            {
                transaction.Start();
                var failureOptions = transaction.GetFailureHandlingOptions();
                failureOptions.SetForcedModalHandling(true);
                transaction.SetFailureHandlingOptions(failureOptions);

                try
                {
                    var result = RoundedCornerService.Apply(
                        document,
                        firstId,
                        secondId,
                        radiusMm);

                    var status = transaction.Commit();
                    if (status != TransactionStatus.Committed)
                        throw new InvalidOperationException(
                            "O Revit não confirmou a criação do canto arredondado.");
                    return result;
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }
        }

        private static string Describe(FamilyInstance instance, string fallback)
        {
            if (instance?.Symbol == null) return fallback;
            string family = instance.Symbol.Family?.Name ?? "Família";
            string type = instance.Symbol.Name ?? "Tipo";
            return $"{fallback}: #{instance.Id.GetId()} - {family} - {type}";
        }

        private sealed class StraightMemberSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                RoundedCornerMember.IsSelectable(element);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
