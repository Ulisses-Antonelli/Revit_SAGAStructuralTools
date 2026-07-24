using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI;
using System;
using System.Collections.Generic;
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
                bool horizontalPair = IsHorizontalPair(first, second);
                PickedMember middle = null;
                Line referenceAxis = null;
                Line referenceBaseAxis = null;
                double horizontalOffsetMm = 0.0;
                XYZ offsetSidePoint = null;
                var modeDecision = AskForConnectionMode();
                if (modeDecision == IntermediateDecision.End)
                {
                    return createdConnections > 0
                        ? Result.Succeeded
                        : Result.Cancelled;
                }
                if (modeDecision == IntermediateDecision.Retry)
                    continue;
                if (modeDecision == IntermediateDecision.BatchReference)
                {
                    int batchCreated = RunReferenceBatch(
                        uiDocument,
                        document,
                        filter,
                        first,
                        second,
                        ref activeRadiusMm,
                        ref radiusConfirmed,
                        ref sagaWarningAcknowledged);
                    createdConnections += batchCreated;
                    continue;
                }

                bool automatic =
                    modeDecision == IntermediateDecision.AutomaticMiddle;
                bool referenceRoute =
                    modeDecision == IntermediateDecision.ReferenceRoute;
                bool compound =
                    automatic || referenceRoute ||
                    modeDecision == IntermediateDecision.SelectMiddle;
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
                            "União direta indisponível; tentando ligação automática pelos eixos: " +
                            ex.Message);
                        bool automaticReferenceCreated = false;
                        try
                        {
                            automaticReferenceCreated =
                                RoundedCornerService.TryCreateAutomaticReferenceAxis(
                                    document,
                                    first.Id,
                                    first.CornerEnd,
                                    second.Id,
                                    second.CornerEnd,
                                    out referenceAxis);
                        }
                        catch (Exception automaticException)
                        {
                            SagaLog.Exception(
                                "JoinHandrailsCommand.PrepareAutomaticFromMembers",
                                automaticException);
                        }

                        if (automaticReferenceCreated)
                        {
                            modeDecision = IntermediateDecision.AutomaticMiddle;
                            automatic = true;
                        }
                        else
                        {
                            SagaLog.Write(
                                "Ligação automática pelos eixos indisponível; " +
                                "solicitando uma aresta de referência.");
                            modeDecision = IntermediateDecision.ReferenceRoute;
                            referenceRoute = true;
                        }
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
                        if (referenceAxis == null &&
                            !RoundedCornerService.TryCreateAutomaticReferenceAxis(
                                document,
                                first.Id,
                                first.CornerEnd,
                                second.Id,
                                second.CornerEnd,
                                out referenceAxis))
                        {
                            throw new InvalidOperationException(
                                "Não foi possível determinar automaticamente um trecho " +
                                "de ligação para os corrimãos selecionados.");
                        }
                        RoundedCornerService.ValidateAutomaticCompoundSelection(
                            document,
                            first.Id,
                            first.CornerEnd,
                            second.Id,
                            second.CornerEnd,
                            referenceAxis);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        continue;
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
                        referenceRoute = false;
                        compound = true;
                    }
                }

                if (referenceRoute)
                {
                    try
                    {
                        referenceBaseAxis = PickHorizontalReferenceEdge(
                            uiDocument,
                            document,
                            first,
                            second,
                            out _,
                            "Selecione a aresta horizontal que definirá a posição e a direção do trecho transversal (Esc reinicia o par)");
                        offsetSidePoint = uiDocument.Selection.PickPoint(
                            "Clique no lado da aresta para onde o trajeto deve ser deslocado");
                        referenceAxis = BuildReferenceRouteAxis(
                            first,
                            second,
                            referenceBaseAxis,
                            horizontalOffsetMm,
                            offsetSidePoint);
                        // A geometria dependente do afastamento é validada somente
                        // depois que a janela permitir editar o valor. Validar aqui
                        // com zero impediria o usuário de corrigir uma referência
                        // inicialmente distante, curta ou atrás da ponta escolhida.
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception(
                            "JoinHandrailsCommand.ValidateReferenceRoute",
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
                        referenceRoute = false;
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
                            referenceAxis,
                            radius);
                }
                else if (referenceRoute)
                {
                    validateRadius = radius =>
                    {
                        if (horizontalPair)
                        {
                            RoundedCornerService.CreateAutomaticCompoundPlan(
                                document,
                                first.Id,
                                first.CornerEnd,
                                second.Id,
                                second.CornerEnd,
                                referenceAxis,
                                radius);
                        }
                        else
                        {
                            RoundedCornerService.CreateReferenceRoutePlan(
                                document,
                                first.Id,
                                first.CornerEnd,
                                second.Id,
                                second.CornerEnd,
                                referenceAxis,
                                radius);
                        }
                    };
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
                    automatic || referenceRoute || !radiusConfirmed ||
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
                        validateRadius,
                        null,
                        referenceRoute ? (double?)horizontalOffsetMm : null,
                        referenceRoute
                            ? (Action<double, double>)((_height, offset) =>
                            {
                                horizontalOffsetMm = offset;
                                referenceAxis = BuildReferenceRouteAxis(
                                    first,
                                    second,
                                    referenceBaseAxis,
                                    horizontalOffsetMm,
                                    offsetSidePoint);
                            })
                            : null);
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
                            referenceAxis,
                            activeRadiusMm);
                        var result = ApplyAutomaticCompound(document, plan);
                        createdArcs = result.CornerResults?.Length ?? 0;
                        modeLabel = "patamar automático";
                    }
                    else if (referenceRoute)
                    {
                        if (horizontalPair)
                        {
                            var plan =
                                RoundedCornerService.CreateAutomaticCompoundPlan(
                                    document,
                                    first.Id,
                                    first.CornerEnd,
                                    second.Id,
                                    second.CornerEnd,
                                    referenceAxis,
                                    activeRadiusMm);
                            var result = ApplyAutomaticCompound(document, plan);
                            createdArcs = result.CornerResults?.Length ?? 0;
                            modeLabel = "ligação horizontal pela aresta";
                        }
                        else
                        {
                            var plan =
                                RoundedCornerService.CreateReferenceRoutePlan(
                                    document,
                                    first.Id,
                                    first.CornerEnd,
                                    second.Id,
                                    second.CornerEnd,
                                    referenceAxis,
                                    activeRadiusMm);
                            var result = ApplyReferenceRoute(document, plan);
                            createdArcs = result.CornerResults?.Length ?? 0;
                            modeLabel = "trajeto pela aresta";
                        }
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

        private static int RunReferenceBatch(
            UIDocument uiDocument,
            Document document,
            StraightBeamPointSelectionFilter filter,
            PickedMember first,
            PickedMember second,
            ref double activeRadiusMm,
            ref bool radiusConfirmed,
            ref bool sagaWarningAcknowledged)
        {
            IList<Reference> remainingReferences;
            try
            {
                remainingReferences = uiDocument.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    filter,
                    "Selecione os demais corrimãos na ordem dos pares e clique em Concluir");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return 0;
            }

            if (remainingReferences == null || remainingReferences.Count == 0 ||
                remainingReferences.Count % 2 != 0)
            {
                ShowCreateError(
                    "A seleção em lote precisa conter pares completos além do primeiro par.",
                    "Selecione 2, 4, 6 ou mais perfis adicionais, sempre na ordem de cada união.");
                return 0;
            }

            var pairs = new List<PickedMember[]>
            {
                new[] { first, second }
            };
            for (int index = 0; index < remainingReferences.Count; index += 2)
            {
                var batchFirst = PickMemberFromReference(
                    document, remainingReferences[index], filter);
                var batchSecond = PickMemberFromReference(
                    document, remainingReferences[index + 1], filter);
                if (batchFirst.Id.Equals(batchSecond.Id))
                    throw new InvalidOperationException(
                        $"O par {pairs.Count + 1} contém o mesmo perfil duas vezes.");
                pairs.Add(new[] { batchFirst, batchSecond });
            }

            Line referenceBaseAxis;
            double suggestedHeightMm;
            XYZ sidePoint;
            try
            {
                referenceBaseAxis = PickHorizontalReferenceEdge(
                    uiDocument,
                    document,
                    first,
                    second,
                    out suggestedHeightMm,
                    "Selecione a aresta horizontal comum às uniões do lote");
                sidePoint = uiDocument.Selection.PickPoint(
                    "Clique no lado da aresta para onde os eixos horizontais devem ser deslocados");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return 0;
            }

            double commonOffsetMm = 0.0;
            var initialHeightsMm = new double[pairs.Count];
            var heightEditable = new bool[pairs.Count];
            var pairDescriptions = new string[pairs.Count];
            for (int index = 0; index < pairs.Count; index++)
            {
                var pair = pairs[index];
                var firstAxis =
                    (pair[0].Instance?.Location as LocationCurve)?.Curve as Line;
                var secondAxis =
                    (pair[1].Instance?.Location as LocationCurve)?.Curve as Line;
                heightEditable[index] =
                    !IsHorizontal(firstAxis) &&
                    !IsHorizontal(secondAxis);
                initialHeightsMm[index] =
                    GetStoredHandrailElevationOffsetMm(pair[0].Instance) ??
                    GetStoredHandrailElevationOffsetMm(pair[1].Instance) ??
                    suggestedHeightMm;
                pairDescriptions[index] =
                    $"{Describe(pair[0].Instance, "Trecho 1")}  ↔  " +
                    Describe(pair[1].Instance, "Trecho 2");
            }

            bool UsesReferenceRoute(int index)
            {
                var pair = pairs[index];
                var firstAxis =
                    (pair[0].Instance?.Location as LocationCurve)?.Curve as Line;
                var secondAxis =
                    (pair[1].Instance?.Location as LocationCurve)?.Curve as Line;
                if (firstAxis == null || secondAxis == null)
                    throw new InvalidOperationException(
                        $"O par {index + 1} não possui dois eixos retos válidos.");
                return IsHorizontal(firstAxis) != IsHorizontal(secondAxis);
            }

            bool UsesHorizontalPair(int index)
            {
                var pair = pairs[index];
                return IsHorizontalPair(pair[0], pair[1]);
            }

            Line BuildBatchReferenceAxis(int index, double heightMm, double offsetMm)
            {
                var pair = pairs[index];
                // Pares mistos e horizontal-horizontal preservam a cota do membro
                // horizontal existente. A altura editável do lote só controla pares
                // formados por dois trechos inclinados.
                if (UsesReferenceRoute(index) || UsesHorizontalPair(index))
                {
                    return BuildReferenceRouteAxis(
                        pair[0],
                        pair[1],
                        referenceBaseAxis,
                        offsetMm,
                        sidePoint);
                }

                var elevationReference = BuildReferenceAxis(
                    referenceBaseAxis,
                    heightMm,
                    offsetMm,
                    sidePoint);
                return RoundedCornerService.TryCreateInclinedPairReferenceAxis(
                    document,
                    pair[0].Id,
                    pair[0].CornerEnd,
                    pair[1].Id,
                    pair[1].CornerEnd,
                    elevationReference,
                    out Line automaticReference)
                    ? automaticReference
                    : elevationReference;
            }

            string ValidateBatchPair(
                int index,
                double heightMm,
                double offsetMm,
                double radiusMm)
            {
                try
                {
                    var pair = pairs[index];
                    var pairReferenceAxis = BuildBatchReferenceAxis(
                        index,
                        heightMm,
                        offsetMm);
                    if (UsesReferenceRoute(index))
                    {
                        RoundedCornerService.CreateReferenceRoutePlan(
                            document,
                            pair[0].Id,
                            pair[0].CornerEnd,
                            pair[1].Id,
                            pair[1].CornerEnd,
                            pairReferenceAxis,
                            radiusMm);
                    }
                    else
                    {
                        RoundedCornerService.CreateAutomaticCompoundPlan(
                            document,
                            pair[0].Id,
                            pair[0].CornerEnd,
                            pair[1].Id,
                            pair[1].CornerEnd,
                            pairReferenceAxis,
                            radiusMm);
                    }
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message;
                }
            }

            var batchDialog = new BatchHandrailJoinWindow(
                pairDescriptions,
                initialHeightsMm,
                activeRadiusMm,
                commonOffsetMm,
                heightEditable,
                ValidateBatchPair);
            new WindowInteropHelper(batchDialog).Owner =
                Process.GetCurrentProcess().MainWindowHandle;
            if (batchDialog.ShowDialog() != true)
                return 0;

            activeRadiusMm = batchDialog.RadiusMm;
            commonOffsetMm = batchDialog.OffsetMm;
            radiusConfirmed = true;
            var plans = new List<BatchGeneratedPlan>();
            for (int index = 0; index < pairs.Count; index++)
            {
                var pair = pairs[index];
                var pairReferenceAxis = BuildBatchReferenceAxis(
                    index,
                    batchDialog.HeightsMm[index],
                    commonOffsetMm);
                if (UsesReferenceRoute(index))
                {
                    plans.Add(new BatchGeneratedPlan
                    {
                        ReferenceRoute =
                            RoundedCornerService.CreateReferenceRoutePlan(
                                document,
                                pair[0].Id,
                                pair[0].CornerEnd,
                                pair[1].Id,
                                pair[1].CornerEnd,
                                pairReferenceAxis,
                                activeRadiusMm)
                    });
                }
                else
                {
                    plans.Add(new BatchGeneratedPlan
                    {
                        Automatic =
                            RoundedCornerService.CreateAutomaticCompoundPlan(
                                document,
                                pair[0].Id,
                                pair[0].CornerEnd,
                                pair[1].Id,
                                pair[1].CornerEnd,
                                pairReferenceAxis,
                                activeRadiusMm)
                    });
                }
                if (HasSagaMember(pair[0], pair[1], null))
                    sagaWarningAcknowledged = true;
            }

            using (var group = new TransactionGroup(
                document,
                "SAGA - Unir corrimãos em lote"))
            {
                group.Start();
                try
                {
                    foreach (var plan in plans)
                    {
                        if (plan.ReferenceRoute != null)
                            ApplyReferenceRoute(document, plan.ReferenceRoute);
                        else
                            ApplyAutomaticCompound(document, plan.Automatic);
                    }
                    if (group.Assimilate() != TransactionStatus.Committed)
                        throw new InvalidOperationException(
                            "O Revit não confirmou a criação do lote de uniões.");
                }
                catch
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                        group.RollBack();
                    throw;
                }
            }

            _lastRadiusMm = activeRadiusMm;
            return plans.Count;
        }

        private static PickedMember PickMemberFromReference(
            Document document,
            Reference reference,
            StraightBeamPointSelectionFilter filter)
        {
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

        private static Line PickHorizontalReferenceEdge(
            UIDocument uiDocument,
            Document document,
            PickedMember first,
            PickedMember second,
            out double suggestedHeightMm,
            string prompt)
        {
            suggestedHeightMm = 0.0;
            var filter = new HorizontalStraightEdgeSelectionFilter(document);
            var reference = uiDocument.Selection.PickObject(
                ObjectType.Edge,
                filter,
                prompt);
            var element = document.GetElement(reference.ElementId);
            var profileAxis = (element?.Location as LocationCurve)?.Curve as Line;
            var edge = element?.GetGeometryObjectFromReference(reference) as Edge;
            var rawLine = edge?.AsCurve() as Line;
            XYZ pickedPoint = null;
            try
            {
                pickedPoint = reference.GlobalPoint;
            }
            catch
            {
                pickedPoint = null;
            }
            var line = ResolveReferenceLineInModelCoordinates(
                element,
                rawLine,
                pickedPoint,
                writeLog: true);
            bool isSagaHandrail = RailAssemblyStore.TryRead(element, out _);
            var rawReference =
                isSagaHandrail && IsHorizontal(profileAxis)
                    ? profileAxis
                    : line;
            if (!IsHorizontal(rawReference))
                throw new InvalidOperationException(
                    "A referência selecionada não é uma aresta reta válida.");

            // Um corrimão SAGA horizontal já fornece a cota de seu próprio eixo.
            if (isSagaHandrail)
            {
                SagaLog.Write(
                    $"JoinHandrails: referência SAGA pelo eixo; " +
                    $"elemento={element.Id.GetId()}, " +
                    $"direção=({rawReference.Direction.X:F6}," +
                    $"{rawReference.Direction.Y:F6}," +
                    $"{rawReference.Direction.Z:F6}).");
                return rawReference;
            }

            SagaLog.Write(
                $"JoinHandrails: referência pela aresta clicada; " +
                $"elemento={element?.Id.GetId()}, " +
                $"direção=({rawReference.Direction.X:F6}," +
                $"{rawReference.Direction.Y:F6}," +
                $"{rawReference.Direction.Z:F6}), " +
                $"inícioModelo=({rawReference.GetEndPoint(0).X * 304.8:F1}," +
                $"{rawReference.GetEndPoint(0).Y * 304.8:F1}," +
                $"{rawReference.GetEndPoint(0).Z * 304.8:F1}) mm.");

            // Em pares com dois inclinados, a altura configurada ainda define a cota
            // sugerida. Quando existe ao menos um horizontal, a rota usa diretamente
            // a cota real dos eixos, portanto diferenças de configuração não impedem
            // a união.
            double? heightOffsetMm = GetStoredHandrailElevationOffsetMm(first?.Instance);
            double? secondOffsetMm = GetStoredHandrailElevationOffsetMm(second?.Instance);
            var firstSelectedAxis =
                (first?.Instance?.Location as LocationCurve)?.Curve as Line;
            var secondSelectedAxis =
                (second?.Instance?.Location as LocationCurve)?.Curve as Line;
            bool hasHorizontalMember =
                firstSelectedAxis != null &&
                secondSelectedAxis != null &&
                (IsHorizontal(firstSelectedAxis) ||
                 IsHorizontal(secondSelectedAxis));
            if (!hasHorizontalMember &&
                heightOffsetMm.HasValue && secondOffsetMm.HasValue &&
                Math.Abs(heightOffsetMm.Value - secondOffsetMm.Value) > 1.0)
            {
                throw new InvalidOperationException(
                    $"Os dois guarda-corpos possuem alturas de eixo diferentes " +
                    $"({heightOffsetMm.Value:F1} mm e {secondOffsetMm.Value:F1} mm). " +
                    "Use como referência um corrimão horizontal já modelado.");
            }

            double? elevationOffsetMm = heightOffsetMm ?? secondOffsetMm;
            suggestedHeightMm = elevationOffsetMm ?? 0.0;
            return rawReference;
        }

        private static Line ResolveReferenceLineInModelCoordinates(
            Element element,
            Line rawLine,
            XYZ pickedPoint,
            bool writeLog = false)
        {
            if (rawLine == null || rawLine.Length <= 1e-9)
                return rawLine;

            Line transformed = null;
            try
            {
                Transform transform = null;
                if (element is FamilyInstance familyInstance)
                    transform = familyInstance.GetTransform();
                else if (element is ImportInstance importInstance)
                    transform = importInstance.GetTotalTransform();

                if (transform != null)
                {
                    var transformedCurve =
                        rawLine.CreateTransformed(transform) as Line;
                    if (transformedCurve != null &&
                        transformedCurve.Length > 1e-9)
                    {
                        transformed = transformedCurve;
                    }
                }
            }
            catch
            {
                transformed = null;
            }

            if (!IsFinitePoint(pickedPoint))
                return transformed ?? rawLine;

            Line best = rawLine;
            string selectedSpace = "local/bruto";
            double rawDistance = DistanceToLineSegment(rawLine, pickedPoint);
            double transformedDistance = double.PositiveInfinity;
            if (transformed != null)
            {
                transformedDistance =
                    DistanceToLineSegment(transformed, pickedPoint);
                if (transformedDistance < rawDistance)
                {
                    best = transformed;
                    selectedSpace = "transformado/modelo";
                }
            }

            if (writeLog)
            {
                SagaLog.Write(
                    $"JoinHandrails: resolução espacial da aresta; " +
                    $"elemento={element?.Id.GetId()}, escolhido={selectedSpace}, " +
                    $"pontoGlobal=({pickedPoint.X * 304.8:F1}," +
                    $"{pickedPoint.Y * 304.8:F1}," +
                    $"{pickedPoint.Z * 304.8:F1}) mm, " +
                    $"distânciaBruta={rawDistance * 304.8:F1} mm, " +
                    $"distânciaTransformada=" +
                    $"{(double.IsInfinity(transformedDistance) ? "n/a" : (transformedDistance * 304.8).ToString("F1"))} mm.");
            }
            return best;
        }

        private static double DistanceToLineSegment(Line line, XYZ point)
        {
            var start = line.GetEndPoint(0);
            var end = line.GetEndPoint(1);
            var delta = end - start;
            double lengthSquared = delta.DotProduct(delta);
            if (lengthSquared <= 1e-18)
                return point.DistanceTo(start);

            double parameter =
                (point - start).DotProduct(delta) / lengthSquared;
            parameter = Math.Max(0.0, Math.Min(1.0, parameter));
            return point.DistanceTo(start + delta * parameter);
        }

        private static Line BuildReferenceAxis(
            Line baseAxis,
            double heightMm,
            double horizontalOffsetMm,
            XYZ sidePoint)
        {
            if (baseAxis == null)
                throw new InvalidOperationException("A aresta de referência é inválida.");
            var elevation = XYZ.BasisZ * (heightMm / 304.8);
            var direction = baseAxis.Direction;
            var perpendicular = new XYZ(-direction.Y, direction.X, 0.0).Normalize();
            if (sidePoint != null &&
                (sidePoint - baseAxis.GetEndPoint(0)).DotProduct(perpendicular) < 0.0)
                perpendicular = -perpendicular;
            var horizontal = perpendicular * (horizontalOffsetMm / 304.8);
            var translation = elevation + horizontal;
            return Line.CreateBound(
                baseAxis.GetEndPoint(0) + translation,
                baseAxis.GetEndPoint(1) + translation);
        }

        private static Line BuildReferenceRouteAxis(
            PickedMember first,
            PickedMember second,
            Line baseAxis,
            double horizontalOffsetMm,
            XYZ sidePoint)
        {
            var firstAxis =
                (first?.Instance?.Location as LocationCurve)?.Curve as Line;
            var secondAxis =
                (second?.Instance?.Location as LocationCurve)?.Curve as Line;
            bool firstHorizontal = IsHorizontal(firstAxis);
            bool secondHorizontal = IsHorizontal(secondAxis);
            if (!firstHorizontal && !secondHorizontal)
            {
                throw new InvalidOperationException(
                    "A rota posicionada pela aresta exige ao menos um corrimão horizontal.");
            }

            double targetElevation;
            string elevationSource;
            if (firstHorizontal && secondHorizontal)
            {
                targetElevation =
                    (firstAxis.GetEndPoint(0).Z +
                     firstAxis.GetEndPoint(1).Z +
                     secondAxis.GetEndPoint(0).Z +
                     secondAxis.GetEndPoint(1).Z) * 0.25;
                elevationSource = "corrimãos horizontais";
            }
            else
            {
                Line horizontalAxis = firstHorizontal ? firstAxis : secondAxis;
                targetElevation =
                    (horizontalAxis.GetEndPoint(0).Z +
                     horizontalAxis.GetEndPoint(1).Z) * 0.5;
                elevationSource = "corrimão horizontal";
            }
            Line offsetReference = BuildReferenceAxis(
                baseAxis,
                0.0,
                horizontalOffsetMm,
                sidePoint);
            Line positionedReference = Line.CreateBound(
                new XYZ(
                    offsetReference.GetEndPoint(0).X,
                    offsetReference.GetEndPoint(0).Y,
                    targetElevation),
                new XYZ(
                    offsetReference.GetEndPoint(1).X,
                    offsetReference.GetEndPoint(1).Y,
                    targetElevation));

            SagaLog.Write(
                $"JoinHandrails: posição e direção da aresta aplicadas; " +
                $"primeiro={first.Id.GetId()}, segundo={second.Id.GetId()}, " +
                $"fonteDaCota={elevationSource}, " +
                $"cota={targetElevation * 304.8:F1} mm, " +
                $"afastamento={horizontalOffsetMm:F3} mm, " +
                $"início=({positionedReference.GetEndPoint(0).X * 304.8:F1}," +
                $"{positionedReference.GetEndPoint(0).Y * 304.8:F1}," +
                $"{positionedReference.GetEndPoint(0).Z * 304.8:F1}) mm.");
            return positionedReference;
        }

        private static bool IsHorizontalPair(
            PickedMember first,
            PickedMember second)
        {
            var firstAxis =
                (first?.Instance?.Location as LocationCurve)?.Curve as Line;
            var secondAxis =
                (second?.Instance?.Location as LocationCurve)?.Curve as Line;
            return IsHorizontal(firstAxis) && IsHorizontal(secondAxis);
        }

        private static double? GetStoredHandrailElevationOffsetMm(Element element)
        {
            if (!RailAssemblyStore.TryRead(element, out var data) || data?.Config == null)
                return null;
            return data.Config.GlobalVerticalOffset + data.Config.HandrailHeight;
        }

        private static bool IsHorizontal(Line line)
        {
            if (line == null || line.Length <= 1e-9) return false;
            double inclinationDegrees =
                Math.Asin(Math.Min(1.0, Math.Abs(line.Direction.Z))) *
                180.0 / Math.PI;
            return inclinationDegrees <= 0.5;
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
            Action<double> validateRadius,
            double? initialReferenceHeightMm = null,
            double? initialHorizontalOffsetMm = null,
            Action<double, double> referencePlacementChanged = null,
            int batchIndex = 0,
            int batchCount = 0)
        {
            double requestedRadiusMm = initialRadiusMm;
            bool automatic = mode == IntermediateDecision.AutomaticMiddle;
            bool referenceRoute =
                mode == IntermediateDecision.ReferenceRoute;
            bool compound =
                automatic || referenceRoute ||
                mode == IntermediateDecision.SelectMiddle;
            bool horizontalPair = IsHorizontalPair(first, second);
            bool horizontalAutomatic = automatic && horizontalPair;
            bool horizontalReferenceRoute =
                referenceRoute && horizontalPair;
            while (true)
            {
                var dialog = new HandrailJoinWindow(
                    Describe(first?.Instance, "Trecho 1"),
                    automatic
                        ? "Patamar calculado pelos dois eixos — será criado após confirmar"
                        : horizontalReferenceRoute
                        ? "Trecho intermediário posicionado pela aresta — será criado após confirmar"
                        : referenceRoute
                        ? "Trecho nivelado + transversal pela aresta — serão criados após confirmar"
                        : compound
                        ? Describe(middle?.Instance, "Patamar")
                        : Describe(second?.Instance, "Trecho 2"),
                    compound ? Describe(second?.Instance, "Trecho 3") : null,
                    horizontalAutomatic
                        ? "Horizontal → ligação perpendicular automática → " +
                          "Horizontal (2 arcos)"
                        : automatic
                        ? $"{DescribeOrientation(first?.Instance)} → " +
                          "patamar na altura do corrimão horizontal → " +
                          $"{DescribeOrientation(second?.Instance)} (2 arcos)"
                        : horizontalReferenceRoute
                        ? "Horizontal → paralelo e posicionado pela aresta → " +
                          "Horizontal (2 arcos)"
                        : referenceRoute
                        ? $"{DescribeOrientation(first?.Instance)} → nivelado → " +
                          "paralelo e posicionado pela aresta → " +
                          $"{DescribeOrientation(second?.Instance)} (3 arcos)"
                        : compound
                        ? $"{DescribeOrientation(first?.Instance)} → patamar existente → " +
                          $"{DescribeOrientation(second?.Instance)} (2 arcos)"
                        : $"{DescribeOrientation(first?.Instance)} → " +
                          $"{DescribeOrientation(second?.Instance)} (1 arco)",
                    requestedRadiusMm,
                    showSagaWarning,
                    initialReferenceHeightMm,
                    initialHorizontalOffsetMm,
                    batchIndex,
                    batchCount);
                new WindowInteropHelper(dialog).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;

                if (dialog.ShowDialog() != true)
                    return null;

                requestedRadiusMm = dialog.RadiusMm;
                try
                {
                    referencePlacementChanged?.Invoke(
                        dialog.ReferenceHeightMm,
                        dialog.HorizontalOffsetMm);
                    if (initialReferenceHeightMm.HasValue)
                        initialReferenceHeightMm = dialog.ReferenceHeightMm;
                    if (initialHorizontalOffsetMm.HasValue)
                        initialHorizontalOffsetMm = dialog.HorizontalOffsetMm;
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

        private static RoundedCornerReferenceRouteResult ApplyReferenceRoute(
            Document document,
            RoundedCornerReferenceRoutePlan plan)
        {
            using (var transaction = CreateTransaction(
                document,
                "SAGA - Criar trajeto do corrimão pela aresta"))
            {
                try
                {
                    var result = RoundedCornerService.ApplyReferenceRoute(
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
                    "A união direta usa um único arco quando os eixos se encontram. " +
                    "A ligação automática usa somente os eixos dos corrimãos. " +
                    "A opção por aresta usa também a posição da referência para " +
                    "posicionar o trecho de ligação.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "Criar automaticamente",
                "Tenta um único arco; quando necessário, cria o patamar somente pelos eixos selecionados.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "Criar patamar por aresta (recomendado)",
                "Posiciona a ligação pela aresta; em pares mistos, também nivela o trecho inclinado.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink3,
                "Criar várias uniões pela mesma aresta",
                "Selecione os perfis restantes em pares e informe a altura de cada união.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink4,
                "Usar trecho horizontal do patamar",
                "Seleciona um terceiro perfil e cria dois arcos tangentes.");
            dialog.DefaultButton = TaskDialogResult.CommandLink1;

            var result = dialog.Show();
            if (result == TaskDialogResult.CommandLink1)
                return IntermediateDecision.Direct;
            if (result == TaskDialogResult.CommandLink2)
                return IntermediateDecision.ReferenceRoute;
            if (result == TaskDialogResult.CommandLink3)
                return IntermediateDecision.BatchReference;
            if (result == TaskDialogResult.CommandLink4)
                return IntermediateDecision.SelectMiddle;
            return IntermediateDecision.End;
        }

        private static IntermediateDecision AskForAutomaticFallback(string reason)
        {
            var dialog = new TaskDialog("SAGA - Unir corrimãos")
            {
                MainInstruction =
                    "Não foi possível criar o patamar pela referência selecionada.",
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
            ReferenceRoute,
            BatchReference,
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

        private sealed class BatchGeneratedPlan
        {
            internal RoundedCornerAutomaticCompoundPlan Automatic { get; set; }
            internal RoundedCornerReferenceRoutePlan ReferenceRoute { get; set; }
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

        private sealed class HorizontalStraightEdgeSelectionFilter : ISelectionFilter
        {
            private readonly Document _document;

            internal HorizontalStraightEdgeSelectionFilter(Document document) =>
                _document = document;

            public bool AllowElement(Element element) => element != null;

            public bool AllowReference(Reference reference, XYZ position)
            {
                try
                {
                    var element = _document?.GetElement(reference?.ElementId);
                    var edge = element?.GetGeometryObjectFromReference(reference) as Edge;
                    var rawLine = edge?.AsCurve() as Line;
                    var line = ResolveReferenceLineInModelCoordinates(
                        element,
                        rawLine,
                        position);
                    if (line == null || line.Length <= 1e-9) return false;

                    return IsHorizontal(line);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
