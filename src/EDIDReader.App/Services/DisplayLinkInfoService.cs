using EDIDReader.App.Models;

namespace EDIDReader.App.Services;

internal sealed class DisplayLinkInfoService : IDisposable
{
    private readonly IReadOnlyList<IDisplayLinkProvider> _providers;

    private DisplayLinkInfoService(IReadOnlyList<IDisplayLinkProvider> providers)
    {
        _providers = providers;
    }

    public static DisplayLinkInfoService Create()
    {
        var providers = new List<IDisplayLinkProvider>(2);
        if (NvidiaNvApiDisplayLinkProvider.TryCreate() is { } nvidia)
        {
            providers.Add(nvidia);
        }
        if (AmdAdlDisplayLinkProvider.TryCreate() is { } amd)
        {
            providers.Add(amd);
        }
        return new DisplayLinkInfoService(providers);
    }

    public DisplayLinkInfo Read(string sourceName, string monitorName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return DisplayLinkInfo.Unavailable;
        }

        foreach (var provider in _providers)
        {
            try
            {
                var info = provider.Read(sourceName, monitorName);
                if (info.Available)
                {
                    return info;
                }
            }
            catch
            {
                // A vendor API failure must not prevent EDID and DisplayConfig data from loading.
            }
        }
        return DisplayLinkInfo.Unavailable;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}

internal interface IDisplayLinkProvider : IDisposable
{
    DisplayLinkInfo Read(string sourceName, string monitorName);
}

internal readonly record struct DisplayLinkRate(double Gbps, string Generation)
{
    public static DisplayLinkRate FromAmd(int value) => value switch
    {
        1 => new(1.62, "RBR"),
        2 => new(2.16, "eDP 2.16"),
        3 => new(2.43, "eDP 2.43"),
        4 => new(2.70, "HBR"),
        5 => new(4.32, "eDP 4.32"),
        6 => new(5.40, "HBR2"),
        7 => new(8.10, "HBR3"),
        8 => new(10.0, "UHBR10"),
        9 => new(13.5, "UHBR13.5"),
        10 => new(20.0, "UHBR20"),
        _ => default
    };

    public static DisplayLinkRate FromNvidia(int value) => value switch
    {
        0x06 or 0x00A2 => new(1.62, "RBR"),
        0x08 or 0x00D8 => new(2.16, "eDP 2.16"),
        0x09 or 0x00F3 => new(2.43, "eDP 2.43"),
        0x00FA => new(2.50, "eDP 2.50"),
        0x0A or 0x010E => new(2.70, "HBR"),
        0x0C or 0x0144 => new(3.24, "eDP 3.24"),
        0x10 or 0x01B0 => new(4.32, "eDP 4.32"),
        0x14 or 0x021C => new(5.40, "HBR2"),
        0x02A3 => new(6.75, "eDP 6.75"),
        0x1E or 0x032A => new(8.10, "HBR3"),
        0x03E8 => new(10.0, "UHBR10"),
        0x0546 => new(13.5, "UHBR13.5"),
        0x07D0 => new(20.0, "UHBR20"),
        _ => default
    };
}
