using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SolidFolderIcon;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Solid Folder Icon", "Registers solid folder icons for Solution Explorer.", "1.0.13")]
[ProvideAutoLoad("{f1536ef8-92ec-443c-9ed7-fdadf150da82}", PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad("{4646b819-1ae0-4e79-97f4-8a8176fdd664}", PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
internal sealed class FolderIconPackage : AsyncPackage
{
    public const string PackageGuidString = "c9c5f0b2-0ca6-4a0b-8f19-2e7d32a6a14f";

    private SolutionFolderIconManager? solutionFolderIconManager;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        FolderIconImageRegistry.Initialize();

        var solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
        var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        var hierarchyItemManager = componentModel?.GetService<IVsHierarchyItemManager>();

        if (solution != null && hierarchyItemManager != null)
        {
            solutionFolderIconManager = new SolutionFolderIconManager(solution, hierarchyItemManager);
        }
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing)
        {
            solutionFolderIconManager?.Dispose();
            solutionFolderIconManager = null;
        }

        base.Dispose(disposing);
    }
}
