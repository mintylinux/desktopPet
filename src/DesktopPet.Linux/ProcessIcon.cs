using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DesktopPet.Linux
{
    /// <summary>
    /// System tray icon + context menu, using Avalonia's cross-platform TrayIcon.
    /// Replaces the original WinForms NotifyIcon-based ProcessIcon.
    /// Note: actual visibility depends on the desktop environment implementing the
    /// StatusNotifierItem/AppIndicator tray protocol (most do; some, like vanilla GNOME,
    /// need an extension).
    /// </summary>
    public sealed class ProcessIcon : IDisposable
    {
        private TrayIcon? _trayIcon;

        public void Display()
        {
            _trayIcon = new TrayIcon
            {
                ToolTipText = "eSheep Desktop Pet",
                IsVisible = true,
            };

            var menu = new NativeMenu();

            var settingsItem = new NativeMenuItem("Settings...");
            settingsItem.Click += (_, _) => Program.SettingsWindow.ShowAndActivate();
            menu.Add(settingsItem);

            var addItem = new NativeMenuItem("New Pet");
            addItem.Click += (_, _) => Program.Mainthread.AddSheep();
            menu.Add(addItem);

            var syncItem = new NativeMenuItem("Sync Pets");
            syncItem.Click += (_, _) => Program.Mainthread.SyncSheeps();
            menu.Add(syncItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) => Program.Mainthread.KillSheeps(true);
            menu.Add(exitItem);

            _trayIcon.Menu = menu;
            _trayIcon.Clicked += (_, _) => Program.SettingsWindow.ShowAndActivate();
        }

        public void SetIcon(MemoryStream? icon, string petName, string aboutAuthor, string aboutTitle, string aboutVersion, string aboutInfo)
        {
            if (_trayIcon == null) return;
            try
            {
                if (icon != null && icon.Length > 0)
                {
                    icon.Position = 0;
                    _trayIcon.Icon = new WindowIcon(new Bitmap(icon));
                }
                _trayIcon.ToolTipText = petName + " Desktop Pet";
            }
            catch (Exception ex)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Animation ICON is invalid: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}
