# One-off: rename ambiguous "Feature"/"feature" identifiers that denote the
# sidewalk/guardrail EDGE FEATURE to an "Edge"/"edge" (or neutral) spelling.
# Whole-token only (regex \b...\b) so nothing inside EdgeFeature/ChainFeature or
# plain English "feature" is touched.

$pairs = @()

# ---- RoadModel / TrackFile / UndoManager settings: per-edge increment settings ----
foreach ($sfx in 'UseGridOffset','UseGridWidth','UseGridBottomZ','UseGridTopZ','UseGridBank',
                 'CustomOffset','CustomWidth','CustomBottomZ','CustomTopZ','CustomBank') {
    $pairs += ,@("FeatureInc$sfx", "EdgeInc$sfx")
}

# ---- MainWindow UI controls (edge-feature editor) ----
foreach ($n in 'Offset','Width','BottomZ','TopZ','Bank') {
    $pairs += ,@("_numFeature$n", "_numEdge$n")
    $pairs += ,@("_chkFeatureInc$n", "_chkEdgeInc$n")
    $pairs += ,@("_numFeatureInc$n", "_numEdgeInc$n")
}
$pairs += ,@('_btnBrowseFeatureMaterial', '_btnBrowseEdgeMaterial')
$pairs += ,@('_lstFeatures', '_lstEdges')
$pairs += ,@('_lstFeaturePoints', '_lstEdgePoints')
$pairs += ,@('_btnAddFeature', '_btnAddEdge')
$pairs += ,@('_btnRemoveFeature', '_btnRemoveEdge')
$pairs += ,@('_cboFeatureKind', '_cboEdgeKind')
$pairs += ,@('_cboFeatureSide', '_cboEdgeSide')
$pairs += ,@('_txtFeatureMaterial', '_txtEdgeMaterial')
$pairs += ,@('_chkFeatureBottom', '_chkEdgeBottom')
$pairs += ,@('_chkFeatureInner', '_chkEdgeInner')
$pairs += ,@('_chkFeatureOuter', '_chkEdgeOuter')

# ---- MainWindow local UI panels/vars ----
$pairs += ,@('featureListHost', 'edgeListHost')
$pairs += ,@('featureActionColumn', 'edgeActionColumn')
$pairs += ,@('featureFaceToggleRow', 'edgeFaceToggleRow')
$pairs += ,@('featureInputs', 'edgeInputs')
$pairs += ,@('featureMaterialFieldHost', 'edgeMaterialFieldHost')

# ---- Methods / helpers ----
$pairs += ,@('RefreshFeatureList', 'RefreshEdgeList')
$pairs += ,@('ApplyFeatureFromEditor', 'ApplyEdgeFromEditor')
$pairs += ,@('ApplyFeatureIncrementsFromControls', 'ApplyEdgeIncrementsFromControls')
$pairs += ,@('LoadFeaturePointIntoEditors', 'LoadEdgePointIntoEditors')
$pairs += ,@('LoadFeatureIncrementsIntoControls', 'LoadEdgeIncrementsIntoControls')
$pairs += ,@('FeatureSummary', 'EdgeSummary')

# ---- Shared row/increment builders used by BOTH the road point editor and the edge
#      feature editor -> neutral names (they are not edge-specific) ----
$pairs += ,@('AddFeatureSettingRow', 'AddIncrementRow')
$pairs += ,@('BuildFeatureIncrementCell', 'BuildIncrementCell')

# ---- Viewport overlay of edge-feature displacement segments ----
$pairs += ,@('ShowFeatureSegments', 'ShowEdgeSegments')
$pairs += ,@('DrawFeatureSegments', 'DrawEdgeSegments')

$files = Get-ChildItem -Path 'RoadGen' -Recurse -Filter '*.cs'
if (Test-Path 'RoadGen.Tests') { $files += Get-ChildItem -Path 'RoadGen.Tests' -Recurse -Filter '*.cs' }

foreach ($f in $files) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    $changed = $text
    foreach ($pair in $pairs) {
        $old = [regex]::Escape($pair[0])
        $changed = [regex]::Replace($changed, "\b$old\b", { param($m) $pair[1] })
    }
    if ($changed -ne $text) {
        [System.IO.File]::WriteAllText($f.FullName, $changed, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output ("updated " + $f.FullName)
    }
}
Write-Output "done"
