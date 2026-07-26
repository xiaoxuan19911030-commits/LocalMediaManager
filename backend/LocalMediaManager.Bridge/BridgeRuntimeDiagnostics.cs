using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record BridgeRuntimeMemorySnapshot(
    DateTimeOffset CapturedAt,
    bool FullGcRequested,
    bool SqlitePoolsCleared,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long VirtualMemoryBytes,
    int HandleCount,
    int ThreadCount,
    long ManagedHeapBytes,
    long TotalAllocatedBytes,
    long Generation0Bytes,
    long Generation1Bytes,
    long Generation2Bytes,
    long LargeObjectHeapBytes,
    long PinnedObjectHeapBytes,
    int Generation0Collections,
    int Generation1Collections,
    int Generation2Collections,
    int ProviderCacheEntries,
    int ProviderHistoryEntries,
    int JavBusPageCacheEntries);

public static class BridgeRuntimeDiagnostics
{
    // This method is only invoked by the validation-only HTTP endpoint.
    public static BridgeRuntimeMemorySnapshot Capture(bool fullGcRequested, bool clearSqlitePools, ProviderManager providers, JavBusProvider javBus)
    {
        if (clearSqlitePools) SqliteConnection.ClearAllPools();
        if (fullGcRequested)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        GCMemoryInfo info = GC.GetGCMemoryInfo();
        ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
        long generation0 = generations.Length > 0 ? generations[0].SizeAfterBytes : 0;
        long generation1 = generations.Length > 1 ? generations[1].SizeAfterBytes : 0;
        long generation2 = generations.Length > 2 ? generations[2].SizeAfterBytes : 0;
        long largeObjectHeap = generations.Length > 3 ? generations[3].SizeAfterBytes : 0;
        long pinnedObjectHeap = generations.Length > 4 ? generations[4].SizeAfterBytes : 0;
        using Process process = Process.GetCurrentProcess();
        return new(
            DateTimeOffset.UtcNow,
            fullGcRequested,
            clearSqlitePools,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.VirtualMemorySize64,
            process.HandleCount,
            process.Threads.Count,
            info.HeapSizeBytes,
            GC.GetTotalAllocatedBytes(precise: false),
            generation0,
            generation1,
            generation2,
            largeObjectHeap,
            pinnedObjectHeap,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            providers.CacheEntryCount,
            providers.HistoryEntryCount,
            javBus.PageCacheEntryCount);
    }
}
