# PAK-Extractor

A fork of [Wildenhaus/IndexV2](https://github.com/Wildenhaus/IndexV2) (based on release
[`v0.4.0.2-Hotfix2`](https://github.com/Wildenhaus/IndexV2/releases/tag/v0.4.0.2-Hotfix2))
that can open **a PAK file you pick yourself**, without needing a game installation path.

Index is a tool for Halo: Combat Evolved Anniversary, Halo 2 Anniversary and Warhammer 40k: Space Marine 2
that extracts textures, models, and more. All credit for the original tool goes to Haus and the IndexV2 contributors.

## What's different in this fork

Upstream Index only works on a scanned game directory: you point it at an install folder, it identifies the
game, and then loads every archive under that folder. This fork adds a second way in:

| How | What it does |
| --- | --- |
| **Open PAK File** button in the launcher | Pick one or more archive files. Only those files are loaded. |
| **Drag and drop** onto the launcher window | Same as above. |
| **Command line** / "Open with" / drop onto the exe | `Index.App.exe path\to\resources.pak [more.pak ...]` skips the launcher and opens the files directly. |

The game profile is chosen automatically from the file extension:

| Extension | Game profile |
| --- | --- |
| `.pak` | Warhammer 40k: Space Marine 2 |
| `.pck` | Halo 2: Anniversary |
| `.s3dpak`, `.ipak` | Halo: Combat Evolved Anniversary |

Files you open are added to the launcher list, so you can open them again later with a double-click.
Scanning a whole game directory still works as before.

### Things to know

- If you pick several files, they must all belong to the same game.
- A file you pick yourself is always loaded, even ones the profile would normally skip during a directory scan
  (e.g. the Halo CEA multiplayer stub paks).
- Some assets reference data stored in *other* archives, such as a model whose textures are in a different pak.
  With only one archive loaded, those references can't be resolved. Select every archive you need together, or
  scan the game directory.

## Download

GitHub builds the app automatically on every change (see the **Build** workflow under the **Actions** tab).

- **Latest build:** open **Actions → Build**, click the newest green run, and download **PAK-Extractor-win-x64**
  from the *Artifacts* section at the bottom of the page. Unzip it and run `Index.App.exe`.
- **Releases:** pushing a tag that starts with `v` (e.g. `v0.4.0.2-pak1`) publishes the build as a zip on the
  **Releases** page.

The app needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64) installed.

## Building

Requirements: Windows, the .NET 8 SDK, and Visual Studio 2022 (or `dotnet build`).

The game profiles reference [LibSaber](https://github.com/Wildenhaus/LibSaber) as a **sibling directory**, so
clone both next to each other:

```
git clone https://github.com/Wildenhaus/LibSaber
git clone https://github.com/0x686F66666D65696572/PAK-Extractor
```

```
<parent>/
  LibSaber/
  PAK-Extractor/
```

Then open `Index.sln` and build, or run `dotnet build Index.sln -c Release`.

## Keeping up with upstream

This repository keeps the upstream git history, so newer IndexV2 releases can be merged in:

```
git remote add upstream https://github.com/Wildenhaus/IndexV2
git fetch upstream --tags
git merge <upstream-tag>
```
