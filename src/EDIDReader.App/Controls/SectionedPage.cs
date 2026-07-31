using System.Windows.Controls;
using System.Windows.Media;

namespace EDIDReader.App.Controls;

public class SectionedPage : UserControl
{
    public SectionedPage()
    {
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }
}
