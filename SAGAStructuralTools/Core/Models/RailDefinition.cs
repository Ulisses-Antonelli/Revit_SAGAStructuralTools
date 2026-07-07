using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.Models
{
    public class RailDefinition
    {
        public List<RailSegment> Segments    { get; set; } = new List<RailSegment>();

        public double TotalLength    => Segments?.Sum(s => s.Length) ?? 0;
        public int    TotalPosts     => Segments?.Sum(s => s.PostCount) ?? 0;
        public double PreviewSpacing => Segments?.FirstOrDefault()?.ActualSpacing ?? 0;

        public List<string> Warnings { get; set; } = new List<string>();
        public bool         IsValid  { get; set; } = true;
    }
}
