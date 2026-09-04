using System;
using System.Drawing;
using System.Windows.Forms;
using RoadGen.Core;

namespace RoadGen;

/// <summary>Pure UI/layout helpers used by <see cref="MainWindow"/>. None of these
/// touch instance state, so they live outside the window class and can be reused or
/// tested independently.</summary>
public static class MainWindowHelpers
{
    public static ToolStripButton ToolButton(string text, EventHandler onClick)
    {
        ToolStripButton btn = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };

        btn.Click += onClick;

        return btn;
    }

    public static void StyleLayerButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.White;
        button.ForeColor = Color.Black;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.FlatAppearance.BorderColor = Color.Gray;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(219, 232, 252);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(190, 214, 248);
    }

    /// <summary>Flat toolstrip/statusbar renderer: paints the strip's solid BackColor (no
    /// gradient "highlight" band behind the labels) and no per-item background, so status
    /// text is never boxed by a gray highlight.</summary>
    public sealed class FlatToolStripRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using SolidBrush background = new SolidBrush(e.ToolStrip.BackColor);
            e.Graphics.FillRectangle(background, e.AffectedBounds);
        }

        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            // Intentionally empty: no hover/selected highlight behind items.
        }
    }

    public static void AddSettingRow(TableLayoutPanel table, int row, string label, Control control)
    {
        Label lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.LightGray
        };

        control.Dock = DockStyle.Fill;

        if (control is NumericUpDown num)
        {
            num.DecimalPlaces = 2;
            num.Minimum = 0;
            num.Maximum = 100000;
            num.Increment = 16;
        }

        table.Controls.Add(lbl, 0, row);
        table.Controls.Add(control, 1, row);
    }

    /// <summary>Adds a label/value row to the edge feature editor, placing the
    /// optional increment cell (Grid checkbox + interval) to the LEFT of the value
    /// so it never gets clipped by the panel's scrollbar.</summary>
    public static void AddFeatureSettingRow(TableLayoutPanel table, int row, string label, Control control, Control incrementCell)
    {
        Label lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.LightGray
        };

        control.Dock = DockStyle.Fill;
        if (control is NumericUpDown num)
        {
            num.DecimalPlaces = 2;
            num.Minimum = 0;
            num.Maximum = 100000;
            num.Increment = 16;
        }

        table.Controls.Add(lbl, 0, row);

        if (incrementCell != null)
        {
            table.Controls.Add(incrementCell, 1, row);
            table.Controls.Add(control, 2, row);
        }
        else
        {
            table.Controls.Add(control, 1, row);
            table.SetColumnSpan(control, 2);
        }
    }

    public static void AddIncrementColumn(TableLayoutPanel table, int col, CheckBox chk, NumericUpDown num)
    {
        chk.Text = "Grid";
        chk.Dock = DockStyle.Fill;
        chk.AutoSize = false;
        chk.TextAlign = ContentAlignment.MiddleCenter;
        chk.ForeColor = Color.LightGray;
        chk.Margin = new Padding(2, 0, 2, 0);

        num.Dock = DockStyle.Fill;
        num.DecimalPlaces = 2;
        num.Minimum = 0.01m;
        num.Maximum = 100000;
        num.Increment = 1;
        num.Margin = new Padding(2, 0, 2, 0);

        table.Controls.Add(chk, col, 0);
        table.Controls.Add(num, col, 1);
    }

    /// <summary>Builds a compact "Grid" checkbox + custom increment field that sits
    /// next to one edge-feature value (Width/Bottom Z/Top Z/Bank).</summary>
    public static Control BuildFeatureIncrementCell(CheckBox chk, NumericUpDown num, bool followGrid, decimal customValue)
    {
        TableLayoutPanel cell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        chk.Text = "Grid";
        chk.Dock = DockStyle.Fill;
        chk.AutoSize = false;
        chk.TextAlign = ContentAlignment.MiddleCenter;
        chk.ForeColor = Color.LightGray;
        chk.Checked = followGrid;
        chk.Margin = new Padding(2, 0, 2, 0);

        num.Dock = DockStyle.Fill;
        num.DecimalPlaces = 2;
        num.Minimum = 0.01m;
        num.Maximum = 100000;
        num.Increment = 1;
        num.Value = customValue;
        num.Visible = !followGrid;
        num.Margin = new Padding(2, 0, 2, 0);

        cell.Controls.Add(chk, 0, 0);
        cell.Controls.Add(num, 1, 0);

        return cell;
    }

    public static void ApplyIncrement(NumericUpDown target, CheckBox chk, NumericUpDown custom, double grid)
    {
        if (chk.Checked)
        {
            target.Increment = (decimal)grid;
            custom.Visible = false;
        }
        else
        {
            target.Increment = custom.Value;
            custom.Visible = true;
        }
    }

    public static int SnapIndex(double snap)
    {
        int[] values = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 };
        int best = 6;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < values.Length; i++)
        {
            double diff = Math.Abs(values[i] - snap);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }

        return best;
    }

    public static int LightmapIndex(int lightmap)
    {
        int[] values = { 1, 2, 4, 8, 16, 32, 64, 128, 256 };
        int best = 4;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < values.Length; i++)
        {
            double diff = Math.Abs(values[i] - lightmap);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }

        return best;
    }

    public static string FeatureSummary(EdgeFeature feature)
    {
        string side = feature.LeftSide ? "Left" : "Right";

        return $"{feature.Kind} {side}";
    }

    public static ListViewItem MakeItem(int index, RoadPoint p)
    {
        ListViewItem item = new ListViewItem(index.ToString());
        item.SubItems.Add(p.Position.X.ToString("0.##"));
        item.SubItems.Add(p.Position.Y.ToString("0.##"));
        item.SubItems.Add(p.Position.Z.ToString("0.##"));
        item.SubItems.Add(p.Width.ToString("0.##"));
        item.SubItems.Add(p.BankDegrees.ToString("0.##"));
        item.SubItems.Add(p.Thickness.ToString("0.##"));

        return item;
    }
}
