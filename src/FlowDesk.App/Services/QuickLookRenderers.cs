using FlowDesk.Core;
using FlowDesk.Core.QuickLook;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FlowDesk.App.Services;

internal interface IQuickLookRenderer : IDisposable
{
    string Note { get; }
    string Details { get; }
    Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token);
}
internal abstract class QuickLookRenderer : IQuickLookRenderer
{
    public string Note { get; protected set; } = "";
    public string Details { get; protected set; } = "";
    public abstract Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token);
    public virtual void Dispose() { }
    protected static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
}
internal static class QuickLookRenderers
{
    public static async Task<IQuickLookRenderer> CreateAsync(QuickLookItem item, QuickLookShell shell, CancellationToken token)
    {
        byte[] prefix = [];
        if (item.Path is not null && !item.IsFolder)
        {
            prefix = await Task.Run(async () =>
            {
                await using var file = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                byte[] bytes = new byte[512];
                int length = await file.ReadAsync(bytes, token);
                return bytes[..length];
            }, token);
        }
        token.ThrowIfCancellationRequested();
        if (item.Path is null) return new FallbackQuickLookRenderer(shell, "This is a virtual Shell item. Open it in Explorer to view its contents.");
        return PreviewContent.Choose(item.Path, item.IsFolder, prefix) switch
        {
            PreviewKind.Image => new ImageQuickLookRenderer(),
            PreviewKind.Pdf => new PdfQuickLookRenderer(),
            PreviewKind.Media => new MediaQuickLookRenderer(shell),
            PreviewKind.Text => new TextQuickLookRenderer(),
            PreviewKind.Folder => new FolderQuickLookRenderer(),
            _ => new FallbackQuickLookRenderer(shell, "No built-in preview for this file type. Choose Open to use its default app.")
        };
    }
}
internal sealed class ImageQuickLookRenderer : QuickLookRenderer
{
    public override async Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(item.Path!).AsTask(token);
        using var stream = await file.OpenReadAsync().AsTask(token);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        var size = PreviewContent.Fit(decoder.PixelWidth, decoder.PixelHeight, 2048);
        stream.Seek(0);
        var bitmap = new BitmapImage { DecodePixelWidth = (int)size.Width, DecodePixelHeight = (int)size.Height,
            DecodePixelType = DecodePixelType.Physical };
        await bitmap.SetSourceAsync(stream).AsTask(token);
        token.ThrowIfCancellationRequested();
        Details = $" · {decoder.PixelWidth} × {decoder.PixelHeight} px";
        return new Image { Source = bitmap, Stretch = Stretch.Uniform };
    }
}
internal sealed class TextQuickLookRenderer : QuickLookRenderer
{
    public override async Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token)
    {
        var result = await Task.Run(async () =>
        {
            await using var stream = new FileStream(item.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
            return await PreviewContent.ReadTextAsync(stream, token);
        }, token);
        token.ThrowIfCancellationRequested();
        Note = result.Truncated ? "Showing first 1 MB" : "";
        var text = Label(result.Text); text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        text.TextWrapping = TextWrapping.NoWrap;
        return new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
internal sealed class MediaQuickLookRenderer(QuickLookShell shell) : QuickLookRenderer
{
    private MediaPlayer? player;
    private MediaPlayerElement? element;
    public override async Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(item.Path!).AsTask(token);
        token.ThrowIfCancellationRequested();
        var source = MediaSource.CreateFromStorageFile(file);
        player = new MediaPlayer { AutoPlay = true, Source = source };
        element = new MediaPlayerElement { AreTransportControlsEnabled = true, Stretch = Stretch.Uniform };
        element.SetMediaPlayer(player);
        var grid = new Grid(); grid.Children.Add(element);
        var failure = Label("This media could not be played. The file may be damaged or need a Windows codec. Choose Open to try its default app.");
        failure.Visibility = Visibility.Collapsed; grid.Children.Add(failure);
        player.MediaFailed += (_, _) => element.DispatcherQueue.TryEnqueue(async () =>
        {
            if (token.IsCancellationRequested) return;
            try
            {
                player?.Pause(); element.Visibility = Visibility.Collapsed; failure.Visibility = Visibility.Visible;
                using var fallback = new FallbackQuickLookRenderer(shell, failure.Text);
                var content = await fallback.LoadAsync(item, token);
                token.ThrowIfCancellationRequested();
                grid.Children.Clear(); grid.Children.Add(content);
            }
            catch (Exception) { /* the reason and header Open button remain available */ }
        });
        return grid;
    }
    public override void Dispose()
    {
        element?.SetMediaPlayer(null);
        if (player is not null)
        {
            player.Pause(); var source = player.Source as MediaSource; player.Source = null; player.Dispose(); source?.Dispose();
        }
        player = null;
    }
}
internal sealed class PdfQuickLookRenderer : QuickLookRenderer
{
    private const string FailedText = "This PDF page could not be rendered. Choose Open to try its default app.";
    private const string SlowText = "This PDF page is still rendering. Choose Open to use its default app.";
    private const int SlowPageMilliseconds = 8000;
    private CancellationTokenSource? lifetime;
    private PdfDocument? document;
    // Windows.Data.Pdf renders one page at a time per process. Two slots keep a recycled page that
    // the engine has not finished yet from holding back the pages that are visible now.
    private readonly SemaphoreSlim renderSlots = new(2, 2);
    private readonly HashSet<Image> tracked = [];
    private readonly Dictionary<Microsoft.UI.Xaml.Controls.Primitives.SelectorItem, CancellationTokenSource> pages = [];
    public override async Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = lifetime.Token;
        var file = await StorageFile.GetFileFromPathAsync(item.Path!).AsTask(token);
        var loaded = await PdfDocument.LoadFromFileAsync(file).AsTask(token);
        token.ThrowIfCancellationRequested();
        document = loaded;
        Details = " · " + CountText.Format(loaded.PageCount, "page");
        QuickLookDiagnostics.Write($"pdf loaded pages={loaded.PageCount}");
        // Round-1 cause (proven headless): an element assigned to ItemContainer.Content inside
        // ContainerContentChanging never reaches the visual tree; the container keeps the template
        // root that ListView built for the data item, so the page area stays empty with no error.
        // Pages are therefore drawn only into the ItemTemplate's own visuals, after phase 0.
        var list = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid MinHeight="160" Margin="0,0,0,12">
                    <Image x:Name="PageImage" Stretch="Uniform" MaxWidth="1600" HorizontalAlignment="Stretch" />
                    <TextBlock x:Name="PageError" Visibility="Collapsed" TextWrapping="Wrap" />
                </Grid>
            </DataTemplate>
            """);
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.Phase != 0) return;
            var container = args.ItemContainer;
            QuickLookDiagnostics.Write($"pdf prepare item={args.Item} phase={args.Phase} recycle={args.InRecycleQueue} template={container.ContentTemplateRoot?.GetType().Name ?? "null"}");
            if (pages.Remove(container, out var previous)) previous.Cancel();
            if (container.ContentTemplateRoot is Grid oldRoot && oldRoot.FindName("PageImage") is Image oldImage) oldImage.Source = null;
            if (!args.InRecycleQueue) args.RegisterUpdateCallback(1, RenderPage);
        };
        async void RenderPage(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            var container = args.ItemContainer;
            if (token.IsCancellationRequested || args.InRecycleQueue || document is not { } source) return;
            if (container.ContentTemplateRoot is not Grid root)
            {
                QuickLookDiagnostics.Write($"pdf template-not-ready phase={args.Phase} retry={args.Phase < 2}");
                if (args.Phase < 2) args.RegisterUpdateCallback(2, RenderPage);
                return;
            }
            if (root.FindName("PageImage") is not Image image || root.FindName("PageError") is not TextBlock error || args.Item is not int index)
            { QuickLookDiagnostics.Write("pdf invalid-template-or-item"); return; }
            Track(image, error);
            image.Tag = index; image.Source = null; image.Visibility = Visibility.Visible; error.Visibility = Visibility.Collapsed;
            container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            QuickLookDiagnostics.Write($"pdf render page={index} phase={args.Phase} width={root.ActualWidth} height={root.ActualHeight}");
            if (pages.Remove(container, out var old)) old.Cancel();
            using var rendering = CancellationTokenSource.CreateLinkedTokenSource(token);
            pages[container] = rendering;
            var pageToken = rendering.Token;
            bool slot = false;
            try
            {
                using var page = source.GetPage((uint)index);
                // Placeholder with the page's final height, so the list does not jump when the image arrives.
                double width = Math.Min(root.ActualWidth, 1600);
                root.Height = width > 0 && page.Size.Width > 0 ? width * page.Size.Height / page.Size.Width : Math.Min(1200, page.Size.Height);
                var size = PreviewContent.Fit(page.Size.Width * 2, page.Size.Height * 2, 1800);
                _ = ShowSlowNoticeAsync(index, image, error, pageToken);
                // A page that scrolls away while it waits here is cancelled before it costs a render.
                await renderSlots.WaitAsync(pageToken);
                slot = true;
                using var stream = await QuickLookPdfStream.RenderAsync(page, size.Width, size.Height, pageToken);
                QuickLookDiagnostics.Write($"pdf stream page={index} position={stream.Position} bytes={stream.Size}");
                pageToken.ThrowIfCancellationRequested();
                var bitmap = new BitmapImage { DecodePixelWidth = (int)size.Width, DecodePixelHeight = (int)size.Height, DecodePixelType = DecodePixelType.Physical };
                await bitmap.SetSourceAsync(stream).AsTask(pageToken);
                pageToken.ThrowIfCancellationRequested();
                error.Visibility = Visibility.Collapsed; image.Visibility = Visibility.Visible;
                image.Source = bitmap; root.Height = double.NaN; root.MinHeight = 0;
                QuickLookDiagnostics.Write($"pdf decoded page={index} width={bitmap.PixelWidth} height={bitmap.PixelHeight}");
            }
            catch (OperationCanceledException) { QuickLookDiagnostics.Write($"pdf cancelled page={index}"); }
            catch (Exception ex)
            {
                QuickLookDiagnostics.Write($"pdf failed page={index} error={ex.GetType().Name} hr=0x{ex.HResult:X8}");
                if (!pageToken.IsCancellationRequested)
                {
                    Note = "A PDF page could not be rendered. Choose Open to try its default app.";
                    error.Text = FailedText;
                    image.Visibility = Visibility.Collapsed; error.Visibility = Visibility.Visible; root.Height = double.NaN;
                }
            }
            finally
            {
                if (slot) renderSlots.Release();
                if (pages.TryGetValue(container, out var active) && ReferenceEquals(active, rendering)) pages.Remove(container);
                rendering.Cancel(); // ends this page's slow-notice timer
            }
        }
        // Hook before assigning items, so initial realization cannot miss its render callback.
        int previewCount = PdfPreviewLimits.PageCount(loaded.PageCount);
        if (loaded.PageCount > previewCount) Note = $"Preview limited to the first {previewCount:N0} pages. Choose Open to read the full PDF.";
        list.ItemsSource = Enumerable.Range(0, previewCount).ToArray();
        return list;
    }
    /// <summary>A page that is neither drawn nor failed must not stay silently empty.</summary>
    private static async Task ShowSlowNoticeAsync(int index, Image image, TextBlock error, CancellationToken pageToken)
    {
        try { await Task.Delay(SlowPageMilliseconds, pageToken); }
        catch (OperationCanceledException) { return; }
        if (pageToken.IsCancellationRequested || image.Source is not null) return;
        QuickLookDiagnostics.Write($"pdf slow page={index} after={SlowPageMilliseconds}ms");
        error.Text = SlowText; error.Visibility = Visibility.Visible;
    }
    /// <summary>Once per template instance. "pdf layout" is the proof that a decoded page is in the
    /// live tree with a real size; a decode failure after SetSourceAsync shows the error text.</summary>
    private void Track(Image image, TextBlock error)
    {
        if (!tracked.Add(image)) return;
        image.SizeChanged += (_, args) =>
        {
            if (image.Source is not null && args.NewSize.Height > 0)
                QuickLookDiagnostics.Write($"pdf layout page={image.Tag} image={args.NewSize.Width:0}x{args.NewSize.Height:0}");
        };
        image.ImageFailed += (_, args) =>
        {
            QuickLookDiagnostics.Write($"pdf image-failed page={image.Tag} error={args.ErrorMessage}");
            error.Text = FailedText; image.Visibility = Visibility.Collapsed; error.Visibility = Visibility.Visible;
        };
    }
    public override void Dispose()
    {
        // PdfDocument has no Close: cancel the page work and drop the only long-lived reference.
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; document = null; tracked.Clear();
    }
}
internal sealed class FolderQuickLookRenderer : QuickLookRenderer
{
    public override async Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token)
    {
        string text = await Task.Run(() =>
        {
            int count = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (string _ in Directory.EnumerateFileSystemEntries(item.Path!))
            {
                token.ThrowIfCancellationRequested(); count++;
                if (watch.ElapsedMilliseconds > 500 || count >= 10000) return $"{CountText.Format(count, "item", numberFormat: "N0", suffix: "+")}\nModified {Directory.GetLastWriteTime(item.Path!):g}\nItem count limited; subfolders are not scanned.";
            }
            return $"{CountText.Format(count, "item", numberFormat: "N0")}\nModified {Directory.GetLastWriteTime(item.Path!):g}\nSubfolders are not scanned.";
        }, token);
        token.ThrowIfCancellationRequested();
        return Label(item.Name + "\n\n" + text);
    }
}
internal sealed class FallbackQuickLookRenderer(QuickLookShell shell, string reason) : QuickLookRenderer
{
    public override async Task<FrameworkElement> LoadAsync(QuickLookItem item, CancellationToken token)
    {
        Note = reason;
        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(Label(item.Name));
        panel.Children.Add(Label(item.Type));
        if (item.Path is not null)
        {
            try
            {
                var metadata = await Task.Run(() =>
                {
                    var info = new FileInfo(item.Path);
                    string size = CountText.Format(info.Length, "byte", numberFormat: "N0");
                    return $"{size}\nModified {info.LastWriteTime:g}\nCreated {info.CreationTime:g}";
                }, token);
                token.ThrowIfCancellationRequested(); panel.Children.Add(Label(metadata));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var bytes = await shell.ThumbnailAsync(item.Path, token);
            token.ThrowIfCancellationRequested();
            if (bytes is not null)
            {
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
                var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream).AsTask(token);
                panel.Children.Insert(0, new Image { Source = bitmap, MaxWidth = 512, MaxHeight = 320, Stretch = Stretch.Uniform });
            }
        }
        panel.Children.Add(Label(reason));
        return new ScrollViewer { Content = panel };
    }
}
