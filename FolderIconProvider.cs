using System;
using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio.ProjectSystem;

namespace SolidFolderIcon;

[Export(typeof(IProjectTreePropertiesProvider))]
[AppliesTo("AspNetCore")]
[Order(int.MaxValue)]
internal sealed class FolderIconProvider : IProjectTreePropertiesProvider
{
    public void CalculatePropertyValues(
        IProjectTreeCustomizablePropertyContext propertyContext,
        IProjectTreeCustomizablePropertyValues propertyValues)
    {
         if (!propertyContext.IsFolder ||
            propertyValues.Flags.Contains(ProjectTreeFlags.Common.ProjectRoot) ||
            !propertyValues.Flags.Contains(ProjectTreeFlags.Common.Folder) &&
            !propertyValues.Flags.Contains(ProjectTreeFlags.Common.VirtualFolder))
        {
            return;
        }

        if (!FolderIconMonikers.TryGetIcons(out var closedIcon, out var openIcon))
        {
            return;
        }

        propertyValues.Icon = closedIcon;
        propertyValues.ExpandedIcon = openIcon;
    }
}
