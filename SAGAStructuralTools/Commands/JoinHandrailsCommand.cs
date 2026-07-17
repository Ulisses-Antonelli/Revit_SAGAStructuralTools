using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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
    public class JoinHandrailsCommand : IExternalCommand
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
                SagaLog.Exception("JoinHandrailsCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Unir corrimãos",
                    $"A ferramenta foi encerrada por um erro inesperado:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static Result RunContinuous(
            UIDocument uiDocument,
            Document document)
        {
            double activeRadiusMm = _lastRadiusMm;
            bool radiusConfirmed = false;
            bool sagaWarningAcknowledged = false;
            int createdConnections = 0;

            while (true)
            {
                var filter = new StraightBeamPointSelectionFilter();
                PickedMember first;
                string hint = radiusConfirmed
                    ? $" (R={activeRadiusMm:F3} mm; {createdConnections} união(ões); Esc encerra)"
                    : " (Esc encerra)";
                try
                {
                    first = PickMember(
                        uiDocument,
                        document,
                        filter,
                        "Selecione o primeiro corrimão perto da ponta que será unida" + hint);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return createdConnections > 0
                        ? Result.Succeeded
                        : Result.Cancelled;
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("JoinHandrailsCommand.PickFirst", ex);
                    ShowCreateError(ex.Message, "Selecione outro perfil ou pressione Esc para encerrar.");
                    continue;
                }

                PickedMember second;
                try
                {
                    second = PickMember(
                        uiDocument,
                        document,
                        filter,
                        "Selecione o segundo corrimão perto da ponta que será unida (Esc reinicia o par)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("JoinHandrailsCommand.PickSecond", ex);
                    ShowCreateError(ex.Message, "Selecione outro par ou pressione Esc para encerrar.");
                    continue;
                }

                if (first.Id.Equals(second.Id))
                {
                    ShowCreateError(
                        "Selecione dois corrimãos diferentes.",
                        "Selecione outro par ou pressione Esc para encerrar.");
                    continue;
                }

                var directRequest = new RoundedCornerRequest(
                    first.Id,
                    second.Id,
                    first.CornerEnd,
                    second.CornerEnd);
                PickedMember middle = null;
                var modeDecision = AskForConnectionMode();
                if (modeDecision == IntermediateDecision.End)
                {
                    return createdConnections > 0
                        ? Result.Succeeded
                        : Result.Cancelled;
                }
                if (modeDecision == IntermediateDecision.Retry)
                    continue;

                bool automatic =
                    modeDecision == IntermediateDecision.AutomaticMiddle;
                bool compound =
                    automatic || modeDecision == IntermediateDecision.SelectMiddle;
                if (modeDecision == IntermediateDecision.Direct)
                {
                    try
                    {
                        RoundedCornerService.ValidateSelection(
                            document,
                            directRequest);
                    }
                    catch (RoundedCornerTransitionRequiredException ex)
                    {
                        SagaLog.Write(
                            "União direta indisponível; tentando patamar automático: " +
                            ex.Message);
                        modeDecision = IntermediateDecision.AutomaticMiddle;
                        automatic = true;
                        compound = true;
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception("JoinHandrailsCommand.ValidateDirect", ex);
                        ShowCreateError(
                            ex.Message,
                            "Clique perto das extremidades corretas e selecione outro par.");
                        continue;
                    }
                }

                if (automatic)
                {
                    try
                    {
                        RoundedCornerService.ValidateAutomaticCompoundSelection(
                            document,
                            first.Id,
                            first.CornerEnd,
                            second.Id,
                            second.CornerEnd);
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception(
                            "JoinHandrailsCommand.ValidateAutomaticCompound",
                            ex);
                        var fallback = AskForAutomaticFallback(ex.Message);
                        if (fallback == IntermediateDecision.End)
                        {
                            return createdConnections > 0
                                ? Result.Succeeded
                                : Result.Cancelled;
                        }
                        if (fallback == IntermediateDecision.Retry)
                            continue;

                        modeDecision = fallback;
                        automatic = false;
                        compound = true;
                    }
                }

                if (modeDecision == IntermediateDecision.SelectMiddle)
                {
                    try
                    {
                        middle = PickMember(
                            uiDocument,
                            document,
                            filter,
                            "Os dois trechos externos já foram selecionados. Agora selecione o corrimão horizontal entre eles, perto da união com o primeiro (Esc reinicia o conjunto)");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        continue;
                    }
                    catch (Exception pickException)
                    {
                        SagaLog.Exception("JoinHandrailsCommand.PickMiddle", pickException);
                        ShowCreateError(
                            pickException.Message,
                            "Selecione novamente os corrimãos.");
                        continue;
                    }
                }

                if (modeDecision == IntermediateDecision.SelectMiddle &&
                    (middle.Id.Equals(first.Id) || middle.Id.Equals(second.Id)))
                {
                    ShowCreateError(
                        "O trecho horizontal deve ser diferente dos dois corrimãos externos.",
                        "Selecione novamente os três trechos.");
                    continue;
                }

                if (modeDecision == IntermediateDecision.SelectMiddle)
                {
                    try
                    {
                        RoundedCornerService.ValidateCompoundSelection(
                            document,
                            first.Id,
                            first.CornerEnd,
                            middle.Id,
                            middle.CornerEnd,
                            second.Id,
                            second.CornerEnd);
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception("JoinHandrailsCommand.ValidateCompound", ex);
                        ShowCreateError(
                            ex.Message,
                            "Selecione novamente os três perfis, clicando perto das pontas corretas.");
                        continue;
                    }
                }

                bool hasSagaMember = HasSagaMember(first, second, middle);
                Action<double> validateRadius;
                if (automatic)
                {
                    validateRadius = radius =>
                        RoundedCornerService.CreateAutomaticCompoundPlan(
                            document,
                            first.Id,
                            first.CornerEnd,
                            second.Id,
                            second.CornerEnd,
                            radius);
                }
                else if (compound)
                {
                    validateRadius = radius => RoundedCornerService.CreateCompoundPlan(
                        document,
                        first.Id,
                        first.CornerEnd,
                        middle.Id,
                        middle.CornerEnd,
                        second.Id,
                        second.CornerEnd,
                        radius);
                }
                else
                {
                    validateRadius = radius =>
                        RoundedCornerService.ValidateRadius(
                            document,
                            directRequest,
                            radius);
                }

                bool mustShowDialog =
                    !radiusConfirmed ||
                    (hasSagaMember && !sagaWarningAcknowledged);
                if (!mustShowDialog)
                {
                    try
                    {
                        validateRadius(activeRadiusMm);
                    }
                    catch (InvalidOperationException)
                    {
                        mustShowDialog = true;
                    }
                }

                if (mustShowDialog)
                {
                    double? selectedRadius = PromptForValidRadius(
                        document,
                        first,
                        second,
                        middle,
                        modeDecision,
                        activeRadiusMm,
                        hasSagaMember && !sagaWarningAcknowledged,
                        validateRadius);
                    if (!selectedRadius.HasValue)
                        continue;

                    activeRadiusMm = selectedRadius.Value;
                    radiusConfirmed = true;
                    if (hasSagaMember) sagaWarningAcknowledged = true;
                }

                try
                {
                    int createdArcs;
                    string modeLabel;
                    if (automatic)
                    {
                        var plan = RoundedCornerService.CreateAutomaticCompoundPlan(
                            document,
                            first.Id,
                            first.CornerEnd,
                            second.Id,
                            second.CornerEnd,
                            activeRadiusMm);
                        var result = ApplyAutomaticCompound(document, plan);
                        createdArcs = result.CornerResults?.Length ?? 0;
                        modeLabel = "patamar automático";
                    }
                    else if (compound)
                    {
                        var plan = RoundedCornerService.CreateCompoundPlan(
                            document,
                            first.Id,
                            first.CornerEnd,
                            middle.Id,
                            middle.CornerEnd,
                            second.Id,
                            second.CornerEnd,
                            activeRadiusMm);
                        createdArcs = ApplyCompound(document, plan).Length;
                        modeLabel = "patamar existente";
                    }
                    else
                    {
                        var plan = RoundedCornerService.CreatePlan(
                            document,
                            directRequest,
                            activeRadiusMm);
                        ApplyDirect(document, plan);
                        createdArcs = 1;
                        modeLabel = "direto";
                    }

                    _lastRadiusMm = activeRadiusMm;
                    createdConnections++;
                    SagaLog.Write(
                        $"União de corrimãos criada: modo={modeLabel} | " +
                        $"arcos={createdArcs} | R={activeRadiusMm:F3}mm | " +
                        $"sessão={createdConnections}.");
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("JoinHandrailsCommand.Apply", ex);
                    ShowCreateError(
                        ex.Message,
                        "Nenhuma parte desta união foi mantida. Selecione outro conjunto ou pressione Esc.");
                }
            }
        }

        private static PickedMember PickMember(
            UIDocument uiDocument,
            Document document,
            StraightBeamPointSelectionFilter filter,
            string prompt)
        {
            filter.ResetCapturedPoint();
            var reference = uiDocument.Selection.PickObject(
                ObjectType.PointOnElement,
                filter,
                prompt);
            var point = GetPickedPoint(reference, filter);
            int cornerEnd = RoundedCornerService.ResolvePickedEnd(
                document,
                reference.ElementId,
                point);
            return new PickedMember
            {
                Id = reference.ElementId,
                CornerEnd = cornerEnd,
                Instance = document.GetElement(reference.ElementId) as FamilyInstance
            };
        }

        private static XYZ GetPickedPoint(
            Reference reference,
            StraightBeamPointSelectionFilter filter)
        {
            XYZ point = null;
            try
            {
                point = reference?.GlobalPoint;
            }
            catch
            {
                point = null;
            }

            if (IsFinitePoint(point))
                return point;

            var captured = filter.GetCapturedPoint(reference?.ElementId);
            return IsFinitePoint(captured) ? captured : null;
        }

        private static bool IsFinitePoint(XYZ point) =>
            point != null &&
            !double.IsNaN(point.X) && !double.IsInfinity(point.X) &&
            !double.IsNaN(point.Y) && !double.IsInfinity(point.Y) &&
            !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);

        private static double? PromptForValidRadius(
            Document document,
            PickedMember first,
            PickedMember second,
            PickedMember middle,
            IntermediateDecision mode,
            double initialRadiusMm,
            bool showSagaWarning,
            Action<double> validateRadius)
        {
            double requestedRadiusMm = initialRadiusMm;
            bool automatic = mode == IntermediateDecision.AutomaticMiddle;
            bool compound =
                automatic || mode == IntermediateDecision.SelectMiddle;
            while (true)
            {
                var dialog = new HandrailJoinWindow(
                    Describe(first?.Instance, "Trecho 1"),
                    automatic
                        ? "Patamar automático — será criado após confirmar"
                        : compound
                        ? Describe(middle?.Instance, "Patamar")
                        : Describe(second?.Instance, "Trecho 2"),
                    compound ? Describe(second?.Instance, "Trecho 3") : null,
                    automatic
                        ? $"{DescribeOrientation(first?.Instance)} → patamar automático → " +
                          $"{DescribeOrientation(second?.Instance)} (2 arcos)"
                        : compound
                        ? $"{DescribeOrientation(first?.Instance)} → patamar existente → " +
                          $"{DescribeOrientation(second?.Instance)} (2 arcos)"
                        : $"{DescribeOrientation(first?.Instance)} → " +
                          $"{DescribeOrientation(second?.Instance)} (1 arco)",
                    requestedRadiusMm,
                    showSagaWarning);
                new WindowInteropHelper(dialog).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;

                if (dialog.ShowDialog() != true)
                    return null;

                requestedRadiusMm = dialog.RadiusMm;
                try
                {
                    validateRadius(requestedRadiusMm);
                    return requestedRadiusMm;
                }
                catch (InvalidOperationException ex)
                {
                    TaskDialog.Show("SAGA - Unir corrimãos", ex.Message);
                }
            }
        }

        private static RoundedCornerResult ApplyDirect(
            Document document,
            RoundedCornerPlan plan)
        {
            using (var transaction = CreateTransaction(
                document,
                "SAGA - Unir corrimãos"))
            {
                try
                {
                    var result = RoundedCornerService.Apply(document, plan);
                    CommitOrThrow(transaction);
                    return result;
                }
                catch
                {
                    RollBackIfNeeded(transaction);
                    throw;
                }
            }
        }

        private static RoundedCornerResult[] ApplyCompound(
            Document document,
            RoundedCornerCompoundPlan plan)
        {
            using (var transaction = CreateTransaction(
                document,
                "SAGA - Unir corrimãos pelo patamar"))
            {
                try
                {
                    var results = RoundedCornerService.ApplyCompound(document, plan);
                    CommitOrThrow(transaction);
                    return results;
                }
                catch
                {
                    RollBackIfNeeded(transaction);
                    throw;
                }
            }
        }

        private static RoundedCornerAutomaticCompoundResult ApplyAutomaticCompound(
            Document document,
            RoundedCornerAutomaticCompoundPlan plan)
        {
            using (var transaction = CreateTransaction(
                document,
                "SAGA - Criar e unir patamar do corrimão"))
            {
                try
                {
                    var result = RoundedCornerService.ApplyAutomaticCompound(
                        document,
                        plan);
                    CommitOrThrow(transaction);
                    return result;
                }
                catch
                {
                    RollBackIfNeeded(transaction);
                    throw;
                }
            }
        }

        private static Transaction CreateTransaction(
            Document document,
            string name)
        {
            var transaction = new Transaction(document, name);
            transaction.Start();
            var options = transaction.GetFailureHandlingOptions();
            options.SetForcedModalHandling(true);
            transaction.SetFailureHandlingOptions(options);
            return transaction;
        }

        private static void CommitOrThrow(Transaction transaction)
        {
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException(
                    "O Revit não confirmou a criação da união dos corrimãos.");
        }

        private static void RollBackIfNeeded(Transaction transaction)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
                transaction.RollBack();
        }

        private static bool HasSagaMember(
            PickedMember first,
            PickedMember second,
            PickedMember middle)
        {
            return IsSaga(first?.Instance) ||
                   IsSaga(second?.Instance) ||
                   IsSaga(middle?.Instance);
        }

        private static bool IsSaga(Element element) =>
            RoundedCornerStore.TryGetRegisteredAssemblyId(element, out _);

        private static string Describe(FamilyInstance instance, string fallback)
        {
            if (instance?.Symbol == null) return fallback;
            string family = instance.Symbol.Family?.Name ?? "Família";
            string type = instance.Symbol.Name ?? "Tipo";
            return $"{fallback}: #{instance.Id.GetId()} - {family} - {type}";
        }

        private static string DescribeOrientation(FamilyInstance instance)
        {
            var line = (instance?.Location as LocationCurve)?.Curve as Line;
            if (line == null || line.Length <= 1e-9)
                return "Perfil";

            double verticalRatio = Math.Abs(
                (line.GetEndPoint(1).Z - line.GetEndPoint(0).Z) /
                line.Length);
            double inclinationDegrees =
                Math.Asin(Math.Min(1.0, verticalRatio)) * 180.0 / Math.PI;
            return inclinationDegrees <= 0.5 ? "Horizontal" : "Inclinado";
        }

        private static IntermediateDecision AskForConnectionMode()
        {
            var dialog = new TaskDialog("SAGA - Unir corrimãos")
            {
                MainInstruction = "Como estes corrimãos devem ser unidos?",
                MainContent =
                    "No modo automático, a ferramenta tenta primeiro um único arco. " +
                    "Se os eixos forem paralelos ou não se encontrarem, ela calcula e " +
                    "cria o trecho horizontal necessário.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "Unir automaticamente (recomendado)",
                "Usa um arco direto quando os eixos se encontram; caso contrário, cria o patamar e dois arcos.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "Criar patamar horizontal",
                "Cria diretamente o trecho horizontal e dois arcos tangentes.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink3,
                "Usar trecho horizontal do patamar",
                "Seleciona um terceiro perfil e cria dois arcos tangentes.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink4,
                "Escolher outro par");
            dialog.DefaultButton = TaskDialogResult.CommandLink1;

            var result = dialog.Show();
            if (result == TaskDialogResult.CommandLink1)
                return IntermediateDecision.Direct;
            if (result == TaskDialogResult.CommandLink2)
                return IntermediateDecision.AutomaticMiddle;
            if (result == TaskDialogResult.CommandLink3)
                return IntermediateDecision.SelectMiddle;
            if (result == TaskDialogResult.CommandLink4)
                return IntermediateDecision.Retry;
            return IntermediateDecision.End;
        }

        private static IntermediateDecision AskForAutomaticFallback(string reason)
        {
            var dialog = new TaskDialog("SAGA - Unir corrimãos")
            {
                MainInstruction =
                    "Não foi possível calcular um patamar horizontal automático.",
                MainContent =
                    reason + "\n\n" +
                    "Você ainda pode indicar um trecho horizontal já modelado.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "Selecionar um trecho horizontal existente");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "Escolher outro par de corrimãos");
            dialog.DefaultButton = TaskDialogResult.CommandLink1;

            var result = dialog.Show();
            if (result == TaskDialogResult.CommandLink1)
                return IntermediateDecision.SelectMiddle;
            if (result == TaskDialogResult.CommandLink2)
                return IntermediateDecision.Retry;
            return IntermediateDecision.End;
        }

        private static void ShowCreateError(string message, string nextStep)
        {
            TaskDialog.Show(
                "SAGA - Unir corrimãos",
                $"Não foi possível criar esta união:\n\n{message}\n\n{nextStep}");
        }

        private enum IntermediateDecision
        {
            AutomaticMiddle,
            Direct,
            SelectMiddle,
            Retry,
            End
        }

        private sealed class PickedMember
        {
            internal ElementId Id { get; set; }
            internal int CornerEnd { get; set; }
            internal FamilyInstance Instance { get; set; }
        }

        private sealed class StraightBeamPointSelectionFilter : ISelectionFilter
        {
            private ElementId _capturedElementId;
            private XYZ _capturedPoint;

            public bool AllowElement(Element element) =>
                RoundedCornerMember.IsBeamSelectable(element);

            public bool AllowReference(Reference reference, XYZ position)
            {
                _capturedElementId = reference?.ElementId;
                _capturedPoint = position;
                return true;
            }

            internal void ResetCapturedPoint()
            {
                _capturedElementId = null;
                _capturedPoint = null;
            }

            internal XYZ GetCapturedPoint(ElementId elementId)
            {
                return elementId != null &&
                       _capturedElementId != null &&
                       elementId.Equals(_capturedElementId)
                    ? _capturedPoint
                    : null;
            }
        }
    }
}
