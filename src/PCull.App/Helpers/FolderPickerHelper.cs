using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT;
using Microsoft.UI.Xaml;

namespace PCull.App.Helpers
{
    public static class FolderPickerHelper
    {
        public static async Task<string?> PickFolderAsync(Window window)
        {
            var picker = new FolderPicker();
            // On WinUI3 we must initialize with window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
    }
}
