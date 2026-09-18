using System;
using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SolidFolderIcon;

internal sealed class SolutionFolderIconManager : IVsSolutionEvents, IDisposable
{
    private static readonly Guid SolutionFolderTypeGuid = new("2150E333-8FDC-42A3-9474-1A3956D46DE8");
    private static readonly Guid VirtualFolderTypeGuid = new("6BB5F8F0-4483-11D3-8BCF-00C04F8EC28C");

    private readonly IVsSolution solution;
    private readonly IVsHierarchyItemManager hierarchyItemManager;
    private readonly uint solutionEventsCookie;
    private bool disposed;

    // Reflection cached access to Microsoft.VisualStudio.PlatformUI.HierarchyItem
    private static FieldInfo? iconMonikerField;
    private static FieldInfo? expandedIconMonikerField;
    private static PropertyInfo? iconMonikerProperty;
    private static PropertyInfo? expandedIconMonikerProperty;
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

        // 1. Scan the solution hierarchy itself, because top-level Solution Folders
        // are children of the solution root hierarchy (IVsSolution as IVsHierarchy).
        if (solution is IVsHierarchy solutionHierarchy)
        {
            ScanHierarchy(solutionHierarchy, VSConstants.VSITEMID_ROOT);
        }

        // 2. Enumerate any solution folder projects or nested hierarchies
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
            if (typeGuid == SolutionFolderTypeGuid || typeGuid == VirtualFolderTypeGuid)
            {
                return true;
            }
        }

        if (itemId != VSConstants.VSITEMID_ROOT &&
            ErrorHandler.Succeeded(hierarchy.GetGuidProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_TypeGuid, out var rootTypeGuid)))
        {
            if (rootTypeGuid == SolutionFolderTypeGuid || rootTypeGuid == VirtualFolderTypeGuid)
            {
                return true;
            }
        }

        if (ErrorHandler.Succeeded(hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_TypeName, out var typeNameObj)) && typeNameObj is string typeName)
        {
            if (typeName.IndexOf("Solution Folder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Folder", StringComparison.OrdinalIgnoreCase) >= 0)
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
            var type = hierarchyItem.GetType();
            while (type != null && type != typeof(object))
            {
                if (iconMonikerField == null)
                {
                    iconMonikerField = type.GetField("_iconMoniker", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                                       ?? type.GetField("iconMoniker", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                }

                if (expandedIconMonikerField == null)
                {
                    expandedIconMonikerField = type.GetField("_expandedIconMoniker", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                                               ?? type.GetField("expandedIconMoniker", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                }

                if (iconMonikerProperty == null)
                {
                    var prop = type.GetProperty("IconMoniker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop?.CanWrite == true)
                    {
                        iconMonikerProperty = prop;
                    }
                }

                if (expandedIconMonikerProperty == null)
                {
                    var prop = type.GetProperty("ExpandedIconMoniker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop?.CanWrite == true)
                    {
                        expandedIconMonikerProperty = prop;
                    }
                }

                if (raisePropertyChangedMethod == null)
                {
                    raisePropertyChangedMethod = type.GetMethod("RaisePropertyChanged", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                                                 ?? type.GetMethod("OnPropertyChanged", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                }

                type = type.BaseType;
            }
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

        var identity = item.HierarchyIdentity;
        bool isFolder = false;

        if (identity.NestedHierarchy != null && IsSolutionFolder(identity.NestedHierarchy, identity.NestedItemID))
        {
            isFolder = true;
        }
        else if (identity.Hierarchy != null && IsSolutionFolder(identity.Hierarchy, identity.ItemID))
        {
            isFolder = true;
        }

        if (!isFolder)
        {
            return;
        }

        if (!FolderIconMonikers.TryGetSolutionFolderMonikers(out var closedMoniker, out var openMoniker))
        {
            return;
        }

        EnsureReflection(item);

        try
        {
            bool changed = false;

            if (iconMonikerField != null)
            {
                var currentIcon = (ImageMoniker)iconMonikerField.GetValue(item);
                if (currentIcon.Guid != closedMoniker.Guid || currentIcon.Id != closedMoniker.Id)
                {
                    iconMonikerField.SetValue(item, closedMoniker);
                    changed = true;
                }
            }
            else if (iconMonikerProperty != null)
            {
                var currentIcon = (ImageMoniker)iconMonikerProperty.GetValue(item);
                if (currentIcon.Guid != closedMoniker.Guid || currentIcon.Id != closedMoniker.Id)
                {
                    iconMonikerProperty.SetValue(item, closedMoniker);
                    changed = true;
                }
            }

            if (expandedIconMonikerField != null)
            {
                var currentExpanded = (ImageMoniker)expandedIconMonikerField.GetValue(item);
                if (currentExpanded.Guid != openMoniker.Guid || currentExpanded.Id != openMoniker.Id)
                {
                    expandedIconMonikerField.SetValue(item, openMoniker);
                    changed = true;
                }
            }
            else if (expandedIconMonikerProperty != null)
            {
                var currentExpanded = (ImageMoniker)expandedIconMonikerProperty.GetValue(item);
                if (currentExpanded.Guid != openMoniker.Guid || currentExpanded.Id != openMoniker.Id)
                {
                    expandedIconMonikerProperty.SetValue(item, openMoniker);
                    changed = true;
                }
            }

            if (changed && raisePropertyChangedMethod != null)
            {
                var parameters = raisePropertyChangedMethod.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
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
