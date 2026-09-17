using Microsoft.VisualStudio.ProjectSystem;

namespace SolidFolderIcon;

internal static class FolderIconMonikers
{
    public static bool TryGetIcons(out ProjectImageMoniker closedProject, out ProjectImageMoniker openProject)
    {
        return FolderIconImageRegistry.TryGetIcons(out closedProject, out openProject);
    }

    public static bool TryGetMonikers(out Microsoft.VisualStudio.Imaging.Interop.ImageMoniker closedMoniker, out Microsoft.VisualStudio.Imaging.Interop.ImageMoniker openMoniker)
    {
        return FolderIconImageRegistry.TryGetMonikers(out closedMoniker, out openMoniker);
    }
}
