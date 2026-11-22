# Changelog

## [1.2.1] - 2026-11-23

The panel, in a dock, so it works while you author.

- `Pixel Art Splats → Hud` opens an `EditorWindow` with the same controls. It targets the selected
  camera, marks the objects dirty and repaints the game view on every change.
- The drawing moved into `PixelArtPanel`, which both the window and the in-game hud call.
- The hud's own `OnGUI` still only redraws in edit mode when the game view gets an event (Unity's
  rule for `ExecuteInEditMode`), which is what the window is for.

## [1.2.0] - 2026-11-23

The browser viewer's settings panel, in the engine.

- `PixelArtHud`: the same look/view panel (pixel size, dither, light, light bands, palette, hard
  pixels, 2x2 supersample, outline, auto orbit), drawn over the game view, editing the same fields.
  H hides it; Q / X / S / E / O flip the switches, the site's keys.
- Ranges follow the site: `pixelSize` 1..8, `exposure` 0.5..2.5, `bands` 0..8.
- The sample camera carries the hud.

## [1.1.0] - 2026-11-23

The demo and the showcase are now the caravan camp, the same world the Godot addon ships.

- `DioImport` reads a `.dio` world (or a raw `.bytes` blob) straight into a splat cloud, through the
  menu or from code. It mirrors x on the way in, because the browser and Godot are right handed and
  Unity is left handed: without that the scene comes out back to front.
- `CaravanDemo` rebuilds the sample from its `.dio`, so the sample has a source.
- Sample: Caravan Camp, 147 769 surfels, 16 colours (replaces Hello Island). `HelloScene` stays, so
  the scene-from-code authoring path still has a worked example.

## [1.0.0] - 2026-11-23

First release.

- Surfel renderer for URP 14: instanced procedural discs from a `StructuredBuffer`, per-splat
  lighting (ambient + sun + up to four point lights + fog), opaque-first two-pass draw.
- Pixel pass as a `ScriptableRendererFeature`: 2× supersampled buffer, coverage vote, palette vote,
  ink outlines, translucent light haze, 4×4 Bayer dithering, point-sampled enlarge.
- Splat cloud assets: 64-byte surfels in a binary blob, opaque/tinted split, no runtime sorting.
- Authoring: signed distance shapes and a surface sampler, baked lighting with sun shadows and
  palette ramps in OKLab, the Hello Island scene, mesh-to-splat baking.
- Editor menus: install the renderer feature, build the demo, bake selected meshes, render a still.
- Sample: Hello Island, 55 682 surfels.
- `Pixel Art Splats → Package → Pack Tarball` to build a distributable, with a guard against assets
  that are missing their `.meta` file.
