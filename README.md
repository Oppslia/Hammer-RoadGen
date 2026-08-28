# RoadGen

A Windows desktop tool that builds winding 3D roads out of Hammer **displacements**
and exports them as a Source `.vmf` file.

<img width="2559" height="1391" alt="image" src="https://github.com/user-attachments/assets/d6ade8e2-2821-476f-b95e-592d97bbd5f5" />


---

<img width="1162" height="647" alt="image" src="https://github.com/user-attachments/assets/99f0dcf2-8c29-46c6-9a2d-0c254b40c2b6" />


---

<img width="678" height="707" alt="image" src="https://github.com/user-attachments/assets/3a99b4eb-245e-44a2-bba6-1f39a1b7955e" />

---

<img width="1490" height="1081" alt="image" src="https://github.com/user-attachments/assets/5938ebfb-529a-4aab-bad7-5e4474a71665" />

You place control points on a 3D curve, give each one a width and a bank (roll)
angle, and RoadGen emits a chain of displacement brushes whose top faces follow the
curve exactly. Adjacent segments share identical boundary vertices, so the road
sews together with **no seams** in Hammer.

## What it does

- **Catmull-Rom spline** centerline through your control points — the road passes
  exactly through every point, so joining curves together is just adding points.
- **Full 3D**: curves can go left/right, up/down, or both at once
  (up+left, down+right, straight+left, etc.).
- **Banking**: each point has a roll angle, applied with a twist-free
  rotation-minimizing frame so the road banks smoothly into turns.
- **Variable width** interpolated between points.
- **Displacement output** that matches the classic Twister convention: normals and
  distances are measured from a planar parallelogram base face, so Hammer
  reconstructs each vertex as `base + normal * distance`.

## Requirements

- Windows
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
  (targets `net9.0-windows`)

## Build & run

```powershell
cd RoadGen
dotnet run
```

## Using the tool
It is highly recommended to save your progress as a trackfile due to the problematic vertex drift hammer is known for. 
This track file tells RoadGen exactly how to build the road you made, so you don't have to worry about losing your progress if your seams become misaligned later while building your map.
Just regenerate your road and place it in your map again!

Importing vmfs is a little jank as of now, so I would always advise to save the road as a .trk file, but you can certainly import a road from a RoadGen generated vmf.

The window is laid out like Hammer:

- **3D** — perspective view. Left-drag to orbit, right/middle-drag to pan,
  wheel to zoom.
- **Top (X/Y)**, **Front (X/Z)**, **Side (Y/Z)** — orthographic views.
  - **Ctrl+click** to add a point.
  - **Click** a point to select it, **drag** to move it.
  - Right/middle-drag to pan, wheel to zoom.

On the right:

- **Control Points** list — select, reorder (Move Up / Move Down), and edit
  X / Y / Z / Width / Bank.
- **Road Settings**:
  - **Power** — displacement subdivision (2, 3 or 4).
  - **Material** — the face material (texture).
  - **Thickness** — brush depth below the road surface.
  - **Segment length** — target length per displacement; smaller = smoother
    curves but more displacements.
  - **Texture scale / Lightmap scale** — face mapping values.
  - **Grid snap** — snap coordinates to a power-of-two grid (0 disables).

Click **Generate VMF...** to write the map.

# Make sure to select all of the road objects in hammer, then open the material **Face Edit Sheet** and click **Fit**. All your textures should be aligned now!

## Files & workflow

RoadGen uses two file types, and they play different roles:

- **`.trk` — RoadGen track (your working file).**
  A small JSON document that stores every control point (X/Y/Z, width, bank) and
  all road settings. This is the file you keep and edit. Save early, save often.

- **`.vmf` — Hammer map (the final product).**
  The compiled road as displacement brushes. This is what you load into Hammer
  to compile and play. It does not contain your control points.

**Workflow:** keep the `.trk` as the source of truth, tweak it, and export a new
`.vmf` whenever you want to test in Hammer.

Toolbar file commands:

- **Open Track...** / **Save Track** / **Save Track As...** — read/write `.trk`.
- **Import VMF...** — rebuild control points from a displacement `.vmf`.
- **Generate VMF...** — export the current road as `.vmf`.

### A note on importing VMFs

You can import a displacement VMF (for example one produced by RoadGen or Twister)
and keep working with it — but the result will have **many more control points**
than the curve you originally drew.

The VMF stores the *subdivided* displacement geometry, not the original control
points, so the importer reconstructs the road centerline by sampling every
displacement. Straight stretches are then collapsed, but a curved road still
imports with one point roughly per displacement boundary. Bank angle is not
stored in a VMF, so imported points get bank `0`.

**Bottom line:** use the `.trk` file to preserve your exact editable work. The
VMF is for Hammer; importing it back is a convenience, not a lossless round-trip.

## Project layout

```
RoadGen/
  Program.cs                   app entry point
  MainForm.cs                  main window + point/settings editing
  Core/
    Vec3.cs                    vector math
    CatmullRom.cs              spline evaluation
    RoadModel.cs               RoadPoint / RoadSettings / RoadDocument
    RoadCurve.cs               position/tangent/width/bank + frame transport
    RoadSurface.cs             surface grid sampling + preview mesh
    DisplacementSegment.cs     one displacement brush -> VMF text
    RoadGenerator.cs           full road -> VMF
    VmfWriter.cs               header/footer + number formatting
    TrackFile.cs               .trk save/load (JSON)
    VmfParser.cs               VMF text -> block tree
    VmfImporter.cs             displacement VMF -> control points
  UI/
    Viewport2D.cs              Top / Front / Side orthographic viewport
    Viewport3D.cs              software-rendered perspective viewport

RoadGen.SmokeTest/             console harness that generates a sample VMF
                               and verifies seamless stitching (seam delta == 0)
```

## How the displacement math works

For each displacement segment the generator samples the road surface on a
`(2^power + 1) x (2^power + 1)` grid. It then:

1. Anchors a **planar parallelogram** base face at three surface corners
   (`A = start`, `B = end-left`, `C = end-right`; the fourth corner is Hammer's
   implied `D = A + C - B`).
2. Computes, per vertex, the vector from the base face to the real surface point:
   `normal = normalize(surface - base)`, `distance = |surface - base|`.
3. Writes those normals/distances into the `dispinfo` block.

Because consecutive segments sample the same spline at their shared boundary with
the same transported frame, their edge vertices are bit-identical — the generator
asserts this in the smoke test (`Seam max vertex delta: 0`).

## Notes

- This is **not** Twister — it is a purpose-built curve/road generator. Twister's
  decompiled sources (`Twister.exe_Decompiler.com`) were used only as a reference
  for the VMF displacement format and its normal/distance convention.
- The 3D view is software-rendered with GDI+ (no GPU dependency). A hardware
  preview can be swapped in later without touching the generation code.
