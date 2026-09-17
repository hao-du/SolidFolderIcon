using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace.VSIntegration.UI;

namespace SolidFolderIcon;

[Export(typeof(INodeExtender))]
[ExportNodeExtender(new[] { "PhysicalTree", "LiveShareFolderView" })]
internal sealed class WorkspaceFolderIconExtender : INodeExtender
{
    public IChildrenSource ProvideChildren(WorkspaceVisualNodeBase parentNode)
    {
        ApplyFolderIcon(parentNode);
        return new ChildrenStylingSource(this, parentNode);
    }

    public IWorkspaceCommandHandler? ProvideCommandHandler(WorkspaceVisualNodeBase node)
    {
        ApplyFolderIcon(node);
        return null;
    }

    internal static void ApplyFolderIcon(WorkspaceVisualNodeBase? node)
    {
        if (node is null)
        {
            return;
        }

        if (node is IFolderNode ||
            node.GetType().Name.IndexOf("Folder", StringComparison.OrdinalIgnoreCase) >= 0 ||
            node.GetType().Name.IndexOf("WorkspaceNode", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (FolderIconMonikers.TryGetMonikers(out var closedMoniker, out var openMoniker))
            {
                node.SetIcon(closedMoniker.Guid, closedMoniker.Id);
                node.SetExpandedIcon(openMoniker.Guid, openMoniker.Id);
            }
        }
    }

    private sealed class ChildrenStylingSource : IChildrenSource
    {
        private readonly WorkspaceVisualNodeBase parentNode;

        public ChildrenStylingSource(INodeExtender extender, WorkspaceVisualNodeBase parentNode)
        {
            Extender = extender;
            this.parentNode = parentNode;
        }

        public INodeExtender Extender { get; }

        public int Order => int.MaxValue;

        public bool ForceExpanded => false;

        public Task<IReadOnlyCollection<WorkspaceVisualNodeBase>> GetCollectionAsync()
        {
            ApplyFolderIcon(parentNode);

            try
            {
                parentNode.ApplyActionOnRealizedNodes(ApplyFolderIcon);
            }
            catch
            {
            }

            return Task.FromResult<IReadOnlyCollection<WorkspaceVisualNodeBase>>(Array.Empty<WorkspaceVisualNodeBase>());
        }

        public void Dispose()
        {
        }
    }
}
