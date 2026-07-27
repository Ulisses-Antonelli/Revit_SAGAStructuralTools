namespace SAGAStructuralTools.Strap.Readers
{
    public interface IStrapDocumentReader
    {
        DocumentReadResult Read(string filePath);
    }
}
