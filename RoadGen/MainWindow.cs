using System;
using System.Drawing;
using System.Windows.Forms;
using RoadGen.Core;
using RoadGen.UI;
using static RoadGen.MainWindowHelpers;

namespace RoadGen;

public sealed partial class MainWindow : Form
{
    private readonly RoadDocument _doc = new RoadDocument();
    private readonly UndoManager _undo;
    private ToolStripButton _btnUndo;
    private ToolStripButton _btnRedo;
    private ToolStripButton _btnOpen;
    private ToolStripButton _btnSave;
    private ToolStripButton _btnSaveAs;
    private ToolStripButton _btnImport;
    private ToolStripButton _btnNew;
    private ToolStripButton _btnAddPoint;
    private ToolStripButton _btnRemovePoint;
    private ToolStripButton _btnMoveUp;
    private ToolStripButton _btnMoveDown;
    private ToolStripButton _btnFrame;
    private ToolStripButton _btnGenerate;
    private ToolStrip _topActionBar;
    private readonly ToolStripComboBox _gridCombo = new ToolStripComboBox();
    private readonly ToolStripButton _btnSnap = new ToolStripButton("Snap on")
    {
        CheckOnClick = true,
        Checked = true
    };
    
    private bool _syncingGrid;

    private readonly Viewport3D _v3d = new Viewport3D();
    private readonly Viewport2D _top = new Viewport2D();
    private readonly Viewport2D _front = new Viewport2D();
    private readonly Viewport2D _side = new Viewport2D();

    private SplitContainer _split;

    private readonly ListView _list = new ListView();
    private readonly ListBox _lstLayers = new ListBox();
    private readonly Button _btnAddLayer = new Button();
    private readonly Button _btnRemoveLayer = new Button();
    private readonly Button _btnRenameLayer = new Button();
    private readonly Button _btnDuplicateLayer = new Button();
    private readonly Button _btnMergeLayer = new Button();
    private readonly Button _btnLayerUp = new Button();
    private readonly Button _btnLayerDown = new Button();
    private readonly CheckBox _chkEnableJoining = new CheckBox();

    private readonly ListBox _lstFeatures = new ListBox();
    private readonly Button _btnAddFeature = new Button();
    private readonly Button _btnRemoveFeature = new Button();
    private readonly ComboBox _cboFeatureKind = new ComboBox();
    private readonly ComboBox _cboFeatureSide = new ComboBox();
    private readonly NumericUpDown _numFeatureOffset = new NumericUpDown();
    private readonly NumericUpDown _numFeatureWidth = new NumericUpDown();
    private readonly NumericUpDown _numFeatureBottomZ = new NumericUpDown();
    private readonly NumericUpDown _numFeatureTopZ = new NumericUpDown();
    private readonly NumericUpDown _numFeatureBank = new NumericUpDown();
    private readonly CheckBox _chkFeatureIncOffset = new CheckBox();
    private readonly CheckBox _chkFeatureIncWidth = new CheckBox();
    private readonly CheckBox _chkFeatureIncBottomZ = new CheckBox();
    private readonly CheckBox _chkFeatureIncTopZ = new CheckBox();
    private readonly CheckBox _chkFeatureIncBank = new CheckBox();
    private readonly NumericUpDown _numFeatureIncOffset = new NumericUpDown();
    private readonly NumericUpDown _numFeatureIncWidth = new NumericUpDown();
    private readonly NumericUpDown _numFeatureIncBottomZ = new NumericUpDown();
    private readonly NumericUpDown _numFeatureIncTopZ = new NumericUpDown();
    private readonly NumericUpDown _numFeatureIncBank = new NumericUpDown();
    private readonly ListView _lstFeaturePoints = new ListView();
    private readonly TextBox _txtFeatureMaterial = new TextBox();
    private readonly CheckBox _chkFeatureBottom = new CheckBox();
    private readonly CheckBox _chkFeatureInner = new CheckBox();
    private readonly CheckBox _chkFeatureOuter = new CheckBox();

    private readonly NumericUpDown _numX = new NumericUpDown();
    private readonly NumericUpDown _numY = new NumericUpDown();
    private readonly NumericUpDown _numZ = new NumericUpDown();
    private readonly NumericUpDown _numWidth = new NumericUpDown();
    private readonly NumericUpDown _numBank = new NumericUpDown();
    private readonly NumericUpDown _trackThickness = new NumericUpDown();

    private readonly NumericUpDown _numTexScale = new NumericUpDown();
    private readonly ComboBox _cboLightmap = new ComboBox();
    private readonly ComboBox _cboSnap = new ComboBox();
    private readonly ComboBox _cboPower = new ComboBox();
    private readonly TextBox _txtMaterial = new TextBox();
    private readonly CheckBox _chkSolidLeft = new CheckBox();
    private readonly CheckBox _chkSolidRight = new CheckBox();
    private readonly CheckBox _chkSolidBottom = new CheckBox();

    // Per-editor increment controls ("Increment/Decrement interval" section).
    private readonly CheckBox _chkIncX = new CheckBox();
    private readonly CheckBox _chkIncY = new CheckBox();
    private readonly CheckBox _chkIncZ = new CheckBox();
    private readonly CheckBox _chkIncWidth = new CheckBox();
    private readonly CheckBox _chkIncBank = new CheckBox();
    private readonly NumericUpDown _numIncX = new NumericUpDown();
    private readonly NumericUpDown _numIncY = new NumericUpDown();
    private readonly NumericUpDown _numIncZ = new NumericUpDown();
    private readonly NumericUpDown _numIncWidth = new NumericUpDown();
    private readonly NumericUpDown _numIncBank = new NumericUpDown();
    private readonly CheckBox _chkIncThickness = new CheckBox();
    private readonly NumericUpDown _numIncThickness = new NumericUpDown();

    // "See disps" preview toggle + live displacement count.
    private readonly CheckBox _chkShowDisps = new CheckBox();
    private readonly CheckBox _chkShowSidewalkDisps = new CheckBox();
    private readonly Label _lblDispCount = new Label();
    private readonly Button _btnOptimizePrev = new Button();
    private readonly Button _btnOptimizeNext = new Button();

    // Hold-to-repeat for the optimization buttons. A single press-and-hold is one
    // undo step: first auto-repeat after a debounce, then a steady interval.
    private readonly System.Windows.Forms.Timer _repeatTimer = new System.Windows.Forms.Timer();
    private bool _repeatActive;
    private bool _repeatDirectionNext;
    private const int RepeatDebounceMs = 500;
    private const int RepeatIntervalMs = 250;

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
    private double _prevThickness;

    public MainWindow()
    {
        _undo = new UndoManager(_doc);

        Text = "RoadGen - 3D Displacement Road Generator";
        Size = new Size(1360, 860);
        MinimumSize = new Size(980, 640);
        BackColor = Color.FromArgb(30, 30, 34);

        BuildTopActionBar();
        BuildLayout();
        ConfigureIncrementControls();
        WireEvents();
        ApplyAllToolTips();
        _repeatTimer.Tick += OnOptRepeatTick;

        SeedDefaultRoad();
        _suppressDirty = true;
        _doc.NotifyChanged();
        _suppressDirty = false;
        _dirty = false;

        RefreshLayerList();
        LoadJoiningIntoControl();
        RefreshFeatureList();
        RefreshList();
        SelectPoint(0);
        FrameAll();
        UpdateUndoButtons();
        UpdateTitle();
    }

    // ---------------------------------------------------------------- layout

    private void BuildTopActionBar()
    {
        ToolStrip topActionBar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _btnOpen = ToolButton("Open Track...", (s, e) => OpenTrack());
        _btnSave = ToolButton("Save Track", (s, e) => SaveTrack());
        _btnSaveAs = ToolButton("Save Track As...", (s, e) => SaveTrackAs());
        _btnImport = ToolButton("Import VMF...", (s, e) => ImportVmf());
        topActionBar.Items.Add(_btnOpen);
        topActionBar.Items.Add(_btnSave);
        topActionBar.Items.Add(_btnSaveAs);
        topActionBar.Items.Add(_btnImport);
        topActionBar.Items.Add(new ToolStripSeparator());
        _btnNew = ToolButton("New", (s, e) => NewRoad());
        topActionBar.Items.Add(_btnNew);
        topActionBar.Items.Add(new ToolStripSeparator());
        _btnAddPoint = ToolButton("Add Point", (s, e) => AddPoint());
        _btnRemovePoint = ToolButton("Remove Point", (s, e) => RemovePoint());
        _btnMoveUp = ToolButton("Move Up", (s, e) => MovePoint(-1));
        _btnMoveDown = ToolButton("Move Down", (s, e) => MovePoint(1));
        topActionBar.Items.Add(_btnAddPoint);
        topActionBar.Items.Add(_btnRemovePoint);
        topActionBar.Items.Add(_btnMoveUp);
        topActionBar.Items.Add(_btnMoveDown);
        topActionBar.Items.Add(new ToolStripSeparator());
        _btnFrame = ToolButton("Frame All", (s, e) => FrameAll());
        topActionBar.Items.Add(_btnFrame);
        topActionBar.Items.Add(_btnSnap);
        // Hammer-style grid controls: an interval (HU) dropdown plus a snap-to-grid
        // toggle. The dropdown mirrors the side-panel "Grid snap" setting.
        topActionBar.Items.Add(new ToolStripSeparator());
        _gridCombo.Items.AddRange(new object[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 });
        _gridCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _gridCombo.SelectedIndex = 6; // 64
        topActionBar.Items.Add(new ToolStripLabel("Grid:"));
        topActionBar.Items.Add(_gridCombo);

        topActionBar.Items.Add(new ToolStripSeparator());
        _btnUndo = ToolButton("Undo", (s, e) => DoUndo());
        _btnRedo = ToolButton("Redo", (s, e) => DoRedo());
        topActionBar.Items.Add(_btnUndo);
        topActionBar.Items.Add(_btnRedo);
        topActionBar.Items.Add(new ToolStripSeparator());
        _btnGenerate = ToolButton("Generate VMF...", (s, e) => Generate());
        topActionBar.Items.Add(_btnGenerate);

        // Experimental brush export (deprecated). Uncomment to enable.
        // topActionBar.Items.Add(ToolButton("Generate Brushes...", (s, e) => GenerateBrushes()));

        // The toolbar is mounted at the top of the viewport panel (Panel1), so it
        // spans only the 3D + 2D views rather than the whole window.
        _topActionBar = topActionBar;
    }

    private void BuildLayout()
    {
        TableLayoutPanel viewportGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        viewportGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        viewportGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        viewportGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        viewportGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _top.SetPlane(Viewport2D.PlaneKind.Top);
        _front.SetPlane(Viewport2D.PlaneKind.Front);
        _side.SetPlane(Viewport2D.PlaneKind.Side);

        // The viewports must fill their cells, otherwise the TableLayoutPanel
        // leaves them at their default (0x0) size and nothing is visible.
        _v3d.Dock = DockStyle.Fill;
        _top.Dock = DockStyle.Fill;
        _front.Dock = DockStyle.Fill;
        _side.Dock = DockStyle.Fill;

        viewportGrid.Controls.Add(_v3d, 0, 0);
        viewportGrid.Controls.Add(_top, 1, 0);
        viewportGrid.Controls.Add(_front, 0, 1);
        viewportGrid.Controls.Add(_side, 1, 1);

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
        // The action bar is docked to the top of the viewport panel, so it spans
        // only the 3D + 2D views; the side panel keeps the full panel height.
        _split.Panel1.Controls.Add(viewportGrid);
        _split.Panel1.Controls.Add(_topActionBar);
        _split.Panel2.Controls.Add(BuildSidePanel());

        StatusStrip statusBar = new StatusStrip();
        statusBar.Items.Add(new ToolStripStatusLabel
        {
            Text = "2D: ctrl+click add, drag to move, shift+drag breaks a weld, drag empty space to box-select  •  3D: right-drag orbit, middle-drag pan, click select  •  [ / ] change grid"
        });

        Panel mainContent = new Panel { Dock = DockStyle.Fill };
        mainContent.Controls.Add(_split);

        // Dock layout is applied in reverse z-order: the Fill panel is added first,
        // the bottom StatusStrip is added last.
        Controls.Add(mainContent);
        Controls.Add(statusBar);
    }

    private Control BuildSidePanel()
    {
        Panel sidePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(38, 38, 42), Padding = new Padding(8) };

        // ── Control Points section ──
        // The whole per-track editor (track list, point table, editors + increments,
        // Solid Roads + joining, road settings) lives in one group with a normal
        // group-box header.
        GroupBox trackSection = new GroupBox
        {
            Text = "Control Points",
            Dock = DockStyle.Top,
            Height = 640,
            Padding = new Padding(6),
            ForeColor = Color.LightGray
        };

        // Track list + action buttons + up/down movers under the header.
        Panel trackListHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 150,
            Padding = new Padding(0, 4, 0, 0)
        };

        _lstLayers.Dock = DockStyle.Fill;
        _lstLayers.IntegralHeight = false;

        // Track action buttons (+ Add / - Remove / Rename / Duplicate / Merge) in
        // a vertical column on the right of the track list.
        FlowLayoutPanel layerActionColumn = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 92,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(4, 0, 0, 0),
            Padding = new Padding(0)
        };

        _btnAddLayer.Text = "+ Add";
        _btnAddLayer.AutoSize = false;
        _btnAddLayer.Size = new Size(88, 26);
        _btnAddLayer.Margin = new Padding(0, 0, 0, 3);
        _btnRemoveLayer.Text = "- Remove";
        _btnRemoveLayer.AutoSize = false;
        _btnRemoveLayer.Size = new Size(88, 26);
        _btnRemoveLayer.Margin = new Padding(0, 0, 0, 3);
        _btnRenameLayer.Text = "Rename";
        _btnRenameLayer.AutoSize = false;
        _btnRenameLayer.Size = new Size(88, 26);
        _btnRenameLayer.Margin = new Padding(0, 0, 0, 3);
        _btnDuplicateLayer.Text = "Duplicate";
        _btnDuplicateLayer.AutoSize = false;
        _btnDuplicateLayer.Size = new Size(88, 26);
        _btnDuplicateLayer.Margin = new Padding(0, 0, 0, 3);
        _btnMergeLayer.Text = "Merge";
        _btnMergeLayer.AutoSize = false;
        _btnMergeLayer.Size = new Size(88, 26);
        _btnMergeLayer.Margin = new Padding(0);
        _btnMergeLayer.Enabled = false;

        StyleLayerButton(_btnAddLayer);
        StyleLayerButton(_btnRemoveLayer);
        StyleLayerButton(_btnRenameLayer);
        StyleLayerButton(_btnDuplicateLayer);
        StyleLayerButton(_btnMergeLayer);

        layerActionColumn.Controls.Add(_btnAddLayer);
        layerActionColumn.Controls.Add(_btnRemoveLayer);
        layerActionColumn.Controls.Add(_btnRenameLayer);
        layerActionColumn.Controls.Add(_btnDuplicateLayer);
        layerActionColumn.Controls.Add(_btnMergeLayer);

        // Up/down mover buttons live at the far right of the track list.
        FlowLayoutPanel layerMoverColumn = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 30,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(4, 0, 0, 0),
            Padding = new Padding(0)
        };

        _btnLayerUp.Text = "\u25B2";
        _btnLayerUp.Size = new Size(26, 24);
        _btnLayerUp.Margin = new Padding(0, 0, 0, 2);
        _btnLayerDown.Text = "\u25BC";
        _btnLayerDown.Size = new Size(26, 24);
        _btnLayerDown.Margin = new Padding(0);

        StyleLayerButton(_btnLayerUp);
        StyleLayerButton(_btnLayerDown);

        layerMoverColumn.Controls.Add(_btnLayerUp);
        layerMoverColumn.Controls.Add(_btnLayerDown);

        // Docking is in reverse z-order (last added docks first), so the action
        // column is added last to sit at the far right, with the movers next to it
        // and the list filling the rest: [list | movers | actions].
        trackListHost.Controls.Add(_lstLayers);
        trackListHost.Controls.Add(layerMoverColumn);
        trackListHost.Controls.Add(layerActionColumn);

        // Point table (fills the middle of the Track section).
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
        _list.Columns.Add("Thickness", 70, HorizontalAlignment.Right);

        // Per-point editors + their increments, one row per property (same line,
        // mirroring the Edge Features rows): label | Grid ☑ + step | value.
        TableLayoutPanel controlPointEditorRows = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(0, 6, 0, 0)
        };
        controlPointEditorRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        controlPointEditorRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        controlPointEditorRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int r = 0; r < 6; r++)
        {
            controlPointEditorRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        }

        AddFeatureSettingRow(controlPointEditorRows, 0, "X", _numX, BuildFeatureIncrementCell(_chkIncX, _numIncX, followGrid: true, customValue: 64m));
        AddFeatureSettingRow(controlPointEditorRows, 1, "Y", _numY, BuildFeatureIncrementCell(_chkIncY, _numIncY, followGrid: true, customValue: 64m));
        AddFeatureSettingRow(controlPointEditorRows, 2, "Z", _numZ, BuildFeatureIncrementCell(_chkIncZ, _numIncZ, followGrid: true, customValue: 64m));
        AddFeatureSettingRow(controlPointEditorRows, 3, "Width", _numWidth, BuildFeatureIncrementCell(_chkIncWidth, _numIncWidth, followGrid: true, customValue: 64m));
        AddFeatureSettingRow(controlPointEditorRows, 4, "Bank", _numBank, BuildFeatureIncrementCell(_chkIncBank, _numIncBank, followGrid: false, customValue: 4m));
        AddFeatureSettingRow(controlPointEditorRows, 5, "Thick", _trackThickness, BuildFeatureIncrementCell(_chkIncThickness, _numIncThickness, followGrid: true, customValue: 64m));

        // AddFeatureSettingRow clamps numerics to 0..100000; restore the wider
        // point-value range (X/Y/Z/Bank can be negative, old editors allowed ±1,000,000).
        _numX.Minimum = -1000000; _numX.Maximum = 1000000;
        _numY.Minimum = -1000000; _numY.Maximum = 1000000;
        _numZ.Minimum = -1000000; _numZ.Maximum = 1000000;
        _numWidth.Minimum = -1000000; _numWidth.Maximum = 1000000;
        _numBank.Minimum = -1000000; _numBank.Maximum = 1000000;
        _trackThickness.Minimum = 0;

        // Road settings inputs.
        TableLayoutPanel roadSettingsInputs = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 135,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(0)
        };
        roadSettingsInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        roadSettingsInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int r = 0; r < 5; r++)
        {
            roadSettingsInputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        }

        _cboPower.Items.AddRange(new object[] { 2, 3, 4 });
        _cboPower.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboPower.SelectedIndex = 1;

        _cboSnap.Items.AddRange(new object[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 });
        _cboSnap.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboSnap.SelectedIndex = 6; // 64

        _cboLightmap.Items.AddRange(new object[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 });
        _cboLightmap.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboLightmap.SelectedIndex = 4; // 16

        _txtMaterial.Text = _doc.Settings.Material;

        AddSettingRow(roadSettingsInputs, 0, "Power", _cboPower);
        AddSettingRow(roadSettingsInputs, 1, "Material", _txtMaterial);
        AddSettingRow(roadSettingsInputs, 2, "Texture scale", _numTexScale);
        _numTexScale.Increment = 0.25m;
        AddSettingRow(roadSettingsInputs, 3, "Lightmap scale", _cboLightmap);
        AddSettingRow(roadSettingsInputs, 4, "Grid snap", _cboSnap);

        // Solid Roads + Enable track joining, directly under the point table.
        GroupBox solidRoadsSection = new GroupBox
        {
            Text = "Solid Roads",
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(6, 2, 6, 4),
            ForeColor = Color.LightGray
        };

        FlowLayoutPanel solidRoadsJoiningRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _chkSolidLeft.Text = "Left side";
        _chkSolidLeft.AutoSize = true;
        _chkSolidLeft.ForeColor = Color.LightGray;
        _chkSolidRight.Text = "Right side";
        _chkSolidRight.AutoSize = true;
        _chkSolidRight.ForeColor = Color.LightGray;
        _chkSolidBottom.Text = "Bottom";
        _chkSolidBottom.AutoSize = true;
        _chkSolidBottom.ForeColor = Color.LightGray;
        _chkEnableJoining.Text = "Enable track joining";
        _chkEnableJoining.AutoSize = true;
        _chkEnableJoining.ForeColor = Color.LightGray;
        _chkEnableJoining.Checked = true;

        solidRoadsJoiningRow.Controls.Add(_chkSolidLeft);
        solidRoadsJoiningRow.Controls.Add(_chkSolidRight);
        solidRoadsJoiningRow.Controls.Add(_chkSolidBottom);
        solidRoadsJoiningRow.Controls.Add(_chkEnableJoining);
        solidRoadsSection.Controls.Add(solidRoadsJoiningRow);

        // Docking stacks in reverse z-order (last added docks first), so children
        // are added bottom-up: Solid Roads, road settings, editor rows, point table,
        // track list, header. The track action buttons live in the track list's
        // right-hand column, so there is no bottom action row.
        trackSection.Controls.Add(solidRoadsSection);
        trackSection.Controls.Add(roadSettingsInputs);
        trackSection.Controls.Add(controlPointEditorRows);
        trackSection.Controls.Add(_list);
        trackSection.Controls.Add(trackListHost);

        // ── Optimization section (standalone group, sits at the bottom of the
        // side panel, under Edge Features). The height fits the toggle row (26) +
        // step row (30) plus the group title and padding, so the step buttons
        // aren't clipped.
        GroupBox optimizationSection = new GroupBox
        {
            Text = "Optimization",
            Dock = DockStyle.Top,
            Height = 90,
            Padding = new Padding(6),
            ForeColor = Color.LightGray
        };

        // Rows are added in reverse visual order because docking stacks in reverse
        // z-order (last added docks first): step row (bottom) then toggle row (top).
        optimizationSection.Controls.Add(BuildOptimizationStepRow());
        optimizationSection.Controls.Add(BuildOptimizationToggleRow());

        GroupBox edgeFeaturesSection = new GroupBox
        {
            Text = "Edge Features",
            Dock = DockStyle.Top,
            Height = 500,
            Padding = new Padding(6),
            ForeColor = Color.LightGray
        };

        // Feature list + action buttons (+ Add / - Remove) in a vertical column on
        // the right, mirroring the track list in the Control Points section.
        Panel featureListHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(0, 0, 0, 4)
        };

        _lstFeatures.Dock = DockStyle.Fill;
        _lstFeatures.IntegralHeight = false;

        FlowLayoutPanel featureActionColumn = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 76,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(4, 0, 0, 0),
            Padding = new Padding(0)
        };

        _btnAddFeature.Text = "+ Add";
        _btnAddFeature.AutoSize = false;
        _btnAddFeature.Size = new Size(72, 24);
        _btnAddFeature.Margin = new Padding(3, 0, 0, 3);
        _btnRemoveFeature.Text = "- Remove";
        _btnRemoveFeature.AutoSize = false;
        _btnRemoveFeature.Size = new Size(72, 24);
        _btnRemoveFeature.Margin = new Padding(3, 0, 0, 3);
        StyleLayerButton(_btnAddFeature);
        StyleLayerButton(_btnRemoveFeature);

        featureActionColumn.Controls.Add(_btnAddFeature);
        featureActionColumn.Controls.Add(_btnRemoveFeature);

        // Docking is in reverse z-order (last added docks first), so the action
        // column is added last to sit at the far right: [feature list | buttons].
        featureListHost.Controls.Add(_lstFeatures);
        featureListHost.Controls.Add(featureActionColumn);

        _lstFeaturePoints.Dock = DockStyle.Fill;
        _lstFeaturePoints.View = View.Details;
        _lstFeaturePoints.FullRowSelect = true;
        _lstFeaturePoints.MultiSelect = true;
        _lstFeaturePoints.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _lstFeaturePoints.Columns.Add("#", 34);
        _lstFeaturePoints.Columns.Add("Width", 64);
        _lstFeaturePoints.Columns.Add("Bottom Z", 68);
        _lstFeaturePoints.Columns.Add("Top Z", 68);
        _lstFeaturePoints.Columns.Add("Bank", 56);

        FlowLayoutPanel featureFaceToggleRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _chkFeatureBottom.Text = "Bottom";
        _chkFeatureBottom.AutoSize = true;
        _chkFeatureBottom.ForeColor = Color.LightGray;
        _chkFeatureBottom.Checked = true;
        _chkFeatureInner.Text = "Inner";
        _chkFeatureInner.AutoSize = true;
        _chkFeatureInner.ForeColor = Color.LightGray;
        _chkFeatureInner.Checked = true;
        _chkFeatureOuter.Text = "Outer";
        _chkFeatureOuter.AutoSize = true;
        _chkFeatureOuter.ForeColor = Color.LightGray;
        _chkFeatureOuter.Checked = true;

        featureFaceToggleRow.Controls.Add(_chkFeatureBottom);
        featureFaceToggleRow.Controls.Add(_chkFeatureInner);
        featureFaceToggleRow.Controls.Add(_chkFeatureOuter);

        TableLayoutPanel featureInputs = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 192,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(0)
        };
        featureInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        featureInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        featureInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int r = 0; r < 8; r++)
        {
            featureInputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        }

        _cboFeatureKind.Items.AddRange(new object[] { "Sidewalk", "Guardrail" });
        _cboFeatureKind.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboFeatureKind.SelectedIndex = 0;

        _cboFeatureSide.Items.AddRange(new object[] { "Left", "Right" });
        _cboFeatureSide.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboFeatureSide.SelectedIndex = 0;

        AddFeatureSettingRow(featureInputs, 0, "Kind", _cboFeatureKind, null);
        AddFeatureSettingRow(featureInputs, 1, "Side", _cboFeatureSide, null);

        AddFeatureSettingRow(featureInputs, 2, "Offset", _numFeatureOffset, BuildFeatureIncrementCell(_chkFeatureIncOffset, _numFeatureIncOffset, followGrid: true, customValue: 64m));

        AddFeatureSettingRow(featureInputs, 3, "Width", _numFeatureWidth, BuildFeatureIncrementCell(_chkFeatureIncWidth, _numFeatureIncWidth, followGrid: true, customValue: 64m));

        AddFeatureSettingRow(featureInputs, 4, "Bottom Z", _numFeatureBottomZ, BuildFeatureIncrementCell(_chkFeatureIncBottomZ, _numFeatureIncBottomZ, followGrid: true, customValue: 64m));

        AddFeatureSettingRow(featureInputs, 5, "Top Z", _numFeatureTopZ, BuildFeatureIncrementCell(_chkFeatureIncTopZ, _numFeatureIncTopZ, followGrid: true, customValue: 64m));

        AddFeatureSettingRow(featureInputs, 6, "Bank", _numFeatureBank, BuildFeatureIncrementCell(_chkFeatureIncBank, _numFeatureIncBank, followGrid: false, customValue: 4m));

        AddFeatureSettingRow(featureInputs, 7, "Material", _txtFeatureMaterial, null);

        _numFeatureOffset.Minimum = -100000;
        _numFeatureBottomZ.Minimum = -100000;
        _numFeatureTopZ.Minimum = -100000;
        _numFeatureBank.Minimum = -100000;

        edgeFeaturesSection.Controls.Add(_lstFeaturePoints);
        edgeFeaturesSection.Controls.Add(featureListHost);
        edgeFeaturesSection.Controls.Add(featureFaceToggleRow);
        edgeFeaturesSection.Controls.Add(featureInputs);

        Button generateCommand = new Button
        {
            Text = "Generate VMF...",
            Dock = DockStyle.Bottom,
            Height = 42,
            FlatStyle = FlatStyle.System,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        generateCommand.Click += (s, e) => Generate();

        // Everything above the Generate button lives in a scrollable panel so the
        // side panel works at any window height.
        Panel sidePanelScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // Docked controls stack in reverse z-order (last added docks first), so
        // the last top-docked control ends up at the very top. Desired layout from
        // top to bottom: Control Points, Edge Features, Optimization.
        sidePanelScroll.Controls.Add(optimizationSection);
        sidePanelScroll.Controls.Add(edgeFeaturesSection);
        sidePanelScroll.Controls.Add(trackSection);

        sidePanel.Controls.Add(sidePanelScroll);
        sidePanel.Controls.Add(generateCommand);

        // Group-box titles are lit up to LightGray; reset the editor text colors
        // so the inputs stay readable on their light backgrounds.
        StyleSectionInputTextColors();

        return sidePanel;
    }

    /// <summary>The section group-box titles are drawn with the group's ForeColor,
    /// which is dark-on-dark here, so they are lit up to LightGray. ForeColor is
    /// ambient, so this explicitly restores the theme window-text color on every
    /// editor input that sits inside those groups, keeping them readable on their
    /// light (white) backgrounds.</summary>
    private void StyleSectionInputTextColors()
    {
        Control[] darkTextInputs =
        {
            _lstLayers, _list, _lstFeatures, _lstFeaturePoints,
            _numX, _numY, _numZ, _numWidth, _numBank, _trackThickness,
            _numTexScale, _cboPower, _cboSnap, _cboLightmap, _txtMaterial,
            _numIncX, _numIncY, _numIncZ, _numIncWidth, _numIncBank, _numIncThickness,
            _numFeatureOffset, _numFeatureWidth, _numFeatureBottomZ, _numFeatureTopZ, _numFeatureBank,
            _numFeatureIncOffset, _numFeatureIncWidth, _numFeatureIncBottomZ, _numFeatureIncTopZ, _numFeatureIncBank,
            _cboFeatureKind, _cboFeatureSide, _txtFeatureMaterial
        };
        foreach (Control inputControl in darkTextInputs)
        {
            inputControl.ForeColor = SystemColors.WindowText;
        }
    }

    /// <summary>Builds the Optimization section's step row (the ◀ / ▶ buttons that
    /// step through displacement counts) and wires their hold-to-repeat behaviour.</summary>
    private FlowLayoutPanel BuildOptimizationStepRow()
    {
        FlowLayoutPanel stepRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 0)
        };

        _btnOptimizePrev.AutoSize = true;
        _btnOptimizePrev.Margin = new Padding(0, 0, 4, 0);
        _btnOptimizePrev.Text = "◀ -";
        _btnOptimizePrev.Enabled = false;
        StyleLayerButton(_btnOptimizePrev);
        _btnOptimizePrev.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) StartOptRepeat(next: false); };
        _btnOptimizePrev.MouseUp += (s, e) => StopOptRepeat();

        _btnOptimizeNext.AutoSize = true;
        _btnOptimizeNext.Margin = new Padding(0, 0, 0, 0);
        _btnOptimizeNext.Text = "- ▶";
        _btnOptimizeNext.Enabled = false;
        StyleLayerButton(_btnOptimizeNext);
        _btnOptimizeNext.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) StartOptRepeat(next: true); };
        _btnOptimizeNext.MouseUp += (s, e) => StopOptRepeat();

        stepRow.Controls.Add(_btnOptimizePrev);
        stepRow.Controls.Add(_btnOptimizeNext);
        return stepRow;
    }

    /// <summary>Builds the Optimization section's toggle row ("See disps" preview
    /// toggles plus the live displacement count).</summary>
    private FlowLayoutPanel BuildOptimizationToggleRow()
    {
        _chkShowDisps.Text = "See disps";
        _chkShowDisps.AutoSize = true;
        _chkShowDisps.Checked = false;

        _chkShowSidewalkDisps.Text = "See sidewalk disps";
        _chkShowSidewalkDisps.AutoSize = true;
        _chkShowSidewalkDisps.Checked = false;

        _lblDispCount.AutoSize = true;
        _lblDispCount.ForeColor = Color.LightGray;
        _lblDispCount.Margin = new Padding(12, 0, 0, 0);
        _lblDispCount.Text = "0 disps";

        FlowLayoutPanel toggleRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 26,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        toggleRow.Controls.Add(_chkShowDisps);
        toggleRow.Controls.Add(_chkShowSidewalkDisps);
        toggleRow.Controls.Add(_lblDispCount);
        return toggleRow;
    }


    private void ConfigureIncrementControls()
    {
        // Seed the increment controls from the document defaults. Events are wired
        // later in WireEvents, so no handlers fire here yet.
        _loading = true;
        LoadIncrementsIntoControls();
        LoadFeatureIncrementsIntoControls();
        _loading = false;
        UpdateIncrements();
    }

    private void LoadIncrementsIntoControls()
    {
        RoadSettings s = _doc.Settings;
        _chkIncX.Checked = s.IncUseGridX;
        _chkIncY.Checked = s.IncUseGridY;
        _chkIncZ.Checked = s.IncUseGridZ;
        _chkIncWidth.Checked = s.IncUseGridWidth;
        _chkIncBank.Checked = s.IncUseGridBank;
        _numIncX.Value = (decimal)s.IncCustomX;
        _numIncY.Value = (decimal)s.IncCustomY;
        _numIncZ.Value = (decimal)s.IncCustomZ;
        _numIncWidth.Value = (decimal)s.IncCustomWidth;
        _numIncBank.Value = (decimal)s.IncCustomBank;
        _chkIncThickness.Checked = s.IncUseGridThickness;
        _numIncThickness.Value = (decimal)s.IncCustomThickness;
    }

    private void LoadFeatureIncrementsIntoControls()
    {
        RoadSettings s = _doc.Settings;
        _chkFeatureIncOffset.Checked = s.FeatureIncUseGridOffset;
        _chkFeatureIncWidth.Checked = s.FeatureIncUseGridWidth;
        _chkFeatureIncBottomZ.Checked = s.FeatureIncUseGridBottomZ;
        _chkFeatureIncTopZ.Checked = s.FeatureIncUseGridTopZ;
        _chkFeatureIncBank.Checked = s.FeatureIncUseGridBank;
        _numFeatureIncOffset.Value = (decimal)s.FeatureIncCustomOffset;
        _numFeatureIncWidth.Value = (decimal)s.FeatureIncCustomWidth;
        _numFeatureIncBottomZ.Value = (decimal)s.FeatureIncCustomBottomZ;
        _numFeatureIncTopZ.Value = (decimal)s.FeatureIncCustomTopZ;
        _numFeatureIncBank.Value = (decimal)s.FeatureIncCustomBank;
    }

    private void ApplyIncrementsFromControls()
    {
        if (_loading)
        {
            return;
        }

        _undo.BeginSession();
        var s = _doc.Settings;
        s.IncUseGridX = _chkIncX.Checked;
        s.IncUseGridY = _chkIncY.Checked;
        s.IncUseGridZ = _chkIncZ.Checked;
        s.IncUseGridWidth = _chkIncWidth.Checked;
        s.IncUseGridBank = _chkIncBank.Checked;
        s.IncCustomX = (double)_numIncX.Value;
        s.IncCustomY = (double)_numIncY.Value;
        s.IncCustomZ = (double)_numIncZ.Value;
        s.IncCustomWidth = (double)_numIncWidth.Value;
        s.IncCustomBank = (double)_numIncBank.Value;
        s.IncUseGridThickness = _chkIncThickness.Checked;
        s.IncCustomThickness = (double)_numIncThickness.Value;

        UpdateIncrements();
        _doc.NotifyChanged();
    }

    private void UpdateIncrements()
    {
        double grid = _doc.Settings.Snap > 0 ? _doc.Settings.Snap : 64;

        // Edge feature values follow the grid snap unless their own "Grid"
        // checkbox is unchecked, in which case the custom interval is used.
        ApplyIncrement(_numFeatureOffset, _chkFeatureIncOffset, _numFeatureIncOffset, grid);
        ApplyIncrement(_numFeatureWidth, _chkFeatureIncWidth, _numFeatureIncWidth, grid);
        ApplyIncrement(_numFeatureBottomZ, _chkFeatureIncBottomZ, _numFeatureIncBottomZ, grid);
        ApplyIncrement(_numFeatureTopZ, _chkFeatureIncTopZ, _numFeatureIncTopZ, grid);
        ApplyIncrement(_numFeatureBank, _chkFeatureIncBank, _numFeatureIncBank, grid);

        ApplyIncrement(_numX, _chkIncX, _numIncX, grid);
        ApplyIncrement(_numY, _chkIncY, _numIncY, grid);
        ApplyIncrement(_numZ, _chkIncZ, _numIncZ, grid);
        ApplyIncrement(_numWidth, _chkIncWidth, _numIncWidth, grid);
        ApplyIncrement(_numBank, _chkIncBank, _numIncBank, grid);
        ApplyIncrement(_trackThickness, _chkIncThickness, _numIncThickness, grid);
    }

    private void ApplyFeatureIncrementsFromControls()
    {
        if (_loading)
        {
            return;
        }

        _undo.BeginSession();
        var s = _doc.Settings;
        s.FeatureIncUseGridOffset = _chkFeatureIncOffset.Checked;
        s.FeatureIncUseGridWidth = _chkFeatureIncWidth.Checked;
        s.FeatureIncUseGridBottomZ = _chkFeatureIncBottomZ.Checked;
        s.FeatureIncUseGridTopZ = _chkFeatureIncTopZ.Checked;
        s.FeatureIncUseGridBank = _chkFeatureIncBank.Checked;
        s.FeatureIncCustomOffset = (double)_numFeatureIncOffset.Value;
        s.FeatureIncCustomWidth = (double)_numFeatureIncWidth.Value;
        s.FeatureIncCustomBottomZ = (double)_numFeatureIncBottomZ.Value;
        s.FeatureIncCustomTopZ = (double)_numFeatureIncTopZ.Value;
        s.FeatureIncCustomBank = (double)_numFeatureIncBank.Value;

        UpdateIncrements();
        _doc.NotifyChanged();
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
            UpdatePreviewInfo();
            UpdateMergeButton();
            UpdateUndoButtons();

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

        _lstLayers.SelectedIndexChanged += OnLayerSelected;
        _btnAddLayer.Click += (s, e) => AddLayer();
        _btnRemoveLayer.Click += (s, e) => RemoveLayer();
        _btnRenameLayer.Click += (s, e) => RenameLayer();
        _btnDuplicateLayer.Click += (s, e) => DuplicateLayer();
        _btnMergeLayer.Click += (s, e) => MergeTracks();
        _btnLayerUp.Click += (s, e) => MoveLayer(-1);
        _btnLayerDown.Click += (s, e) => MoveLayer(1);
        _chkEnableJoining.CheckedChanged += (s, e) => ApplyJoiningFromControl();

        _lstFeatures.SelectedIndexChanged += (s, e) => LoadFeatureIntoEditor();
        _lstFeaturePoints.SelectedIndexChanged += (s, e) => LoadFeaturePointIntoEditors();
        _btnAddFeature.Click += (s, e) => AddFeature();
        _btnRemoveFeature.Click += (s, e) => RemoveFeature();
        _cboFeatureKind.SelectedIndexChanged += (s, e) => ApplyFeatureFromEditor();
        _cboFeatureSide.SelectedIndexChanged += (s, e) => ApplyFeatureFromEditor();
        _numFeatureOffset.ValueChanged += (s, e) => ApplyFeatureFromEditor();
        _numFeatureWidth.ValueChanged += (s, e) => ApplyFeaturePointFromEditor();
        _numFeatureBottomZ.ValueChanged += (s, e) => ApplyFeaturePointFromEditor();
        _numFeatureTopZ.ValueChanged += (s, e) => ApplyFeaturePointFromEditor();
        _numFeatureBank.ValueChanged += (s, e) => ApplyFeaturePointFromEditor();
        _txtFeatureMaterial.TextChanged += (s, e) => ApplyFeatureFromEditor();
        _chkFeatureBottom.CheckedChanged += (s, e) => ApplyFeatureFromEditor();
        _chkFeatureInner.CheckedChanged += (s, e) => ApplyFeatureFromEditor();
        _chkFeatureOuter.CheckedChanged += (s, e) => ApplyFeatureFromEditor();

        // Feature increment/decrement interval controls (editor UI only).
        _chkFeatureIncOffset.CheckedChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _chkFeatureIncWidth.CheckedChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _chkFeatureIncBottomZ.CheckedChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _chkFeatureIncTopZ.CheckedChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _chkFeatureIncBank.CheckedChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _numFeatureIncOffset.ValueChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _numFeatureIncWidth.ValueChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _numFeatureIncBottomZ.ValueChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _numFeatureIncTopZ.ValueChanged += (s, e) => ApplyFeatureIncrementsFromControls();
        _numFeatureIncBank.ValueChanged += (s, e) => ApplyFeatureIncrementsFromControls();

        AttachUndoBatch(_cboFeatureKind);
        AttachUndoBatch(_cboFeatureSide);
        AttachUndoBatch(_numFeatureOffset);
        AttachUndoBatch(_numFeatureWidth);
        AttachUndoBatch(_numFeatureBottomZ);
        AttachUndoBatch(_numFeatureTopZ);
        AttachUndoBatch(_numFeatureBank);
        AttachUndoBatch(_txtFeatureMaterial);
        AttachUndoBatch(_chkFeatureBottom);
        AttachUndoBatch(_chkFeatureInner);
        AttachUndoBatch(_chkFeatureOuter);
        AttachUndoBatch(_chkFeatureIncOffset);
        AttachUndoBatch(_chkFeatureIncWidth);
        AttachUndoBatch(_chkFeatureIncBottomZ);
        AttachUndoBatch(_chkFeatureIncTopZ);
        AttachUndoBatch(_chkFeatureIncBank);
        AttachUndoBatch(_numFeatureIncOffset);
        AttachUndoBatch(_numFeatureIncWidth);
        AttachUndoBatch(_numFeatureIncBottomZ);
        AttachUndoBatch(_numFeatureIncTopZ);
        AttachUndoBatch(_numFeatureIncBank);

        _numX.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numY.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numZ.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numWidth.ValueChanged += (s, e) => UpdatePointFromEditors();
        _numBank.ValueChanged += (s, e) => UpdatePointFromEditors();
        _trackThickness.ValueChanged += (s, e) => UpdatePointFromEditors();

        _cboPower.SelectedIndexChanged += (s, e) => ApplySettingsFromControls();
        _txtMaterial.TextChanged += (s, e) => ApplySettingsFromControls();
        _numTexScale.ValueChanged += (s, e) => ApplySettingsFromControls();
        _cboLightmap.SelectedIndexChanged += (s, e) => ApplySettingsFromControls();
        _cboSnap.SelectedIndexChanged += (s, e) => ApplySettingsFromControls();
        _gridCombo.SelectedIndexChanged += (s, e) => ApplyGridCombo();
        _btnSnap.CheckedChanged += (s, e) => ApplySnapToggle();
        _chkSolidLeft.CheckedChanged += (s, e) => ApplySettingsFromControls();
        _chkSolidRight.CheckedChanged += (s, e) => ApplySettingsFromControls();
        _chkSolidBottom.CheckedChanged += (s, e) => ApplySettingsFromControls();

        _chkShowDisps.CheckedChanged += (s, e) =>
        {
            _v3d.ShowSegments = _chkShowDisps.Checked;
            _top.ShowSegments = _chkShowDisps.Checked;
            _front.ShowSegments = _chkShowDisps.Checked;
            _side.ShowSegments = _chkShowDisps.Checked;
            InvalidateAll();
        };

        _chkShowSidewalkDisps.CheckedChanged += (s, e) =>
        {
            _v3d.ShowFeatureSegments = _chkShowSidewalkDisps.Checked;
            _top.ShowFeatureSegments = _chkShowSidewalkDisps.Checked;
            _front.ShowFeatureSegments = _chkShowSidewalkDisps.Checked;
            _side.ShowFeatureSegments = _chkShowSidewalkDisps.Checked;
            InvalidateAll();
        };

        AttachUndoBatch(_numX);
        AttachUndoBatch(_numY);
        AttachUndoBatch(_numZ);
        AttachUndoBatch(_numWidth);
        AttachUndoBatch(_numBank);
        AttachUndoBatch(_trackThickness);
        AttachUndoBatch(_numTexScale);
        AttachUndoBatch(_cboLightmap);
        AttachUndoBatch(_cboSnap);
        _chkIncX.CheckedChanged += (s, e) => ApplyIncrementsFromControls();
        _chkIncY.CheckedChanged += (s, e) => ApplyIncrementsFromControls();
        _chkIncZ.CheckedChanged += (s, e) => ApplyIncrementsFromControls();
        _chkIncWidth.CheckedChanged += (s, e) => ApplyIncrementsFromControls();
        _chkIncBank.CheckedChanged += (s, e) => ApplyIncrementsFromControls();
        _numIncX.ValueChanged += (s, e) => ApplyIncrementsFromControls();
        _numIncY.ValueChanged += (s, e) => ApplyIncrementsFromControls();
        _numIncZ.ValueChanged += (s, e) => ApplyIncrementsFromControls();
        _numIncWidth.ValueChanged += (s, e) => ApplyIncrementsFromControls();
        _numIncBank.ValueChanged += (s, e) => ApplyIncrementsFromControls();
        _chkIncThickness.CheckedChanged += (s, e) => ApplyIncrementsFromControls();
        _numIncThickness.ValueChanged += (s, e) => ApplyIncrementsFromControls();
        AttachUndoBatch(_chkIncX);
        AttachUndoBatch(_chkIncY);
        AttachUndoBatch(_chkIncZ);
        AttachUndoBatch(_chkIncWidth);
        AttachUndoBatch(_chkIncBank);
        AttachUndoBatch(_numIncX);
        AttachUndoBatch(_numIncY);
        AttachUndoBatch(_numIncZ);
        AttachUndoBatch(_numIncWidth);
        AttachUndoBatch(_numIncBank);
        AttachUndoBatch(_chkIncThickness);
        AttachUndoBatch(_numIncThickness);
        AttachUndoBatch(_chkSolidLeft);
        AttachUndoBatch(_chkSolidRight);
        AttachUndoBatch(_chkSolidBottom);
        AttachUndoBatch(_chkEnableJoining);
        AttachUndoBatch(_cboPower);
        AttachUndoBatch(_txtMaterial);

        // Selection lists close any in-progress coalescing session (e.g. number-box
        // increments), so editing one point then another stays as separate undo steps.
        AttachUndoBatch(_lstLayers);
        AttachUndoBatch(_list);
        AttachUndoBatch(_lstFeatures);
        AttachUndoBatch(_lstFeaturePoints);
    }

    // ---------------------------------------------------------------- data

    private void SeedDefaultRoad()
    {
        _doc.Points.Add(new RoadPoint(new Vec3(-512, 256, -320), 256, 16));
        _doc.Points.Add(new RoadPoint(new Vec3(0, 256, -256), 256, 12));
        _doc.Points.Add(new RoadPoint(new Vec3(576, 0, -128), 256, -4));
        _doc.Points.Add(new RoadPoint(new Vec3(1088, 320, 192), 256, -10));
        _doc.Points.Add(new RoadPoint(new Vec3(1600, 128, 128), 256, -14));
        _doc.Points.Add(new RoadPoint(new Vec3(2112, 384, 320), 256, 0));
    }

    private void NewRoad()
    {
        _undo.RecordSingle();
        _doc.Tracks.Clear();
        _doc.Tracks.Add(new Track("Track 1"));
        _doc.ActiveTrackIndex = 0;
        _selectedIndex = -1;
        _currentTrackPath = null;
        _suppressDirty = true;
        _doc.NotifyChanged();
        _suppressDirty = false;
        _dirty = false;
        RefreshLayerList();
        RefreshList();
        LoadPointIntoEditors();
        LoadSettingsIntoControls();
        LoadJoiningIntoControl();
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
            TrackFile.TrackLoadResult result = TrackFile.Load(dlg.FileName);
            _undo.RecordSingle();
            ApplyDocument(result.Document);
            _currentTrackPath = dlg.FileName;
            _dirty = false;
            AfterDocumentLoaded();
            UpdateTitle();

            if (result.NeedsUpgrade)
            {
                var answer = MessageBox.Show(
                    this,
                    $"This track was saved with an older format (v{result.FromVersion}).\n" +
                    $"Upgrade it to the current format (v{result.ToVersion})? Your road is unchanged.",
                    "RoadGen",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    TrackFile.Save(_doc, dlg.FileName);
                    _dirty = false;
                    UpdateTitle();
                }
            }
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
            List<RoadPoint> importedPoints = VmfImporter.ImportRoad(text);
            string sourceName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);

            _undo.RecordSingle();
            Track importedTrack = new Track(NextUniqueTrackName(sourceName, "Imported road"));
            foreach (RoadPoint point in importedPoints)
            {
                importedTrack.Points.Add(point);
            }

            _doc.Tracks.Add(importedTrack);
            _doc.ActiveTrackIndex = _doc.Tracks.Count - 1;

            _currentTrackPath = null;
            _dirty = true;
            _selectedIndex = -1;
            RefreshLayerList();
            ActivateLayer();
            UpdateUndoButtons();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyDocument(RoadDocument loaded)
    {
        _doc.Tracks.Clear();
        foreach (Track track in loaded.Tracks)
        {
            _doc.Tracks.Add(track.Clone());
        }

        if (_doc.Tracks.Count == 0)
        {
            _doc.Tracks.Add(new Track("Track 1"));
        }

        _doc.ActiveTrackIndex = Math.Clamp(loaded.ActiveTrackIndex, 0, _doc.Tracks.Count - 1);
    }

    private void AfterDocumentLoaded()
    {
        RefreshLayerList();
        LoadSettingsIntoControls();
        LoadJoiningIntoControl();
        RefreshFeatureList();
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
        UpdatePreviewInfo();
        UpdateIncrements();
        UpdateMergeButton();
    }

    // ---------------------------------------------------------------- layers

    private void RefreshLayerList()
    {
        _loading = true;
        _lstLayers.BeginUpdate();
        _lstLayers.Items.Clear();

        foreach (Track track in _doc.Tracks)
        {
            _lstLayers.Items.Add(track.Name);
        }

        int activeIndex = _doc.ActiveTrackIndex;
        if (activeIndex >= 0 && activeIndex < _lstLayers.Items.Count)
        {
            _lstLayers.SelectedIndex = activeIndex;
        }
        else if (_lstLayers.Items.Count > 0)
        {
            _lstLayers.SelectedIndex = 0;
        }

        _lstLayers.EndUpdate();
        _loading = false;
    }

    private void OnLayerSelected(object sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        int layerIndex = _lstLayers.SelectedIndex;
        if (layerIndex < 0 || layerIndex >= _doc.Tracks.Count)
        {
            return;
        }

        if (layerIndex == _doc.ActiveTrackIndex)
        {
            return;
        }

        _doc.ActiveTrackIndex = layerIndex;
        ActivateLayer();
    }

    private void LoadJoiningIntoControl()
    {
        _loading = true;
        _chkEnableJoining.Checked = _doc.ActiveTrack != null && _doc.ActiveTrack.EnableJoining;
        _loading = false;
    }

    private void ApplyJoiningFromControl()
    {
        if (_loading || _doc.ActiveTrack == null)
        {
            return;
        }

        _undo.BeginChange();
        _doc.ActiveTrack.EnableJoining = _chkEnableJoining.Checked;
        _doc.NotifyChanged();
        UpdateUndoButtons();
    }

    private void RefreshFeatureList()
    {
        _loading = true;
        int selected = _lstFeatures.SelectedIndex;
        _lstFeatures.BeginUpdate();
        _lstFeatures.Items.Clear();

        Track activeTrack = _doc.ActiveTrack;
        if (activeTrack != null)
        {
            foreach (EdgeFeature feature in activeTrack.EdgeFeatures)
            {
                _lstFeatures.Items.Add(FeatureSummary(feature));
            }
        }

        if (selected >= 0 && selected < _lstFeatures.Items.Count)
        {
            _lstFeatures.SelectedIndex = selected;
        }
        else if (_lstFeatures.Items.Count > 0)
        {
            // Nothing valid was selected (e.g. a feature was restored by undo), so
            // fall back to the first feature instead of leaving the editor empty.
            _lstFeatures.SelectedIndex = 0;
        }

        _lstFeatures.EndUpdate();
        _loading = false;
        LoadFeatureIntoEditor();
    }

    private void LoadFeatureIntoEditor()
    {
        if (_loading)
        {
            return;
        }

        Track activeTrack = _doc.ActiveTrack;
        if (activeTrack == null)
        {
            UpdateFeatureEditorEnabled();
            return;
        }

        int index = _lstFeatures.SelectedIndex;
        if (index < 0 || index >= activeTrack.EdgeFeatures.Count)
        {
            // No feature selected (e.g. after undoing "Add"): clear the editor so
            // stale values don't linger in the controls.
            _loading = true;
            _cboFeatureKind.SelectedIndex = 0;
            _cboFeatureSide.SelectedIndex = 0;
            _numFeatureOffset.Value = 0;
            _numFeatureWidth.Value = 0;
            _numFeatureBottomZ.Value = 0;
            _numFeatureTopZ.Value = 0;
            _numFeatureBank.Value = 0;
            _txtFeatureMaterial.Text = string.Empty;
            _chkFeatureBottom.Checked = true;
            _chkFeatureInner.Checked = true;
            _chkFeatureOuter.Checked = true;
            _lstFeaturePoints.Items.Clear();
            _loading = false;
            UpdateFeatureEditorEnabled();
            return;
        }

        EdgeFeature feature = activeTrack.EdgeFeatures[index];

        _loading = true;
        _cboFeatureKind.SelectedIndex = (int)feature.Kind;
        _cboFeatureSide.SelectedIndex = feature.LeftSide ? 0 : 1;
        _numFeatureOffset.Value = (decimal)feature.Offset;
        _txtFeatureMaterial.Text = feature.Material;
        _chkFeatureBottom.Checked = feature.SolidBottom;
        _chkFeatureInner.Checked = feature.SolidInner;
        _chkFeatureOuter.Checked = feature.SolidOuter;
        _loading = false;

        RefreshFeaturePointTable(feature);
        LoadFeaturePointIntoEditors();
        UpdateFeatureEditorEnabled();
    }

    private void RefreshFeaturePointTable(EdgeFeature feature)
    {
        _lstFeaturePoints.BeginUpdate();
        _lstFeaturePoints.Items.Clear();

        for (int pointIndex = 0; pointIndex < feature.Points.Count; pointIndex++)
        {
            EdgeFeaturePoint point = feature.Points[pointIndex];
            ListViewItem row = new ListViewItem(pointIndex.ToString());
            row.SubItems.Add(point.Width.ToString("0.##"));
            row.SubItems.Add(point.BottomOffset.ToString("0.##"));
            row.SubItems.Add(point.TopOffset.ToString("0.##"));
            row.SubItems.Add(point.BankDegrees.ToString("0.##"));
            _lstFeaturePoints.Items.Add(row);
        }

        if (_lstFeaturePoints.Items.Count > 0)
        {
            _lstFeaturePoints.SelectedIndices.Clear();
            _lstFeaturePoints.SelectedIndices.Add(0);
        }

        _lstFeaturePoints.EndUpdate();
    }

    private void LoadFeaturePointIntoEditors()
    {
        if (_loading || _doc.ActiveTrack == null)
        {
            return;
        }

        int featureIndex = _lstFeatures.SelectedIndex;
        if (featureIndex < 0 || featureIndex >= _doc.ActiveTrack.EdgeFeatures.Count)
        {
            return;
        }

        EdgeFeature feature = _doc.ActiveTrack.EdgeFeatures[featureIndex];
        int pointIndex = _lstFeaturePoints.SelectedIndices.Count > 0 ? _lstFeaturePoints.SelectedIndices[0] : -1;
        if (pointIndex < 0 || pointIndex >= feature.Points.Count)
        {
            return;
        }

        EdgeFeaturePoint point = feature.Points[pointIndex];
        _loading = true;
        _numFeatureWidth.Value = (decimal)point.Width;
        _numFeatureBottomZ.Value = (decimal)point.BottomOffset;
        _numFeatureTopZ.Value = (decimal)point.TopOffset;
        _numFeatureBank.Value = (decimal)point.BankDegrees;
        _loading = false;
    }

    private void ApplyFeatureFromEditor()
    {
        if (_loading || _doc.ActiveTrack == null)
        {
            return;
        }

        int index = _lstFeatures.SelectedIndex;
        if (index < 0 || index >= _doc.ActiveTrack.EdgeFeatures.Count)
        {
            return;
        }

        _undo.BeginChange();
        EdgeFeature feature = _doc.ActiveTrack.EdgeFeatures[index];
        feature.Kind = (EdgeFeatureKind)_cboFeatureKind.SelectedIndex;
        feature.LeftSide = _cboFeatureSide.SelectedIndex == 0;
        feature.Offset = (double)_numFeatureOffset.Value;
        feature.Material = _txtFeatureMaterial.Text;
        feature.SolidBottom = _chkFeatureBottom.Checked;
        feature.SolidInner = _chkFeatureInner.Checked;
        feature.SolidOuter = _chkFeatureOuter.Checked;

        _lstFeatures.Items[index] = FeatureSummary(feature);
        _doc.NotifyChanged();
    }

    private void ApplyFeaturePointFromEditor()
    {
        if (_loading || _doc.ActiveTrack == null)
        {
            return;
        }

        int featureIndex = _lstFeatures.SelectedIndex;
        if (featureIndex < 0 || featureIndex >= _doc.ActiveTrack.EdgeFeatures.Count)
        {
            return;
        }

        EdgeFeature feature = _doc.ActiveTrack.EdgeFeatures[featureIndex];
        List<int> selected = SelectedFeaturePointIndices();
        if (selected.Count == 0)
        {
            return;
        }

        _undo.BeginSession();

        // Width/bottom/top/bank are applied as absolute values across the whole
        // selection, matching the road point editor's width/bank behaviour.
        double width = (double)_numFeatureWidth.Value;
        double bottom = (double)_numFeatureBottomZ.Value;
        double top = (double)_numFeatureTopZ.Value;
        double bank = (double)_numFeatureBank.Value;

        foreach (int pointIndex in selected)
        {
            if (pointIndex < 0 || pointIndex >= feature.Points.Count)
            {
                continue;
            }

            EdgeFeaturePoint point = feature.Points[pointIndex];
            point.Width = width;
            point.BottomOffset = bottom;
            point.TopOffset = top;
            point.BankDegrees = bank;

            ListViewItem row = _lstFeaturePoints.Items[pointIndex];
            row.SubItems[1].Text = point.Width.ToString("0.##");
            row.SubItems[2].Text = point.BottomOffset.ToString("0.##");
            row.SubItems[3].Text = point.TopOffset.ToString("0.##");
            row.SubItems[4].Text = point.BankDegrees.ToString("0.##");
        }

        _doc.NotifyChanged();
    }

    private List<int> SelectedFeaturePointIndices()
    {
        List<int> result = new List<int>();
        foreach (int index in _lstFeaturePoints.SelectedIndices)
        {
            result.Add(index);
        }

        return result;
    }

    /// <summary>True when a feature is actually selected (and the active track has
    /// one), so the per-feature property editor is usable.</summary>
    private bool HasSelectedFeature()
    {
        Track activeTrack = _doc.ActiveTrack;
        if (activeTrack == null)
        {
            return false;
        }

        int index = _lstFeatures.SelectedIndex;
        return index >= 0 && index < activeTrack.EdgeFeatures.Count;
    }

    /// <summary>Gray out every edge-feature property control when there is no
    /// feature to edit yet. Kind, Side and "+ Add" always stay enabled so the user
    /// can choose what kind of feature to add before it exists.</summary>
    private void UpdateFeatureEditorEnabled()
    {
        bool enabled = HasSelectedFeature();

        _numFeatureOffset.Enabled = enabled;
        _numFeatureWidth.Enabled = enabled;
        _numFeatureBottomZ.Enabled = enabled;
        _numFeatureTopZ.Enabled = enabled;
        _numFeatureBank.Enabled = enabled;
        _txtFeatureMaterial.Enabled = enabled;
        _chkFeatureBottom.Enabled = enabled;
        _chkFeatureInner.Enabled = enabled;
        _chkFeatureOuter.Enabled = enabled;

        _chkFeatureIncOffset.Enabled = enabled;
        _chkFeatureIncWidth.Enabled = enabled;
        _chkFeatureIncBottomZ.Enabled = enabled;
        _chkFeatureIncTopZ.Enabled = enabled;
        _chkFeatureIncBank.Enabled = enabled;
        _numFeatureIncOffset.Enabled = enabled;
        _numFeatureIncWidth.Enabled = enabled;
        _numFeatureIncBottomZ.Enabled = enabled;
        _numFeatureIncTopZ.Enabled = enabled;
        _numFeatureIncBank.Enabled = enabled;

        _lstFeaturePoints.Enabled = enabled;
        _btnRemoveFeature.Enabled = enabled;

        // Kind, Side and "+ Add" are always usable.
        _cboFeatureKind.Enabled = true;
        _cboFeatureSide.Enabled = true;
        _btnAddFeature.Enabled = _doc.ActiveTrack != null;
    }

    private void AddFeature()
    {
        if (_doc.ActiveTrack == null)
        {
            return;
        }

        _undo.RecordSingle();
        EdgeFeature feature = new EdgeFeature
        {
            Kind = (EdgeFeatureKind)_cboFeatureKind.SelectedIndex,
            LeftSide = _cboFeatureSide.SelectedIndex == 0
        };
        double topOffset = _doc.ActiveTrack.Settings.Snap > 0 ? _doc.ActiveTrack.Settings.Snap : 64;
        foreach (RoadPoint roadPoint in _doc.ActiveTrack.Points)
        {
            feature.Points.Add(new EdgeFeaturePoint { TopOffset = topOffset });
        }

        _doc.ActiveTrack.EdgeFeatures.Add(feature);
        _doc.NotifyChanged();

        RefreshFeatureList();
        _lstFeatures.SelectedIndex = _lstFeatures.Items.Count - 1;
        UpdateUndoButtons();
    }

    private void SyncFeaturePointsToTrack()
    {
        Track activeTrack = _doc.ActiveTrack;
        if (activeTrack == null)
        {
            return;
        }

        foreach (EdgeFeature feature in activeTrack.EdgeFeatures)
        {
            while (feature.Points.Count < activeTrack.Points.Count)
            {
                EdgeFeaturePoint last = feature.Points.Count > 0 ? feature.Points[feature.Points.Count - 1] : new EdgeFeaturePoint();
                bool lastEnabled = feature.Enabled.Count > 0 ? feature.Enabled[feature.Enabled.Count - 1] : true;
                feature.Points.Add(last.Clone());
                if (feature.Enabled.Count > 0)
                {
                    feature.Enabled.Add(lastEnabled);
                }
            }

            while (feature.Points.Count > activeTrack.Points.Count)
            {
                feature.Points.RemoveAt(feature.Points.Count - 1);
                if (feature.Enabled.Count > 0)
                {
                    feature.Enabled.RemoveAt(feature.Enabled.Count - 1);
                }
            }
        }
    }

    private void RemoveFeature()
    {
        if (_doc.ActiveTrack == null)
        {
            return;
        }

        int index = _lstFeatures.SelectedIndex;
        if (index < 0 || index >= _doc.ActiveTrack.EdgeFeatures.Count)
        {
            return;
        }

        _undo.RecordSingle();
        _doc.ActiveTrack.EdgeFeatures.RemoveAt(index);
        _doc.NotifyChanged();
        RefreshFeatureList();
        UpdateUndoButtons();
    }

    private void ActivateLayer()
    {
        _selectedIndex = -1;
        RefreshList();
        LoadSettingsIntoControls();
        LoadJoiningIntoControl();
        RefreshFeatureList();
        LoadPointIntoEditors();

        if (_doc.Points.Count > 0)
        {
            SelectPoint(0);
        }

        InvalidateAll();
        UpdatePreviewInfo();
        UpdateIncrements();
        UpdateMergeButton();
    }

    private void AddLayer()
    {
        _undo.RecordSingle();

        Track newTrack = new Track(NextTrackName());
        _doc.Tracks.Add(newTrack);
        _doc.ActiveTrackIndex = _doc.Tracks.Count - 1;

        _doc.NotifyChanged();
        RefreshLayerList();
        ActivateLayer();
        UpdateUndoButtons();
    }

    private void RemoveLayer()
    {
        int layerIndex = _doc.ActiveTrackIndex;
        if (layerIndex < 0 || layerIndex >= _doc.Tracks.Count)
        {
            return;
        }

        _undo.RecordSingle();

        if (_doc.Tracks.Count == 1)
        {
            // Never allow a zero-track document: clear the last layer instead.
            _doc.Tracks[0].Points.Clear();
        }
        else
        {
            _doc.Tracks.RemoveAt(layerIndex);
            if (_doc.ActiveTrackIndex >= _doc.Tracks.Count)
            {
                _doc.ActiveTrackIndex = _doc.Tracks.Count - 1;
            }
        }

        _doc.NotifyChanged();
        RefreshLayerList();
        ActivateLayer();
        UpdateUndoButtons();
    }

    private void MoveLayer(int direction)
    {
        int layerIndex = _doc.ActiveTrackIndex;
        if (layerIndex < 0 || layerIndex >= _doc.Tracks.Count)
        {
            return;
        }

        int targetIndex = layerIndex + direction;
        if (targetIndex < 0 || targetIndex >= _doc.Tracks.Count)
        {
            return;
        }

        _undo.RecordSingle();

        Track movedTrack = _doc.Tracks[layerIndex];
        _doc.Tracks[layerIndex] = _doc.Tracks[targetIndex];
        _doc.Tracks[targetIndex] = movedTrack;
        _doc.ActiveTrackIndex = targetIndex;

        _doc.NotifyChanged();
        RefreshLayerList();
        UpdateUndoButtons();
    }

    private void RenameLayer()
    {
        int layerIndex = _doc.ActiveTrackIndex;
        if (layerIndex < 0 || layerIndex >= _doc.Tracks.Count)
        {
            return;
        }

        string currentName = _doc.Tracks[layerIndex].Name;
        string newName = PromptForText("Rename layer", "Layer name:", currentName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        _undo.RecordSingle();
        _doc.Tracks[layerIndex].Name = newName.Trim();
        _doc.NotifyChanged();
        RefreshLayerList();
        UpdateUndoButtons();
    }

    private void DuplicateLayer()
    {
        int layerIndex = _doc.ActiveTrackIndex;
        if (layerIndex < 0 || layerIndex >= _doc.Tracks.Count)
        {
            return;
        }

        Track sourceTrack = _doc.Tracks[layerIndex];
        Track duplicateTrack = sourceTrack.Clone();
        duplicateTrack.Name = NextUniqueTrackName(sourceTrack.Name + " copy", "Track copy");
        duplicateTrack.EnableJoining = false;

        _undo.RecordSingle();
        _doc.Tracks.Insert(layerIndex + 1, duplicateTrack);
        _doc.ActiveTrackIndex = layerIndex + 1;

        _doc.NotifyChanged();
        RefreshLayerList();
        ActivateLayer();
        UpdateUndoButtons();
    }

    private void MergeTracks()
    {
        Track activeTrack = _doc.ActiveTrack;
        if (activeTrack == null)
        {
            return;
        }

        RoadChain joinedChain = FindChainContaining(activeTrack);
        if (joinedChain == null || joinedChain.Spans.Count < 2)
        {
            return;
        }

        // Merge every track in the joined chain into one, named after the active
        // track. The chain's point sequence is already deduplicated at junctions,
        // and edge features are rebuilt so sidewalks keep their side and extent.
        Track mergedTrack = _doc.MergeChain(joinedChain, activeTrack.Name, activeTrack.Settings, activeTrack.EnableJoining);

        HashSet<Track> chainTracks = new HashSet<Track>();
        foreach (ChainSpan span in joinedChain.Spans)
        {
            chainTracks.Add(span.Track);
        }

        int insertIndex = _doc.Tracks.Count;
        for (int index = 0; index < _doc.Tracks.Count; index++)
        {
            if (chainTracks.Contains(_doc.Tracks[index]) && index < insertIndex)
            {
                insertIndex = index;
            }
        }

        _undo.RecordSingle();

        _doc.Tracks.RemoveAll(track => chainTracks.Contains(track));
        _doc.Tracks.Insert(insertIndex, mergedTrack);
        _doc.ActiveTrackIndex = Math.Clamp(insertIndex, 0, _doc.Tracks.Count - 1);

        _doc.NotifyChanged();
        RefreshLayerList();
        ActivateLayer();
        UpdateUndoButtons();
    }

    private RoadChain FindChainContaining(Track track)
    {
        foreach (RoadChain chain in _doc.BuildChains())
        {
            foreach (ChainSpan span in chain.Spans)
            {
                if (ReferenceEquals(span.Track, track))
                {
                    return chain;
                }
            }
        }

        return null;
    }

    private void UpdateMergeButton()
    {
        if (_btnMergeLayer == null)
        {
            return;
        }

        Track activeTrack = _doc.ActiveTrack;
        RoadChain chain = activeTrack == null ? null : FindChainContaining(activeTrack);
        _btnMergeLayer.Enabled = chain != null && chain.Spans.Count > 1;
    }

    private string NextTrackName()
    {
        int number = 1;
        while (true)
        {
            string candidate = $"Track {number}";
            if (!TrackNameInUse(candidate))
            {
                return candidate;
            }

            number++;
        }
    }

    private string NextUniqueTrackName(string baseName, string fallback)
    {
        string cleanBase = string.IsNullOrWhiteSpace(baseName) ? fallback : baseName;
        string candidate = cleanBase;
        int number = 2;

        while (TrackNameInUse(candidate))
        {
            candidate = $"{cleanBase} {number}";
            number++;
        }

        return candidate;
    }

    private bool TrackNameInUse(string name)
    {
        foreach (Track track in _doc.Tracks)
        {
            if (track.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private string PromptForText(string title, string prompt, string initialValue)
    {
        using Form dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(300, 110),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        Label promptLabel = new Label
        {
            Text = prompt,
            Location = new Point(12, 12),
            AutoSize = true
        };

        TextBox input = new TextBox
        {
            Text = initialValue,
            Location = new Point(12, 34),
            Width = 276
        };

        Button okCommand = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(126, 68),
            Size = new Size(78, 26)
        };

        Button cancelCommand = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(210, 68),
            Size = new Size(78, 26)
        };

        dialog.AcceptButton = okCommand;
        dialog.CancelButton = cancelCommand;

        dialog.Controls.Add(promptLabel);
        dialog.Controls.Add(input);
        dialog.Controls.Add(okCommand);
        dialog.Controls.Add(cancelCommand);

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }

    private void AddPoint()
    {
        _undo.RecordSingle();
        RoadPoint last = _doc.Points.Count > 0 ? _doc.Points[_doc.Points.Count - 1] : new RoadPoint(Vec3.Zero, 256, 0);
        RoadPoint p = new RoadPoint(last.Position + new Vec3(256, 0, 0), last.Width, last.BankDegrees, last.Thickness);
        _doc.Points.Add(p);
        SyncFeaturePointsToTrack();
        _doc.NotifyChanged();
        RefreshList();
        SelectPoint(_doc.Points.Count - 1);
        UpdateUndoButtons();
    }

    private void RemovePoint()
    {
        // Stop any in-progress point drag first, so its stale indices don't keep
        // mutating the wrong points after the list shifts on removal.
        _v3d.CancelDrag();
        _top.CancelDrag();
        _front.CancelDrag();
        _side.CancelDrag();

        // Deletes every selected point. Falls back to the single tracked
        // selection when the ListView has no selection (e.g. Delete key).
        List<int> selected = new List<int>();
        foreach (int i in SelectedIndices())
        {
            if (i >= 0 && i < _doc.Points.Count && !selected.Contains(i))
            {
                selected.Add(i);
            }
        }

        if (selected.Count == 0)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _doc.Points.Count)
            {
                return;
            }

            selected.Add(_selectedIndex);
        }

        selected.Sort();
        int anchor = selected[0];

        _undo.RecordSingle();

        // Remove from the end so earlier indices stay valid.
        for (int k = selected.Count - 1; k >= 0; k--)
        {
            _doc.Points.RemoveAt(selected[k]);
        }

        SyncFeaturePointsToTrack();

        _selectedIndex = -1;
        _doc.NotifyChanged();
        RefreshList();

        int next = Math.Min(anchor, _doc.Points.Count - 1);
        if (next >= 0)
        {
            SelectPoint(next);
        }
        else
        {
            LoadPointIntoEditors();
            InvalidateAll();
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
        SyncFeaturePointsToTrack();
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

        _undo.BeginSession();

        // Position edits are applied as a delta (moves the group together);
        // width/bank are applied as absolute values across the selection.
        double dx = (double)_numX.Value - _prevX;
        double dy = (double)_numY.Value - _prevY;
        double dz = (double)_numZ.Value - _prevZ;
        bool widthChanged = (double)_numWidth.Value != _prevWidth;
        bool bankChanged = (double)_numBank.Value != _prevBank;
        bool thicknessChanged = (double)_trackThickness.Value != _prevThickness;

        foreach (int i in selected)
        {
            RoadPoint p = _doc.Points[i];
            Vec3 oldPosition = p.Position;
            Vec3 newPosition = new Vec3(p.Position.X + dx, p.Position.Y + dy, p.Position.Z + dz);
            _doc.MovePointWelded(_doc.ActiveTrack, i, newPosition, oldPosition);

            if (widthChanged)
            {
                p.Width = (double)_numWidth.Value;
            }

            if (bankChanged)
            {
                p.BankDegrees = (double)_numBank.Value;
            }

            if (thicknessChanged)
            {
                p.Thickness = (double)_trackThickness.Value;
            }

            UpdateListRow(i);
        }

        CaptureEditorValues();
        _doc.NotifyChanged();
    }

    private List<int> SelectedIndices()
    {
        List<int> result = new List<int>();
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
        _prevThickness = (double)_trackThickness.Value;
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
        if (_undo.Undo())
        {
            AfterUndoRedo();
        }
        else
        {
            // Nothing was undone (e.g. an open batch whose net change is zero, like
            // a checkbox toggled twice). Just re-sync the button state.
            UpdateUndoButtons();
        }
    }

    private void DoRedo()
    {
        if (_undo.Redo())
        {
            AfterUndoRedo();
        }
        else
        {
            UpdateUndoButtons();
        }
    }

    private void AfterUndoRedo()
    {
        // Preserve the selection across undo/redo.
        List<int> selection = SelectedIndices();

        RefreshLayerList();
        _selectedIndex = -1;
        LoadSettingsIntoControls();
        LoadJoiningIntoControl();
        RefreshFeatureList();
        RefreshList();

        // Re-select the same points. Out-of-range indices are ignored, which is
        // correct when the undo/redo added or removed points.
        SelectMany(selection, additive: false);

        _doc.NotifyChanged();
        UpdateUndoButtons();
        UpdateIncrements();
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

        _undo.BeginChange();
        RoadSettings settings = _doc.Settings;
        settings.Power = (int)_cboPower.SelectedItem;
        settings.Material = _txtMaterial.Text;
        settings.TextureScale = (double)_numTexScale.Value;
        settings.LightmapScale = (int)_cboLightmap.SelectedItem;
        settings.Snap = (int)_cboSnap.SelectedItem;
        settings.SolidLeft = _chkSolidLeft.Checked;
        settings.SolidRight = _chkSolidRight.Checked;
        settings.SolidBottom = _chkSolidBottom.Checked;
        SyncGridCombo();
        UpdateIncrements();
        _doc.NotifyChanged();
    }

    // The toolbar grid dropdown just mirrors the side-panel "Grid snap" dropdown, so
    // picking an interval there sets _cboSnap, which applies the setting as usual.
    private void ApplyGridCombo()
    {
        if (_loading || _syncingGrid || _gridCombo.SelectedIndex < 0)
        {
            return;
        }

        _syncingGrid = true;
        _cboSnap.SelectedIndex = _gridCombo.SelectedIndex;
        _syncingGrid = false;
    }

    private void ApplySnapToggle()
    {
        if (_loading)
        {
            return;
        }

        _btnSnap.Text = _btnSnap.Checked ? "Snap on" : "Snap off";
        _undo.RecordSingle();
        _doc.Settings.SnapEnabled = _btnSnap.Checked;
        _doc.NotifyChanged();
        UpdateUndoButtons();
    }

    private void SyncGridCombo()
    {
        if (_syncingGrid)
        {
            return;
        }

        int index = SnapIndex(_doc.Settings.Snap);
        if (index >= 0 && index < _gridCombo.Items.Count && _gridCombo.SelectedIndex != index)
        {
            _syncingGrid = true;
            _gridCombo.SelectedIndex = index;
            _syncingGrid = false;
        }
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
            _trackThickness.Value = (decimal)p.Thickness;
        }

        CaptureEditorValues();
        _loading = false;
        SyncFeaturePointSelection();
    }

    /// <summary>Mirror the selected control points onto the open feature's point
    /// table, so picking a track point also highlights the matching sidewalk point
    /// and loads its values into the feature editor. Feature points are indexed the
    /// same as track control points, so row i maps straight to track point i.</summary>
    private void SyncFeaturePointSelection()
    {
        if (_lstFeaturePoints == null || _doc.ActiveTrack == null)
        {
            return;
        }

        List<int> selected = SelectedIndices();
        _loading = true;
        _lstFeaturePoints.SelectedIndices.Clear();
        foreach (int index in selected)
        {
            if (index >= 0 && index < _lstFeaturePoints.Items.Count)
            {
                _lstFeaturePoints.SelectedIndices.Add(index);
            }
        }

        _loading = false;
        LoadFeaturePointIntoEditors();
    }

    private void LoadSettingsIntoControls()
    {
        _loading = true;
        RoadSettings settings = _doc.Settings;
        _cboPower.SelectedIndex = Math.Max(0, Array.IndexOf(new object[] { 2, 3, 4 }, settings.Power));
        _txtMaterial.Text = settings.Material;
        _numTexScale.Value = (decimal)settings.TextureScale;
        _cboLightmap.SelectedIndex = LightmapIndex(settings.LightmapScale);
        _cboSnap.SelectedIndex = SnapIndex(settings.Snap);
        _btnSnap.Checked = settings.SnapEnabled;
        _btnSnap.Text = settings.SnapEnabled ? "Snap on" : "Snap off";
        SyncGridCombo();
        _chkSolidLeft.Checked = settings.SolidLeft;
        _chkSolidRight.Checked = settings.SolidRight;
        _chkSolidBottom.Checked = settings.SolidBottom;
        LoadIncrementsIntoControls();
        LoadFeatureIncrementsIntoControls();
        _loading = false;
        UpdateIncrements();
    }

    /// <summary>True when the active track's own first and last points coincide, so
    /// its optimisation preview counts the closing segment of a single-track loop.</summary>
    private bool ActiveTrackClosed =>
        _doc != null && _doc.Points.Count >= 3 && RoadDocument.PositionsMatch(_doc.Points[0].Position, _doc.Points[_doc.Points.Count - 1].Position);

    private void UpdatePreviewInfo()
    {
        if (_lblDispCount == null)
        {
            return;
        }

        int count = _doc.Points.Count >= 2 ? SegmentLayout.CountSegments(_doc.Points, _doc.Settings.SegmentLength, ActiveTrackClosed) : 0;
        _lblDispCount.Text = $"{count} disps";

        if (_btnOptimizeNext == null || _btnOptimizePrev == null)
        {
            return;
        }

        if (_doc.Points.Count < 2)
        {
            _btnOptimizeNext.Text = "- ▶";
            _btnOptimizeNext.Enabled = false;
            _btnOptimizePrev.Text = "◀ -";
            _btnOptimizePrev.Enabled = false;
            return;
        }

        double current = Math.Max(1.0, _doc.Settings.SegmentLength);

        double next = SegmentLayout.NextBreakpoint(_doc.Points, current, out int nextCount, ActiveTrackClosed);
        if (next > current && nextCount < count)
        {
            _btnOptimizeNext.Text = $"{nextCount} disps ▶";
            _btnOptimizeNext.Enabled = true;
            _tooltipManager.Attach(_btnOptimizeNext, $"Fewer displacements: {nextCount} at scale {Math.Ceiling(next * 100) / 100:0.##}");
        }
        else
        {
            _btnOptimizeNext.Text = "Fully optimized";
            _btnOptimizeNext.Enabled = false;
        }

        double prev = SegmentLayout.PreviousBreakpoint(_doc.Points, current, out int prevCount, ActiveTrackClosed);
        if (prev < current && prevCount > count)
        {
            _btnOptimizePrev.Text = $"◀ {prevCount} disps";
            _btnOptimizePrev.Enabled = true;
            _tooltipManager.Attach(_btnOptimizePrev, $"More displacements: {prevCount} at scale {Math.Floor(prev * 100) / 100:0.##}");
        }
        else
        {
            _btnOptimizePrev.Text = "◀ Max";
            _btnOptimizePrev.Enabled = false;
        }
    }

    /// <summary>Apply one optimization step (next = fewer disps, else more). Undo
    /// bookkeeping is handled by the caller: a single record for a click, or a
    /// BeginBatch/EndBatch pair for a hold.</summary>
    private bool StepOptimization(bool next)
    {
        if (_doc.Points.Count < 2)
        {
            return false;
        }

        double current = Math.Max(1.0, _doc.Settings.SegmentLength);
        if (next)
        {
            double target = SegmentLayout.NextBreakpoint(_doc.Points, current, out _, ActiveTrackClosed);
            if (target <= current)
            {
                return false;
            }

            // Round up so the stored value lands at or past the breakpoint.
            _doc.Settings.SegmentLength = Math.Ceiling(target * 100) / 100;
        }
        else
        {
            double target = SegmentLayout.PreviousBreakpoint(_doc.Points, current, out _, ActiveTrackClosed);
            if (target >= current)
            {
                return false;
            }

            // Round down so the stored value lands at or below the breakpoint.
            _doc.Settings.SegmentLength = Math.Max(1.0, Math.Floor(target * 100) / 100);
        }

        _doc.NotifyChanged();
        return true;
    }

    private void StartOptRepeat(bool next)
    {
        if (_repeatActive)
        {
            return;
        }

        // Don't start (or record a no-op undo) if there is no step to take.
        if (_doc.Points.Count < 2)
        {
            return;
        }

        double current = Math.Max(1.0, _doc.Settings.SegmentLength);
        bool canStep = next
            ? SegmentLayout.NextBreakpoint(_doc.Points, current, out _, ActiveTrackClosed) > current
            : SegmentLayout.PreviousBreakpoint(_doc.Points, current, out _, ActiveTrackClosed) < current;
        if (!canStep)
        {
            return;
        }

        _repeatActive = true;
        _repeatDirectionNext = next;

        // Commit any in-progress batch (e.g. a still-focused numeric editor) so the
        // optimization steps become their own independent undo unit.
        _undo.EndBatch();
        _undo.BeginBatch();
        StepOptimization(next);
        _repeatTimer.Interval = RepeatDebounceMs;
        _repeatTimer.Start();
    }

    private void OnOptRepeatTick(object sender, EventArgs e)
    {
        _repeatTimer.Interval = RepeatIntervalMs;
        if (!StepOptimization(_repeatDirectionNext))
        {
            StopOptRepeat();
        }
    }

    private void StopOptRepeat()
    {
        if (!_repeatActive)
        {
            return;
        }

        _repeatActive = false;
        _repeatTimer.Stop();
        _undo.EndBatch();
        UpdateUndoButtons();
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
        item.SubItems[6].Text = p.Thickness.ToString("0.##");
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
                if (ActiveControl is TextBox)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                RemovePoint();
                return true;
            case Keys.Control | Keys.F:
                FrameAll();
                return true;
            case Keys.Control | Keys.G:
                Generate();
                return true;
            case Keys.OemOpenBrackets:
                if (ActiveControl is TextBox)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                ChangeGridSnap(-1);
                return true;
            case Keys.OemCloseBrackets:
                if (ActiveControl is TextBox)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                ChangeGridSnap(1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ChangeGridSnap(int direction)
    {
        int currentIndex = SnapIndex(_doc.Settings.Snap);
        int newIndex = Math.Clamp(currentIndex + direction, 0, 9);
        if (newIndex == currentIndex)
        {
            return;
        }

        _undo.RecordSingle();
        _cboSnap.SelectedIndex = newIndex;
        UpdateUndoButtons();
    }

    // ---------------------------------------------------------------- output

    private void Generate()
    {
        bool anyTrackHasEnoughPoints = false;
        foreach (Track track in _doc.Tracks)
        {
            if (track.Points.Count >= 2)
            {
                anyTrackHasEnoughPoints = true;
                break;
            }
        }

        if (!anyTrackHasEnoughPoints)
        {
            MessageBox.Show(this, "Add at least two control points to a track.", "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using SaveFileDialog dlg = new SaveFileDialog
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

    // Experimental brush export (deprecated). Uncomment this method, the
    // toolbar button in BuildToolStrip(), and remove the [Obsolete] markers
    // in BrushSegment / RoadGenerator to re-enable.
    /*
    private void GenerateBrushes()
    {
        if (_doc.Points.Count < 2)
        {
            MessageBox.Show(this, "Add at least two control points first.", "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "Hammer Files (.vmf)|*.vmf",
            FileName = "road_brushes.vmf",
            Title = "Generate road brush VMF"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            string vmf = RoadGenerator.GenerateBrushes(_doc);
            System.IO.File.WriteAllText(dlg.FileName, vmf);
            MessageBox.Show(this, $"Wrote {dlg.FileName}", "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Generation failed:\n" + ex.Message, "RoadGen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    */

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
        int distance = Math.Max(50, width - 460);
        _split.SplitterDistance = distance;
        _split.Panel1MinSize = Math.Min(380, _split.SplitterDistance);
        _split.Panel2MinSize = Math.Min(360, width - _split.SplitterDistance);

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
