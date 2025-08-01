using NewTek.NDI;
using SDL2;
using Serilog;
using System.Runtime.InteropServices;

public static class PInvokeHelpersLinux
{
    // ── 1) P/Invokes ────────────────────────────────────────────────
    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern bool gtk_init_check(ref int argc, IntPtr argv);

    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr gtk_menu_new();

    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr gtk_menu_item_new_with_label(
        [MarshalAs(UnmanagedType.LPStr)] string label);

    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern void gtk_menu_item_set_submenu(IntPtr menuItem, IntPtr submenu);

    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern void gtk_menu_shell_append(IntPtr menuShell, IntPtr child);

    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr gtk_separator_menu_item_new();

    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern void gtk_widget_show_all(IntPtr widget);

    // non-blocking popup at pointer
    [DllImport("libgtk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern void gtk_menu_popup(
        IntPtr menu,
        IntPtr parent_shell,
        IntPtr parent_item,
        IntPtr func,
        IntPtr data,
        uint button,
        uint activate_time);

    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern ulong g_signal_connect_data(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPStr)] string detailedSignal,
        MenuCallback cb,
        IntPtr userData,
        IntPtr destroyData,
        uint connectFlags);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern bool g_main_context_iteration(IntPtr context, bool may_block);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr g_main_context_default();

    delegate void MenuCallback(IntPtr widget, IntPtr userData);

    // ── 2) Initialization ─────────────────────────────────────────
    static bool _gtkInited = false;
    static void EnsureGtkInit()
    {
        if (_gtkInited) return;
        int argc = 0;
        gtk_init_check(ref argc, IntPtr.Zero);
        _gtkInited = true;
    }

    // ── 3) The non-blocking ShowContextMenu ────────────────────────
    /// <summary>
    /// Pops up a native GTK context menu immediately, and invokes onSelected
    /// once the user picks an item or clicks away.
    /// </summary>
    /// <param name="sdlWindow">your SDL_Window*</param>
    /// <param name="sources">list of NDI sources to group by ComputerName</param>
    /// <param name="onSelected">
    /// callback receives:
    ///  - a Source.Name
    ///  - "!!DISC", "!!TOGGLEFS", "!!EXIT"
    ///  - or null if dismissed
    /// </param>
    public static void ShowContextMenuNonBlocking(
        IntPtr sdlWindow,
        List<Source> sources,
        Action<string?> onSelected)
    {
        EnsureGtkInit();

        // 1) Build the top‐level menu
        IntPtr menu = gtk_menu_new();
        var pinned = new List<IntPtr>();

        // 2) Group by ComputerName → submenus
        foreach (var grp in sources.GroupBy(s => s.ComputerName))
        {
            IntPtr sub = gtk_menu_new();
            foreach (var src in grp)
            {
                var item   = gtk_menu_item_new_with_label(src.SourceName);
                var ptr    = Marshal.StringToHGlobalAnsi(src.Name);
                pinned.Add(ptr);

                // when activated, invoke callback & cleanup
                g_signal_connect_data(
                    item, "activate",
                    (w, u) => {
                        onSelected(Marshal.PtrToStringAnsi(u));
                        // no need to destroy menu here—GTK will GC it
                    },
                    ptr, IntPtr.Zero, 0);

                gtk_menu_shell_append(sub, item);
            }

            var parent = gtk_menu_item_new_with_label(grp.Key);
            gtk_menu_item_set_submenu(parent, sub);
            gtk_menu_shell_append(menu, parent);
        }

        // 3) "(No Source)"
        gtk_menu_shell_append(menu, gtk_separator_menu_item_new());
        {
            var noItem = gtk_menu_item_new_with_label("(No Source)");
            var ptr    = Marshal.StringToHGlobalAnsi("!!DISC");
            pinned.Add(ptr);
            g_signal_connect_data(
                noItem, "activate",
                (w, u) => onSelected("!!DISC"),
                IntPtr.Zero, IntPtr.Zero, 0);
            gtk_menu_shell_append(menu, noItem);
        }

        // 4) "Toggle Full Screen"
        gtk_menu_shell_append(menu, gtk_separator_menu_item_new());
        {
            var togItem = gtk_menu_item_new_with_label("Toggle Full Screen");
            var ptr     = Marshal.StringToHGlobalAnsi("!!TOGGLEFS");
            pinned.Add(ptr);
            g_signal_connect_data(
                togItem, "activate",
                (w, u) => onSelected("!!TOGGLEFS"),
                IntPtr.Zero, IntPtr.Zero, 0);
            gtk_menu_shell_append(menu, togItem);
        }

        // 5) "Exit"
        {
            var exitItem = gtk_menu_item_new_with_label("Exit");
            var ptr      = Marshal.StringToHGlobalAnsi("!!EXIT");
            pinned.Add(ptr);
            g_signal_connect_data(
                exitItem, "activate",
                (w, u) => onSelected("!!EXIT"),
                IntPtr.Zero, IntPtr.Zero, 0);
            gtk_menu_shell_append(menu, exitItem);
        }

        // 6) Hook “deactivate” so we get a callback on dismissal
        g_signal_connect_data(
            menu, "deactivate",
            (w, u) => onSelected(null),
            IntPtr.Zero, IntPtr.Zero, 0);

        // 7) Show & pop up at pointer (non-blocking)
        gtk_widget_show_all(menu);
        gtk_menu_popup(menu, IntPtr.Zero, IntPtr.Zero,
                       IntPtr.Zero, IntPtr.Zero,
                       3, 0);

        // 8) Clean up our pinned strings after one iteration
        //    (they stay alive long enough for the callbacks)
        foreach (var p in pinned)
            Marshal.FreeHGlobal(p);
    }

    // ── 4) Call this from your SDL loop to pump GTK events ────────
    public static void PumpGdkEvents()
    {
        // Dispatch any pending GTK/GLib events without blocking
        g_main_context_iteration(g_main_context_default(), false);
    }
}