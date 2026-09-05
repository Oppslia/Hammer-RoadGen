using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using RoadGen.Core.Vtf;

namespace RoadGen.UI;

/// <summary>Hammer-style material browser for RoadGen. Shows the materials the mounted game
/// content can serve (every "materials/**/*.vmt" — a lone .vtf is never listed, mirroring how
/// Hammer browses materials) as a scrollable grid of thumbnails with the file name caption
/// under each.
///
/// Faithful subset of Hammer's classic texture browser:
///  • A single FILTER box with Hammer's name-filter semantics: the text is split into tokens
///    on space/comma/semicolon and every token must be a case-insensitive substring of the
///    material's FILE name (CTextureWindow::SetNameFilter + MatchKeywords). Empty = show all.
///  • "Only used textures": restricts the grid to materials referenced by the imported layout
///    (Hammer's map-usage filter).
///  • Double-click a tile (or select + OK) picks it; the returned value is the canonical
///    material name ("folder/name", no extension).
///  • "Open Source" opens the selected material's .vmt on disk in its default app (Hammer's
///    CTextureSystem::OpenSource — only works for loose on-disk .vmts; VPK-only materials
///    have no local file and are skipped).
/// Dropped (per scope): mark/replace, type-filter checkboxes, size selector, keywords box.
/// Layout mirrors the reference: the thumbnail grid fills the window and ALL controls
/// (filter, used toggle, buttons) sit in a bar at the bottom.</summary>
public sealed class MaterialBrowserDialog : Form
{
    private readonly VtfMaterialCache _cache;
    private readonly TilePanel _tiles;
    private readonly TextBox _filter = new TextBox();
    private readonly CheckBox _chkUsed = new CheckBox();
    private readonly Button _btnOpenSource = new Button();
    private readonly Label _lblInfo = new Label();
    private readonly HashSet<string> _used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _all;

    /// <summary>The picked canonical material name ("folder/name"), or "" if nothing was
    /// chosen (OK with no selection keeps the caller's current value).</summary>
    public string SelectedMaterial { get; private set; } = "";

    public MaterialBrowserDialog(VtfMaterialCache cache, string initial, IReadOnlyCollection<string> usedMaterials)
    {
        Text = "Browse Materials";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 380);
        Size = new Size(1020, 720);

        if (usedMaterials != null)
        {
            foreach (string material in usedMaterials)
            {
                string normalized = VtfMaterialCache.NormalizeMaterialName(material);
                if (normalized.Length > 0)
                {
                    _used.Add(normalized);
                }
            }
        }

        _all = cache.EnumerateMaterialNames();
        _cache = cache;

        // ---- All controls live in a bar at the BOTTOM (filter, used toggle, Open Source,
        // OK, Cancel) with a thin selection/count read-out just above it; the tile grid
        // fills everything above, matching the reference layout.
        Label lblFilter = new Label
        {
            Text = "Filter:",
            AutoSize = true,
            Margin = new Padding(0, 9, 2, 0)
        };

        _filter.Margin = new Padding(0, 6, 8, 0);
        _filter.Dock = DockStyle.Fill;
        _filter.TextChanged += (s, e) => RebuildVisible();

        _chkUsed.Text = "Only used textures";
        _chkUsed.AutoSize = true;
        _chkUsed.Margin = new Padding(0, 8, 14, 0);
        _chkUsed.CheckedChanged += (s, e) => RebuildVisible();

        _btnOpenSource.Text = "Open Source";
        _btnOpenSource.AutoSize = false;
        _btnOpenSource.Width = 96;
        _btnOpenSource.Height = 26;
        _btnOpenSource.Margin = new Padding(0, 6, 8, 0);
        _btnOpenSource.Click += (s, e) => OpenSource();

        Button ok = new Button { Text = "OK", AutoSize = false, Width = 86, Height = 26, Margin = new Padding(0, 6, 6, 0) };
        ok.DialogResult = DialogResult.OK;
        Button cancel = new Button { Text = "Cancel", AutoSize = false, Width = 86, Height = 26, Margin = new Padding(0, 6, 0, 0) };
        cancel.DialogResult = DialogResult.Cancel;

        TableLayoutPanel bottomBar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            ColumnCount = 6,
            RowCount = 1,
            Padding = new Padding(10, 0, 10, 4),
            Margin = new Padding(0)
        };
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // "Filter:"
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // filter box
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Only used textures
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Open Source
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // OK
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Cancel
        bottomBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        bottomBar.Controls.Add(lblFilter, 0, 0);
        bottomBar.Controls.Add(_filter, 1, 0);
        bottomBar.Controls.Add(_chkUsed, 2, 0);
        bottomBar.Controls.Add(_btnOpenSource, 3, 0);
        bottomBar.Controls.Add(ok, 4, 0);
        bottomBar.Controls.Add(cancel, 5, 0);

        _lblInfo.Dock = DockStyle.Bottom;
        _lblInfo.Height = 20;
        _lblInfo.AutoEllipsis = true;
        _lblInfo.TextAlign = ContentAlignment.MiddleLeft;
        _lblInfo.Padding = new Padding(10, 0, 0, 0);
        _lblInfo.ForeColor = SystemColors.GrayText;
        _lblInfo.Text = "";

        // ---- Tile grid ----
        _tiles = new TilePanel(cache);
        _tiles.Dock = DockStyle.Fill;
        _tiles.SelectionChanged += (s, e) => UpdateInfo();
        _tiles.Picked += name =>
        {
            SelectedMaterial = name;
            DialogResult = DialogResult.OK;
            Close();
        };

        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (s, e) =>
        {
            SelectedMaterial = _tiles.SelectedName;
            DialogResult = DialogResult.OK;
        };

        // Docking fills bottom-up in reverse z-order (last added docks first): the control
        // bar goes at the very bottom, the info read-out above it, tiles fill the rest.
        Controls.Add(_tiles);
        Controls.Add(_lblInfo);
        Controls.Add(bottomBar);

        _tiles.SetItems(_all);
        if (!string.IsNullOrWhiteSpace(initial))
        {
            string norm = VtfMaterialCache.NormalizeMaterialName(initial);
            _tiles.SelectName(norm);
        }

        UpdateInfo();
    }

    /// <summary>Scrolls the tile grid with the mouse wheel even when the filter box has
    /// focus (WM_MOUSEWHEEL goes to the focused control; the grid should still scroll when the
    /// cursor is over it, like Hammer's texture window).</summary>
    protected override void WndProc(ref Message m)
    {
        const int wmMouseWheel = 0x020A;
        if (m.Msg == wmMouseWheel && ReferenceEquals(GetChildAtPoint(PointToClient(Cursor.Position)), _tiles))
        {
            long wParam = m.WParam.ToInt64();
            int delta = unchecked((short)((wParam >> 16) & 0xFFFF));
            _tiles.ScrollBy(delta);
            return;
        }

        base.WndProc(ref m);
    }

    private List<string> CurrentList()
    {
        bool usedOnly = _chkUsed.Checked && _used.Count > 0;
        string[] tokens = SplitTokens(_filter.Text);
        var visible = new List<string>();
        foreach (string name in _all)
        {
            if (usedOnly && !_used.Contains(name))
            {
                continue;
            }

            if (!MatchesName(name, tokens))
            {
                continue;
            }

            visible.Add(name);
        }

        return visible;
    }

    private void RebuildVisible()
    {
        string keep = _tiles.SelectedName;
        _tiles.SetItems(CurrentList());
        _tiles.SelectName(keep);
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        string selected = _tiles.SelectedName;
        _lblInfo.Text = selected.Length > 0
            ? selected
            : _tiles.Count + " material(s)";
    }

    /// <summary>Opens the selected material's source .vmt on disk in its default app,
    /// mirroring Hammer's "Open Source" (CTextureSystem::OpenSource resolves
    /// "materials/&lt;name&gt;.vmt" to a local path and ShellExecutes it). Materials that only
    /// exist inside a VPK archive have no local file — like Hammer, nothing is opened, and the
    /// user is told why.</summary>
    private void OpenSource()
    {
        string name = _tiles.SelectedName;
        if (name.Length == 0)
        {
            return;
        }

        if (!_cache.TryOpenMaterialSource(name))
        {
            MessageBox.Show(
                this,
                name + " has no loose .vmt on disk (it only exists inside a VPK archive), so there is no source file to open.",
                "Browse Materials",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    /// <summary>Splits a filter string into tokens on space/comma/semicolon (uppercased),
    /// exactly like Hammer's texture browser filter.</summary>
    private static string[] SplitTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        string[] parts = text.ToUpperInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (part.Length > 0)
            {
                tokens.Add(part);
            }
        }

        return tokens.ToArray();
    }

    /// <summary>Every token must be a substring of the material's file name (the part after
    /// the last '/'), case-insensitive — Hammer matches the short name, not the folder.</summary>
    private static bool MatchesName(string name, string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return true;
        }

        int slash = name.LastIndexOf('/');
        string file = (slash >= 0 ? name.Substring(slash + 1) : name).ToUpperInvariant();
        foreach (string token in tokens)
        {
            if (file.IndexOf(token, StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The scrollable thumbnail grid. Tiles are a square of decoded material art plus
    /// the file-name caption underneath; the selected tile is highlighted. Only the tiles in
    /// the paint clip region are drawn (and therefore decoded), so scrolling a large library
    /// stays responsive — the same lazy-on-paint approach Hammer uses.</summary>
    private sealed class TilePanel : Panel
    {
        private const int TileSize = 96;
        private const int CaptionHeight = 18;
        private const int Pad = 8;
        private const int PitchY = TileSize + CaptionHeight + 6;
        private readonly VtfMaterialCache _cache;
        private readonly List<string> _items = new List<string>();
        private int _selected = -1;
        private readonly Font _font = new Font("Segoe UI", 8.5f);

        public event EventHandler SelectionChanged;
        public event Action<string> Picked;

        public int Count => _items.Count;
        public string SelectedName => _selected >= 0 && _selected < _items.Count ? _items[_selected] : "";

        public TilePanel(VtfMaterialCache cache)
        {
            _cache = cache;
            BackColor = Color.FromArgb(38, 38, 42);
            AutoScroll = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void SetItems(List<string> items)
        {
            _items.Clear();
            if (items != null)
            {
                _items.AddRange(items);
            }

            _selected = -1;
            Reflow();
        }

        public void SelectName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    _selected = i;
                    ScrollIntoView(i);
                    Invalidate();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }

        private int Columns => Math.Max(1, Math.Max(1, (ClientSize.Width - Pad * 2) / (TileSize + Pad)));

        private void Reflow()
        {
            int rows = _items.Count == 0 ? 0 : (_items.Count + Columns - 1) / Columns;
            int height = Math.Max(ClientSize.Height, rows * PitchY + Pad);
            AutoScrollMinSize = new Size(0, height);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Reflow();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (_items.Count == 0)
            {
                using Font f = new Font("Segoe UI", 10f);
                TextRenderer.DrawText(e.Graphics, "No materials found.", f, Point.Empty, Color.FromArgb(150, 160, 175));
                return;
            }

            int cols = Columns;
            int xOff = AutoScrollPosition.X;
            int yOff = AutoScrollPosition.Y;
            e.Graphics.TranslateTransform(xOff, yOff);
            Rectangle clip = e.ClipRectangle;
            clip.Offset(-xOff, -yOff);

            int rowStart = Math.Max(0, (clip.Top - Pad) / PitchY - 1);
            int rowEnd = Math.Min((_items.Count + cols - 1) / cols, (clip.Bottom - Pad) / PitchY + 1);

            for (int row = rowStart; row < rowEnd; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int index = row * cols + col;
                    if (index >= _items.Count)
                    {
                        break;
                    }

                    DrawTile(e.Graphics, index, col, row);
                }
            }
        }

        private void DrawTile(Graphics g, int index, int col, int row)
        {
            int x = Pad + col * (TileSize + Pad);
            int y = Pad + row * PitchY;
            string name = _items[index];

            // Do NOT dispose this bitmap — GetMaterialBitmap returns the cache's shared
            // bitmap (fallback/water included), which the 3D view also draws from.
            Bitmap texture = _cache.GetMaterialBitmap(name);
            Rectangle image = new Rectangle(x, y, TileSize, TileSize);
            g.FillRectangle(new SolidBrush(Color.FromArgb(20, 20, 24)), image);
            if (texture != null)
            {
                g.DrawImage(texture, image);
            }

            if (index == _selected)
            {
                using Pen sel = new Pen(Color.FromArgb(0, 200, 255), 2f);
                g.DrawRectangle(sel, image);
            }
            else
            {
                using Pen border = new Pen(Color.FromArgb(70, 76, 86), 1f);
                g.DrawRectangle(border, image);
            }

            int slash = name.LastIndexOf('/');
            string caption = slash >= 0 ? name.Substring(slash + 1) : name;
            Rectangle textRect = new Rectangle(x, y + TileSize, TileSize, CaptionHeight);
            TextRenderer.DrawText(g, caption, _font, textRect, Color.FromArgb(220, 226, 235), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = IndexAt(e.Location);
            if (index >= 0)
            {
                _selected = index;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Focus();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                int index = IndexAt(e.Location);
                if (index >= 0 && index < _items.Count)
                {
                    Picked?.Invoke(_items[index]);
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(e.Delta);
        }

        /// <summary>Scrolls the grid by a wheel delta (positive = up, negative = down).</summary>
        public void ScrollBy(int delta)
        {
            int step = delta > 0 ? -PitchY * 3 : PitchY * 3;
            int current = -AutoScrollPosition.Y;
            AutoScrollPosition = new Point(0, current + step);
            Invalidate();
        }

        private int IndexAt(Point p)
        {
            int scrolledX = p.X - AutoScrollPosition.X;
            int scrolledY = p.Y - AutoScrollPosition.Y;
            int cols = Columns;
            int col = (scrolledX - Pad) / (TileSize + Pad);
            int row = (scrolledY - Pad) / PitchY;
            if (col < 0 || col >= cols)
            {
                return -1;
            }

            int index = row * cols + col;
            return index >= 0 && index < _items.Count ? index : -1;
        }

        private void ScrollIntoView(int index)
        {
            int cols = Columns;
            int row = index / cols;
            int targetY = row * PitchY;
            int top = -AutoScrollPosition.Y;
            if (targetY < top)
            {
                AutoScrollPosition = new Point(0, targetY);
            }
            else if (targetY + PitchY > top + ClientSize.Height)
            {
                AutoScrollPosition = new Point(0, targetY - ClientSize.Height + PitchY);
            }
        }
    }
}
