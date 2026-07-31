using System.Buffers.Binary;
using EDIDReader.App.Models;

namespace EDIDReader.App.Services;

internal static class EdidParser
{
    private static readonly byte[] Header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    public static ParsedEdid Parse(byte[] bytes)
    {
        var result = new ParsedEdid
        {
            RawBytes = bytes.ToArray(),
            HeaderValid = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(Header)
        };

        if (bytes.Length < 128)
        {
            result.StatusText = "EDID 少于 128 字节，数据已截断";
            return result;
        }

        ParseBaseBlock(bytes.AsSpan(0, 128), result);

        var availableBlocks = bytes.Length / 128;
        var expectedBlocks = result.DeclaredExtensionCount + 1;
        result.IsTruncated = availableBlocks < expectedBlocks;
        var parsedBlocks = Math.Min(availableBlocks, expectedBlocks);

        for (var blockIndex = 0; blockIndex < parsedBlocks; blockIndex++)
        {
            var block = bytes.AsSpan(blockIndex * 128, 128);
            var valid = ChecksumValid(block);
            var type = blockIndex == 0 ? "EDID 基础块" : ExtensionName(block[0]);
            result.Blocks.Add(new EdidBlockInfo
            {
                Index = blockIndex,
                Type = type,
                ByteCount = 128,
                ChecksumValid = valid
            });

            if (blockIndex == 0)
            {
                continue;
            }

            result.ExtensionNames.Add(type);
            switch (block[0])
            {
                case 0x02:
                    ParseCtaExtension(block, result);
                    break;
                case 0x70:
                    ParseDisplayIdExtension(block, result);
                    break;
                default:
                    result.UnknownExtensionCount++;
                    break;
            }
        }

        result.IsValid = result.HeaderValid && !result.IsTruncated && result.Blocks.Count > 0 && result.Blocks.All(block => block.ChecksumValid);
        result.StatusText = result.IsValid
            ? "头标识、长度与全部块校验通过"
            : BuildInvalidStatus(result);

        FinalizeResult(result);
        return result;
    }

    private static void ParseBaseBlock(ReadOnlySpan<byte> block, ParsedEdid result)
    {
        var manufacturerWord = BinaryPrimitives.ReadUInt16BigEndian(block.Slice(8, 2));
        result.ManufacturerCode = new string(
        [
            DecodeManufacturerCharacter((manufacturerWord >> 10) & 0x1F),
            DecodeManufacturerCharacter((manufacturerWord >> 5) & 0x1F),
            DecodeManufacturerCharacter(manufacturerWord & 0x1F)
        ]);

        result.ProductCode = $"0x{BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(10, 2)):X4}";
        var numericSerial = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(12, 4));
        result.SerialNumber = numericSerial == 0 ? "未声明" : numericSerial.ToString();

        var week = block[16];
        var year = 1990 + block[17];
        result.Manufactured = week switch
        {
            0 => $"{year} 年",
            255 => $"型号年份 {year}",
            _ => $"{year} 年第 {week} 周"
        };

        result.Version = $"EDID {block[18]}.{block[19]}";
        result.DeclaredExtensionCount = block[126];
        result.IsDigital = (block[20] & 0x80) != 0;
        result.InputDefinition = ParseInputDefinition(block[20], result.IsDigital, block[18], block[19]);
        result.DeclaredBitDepth = ParseDeclaredBitDepth(block[20], result.IsDigital);
        var declaredBitDepth = ParseDeclaredBitDepthValue(block[20], result.IsDigital);
        if (declaredBitDepth > 0)
        {
            result.SupportedBitDepths.Add(declaredBitDepth);
        }

        var widthCm = block[21];
        var heightCm = block[22];
        result.PhysicalSize = widthCm > 0 && heightCm > 0
            ? $"{widthCm} × {heightCm} cm"
            : "未声明";
        result.Gamma = block[23] == 0xFF ? "未声明" : $"{(block[23] + 100) / 100d:0.00}";
        result.SrgbDefault = (block[24] & 0x04) != 0;

        ParseChromaticity(block, result);
        ParseEstablishedTimings(block, result);
        ParseStandardTimings(block, result);

        for (var descriptorIndex = 0; descriptorIndex < 4; descriptorIndex++)
        {
            var descriptor = block.Slice(54 + descriptorIndex * 18, 18);
            var pixelClock = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(0, 2));
            if (pixelClock != 0)
            {
                var timing = ParseDetailedTiming(descriptor, "基础 DTD", descriptorIndex == 0 ? "首选" : string.Empty);
                if (timing is not null)
                {
                    result.VideoModes.Add(timing.Mode);
                    result.DetailedTimings.Add(timing);
                    result.PreferredTiming ??= timing;
                }
                continue;
            }

            if (descriptor[2] != 0 || descriptor[3] == 0)
            {
                continue;
            }

            var descriptorTag = descriptor[3];
            switch (descriptorTag)
            {
                case 0xFC:
                    result.DisplayName = ReadDescriptorText(descriptor);
                    break;
                case 0xFF:
                    var serialText = ReadDescriptorText(descriptor);
                    if (!string.IsNullOrWhiteSpace(serialText))
                    {
                        result.SerialNumber = serialText;
                    }
                    break;
                case 0xFD:
                    ParseRangeLimits(descriptor, result);
                    break;
            }
        }
    }

    private static void ParseChromaticity(ReadOnlySpan<byte> block, ParsedEdid result)
    {
        result.RedX = ((block[27] << 2) | ((block[25] >> 6) & 0x03)) / 1024d;
        result.RedY = ((block[28] << 2) | ((block[25] >> 4) & 0x03)) / 1024d;
        result.GreenX = ((block[29] << 2) | ((block[25] >> 2) & 0x03)) / 1024d;
        result.GreenY = ((block[30] << 2) | (block[25] & 0x03)) / 1024d;
        result.BlueX = ((block[31] << 2) | ((block[26] >> 6) & 0x03)) / 1024d;
        result.BlueY = ((block[32] << 2) | ((block[26] >> 4) & 0x03)) / 1024d;
        result.WhiteX = ((block[33] << 2) | ((block[26] >> 2) & 0x03)) / 1024d;
        result.WhiteY = ((block[34] << 2) | (block[26] & 0x03)) / 1024d;
        result.HasChromaticity = result.RedX > 0 && result.RedY > 0 && result.GreenX > 0 && result.GreenY > 0 && result.BlueX > 0 && result.BlueY > 0;
    }

    private static void ParseEstablishedTimings(ReadOnlySpan<byte> block, ParsedEdid result)
    {
        var mappings = new (int ByteIndex, int Bit, int Width, int Height, double Refresh, bool Interlaced)[]
        {
            (35, 7, 720, 400, 70, false), (35, 6, 720, 400, 88, false),
            (35, 5, 640, 480, 60, false), (35, 4, 640, 480, 67, false),
            (35, 3, 640, 480, 72, false), (35, 2, 640, 480, 75, false),
            (35, 1, 800, 600, 56, false), (35, 0, 800, 600, 60, false),
            (36, 7, 800, 600, 72, false), (36, 6, 800, 600, 75, false),
            (36, 5, 832, 624, 75, false), (36, 4, 1024, 768, 87, true),
            (36, 3, 1024, 768, 60, false), (36, 2, 1024, 768, 70, false),
            (36, 1, 1024, 768, 75, false), (36, 0, 1280, 1024, 75, false),
            (37, 7, 1152, 870, 75, false)
        };

        foreach (var mapping in mappings)
        {
            if ((block[mapping.ByteIndex] & (1 << mapping.Bit)) == 0)
            {
                continue;
            }

            result.VideoModes.Add(new VideoModeInfo
            {
                Width = mapping.Width,
                Height = mapping.Height,
                RefreshHz = mapping.Refresh,
                Interlaced = mapping.Interlaced,
                Source = "基础建立时序"
            });
        }
    }

    private static void ParseStandardTimings(ReadOnlySpan<byte> block, ParsedEdid result)
    {
        for (var index = 0; index < 8; index++)
        {
            var first = block[38 + index * 2];
            var second = block[39 + index * 2];
            if ((first == 0x01 && second == 0x01) || first == 0)
            {
                continue;
            }

            var width = (first + 31) * 8;
            var aspect = (second >> 6) & 0x03;
            var height = aspect switch
            {
                0 => (int)Math.Round(width * 10d / 16d),
                1 => (int)Math.Round(width * 3d / 4d),
                2 => (int)Math.Round(width * 4d / 5d),
                _ => (int)Math.Round(width * 9d / 16d)
            };

            result.VideoModes.Add(new VideoModeInfo
            {
                Width = width,
                Height = height,
                RefreshHz = (second & 0x3F) + 60,
                Source = "基础标准时序"
            });
        }
    }

    private static DetailedTimingInfo? ParseDetailedTiming(ReadOnlySpan<byte> descriptor, string source, string mark)
    {
        var clockUnits = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(0, 2));
        if (clockUnits == 0)
        {
            return null;
        }

        var horizontalActive = descriptor[2] | ((descriptor[4] & 0xF0) << 4);
        var horizontalBlanking = descriptor[3] | ((descriptor[4] & 0x0F) << 8);
        var verticalActive = descriptor[5] | ((descriptor[7] & 0xF0) << 4);
        var verticalBlanking = descriptor[6] | ((descriptor[7] & 0x0F) << 8);
        var pixelClockMHz = clockUnits / 100d;
        var horizontalTotal = horizontalActive + horizontalBlanking;
        var verticalTotal = verticalActive + verticalBlanking;
        var refresh = horizontalTotal > 0 && verticalTotal > 0
            ? pixelClockMHz * 1_000_000d / (horizontalTotal * verticalTotal)
            : 0;
        var flags = descriptor[17];
        var interlaced = (flags & 0x80) != 0;
        var horizontalSyncOffset = descriptor[8] | ((descriptor[11] & 0xC0) << 2);
        var horizontalSyncWidth = descriptor[9] | ((descriptor[11] & 0x30) << 4);
        var verticalSyncOffset = ((descriptor[10] >> 4) & 0x0F) | ((descriptor[11] & 0x0C) << 2);
        var verticalSyncWidth = (descriptor[10] & 0x0F) | ((descriptor[11] & 0x03) << 4);
        var sync = (flags & 0x18) == 0x18
            ? $"水平{(((flags & 0x02) != 0) ? "正" : "负")}，垂直{(((flags & 0x04) != 0) ? "正" : "负")}"
            : "见原始 DTD 标志";

        return new DetailedTimingInfo
        {
            HorizontalActive = horizontalActive,
            HorizontalBlanking = horizontalBlanking,
            VerticalActive = verticalActive,
            VerticalBlanking = verticalBlanking,
            SyncPolarity = sync,
            Mode = new VideoModeInfo
            {
                Width = horizontalActive,
                Height = verticalActive,
                RefreshHz = refresh,
                Interlaced = interlaced,
                PixelClockMHz = pixelClockMHz,
                Source = source,
                Mark = mark,
                HorizontalBlanking = horizontalBlanking,
                VerticalBlanking = verticalBlanking,
                HorizontalSyncOffset = horizontalSyncOffset,
                HorizontalSyncWidth = horizontalSyncWidth,
                VerticalSyncOffset = verticalSyncOffset,
                VerticalSyncWidth = verticalSyncWidth,
                SyncPolarity = sync
            }
        };
    }

    private static void ParseRangeLimits(ReadOnlySpan<byte> descriptor, ParsedEdid result)
    {
        result.VerticalFrequencyRange = descriptor[5] > 0 && descriptor[6] >= descriptor[5]
            ? $"{descriptor[5]} 至 {descriptor[6]} Hz"
            : "未声明";
        if (descriptor[9] > 0)
        {
            result.RangeMaximumPixelClockMHz = descriptor[9] * 10d;
        }
    }

    private static void ParseCtaExtension(ReadOnlySpan<byte> block, ParsedEdid result)
    {
        result.CtaRevision = Math.Max(result.CtaRevision, block[1]);
        result.HasCtaExtension = true;
        result.BasicAudio |= (block[3] & 0x40) != 0;
        result.SupportsYcbcr444 |= (block[3] & 0x20) != 0;
        result.SupportsYcbcr422 |= (block[3] & 0x10) != 0;

        var detailedTimingOffset = block[2] == 0 ? 127 : Math.Clamp(block[2], (byte)4, (byte)127);
        var cursor = 4;
        while (cursor < detailedTimingOffset)
        {
            var header = block[cursor++];
            var tag = header >> 5;
            var length = header & 0x1F;
            if (cursor + length > detailedTimingOffset)
            {
                result.MalformedDataBlock = true;
                break;
            }

            var payload = block.Slice(cursor, length);
            switch (tag)
            {
                case 1:
                    ParseAudioDataBlock(payload, result);
                    result.CtaDataBlocks.Add("音频数据块");
                    break;
                case 2:
                    ParseVideoDataBlock(payload, result, false);
                    result.CtaDataBlocks.Add("视频数据块");
                    break;
                case 3:
                    ParseVendorDataBlock(payload, result);
                    break;
                case 4:
                    ParseSpeakerAllocation(payload, result);
                    result.CtaDataBlocks.Add("扬声器分配数据块");
                    break;
                case 7:
                    ParseExtendedDataBlock(payload, result);
                    break;
                default:
                    result.CtaDataBlocks.Add($"CTA 数据块类型 {tag}");
                    break;
            }

            cursor += length;
        }

        for (var offset = detailedTimingOffset; offset + 18 <= 127; offset += 18)
        {
            var timing = ParseDetailedTiming(block.Slice(offset, 18), "CTA DTD", string.Empty);
            if (timing is null)
            {
                continue;
            }
            result.VideoModes.Add(timing.Mode);
            result.DetailedTimings.Add(timing);
        }
    }

    private static void ParseAudioDataBlock(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        for (var offset = 0; offset + 2 < payload.Length; offset += 3)
        {
            var formatCode = (payload[offset] >> 3) & 0x0F;
            var channels = (payload[offset] & 0x07) + 1;
            var sampleRates = DecodeSampleRates(payload[offset + 1]);
            string[] sampleRateItems = sampleRates.Count > 0
                ? sampleRates.Select(rate => $"{rate:g} kHz").ToArray()
                : ["未声明"];
            string[] detailItems = formatCode == 1
                ? DecodeLpcmBitDepthValues(payload[offset + 2]).Select(depth => $"{depth} bit").ToArray()
                : formatCode is >= 2 and <= 8
                    ? [$"最大 {payload[offset + 2] * 8} kbps"]
                    : [$"格式字节 0x{payload[offset + 2]:X2}"];

            result.AudioFormats.Add(new AudioFormatInfo
            {
                Format = AudioFormatName(formatCode),
                Channels = channels,
                SampleRates = string.Join(" · ", sampleRateItems),
                Detail = detailItems.Length > 0 ? string.Join(" · ", detailItems) : "未声明"
            });
            result.MaximumAudioChannels = Math.Max(result.MaximumAudioChannels, channels);
            if (sampleRates.Count > 0)
            {
                result.MaximumAudioSampleRateKHz = Math.Max(result.MaximumAudioSampleRateKHz, sampleRates.Max());
            }
            if (formatCode == 1)
            {
                result.LpcmBitDepths.UnionWith(DecodeLpcmBitDepthValues(payload[offset + 2]));
            }
        }
    }

    private static void ParseVideoDataBlock(ReadOnlySpan<byte> payload, ParsedEdid result, bool ycbcr420Only)
    {
        foreach (var rawVic in payload)
        {
            var vic = rawVic & 0x7F;
            if (vic == 0)
            {
                continue;
            }

            var native = !ycbcr420Only && (rawVic & 0x80) != 0;
            var mode = VideoModeFromVic(vic, ycbcr420Only ? "CTA YCbCr 4:2:0 VIC" : "CTA VIC", native ? "原生" : $"VIC {vic}");
            result.VideoModes.Add(mode);
        }
    }

    private static void ParseVendorDataBlock(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        if (payload.Length < 3)
        {
            return;
        }

        var oui = payload[0] | (payload[1] << 8) | (payload[2] << 16);
        switch (oui)
        {
            case 0x000C03:
                result.InterfaceCapabilities.Add("HDMI VSDB");
                result.CtaDataBlocks.Add("HDMI 厂商数据块");
                result.SupportedBitDepths.Add(8);
                if (payload.Length >= 7 && payload[6] > 0)
                {
                    result.MaximumTmdsClockMHz = Math.Max(result.MaximumTmdsClockMHz, payload[6] * 5);
                }
                if (payload.Length >= 6)
                {
                    if ((payload[5] & 0x10) != 0)
                    {
                        result.SupportedBitDepths.Add(10);
                        result.InterfaceCapabilities.Add("10 bpc 色深");
                    }
                    if ((payload[5] & 0x20) != 0)
                    {
                        result.SupportedBitDepths.Add(12);
                        result.InterfaceCapabilities.Add("12 bpc 色深");
                    }
                    if ((payload[5] & 0x40) != 0)
                    {
                        result.SupportedBitDepths.Add(16);
                        result.InterfaceCapabilities.Add("16 bpc 色深");
                    }
                }
                break;
            case 0xC45DD8:
                result.InterfaceCapabilities.Add("HDMI Forum VSDB");
                result.CtaDataBlocks.Add("HDMI Forum 厂商数据块");
                result.SupportedBitDepths.Add(8);
                if (payload.Length >= 5 && payload[4] > 0)
                {
                    result.MaximumTmdsClockMHz = Math.Max(result.MaximumTmdsClockMHz, payload[4] * 5);
                }
                ParseHdmiForumCapabilities(payload.Slice(3), result);
                break;
            case 0x00001A:
                result.InterfaceCapabilities.Add("AMD FreeSync VSDB");
                result.CtaDataBlocks.Add("AMD FreeSync 厂商数据块");
                ParseAmdFreeSyncVrr(payload.Slice(3), result);
                break;
            default:
                result.InterfaceCapabilities.Add($"厂商数据块 OUI 0x{oui:X6}");
                result.CtaDataBlocks.Add($"厂商数据块 0x{oui:X6}");
                break;
        }
    }

    private static void ParseHdmiForumCapabilities(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        // payload starts at the HDMI Forum VSDB version byte. CTA-861 defines
        // Max_FRL_Rate and YCbCr 4:2:0 deep color in byte 3, ALLM in byte 4,
        // and VRRmin/VRRmax in bytes 5 and 6.
        if (payload.Length >= 2 && payload[1] > 0)
        {
            result.MaximumTmdsClockMHz = Math.Max(result.MaximumTmdsClockMHz, payload[1] * 5);
        }

        if (payload.Length >= 3 && (payload[2] & 0x80) != 0)
        {
            result.InterfaceCapabilities.Add("SCDC");
        }

        if (payload.Length >= 4)
        {
            var frlCode = payload[3] >> 4;
            var frl = DecodeFrlRate(frlCode);
            if (frl.TotalGbps > 0)
            {
                result.MaximumFrlGbps = frl.TotalGbps;
                result.MaximumFrlLaneRateGbps = frl.LaneRateGbps;
                result.FrlLaneCount = frl.LaneCount;
                result.InterfaceCapabilities.Add($"FRL {frl.TotalGbps} Gbps");
            }

            if ((payload[3] & 0x01) != 0)
            {
                result.Ycbcr420BitDepths.Add(10);
                result.SupportedBitDepths.Add(10);
            }
            if ((payload[3] & 0x02) != 0)
            {
                result.Ycbcr420BitDepths.Add(12);
                result.SupportedBitDepths.Add(12);
            }
            if ((payload[3] & 0x04) != 0)
            {
                result.Ycbcr420BitDepths.Add(16);
                result.SupportedBitDepths.Add(16);
            }
        }

        if (payload.Length >= 5 && (payload[4] & 0x02) != 0)
        {
            result.SupportsAllm = true;
            result.InterfaceCapabilities.Add("ALLM");
        }

        if (payload.Length < 7)
        {
            return;
        }

        var minimum = payload[5] & 0x3F;
        var maximum = ((payload[5] & 0xC0) << 2) | payload[6];
        AddVrrRange(result, minimum, maximum, "HDMI VRR");
    }

    private static (int TotalGbps, int LaneRateGbps, int LaneCount) DecodeFrlRate(int code) => code switch
    {
        1 => (9, 3, 3),
        2 => (18, 6, 3),
        3 => (24, 6, 4),
        4 => (32, 8, 4),
        5 => (40, 10, 4),
        6 => (48, 12, 4),
        _ => (0, 0, 0)
    };

    private static void ParseAmdFreeSyncVrr(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        // AMD VSDB starts with version, feature flags, minimum rate and the legacy maximum rate.
        if (payload.Length < 5)
        {
            return;
        }

        var version = payload[0];
        var minimum = payload[2];
        var maximum = version >= 3 && payload.Length >= 12
            ? ((payload[11] & 0x03) << 8) | payload[10]
            : payload[3];
        AddVrrRange(result, minimum, maximum, "AMD FreeSync");
    }

    private static void AddVrrRange(ParsedEdid result, int minimum, int maximum, string technology)
    {
        if (minimum <= 0 || maximum < minimum)
        {
            return;
        }

        result.VrrMinimumHz = result.VrrMinimumHz is null
            ? minimum
            : Math.Min(result.VrrMinimumHz.Value, minimum);
        result.VrrMaximumHz = result.VrrMaximumHz is null
            ? maximum
            : Math.Max(result.VrrMaximumHz.Value, maximum);
        result.VrrTechnologies.Add(technology);
    }

    private static void ParseSpeakerAllocation(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        if (payload.Length == 0)
        {
            return;
        }

        var names = new[]
        {
            "前置左右 FL / FR", "低频 LFE1", "前置中置 FC", "后置左右 BL / BR",
            "后置中置 BC", "前置左右中置 FLC / FRC", "后置左右中置 RLC / RRC", "前置宽声道 FLW / FRW"
        };
        for (var bit = 0; bit < names.Length; bit++)
        {
            if ((payload[0] & (1 << bit)) != 0)
            {
                result.SpeakerLayouts.Add(names[bit]);
            }
        }

        for (var byteIndex = 1; byteIndex < Math.Min(payload.Length, 3); byteIndex++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                if ((payload[byteIndex] & (1 << bit)) != 0)
                {
                    result.SpeakerLayouts.Add($"CTA 扩展扬声器位 {byteIndex * 8 + bit}");
                }
            }
        }
    }

    private static void ParseExtendedDataBlock(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        if (payload.Length == 0)
        {
            return;
        }

        var extendedTag = payload[0];
        switch (extendedTag)
        {
            case 0x05:
                ParseColorimetry(payload, result);
                result.CtaDataBlocks.Add("色度学数据块");
                break;
            case 0x06:
                ParseHdrStaticMetadata(payload, result);
                result.CtaDataBlocks.Add("HDR 静态元数据块");
                break;
            case 0x0E:
                result.SupportsYcbcr420 = true;
                ParseVideoDataBlock(payload.Slice(1), result, true);
                result.CtaDataBlocks.Add("YCbCr 4:2:0 视频数据块");
                break;
            case 0x0F:
                result.SupportsYcbcr420 = true;
                result.CtaDataBlocks.Add("YCbCr 4:2:0 能力映射");
                break;
            case 0x79:
                result.InterfaceCapabilities.Add("HDMI Forum SCDB");
                result.CtaDataBlocks.Add("HDMI Forum Sink Capability 数据块");
                result.SupportedBitDepths.Add(8);
                if (payload.Length > 3)
                {
                    ParseHdmiForumCapabilities(payload.Slice(3), result);
                }
                break;
            default:
                result.CtaDataBlocks.Add($"CTA 扩展数据块 0x{extendedTag:X2}");
                break;
        }
    }

    private static void ParseColorimetry(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        if (payload.Length < 2)
        {
            return;
        }

        var first = payload[1];
        AddFlag(first, 0, "xvYCC 601", result.Colorimetry);
        AddFlag(first, 1, "xvYCC 709", result.Colorimetry);
        AddFlag(first, 2, "sYCC 601", result.Colorimetry);
        AddFlag(first, 3, "Adobe YCC 601", result.Colorimetry);
        AddFlag(first, 4, "Adobe RGB", result.Colorimetry);
        AddFlag(first, 5, "BT.2020 cYCC", result.Colorimetry);
        AddFlag(first, 6, "BT.2020 YCC", result.Colorimetry);
        AddFlag(first, 7, "BT.2020 RGB", result.Colorimetry);
        if (payload.Length >= 3)
        {
            AddFlag(payload[2], 7, "DCI P3", result.Colorimetry);
        }
    }

    private static void ParseHdrStaticMetadata(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        if (payload.Length < 2)
        {
            return;
        }

        var eotf = payload[1];
        AddFlag(eotf, 0, "传统 SDR", result.HdrEotfs);
        AddFlag(eotf, 1, "传统 HDR", result.HdrEotfs);
        AddFlag(eotf, 2, "PQ / ST 2084", result.HdrEotfs);
        AddFlag(eotf, 3, "HLG", result.HdrEotfs);
        if (payload.Length >= 3 && (payload[2] & 0x01) != 0)
        {
            result.HdrMetadataTypes.Add("静态元数据 Type 1");
        }
        if (payload.Length >= 4 && payload[3] > 0)
        {
            result.MaximumLuminanceNits = DecodeHdrLuminance(payload[3]);
        }
        if (payload.Length >= 5 && payload[4] > 0)
        {
            result.MaximumFrameAverageLuminanceNits = DecodeHdrLuminance(payload[4]);
        }
        if (payload.Length >= 6 && payload[5] > 0 && result.MaximumLuminanceNits is > 0)
        {
            var normalized = payload[5] / 255d;
            result.MinimumLuminanceNits = result.MaximumLuminanceNits.Value * normalized * normalized / 100d;
        }
    }

    private static void ParseDisplayIdExtension(ReadOnlySpan<byte> block, ParsedEdid result)
    {
        result.HasDisplayIdExtension = true;
        if (block.Length >= 5)
        {
            result.DisplayIdVersion = $"DisplayID {block[1] >> 4}.{block[1] & 0x0F}";
        }

        var dataEnd = Math.Min(5 + block[2], 127);
        var cursor = 5;
        while (cursor + 3 <= dataEnd)
        {
            var tag = block[cursor];
            var revision = block[cursor + 1];
            var length = block[cursor + 2];
            cursor += 3;
            if (cursor + length > dataEnd)
            {
                result.MalformedDataBlock = true;
                break;
            }

            var payload = block.Slice(cursor, length);
            result.DisplayIdDataBlocks.Add($"DisplayID 数据块 0x{tag:X2}，修订版 {revision}");
            if (tag == 0x03)
            {
                ParseDisplayIdTypeOneTimings(payload, result);
            }
            cursor += length;
        }
    }

    private static void ParseDisplayIdTypeOneTimings(ReadOnlySpan<byte> payload, ParsedEdid result)
    {
        for (var offset = 0; offset + 20 <= payload.Length; offset += 20)
        {
            var descriptor = payload.Slice(offset, 20);
            var encodedClock = descriptor[0] | (descriptor[1] << 8) | (descriptor[2] << 16);
            var pixelClockMHz = (encodedClock + 1) / 100d;
            var horizontalActive = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(4, 2)) + 1;
            var horizontalBlanking = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(6, 2)) + 1;
            var encodedHorizontalSyncOffset = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(8, 2));
            var horizontalSyncOffset = (encodedHorizontalSyncOffset & 0x7FFF) + 1;
            var horizontalSyncWidth = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(10, 2)) + 1;
            var verticalActive = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(12, 2)) + 1;
            var verticalBlanking = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(14, 2)) + 1;
            var encodedVerticalSyncOffset = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(16, 2));
            var verticalSyncOffset = (encodedVerticalSyncOffset & 0x7FFF) + 1;
            var verticalSyncWidth = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(18, 2)) + 1;
            var horizontalTotal = horizontalActive + horizontalBlanking;
            var verticalTotal = verticalActive + verticalBlanking;
            var refresh = horizontalTotal > 0 && verticalTotal > 0
                ? pixelClockMHz * 1_000_000d / (horizontalTotal * verticalTotal)
                : 0;
            var preferred = (descriptor[3] & 0x80) != 0;
            var sync = $"水平{(((encodedHorizontalSyncOffset & 0x8000) != 0) ? "正" : "负")}，垂直{(((encodedVerticalSyncOffset & 0x8000) != 0) ? "正" : "负")}";

            var timing = new DetailedTimingInfo
            {
                HorizontalActive = horizontalActive,
                HorizontalBlanking = horizontalBlanking,
                VerticalActive = verticalActive,
                VerticalBlanking = verticalBlanking,
                SyncPolarity = sync,
                Mode = new VideoModeInfo
                {
                    Width = horizontalActive,
                    Height = verticalActive,
                    RefreshHz = refresh,
                    Interlaced = false,
                    PixelClockMHz = pixelClockMHz,
                    Source = "DisplayID Type I",
                    HorizontalBlanking = horizontalBlanking,
                    VerticalBlanking = verticalBlanking,
                    HorizontalSyncOffset = horizontalSyncOffset,
                    HorizontalSyncWidth = horizontalSyncWidth,
                    VerticalSyncOffset = verticalSyncOffset,
                    VerticalSyncWidth = verticalSyncWidth,
                    SyncPolarity = sync,
                    Mark = preferred
                        ? $"首选，HSync {horizontalSyncOffset}/{horizontalSyncWidth}，VSync {verticalSyncOffset}/{verticalSyncWidth}"
                        : $"HSync {horizontalSyncOffset}/{horizontalSyncWidth}，VSync {verticalSyncOffset}/{verticalSyncWidth}"
                }
            };
            result.DetailedTimings.Add(timing);
            result.VideoModes.Add(timing.Mode);
        }
    }

    private static void FinalizeResult(ParsedEdid result)
    {
        if (string.IsNullOrWhiteSpace(result.DisplayName))
        {
            result.DisplayName = $"{result.ManufacturerCode} {result.ProductCode}";
        }

        if (result.SrgbDefault)
        {
            result.Colorimetry.Add("sRGB 默认色空间");
        }

        result.ColorFormats.Add("RGB");
        if (result.SupportsYcbcr444) result.ColorFormats.Add("YCbCr 4:4:4");
        if (result.SupportsYcbcr422) result.ColorFormats.Add("YCbCr 4:2:2");
        if (result.SupportsYcbcr420)
        {
            result.ColorFormats.Add("YCbCr 4:2:0");
            result.Ycbcr420BitDepths.Add(8);
            result.SupportedBitDepths.Add(8);
        }

        result.VideoModes = result.VideoModes
            .Where(mode => mode.Width > 0 && mode.Height > 0)
            .GroupBy(mode => $"{mode.Width}:{mode.Height}:{Math.Round(mode.RefreshHz, 1)}:{mode.Interlaced}")
            .Select(group => group.OrderByDescending(mode => mode.Mark == "首选").ThenByDescending(mode => mode.PixelClockMHz ?? 0).First())
            .OrderByDescending(mode => mode.Mark == "首选")
            .ThenByDescending(mode => mode.Width * mode.Height)
            .ThenByDescending(mode => mode.RefreshHz)
            .ToList();

        var maximumClock = result.DetailedTimings.Select(timing => timing.Mode.PixelClockMHz ?? 0).DefaultIfEmpty(0).Max();
        result.MaximumDeclaredPixelClockMHz = Math.Max(maximumClock, result.RangeMaximumPixelClockMHz);
        result.PreferredTiming ??= result.DetailedTimings.FirstOrDefault();

        if (result.HasChromaticity)
        {
            var device = new[]
            {
                new ChromaticityPoint(result.RedX, result.RedY),
                new ChromaticityPoint(result.GreenX, result.GreenY),
                new ChromaticityPoint(result.BlueX, result.BlueY)
            };
            result.SrgbCoverage = Coverage(device, ReferenceGamuts.Srgb);
            result.P3Coverage = Coverage(device, ReferenceGamuts.DisplayP3);
            result.Bt2020Coverage = Coverage(device, ReferenceGamuts.Bt2020);
            result.SrgbVolume = Volume(device, ReferenceGamuts.Srgb);
            result.P3Volume = Volume(device, ReferenceGamuts.DisplayP3);
            result.Bt2020Volume = Volume(device, ReferenceGamuts.Bt2020);
        }
    }

    private static double Coverage(IReadOnlyList<ChromaticityPoint> device, IReadOnlyList<ChromaticityPoint> reference)
    {
        var clipped = device.ToList();
        for (var edge = 0; edge < reference.Count && clipped.Count > 0; edge++)
        {
            var start = reference[edge];
            var end = reference[(edge + 1) % reference.Count];
            clipped = ClipPolygon(clipped, start, end);
        }

        var referenceArea = PolygonArea(reference);
        return referenceArea <= 0 ? 0 : Math.Clamp(PolygonArea(clipped) / referenceArea * 100d, 0, 100);
    }

    private static double Volume(IReadOnlyList<ChromaticityPoint> device, IReadOnlyList<ChromaticityPoint> reference)
    {
        var referenceArea = PolygonArea(reference);
        return referenceArea <= 0 ? 0 : PolygonArea(device) / referenceArea * 100d;
    }

    private static List<ChromaticityPoint> ClipPolygon(IReadOnlyList<ChromaticityPoint> input, ChromaticityPoint edgeStart, ChromaticityPoint edgeEnd)
    {
        var output = new List<ChromaticityPoint>();
        if (input.Count == 0)
        {
            return output;
        }

        var previous = input[^1];
        var previousInside = IsInside(previous, edgeStart, edgeEnd);
        foreach (var current in input)
        {
            var currentInside = IsInside(current, edgeStart, edgeEnd);
            if (currentInside != previousInside)
            {
                output.Add(LineIntersection(previous, current, edgeStart, edgeEnd));
            }
            if (currentInside)
            {
                output.Add(current);
            }
            previous = current;
            previousInside = currentInside;
        }
        return output;
    }

    private static bool IsInside(ChromaticityPoint point, ChromaticityPoint edgeStart, ChromaticityPoint edgeEnd)
        => Cross(edgeEnd.X - edgeStart.X, edgeEnd.Y - edgeStart.Y, point.X - edgeStart.X, point.Y - edgeStart.Y) >= -1e-10;

    private static ChromaticityPoint LineIntersection(ChromaticityPoint a, ChromaticityPoint b, ChromaticityPoint c, ChromaticityPoint d)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var cdX = d.X - c.X;
        var cdY = d.Y - c.Y;
        var denominator = Cross(abX, abY, cdX, cdY);
        if (Math.Abs(denominator) < 1e-12)
        {
            return b;
        }
        var acX = c.X - a.X;
        var acY = c.Y - a.Y;
        var t = Cross(acX, acY, cdX, cdY) / denominator;
        return new ChromaticityPoint(a.X + t * abX, a.Y + t * abY);
    }

    private static double PolygonArea(IReadOnlyList<ChromaticityPoint> polygon)
    {
        if (polygon.Count < 3)
        {
            return 0;
        }
        double sum = 0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Count];
            sum += current.X * next.Y - next.X * current.Y;
        }
        return Math.Abs(sum) / 2d;
    }

    private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;

    private static VideoModeInfo VideoModeFromVic(int vic, string source, string mark)
    {
        var info = vic switch
        {
            1 => (640, 480, 59.94, false),
            2 or 3 => (720, 480, 59.94, false),
            4 => (1280, 720, 60d, false),
            5 => (1920, 1080, 60d, true),
            16 => (1920, 1080, 60d, false),
            17 or 18 => (720, 576, 50d, false),
            19 => (1280, 720, 50d, false),
            20 => (1920, 1080, 50d, true),
            31 => (1920, 1080, 50d, false),
            32 => (1920, 1080, 24d, false),
            33 => (1920, 1080, 25d, false),
            34 => (1920, 1080, 30d, false),
            40 => (1920, 1080, 100d, true),
            41 => (1280, 720, 100d, false),
            46 => (1920, 1080, 120d, true),
            47 => (1280, 720, 120d, false),
            60 => (1280, 720, 24d, false),
            61 => (1280, 720, 25d, false),
            62 => (1280, 720, 30d, false),
            63 => (1920, 1080, 120d, false),
            64 => (1920, 1080, 100d, false),
            93 => (3840, 2160, 24d, false),
            94 => (3840, 2160, 25d, false),
            95 => (3840, 2160, 30d, false),
            96 => (3840, 2160, 50d, false),
            97 => (3840, 2160, 60d, false),
            98 => (4096, 2160, 24d, false),
            99 => (4096, 2160, 25d, false),
            100 => (4096, 2160, 30d, false),
            101 => (4096, 2160, 50d, false),
            102 => (4096, 2160, 60d, false),
            117 => (3840, 2160, 100d, false),
            118 => (3840, 2160, 120d, false),
            119 => (4096, 2160, 100d, false),
            120 => (4096, 2160, 120d, false),
            _ => (0, 0, 0d, false)
        };
        return new VideoModeInfo
        {
            Width = info.Item1,
            Height = info.Item2,
            RefreshHz = info.Item3,
            Interlaced = info.Item4,
            Source = source,
            Mark = mark
        };
    }

    private static string BuildInvalidStatus(ParsedEdid result)
    {
        var issues = new List<string>();
        if (!result.HeaderValid) issues.Add("头标识错误");
        if (result.IsTruncated) issues.Add("扩展块缺失");
        if (result.Blocks.Any(block => !block.ChecksumValid)) issues.Add("存在校验失败的数据块");
        if (result.MalformedDataBlock) issues.Add("CTA 数据块长度异常");
        return issues.Count == 0 ? "EDID 数据异常" : string.Join("；", issues);
    }

    private static string ParseInputDefinition(byte value, bool digital, byte major, byte minor)
    {
        if (!digital)
        {
            return "模拟视频输入";
        }
        if (major < 1 || minor < 4)
        {
            return "数字视频输入";
        }
        return (value & 0x0F) switch
        {
            1 => "DVI",
            2 => "HDMI Type A",
            3 => "HDMI Type B",
            4 => "MDDI",
            5 => "DisplayPort",
            _ => "数字接口，类型未定义"
        };
    }

    private static string ParseDeclaredBitDepth(byte value, bool digital)
    {
        if (!digital)
        {
            return "不适用";
        }
        return ((value >> 4) & 0x07) switch
        {
            1 => "6 bpc",
            2 => "8 bpc",
            3 => "10 bpc",
            4 => "12 bpc",
            5 => "14 bpc",
            6 => "16 bpc",
            _ => "未声明"
        };
    }

    private static int ParseDeclaredBitDepthValue(byte value, bool digital)
    {
        if (!digital)
        {
            return 0;
        }

        return ((value >> 4) & 0x07) switch
        {
            1 => 6,
            2 => 8,
            3 => 10,
            4 => 12,
            5 => 14,
            6 => 16,
            _ => 0
        };
    }

    private static string ReadDescriptorText(ReadOnlySpan<byte> descriptor)
    {
        return System.Text.Encoding.ASCII.GetString(descriptor.Slice(5, 13))
            .Replace("\0", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();
    }

    private static bool ChecksumValid(ReadOnlySpan<byte> block)
    {
        var sum = 0;
        foreach (var value in block)
        {
            sum = (sum + value) & 0xFF;
        }
        return sum == 0;
    }

    private static char DecodeManufacturerCharacter(int value) => value is >= 1 and <= 26 ? (char)('A' + value - 1) : '?';

    private static string ExtensionName(byte tag) => tag switch
    {
        0x02 => "CTA 861 扩展",
        0x10 => "VTB 扩展",
        0x40 => "显示信息扩展",
        0x50 => "本地化字符串扩展",
        0x60 => "数字分组像素视频链路扩展",
        0x70 => "DisplayID 扩展",
        0xF0 => "扩展块映射",
        _ => $"未知扩展 0x{tag:X2}"
    };

    private static List<double> DecodeSampleRates(byte value)
    {
        var rates = new[] { 32d, 44.1, 48d, 88.2, 96d, 176.4, 192d };
        return rates.Where((_, bit) => (value & (1 << bit)) != 0).ToList();
    }

    private static IReadOnlyList<int> DecodeLpcmBitDepthValues(byte value)
    {
        var depths = new[] { 16, 20, 24 };
        return depths.Where((_, bit) => (value & (1 << bit)) != 0).ToArray();
    }

    private static string AudioFormatName(int formatCode) => formatCode switch
    {
        1 => "LPCM", 2 => "AC 3", 3 => "MPEG 1", 4 => "MP3", 5 => "MPEG 2",
        6 => "AAC LC", 7 => "DTS", 8 => "ATRAC", 9 => "One Bit Audio", 10 => "E AC 3",
        11 => "DTS HD", 12 => "MLP", 13 => "DST", 14 => "WMA Pro", 15 => "扩展音频格式",
        _ => $"音频格式 {formatCode}"
    };

    private static void AddFlag(byte value, int bit, string label, ISet<string> target)
    {
        if ((value & (1 << bit)) != 0)
        {
            target.Add(label);
        }
    }

    private static double DecodeHdrLuminance(byte code) => 50d * Math.Pow(2d, code / 32d);

    private readonly record struct ChromaticityPoint(double X, double Y);

    private static class ReferenceGamuts
    {
        public static readonly ChromaticityPoint[] Srgb = [new(0.640, 0.330), new(0.300, 0.600), new(0.150, 0.060)];
        public static readonly ChromaticityPoint[] DisplayP3 = [new(0.680, 0.320), new(0.265, 0.690), new(0.150, 0.060)];
        public static readonly ChromaticityPoint[] Bt2020 = [new(0.708, 0.292), new(0.170, 0.797), new(0.131, 0.046)];
    }
}

internal sealed class ParsedEdid
{
    public byte[] RawBytes { get; set; } = [];
    public bool HeaderValid { get; set; }
    public bool IsTruncated { get; set; }
    public bool IsValid { get; set; }
    public bool MalformedDataBlock { get; set; }
    public string StatusText { get; set; } = "未读取";
    public string DisplayName { get; set; } = string.Empty;
    public string ManufacturerCode { get; set; } = "未声明";
    public string ProductCode { get; set; } = "未声明";
    public string SerialNumber { get; set; } = "未声明";
    public string Manufactured { get; set; } = "未声明";
    public string PhysicalSize { get; set; } = "未声明";
    public string Version { get; set; } = "未知";
    public string InputDefinition { get; set; } = "未声明";
    public string DeclaredBitDepth { get; set; } = "未声明";
    public string Gamma { get; set; } = "未声明";
    public string VerticalFrequencyRange { get; set; } = "未声明";
    public bool IsDigital { get; set; }
    public bool SrgbDefault { get; set; }
    public int DeclaredExtensionCount { get; set; }
    public int UnknownExtensionCount { get; set; }
    public bool HasCtaExtension { get; set; }
    public int CtaRevision { get; set; }
    public bool HasDisplayIdExtension { get; set; }
    public string DisplayIdVersion { get; set; } = string.Empty;
    public bool BasicAudio { get; set; }
    public bool SupportsYcbcr444 { get; set; }
    public bool SupportsYcbcr422 { get; set; }
    public bool SupportsYcbcr420 { get; set; }
    public int? VrrMinimumHz { get; set; }
    public int? VrrMaximumHz { get; set; }
    public int MaximumTmdsClockMHz { get; set; }
    public int MaximumFrlGbps { get; set; }
    public int MaximumFrlLaneRateGbps { get; set; }
    public int FrlLaneCount { get; set; }
    public bool SupportsAllm { get; set; }
    public double RangeMaximumPixelClockMHz { get; set; }
    public double MaximumDeclaredPixelClockMHz { get; set; }
    public int MaximumAudioChannels { get; set; }
    public double MaximumAudioSampleRateKHz { get; set; }
    public double RedX { get; set; }
    public double RedY { get; set; }
    public double GreenX { get; set; }
    public double GreenY { get; set; }
    public double BlueX { get; set; }
    public double BlueY { get; set; }
    public double WhiteX { get; set; }
    public double WhiteY { get; set; }
    public bool HasChromaticity { get; set; }
    public double SrgbCoverage { get; set; }
    public double P3Coverage { get; set; }
    public double Bt2020Coverage { get; set; }
    public double SrgbVolume { get; set; }
    public double P3Volume { get; set; }
    public double Bt2020Volume { get; set; }
    public double? MaximumLuminanceNits { get; set; }
    public double? MaximumFrameAverageLuminanceNits { get; set; }
    public double? MinimumLuminanceNits { get; set; }
    public DetailedTimingInfo? PreferredTiming { get; set; }
    public List<DetailedTimingInfo> DetailedTimings { get; } = [];
    public List<EdidBlockInfo> Blocks { get; } = [];
    public HashSet<string> ExtensionNames { get; } = [];
    public HashSet<string> CtaDataBlocks { get; } = [];
    public HashSet<string> DisplayIdDataBlocks { get; } = [];
    public HashSet<string> ColorFormats { get; } = [];
    public HashSet<string> Colorimetry { get; } = [];
    public HashSet<string> HdrEotfs { get; } = [];
    public HashSet<string> HdrMetadataTypes { get; } = [];
    public HashSet<string> InterfaceCapabilities { get; } = [];
    public HashSet<int> SupportedBitDepths { get; } = [];
    public HashSet<int> Ycbcr420BitDepths { get; } = [];
    public HashSet<string> VrrTechnologies { get; } = [];
    public List<AudioFormatInfo> AudioFormats { get; } = [];
    public HashSet<int> LpcmBitDepths { get; } = [];
    public List<string> SpeakerLayouts { get; } = [];
    public List<VideoModeInfo> VideoModes { get; set; } = [];
}

internal sealed record DetailedTimingInfo
{
    public int HorizontalActive { get; init; }
    public int HorizontalBlanking { get; init; }
    public int VerticalActive { get; init; }
    public int VerticalBlanking { get; init; }
    public string SyncPolarity { get; init; } = "未声明";
    public VideoModeInfo Mode { get; init; } = new();
}
