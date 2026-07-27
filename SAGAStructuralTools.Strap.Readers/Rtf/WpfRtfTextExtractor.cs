using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SAGAStructuralTools.Strap.Readers.Rtf
{
    public sealed class WpfRtfTextExtractor : IRtfTextExtractor
    {
        public string Extract(string filePath)
        {
            string result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var document = new FlowDocument();
                    var range = new TextRange(document.ContentStart, document.ContentEnd);
                    using (FileStream stream = File.OpenRead(filePath))
                    {
                        range.Load(stream, DataFormats.Rtf);
                    }
                    result = range.Text;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
            return result ?? string.Empty;
        }
    }
}
