# TerrainHeightJob

**File:** `TerrainHeightJob.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Implements:** `IJobParallelFor`  
**Burst attribute:** `[BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]`

---

## Purpose

`TerrainHeightJob` is the **Burst-compiled parallel job** that computes a terrain heightmap one texel at a time. Each execution of `Execute(int index)` calculates the world-space position of a single heightmap texel and runs it through the full height pipeline: biome blending → mountain ridges → erosion → rivers.

This job is scheduled and managed by `StreamingTerrainGeneratorJobs`.

---

## Supporting Types

### `BiomeParams` (struct)

Holds the noise parameters for a single biome, passed to the job via a read-only `NativeArray`.

| Field | Type | Description |
|---|---|---|
| `baseHeight` | `float` | Constant height offset added on top of FBM output. |
| `amplitude` | `float` | Vertical scale of the FBM noise. |
| `noiseScale` | `float` | Horizontal frequency of the FBM noise for this biome. |
| `octaves` | `int` | Number of FBM layers. |
| `lacunarity` | `float` | Frequency multiplier between octaves. |
| `gain` | `float` | Amplitude multiplier (decay) between octaves. |

---

## Job Fields

### Input

| Field | Type | Description |
|---|---|---|
| `resolution` | `int` | Width/height of the heightmap in texels. |
| `worldOrigin` | `float2` | World-space XZ origin of this chunk. |
| `terrainSize` | `float2` | World-space XZ dimensions of this chunk. |
| `biomeScale` | `float` | Frequency of the biome selection noise. |
| `biomeCount` | `int` | Number of entries in `biomes`. |
| `mountainRidgeScale` | `float` | Frequency of ridged mountain noise. |
| `mountainRidgeStrength` | `float` | Maximum height added by mountain ridges. |
| `erosionScale` | `float` | Frequency of the erosion gradient noise. |
| `erosionStrength` | `float` | Slope multiplier for erosion intensity. |
| `erosionDepth` | `float` | Maximum height reduction from erosion. |
| `riverScale` | `float` | Frequency of river path noise. |
| `riverWidth` | `float` | `smoothstep` upper bound for river carving. |
| `riverDepth` | `float` | Maximum height reduction inside river channels. |
| `biomes` | `NativeArray<BiomeParams>` (ReadOnly) | Per-biome noise configuration. |

### Output

| Field | Type | Description |
|---|---|---|
| `heights` | `NativeArray<float>` (WriteOnly) | Computed normalized [0, 1] heights, indexed `y * resolution + x`. |

---

## Height Pipeline

```
SampleHeight()
    └─► SampleBiomeHeight()      — blended FBM across biome definitions
    └─► MountainStrength()       — determines how much ridge noise to add
    └─► RidgedNoise()            — adds ridge features in mountain zones
    └─► ApplyErosion()           — subtracts gradient-based erosion
    └─► ApplyRiver()             — carves river channels in low areas
```

### 1. Biome Height — `SampleBiomeHeight()`

A low-frequency Simplex noise value (`biomeScale`) is mapped to a position in the `biomes` array. The two nearest biomes are blended with linear interpolation:

```
biomePos = normalize(noise) * (biomes.Length - 1)
height   = lerp(SampleSingleBiome(biomeA), SampleSingleBiome(biomeB), frac(biomePos))
```

Each biome's height is computed with **Fractional Brownian Motion (FBM)** using `octaves`, `lacunarity`, and `gain` from its `BiomeParams`.

### 2. Mountain Ridges — `MountainStrength()` + `RidgedNoise()`

Mountain strength is determined by how far into the *last two biome indices* the current point falls (`saturate(biomePos - (biomeCount - 2))`). Where strength > 0, a 4-octave ridged noise value is added:

```
ridge = 1 - |snoise(x)|   (repeated and accumulated)
height += ridge * mountainRidgeStrength * mountainStrength
```

### 3. Erosion — `ApplyErosion()`

Computes a finite-difference slope approximation from noise gradients in X and Z:

```
slope = |dNoise/dx| + |dNoise/dz|
erosion = saturate(slope * erosionStrength)
height -= erosion * erosionDepth
```

Higher slopes are eroded more, producing natural-looking weathering on steep terrain.

### 4. Rivers — `ApplyRiver()`

An absolute Simplex noise value determines flow intensity. A `smoothstep` threshold creates a smooth channel where the noise is near zero:

```
flow = |snoise(x, z)|
river = smoothstep(riverWidth, 0, flow)
heightMask = saturate(1 - height * 4)   // Only affects low-lying terrain
height -= river * heightMask * riverDepth
```

The height mask prevents rivers from appearing on mountain tops.

---

## Noise Primitives

### `FBM(x, y, octaves, lacunarity, gain)`
Standard fractional Brownian motion: sums `snoise` contributions at increasing frequencies and decreasing amplitudes.

### `RidgedNoise(x, y)`
Four-octave loop computing `1 - |snoise|` at each layer, producing sharp ridgelines.

Both use `Unity.Mathematics.noise.snoise` (Simplex noise), which is compatible with Burst compilation.

---

## Notes

- All heights are clamped to `[0, 1]` in `Execute()` before writing to the output array.
- The job is safe for parallel execution because each index writes to a unique position in `heights`.
- Switching the Burst attribute to `FloatMode.Fast` may yield a modest speed improvement but could introduce platform-dependent floating-point differences that break seamless chunk borders. Use `FloatMode.Strict` when cross-platform consistency is required.
