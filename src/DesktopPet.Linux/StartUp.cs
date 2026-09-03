using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace DesktopPet.Linux
{
    /// <summary>
    /// StartUp class. This class will initialize the entire application and define some constants.
    /// Ported from the original WinForms StartUp.cs: System.Windows.Forms.Timer replaced with
    /// Avalonia.Threading.DispatcherTimer (callbacks already run on the UI thread, so the
    /// original InvokeRequired/BeginInvoke pattern is no longer needed), Application.DoEvents()
    /// replaced with async/await, Application.Exit() replaced with an injected shutdown action.
    /// </summary>
    public sealed class StartUp : IDisposable
    {
        public const int MAX_SHEEPS = 16;

        public enum DEBUG_TYPE
        {
            info = 1,
            warning = 2,
            error = 3,
        }

        private readonly DispatcherTimer timer1 = new DispatcherTimer();
        private string timerTag = "A";

        readonly PetWindow[] sheeps = new PetWindow[MAX_SHEEPS];

        int iSheeps = 0;

        Xml xml;
        Animations animations;

        readonly ProcessIcon pi;

        bool isRealoadingSettings = false;
        bool isReloadingXml = false;

        public struct TError
        {
            public string AudioErrorMessage;
        }

        public TError ErrorMessages;

        /// <summary>Action invoked to actually terminate the application (set by Program.cs).</summary>
        public static Action? ExitAction;

        public StartUp(ProcessIcon processIcon)
        {
            pi = processIcon;

            xml = new Xml((int)Math.Pow(2, Program.MyData.GetScale() - 1));
            animations = new Animations(xml);

            if (!xml.ReadXML())
            {
                Program.MyData.SetXml(DefaultAnimations.Xml, "esheep64");
                xml.ReadXML();
            }

            pi.SetIcon(xml.bitmapIcon,
                        xml.AnimationXML.Header.Petname,
                        xml.AnimationXML.Header.Author,
                        xml.AnimationXML.Header.Title,
                        xml.AnimationXML.Header.Version,
                        xml.AnimationXML.Header.Info
                        );

            timerTag = "A";
            timer1.Tick += Timer1_Tick;
            timer1.Interval = TimeSpan.FromMilliseconds(1000);
            timer1.IsEnabled = true;

            Program.MyData.ListenOnXMLChanged(XmlFileChanged);
            Program.MyData.ListenOnOptionsChanged(OptionFileChanged);
        }

        private void XmlFileChanged(object source, FileSystemEventArgs e)
        {
            if (isReloadingXml) return;
            isReloadingXml = true;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(200);
                try
                {
                    Program.MyData.LoadXML();
                    LoadNewXMLFromString(Program.MyData.GetXml());
                }
                finally
                {
                    isReloadingXml = false;
                }
            });
        }

        private void OptionFileChanged(object source, FileSystemEventArgs e)
        {
            if (isRealoadingSettings) return;
            isRealoadingSettings = true;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(1000);
                Program.MyData.LoadSettings();
                await Task.Delay(200);
                isRealoadingSettings = false;
            });
        }

        public void Dispose()
        {
            xml.Dispose();
            pi.Dispose();
        }

        public void AddSheep()
        {
            if (iSheeps < MAX_SHEEPS)
            {
                try
                {
                    var newSheep = new PetWindow(animations, xml);
                    foreach (var sprite in xml.sprites)
                    {
                        newSheep.AddImage(sprite);
                    }
                    sheeps[iSheeps] = newSheep;
                    sheeps[iSheeps].ShowPet(xml.spriteWidth, xml.spriteHeight);
                    AddDebugInfo(DEBUG_TYPE.info, "new pet...");
                    AddDebugInfo(DEBUG_TYPE.info, xml.sprites.Count.ToString() + " frames added");

                    sheeps[iSheeps].Play(true);
                    iSheeps++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("AddSheep failed: " + ex);
                }
            }
            else
            {
                AddDebugInfo(DEBUG_TYPE.warning, "max PETs reached");
            }
        }

        public async void KillSheeps(bool exit)
        {
            AddDebugInfo(DEBUG_TYPE.info, "Killing all sheeps");
            timerTag = "0";
            pi.Dispose();

            if (iSheeps > 0)
            {
                Random rand = new Random();
                for (int i = 0; i < iSheeps; i++)
                {
                    await Task.Delay(rand.Next(100, 200));
                    sheeps[i].Kill();
                }
                iSheeps = 0;

                if (exit)
                {
                    await Task.Delay(1100);
                    ExitAction?.Invoke();
                }
            }
            else if (exit)
            {
                ExitAction?.Invoke();
            }
        }

        public void TopMostSheeps()
        {
            AddDebugInfo(DEBUG_TYPE.info, "Top most all sheeps");

            for (int i = 0; i < iSheeps; i++)
            {
                sheeps[i].Topmost = true;
            }
        }

        public bool KillSheep(PetWindow sheep)
        {
            bool bSheepRemoved = false;

            AddDebugInfo(DEBUG_TYPE.info, "Kill one sheep");

            for (int i = 0; i < iSheeps; i++)
            {
                if (sheeps[i] == sheep)
                {
                    sheeps[i].Kill();
                    for (int j = i; j < iSheeps - 1; j++) sheeps[j] = sheeps[j + 1];
                    iSheeps--;
                    bSheepRemoved = true;
                    break;
                }
            }

            return bSheepRemoved;
        }

        private void Timer1_Tick(object? sender, EventArgs e)
        {
            if (timerTag == "A")
            {
                if (iSheeps < Program.MyData.GetAutoStartPets() && iSheeps < MAX_SHEEPS)
                {
                    if (iSheeps == 0)
                    {
                        AddDebugInfo(DEBUG_TYPE.info, "init application...");
                        xml.LoadAnimations(animations);
                    }

                    AddSheep();
                }
                else
                {
                    timer1.IsEnabled = false;
                    timerTag = "B";
                }
            }
            else if (timerTag == "0")
            {
                timerTag = "1";
            }
            else
            {
                ExitAction?.Invoke();
            }
        }

        public void LoadNewXMLFromString(string strXml)
        {
            AddDebugInfo(DEBUG_TYPE.info, "load new XML string");

            for (int i = 0; i < iSheeps; i++)
            {
                sheeps[i].Kill();
            }
            iSheeps = 0;

            xml = new Xml(Program.MyData.GetScale());
            animations = new Animations(xml);

            if (!xml.ReadXML())
            {
                Program.MyData.SetXml(DefaultAnimations.Xml, "esheep64");
                xml.ReadXML();
            }

            pi.SetIcon(
                xml.bitmapIcon,
                xml.AnimationXML.Header.Petname,
                xml.AnimationXML.Header.Author,
                xml.AnimationXML.Header.Title,
                xml.AnimationXML.Header.Version,
                xml.AnimationXML.Header.Info);

            timerTag = "A";
            timer1.IsEnabled = true;
        }

        public Animations GetAnimations()
        {
            return animations;
        }

        public static void AddDebugInfo(DEBUG_TYPE type, string text)
        {
            Console.WriteLine($"[{type}] {text}");
        }

        public static bool IsDebugActive()
        {
            return false;
        }

        public void SyncSheeps()
        {
            AddDebugInfo(DEBUG_TYPE.info, "synchronize sheeps");
            for (int i = 0; i < iSheeps; i++)
            {
                sheeps[i].Sync();
            }
        }
    }
}
