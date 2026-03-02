# Fallout 4 Shader Permutation System

## Full Engineering Reconstruction (v1.0)

------------------------------------------------------------------------

# 1. Project Objective

Reverse engineer the Fallout 4 shader permutation system by:

-   Decoding SPF1/SPF2 bitfields
-   Correlating NIF shader properties with BGSM/BGEM materials
-   Building a deterministic permutation index
-   Classifying permutations by asset class
-   Validating render correctness structurally
-   Producing strict and statistical validation models

------------------------------------------------------------------------

# 2. Core Architecture

Final engine permutation determined by:

Permutation = BlockType \| ShaderType \| SPF1 \| SPF2

Sources: - NIF → BSLightingShaderProperty / BSEffectShaderProperty -
BGSM/BGEM → material configuration layer

------------------------------------------------------------------------

# 3. Data Extraction Pipeline

## 3.1 NIF Extraction

``` powershell
dotnet .\bin\Release\net8.0\NifCli.dll list `
  --in "C:\Fallout4\Data\Meshes\Landscape\Trees\Cedar01.nif" `
  --show-enums |
  Set-Content .\enum_dump.txt -Encoding UTF8
```

## 3.2 Bulk Mesh Dump

``` powershell
$root = "C:\Fallout4\Data\Meshes"
Get-ChildItem $root -Recurse -Filter *.nif | ForEach-Object {
    dotnet .\NifCli.dll list --in $_.FullName --json
} | Set-Content mesh_dump.jsonl
```

## 3.3 BGSM Inspection

``` powershell
$cli = ".\MaterialCli.exe"
& $cli list --in "<path_to_bgsm>" |
  Select-String "AlphaBlendMode|AlphaTestRef|AlphaTest|ZBufferWrite|ZBufferTest|TwoSided"
```

------------------------------------------------------------------------

# 4. Bitfield Engineering

## 4.1 Verified SPF1 Mappings

  Bit   Meaning
  ----- -------------------------------------
  0     HasSpecular
  1     Skinned
  3     HasVertexAlpha
  7     HasEnvironmentMapping
  17    HasEyeEnvironmentMapping
  21    SkinTint discriminator
  22    Emissive
  29    External Emittance
  31    Parallax/Backlight/Rimlight cluster

## 4.2 SPF2 Observations

  Bit   Likely Meaning
  ----- ----------------------------
  0     ZBufferWrite
  5     ZBufferTest
  7     DoubleSided
  4     Alpha-related
  30    External emittance support

------------------------------------------------------------------------

# 5. Statistical Correlation Method

For each bit:

-   P(feature=true \| bit=1)
-   Jaccard index

Perfect mappings identified when both metrics = 1.

------------------------------------------------------------------------

# 6. Asset Class Inference

Derived from mesh path patterns:

  Class          Pattern
  -------------- -------------------------
  Trees          Landscape`\Trees`{=tex}
  Architecture   Architecture\\
  Actors         Actors\\
  Decals         Decals\\
  Other          Fallback

Tracked:

-   Occurrences
-   Unique NIFs
-   DominantClass

------------------------------------------------------------------------

# 7. Validation Model

## 7.1 Strict Mode

Engine invariant enforcement:

-   SkinTint must contain bit21
-   Trees must not contain Skinned or SkinTint
-   EnvironmentMap must contain bit7
-   Actor shaders must not appear in decals

## 7.2 Relative Mode

-   Statistical outlier detection
-   Rare flag combinations
-   Cross-class contamination detection

------------------------------------------------------------------------

# 8. Editing Templates

## 8.1 Edit BGSM

``` powershell
& $cli set --in "<path_to_bgsm>" --field TwoSided --value true
& $cli save --in "<path_to_bgsm>"
```

## 8.2 Edit NIF Shader Flags

``` powershell
dotnet .\NifCli.dll set `
  --in "<path_to_nif>" `
  --field ShaderFlags_F4SPF1 `
  --value <new_value>

dotnet .\NifCli.dll save --in "<path_to_nif>"
```

## 8.3 Bit Inspection Helper

``` powershell
function ShowBits($num) {
  for ($i=0; $i -lt 32; $i++) {
    if ($num -band (1 -shl $i)) { $i }
  }
}
```

------------------------------------------------------------------------

# 9. Known Invariants

-   SkinTint ↔ Bit21 (perfect isolation)
-   EnvironmentMap ↔ Bit7
-   VertexAlpha ↔ Bit3
-   Specular ↔ Bit0
-   Emissive ↔ Bit22

------------------------------------------------------------------------

# 10. Engineering Outcomes

-   Deterministic permutation registry
-   Asset-aware shader index
-   Verified bit-to-feature mappings
-   Structural validation engine
-   Cross-domain misuse detection
-   Statistical deviation model

------------------------------------------------------------------------

# 11. Future Directions

-   Permutation risk scoring
-   DLC delta automation
-   GPU stage modeling
-   Regression testing harness
-   Mod conflict detection

------------------------------------------------------------------------

End of Reconstruction
