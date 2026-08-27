using System;
using System.Drawing;
using System.Windows.Forms;
using RoadGen.Core;
using RoadGen.UI;

namespace RoadGen;

public sealed class MainForm : Form
{
    private readonly RoadDocument _doc = new RoadDocument();
    private readonly UndoManager _undo;
    private ToolStripButton _btnUndo;
    private ToolStripButton _btnRedo;

    private readonly Viewport3D _v3d = new Viewport3D();
    private readonly Viewport2D _top = new Viewport2D();
    private readonly Viewport2D _front = new Viewport2D();
    private readonly Viewport2D _side = new Viewport2D();

    private SplitContainer _split;

    private readonly ListView _list = new ListView();
    private readonly NumericUpDown _numX = new NumericUpDown();
    private readonly NumericUpDown _numY = new NumericUpDown();
    private readonly NumericUpDown _numZ = new NumericUpDown();
    private readonly NumericUpDown _numWidth = new NumericUpDown();
    private readonly NumericUpDown _numBank = new NumericUpDown();

    private readonly NumericUpDown _numThickness = new NumericUpDown();
    private readonly NumericUpDown _numSegmentLength = new NumericUpDown();
    private readonly NumericUpDown _numTexScale = new NumericUpDown();
    private readonly NumericUpDown _numLightmap = new NumericUpDown();
    private readonly NumericUpDown _numSnap = new NumericUpDown();
    private readonly ComboBox _cboPower = new ComboBox();
    private readonly TextBox _txtMaterial = new TextBox();

    private int _selectedIndex = -1;
    private bool _loading;
    private string _currentTrackPath;
    private bool _dirty;
    private bool _suppressDirty;

    private double _prevX;
    private double _prevY;
    private double _prevZ;
    private double _prevWidth;
    private double _prevBank;

    public MainForm()
    {
        _undo = new UndoManager(_doc);

        Text = "RoadGen - 3D Displacement Road Generator";
        Size = new Size(1360, 860);
        MinimumSize = new Size(980, 640);
        BackColor = Color.FromArgb(30, 30, 34);

        BuildToolStrip();
        BuildLayout();
        WireEvents();

        SeedDefaultRoad();
        _suppressDirty = true;
        _doc.NotifyChanged();
        _suppressDirty = false;
        _dirty = false;

        RefreshList();
        SelectPoint(0);
        FrameAll();
        UpdateUndoButtons();
        UpdateTitle();
    }

    // ---------------------------------------------------------------- layout

    private void BuildToolStrip()
    {
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        strip.Items.Add(ToolButton("Open Track...", (s, e) => OpenTrack()));
        strip.Items.Add(ToolButton("Save Track", (s, e) => SaveTrack()));
        strip.Items.Add(ToolButton("Save Track As...", (s, e) => SaveTrackAs()));
        strip.Items.Add(ToolButton("Import VMF...", (s, e) => ImportVmf()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(ToolButton("New", (s, e) => NewRoad()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(ToolButton("Add Point", (s, e) => AddPoint()));
        strip.Items.Add(ToolButton("Remove Point", (s, e) => RemovePoint()));
        strip.Items.Add(ToolButton("Move Up", (s, e) => MovePoint(-1)));
        strip.Items.Add(ToolButton("Move Down", (s, e) => MovePoint(1)));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(ToolButton("Frame All", (s, e) => FrameAll()));
        strip.Items.Add(new ToolStripSeparator());
        _btnUndo = ToolButton("Undo", (s, e) => DoUndo());
        _btnRedo = ToolButton("Redo", (s, e) => DoRedo());
        strip.Items.Add(_btnUndo);
        strip.Items.Add(_btnRedo);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(ToolButton("Generate VMF...", (s, e) => Generate()));

        Controls.Add(strip);
    }

    private static ToolStripButton ToolButton(string text, EventHandler onClick)
    {
        var btn = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        btn.Click += onClick;
        return btn;
    }

    private void BuildLayout()
    {
        var viewports = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        viewports.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        viewports.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        viewports.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        viewports.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _top.SetPlane(Viewport2D.PlaneKind.Top);
        _front.SetPlane(Viewport2D.PlaneKind.Front);
        _side.SetPlane(Viewport2D.PlaneKind.Side);

        // The viewports must fill their cells, otherwise the TableLayoutPanel
        // leaves them at their default (0x0) size and nothing is visible.
        _v3d.Dock = DockStyle.Fill;
        _top.Dock = DockStyle.Fill;
        _front.Dock = DockStyle.Fill;
        _side.Dock = DockStyle.Fill;

        viewports.Controls.Add(_v3d, 0, 0);
        viewports.Controls.Add(_top, 1, 0);
        viewports.Controls.Add(_front, 0, 1);
        viewports.Controls.Add(_side, 1, 1);

        // Draggable splitter between the viewports and the side panel.
        // Min sizes and SplitterDistance are configured after layout (they
        // validate against each other and the current size, so setting them here
        // would throw before the control has real dimensions).
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(38, 38, 42)
        };
        _split.Panel1.Controls.Add(viewports);
        _split.Panel2.Controls.Add(BuildSidePanel());

        var status = new StatusStrip();
        status.Items.Add(new ToolStripStatusLabel
        {
            Text = "2D: ctrl+click add, drag to move, drag empty space to box-select  •  3D: right-drag orbit, middle-drag pan, click select"
        });

        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(_split);

        // Dock layout is applied in reverse z-order: the Fill panel is added first,
        // the bottom StatusStrip is added last.
        Controls.Add(content);
        Controls.Add(status);
    }

    private Control BuildSidePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(38, 38, 42), Padding = new Padding(8) };

        var pointsGroup = new GroupBox
        {
            Text = "Control Points",
            Dock = DockStyle.Top,
            Height = 360,
            Padding = new Padding(6)
        };

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.GridLines = true;
        _list.Columns.Add("#", 34, HorizontalAlignment.Left);
        _list.Columns.Add("X", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Y", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Z", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Width", 62, HorizontalAlignment.Right);
        _list.Columns.Add("Bank", 62, HorizontalAlignment.Right);
        pointsGroup.Controls.Add(_list);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            ColumnCount = 5,
            RowCount = 2,
            Padding = new Padding(0, 6, 0, 0)
        };
        for (int c = 0; c < 5; c++)
        {
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        AddField(editor, 0, "X", _numX);
        AddField(editor, 1, "Y", _numY);
        AddField(editor, 2, "Z", _numZ);
        AddField(editor, 3, "Width", _numWidth);
        AddField(editor, 4, "Bank", _numBank);
        _numBank.Increment = 8;
        pointsGroup.Controls.Add(editor);

        var settingsGroup = new GroupBox
        {
            Text = "Road Settings",
            Dock = DockStyle.Top,
            Height = 240,
            Padding = new Padding(6)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int r = 0; r < 8; r++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        }

        _cboPower.Items.AddRange(new object[] { 2, 3, 4 });
        _cboPower.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboPower.SelectedIndex = 1;

        _txtMaterial.Text = _doc.Settings.Material;

        AddSettingRow(table, 0, "Power", _cboPower);
        AddSettingRow(table, 1, "Material", _txtMaterial);
        AddSettingRow(table, 2, "Thickness", _numThickness);
        AddSettingRow(table, 3, "Segment length", _numSegmentLength);
        AddSettingRow(table, 4, "Texture scale", _numTexScale);
        AddSettingRow(table, 5, "Lightmap scale", _numLightmap);
        AddSettingRow(table, 6, "Grid snap (0=off)", _numSnap);

        settingsGroup.Controls.Add(table);

        var generate = new Button
        {
            Text = "Generate VMF...",
            Dock = DockStyle.Bottom,
            Height = 42,
            FlatStyle = FlatStyle.System,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        generate.Click += (s, e) => Generate();

        // Docking is applied in reverse z-order, so add in reverse of desired layout:
        // settings first, points second, bottom button last.
        panel.Controls.Add(settingsGroup);
        panel.Controls.Add(pointsGroup);
        panel.Controls.Add(generate);

        return panel;
    }

    private static void AddField(TableLayoutPanel table, int col, string label, NumericUpDown num)
    {
        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(190, 195, 205),
            Font = new Font("Segoe UI", 8),
            Margin = new Padding(4, 0, 4, 0)
        };

        num.Dock = DockStyle.Fill;
        num.DecimalPlaces = 2;
        num.Minimum = -1000000;
        num.Maximum = 1000000;
        num.Increment = 16;
        num.Margin = new Padding(4, 2, 4, 2);

        table.Controls.Add(lbl, col, 0);
        table.Controls.Add(num, col, 1);
    }

    private static void AddSettingRow(TableLayoutPanel table, int row, string label, Control control)
    {
        var lbl = new Label
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

    // ---------------------------------------------------------------- events

    private void WireEvents()
    {
        _doc.Changed += (s, e) =>
        {
            _v3d.Invalidate();
            _top.Invalidate();
            _front.Invalidate();
            _side.Invalidate();

            if (!_suppressDirty)
            {
                _dirty = true;
                UpdateTitle();
            }
        };

        _v3d.SetDocument(_doc);
        _top.SetDocument(_doc);
        _front.SetDocument(_doc);
        _side.SetDocument(_doc);

        _v3d.PointSelected = OnViewPointSelected;
        _top.PointSelected = OnViewPointSelected;
        _front.PointSelected = OnViewPointSelected;
        _side.PointSelected = OnViewPointSelected;

        _top.BoxSelected = OnViewBoxSelected;
        _front.BoxSelected = OnViewBoxSelected;
        _side.BoxSelected = OnViewBoxSelected;

        _top.PointsEdited = OnViewPointsEdited;
        _front.PointsEdited = OnViewPointsEdited;
        _side.PointsEdited = OnViewPointsEdited;

        _top.PointAdded = OnViewPointAdded;
        _front.PointAdded = OnViewPointAdded;
        _side.PointAdded = OnViewPointAdded;

        _top.EditBegin = OnViewEditBegin;
        _top.EditEnd = OnViewEditEnd;
        _top.PointAddBegin = OnViewPointAddBegin;
        _front.EditBegin = OnViewEditBegin;
        _front.EditEnd = OnViewEditEnd;
        _front.PointAddBegin = OnViewPointAddBegin;
        _side.EditBegin = OnViewEditBegin;
        _side.EditEnd = OnViewEditEnd;
        _side.PointAddBegin = OnViewPointAddBegin;

        _v3d.GetSelectedIndex = () => _selectedIndex;
        _top.GetSelectedIndex = () => _selectedIndex;
        _front.GetSelectedIndex = () => _selectedIndex;
        _side.GetSelectedIndex = () => _selectedIndex;

        _v3d.GetSelectedIndices = () => SelectedIndices();
        _top.GetSelectedIndices = () => SelectedIndices();
        _front.GetSelectedIndices = () => SelectedIndices();
        _side.GetSelectedIndices = () => SelectedIndices();

        _list.SelectedIndexChanged += (s, e) =>
        {
            if (_loading)
            {
                return;
            }

            _selectedIndex = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
            LoadPointIntoEditors();
            InvalidateAll();
        };

        _numX.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numY.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numZ.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numWidth.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numBank.ValueChanged += (s, e) => UpdatePointFromEditors();

        _cboPower.SelectedIndexChanged += (s, e) => ApplySettingsFromControls();
        _txtMaterial.TextChanged += (s, e) => ApplySettingsFromControls();
        _numThickness.ValueChanged += (s, e) => ApplySettingsFromControls();
        _numSegmentLength.ValueChanged += (s, e) => ApplySettingsFromControls();
        _numTexScale.ValueChanged += (s, e) => ApplySettingsFromControls();
        _numLightmap.ValueChanged += (s, e) => ApplySettingsFromControls();
        _numSnap.ValueChanged += (s, e) => ApplySettingsFromControls();

        AttachUndoBatch(_numX);
        AttachUndoBatch(_numY);
        AttachUndoBatch(_numZ);
        AttachUndoBatch(_numWidth);
        AttachUndoBatch(_numBank);
        AttachUndoBatch(_numThickness);
        AttachUndoBatch(_numSegmentLength);
        AttachUndoBatch(_numTexScale);
        AttachUndoBatch(_numLightmap);
        AttachUndoBatch(_numSnap);
        AttachUndoBatch(_cboPower);
        AttachUndoBatch(_txtMaterial);
    }

    // ---------------------------------------------------------------- data

    private void SeedDefaultRoad()
    {
        _doc.Points.Add(new RoadPoint(new Vec3(0, 0, 0), 256, 0));
        _doc.Points.Add(new RoadPoint(new Vec3(512, 256, 32), 256, 12));
        _doc.Points.Add(new RoadPoint(new Vec3(1024, 0, 96), 256, -12));
        _doc.Points.Add(new RoadPoint(new Vec3(1536, 256, 160), 256, 14));
        _doc.Points.Add(new RoadPoint(new Vec3(2048, 0, 224), 256, -14));
        _doc.Points.Add(new RoadPoint(new Vec3(2560, 256, 288), 256, 0));
    }

    private void NewRoad()
    {
        _undo.RecordSingle();
        _doc.Points.Clear();
        _selectedIndex = -1;
        _currentTrackPath = null;
        _suppressDirty = true;
        _doc.NotifyChanged();
        _suppressDirty = false;
        _dirty = false;
        RefreshList();
        LoadPointIntoEditors();
        UpdateUndoButtons();
        UpdateTitle();
    }

    private void OpenTrack()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "RoadGen Track (*.trk)|*.trk",
            Title = "Open track"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            RoadDocument loaded = TrackFile.Load(dlg.FileName);
            _undo.RecordSingle();
            ApplyDocument(loaded);
            _currentTrackPath = dlg.FileName;
            _dirty = false;
            AfterDocumentLoaded();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open track:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool SaveTrack()
    {
        if (string.IsNullOrEmpty(_currentTrackPath))
        {
            return SaveTrackAs();
        }

        try
        {
            TrackFile.Save(_doc, _currentTrackPath);
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save track:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool SaveTrackAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "RoadGen Track (*.trk)|*.trk",
            FileName = "track.trk",
            Title = "Save track"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        try
        {
            TrackFile.Save(_doc, dlg.FileName);
            _currentTrackPath = dlg.FileName;
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save track:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ImportVmf()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Hammer Files (*.vmf)|*.vmf",
            Title = "Import VMF road"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            string text = System.IO.File.ReadAllText(dlg.FileName);
            var points = VmfImporter.ImportRoad(text);
            _undo.RecordSingle();
            _doc.Points.Clear();
            foreach (RoadPoint p in points)
            {
                _doc.Points.Add(p);
            }

            _currentTrackPath = null;
            _dirty = true;
            _selectedIndex = -1;
            AfterDocumentLoaded();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyDocument(RoadDocument loaded)
    {
        _doc.Points.Clear();
        foreach (RoadPoint p in loaded.Points)
        {
            _doc.Points.Add(p);
        }

        var s = _doc.Settings;
        s.Power = loaded.Settings.Power;
        s.Material = loaded.Settings.Material;
        s.Thickness = loaded.Settings.Thickness;
        s.SegmentLength = loaded.Settings.SegmentLength;
        s.TextureScale = loaded.Settings.TextureScale;
        s.LightmapScale = loaded.Settings.LightmapScale;
        s.Snap = loaded.Settings.Snap;
    }

    private void AfterDocumentLoaded()
    {
        LoadSettingsIntoControls();
        RefreshList();
        _selectedIndex = -1;
        if (_doc.Points.Count > 0)
        {
            SelectPoint(0);
        }
        else
        {
            LoadPointIntoEditors();
        }

        InvalidateAll();
        FrameAll();
    }

    private void AddPoint()
    {
        _undo.RecordSingle();
        RoadPoint last = _doc.Points.Count > 0 ? _doc.Points[_doc.Points.Count - 1] : new RoadPoint(Vec3.Zero, 256, 0);
        var p = new RoadPoint(last.Position + new Vec3(256, 0, 0), last.Width, last.BankDegrees);
        _doc.Points.Add(p);
        _doc.NotifyChanged();
        RefreshList();
        SelectPoint(_doc.Points.Count - 1);
        UpdateUndoButtons();
    }

    private void RemovePoint()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _doc.Points.Count)
        {
            return;
        }

        _undo.RecordSingle();
        _doc.Points.RemoveAt(_selectedIndex);
        int next = Math.Min(_selectedIndex, _doc.Points.Count - 1);
        _selectedIndex = -1;
        _doc.NotifyChanged();
        RefreshList();
        if (next >= 0)
        {
            SelectPoint(next);
        }
        else
        {
            LoadPointIntoEditors();
        }

        UpdateUndoButtons();
    }

    private void MovePoint(int delta)
    {
        int from = _selectedIndex;
        int to = from + delta;
        if (from < 0 || to < 0 || from >= _doc.Points.Count || to >= _doc.Points.Count)
        {
            return;
        }

        _undo.RecordSingle();
        RoadPoint tmp = _doc.Points[from];
        _doc.Points[from] = _doc.Points[to];
        _doc.Points[to] = tmp;

        _doc.NotifyChanged();
        RefreshList();
        SelectPoint(to);
        UpdateUndoButtons();
    }

    private void SelectPoint(int index)
    {
        if (index < 0 || index >= _doc.Points.Count)
        {
            _selectedIndex = -1;
            _loading = true;
            _list.SelectedIndices.Clear();
            _loading = false;
            LoadPointIntoEditors();
            InvalidateAll();
            return;
        }

        _selectedIndex = index;
        _loading = true;
        _list.SelectedIndices.Clear();
        if (index < _list.Items.Count)
        {
            _list.SelectedIndices.Add(index);
            _list.EnsureVisible(index);
        }

        _loading = false;
        LoadPointIntoEditors();
        InvalidateAll();
    }

    private void OnViewPointsEdited(IReadOnlyList<int> indices)
    {
        foreach (int i in indices)
        {
            if (i >= 0 && i < _doc.Points.Count)
            {
                UpdateListRow(i);
            }
        }

        if (_selectedIndex >= 0 && indices.Contains(_selectedIndex))
        {
            // Keep the X/Y/Z/Width/Bank editors in sync with the drag.
            LoadPointIntoEditors();
        }
    }

    private void OnViewPointAdded(int index)
    {
        RefreshList();
        SelectPoint(index);
        UpdateUndoButtons();
    }

    private void UpdatePointFromEditors()
    {
        if (_loading)
        {
            return;
        }

        List<int> selected = SelectedIndices();
        if (selected.Count == 0)
        {
            CaptureEditorValues();
            return;
        }

        // Position edits are applied as a delta (moves the group together);
        // width/bank are applied as absolute values across the selection.
        double dx = (double)_numX.Value - _prevX;
        double dy = (double)_numY.Value - _prevY;
        double dz = (double)_numZ.Value - _prevZ;
        bool widthChanged = (double)_numWidth.Value != _prevWidth;
        bool bankChanged = (double)_numBank.Value != _prevBank;

        foreach (int i in selected)
        {
            RoadPoint p = _doc.Points[i];
            p.Position = new Vec3(p.Position.X + dx, p.Position.Y + dy, p.Position.Z + dz);
            if (widthChanged)
            {
                p.Width = (double)_numWidth.Value;
            }

            if (bankChanged)
            {
                p.BankDegrees = (double)_numBank.Value;
            }

            UpdateListRow(i);
        }

        CaptureEditorValues();
        _doc.NotifyChanged();
    }

    private List<int> SelectedIndices()
    {
        var result = new List<int>();
        foreach (int i in _list.SelectedIndices)
        {
            result.Add(i);
        }

        return result;
    }

    private void CaptureEditorValues()
    {
        _prevX = (double)_numX.Value;
        _prevY = (double)_numY.Value;
        _prevZ = (double)_numZ.Value;
        _prevWidth = (double)_numWidth.Value;
        _prevBank = (double)_numBank.Value;
    }

    private void OnViewPointSelected(int index, bool additive)
    {
        if (index < 0)
        {
            ClearSelection();
        }
        else if (additive)
        {
            ToggleSelectPoint(index);
        }
        else
        {
            SelectPoint(index);
        }
    }

    private void OnViewBoxSelected(IReadOnlyList<int> indices, bool additive)
    {
        SelectMany(new List<int>(indices), additive);
    }

    private void OnViewEditBegin() => _undo.BeginBatch();

    private void OnViewEditEnd()
    {
        _undo.EndBatch();
        UpdateUndoButtons();
    }

    private void OnViewPointAddBegin() => _undo.RecordSingle();

    private void ClearSelection()
    {
        _selectedIndex = -1;
        _loading = true;
        _list.SelectedIndices.Clear();
        _loading = false;
        LoadPointIntoEditors();
        InvalidateAll();
    }

    private void ToggleSelectPoint(int index)
    {
        if (index < 0 || index >= _doc.Points.Count)
        {
            return;
        }

        _loading = true;
        _list.BeginUpdate();
        if (_list.SelectedIndices.Contains(index))
        {
            _list.SelectedIndices.Remove(index);
        }
        else
        {
            _list.SelectedIndices.Add(index);
        }

        _list.EndUpdate();
        _loading = false;

        _selectedIndex = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        LoadPointIntoEditors();
        InvalidateAll();
    }

    private void SelectMany(List<int> indices, bool additive)
    {
        _loading = true;
        _list.BeginUpdate();
        if (!additive)
        {
            _list.SelectedIndices.Clear();
        }

        foreach (int i in indices)
        {
            if (i >= 0 && i < _list.Items.Count && !_list.SelectedIndices.Contains(i))
            {
                _list.SelectedIndices.Add(i);
            }
        }

        _list.EndUpdate();
        _loading = false;

        _selectedIndex = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        LoadPointIntoEditors();
        InvalidateAll();
    }

    private void DoUndo()
    {
        if (!_undo.CanUndo)
        {
            return;
        }

        _undo.Undo();
        AfterUndoRedo();
    }

    private void DoRedo()
    {
        if (!_undo.CanRedo)
        {
            return;
        }

        _undo.Redo();
        AfterUndoRedo();
    }

    private void AfterUndoRedo()
    {
        // Preserve the selection across undo/redo.
        List<int> selection = SelectedIndices();

        _selectedIndex = -1;
        LoadSettingsIntoControls();
        RefreshList();

        // Re-select the same points. Out-of-range indices are ignored, which is
        // correct when the undo/redo added or removed points.
        SelectMany(selection, additive: false);

        _doc.NotifyChanged();
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        if (_btnUndo != null)
        {
            _btnUndo.Enabled = _undo.CanUndo;
        }

        if (_btnRedo != null)
        {
            _btnRedo.Enabled = _undo.CanRedo;
        }
    }

    private void AttachUndoBatch(Control control)
    {
        control.Enter += (s, e) => _undo.BeginBatch();
        control.Leave += (s, e) =>
        {
            _undo.EndBatch();
            UpdateUndoButtons();
        };
    }

    private void ApplySettingsFromControls()
    {
        if (_loading)
        {
            return;
        }

        var s = _doc.Settings;
        s.Power = (int)_cboPower.SelectedItem;
        s.Material = _txtMaterial.Text;
        s.Thickness = (double)_numThickness.Value;
        s.SegmentLength = (double)_numSegmentLength.Value;
        s.TextureScale = (double)_numTexScale.Value;
        s.LightmapScale = (int)_numLightmap.Value;
        s.Snap = (double)_numSnap.Value;
        _doc.NotifyChanged();
    }

    private void LoadPointIntoEditors()
    {
        _loading = true;
        if (_selectedIndex >= 0 && _selectedIndex < _doc.Points.Count)
        {
            RoadPoint p = _doc.Points[_selectedIndex];
            _numX.Value = (decimal)p.Position.X;
            _numY.Value = (decimal)p.Position.Y;
            _numZ.Value = (decimal)p.Position.Z;
            _numWidth.Value = (decimal)p.Width;
            _numBank.Value = (decimal)p.BankDegrees;
        }

        CaptureEditorValues();
        _loading = false;
    }

    private void LoadSettingsIntoControls()
    {
        _loading = true;
        var s = _doc.Settings;
        _cboPower.SelectedIndex = Math.Max(0, Array.IndexOf(new object[] { 2, 3, 4 }, s.Power));
        _txtMaterial.Text = s.Material;
        _numThickness.Value = (decimal)s.Thickness;
        _numSegmentLength.Value = (decimal)s.SegmentLength;
        _numTexScale.Value = (decimal)s.TextureScale;
        _numLightmap.Value = (decimal)s.LightmapScale;
        _numSnap.Value = (decimal)s.Snap;
        _loading = false;
    }

    private void RefreshList()
    {
        _loading = true;
        int selected = _selectedIndex;
        _list.BeginUpdate();
        _list.Items.Clear();
        for (int i = 0; i < _doc.Points.Count; i++)
        {
            _list.Items.Add(MakeItem(i, _doc.Points[i]));
        }

        if (selected >= 0 && selected < _list.Items.Count)
        {
            _list.SelectedIndices.Add(selected);
        }

        _list.EndUpdate();
        _loading = false;
    }

    private static ListViewItem MakeItem(int index, RoadPoint p)
    {
        var item = new ListViewItem(index.ToString());
        item.SubItems.Add(p.Position.X.ToString("0.##"));
        item.SubItems.Add(p.Position.Y.ToString("0.##"));
        item.SubItems.Add(p.Position.Z.ToString("0.##"));
        item.SubItems.Add(p.Width.ToString("0.##"));
        item.SubItems.Add(p.BankDegrees.ToString("0.##"));
        return item;
    }

    private void UpdateListRow(int index)
    {
        if (index < 0 || index >= _list.Items.Count || index >= _doc.Points.Count)
        {
            return;
        }

        RoadPoint p = _doc.Points[index];
        ListViewItem item = _list.Items[index];
        item.SubItems[1].Text = p.Position.X.ToString("0.##");
        item.SubItems[2].Text = p.Position.Y.ToString("0.##");
        item.SubItems[3].Text = p.Position.Z.ToString("0.##");
        item.SubItems[4].Text = p.Width.ToString("0.##");
        item.SubItems[5].Text = p.BankDegrees.ToString("0.##");
    }

    private void FrameAll()
    {
        _top.FrameAll();
        _front.FrameAll();
        _side.FrameAll();
        _v3d.FrameAll();
    }

    private void InvalidateAll()
    {
        _v3d.Invalidate();
        _top.Invalidate();
        _front.Invalidate();
        _side.Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Z:
                if (ActiveControl is TextBox)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                DoUndo();
                return true;
            case Keys.Control | Keys.Y:
                if (ActiveControl is TextBox)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                DoRedo();
                return true;
            case Keys.Control | Keys.O:
                OpenTrack();
                return true;
            case Keys.Control | Keys.S:
                SaveTrack();
                return true;
            case Keys.Control | Keys.N:
                NewRoad();
                return true;
            case Keys.Control | Keys.A:
                AddPoint();
                return true;
            case Keys.Delete:
                RemovePoint();
                return true;
            case Keys.Control | Keys.F:
                FrameAll();
                return true;
            case Keys.Control | Keys.G:
                Generate();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------- output

    private void Generate()
    {
        if (_doc.Points.Count < 2)
        {
            MessageBox.Show(this, "Add at least two control points first.", "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "Hammer Files (.vmf)|*.vmf",
            FileName = "road.vmf",
            Title = "Generate road VMF"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            string vmf = RoadGenerator.GenerateVmf(_doc);
            System.IO.File.WriteAllText(dlg.FileName, vmf);
            MessageBox.Show(this, $"Wrote {dlg.FileName}", "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Generation failed:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadSettingsIntoControls();
        FrameAll();

        // Defer splitter setup until layout has given it a real size.
        BeginInvoke(new Action(SetupSplitter));
    }

    private void SetupSplitter()
    {
        if (_split == null || IsDisposed || _split.Width <= 0)
        {
            return;
        }

        int width = _split.Width;

        // Set the distance first (validated against the current width), then the
        // min sizes (validated against the distance), then re-clamp the distance.
        int distance = Math.Max(50, width - 360);
        _split.SplitterDistance = distance;
        _split.Panel1MinSize = Math.Min(380, _split.SplitterDistance);
        _split.Panel2MinSize = Math.Min(280, width - _split.SplitterDistance);

        distance = Math.Max(_split.Panel1MinSize, _split.SplitterDistance);
        distance = Math.Min(distance, width - _split.Panel2MinSize);
        _split.SplitterDistance = distance;
    }

    private void UpdateTitle()
    {
        string name = string.IsNullOrEmpty(_currentTrackPath)
            ? "Untitled"
            : System.IO.Path.GetFileName(_currentTrackPath);
        Text = _dirty ? $"RoadGen - {name}*" : $"RoadGen - {name}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (!_dirty)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            "Save changes to the current track before exiting?",
            "RoadGen",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            if (!SaveTrack())
            {
                e.Cancel = true;
            }
        }
        else if (result == DialogResult.Cancel)
        {
            e.Cancel = true;
        }
    }
}
