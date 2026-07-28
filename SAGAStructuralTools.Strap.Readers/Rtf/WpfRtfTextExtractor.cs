using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SAGAStructuralTools.Strap.Readers.Rtf
{
    public sealed class WpfRtfTextExtractor : IRtfTextExtractor
    {
        public string Extract(string filePath)
        {
            byte[] rtfBytes = PrepareRtfBytes(File.ReadAllBytes(filePath));
            string result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var document = new FlowDocument();
                    var range = new TextRange(document.ContentStart, document.ContentEnd);
                    using (var stream = new MemoryStream(rtfBytes, writable: false))
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

        private static byte[] PrepareRtfBytes(byte[] source)
        {
            int inspectionLength = Math.Min(source.Length, 1024);
            string header = Encoding.ASCII.GetString(source, 0, inspectionLength);
            if (header.IndexOf(@"\ansicpg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                !source.Any(value => value >= 0x80) ||
                IsValidUtf8(source))
            {
                return source;
            }

            byte[] marker = Encoding.ASCII.GetBytes(@"\ansi");
            int markerIndex = IndexOf(source, marker);
            if (markerIndex < 0)
                return source;

            source = EscapeHighBytes(source);
            byte[] declaration = Encoding.ASCII.GetBytes(@"\ansicpg1252");
            int insertion = markerIndex + marker.Length;
            var prepared = new byte[source.Length + declaration.Length];
            Buffer.BlockCopy(source, 0, prepared, 0, insertion);
            Buffer.BlockCopy(declaration, 0, prepared, insertion, declaration.Length);
            Buffer.BlockCopy(
                source,
                insertion,
                prepared,
                insertion + declaration.Length,
                source.Length - insertion);
            return prepared;
        }

        private static byte[] EscapeHighBytes(byte[] source)
        {
            using (var stream = new MemoryStream())
            {
                foreach (byte value in source)
                {
                    if (value < 0x80)
                    {
                        stream.WriteByte(value);
                        continue;
                    }

                    byte[] escaped = Encoding.ASCII.GetBytes(
                        @"\'" + value.ToString("x2"));
                    stream.Write(escaped, 0, escaped.Length);
                }
                return stream.ToArray();
            }
        }

        private static bool IsValidUtf8(byte[] source)
        {
            try
            {
                new UTF8Encoding(false, true).GetString(source);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static int IndexOf(byte[] source, byte[] value)
        {
            for (int index = 0; index <= source.Length - value.Length; index++)
            {
                bool matches = true;
                for (int offset = 0; offset < value.Length; offset++)
                {
                    if (source[index + offset] == value[offset])
                    {
                        continue;
                    }
                    matches = false;
                    break;
                }
                if (matches)
                    return index;
            }
            return -1;
        }
    }
}
