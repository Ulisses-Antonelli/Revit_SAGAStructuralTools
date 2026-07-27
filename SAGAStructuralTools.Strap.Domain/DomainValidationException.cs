using System;

namespace SAGAStructuralTools.Strap.Domain
{
    public sealed class DomainValidationException : Exception
    {
        public DomainValidationException(string message)
            : base(message)
        {
        }
    }
}
