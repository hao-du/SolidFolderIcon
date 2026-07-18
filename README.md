# Solid Folder Icon

This is a starter CPS extension that swaps folder nodes in Solution Explorer to a solid custom folder icon.

## What it does

- Uses `IProjectTreePropertiesProvider` to customize tree node icons.
- Uses SVG folder glyphs inspired by the `assets/icons` structure from the reference project:
  - `Images/SolidFolderClosed.svg`
  - `Images/SolidFolderOpen.svg`
- Packs the assembly as a VSIX MEF component.

## What to know

- This is intentionally scoped to CPS-style projects.
- The folder detection is conservative and heuristic-based so the sample stays safe.
- The provider is ordered late so it can win over other CPS icon customizations.

## Build notes

- Target framework: `.NET Framework 4.8`
- Uses:
  - `Microsoft.VisualStudio.Sdk`
  - `Microsoft.VSSDK.BuildTools`

If restore fails because a package version moved, update the versions in [`SolidFolderIcon.csproj`](./SolidFolderIcon.csproj) to the latest available NuGet versions.

## Next step

Open the project in Visual Studio, restore packages, build, and install the generated VSIX into an experimental instance.
