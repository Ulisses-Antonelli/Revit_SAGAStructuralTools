using System;
using System.Threading;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Coordena ferramentas de selecao que nao podem disparar a abertura automatica
    /// do editor de guarda-corpo durante PickObject.
    /// </summary>
    internal static class RailToolSession
    {
        private static int _cornerCommandDepth;

        internal static event Action CornerCommandStarted;

        internal static bool IsCornerCommandActive =>
            Volatile.Read(ref _cornerCommandDepth) > 0;

        internal static IDisposable BeginCornerCommand()
        {
            if (Interlocked.Increment(ref _cornerCommandDepth) == 1)
            {
                try
                {
                    CornerCommandStarted?.Invoke();
                }
                catch
                {
                    Interlocked.Decrement(ref _cornerCommandDepth);
                    throw;
                }
            }
            return new CornerCommandScope();
        }

        private sealed class CornerCommandScope : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    Interlocked.Decrement(ref _cornerCommandDepth);
            }
        }
    }
}
