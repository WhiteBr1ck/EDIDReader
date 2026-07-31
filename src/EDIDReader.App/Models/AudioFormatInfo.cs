namespace EDIDReader.App.Models;

public sealed record AudioFormatInfo
{
    public string Format { get; init; } = "未知格式";
    public int Channels { get; init; }
    public string SampleRates { get; init; } = "未声明";
    public string Detail { get; init; } = "未声明";
    public string ChannelText => Channels > 0 ? $"{Channels}" : "未知";
}
