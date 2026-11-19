// Test helper: asks the Windows shell for the same context menu Explorer shows, lists it, and
// invokes an item, going through the same COM interfaces Explorer uses. C# 5 for Windows PowerShell's Add-Type.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public static class ShellHelper
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attrs);
        void EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, StringBuilder name, uint cch);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, StringBuilder name, uint cch);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string lpTitle;
        public IntPtr lpVerbW;
        public string lpParametersW;
        public string lpDirectoryW;
        public string lpTitleW;
        public int ptInvokeX;
        public int ptInvokeY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MENUITEMINFO
    {
        public int cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgao, out uint attrs);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

    private const uint MIIM_ID = 0x2, MIIM_SUBMENU = 0x4, MIIM_STRING = 0x40, MIIM_FTYPE = 0x100;
    private const uint MFT_SEPARATOR = 0x800;
    private const uint CMF_NORMAL = 0, CMF_EXPLORE = 4;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_FLAG_NO_UI = 0x400;
    private const uint WM_INITMENUPOPUP = 0x117;

    public class Item
    {
        public string Text;
        public uint Id;
        public int Depth;
        public string Path;
    }

    private static IntPtr[] Children(string[] files, out IShellFolder folder, out List<IntPtr> pidls)
    {
        var iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
        pidls = new List<IntPtr>();
        var children = new IntPtr[files.Length];
        folder = null;
        for (int i = 0; i < files.Length; i++)
        {
            IntPtr pidl; uint attrs; IntPtr ppv; IntPtr last;
            SHParseDisplayName(files[i], IntPtr.Zero, out pidl, 0, out attrs);
            SHBindToParent(pidl, ref iidFolder, out ppv, out last);
            if (folder == null) folder = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
            children[i] = last;
            pidls.Add(pidl);
        }
        return children;
    }

    private static IContextMenu MenuFor(string[] files, IntPtr hmenu)
    {
        IShellFolder folder; List<IntPtr> pidls;
        var children = Children(files, out folder, out pidls);
        var iidMenu = new Guid("000214E4-0000-0000-C000-000000000046");
        IntPtr ppv;
        folder.GetUIObjectOf(IntPtr.Zero, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out ppv);
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(ppv);
        int hr = menu.QueryContextMenu(hmenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);
        if (hr < 0) throw new Exception("QueryContextMenu failed: " + hr);
        return menu;
    }

    private static void Walk(IContextMenu menu, IntPtr hmenu, int depth, string parent, List<Item> items)
    {
        var menu2 = menu as IContextMenu2;
        int count = GetMenuItemCount(hmenu);
        for (uint i = 0; i < count; i++)
        {
            var info = new MENUITEMINFO { cbSize = Marshal.SizeOf(typeof(MENUITEMINFO)), fMask = MIIM_ID | MIIM_SUBMENU | MIIM_STRING | MIIM_FTYPE };
            if (!GetMenuItemInfo(hmenu, i, true, ref info)) continue;
            if ((info.fType & MFT_SEPARATOR) != 0) continue;

            string text = "";
            if (info.cch > 0)
            {
                var buffer = Marshal.AllocHGlobal(((int)info.cch + 2) * 2);
                info.dwTypeData = buffer;
                info.cch++;
                GetMenuItemInfo(hmenu, i, true, ref info);
                text = Marshal.PtrToStringUni(buffer);
                Marshal.FreeHGlobal(buffer);
            }

            string clean = text.Replace("&", "");
            string path = parent == null ? clean : parent + " > " + clean;
            items.Add(new Item { Text = clean, Id = info.wID, Depth = depth, Path = path });

            if (info.hSubMenu != IntPtr.Zero)
            {
                // Submenus can fill themselves in lazily when opened, as they do on hover in Explorer.
                if (menu2 != null) menu2.HandleMenuMsg(WM_INITMENUPOPUP, info.hSubMenu, (IntPtr)i);
                Walk(menu, info.hSubMenu, depth + 1, path, items);
            }
        }
    }

    public static List<Item> List(string[] files)
    {
        var hmenu = CreatePopupMenu();
        var menu = MenuFor(files, hmenu);
        var items = new List<Item>();
        Walk(menu, hmenu, 0, null, items);
        DestroyMenu(hmenu);
        Marshal.ReleaseComObject(menu);
        return items;
    }

    /// <summary>Invokes the first item whose full path contains the given text, as a click in Explorer would.</summary>
    public static string Invoke(string[] files, string pathContains)
    {
        var hmenu = CreatePopupMenu();
        var menu = MenuFor(files, hmenu);
        var items = new List<Item>();
        Walk(menu, hmenu, 0, null, items);
        var target = items.FirstOrDefault(x => x.Path.IndexOf(pathContains, StringComparison.OrdinalIgnoreCase) >= 0 && x.Id >= 1 && x.Id <= 0x7FFF);
        if (target == null) throw new Exception("No menu item matching: " + pathContains);

        var info = new CMINVOKECOMMANDINFOEX();
        info.cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX));
        info.fMask = CMIC_MASK_UNICODE | CMIC_MASK_FLAG_NO_UI;
        info.hwnd = GetDesktopWindow();
        info.lpVerb = (IntPtr)(target.Id - 1);
        info.lpVerbW = (IntPtr)(target.Id - 1);
        info.lpDirectoryW = Path.GetDirectoryName(files[0]);
        info.nShow = 1;
        int hr = menu.InvokeCommand(ref info);
        DestroyMenu(hmenu);
        Marshal.ReleaseComObject(menu);
        if (hr < 0) throw new Exception("InvokeCommand failed: 0x" + hr.ToString("X8"));
        return target.Path;
    }
}
