using System.Collections.Generic;
using Avalonia.Controls;

namespace DesktopPet.Linux
{
    /// <summary>
    /// Snapshot of monitor bounds/work-areas, refreshed from Avalonia's cross-platform Screens
    /// API. Replaces System.Windows.Forms.Screen.AllScreens/PrimaryScreen, which Xml.cs and
    /// Animations.cs originally used directly.
    /// </summary>
    public static class ScreenInfo
    {
        public static List<XRect> Bounds { get; private set; } = new() { new XRect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 } };
        public static List<XRect> WorkAreas { get; private set; } = new() { new XRect { Left = 0, Top = 0, Right = 1920, Bottom = 1050 } };
        public static int PrimaryIndex { get; private set; } = 0;

        public static XRect PrimaryBounds => Bounds[PrimaryIndex];
        public static XRect PrimaryWorkArea => WorkAreas[PrimaryIndex];

        /// <summary>
        /// Re-reads the current monitor layout. Call once at startup (and optionally whenever
        /// the pet window detects it moved to a different screen).
        /// </summary>
        public static void Refresh(Screens screens)
        {
            var bounds = new List<XRect>();
            var workAreas = new List<XRect>();
            int primary = 0;

            var all = screens.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                bounds.Add(new XRect { Left = s.Bounds.X, Top = s.Bounds.Y, Right = s.Bounds.X + s.Bounds.Width, Bottom = s.Bounds.Y + s.Bounds.Height });
                workAreas.Add(new XRect { Left = s.WorkingArea.X, Top = s.WorkingArea.Y, Right = s.WorkingArea.X + s.WorkingArea.Width, Bottom = s.WorkingArea.Y + s.WorkingArea.Height });
                if (s.IsPrimary) primary = i;
            }

            if (bounds.Count > 0)
            {
                Bounds = bounds;
                WorkAreas = workAreas;
                PrimaryIndex = primary;
            }
        }
    }
}
