using System.Runtime.InteropServices;
using EDIDReader.App.Models;

namespace EDIDReader.App.Services;

internal sealed class AmdAdlDisplayLinkProvider : IDisplayLinkProvider
{
    private const int AdlOk = 0;
    private const int AmdVendorId = 1002;
    private const int DisplayConnected = 0x00000001;
    private const int DisplayMapped = 0x00000002;
    private const int DpSettings = 1;

    private readonly IntPtr _library;
    private readonly IntPtr _context;
    private readonly AdlMainMemoryAlloc _memoryAlloc;
    private readonly Adl2MainControlDestroy _destroy;
    private readonly Adl2AdapterNumberOfAdaptersGet _numberOfAdaptersGet;
    private readonly Adl2AdapterAdapterInfoGet _adapterInfoGet;
    private readonly Adl2DisplayDisplayInfoGet _displayInfoGet;
    private readonly Adl2DisplayDceGet _dceGet;
    private readonly IReadOnlyList<AdlAdapterInfo> _adapters;
    private bool _disposed;

    private AmdAdlDisplayLinkProvider(IntPtr library)
    {
        _library = library;
        _memoryAlloc = size => size > 0 ? Marshal.AllocCoTaskMem(size) : IntPtr.Zero;
        var create = GetDelegate<Adl2MainControlCreate>(library, "ADL2_Main_Control_Create");
        _destroy = GetDelegate<Adl2MainControlDestroy>(library, "ADL2_Main_Control_Destroy");
        _numberOfAdaptersGet = GetDelegate<Adl2AdapterNumberOfAdaptersGet>(library, "ADL2_Adapter_NumberOfAdapters_Get");
        _adapterInfoGet = GetDelegate<Adl2AdapterAdapterInfoGet>(library, "ADL2_Adapter_AdapterInfo_Get");
        _displayInfoGet = GetDelegate<Adl2DisplayDisplayInfoGet>(library, "ADL2_Display_DisplayInfo_Get");
        _dceGet = GetDelegate<Adl2DisplayDceGet>(library, "ADL2_Display_DCE_Get");

        if (create(_memoryAlloc, 1, out _context) != AdlOk || _context == IntPtr.Zero)
        {
            throw new InvalidOperationException("AMD ADL initialization failed.");
        }
        try
        {
            _adapters = ReadAdapters();
        }
        catch
        {
            _destroy(_context);
            throw;
        }
    }

    public static AmdAdlDisplayLinkProvider? TryCreate()
    {
        var names = Environment.Is64BitProcess
            ? new[] { "atiadlxx.dll", "atiadlxy.dll" }
            : new[] { "atiadlxy.dll", "atiadlxx.dll" };
        foreach (var name in names)
        {
            if (!NativeLibrary.TryLoad(name, out var library))
            {
                continue;
            }
            try
            {
                return new AmdAdlDisplayLinkProvider(library);
            }
            catch
            {
                NativeLibrary.Free(library);
            }
        }
        return null;
    }

    public DisplayLinkInfo Read(string sourceName, string monitorName)
    {
        var adapters = _adapters
            .Where(adapter => adapter.VendorId == AmdVendorId
                && DisplayNamesEqual(adapter.DisplayName, sourceName))
            .ToArray();
        if (adapters.Length == 0)
        {
            return DisplayLinkInfo.Unavailable;
        }

        foreach (var adapter in adapters)
        {
            var matches = ReadDisplayLinks(adapter.AdapterIndex);
            var selected = matches
                .Where(match => MonitorNamesEqual(match.DisplayName, monitorName))
                .Select(match => match.Info)
                .FirstOrDefault();
            if (selected?.Available != true)
            {
                selected = matches.Select(match => match.Info).FirstOrDefault();
            }
            if (selected?.Available == true)
            {
                return selected;
            }
        }
        return DisplayLinkInfo.Unavailable;
    }

    private IReadOnlyList<(string DisplayName, DisplayLinkInfo Info)> ReadDisplayLinks(int adapterIndex)
    {
        var values = new List<(string, DisplayLinkInfo)>();
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (_displayInfoGet(_context, adapterIndex, out var displayCount, out buffer, 1) != AdlOk
                || displayCount <= 0
                || buffer == IntPtr.Zero)
            {
                return values;
            }

            var itemSize = Marshal.SizeOf<AdlDisplayInfo>();
            for (var index = 0; index < displayCount; index++)
            {
                var display = Marshal.PtrToStructure<AdlDisplayInfo>(IntPtr.Add(buffer, index * itemSize));
                if ((display.DisplayInfoValue & DisplayConnected) == 0
                    || (display.DisplayInfoValue & DisplayMapped) == 0)
                {
                    continue;
                }

                var settings = new AdlDceSettings { Type = DpSettings };
                if (_dceGet(_context, adapterIndex, display.DisplayId.DisplayLogicalIndex, ref settings) != AdlOk)
                {
                    continue;
                }

                var rate = DisplayLinkRate.FromAmd(settings.LinkRate);
                if (rate.Gbps <= 0 || settings.NumberOfActiveLanes == 0)
                {
                    continue;
                }

                values.Add((display.DisplayName, new DisplayLinkInfo
                {
                    Available = true,
                    Vendor = "AMD",
                    Source = "AMD ADL",
                    CurrentLinkRateGbps = rate.Gbps,
                    CurrentLaneCount = checked((int)settings.NumberOfActiveLanes),
                    TotalAvailableLaneCount = settings.NumberOfTotalLanes > 0
                        ? checked((int)settings.NumberOfTotalLanes)
                        : null,
                    CurrentGeneration = rate.Generation
                }));
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
        return values;
    }

    private IReadOnlyList<AdlAdapterInfo> ReadAdapters()
    {
        if (_numberOfAdaptersGet(_context, out var count) != AdlOk || count <= 0)
        {
            return [];
        }

        var itemSize = Marshal.SizeOf<AdlAdapterInfo>();
        var bufferSize = checked(itemSize * count);
        var buffer = Marshal.AllocCoTaskMem(bufferSize);
        try
        {
            for (var offset = 0; offset < bufferSize; offset++)
            {
                Marshal.WriteByte(buffer, offset, 0);
            }
            if (_adapterInfoGet(_context, buffer, bufferSize) != AdlOk)
            {
                return [];
            }

            var values = new List<AdlAdapterInfo>(count);
            for (var index = 0; index < count; index++)
            {
                values.Add(Marshal.PtrToStructure<AdlAdapterInfo>(IntPtr.Add(buffer, index * itemSize)));
            }
            return values;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_context != IntPtr.Zero)
        {
            _destroy(_context);
        }
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
        }
    }

    private static T GetDelegate<T>(IntPtr library, string name) where T : Delegate
        => NativeLibrary.TryGetExport(library, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : throw new EntryPointNotFoundException(name);

    private static bool DisplayNamesEqual(string first, string second)
        => string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MonitorNamesEqual(string first, string second)
    {
        static string Normalize(string value)
            => string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit));
        var left = Normalize(first);
        var right = Normalize(second);
        return left.Length > 0 && right.Length > 0
            && (left.Contains(right, StringComparison.OrdinalIgnoreCase)
                || right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr AdlMainMemoryAlloc(int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Adl2MainControlCreate(AdlMainMemoryAlloc callback, int enumConnectedAdapters, out IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Adl2MainControlDestroy(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Adl2AdapterNumberOfAdaptersGet(IntPtr context, out int numberOfAdapters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Adl2AdapterAdapterInfoGet(IntPtr context, IntPtr info, int inputSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Adl2DisplayDisplayInfoGet(IntPtr context, int adapterIndex, out int numberOfDisplays, out IntPtr info, int forceDetect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Adl2DisplayDceGet(IntPtr context, int adapterIndex, int displayIndex, ref AdlDceSettings settings);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdlAdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string UniqueDeviceId;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        public int Present;
        public int Exists;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPathExtended;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PnpString;
        public int OsDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlDisplayId
    {
        public int DisplayLogicalIndex;
        public int DisplayPhysicalIndex;
        public int DisplayLogicalAdapterIndex;
        public int DisplayPhysicalAdapterIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdlDisplayInfo
    {
        public AdlDisplayId DisplayId;
        public int DisplayControllerIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayManufacturerName;
        public int DisplayType;
        public int DisplayOutputType;
        public int DisplayConnector;
        public int DisplayInfoMask;
        public int DisplayInfoValue;
    }

    [StructLayout(LayoutKind.Explicit, Size = 88)]
    private struct AdlDceSettings
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(4)] public int LinkRate;
        [FieldOffset(8)] public uint NumberOfActiveLanes;
        [FieldOffset(12)] public uint NumberOfTotalLanes;
        [FieldOffset(16)] public int RelativePreEmphasis;
        [FieldOffset(20)] public int RelativeVoltageSwing;
        [FieldOffset(24)] public int PersistFlag;
    }
}
