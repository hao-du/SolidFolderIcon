# Solid Folder Icon

Tired of the default hollow folder icons in Visual Studio 2026 being hard to see? Solid Folder Icon gives Solution Explorer a cleaner, stronger look by replacing hollow folders with solid folder icons that are easier to spot at a glance.

## What It Is

Solid Folder Icon is a lightweight Visual Studio 2026 extension for SDK-style projects. It changes folder nodes in Solution Explorer from the default hollow style to a solid folder icon.

If your project tree feels washed out, crowded, or just a little too hard to scan, this extension is for you. Install it, reopen your solution, and give your folders the visual weight they deserve.

## Setup

### Install From Visual Studio Marketplace

1. Open Visual Studio.
2. Go to `Extensions` > `Manage Extensions`.
3. Search for `Solid Folder Icon`.
4. Click `Install`.
5. Restart Visual Studio when prompted.

### Install From VSIX

1. Download `SolidFolderIcon.vsix`.
2. Close Visual Studio.
3. Double-click the `.vsix` file.
4. Choose the Visual Studio instance you want to install it into.
5. Restart Visual Studio.

After installation, open an SDK-style project and check Solution Explorer. Folder icons should appear as solid folders.

## Development

To build the extension locally:

1. Clone the repository.
2. Open `SolidFolderIcon.csproj` in Visual Studio.
3. Restore NuGet packages.
4. Build the project in `Release` mode.
5. The installable VSIX is generated as `SolidFolderIcon.vsix`.

Useful files:

- `FolderIconProvider.cs` controls when folder icons are replaced.
- `FolderIconImageRegistry.cs` registers the custom folder images with Visual Studio.
- `Images/SolidFolderClosed.svg` is the closed folder icon.
- `Images/SolidFolderOpen.svg` is the opened folder icon.
- `source.extension.vsixmanifest` contains the VSIX metadata.
