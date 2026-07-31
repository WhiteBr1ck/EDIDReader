using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace EDIDReader.App.Services;

internal static class ClipboardService
{
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(20 * (attempt + 1));
            }
        }

        return false;
    }
}
