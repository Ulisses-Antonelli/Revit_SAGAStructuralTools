using System;
using System.Text;

namespace SAGAStructuralTools.Strap.Readers.Text
{
    internal static class TextEncodingDetector
    {
        private static readonly object RegistrationLock = new object();
        private static bool _registered;

        public static Encoding Detect(byte[] bytes)
        {
            EnsureCodePagesRegistered();
            if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
                return new UTF8Encoding(true, true);
            if (HasPrefix(bytes, 0xFF, 0xFE))
                return new UnicodeEncoding(false, true, true);
            if (HasPrefix(bytes, 0xFE, 0xFF))
                return new UnicodeEncoding(true, true, true);

            var strictUtf8 = new UTF8Encoding(false, true);
            try
            {
                strictUtf8.GetString(bytes);
                return strictUtf8;
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(
                    1252,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
            }
        }

        private static void EnsureCodePagesRegistered()
        {
            if (_registered)
                return;

            lock (RegistrationLock)
            {
                if (_registered)
                    return;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _registered = true;
            }
        }

        private static bool HasPrefix(byte[] bytes, params byte[] prefix)
        {
            if (bytes == null || bytes.Length < prefix.Length)
                return false;
            for (int index = 0; index < prefix.Length; index++)
            {
                if (bytes[index] != prefix[index])
                    return false;
            }
            return true;
        }
    }
}
