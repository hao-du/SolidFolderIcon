using System;
using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SolidFolderIcon;

internal sealed class SolutionFolderIconManager : IVsSolutionEvents, IDisposable
{
    private static readonly Guid SolutionFolderTypeGuid = new("2152E033-0034-406F-BF46-880F05740FFC");

    private readonly IVsSolution solution;
    private readonly IVsHierarchyItemManager hierarchyItemManager;
    private readonly uint solutionEventsCookie;
    private bool disposed;

    // Reflection cached access to Microsoft.VisualStudio.PlatformUI.HierarchyItem
    private static Type? hierarchyItemType;
    private static FieldInfo? iconMonikerField;
    private static FieldInfo? expandedIconMonikerField;
    private static MethodInfo? raisePropertyChangedMethod;
    private static bool reflectionInitialized;

    public SolutionFolderIconManager(IVsSolution solution, IVsHierarchyItemManager hierarchyItemManager)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.solution = solution ?? throw new ArgumentNullException(nameof(solution));
        this.hierarchyItemManager = hierarchyItemManager ?? throw new ArgumentNullException(nameof(hierarchyItemManager));

        // Advise solution events
        ErrorHandler.ThrowOnFailure(this.solution.AdviseSolutionEvents(this, out solutionEventsCookie));

        // Hook hierarchy item manager events
        this.hierarchyItemManager.AfterInvalidateItems += OnAfterInvalidateItems;

        // Scan any already-loaded solution folders
        UpdateAllSolutionFolders();
    }

    private void OnAfterInvalidateItems(object? sender, HierarchyItemEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e?.Item != null)
        {
            TryCustomizeHierarchyItem(e.Item);
        }
    }

    public void UpdateAllSolutionFolders()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var guid = Guid.Empty;
        if (ErrorHandler.Succeeded(solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_ALLPROJECTS, ref guid, out var enumProjects)))
        {
            var hierarchyArray = new IVsHierarchy[1];
            while (ErrorHandler.Succeeded(enumProjects.Next(1, hierarchyArray, out var fetched)) && fetched == 1)
            {
                var hierarchy = hierarchyArray[0];
                if (hierarchy != null)
                {
                    ScanHierarchy(hierarchy, VSConstants.VSITEMID_ROOT);
                }
            }
        }
    }

    private void ScanHierarchy(IVsHierarchy hierarchy, uint itemId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (IsSolutionFolder(hierarchy, itemId))
        {
            if (hierarchyItemManager.TryGetHierarchyItem(hierarchy, itemId, out var item) && item != null)
            {
                TryCustomizeHierarchyItem(item);
            }
        }

        // Check children
        if (ErrorHandler.Succeeded(hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_FirstChild, out var childObj)) && childObj != null)
        {
            if (uint.TryParse(childObj.ToString(), out var childId) && childId != VSConstants.VSITEMID_NIL)
            {
                while (childId != VSConstants.VSITEMID_NIL)
                {
                    // Check if child is a nested hierarchy
                    var nestedGuid = typeof(IVsHierarchy).GUID;
                    if (ErrorHandler.Succeeded(hierarchy.GetNestedHierarchy(childId, ref nestedGuid, out var nestedHierarchyPtr, out var nestedItemId)) &&
                        nestedHierarchyPtr != IntPtr.Zero)
                    {
                        try
                        {
                            var nestedHierarchy = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(nestedHierarchyPtr) as IVsHierarchy;
                            if (nestedHierarchy != null)
                            {
                                ScanHierarchy(nestedHierarchy, nestedItemId);
                            }
                        }
                        finally
                        {
                            System.Runtime.InteropServices.Marshal.Release(nestedHierarchyPtr);
                        }
                    }
                    else
                    {
                        ScanHierarchy(hierarchy, childId);
                    }

                    if (ErrorHandler.Failed(hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_NextSibling, out var nextChildObj)) || nextChildObj == null)
                    {
                        break;
                    }

                    if (!uint.TryParse(nextChildObj.ToString(), out childId))
                    {
                        break;
                    }
                }
            }
        }
    }

    private static bool IsSolutionFolder(IVsHierarchy hierarchy, uint itemId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (ErrorHandler.Succeeded(hierarchy.GetGuidProperty(itemId, (int)__VSHPROPID.VSHPROPID_TypeGuid, out var typeGuid)))
        {
            if (typeGuid == SolutionFolderTypeGuid)
            {
                return true;
            }
        }

        if (ErrorHandler.Succeeded(hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_TypeName, out var typeNameObj)) && typeNameObj is string typeName)
        {
            if (typeName.IndexOf("Solution Folder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureReflection(object hierarchyItem)
    {
        if (reflectionInitialized) return;
        reflectionInitialized = true;

        try
        {
            hierarchyItemType = hierarchyItem.GetType();
            iconMonikerField = hierarchyItemType.GetField("_iconMoniker", BindingFlags.NonPublic | BindingFlags.Instance);
            expandedIconMonikerField = hierarchyItemType.GetField("_expandedIconMoniker", BindingFlags.NonPublic | BindingFlags.Instance);
            raisePropertyChangedMethod = hierarchyItemType.GetMethod("RaisePropertyChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        catch
        {
        }
    }

    private static void TryCustomizeHierarchyItem(IVsHierarchyItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (item?.HierarchyIdentity == null)
        {
            return;
        }

        var hier = item.HierarchyIdentity.NestedHierarchy ?? item.HierarchyIdentity.Hierarchy;
        var itemId = item.HierarchyIdentity.IsNestedItem ? item.HierarchyIdentity.NestedItemID : item.HierarchyIdentity.ItemID;

        if (hier == null || !IsSolutionFolder(hier, itemId))
        {
            return;
        }

        if (!FolderIconMonikers.TryGetMonikers(out var closedMoniker, out var openMoniker))
        {
            return;
        }

        EnsureReflection(item);

        try
        {
            if (iconMonikerField != null && expandedIconMonikerField != null)
            {
                var currentIcon = (ImageMoniker)iconMonikerField.GetValue(item);
                var currentExpanded = (ImageMoniker)expandedIconMonikerField.GetValue(item);

                bool changed = false;
                if (currentIcon.Guid != closedMoniker.Guid || currentIcon.Id != closedMoniker.Id)
                {
                    iconMonikerField.SetValue(item, closedMoniker);
                    changed = true;
                }

                if (currentExpanded.Guid != openMoniker.Guid || currentExpanded.Id != openMoniker.Id)
                {
                    expandedIconMonikerField.SetValue(item, openMoniker);
                    changed = true;
                }

                if (changed && raisePropertyChangedMethod != null)
                {
                    raisePropertyChangedMethod.Invoke(item, new object[] { "IconMoniker" });
                    raisePropertyChangedMethod.Invoke(item, new object[] { "ExpandedIconMoniker" });
                }
            }
        }
        catch
        {
        }
    }

    public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (pHierarchy != null)
        {
            ScanHierarchy(pHierarchy, VSConstants.VSITEMID_ROOT);
        }
        return VSConstants.S_OK;
    }

    public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
    public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
    public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (pRealHierarchy != null)
        {
            ScanHierarchy(pRealHierarchy, VSConstants.VSITEMID_ROOT);
        }
        return VSConstants.S_OK;
    }
    public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
    public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;

    public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        UpdateAllSolutionFolders();
        return VSConstants.S_OK;
    }

    public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
    public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;
    public int OnAfterCloseSolution(object pUnkReserved) => VSConstants.S_OK;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        ThreadHelper.ThrowIfNotOnUIThread();
        if (hierarchyItemManager != null)
        {
            hierarchyItemManager.AfterInvalidateItems -= OnAfterInvalidateItems;
        }

        if (solutionEventsCookie != 0)
        {
            solution.UnadviseSolutionEvents(solutionEventsCookie);
        }
    }
}
