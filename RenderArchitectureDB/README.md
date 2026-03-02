# RenderArchitectureDB (C#) — v1.0 Scaffold

Offline correlation + analysis layer for:
- Permutation Index (CSV)
- FO4ShaderCensus outputs (JSON)

Outputs:
- normalized/perm_table.json
- normalized/shader_table.json
- normalized/session_table.json
- normalized/structural_clusters.json
- correlation/perm_shader_links.json
- exports/*.csv

## Quick start

```powershell
dotnet restore
dotnet run --project .\src\RenderArchitectureDB -- --help

# Normalize inputs
dotnet run --project .\src\RenderArchitectureDB -- normalize --permCsv "<path>\permutation_index.csv" --shaderDb "<path>\FO4ShaderDB" --out "<path>\RenderArchitectureDB_Out"

# Build clusters
dotnet run --project .\src\RenderArchitectureDB -- cluster --in "<out>" --out "<out>"

# Link permutations to shaders (direct observation, sessions)
dotnet run --project .\src\RenderArchitectureDB -- link --in "<out>" --out "<out>"
```

## Determinism rules
- All outputs are stable-sorted.
- perm_id is SHA1(BlockType|ShaderType|SPF1|SPF2).
- schema_version is embedded in every output.
