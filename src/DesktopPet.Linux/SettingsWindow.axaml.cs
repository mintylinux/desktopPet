using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DesktopPet.Linux
{
    /// <summary>
    /// Main settings window: pick/switch the pet character or color, spawn additional pets,
    /// or exit. Closing the window (the [x] button) hides it instead of quitting the
    /// application, mirroring how apps like Steam minimize to the tray - the app keeps running
    /// (pets keep walking, tray icon stays) until "Exit" is explicitly chosen.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            PetList.ItemsSource = PetCatalog.AvailablePets;
            var current = PetCatalog.AvailablePets.FirstOrDefault(p => p.Id == Program.MyData.GetCurrentPet());
            PetList.SelectedItem = current ?? PetCatalog.AvailablePets.FirstOrDefault();

            NewPetButton.Click += (_, _) => Program.Mainthread.AddSheep();
            ExitButton.Click += (_, _) => Program.Mainthread.KillSheeps(true);
            ChangePetButton.Click += ChangePetButton_Click;

            Closing += (_, e) =>
            {
                e.Cancel = true;
                Hide();
            };
        }

        private void ChangePetButton_Click(object? sender, RoutedEventArgs e)
        {
            if (PetList.SelectedItem is not PetInfo pet) return;

            try
            {
                string xml = PetCatalog.LoadXml(pet);
                Program.MyData.SetXml(xml, pet.Id);
                Program.Mainthread.LoadNewXMLFromString(xml);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to switch pet: " + ex);
            }
        }

        /// <summary>Brings the window to the front, showing it again if it was hidden/minimized to tray.</summary>
        public void ShowAndActivate()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}
