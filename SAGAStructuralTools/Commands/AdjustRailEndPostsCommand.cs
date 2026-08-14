using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.Rail.Domain;
using SAGAStructuralTools.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AdjustRailEndPostsCommand : IExternalCommand
    {
        private const double FeetToMillimeters = 304.8;
        private const double EndToleranceMm = 2.0;
        private const double SameRunToleranceMm = 10.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            try
            {
                var selectedReference = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    new SagaPostFilter(),
                    "Selecione um montante de extremidade do guarda-corpo SAGA");
                var selectedPost = document.GetElement(selectedReference.ElementId);
                if (!RailAssemblyStore.TryRead(selectedPost, out var stored))
                    throw new InvalidOperationException("O montante não pertence a um guarda-corpo criado pelo SAGA.");

                var run = RailRunGeometry.Create(stored.Start.ToXyz(), stored.End.ToXyz());
                var posts = FindPostsOfSameType(document, stored, selectedPost, run);
                if (posts.Count < 2)
                    throw new InvalidOperationException("Não foi possível localizar ao menos dois montantes do mesmo tipo neste trecho.");

                int selectedIndex = posts.FindIndex(item => item.Element.Id.Equals(selectedPost.Id));
                bool movesStart = selectedIndex == 0 &&
                                  Math.Abs(posts[0].StationMm - posts[selectedIndex].StationMm) <= EndToleranceMm;
                bool movesEnd = selectedIndex == posts.Count - 1 &&
                                Math.Abs(posts[posts.Count - 1].StationMm - posts[selectedIndex].StationMm) <= EndToleranceMm;
                if (!movesStart && !movesEnd)
                    throw new InvalidOperationException(
                        "O montante selecionado não é uma extremidade. Selecione o primeiro ou o último montante do trecho.");

                double selectedStation = posts[selectedIndex].StationMm;
                double fixedStation = movesStart ? posts[posts.Count - 1].StationMm : posts[0].StationMm;
                double targetStation = GetTargetStation(uiDocument, run, selectedStation, movesStart);
                if (Math.Abs(targetStation - selectedStation) < 0.1)
                    throw new InvalidOperationException("A nova posição coincide com a posição atual do montante.");
                if ((movesStart && targetStation >= fixedStation - 1.0) ||
                    (movesEnd && targetStation <= fixedStation + 1.0))
                    throw new InvalidOperationException("A nova extremidade não pode ultrapassar a extremidade fixa.");
                if (Math.Abs(targetStation - selectedStation) > 50000.0)
                    throw new InvalidOperationException("O deslocamento informado ultrapassa o limite de segurança de 50.000 mm.");

                int targetCount = ResolveTargetCount(stored.Config, posts.Count, Math.Abs(targetStation - fixedStation));
                var distribution = EndPostDistribution.Create(fixedStation, targetStation, targetCount);

                using (var transaction = new Transaction(document, "SAGA - Ajustar extremidade dos montantes"))
                {
                    transaction.Start();
                    var editablePosts = AddRequiredCopies(document, selectedPost, posts, targetCount, run);
                    MovePostsToStations(document, editablePosts, selectedPost.Id, movesStart,
                        distribution.StationsMm, run);

                    var context = RailEditContext.FromStored(stored);
                    var memberIds = RailAssemblyStore.FindMemberIds(document, context);
                    foreach (var post in editablePosts)
                        if (!memberIds.Contains(post.Element.Id)) memberIds.Add(post.Element.Id);
                    RailAssemblyStore.Attach(document, memberIds, stored);
                    transaction.Commit();
                }

                TaskDialog.Show("SAGA - Ajustar extremidade",
                    $"Extremidade ajustada e montantes redistribuídos.\n\n" +
                    $"Montantes: {targetCount}\nEspaçamento resultante: {distribution.ActualSpacingMm:F1} mm\n" +
                    $"Deslocamento da extremidade: {Math.Abs(targetStation - selectedStation):F1} mm.\n\n" +
                    "Corrimãos, travessas e demais barras não foram alterados.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (UserCancelledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("AdjustRailEndPostsCommand.Execute", ex);
                TaskDialog.Show("SAGA - Ajustar extremidade", $"Não foi possível ajustar os montantes:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static double GetTargetStation(UIDocument uiDocument, RailRunGeometry run,
            double selectedStation, bool movesStart)
        {
            var choice = new TaskDialog("SAGA - Ajustar extremidade")
            {
                MainInstruction = "Como deseja definir a nova posição?",
                MainContent = "Por ponto: clique e o ponto será projetado sobre o eixo do guarda-corpo.\n" +
                              "Por valor: positivo aumenta o trecho e negativo recua.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            choice.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selecionar um ponto");
            choice.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Informar um valor em milímetros");
            TaskDialogResult result = choice.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                XYZ point = uiDocument.Selection.PickPoint("Clique na nova posição da extremidade");
                return (point - run.Start).DotProduct(run.Direction) * FeetToMillimeters;
            }
            if (result == TaskDialogResult.CommandLink2)
            {
                var window = new AdjustRailEndWindow();
                new WindowInteropHelper(window).Owner = Process.GetCurrentProcess().MainWindowHandle;
                if (window.ShowDialog() != true)
                    throw new UserCancelledException();
                return selectedStation + (movesStart ? -window.ValueMm : window.ValueMm);
            }
            throw new UserCancelledException();
        }

        private static int ResolveTargetCount(RailConfig config, int currentCount, double lengthMm)
        {
            double maximumSpacing = 0.0;
            if (config?.DistMode == DistributionMode.MaxSpan) maximumSpacing = config.MaxPostSpan;
            else if (config?.DistMode == DistributionMode.FixedAxis) maximumSpacing = config.FixedAxisSpacing;
            if (maximumSpacing < 1.0) return currentCount;

            int required = EndPostDistribution.RequiredPostCount(lengthMm, maximumSpacing);
            if (required <= currentCount) return currentCount;

            int additional = required - currentCount;
            var dialog = new TaskDialog("SAGA - Ajustar extremidade")
            {
                MainInstruction = additional == 1
                    ? "O novo comprimento exige 1 montante adicional."
                    : $"O novo comprimento exige {additional} montantes adicionais.",
                MainContent = $"Para respeitar o espaçamento configurado de {maximumSpacing:F1} mm, " +
                              $"a distribuição precisa de {required} montantes.\n\n" +
                              "Sim: adicionar | Não: manter a quantidade atual | Cancelar: não alterar",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Yes
            };
            TaskDialogResult result = dialog.Show();
            if (result == TaskDialogResult.Yes) return required;
            if (result == TaskDialogResult.No) return currentCount;
            throw new UserCancelledException();
        }

        private static List<PostStation> FindPostsOfSameType(Document document, RailAssemblyData stored,
            Element selectedPost, RailRunGeometry run)
        {
            var context = RailEditContext.FromStored(stored);
            double selectedLateralOffset = LateralOffsetOf(selectedPost, run);
            return RailAssemblyStore.FindMemberIds(document, context)
                .Select(document.GetElement)
                .Where(element => element != null && element.GetTypeId().Equals(selectedPost.GetTypeId()))
                .Where(IsPostGeometry)
                .Where(element => Math.Abs(LateralOffsetOf(element, run) - selectedLateralOffset) <= SameRunToleranceMm)
                .Select(element => new PostStation(element, StationOf(element, run)))
                .OrderBy(item => item.StationMm)
                .ToList();
        }

        private static List<PostStation> AddRequiredCopies(Document document, Element selectedPost,
            List<PostStation> posts, int targetCount, RailRunGeometry run)
        {
            var result = new List<PostStation>(posts);
            while (result.Count < targetCount)
            {
                ElementId copyId = ElementTransformUtils.CopyElement(document, selectedPost.Id, XYZ.Zero).First();
                var copy = document.GetElement(copyId);
                result.Add(new PostStation(copy, StationOf(copy, run)));
            }
            return result;
        }

        private static void MovePostsToStations(Document document, List<PostStation> posts,
            ElementId selectedId, bool movesStart, IReadOnlyList<double> targets, RailRunGeometry run)
        {
            var selected = posts.Single(item => item.Element.Id.Equals(selectedId));
            var fixedPost = movesStart
                ? posts.OrderBy(item => item.StationMm).Last()
                : posts.OrderBy(item => item.StationMm).First();
            double selectedTarget = movesStart ? targets[0] : targets[targets.Count - 1];
            double fixedTarget = movesStart ? targets[targets.Count - 1] : targets[0];
            MovePost(document, selected, selectedTarget, run);
            MovePost(document, fixedPost, fixedTarget, run);

            var remainingPosts = posts
                .Where(item => !item.Element.Id.Equals(selected.Element.Id) &&
                               !item.Element.Id.Equals(fixedPost.Element.Id))
                .OrderBy(item => item.StationMm)
                .ToList();
            var remainingTargets = targets.Skip(1).Take(targets.Count - 2).ToList();
            for (int index = 0; index < remainingPosts.Count; index++)
                MovePost(document, remainingPosts[index], remainingTargets[index], run);
        }

        private static void MovePost(Document document, PostStation post, double targetStationMm, RailRunGeometry run)
        {
            double deltaFt = (targetStationMm - post.StationMm) / FeetToMillimeters;
            if (Math.Abs(deltaFt) < 1e-9) return;
            XYZ movement = run.Direction * deltaFt;

            if (post.Element.Location is LocationCurve curveLocation && curveLocation.Curve is Line line)
            {
                curveLocation.Curve = Line.CreateBound(line.GetEndPoint(0) + movement, line.GetEndPoint(1) + movement);
                return;
            }
            if (post.Element.Location is LocationPoint)
            {
                XYZ horizontal = new XYZ(movement.X, movement.Y, 0.0);
                if (horizontal.GetLength() > 1e-9)
                    ElementTransformUtils.MoveElement(document, post.Element.Id, horizontal);
                bool baseMoved = AddToParameter(post.Element, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, movement.Z);
                bool topMoved = AddToParameter(post.Element, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, movement.Z);
                if (!baseMoved && !topMoved && Math.Abs(movement.Z) > 1e-9)
                    ElementTransformUtils.MoveElement(document, post.Element.Id, XYZ.BasisZ * movement.Z);
                return;
            }
            throw new InvalidOperationException("Um dos montantes não permite movimentação ao longo do trecho.");
        }

        private static bool AddToParameter(Element element, BuiltInParameter parameterId, double increment)
        {
            var parameter = element?.get_Parameter(parameterId);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) return false;
            parameter.Set(parameter.AsDouble() + increment);
            return true;
        }

        private static double StationOf(Element element, RailRunGeometry run) =>
            (GetPostPoint(element) - run.Start).DotProduct(run.Direction) * FeetToMillimeters;

        private static double LateralOffsetOf(Element element, RailRunGeometry run) =>
            (GetPostPoint(element) - run.Start).DotProduct(run.Lateral) * FeetToMillimeters;

        private static XYZ GetPostPoint(Element element)
        {
            if (element?.Location is LocationCurve curveLocation && curveLocation.Curve is Line line)
            {
                XYZ first = line.GetEndPoint(0);
                XYZ second = line.GetEndPoint(1);
                return first.Z <= second.Z ? first : second;
            }
            if (element?.Location is LocationPoint pointLocation) return pointLocation.Point;
            throw new InvalidOperationException("O elemento não possui um eixo vertical reconhecível.");
        }

        private static bool IsPostGeometry(Element element)
        {
            if (element?.Location is LocationPoint) return true;
            if (element?.Location is LocationCurve location && location.Curve is Line line)
                return Math.Abs(line.Direction.Z) >= 0.99;
            return false;
        }

        private sealed class SagaPostFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                RailAssemblyStore.TryRead(element, out _) && IsPostGeometry(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class PostStation
        {
            public Element Element { get; }
            public double StationMm { get; }
            public PostStation(Element element, double stationMm)
            {
                Element = element;
                StationMm = stationMm;
            }
        }

        private sealed class UserCancelledException : Exception
        {
        }
    }
}
