namespace SAGAStructuralTools.Core.Models
{
    public enum ConversionStatus
    {
        Success,
        NotFound,
        GeometryError
    }

    public class ConversionResult
    {
        public int              ElementId    { get; set; }
        public string           OriginalName { get; set; }
        public ConversionStatus Status       { get; set; }
        public string           Message      { get; set; }
    }
}
