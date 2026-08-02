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
    IReadOnlyList<BridgeVirtualMemorySummary> VirtualMemorySummaries,
    BridgeNativeHeapSnapshot? NativeHeap,
    ImagePipelineValidationCounters? ImagePipeline);

public sealed record BridgeVirtualMemorySummary(string Type, long CommittedBytes, int RegionCount);
public sealed record BridgeNativeHeapSnapshot(
    int HeapCount,
    bool WalkSucceeded,
    long BusyEntries,
    long FreeEntries,
    long BusyBytes,
    long FreeBytes,
    long LargestBusyBlock,
    long LargestFreeBlock,
    bool Complete,
    int? ErrorCode,
    string? InterruptedAt);

public static class BridgeRuntimeDiagnostics
{
    // This method is only invoked by the validation-only HTTP endpoint.
    public static BridgeRuntimeMemorySnapshot Capture(bool fullGcRequested, bool clearSqlitePools, bool includeNativeHeap,
        ProviderManager providers, JavBusProvider javBus)
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
            ReadVirtualMemorySummaries(),
            includeNativeHeap ? ReadNativeHeapSnapshot() : null,
            ImagePipelineValidationDiagnostics.IsEnabled ? ImagePipelineValidationDiagnostics.Snapshot() : null);
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

    private static BridgeNativeHeapSnapshot? ReadNativeHeapSnapshot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        uint heapCount = GetProcessHeaps(0, null);
        if (heapCount == 0)
        {
            int error = Marshal.GetLastWin32Error();
            return new(0, false, 0, 0, 0, 0, 0, 0, false, error, "GetProcessHeaps");
        }
        var heaps = new IntPtr[heapCount];
        uint read = GetProcessHeaps(heapCount, heaps);
        if (read == 0)
        {
            int error = Marshal.GetLastWin32Error();
            return new(0, false, 0, 0, 0, 0, 0, 0, false, error, "GetProcessHeaps");
        }

        long busyBytes = 0;
        long freeBytes = 0;
        long busyEntries = 0;
        long freeEntries = 0;
        long largestBusyBlock = 0;
        long largestFreeBlock = 0;
        int heapIndex = 0;
        foreach (IntPtr heap in heaps.Take((int)Math.Min(read, (uint)heaps.Length))) {
            ProcessHeapEntry entry = default;
            long entryIndex = 0;
            while (HeapWalk(heap, ref entry)) {
                entryIndex++;
                long bytes = entry.DataSize;
                if ((entry.Flags & 0x0004) != 0)
                {
                    busyBytes = SaturatingAdd(busyBytes, bytes);
                    busyEntries = SaturatingAdd(busyEntries, 1);
                    largestBusyBlock = Math.Max(largestBusyBlock, bytes);
                }
                else if ((entry.Flags & 0x0002) != 0)
                {
                    freeBytes = SaturatingAdd(freeBytes, bytes);
                    freeEntries = SaturatingAdd(freeEntries, 1);
                    largestFreeBlock = Math.Max(largestFreeBlock, bytes);
                }
            }
            int error = Marshal.GetLastWin32Error();
            if (error is not 0 and not 259)
                return new((int)read, false, busyEntries, freeEntries, busyBytes, freeBytes,
                    largestBusyBlock, largestFreeBlock, false, error, $"heap={heapIndex}; entry={entryIndex}");
            heapIndex++;
        }
        return new((int)read, true, busyEntries, freeEntries, busyBytes, freeBytes,
            largestBusyBlock, largestFreeBlock, true, null, null);
    }

    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;

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

    // PROCESS_HEAP_ENTRY has a pointer-sized first field followed by a 32-bit cbData.
    // Keep the trailing union so HeapWalk receives the native structure's full size.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessHeapEntry
    {
        public IntPtr Data;
        public uint DataSize;
        public byte Overhead;
        public byte RegionIndex;
        public ushort Flags;
        public ProcessHeapEntryUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ProcessHeapEntryUnion
    {
        [FieldOffset(0)] public IntPtr BlockHandle;
        [FieldOffset(0)] public uint RegionCommittedSize;
        [FieldOffset(4)] public uint RegionUncommittedSize;
        [FieldOffset(8)] public IntPtr RegionFirstBlock;
        [FieldOffset(16)] public IntPtr RegionLastBlock;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQuery(IntPtr address, out MemoryBasicInformation buffer, nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessHeaps(uint numberOfHeaps, [Out] IntPtr[]? processHeaps);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool HeapWalk(IntPtr heap, ref ProcessHeapEntry entry);
}
