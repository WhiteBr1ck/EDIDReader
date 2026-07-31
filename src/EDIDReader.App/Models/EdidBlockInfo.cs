namespace EDIDReader.App.Models;

public sealed record EdidBlockInfo
{
    public int Index { get; init; }
    public string Type { get; init; } = "未知数据块";
    public int ByteCount { get; init; }
    public bool ChecksumValid { get; init; }

    public string IndexText => $"块 {Index}";
    public string SizeText => $"{ByteCount} B";
    public string ChecksumText => ChecksumValid ? "校验通过" : "校验失败";
}
