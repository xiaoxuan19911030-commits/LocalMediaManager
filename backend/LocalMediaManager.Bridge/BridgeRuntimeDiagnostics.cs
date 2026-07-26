using System.Diagnostics;
using System.Runtime.InteropServices;
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
    int JavBusPageCacheEntries,
    IReadOnlyList<BridgeVirtualMemorySummary> VirtualMemorySummaries);

public sealed record BridgeVirtualMemorySummary(string Type, long CommittedBytes, int RegionCount);

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
            javBus.PageCacheEntryCount,
            ReadVirtualMemorySummaries());
    }

    private static IReadOnlyList<BridgeVirtualMemorySummary> ReadVirtualMemorySummaries()
    {
        if (!OperatingSystem.IsWindows()) return [];
        const uint memCommit = 0x1000;
        var summaries = new Dictionary<uint, (long Bytes, int Regions)>();
        nint address = 0;
        nuint informationSize = (nuint)Marshal.SizeOf<MemoryBasicInformation>();
        while (true) {
            nuint result = VirtualQuery((IntPtr)address, out MemoryBasicInformation information, informationSize);
            if (result == 0 || information.RegionSize == 0) break;
            if (information.State == memCommit) {
                summaries.TryGetValue(information.Type, out (long Bytes, int Regions) current);
                long bytes = information.RegionSize > long.MaxValue ? long.MaxValue : (long)information.RegionSize;
                summaries[information.Type] = (checked(current.Bytes + bytes), current.Regions + 1);
            }
            nint next = address + (nint)information.RegionSize;
            if (next <= address) break;
            address = next;
        }
        return summaries.OrderBy(pair => pair.Key).Select(pair => new BridgeVirtualMemorySummary(MemoryTypeName(pair.Key), pair.Value.Bytes, pair.Value.Regions)).ToArray();
    }

    private static string MemoryTypeName(uint type) => type switch {
        0x00020000 => "Private",
        0x00040000 => "Mapped",
        0x01000000 => "Image",
        _ => "Other",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQuery(IntPtr address, out MemoryBasicInformation buffer, nuint length);
}
