using System;
using System.Xml;
using System.IO;
using System.Data;
using System.Xml.Schema;
using System.Globalization;
using System.Xml.Serialization;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace DesktopPet.Linux
{
    /// <summary>
    /// Xml class contains all functions to read the XML file and functions to parse it.
    /// Ported from the original Windows/WinForms Xml.cs: GDI+ (System.Drawing) bitmap building
    /// replaced with Avalonia imaging (CroppedBitmap), Screen.* replaced with ScreenInfo,
    /// MessageBox replaced with StartUp debug logging.
    /// </summary>
    public sealed class Xml : IDisposable
    {
        public XmlData.RootNode AnimationXML = null!;
        public string AnimationXMLString = "";
        public IList<Bitmap> sprites;
        public int spriteWidth;
        public int spriteHeight;
        public MemoryStream? bitmapIcon;

        public int parentX;
        public int parentY;
        public bool parentFlipped;

        int iRandomSpawn = 10;
        int iScale = 1;

        public Xml(int scaleFactor = 1)
        {
            sprites = new List<Bitmap>();
            iScale = scaleFactor;

            parentX = -1;
            parentY = -1;
            parentFlipped = false;

            Random rand = new Random();
            iRandomSpawn = rand.Next(10, 90);
        }

        public void Dispose()
        {
            bitmapIcon?.Dispose();

            foreach (var sprite in sprites)
            {
                sprite?.Dispose();
            }
            sprites.Clear();
        }

        static void ValidationEventHandler(object sender, ValidationEventArgs e)
        {
            switch (e.Severity)
            {
                case XmlSeverityType.Error:
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "XSD validation: " + e.Message);
                    break;
                case XmlSeverityType.Warning:
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "XSD validation: " + e.Message);
                    break;
            }
        }

        /// <summary>
        /// This function will load the XML. If something can't be loaded as expected, the default XML will be loaded.
        /// </summary>
        public bool ReadXML()
        {
            bool bError = false;
            XmlSerializer mySerializer = new XmlSerializer(typeof(XmlData.RootNode));
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            try
            {
                writer.Write(Program.MyData.GetXml());
                AnimationXMLString = Program.MyData.GetXml();

                writer.Flush();
                stream.Position = 0;
                AnimationXML = (XmlData.RootNode)mySerializer.Deserialize(stream)!;

                stream.Close();

                Program.MyData.SetImages(AnimationXML.Image.Png);
                Program.MyData.SetIcon(AnimationXML.Header.Icon);
            }
            catch (Exception ex)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "User XML error: " + ex);
                if (Program.MyData.GetXml().Length > 100)
                {
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Error parsing animation XML: " + ex);
                }
                stream.Flush();
                stream.Position = 0;
                string defaultXml = DefaultAnimations.Xml;
                writer.Write(defaultXml);
                writer.Flush();
                AnimationXMLString = defaultXml;
                stream.Position = 0;
                AnimationXML = (XmlData.RootNode)mySerializer.Deserialize(stream)!;

                Program.MyData.SetXml(defaultXml, "esheep64");
                Program.MyData.SetImages(AnimationXML.Image.Png);
                Program.MyData.SetIcon(AnimationXML.Header.Icon);
            }
            finally
            {
                AnimationXML.Image.Png = string.Empty;
                AnimationXML.Header.Icon = string.Empty;
                try
                {
                    ReadImages();

                    if (AnimationXML.Header.Petname.Length > 16) AnimationXML.Header.Petname = AnimationXML.Header.Petname.Substring(0, 16);
                }
                catch (Exception ex)
                {
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Error reading XML: " + ex.Message);
                    bError = true;
                }
            }

            if (bError)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Error, can't load animations file. The original pet will be loaded");
            }

            return !bError;
        }

        /// <summary>
        /// Load the animations (read them from XML file)
        /// </summary>
        public void LoadAnimations(Animations animations)
        {
            if (AnimationXML.Animations == null)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "No animations for this pet");
                return;
            }
            foreach (XmlData.AnimationNode node in AnimationXML.Animations.Animation)
            {
                TAnimation ani = animations.AddAnimation(node.Id, node.Id.ToString());
                ani.Border = node.Border != null;
                ani.Gravity = node.Gravity != null;

                ani.Name = node.Name;
                switch (ani.Name)
                {
                    case "fall": animations.AnimationFall = node.Id; break;
                    case "drag": animations.AnimationDrag = node.Id; break;
                    case "kill": animations.AnimationKill = node.Id; break;
                    case "sync": animations.AnimationSync = node.Id; break;
                    case "toss": animations.AnimationToss = node.Id; break;
                    case "fall soft": animations.AnimationFallSoft = node.Id; break;
                    case "fall hard": animations.AnimationFallHard = node.Id; break;
                }

                ani.Start.X = GetXMLCompute(node.Start.X, "animation " + node.Id + ": node.start.X");
                ani.Start.Y = GetXMLCompute(node.Start.Y, "animation " + node.Id + ": node.start.Y");
                ani.Start.Interval = GetXMLCompute(node.Start.Interval, "animation " + node.Id + ": node.start.Interval");
                ani.Start.OffsetY = node.Start.OffsetY;
                ani.Start.Opacity = node.Start.Opacity;

                ani.End.X = GetXMLCompute(node.End.X, "animation " + node.Id + ": node.end.X");
                ani.End.Y = GetXMLCompute(node.End.Y, "animation " + node.Id + ": node.end.Y");
                ani.End.Interval = GetXMLCompute(node.End.Interval, "animation " + node.Id + ": node.end.Interval");
                ani.End.OffsetY = node.End.OffsetY;
                ani.End.Opacity = node.End.Opacity;

                ani.Sequence.RepeatFrom = node.Sequence.RepeatFromFrame;
                ani.Sequence.Action = node.Sequence.Action;
                ani.Sequence.Repeat = GetXMLCompute(node.Sequence.RepeatCount, "animation " + node.Id + ": node.sequence.Repeat");
                ani.Sequence.Frames.AddRange(node.Sequence.Frame);
                if (ani.Sequence.RepeatFrom > 0)
                    ani.Sequence.TotalSteps = ani.Sequence.Frames.Count + (ani.Sequence.Frames.Count - ani.Sequence.RepeatFrom - 1) * ani.Sequence.Repeat.Value;
                else
                    ani.Sequence.TotalSteps = ani.Sequence.Frames.Count + ani.Sequence.Frames.Count * ani.Sequence.Repeat.Value;
                if (node.Sequence.Next != null)
                {
                    foreach (XmlData.NextNode nextNode in node.Sequence.Next)
                    {
                        ani.EndAnimation.Add(new TNextAnimation(nextNode.Value, nextNode.Probability, ParseOnly(nextNode.OnlyFlag)));
                    }
                }

                if (ani.Border)
                {
                    foreach (XmlData.NextNode nextNode in node.Border.Next)
                    {
                        ani.Border = true;
                        ani.EndBorder.Add(new TNextAnimation(nextNode.Value, nextNode.Probability, ParseOnly(nextNode.OnlyFlag)));
                    }
                }

                if (ani.Gravity)
                {
                    foreach (XmlData.NextNode nextNode in node.Gravity.Next)
                    {
                        ani.Gravity = true;
                        ani.EndGravity.Add(new TNextAnimation(nextNode.Value, nextNode.Probability, ParseOnly(nextNode.OnlyFlag)));
                    }
                }

                animations.SaveAnimation(ani, node.Id);
            }

            if (AnimationXML.Spawns.Spawn != null)
            {
                foreach (XmlData.SpawnNode node in AnimationXML.Spawns.Spawn)
                {
                    TSpawn ani = animations.AddSpawn(node.Id, node.Probability);

                    ani.Start.X = GetXMLCompute(node.X, "spawn " + node.Id + ": node.X");
                    ani.Start.Y = GetXMLCompute(node.Y, "spawn " + node.Id + ": node.X");
                    ani.Next = node.Next.Value;

                    animations.SaveSpawn(ani, node.Id);
                }
            }

            if (AnimationXML.Childs.Child != null)
            {
                foreach (XmlData.ChildNode node in AnimationXML.Childs.Child)
                {
                    TChild aniChild = animations.AddChild(node.Id);
                    aniChild.AnimationID = node.Id;

                    aniChild.Position.X = GetXMLCompute(node.X, "child " + node.Id + ": node.X");
                    aniChild.Position.Y = GetXMLCompute(node.Y, "child " + node.Id + ": node.Y");
                    aniChild.Next = node.Next;

                    animations.SaveChild(aniChild, node.Id);
                }
            }

            if (AnimationXML.Sounds != null && AnimationXML.Sounds.Sound != null)
            {
                foreach (XmlData.SoundNode node in AnimationXML.Sounds.Sound)
                {
                    animations.AddSound(node.Id, node.Probability, node.Loop, node.Base64);
                }
            }
        }

        private static TNextAnimation.TOnly ParseOnly(string onlyFlag)
        {
            switch (onlyFlag)
            {
                case "taskbar": return TNextAnimation.TOnly.TASKBAR;
                case "window": return TNextAnimation.TOnly.WINDOW;
                case "horizontal": return TNextAnimation.TOnly.HORIZONTAL;
                case "horizontal+": return TNextAnimation.TOnly.HORIZONTAL_;
                case "vertical": return TNextAnimation.TOnly.VERTICAL;
                default: return TNextAnimation.TOnly.NONE;
            }
        }

        public TValue GetXMLCompute(string text, string debugInfo)
        {
            TValue v;

            v.Compute = text;
            v.IsDynamic = (v.Compute.IndexOf("random") >= 0 || v.Compute.IndexOf("randS") >= 0 || v.Compute.IndexOf("imageX") >= 0 || v.Compute.IndexOf("imageY") >= 0);
            v.IsScreen = (v.Compute.IndexOf("screen") >= 0 || v.Compute.IndexOf("area") >= 0);
            v.Value = ParseValue(v.Compute, debugInfo);

            return v;
        }

        /// <summary>
        /// Parse a value, converting keys like screenW, imageH, random,... to integers.
        /// </summary>
        public int ParseValue(string parsingText, string debugInfo, int screenIndex = -1)
        {
            int iRet = 0;
            DataTable dt = new DataTable();
            Random rand = new Random();

            if (parentFlipped)
            {
                if (parsingText.IndexOf("-imageW") >= 0)
                {
                    parsingText = parsingText.Replace("-imageW", "+imageW");
                }
                else
                {
                    parsingText = parsingText.Replace("imageW", "(-imageW)");
                }
            }

            int idx = screenIndex >= 0 && screenIndex < ScreenInfo.Bounds.Count ? screenIndex : ScreenInfo.PrimaryIndex;
            XRect screenBounds = ScreenInfo.Bounds[idx];
            XRect workArea = ScreenInfo.WorkAreas[idx];

            parsingText = parsingText.Replace("screenW", screenBounds.Width.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("screenH", screenBounds.Height.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("areaW", workArea.Width.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("areaH", (workArea.Height + workArea.Top).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageW", spriteWidth.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageH", spriteHeight.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageX", (parentX).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageY", (parentY).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("random", rand.Next(0, 100).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("randS", iRandomSpawn.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("scale", Program.MyData.GetScale().ToString());

            var v = dt.Compute(parsingText, "");
            if (double.TryParse(v.ToString(), out double dv))
            {
                iRet = (int)dv;
            }
            else
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Unable to parse integer: " + parsingText + " - " + debugInfo);
            }

            return iRet;
        }

        private void ReadImages()
        {
            MemoryStream? imageStream = null;

            try
            {
                if (Program.MyData.GetImages().Length < 2) throw new InvalidDataException();
                imageStream = new MemoryStream(Convert.FromBase64String(Program.MyData.GetImages()));
                Program.MyData.SetImages(string.Empty);
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "user images loaded");
            }
            catch (Exception)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "user images not found, loading defaults");
                try
                {
                    string pngStr = AnimationXML.Image.Png;
                    int mod4 = pngStr.Length % 4;
                    if (mod4 > 0)
                    {
                        pngStr += new string('=', 4 - mod4);
                    }
                    Program.MyData.SetImages(pngStr);
                }
                catch (Exception ex)
                {
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, ex.Message);
                }
                try
                {
                    imageStream = new MemoryStream(Convert.FromBase64String(Program.MyData.GetImages()));
                    Program.MyData.SetImages(string.Empty);
                }
                catch (Exception ex)
                {
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, ex.Message);
                }
            }

            try
            {
                if (Program.MyData.GetIcon().Length < 100) throw new InvalidDataException();
                bitmapIcon = new MemoryStream(Convert.FromBase64String(Program.MyData.GetIcon()));
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "user icon loaded");
            }
            catch (Exception)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "no user icon, loading default");
                try
                {
                    var strIco = AnimationXML.Header.Icon;
                    int mod4 = strIco.Length % 4;
                    if (mod4 > 0)
                    {
                        strIco += new string('=', 4 - mod4);
                    }
                    Program.MyData.SetIcon(strIco);
                }
                catch (Exception ex)
                {
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, ex.Message);
                }
                try
                {
                    bitmapIcon = new MemoryStream(Convert.FromBase64String(Program.MyData.GetIcon()));
                }
                catch (Exception ex)
                {
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, ex.Message);
                }
            }

            using var fullSheet = new Bitmap(imageStream!);
            imageStream!.Close();

            while (fullSheet.PixelSize.Width * iScale / AnimationXML.Image.TilesX > 255) iScale--;
            spriteWidth = fullSheet.PixelSize.Width * iScale / AnimationXML.Image.TilesX;
            spriteHeight = fullSheet.PixelSize.Height * iScale / AnimationXML.Image.TilesY;

            sprites = BuildSprites(fullSheet);
        }

        /// <summary>
        /// Build sprite frames from the animation spritesheet. Unlike the original GDI+
        /// implementation (which pre-scaled every frame pixel-by-pixel), frames are cropped at
        /// native resolution; the display-time scale is applied by the renderer (PetWindow) via
        /// nearest-neighbor stretching to spriteWidth/spriteHeight, which keeps the same crisp
        /// pixel-art look with far less work here.
        /// </summary>
        private IList<Bitmap> BuildSprites(Bitmap spriteSheet)
        {
            var result = new List<Bitmap>();
            int tileW = spriteSheet.PixelSize.Width / AnimationXML.Image.TilesX;
            int tileH = spriteSheet.PixelSize.Height / AnimationXML.Image.TilesY;

            for (var yOffset = 0; yOffset < spriteSheet.PixelSize.Height; yOffset += tileH)
            {
                for (var xOffset = 0; xOffset < spriteSheet.PixelSize.Width; xOffset += tileW)
                {
                    var crop = new CroppedBitmap(spriteSheet, new Avalonia.PixelRect(xOffset, yOffset, tileW, tileH));
                    // Materialize into a standalone RenderTargetBitmap so each frame owns its
                    // own pixel data (CroppedBitmap otherwise keeps referencing the full sheet).
                    var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(tileW, tileH));
                    using (var ctx = frame.CreateDrawingContext())
                    {
                        ctx.DrawImage(crop, new Avalonia.Rect(0, 0, tileW, tileH));
                    }
                    result.Add(frame);
                }
            }
            return result;
        }
    }
}
