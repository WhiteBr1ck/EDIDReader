using System.Runtime.InteropServices;
using EDIDReader.App.Models;

namespace EDIDReader.App.Services;

internal sealed class NvidiaNvApiDisplayLinkProvider : IDisplayLinkProvider
{
    private const int NvApiOk = 0;
    private const int NvApiIncompatibleStructVersion = -9;
    private const uint InitializeId = 0x0150E828;
    private const uint UnloadId = 0xD22BDD7E;
    private const uint GetDisplayPortInfoId = 0xC64FF367;
    private const uint GetDisplayIdByNameId = 0xAE457190;

    private readonly IntPtr _library;
    private readonly NvApiUnload _unload;
    private readonly NvApiGetDisplayPortInfo _getDisplayPortInfo;
    private readonly NvApiGetDisplayIdByDisplayName _getDisplayIdByName;
    private bool _disposed;

    private NvidiaNvApiDisplayLinkProvider(IntPtr library)
    {
        _library = library;
        var query = Marshal.GetDelegateForFunctionPointer<NvApiQueryInterface>(
            NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
        var initialize = GetDelegate<NvApiInitialize>(query, InitializeId);
        _unload = GetDelegate<NvApiUnload>(query, UnloadId);
        _getDisplayPortInfo = GetDelegate<NvApiGetDisplayPortInfo>(query, GetDisplayPortInfoId);
        _getDisplayIdByName = GetDelegate<NvApiGetDisplayIdByDisplayName>(query, GetDisplayIdByNameId);
        if (initialize() != NvApiOk)
        {
            throw new InvalidOperationException("NVIDIA NVAPI initialization failed.");
        }
    }

    public static NvidiaNvApiDisplayLinkProvider? TryCreate()
    {
        var name = Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll";
        if (!NativeLibrary.TryLoad(name, out var library))
        {
            return null;
        }
        try
        {
            return new NvidiaNvApiDisplayLinkProvider(library);
        }
        catch
        {
            NativeLibrary.Free(library);
            return null;
        }
    }

    public DisplayLinkInfo Read(string sourceName, string monitorName)
    {
        var displayName = ToNvApiDisplayName(sourceName);
        if (_getDisplayIdByName(displayName, out var displayId) != NvApiOk)
        {
            return DisplayLinkInfo.Unavailable;
        }

        var info = NvDisplayPortInfo.Create(2);
        var status = _getDisplayPortInfo(IntPtr.Zero, displayId, ref info);
        if (status == NvApiIncompatibleStructVersion)
        {
            info = NvDisplayPortInfo.Create(1);
            status = _getDisplayPortInfo(IntPtr.Zero, displayId, ref info);
        }
        if (status != NvApiOk || (info.Flags & 0x01) == 0)
        {
            return DisplayLinkInfo.Unavailable;
        }

        var current = DisplayLinkRate.FromNvidia(info.CurrentLinkRate);
        var maximum = DisplayLinkRate.FromNvidia(info.MaximumLinkRate);
        if (current.Gbps <= 0 || info.CurrentLaneCount <= 0)
        {
            return DisplayLinkInfo.Unavailable;
        }

        return new DisplayLinkInfo
        {
            Available = true,
            Vendor = "NVIDIA",
            Source = "NVIDIA NVAPI",
            CurrentLinkRateGbps = current.Gbps,
            CurrentLaneCount = info.CurrentLaneCount,
            MaximumLinkRateGbps = maximum.Gbps > 0 ? maximum.Gbps : null,
            MaximumLaneCount = info.MaximumLaneCount > 0 ? info.MaximumLaneCount : null,
            CurrentGeneration = current.Generation,
            MaximumGeneration = maximum.Gbps > 0 ? maximum.Generation : "驱动未提供"
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _unload();
        NativeLibrary.Free(_library);
    }

    private static string ToNvApiDisplayName(string sourceName)
        => sourceName.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? @"\\" + sourceName[4..]
            : sourceName;

    private static T GetDelegate<T>(NvApiQueryInterface query, uint id) where T : Delegate
    {
        var address = query(id);
        return address != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : throw new EntryPointNotFoundException($"NVAPI 0x{id:X8}");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NvApiQueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiInitialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiUnload();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvApiGetDisplayIdByDisplayName([MarshalAs(UnmanagedType.LPStr)] string displayName, out uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiGetDisplayPortInfo(IntPtr displayHandle, uint outputId, ref NvDisplayPortInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvDisplayPortInfo
    {
        public uint Version;
        public uint DpcdVersion;
        public int MaximumLinkRate;
        public int MaximumLaneCount;
        public int CurrentLinkRate;
        public int CurrentLaneCount;
        public int ColorFormat;
        public int DynamicRange;
        public int Colorimetry;
        public int BitsPerComponent;
        public uint Flags;

        public static NvDisplayPortInfo Create(int version)
            => new()
            {
                Version = checked((uint)Marshal.SizeOf<NvDisplayPortInfo>()) | checked((uint)version << 16)
            };
    }
}
