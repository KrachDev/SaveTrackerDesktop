using Avalonia.Media.Imaging;
using System.IO;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.HELPERS
{
    public static class UiHelpers
    {
        public static Bitmap? ExtractIconFromExe(string path)
        {
            var bytes = Misc.ExtractIconFromExe(path);
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}
