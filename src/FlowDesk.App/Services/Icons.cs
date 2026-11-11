using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
namespace FlowDesk.App.Services;
public static class Icons
{
    public static async Task LoadAsync(Image image, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32, ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null) return;
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(thumbnail); image.Source = bitmap;
        }
        catch (Exception) { /* Keep the labelled Fluent fallback when Shell has no icon. */ }
    }
}
