namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

/// <summary>
/// Keeps Chromium treating a WebView as compositor-visible during CDP file uploads.
/// </summary>
public interface IDomUploadCompositorScope : IDisposable;
