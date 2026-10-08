using System;

namespace SAGAStructuralTools.Commands.Strap
{
    // Generico e BCL-only para que o wrapper possa ser testado sem o Revit.
    internal static class SafeExecutionBoundary
    {
        public static TResult Run<TResult>(
            Func<TResult> core,
            Func<Exception, TResult> failure)
        {
            if (core == null)
                throw new ArgumentNullException(nameof(core));
            if (failure == null)
                throw new ArgumentNullException(nameof(failure));

            try
            {
                return core();
            }
            catch (Exception exception)
            {
                try
                {
                    return failure(exception);
                }
                catch
                {
                    return default(TResult);
                }
            }
        }
    }
}
