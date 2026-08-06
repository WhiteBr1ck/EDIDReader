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
    private const uint GetAssociatedNvidiaDisplayHandleId = 0x35C29134;
    private const uint GetAssociatedDisplayOutputIdId = 0xD995937E;

    private readonly IntPtr _library;
    private readonly NvApiUnload _unload;
    private readonly NvApiGetDisplayPortInfo _getDisplayPortInfo;
    private readonly NvApiGetDisplayIdByDisplayName _getDisplayIdByName;
    private readonly NvApiGetAssociatedNvidiaDisplayHandle? _getAssociatedDisplayHandle;
    private readonly NvApiGetAssociatedDisplayOutputId? _getAssociatedDisplayOutputId;
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
        _getAssociatedDisplayHandle = TryGetDelegate<NvApiGetAssociatedNvidiaDisplayHandle>(query, GetAssociatedNvidiaDisplayHandleId);
        _getAssociatedDisplayOutputId = TryGetDelegate<NvApiGetAssociatedDisplayOutputId>(query, GetAssociatedDisplayOutputIdId);
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
        _ = monitorName;
        var failureReason = "NVIDIA 显示器映射失败";
        var displayNames = GetDisplayNameCandidates(sourceName);

        foreach (var displayName in displayNames)
        {
            if (_getDisplayIdByName(displayName, out var displayId) != NvApiOk)
            {
                continue;
            }

            if (!TryReadDisplayPortInfo(IntPtr.Zero, displayId, out var info))
            {
                failureReason = "NVIDIA DP 信息读取失败";
                continue;
            }

            if (TryCreateDisplayLinkInfo(info, out var result, out failureReason))
            {
                return result;
            }
        }

        if (_getAssociatedDisplayHandle is not null && _getAssociatedDisplayOutputId is not null)
        {
            foreach (var displayName in displayNames)
            {
                if (_getAssociatedDisplayHandle(displayName, out var displayHandle) != NvApiOk
                    || displayHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (_getAssociatedDisplayOutputId(displayHandle, out var outputId) != NvApiOk
                    || outputId == 0)
                {
                    failureReason = "NVIDIA 输出 ID 读取失败";
                    continue;
                }

                if (!TryReadDisplayPortInfo(displayHandle, outputId, out var info))
                {
                    failureReason = "NVIDIA DP 信息读取失败";
                    continue;
                }

                if (TryCreateDisplayLinkInfo(info, out var result, out failureReason))
                {
                    return result;
                }
            }
        }

        return DisplayLinkInfo.UnavailableFrom("NVIDIA NVAPI", failureReason);
    }

    private bool TryReadDisplayPortInfo(IntPtr displayHandle, uint outputId, out NvDisplayPortInfo info)
    {
        info = NvDisplayPortInfo.Create(2);
        var status = _getDisplayPortInfo(displayHandle, outputId, ref info);
        if (status == NvApiIncompatibleStructVersion)
        {
            info = NvDisplayPortInfo.Create(1);
            status = _getDisplayPortInfo(displayHandle, outputId, ref info);
        }
        return status == NvApiOk;
    }

    private static bool TryCreateDisplayLinkInfo(
        NvDisplayPortInfo info,
        out DisplayLinkInfo result,
        out string failureReason)
    {
        if ((info.Flags & 0x03) == 0)
        {
            result = DisplayLinkInfo.Unavailable;
            failureReason = "当前输出不是 NVIDIA DP";
            return false;
        }

        var current = DisplayLinkRate.FromNvidia(info.CurrentLinkRate);
        var maximum = DisplayLinkRate.FromNvidia(info.MaximumLinkRate);
        if (current.Gbps <= 0 || info.CurrentLaneCount <= 0)
        {
            result = DisplayLinkInfo.Unavailable;
            failureReason = "NVIDIA DP 链路数据无效";
            return false;
        }

        result = new DisplayLinkInfo
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
        failureReason = string.Empty;
        return true;
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

    private static IReadOnlyList<string> GetDisplayNameCandidates(string sourceName)
    {
        var value = sourceName.Trim();
        if (value.Length == 0)
        {
            return [];
        }

        var values = new List<string>(2) { value };
        if (value.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            values.Add(@"\\" + value[4..]);
        }
        else if (value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            values.Add(@"\\.\" + value[2..]);
        }
        return values;
    }

    private static T GetDelegate<T>(NvApiQueryInterface query, uint id) where T : Delegate
    {
        var address = query(id);
        return address != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : throw new EntryPointNotFoundException($"NVAPI 0x{id:X8}");
    }

    private static T? TryGetDelegate<T>(NvApiQueryInterface query, uint id) where T : Delegate
    {
        var address = query(id);
        return address != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NvApiQueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiInitialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiUnload();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvApiGetDisplayIdByDisplayName([MarshalAs(UnmanagedType.LPStr)] string displayName, out uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvApiGetAssociatedNvidiaDisplayHandle([MarshalAs(UnmanagedType.LPStr)] string displayName, out IntPtr displayHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvApiGetAssociatedDisplayOutputId(IntPtr displayHandle, out uint outputId);

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
