using Microsoft.VisualStudio.ProjectSystem;

namespace SolidFolderIcon;

internal static class FolderIconMonikers
{
    public static bool TryGetIcons(out ProjectImageMoniker closedProject, out ProjectImageMoniker openProject)
    {
        return FolderIconImageRegistry.TryGetIcons(out closedProject, out openProject);
    }
}
