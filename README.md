# Mega Batch Renamer

Unity Editor tool for batch renaming:
- Scene GameObjects (including nested objects)
- Project assets and folders

## Features

- Live preview before apply
- Tokens:
  - `$&` original name
  - `$n`, `$nn`, `$nnn` ascending counter
  - `$N`, `$NN`, `$NNN` descending counter
  - `$TT` all matching type tokens
  - `$?Type?` conditional type token
- Quick actions:
  - `UPPER`
  - `lower`
  - `camelCase`
  - `camel_Case`
- Type/asset filters with quick toggles
- Undo support for scene object renames

## Install via Git URL

In Unity Package Manager:
1. `Window > Package Manager`
2. `+` button > `Add package from git URL...`
3. Use:

```text
https://github.com/gismalink/megabatchrenamer.git
```

## Open Tool

`Tools > BatchRenamer`

## Package Layout

- `Editor/MegaBatchRenamer.cs`
- `Editor/MegaBatchRenamer.Editor.asmdef`
