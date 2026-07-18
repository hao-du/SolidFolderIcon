using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace SolidFolderIcon;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Solid Folder Icon", "Registers solid folder icons for CPS-style projects.", "1.0")]
[ProvideAutoLoad("{f1536ef8-92ec-443c-9ed7-fdadf150da82}", PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
internal sealed class FolderIconPackage : AsyncPackage
{
    public const string PackageGuidString = "c9c5f0b2-0ca6-4a0b-8f19-2e7d32a6a14f";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        FolderIconImageRegistry.Initialize();
    }
}
