using NewTek.NDI;
using SDL2;
using Serilog;
using System.Runtime.InteropServices;
public static class PInvokeHelpersWindows
{
    // Win32 menu/api constants
    const uint MF_STRING = 0x00000000;
    const uint MF_POPUP = 0x00000010;
    const uint MF_MENUBARBREAK = 0x00000020;
    const uint MF_SEPARATOR = 0x00000800;
    const uint TPM_LEFTALIGN = 0x0000;
    const uint TPM_TOPALIGN = 0x0000;
    const uint TPM_RETURNCMD = 0x0100;

    // P/Invoke signatures
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool AppendMenu(
        IntPtr hMenu,
        uint uFlags,
        UIntPtr uIDNewItem,
        string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hWnd,
        IntPtr lptpm);

    const int CMD_COPY = 1;
    const int CMD_PASTE = 2;
    const int CMD_EXIT = 3;
    const int CMD_SUB_A = 10;
    const int CMD_SUB_B = 11;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    public static string? ShowContextMenu(IntPtr sdlWindow, List<Source> sources)
    {
        // 1) Get the native HWND from SDL
        SDL.SDL_SysWMinfo wmInfo = new SDL.SDL_SysWMinfo();
        SDL.SDL_VERSION(out wmInfo.version);
        SDL.SDL_GetWindowWMInfo(sdlWindow, ref wmInfo);
        IntPtr hwnd = wmInfo.info.win.window;

        // 2) Build the menus
        IntPtr popup = CreatePopupMenu();

        var submenuPtrs = new List<IntPtr>();

        var sourceGroup = sources.ToArray();
        var sourceMap = new Dictionary<Source, int>();

        for (var i = 0; i < sourceGroup.Length; i++)
        {
            sourceMap[sourceGroup[i]] = i + 1000;
        }

        var inverseSourceMap = sourceMap.ToDictionary(x => x.Value, x => x.Key);

        var hierarchy = sourceGroup.GroupBy(x => x.ComputerName);

        foreach (var item in hierarchy)
        {
            var submenu = CreatePopupMenu();
            foreach (var source in item)
            {
                AppendMenu(submenu, MF_STRING, (UIntPtr)sourceMap[source], source.SourceName);
            }
            submenuPtrs.Add(submenu);
            AppendMenu(popup, MF_POPUP, (UIntPtr)submenu, item.Key);
        }

        AppendMenu(popup, MF_SEPARATOR, 0, "");
        AppendMenu(popup, MF_STRING, 999997, "(No Source)");
        AppendMenu(popup, MF_SEPARATOR, 0, "");
        AppendMenu(popup, MF_STRING, 999998, "Toggle Full Screen");
        AppendMenu(popup, MF_STRING, 999999, "Exit");

        // 3) Position & display at current mouse
        GetCursorPos(out POINT pt);
        int cmd = TrackPopupMenuEx(
            popup,
            TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RETURNCMD,
            pt.X, pt.Y,
            hwnd,
            IntPtr.Zero);

        // 4) Tear down
        DestroyMenu(popup);
        foreach (var item in submenuPtrs)
        {
            DestroyMenu(item);
        }

        Log.Debug($"CMD: {cmd}");

        // 5) Handle the user’s choice
        if (inverseSourceMap.TryGetValue(cmd, out var newSource))
        {
            return newSource.Name;
        }
        else if(cmd == 999997)
        {
            return "!!DISC";
        }
        else if (cmd == 999999)
        {
            return "!!EXIT";
        }
        else if (cmd == 999998)
        {
            return "!!TOGGLEFS";
        }

        return null;
    }
}