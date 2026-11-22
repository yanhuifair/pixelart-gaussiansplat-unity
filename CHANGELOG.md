# Changelog

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
