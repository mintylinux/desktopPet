using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPet.Linux
{
    /// <summary>
    /// Simple rectangle in root-window (screen) coordinates. Mirrors the role of the
    /// original Windows-only NativeMethods.RECT.
    /// </summary>
    public struct XRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>
    /// A visible top-level window found on the desktop, used for the "fall onto / walk on
    /// top of windows" feature (replaces EnumWindows + GetWindowRect + GetTitleBarInfo).
    /// </summary>
    public struct XWindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public XRect Rect;
    }

    /// <summary>
    /// P/Invoke wrapper around libX11 + EWMH (Extended Window Manager Hints) for the pieces
    /// of desktop/window introspection the original Windows app got from user32.dll:
    /// - screen bounds / work area (taskbar exclusion)
    /// - hiding the pet window from the taskbar/pager and keeping it above other windows
    /// - enumerating visible top-level windows and their geometry, for landing on top of them
    /// </summary>
    public static class X11Interop
    {
        private const string LibX11 = "libX11.so.6";

        [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(LibX11)] private static extern int XDefaultScreen(IntPtr display);
        [DllImport(LibX11)] private static extern int XDisplayWidth(IntPtr display, int screenNumber);
        [DllImport(LibX11)] private static extern int XDisplayHeight(IntPtr display, int screenNumber);
        [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);
        [DllImport(LibX11)] private static extern int XFree(IntPtr data);
        [DllImport(LibX11)]
        private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
            long longOffset, long longLength, bool delet, IntPtr reqType,
            out IntPtr actualTypeReturn, out int actualFormatReturn,
            out long nItemsReturn, out long bytesAfterReturn, out IntPtr propReturn);
        [DllImport(LibX11)]
        private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property,
            IntPtr type, int format, int mode, long[] data, int nElements);
        [DllImport(LibX11, EntryPoint = "XChangeProperty")]
        private static extern int XChangeProperty32(IntPtr display, IntPtr window, IntPtr property,
            IntPtr type, int format, int mode, int[] data, int nElements);
        [DllImport(LibX11)]
        private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);
        [DllImport(LibX11)]
        private static extern bool XTranslateCoordinates(IntPtr display, IntPtr srcWindow, IntPtr destWindow,
            int srcX, int srcY, out int destXReturn, out int destYReturn, out IntPtr childReturn);
        [DllImport(LibX11)]
        private static extern int XFetchName(IntPtr display, IntPtr window, out IntPtr windowNameReturn);
        [DllImport(LibX11)] private static extern int XFlush(IntPtr display);

        [StructLayout(LayoutKind.Sequential)]
        private struct XWindowAttributes
        {
            public int x, y;
            public int width, height;
            public int border_width;
            public int depth;
            public IntPtr visual;
            public IntPtr root;
            public int c_class;
            public int bit_gravity;
            public int win_gravity;
            public int backing_store;
            public long backing_planes;
            public long backing_pixel;
            public bool save_under;
            public IntPtr colormap;
            public bool map_installed;
            public int map_state; // 0=Unmapped, 1=Unviewable, 2=Viewable
            public long all_event_masks;
            public long your_event_mask;
            public long do_not_propagate_mask;
            public bool override_redirect;
            public IntPtr screen;
        }

        private static IntPtr _display = IntPtr.Zero;

        /// <summary>
        /// Opens (once) the connection to the X server. Returns false if not running under X11
        /// (e.g. Wayland without XWayland), in which case all other calls in this class become no-ops.
        /// </summary>
        public static bool EnsureDisplay()
        {
            if (_display != IntPtr.Zero) return true;
            try
            {
                _display = XOpenDisplay(IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                _display = IntPtr.Zero;
            }
            return _display != IntPtr.Zero;
        }

        private static IntPtr Atom(string name) => XInternAtom(_display, name, false);

        /// <summary>
        /// Full screen bounds (in pixels) for the default screen.
        /// </summary>
        public static XRect GetScreenBounds()
        {
            if (!EnsureDisplay()) return new XRect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
            int screen = XDefaultScreen(_display);
            return new XRect { Left = 0, Top = 0, Right = XDisplayWidth(_display, screen), Bottom = XDisplayHeight(_display, screen) };
        }

        /// <summary>
        /// Work area (screen area excluding panels/taskbars), read from the standard EWMH
        /// _NET_WORKAREA property on the root window. Falls back to full screen bounds if the
        /// window manager doesn't publish it.
        /// </summary>
        public static XRect GetWorkArea()
        {
            var full = GetScreenBounds();
            if (!EnsureDisplay()) return full;

            IntPtr root = XDefaultRootWindow(_display);
            long[]? values = GetCardinalArray(root, "_NET_WORKAREA");
            if (values == null || values.Length < 4) return full;

            // _NET_WORKAREA is 4 CARDINALs (x, y, width, height) per desktop; use the first entry.
            int x = (int)values[0];
            int y = (int)values[1];
            int w = (int)values[2];
            int h = (int)values[3];
            return new XRect { Left = x, Top = y, Right = x + w, Bottom = y + h };
        }

        /// <summary>
        /// Reads a property of type CARDINAL/ATOM (32-bit values) from a window.
        /// </summary>
        private static long[]? GetCardinalArray(IntPtr window, string propertyName)
        {
            IntPtr prop = Atom(propertyName);
            if (prop == IntPtr.Zero) return null;

            int status = XGetWindowProperty(_display, window, prop, 0, 1024, false, IntPtr.Zero,
                out IntPtr actualType, out int actualFormat, out long nItems, out long bytesAfter, out IntPtr data);

            if (status != 0 || data == IntPtr.Zero || nItems == 0) return null;

            var result = new long[nItems];
            try
            {
                for (long i = 0; i < nItems; i++)
                {
                    result[i] = actualFormat == 32
                        ? Marshal.ReadInt32(data, (int)(i * 4))
                        : Marshal.ReadByte(data, (int)i);
                }
            }
            finally
            {
                XFree(data);
            }
            return result;
        }

        /// <summary>
        /// Applies the EWMH hints that keep the pet window off the taskbar/pager/alt-tab list
        /// and marks it as a "utility" style window (replacing WS_EX_TOOLWINDOW).
        /// Should be called once the underlying X11 window handle is available (before or
        /// shortly after the window is shown).
        /// </summary>
        public static void SetPetWindowHints(IntPtr x11Window)
        {
            if (!EnsureDisplay() || x11Window == IntPtr.Zero) return;

            // _NET_WM_WINDOW_TYPE = _NET_WM_WINDOW_TYPE_UTILITY
            IntPtr typeAtom = Atom("_NET_WM_WINDOW_TYPE");
            IntPtr utilityAtom = Atom("_NET_WM_WINDOW_TYPE_UTILITY");
            if (typeAtom != IntPtr.Zero && utilityAtom != IntPtr.Zero)
            {
                XChangeProperty32(_display, x11Window, typeAtom, new IntPtr(4) /* XA_ATOM */, 32, 0 /* PropModeReplace */,
                    new[] { (int)utilityAtom.ToInt64() }, 1);
            }

            // _NET_WM_STATE = SKIP_TASKBAR, SKIP_PAGER, ABOVE
            IntPtr stateAtom = Atom("_NET_WM_STATE");
            IntPtr skipTaskbar = Atom("_NET_WM_STATE_SKIP_TASKBAR");
            IntPtr skipPager = Atom("_NET_WM_STATE_SKIP_PAGER");
            IntPtr above = Atom("_NET_WM_STATE_ABOVE");
            if (stateAtom != IntPtr.Zero)
            {
                var states = new List<int>();
                if (skipTaskbar != IntPtr.Zero) states.Add((int)skipTaskbar.ToInt64());
                if (skipPager != IntPtr.Zero) states.Add((int)skipPager.ToInt64());
                if (above != IntPtr.Zero) states.Add((int)above.ToInt64());
                if (states.Count > 0)
                {
                    XChangeProperty32(_display, x11Window, stateAtom, new IntPtr(4), 32, 0, states.ToArray(), states.Count);
                }
            }

            XFlush(_display);
        }

        /// <summary>
        /// Enumerates visible, titled top-level windows on the desktop (via the EWMH
        /// _NET_CLIENT_LIST_STACKING property), with their geometry translated to root/screen
        /// coordinates. Used to replace EnumWindows + GetWindowRect + GetTitleBarInfo for the
        /// "fall and land on top of a window" feature.
        /// </summary>
        public static List<XWindowInfo> GetVisibleWindows(IntPtr ignoreWindow)
        {
            var result = new List<XWindowInfo>();
            if (!EnsureDisplay()) return result;

            IntPtr root = XDefaultRootWindow(_display);
            IntPtr listAtom = Atom("_NET_CLIENT_LIST_STACKING");
            if (listAtom == IntPtr.Zero) return result;

            int status = XGetWindowProperty(_display, root, listAtom, 0, 4096, false, IntPtr.Zero,
                out IntPtr actualType, out int actualFormat, out long nItems, out long bytesAfter, out IntPtr data);
            if (status != 0 || data == IntPtr.Zero) return result;

            try
            {
                for (long i = 0; i < nItems; i++)
                {
                    IntPtr win = new IntPtr(Marshal.ReadInt32(data, (int)(i * 4)));
                    if (win == IntPtr.Zero || win == ignoreWindow) continue;

                    if (XGetWindowAttributes(_display, win, out var attrs) == 0) continue;
                    if (attrs.map_state != 2 /* Viewable */) continue;
                    if (attrs.override_redirect) continue; // skip tooltips/menus/other pet windows
                    if (IsDesktopOrDockWindow(win)) continue; // skip the desktop/wallpaper window and panels/taskbars

                    string title = GetWindowTitle(win);
                    if (string.IsNullOrEmpty(title)) continue;

                    XTranslateCoordinates(_display, win, root, 0, 0, out int rootX, out int rootY, out _);

                    result.Add(new XWindowInfo
                    {
                        Handle = win,
                        Title = title,
                        Rect = new XRect { Left = rootX, Top = rootY, Right = rootX + attrs.width, Bottom = rootY + attrs.height }
                    });
                }
            }
            finally
            {
                XFree(data);
            }
            return result;
        }

        /// <summary>
        /// Gets the current geometry (in root/screen coordinates) of a specific window handle,
        /// used to track a window the pet is standing on (replaces GetWindowRect).
        /// </summary>
        public static bool TryGetWindowRect(IntPtr window, out XRect rect)
        {
            rect = default;
            if (!EnsureDisplay() || window == IntPtr.Zero) return false;
            if (XGetWindowAttributes(_display, window, out var attrs) == 0) return false;

            IntPtr root = XDefaultRootWindow(_display);
            XTranslateCoordinates(_display, window, root, 0, 0, out int rootX, out int rootY, out _);
            rect = new XRect { Left = rootX, Top = rootY, Right = rootX + attrs.width, Bottom = rootY + attrs.height };
            return true;
        }

        /// <summary>
        /// Checks whether the window still exists and is mapped/visible (used to detect a
        /// window the pet was standing on having been closed or minimized).
        /// </summary>
        public static bool IsWindowVisible(IntPtr window)
        {
            if (!EnsureDisplay() || window == IntPtr.Zero) return false;
            return XGetWindowAttributes(_display, window, out var attrs) != 0 && attrs.map_state == 2;
        }

        /// <summary>
        /// True if the window advertises itself (via _NET_WM_WINDOW_TYPE) as the desktop
        /// background or a dock/panel. These windows commonly span the whole screen (or a
        /// screen edge) and are never real Z-order-above content, so treating them as regular
        /// windows in the landing/occlusion checks caused every other window to look
        /// permanently "occluded" underneath the desktop window and pets never landed on
        /// anything.
        /// </summary>
        private static bool IsDesktopOrDockWindow(IntPtr window)
        {
            long[]? types = GetCardinalArray(window, "_NET_WM_WINDOW_TYPE");
            if (types == null) return false;

            IntPtr desktopAtom = Atom("_NET_WM_WINDOW_TYPE_DESKTOP");
            IntPtr dockAtom = Atom("_NET_WM_WINDOW_TYPE_DOCK");
            foreach (long t in types)
            {
                if ((desktopAtom != IntPtr.Zero && t == desktopAtom.ToInt64()) ||
                    (dockAtom != IntPtr.Zero && t == dockAtom.ToInt64()))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetWindowTitle(IntPtr window)
        {
            // Prefer _NET_WM_NAME (UTF8_STRING), fall back to WM_NAME via XFetchName.
            IntPtr nameAtom = Atom("_NET_WM_NAME");
            if (nameAtom != IntPtr.Zero)
            {
                int status = XGetWindowProperty(_display, window, nameAtom, 0, 1024, false, IntPtr.Zero,
                    out _, out _, out long nItems, out _, out IntPtr data);
                if (status == 0 && data != IntPtr.Zero)
                {
                    try
                    {
                        if (nItems > 0)
                        {
                            var bytes = new byte[nItems];
                            Marshal.Copy(data, bytes, 0, (int)nItems);
                            return Encoding.UTF8.GetString(bytes);
                        }
                    }
                    finally
                    {
                        XFree(data);
                    }
                }
            }

            if (XFetchName(_display, window, out IntPtr namePtr) != 0 && namePtr != IntPtr.Zero)
            {
                string? s = Marshal.PtrToStringAnsi(namePtr);
                XFree(namePtr);
                return s ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
