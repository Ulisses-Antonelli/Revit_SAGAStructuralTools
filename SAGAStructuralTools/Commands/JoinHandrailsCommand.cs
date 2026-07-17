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
                PickedMember middle = null;
                Line referenceAxis = null;
                Line referenceBaseAxis = null;
                double referenceHeightMm = 0.0;
                double horizontalOffsetMm = 0.0;
                XYZ offsetSidePoint = null;
                bool automaticFromSelectedMembers = false;
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
                        automaticFromSelectedMembers =
                            RoundedCornerService.TryCreateMixedAutomaticReferenceAxis(
                            document,
                            first.Id,
                            first.CornerEnd,
                            second.Id,
                            second.CornerEnd,
                            out referenceAxis);
                        if (!automaticFromSelectedMembers)
                        {
                            referenceBaseAxis = PickHorizontalReferenceEdge(
                                uiDocument,
                                document,
                                first,
                                second,
                                out referenceHeightMm,
                                "Selecione a aresta de um perfil horizontal do patamar; ela definirá a cota-base e o plano da união (Esc reinicia o par)");
                            offsetSidePoint = uiDocument.Selection.PickPoint(
                                "Clique no lado da aresta para onde o eixo horizontal deve ser deslocado");
                            referenceAxis = BuildReferenceAxis(
                                referenceBaseAxis,
                                referenceHeightMm,
                                horizontalOffsetMm,
                                offsetSidePoint);
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
                    automatic || !radiusConfirmed ||
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
                        automatic && !automaticFromSelectedMembers
                            ? (double?)referenceHeightMm
                            : null,
                        automatic && !automaticFromSelectedMembers
                            ? (double?)horizontalOffsetMm
                            : null,
                        automatic && !automaticFromSelectedMembers
                            ? (Action<double, double>)((height, offset) =>
                            {
                                referenceHeightMm = height;
                                horizontalOffsetMm = offset;
                                referenceAxis = BuildReferenceAxis(
                                    referenceBaseAxis,
                                    referenceHeightMm,
                                horizontalOffsetMm,
                                offsetSidePoint);
                            })
                            : null,
                        automaticFromSelectedMembers:
                            automaticFromSelectedMembers);
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
            var plans = new List<RoundedCornerAutomaticCompoundPlan>();
            for (int index = 0; index < pairs.Count; index++)
            {
                var pair = pairs[index];
                double pairHeightMm =
                    GetStoredHandrailElevationOffsetMm(pair[0].Instance) ??
                    GetStoredHandrailElevationOffsetMm(pair[1].Instance) ??
                    suggestedHeightMm;
                Line pairReferenceAxis = BuildReferenceAxis(
                    referenceBaseAxis,
                    pairHeightMm,
                    commonOffsetMm,
                    sidePoint);
                Action<double> validateRadius = radius =>
                    RoundedCornerService.CreateAutomaticCompoundPlan(
                        document,
                        pair[0].Id,
                        pair[0].CornerEnd,
                        pair[1].Id,
                        pair[1].CornerEnd,
                        pairReferenceAxis,
                        radius);

                double? selectedRadius = PromptForValidRadius(
                    document,
                    pair[0],
                    pair[1],
                    null,
                    IntermediateDecision.AutomaticMiddle,
                    activeRadiusMm,
                    HasSagaMember(pair[0], pair[1], null) &&
                        !sagaWarningAcknowledged,
                    validateRadius,
                    pairHeightMm,
                    commonOffsetMm,
                    (height, offset) =>
                    {
                        pairHeightMm = height;
                        commonOffsetMm = offset;
                        pairReferenceAxis = BuildReferenceAxis(
                            referenceBaseAxis,
                            pairHeightMm,
                            commonOffsetMm,
                            sidePoint);
                    },
                    index,
                    pairs.Count);
                if (!selectedRadius.HasValue)
                    return 0;

                activeRadiusMm = selectedRadius.Value;
                radiusConfirmed = true;
                if (HasSagaMember(pair[0], pair[1], null))
                    sagaWarningAcknowledged = true;
                plans.Add(RoundedCornerService.CreateAutomaticCompoundPlan(
                    document,
                    pair[0].Id,
                    pair[0].CornerEnd,
                    pair[1].Id,
                    pair[1].CornerEnd,
                    pairReferenceAxis,
                    activeRadiusMm));
            }

            using (var group = new TransactionGroup(
                document,
                "SAGA - Unir corrimãos em lote"))
            {
                group.Start();
                try
                {
                    foreach (var plan in plans)
                        ApplyAutomaticCompound(document, plan);
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
            var line = edge?.AsCurve() as Line;
            var rawReference = IsHorizontal(profileAxis) ? profileAxis : line;
            if (!IsHorizontal(rawReference))
                throw new InvalidOperationException(
                    "A referência selecionada não é uma aresta reta válida.");

            // Um corrimão SAGA horizontal já fornece a cota de seu próprio eixo.
            if (RailAssemblyStore.TryRead(element, out _))
                return rawReference;

            // Perfis estruturais do patamar fornecem a cota-base e a direção. A cota
            // funcional do corrimão vem da configuração persistida no conjunto SAGA.
            double? heightOffsetMm = GetStoredHandrailElevationOffsetMm(first?.Instance);
            double? secondOffsetMm = GetStoredHandrailElevationOffsetMm(second?.Instance);
            if (heightOffsetMm.HasValue && secondOffsetMm.HasValue &&
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
            int batchCount = 0,
            bool automaticFromSelectedMembers = false)
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
                        ? automaticFromSelectedMembers
                            ? "Patamar calculado pelos dois eixos — será criado após confirmar"
                            : "Patamar no plano da aresta — será criado após confirmar"
                        : compound
                        ? Describe(middle?.Instance, "Patamar")
                        : Describe(second?.Instance, "Trecho 2"),
                    compound ? Describe(second?.Instance, "Trecho 3") : null,
                    automatic
                        ? $"{DescribeOrientation(first?.Instance)} → " +
                          (automaticFromSelectedMembers
                              ? "patamar na altura do corrimão horizontal → "
                              : "patamar no plano da aresta → ") +
                          $"{DescribeOrientation(second?.Instance)} (2 arcos)"
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
                    "Para criar um patamar, selecione uma aresta reta e horizontal que " +
                    "defina sua cota-base e seu plano horizontal.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "Tentar união direta",
                "Usa um único arco; se os eixos não se encontrarem, solicita uma aresta de referência.");
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "Criar patamar por aresta (recomendado)",
                "Seleciona uma aresta horizontal para definir a cota-base e cria o trecho entre as duas pontas.");
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
                return IntermediateDecision.AutomaticMiddle;
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
                    var line = edge?.AsCurve() as Line;
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
