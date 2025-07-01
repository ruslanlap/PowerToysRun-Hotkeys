using System.Windows;

namespace PowerToysRun.ShortcutFinder.Plugin.Helpers
{
    public static class ClipboardHelper
    {
        public static void SetClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        }
    }
}
