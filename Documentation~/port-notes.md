# How it works

The whole thing is three ideas. This page walks them and names the files.

## 1. A splat is a surfel

A Gaussian splat is a position, a size along three axes, a rotation and a colour. Here every splat is
a **surfel**: wide in two directions, thin in the third, lying flat on a surface.

- The thin axis is the surface normal, so lighting needs no extra storage.
- Splats sit on a regular spacing `h` and are `0.66 h` wide, so neighbours overlap and the surface has
  no holes (`Surfel.RadiusPerSpacing`).
- Scenes are sampled from signed-distance shapes (`Editor/SdfTools.cs` walks a grid, keeps the cells
  the surface passes through, nudges each point onto the surface with two Newton steps, measures
  occlusion by stepping out along the normal).
- Meshes are scattered over their triangles (`SurfelBaker.FromMeshes`).

## 2. Lighting, baked or per frame

**Scenes: baked** (`Editor/LightBaker.cs`). A scene does not move, so each surfel's light is worked
out once at build time:

1. Sun: diffuse from the key direction times a soft shadow, marched through the scene's distance
   field — darker the closer the ray passes to something.
2. Sky: more light on surfaces facing up, scaled by the sampler's occlusion.
3. Leaves: lit from both sides, only half shadowed, so a canopy glows instead of turning into a dark
   ball.
4. Lamps: fires and lanterns light what they can see (another marched ray), and their warm light may
   pull neighbouring surfaces into their hue.
5. Colour ramps: the lit colour is matched to the palette in OKLab with a penalty for leaving the
   surface's own hue, so a shaded orange becomes a darker orange or a red, never a green that happens
   to be close. Shadows lean cool, light warm.

**Models: every frame** (`Shaders/SurfelSplat.hlsl`, `ShadeSurfel`). Ambient from the normal's y,
one directional light, up to four point lights, distance fog, two-sided and up-facing variants. The
normal travels with the splat, so lighting stays right when the object moves.

Both write colours in **display space** — the space the palette lives in — so the pixel pass never has
to convert anything before matching.

## 3. The pixel pass

`Shaders/PixelArtComposite.shader`, driven by `Runtime/PixelArtSplatFeature.cs`.

The splats are drawn first, into a small buffer: art resolution × 2 (or × 1 with `supersample` off),
`R16G16B16A16F` with a 24-bit depth buffer, cleared to `(0,0,0,0)`.

- **Hard discs.** The Gaussian falloff is off: each splat is a solid disc, fully opaque.
- **Coverage.** An art pixel is the object when at least half of its four samples are solid.
- **Palette by vote.** Each solid sample snaps to its nearest palette colour and the pixel takes the
  colour most of them chose — so a pixel on the border of two materials gets one of them, never their
  average snapped to some third colour. Solid pixels never take the background colour.
- **Outlines.** A solid pixel with the background on one to three sides takes an ink of its own
  colour: darker, but lifted to a minimum brightness so it still reads against a near-black
  background. Isolated dots keep their colour.
- **Translucent light.** Halos, beams and smoke are blended over the background and matched to the
  palette in OKLab while keeping the light's own hue, so a faint gold beam becomes a muted gold band
  rather than a grey that happens to be close. Below about 30% opacity it is left out.
- **Dithering.** Off by default. Above 0 the pass averages the samples, finds the two nearest palette
  colours, and picks one or the other with a 4×4 Bayer pattern in proportion to where the colour sits
  between them.

Then one point-sampled draw enlarges the art to the screen (nearest neighbour: one art pixel, one
square of screen) and converts display space to linear for the camera target.

## How the splats are drawn

No meshes, no material instances:

```
cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 6, instanceCount)
```

- `SV_InstanceID` indexes `StructuredBuffer<SurfelData>` — 64 bytes: `pos`, `axis1`, `axis2`,
  `normal`, `color`, `flags`.
- `SV_VertexID` picks one of six vertices. The two wide axes are the quad's edges, so **the quad is the
  splat's own footprint, projected**. The disc test happens in the fragment on the quad's own `(u,v)`,
  which makes the projected ellipse for free.
- The corners carry their real world position, so depth at a pixel is the depth of the **surfel plane**
  there, not the centre's.
- Splats that project smaller than `minTexels` are grown, otherwise distant surfaces get pinholes.
- Splats facing away are collapsed before rasterising (culling), unless two-sided or a glow.
- Two passes: solid (`Blend Off`, `ZWrite On`), then translucent light
  (`One OneMinusSrcAlpha`, no depth write). The cloud is stored opaque-first so this needs no sorting.

## Colour space

The splat colours, the palette, the vote, the ink, the OKLab matching — all of it is display space,
the same space the hex colours are authored in. Only the final enlarge converts to linear
(`SRGBToLinear`) for the camera target. That way a palette colour goes in and the same colour comes
out, value for value.

The browser original had to un-linearise (`pow 1/2.2`) because Spark writes into a linear target;
here nothing linearises on the way in, so that step is the identity.

## Things that cost time

Recorded so the next person does not pay again.

- **Do not use an `RTHandle` from `RenderingUtils.ReAllocateIfNeeded` for your own intermediate
  buffers.** The colour written into it read back as **depth** (the handle pool handed out a resource
  whose attachments did not match), so the frame came out black. A plain `RenderTexture` plus
  `cmd.SetRenderTarget(rt)` fixed it immediately.
- **No `float3` in a varyings struct.** `float3 color : TEXCOORD1` next to `float alpha : TEXCOORD2`
  mis-linked on Metal: in the fragment, `color` always equalled `SV_POSITION.z`. One `float4` per
  varying behaves predictably. (`SurfelSplat.hlsl`.)
- **Procedural draws need `#pragma target 4.5`** for `SV_InstanceID`.
- **Use `GetFullScreenTriangleVertexPosition` / `GetFullScreenTriangleTexCoord`** rather than building
  clip-space positions by hand: the y axis differs per platform, and hand-rolled triangles end up
  flipped or sampling the wrong row.
- **`[ExecuteAlways]` or the component does not run in edit mode.** `OrbitCamera` without it never had
  `OnEnable` called, so the scene saved with the camera at the origin — inside the island — and every
  render looked like a handful of enormous splats.
- A struct of five `float3` and a `uint` (64 bytes) worked as a `StructuredBuffer` element on Metal,
  but re-verify if you change the backend or the layout.

## Not ported

Particles (embers, fireflies, falling leaves, smoke), skinned animation with dual-quaternion
splat skinning, automatic palettes extracted from a model's colours (k-means), light shaft data, and
the island themes beyond the one the sample uses. The renderer supports translucent light and
two-sided splats, so those are data problems rather than renderer problems.
