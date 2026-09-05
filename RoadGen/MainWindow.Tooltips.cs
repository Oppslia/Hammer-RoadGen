using RoadGen.UI;

namespace RoadGen;

/// <summary>
/// Tooltip wiring for the whole form. This is a partial of MainForm so it can reach
/// the private controls, while keeping the main MainForm.cs free of tooltip noise.
/// All hint text lives here in one place; the reusable <see cref="TooltipManager"/>
/// decides whether a control uses ToolTipText (toolbar) or SetToolTip (form).
/// </summary>
public sealed partial class MainWindow
{
    private readonly TooltipManager _tooltipManager = new TooltipManager();

    private void ApplyAllToolTips()
    {
        // ---- Toolbar ----
        _tooltipManager.Attach(_btnOpen, "Open a track (.trk) file");
        _tooltipManager.Attach(_btnSave, "Save the current track to its file");
        _tooltipManager.Attach(_btnSaveAs, "Save the current track to a new file");
        _tooltipManager.Attach(_btnImport, "Import road geometry from a VMF");
        _tooltipManager.Attach(_btnNew, "Start a new empty road");
        _tooltipManager.Attach(_btnAddPoint, "Add a control point (Ctrl+A)");
        _tooltipManager.Attach(_btnRemovePoint, "Remove the selected control point (Del)");
        _tooltipManager.Attach(_btnMoveUp, "Move the selected point later in the list");
        _tooltipManager.Attach(_btnMoveDown, "Move the selected point earlier in the list");
        _tooltipManager.Attach(_btnFrame, "Fit the road to the view (Ctrl+F)");
        _tooltipManager.Attach(_btnUndo, "Undo (Ctrl+Z)");
        _tooltipManager.Attach(_btnRedo, "Redo (Ctrl+Y)");
        _tooltipManager.Attach(_btnGenerate, "Export the road to a VMF (Ctrl+G)");
        _tooltipManager.Attach(_gridCombo, "Grid interval (HU)");
        _tooltipManager.Attach(_btnSnap, "Toggle snapping points to the grid");
        _tooltipManager.Attach(_btnTextures, "Show the imported layout textured with its game materials");
        _tooltipManager.Attach(_btnLayoutGrid, "Show the imported layout's wireframe grid (turn off to view just the textures)");
        _tooltipManager.Attach(_btnHideTools, "Hide imported tool-texture brushes (tools/* like clip/skip/areaportal) — on by default; uncheck to show them");

        // ---- Road settings ----
        _tooltipManager.Attach(_cboPower, "Displacement power (higher = denser grid)");
        _tooltipManager.Attach(_txtMaterial, "Material applied to every generated face — click or use … to browse installed game materials");
        _tooltipManager.Attach(_btnBrowseMaterial, "Browse installed game materials");
        _tooltipManager.Attach(_btnBrowseFeatureMaterial, "Browse installed game materials for this strip");
        _tooltipManager.Attach(_numTexScale, "Texture scale written to the displacement face");
        _tooltipManager.Attach(_cboLightmap, "Lightmap scale for every face");
        _tooltipManager.Attach(_cboSnap, "Grid snap interval (HU)");
        _tooltipManager.Attach(_chkSolidLeft, "Generate the left side wall");
        _tooltipManager.Attach(_chkSolidRight, "Generate the right side wall");
        _tooltipManager.Attach(_chkSolidBottom, "Generate the bottom face");

        _tooltipManager.Attach(_chkEnableJoining, "Allow this road to weld/join with others at shared endpoints");
        _tooltipManager.Attach(_chkShowDisps, "Overlay the displacement segment layout on the road");
        _tooltipManager.Attach(_chkShowSidewalkDisps, "Overlay the displacement segments of sidewalk/guardrail strips");

        // ---- Layers ----
        _tooltipManager.Attach(_btnAddLayer, "Add a new road layer");
        _tooltipManager.Attach(_btnRemoveLayer, "Remove the selected layer");
        _tooltipManager.Attach(_btnRenameLayer, "Rename the selected layer");
        _tooltipManager.Attach(_btnDuplicateLayer, "Duplicate the selected layer");
        _tooltipManager.Attach(_btnMergeLayer, "Join the welded roads in this chain into one layer");
        _tooltipManager.Attach(_btnLayerUp, "Move the selected layer up");
        _tooltipManager.Attach(_btnLayerDown, "Move the selected layer down");

        // ---- Edge features ----
        _tooltipManager.Attach(_btnAddFeature, "Add a sidewalk or guardrail strip");
        _tooltipManager.Attach(_btnRemoveFeature, "Remove the selected strip");
        _tooltipManager.Attach(_cboFeatureKind, "Kind of edge feature: sidewalk or guardrail");
        _tooltipManager.Attach(_cboFeatureSide, "Which side of the road the strip sits on");
        _tooltipManager.Attach(_numFeatureOffset, "Distance from the road edge to the strip");
        _tooltipManager.Attach(_numFeatureWidth, "Strip width");
        _tooltipManager.Attach(_numFeatureBottomZ, "Strip bottom height above the road surface");
        _tooltipManager.Attach(_numFeatureTopZ, "Strip top height above the road surface");
        _tooltipManager.Attach(_numFeatureBank, "Cross-slope (bank) of the strip in degrees");

        // ---- Cordon ----
        _tooltipManager.Attach(_chkCordonEdit, "Arm the cordon tool, then in a 2D view drag inside the box to move it (grid-snapped, starts as a 5k x 5k box at the origin) or drag a corner handle to resize it. Clicking empty space does not redraw the box; use Fit to map to size it to the layout");
        _tooltipManager.Attach(_chkCordonActive, "Turn cordoning on: only the imported layout inside the red box is drawn, and only tracks inside it are exported to the VMF");
        _tooltipManager.Attach(_btnCordonFit, "Re-seed the cordon box to the whole imported layout");
    }
}
