using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT;
using Microsoft.UI.Xaml;

namespace PhotoCull.App.Helpers
{
    public static class FolderPickerHelper
    {
        public static async Task<string?> PickFolderAsync(Window window)
        {
            var picker = new FolderPicker();
            // On WinUI3 we must initialize with window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            try
            {
                var pickerNative = picker.As<IInitializeWithWindow>();
                pickerNative.Initialize(hwnd);
            }
            catch { }

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        [ComImport]
        [Guid("3E68D4BD-7135-4D3B-8F3A-6D52B5E9F6A9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithWindow
        {
            void Initialize(IntPtr hwnd);
        }
    }
}
