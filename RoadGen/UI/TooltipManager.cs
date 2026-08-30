using System.Windows.Forms;

namespace RoadGen.UI;

/// <summary>Central helper for attaching tooltips across the app. It owns the single
/// <see cref="ToolTip"/> component so every hint shares the same timing, and it routes
/// <see cref="ToolStripItem"/>s (toolbar) to their <c>ToolTipText</c> property while
/// <see cref="Control"/>s (side panel, buttons) use <see cref="ToolTip.SetToolTip"/>.</summary>
public sealed class TooltipManager
{
    private readonly ToolTip _toolTip = new ToolTip
    {
        AutoPopDelay = 6000,
        InitialDelay = 500,
        ReshowDelay = 100,
        ShowAlways = true
    };

    /// <summary>Attach a hint to a regular control.</summary>
    public void Attach(Control control, string text)
    {
        if (control == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _toolTip.SetToolTip(control, text);
    }

    /// <summary>Attach a hint to a toolbar item (ToolStripButton, ToolStripComboBox, ...).</summary>
    public void Attach(ToolStripItem item, string text)
    {
        if (item == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        item.ToolTipText = text;
    }
}
