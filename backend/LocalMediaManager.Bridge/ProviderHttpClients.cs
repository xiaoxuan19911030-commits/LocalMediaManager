using System.Net;

namespace LocalMediaManager.Bridge;

internal static class ProviderHttpClients
{
    public static HttpClient Create(int timeoutSeconds, ProviderNetworkSettingsDto? network)
    {
        var handler = new HttpClientHandler {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        };
        ApplyProxy(handler, network);
        return new(handler, disposeHandler: true) {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 180)),
        };
    }

    public static void ApplyProxy(HttpClientHandler handler, ProviderNetworkSettingsDto? network)
    {
        ProviderNetworkSettingsDto clean = MetadataProviderSettingsService.NormalizeNetwork(network ?? ProviderNetworkSettingsDto.Default);
        if (clean.ProxyMode.Equals("Direct", StringComparison.OrdinalIgnoreCase)) {
            handler.UseProxy = false;
            return;
        }
        handler.UseProxy = true;
        if (!clean.ProxyMode.Equals("Manual", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(clean.ProxyUrl))
            return;

        var proxy = new WebProxy(clean.ProxyUrl);
        if (!string.IsNullOrWhiteSpace(clean.Username))
            proxy.Credentials = new NetworkCredential(clean.Username, clean.Password);
        handler.Proxy = proxy;
    }
}
