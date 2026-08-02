using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;

namespace LocalMediaManager.Bridge;

public interface IAiModel
{
    string Id { get; }
    string Version { get; }
    string RelativeResourcePath { get; }
}

public sealed record AiModel(string Id, string Version, string RelativeResourcePath) : IAiModel;

public sealed class AiModelRegistry
{
    public static readonly AiModel YuNetFaceDetector = new("face-detection-yunet", "2023mar", Path.Combine("AppResources", "AI", "Models", "face_detection_yunet_2023mar.onnx"));
    private readonly IReadOnlyDictionary<string, IAiModel> models = new Dictionary<string, IAiModel>(StringComparer.OrdinalIgnoreCase) {
        [YuNetFaceDetector.Id] = YuNetFaceDetector,
    };

    public IAiModel GetRequired(string id) => models.TryGetValue(id, out IAiModel? model) ? model : throw new KeyNotFoundException($"AI model is not registered: {id}");
}

public sealed class AiModelSessionManager(AiModelRegistry registry) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<InferenceSession>> sessions = new(StringComparer.OrdinalIgnoreCase);

    public InferenceSession GetRequiredSession(string modelId) => sessions.GetOrAdd(modelId, id => new Lazy<InferenceSession>(() => {
        IAiModel model = registry.GetRequired(id);
        string path = Path.Combine(AppContext.BaseDirectory, model.RelativeResourcePath);
        if (!File.Exists(path)) throw new FileNotFoundException($"AI model is unavailable: {model.Id}", path);
        return new InferenceSession(path, new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 });
    }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    public void Dispose() { foreach (Lazy<InferenceSession> session in sessions.Values) if (session.IsValueCreated) session.Value.Dispose(); }
}

public sealed class AiInferenceQueue : IDisposable
{
    private readonly SemaphoreSlim slots = new(2, 2);
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken token = default)
    {
        await slots.WaitAsync(token);
        try { return await work(token); }
        finally { slots.Release(); }
    }
    public void Dispose() => slots.Dispose();
}

public sealed class AiResultCache(string dataRoot)
{
    private readonly string root = Path.Combine(dataRoot, "AI", "Cache");
    public async Task<T?> ReadAsync<T>(string key, CancellationToken token = default)
    {
        string path = Path.Combine(root, Hash(key) + ".json");
        if (!File.Exists(path)) return default;
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token);
    }
    public async Task WriteAsync<T>(string key, T value, CancellationToken token = default)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, Hash(key) + ".json");
        string temp = path + ".tmp";
        await using (FileStream stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, value, cancellationToken: token);
        File.Move(temp, path, true);
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
