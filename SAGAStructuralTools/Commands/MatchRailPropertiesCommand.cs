using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MatchRailPropertiesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApplication = commandData?.Application;
            var uiDocument = uiApplication?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            int updatedCount = 0;
            try
            {
                var filter = new RegisteredRailFilter();
                var sourceReference = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    filter,
                    "Selecione o guarda-corpo SAGA de referência");
                var sourceElement = document.GetElement(sourceReference.ElementId);
                if (!RailAssemblyStore.TryRead(sourceElement, out var sourceData))
                    throw new InvalidOperationException(
                        "O guarda-corpo de referência não possui configuração SAGA válida.");

                var processedAssemblies = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    Reference targetReference;
                    try
                    {
                        targetReference = uiDocument.Selection.PickObject(
                            ObjectType.Element,
                            filter,
                            updatedCount == 0
                                ? "Selecione um guarda-corpo de destino (Esc encerra)"
                                : $"Selecione outro destino ({updatedCount} atualizado(s); Esc encerra)");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break;
                    }

                    var targetElement = document.GetElement(targetReference.ElementId);
                    if (!RailAssemblyStore.TryRead(targetElement, out var targetData))
                        continue;
                    if (string.Equals(
                        sourceData.AssemblyId,
                        targetData.AssemblyId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        TaskDialog.Show(
                            "SAGA - Igualar propriedades",
                            "O guarda-corpo de referência não pode ser usado como destino.");
                        continue;
                    }
                    if (!processedAssemblies.Add(targetData.AssemblyId))
                        continue;

                    ApplyProperties(
                        uiApplication,
                        document,
                        sourceData,
                        targetData,
                        out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        processedAssemblies.Remove(targetData.AssemblyId);
                        TaskDialog.Show(
                            "SAGA - Igualar propriedades",
                            $"Não foi possível atualizar este destino:\n\n{error}");
                        continue;
                    }

                    updatedCount++;
                }

                return updatedCount > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return updatedCount > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("MatchRailPropertiesCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ApplyProperties(
            UIApplication uiApplication,
            Document document,
            RailAssemblyData sourceData,
            RailAssemblyData targetData,
            out string error)
        {
            error = null;
            var config = RailAssemblyStore.CloneConfig(sourceData.Config);
            PreserveTargetTopology(config, targetData.Config);
            var context = RailEditContext.FromStored(targetData);
            context.SourceDocument = document;

            double lengthMm = context.Start.DistanceTo(context.End) * 304.8;
            var definition = RailCalculator.Calculate(
                new List<double> { lengthMm },
                config);
            if (!definition.IsValid)
                throw new InvalidOperationException(
                    string.Join("\n", definition.Warnings));

            var handler = new RailCreationHandler
            {
                Definition = definition,
                Config = config,
                EditContext = context,
                ReplaceEditBaseLine = false,
                IsInclinedRun = Math.Abs(context.End.Z - context.Start.Z) * 304.8 > 1.0
            };
            string completionError = null;
            handler.Completed += result => completionError = result;
            handler.Execute(uiApplication);
            error = completionError;
        }

        private static void PreserveTargetTopology(
            Core.Models.RailConfig destination,
            Core.Models.RailConfig target)
        {
            destination.MirrorPairEnabled = target.MirrorPairEnabled;
            destination.MirrorReferenceDefined = target.MirrorReferenceDefined;
            destination.MirrorTranslationX = target.MirrorTranslationX;
            destination.MirrorTranslationY = target.MirrorTranslationY;
            destination.MirrorStairWidth = target.MirrorStairWidth;
            destination.MirrorWidthIsAxis = target.MirrorWidthIsAxis;
            destination.MirrorInvertSide = target.MirrorInvertSide;
            destination.MirrorProfileWidth = target.MirrorProfileWidth;
            destination.MirrorBaseSideSign = target.MirrorBaseSideSign;
        }

        private sealed class RegisteredRailFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                RailAssemblyStore.TryRead(element, out _);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
