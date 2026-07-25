using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace LocalMediaManager.Bridge;

public sealed class ProviderNetworkException(string provider, Uri uri, string message, Exception? inner = null)
    : HttpRequestException(message, inner)
{
    public string Provider { get; } = provider;
    public Uri Uri { get; } = uri;
}

public static class ProviderNetworkDiagnostics
{
    private static readonly Regex CloudflareChallenge = new("cf-chl|driver-verify|checking your browser|turnstile", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Blocked = new("captcha|age.?check|sign.?in|login|not-available-in-your-region|region", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<string> ProbeAsync(string provider, Uri uri, int timeoutSeconds, Action<HttpRequestMessage>? configure = null,
        CancellationToken token = default, ProviderNetworkSettingsDto? network = null)
    {
        var lines = new List<string>();
        var watch = Stopwatch.StartNew();
        ProviderNetworkSettingsDto cleanNetwork = MetadataProviderSettingsService.NormalizeNetwork(network ?? ProviderNetworkSettingsDto.Default);
        lines.Add($"Proxy Mode: {cleanNetwork.ProxyMode}");
        Uri? proxyUri = EffectiveProxyUri(uri, cleanNetwork);
        bool skipDirectTcp = cleanNetwork.ProxyMode.Equals("Manual", StringComparison.OrdinalIgnoreCase) || proxyUri is not null;

        if (skipDirectTcp) {
            lines.Add(proxyUri is null
                ? "Direct TCP skipped because manual proxy is configured"
                : $"Direct TCP skipped because system proxy is configured ({proxyUri})");
        } else {
            IPAddress[] addresses;
            try {
                lines.Add($"DNS Resolve: {uri.Host}");
                addresses = await Dns.GetHostAddressesAsync(uri.Host, token).WaitAsync(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 60)), token);
                lines.Add($"DNS OK: {string.Join(", ", addresses.Select(value => value.ToString()).Take(4))}");
            } catch (Exception error) when (error is SocketException or TimeoutException or OperationCanceledException) {
                lines.Add($"DNS Failed: {error.Message}");
                return Format(provider, lines, watch);
            }

            int port = uri.Port > 0 ? uri.Port : uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
            try {
                lines.Add($"TCP Connect: {uri.Host}:{port}");
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(addresses, port, token).AsTask().WaitAsync(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 60)), token);
                lines.Add("TCP Connected");

                if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
                    lines.Add("TLS Handshake");
                    using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, errors) => errors == SslPolicyErrors.None);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.Host }, token)
                        .WaitAsync(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 60)), token);
                    lines.Add($"TLS OK: {ssl.SslProtocol}");
                }
            } catch (Exception error) when (error is SocketException or IOException or AuthenticationException or TimeoutException or OperationCanceledException) {
                lines.Add($"{(uri.Scheme == "https" ? "TLS/TCP" : "TCP")} Failed: {error.Message}");
                return Format(provider, lines, watch);
            }
        }

        try {
            using var handler = new HttpClientHandler {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false,
            };
            ProviderHttpClients.ApplyProxy(handler, cleanNetwork);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 180)) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            configure?.Invoke(request);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            lines.Add($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.Headers.Location is Uri location)
                lines.Add($"Redirect: {location}");
            string? server = response.Headers.Server.Count > 0 ? string.Join(" ", response.Headers.Server) : null;
            if (!string.IsNullOrWhiteSpace(server)) lines.Add($"Server: {server}");

            string sample = await ReadSampleAsync(response, token);
            bool cloudflareChallenge = CloudflareChallenge.IsMatch(sample);
            if (cloudflareChallenge)
                lines.Add("Cloudflare: detected");
            if (Blocked.IsMatch(sample))
                lines.Add("Blocked Page: login/age/region/captcha signal detected");
            if (response.IsSuccessStatusCode && !cloudflareChallenge && !Blocked.IsMatch(sample))
                lines.Add("Parse Input: reachable HTML/API response");
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException) {
            lines.Add(error is TaskCanceledException ? "HTTP Timeout" : $"HTTP Failed: {error.Message}");
        }
        return Format(provider, lines, watch);
    }

    public static void BrowserHeaders(HttpRequestMessage request, string? cookie = null, string? referer = null)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.7,en;q=0.5");
        if (!string.IsNullOrWhiteSpace(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out Uri? refererUri)) request.Headers.Referrer = refererUri;
    }

    public static void JsonHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("LocalMediaManager/0.7.5");
        request.Headers.Accept.ParseAdd("application/json,*/*;q=0.8");
    }

    private static Uri? EffectiveProxyUri(Uri uri, ProviderNetworkSettingsDto network)
    {
        if (!network.ProxyMode.Equals("System", StringComparison.OrdinalIgnoreCase)) return null;
        try {
            IWebProxy proxy = WebRequest.GetSystemWebProxy();
            if (proxy.IsBypassed(uri)) return null;
            Uri? candidate = proxy.GetProxy(uri);
            if (candidate is null) return null;
            return Uri.Compare(candidate, uri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
                ? null
                : candidate;
        } catch {
            return null;
        }
    }

    private static async Task<string> ReadSampleAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength == 0) return "";
        await using Stream stream = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[16384];
        int read = await stream.ReadAsync(buffer, token);
        return read <= 0 ? "" : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string Format(string provider, IReadOnlyList<string> lines, Stopwatch watch) =>
        $"{provider} network trace ({watch.ElapsedMilliseconds} ms): {string.Join(" -> ", lines)}";
}
