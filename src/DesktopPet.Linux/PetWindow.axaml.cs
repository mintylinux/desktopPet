using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DesktopPet.Linux
{
    /// <summary>
    /// The pet window. Ported from the original WinForms FormPet.cs:
    /// - PictureBox/ImageList frame rendering -> a single Image control, source swapped per frame.
    /// - Win32 CreateParams/WS_EX_* flags -> Avalonia window properties (Topmost, ShowInTaskbar,
    ///   SystemDecorations) + X11Interop.SetPetWindowHints for the EWMH skip-taskbar/utility hints.
    /// - NativeMethods (user32.dll) calls -> X11Interop.
    /// - System.Windows.Forms.Timer -> Avalonia.Threading.DispatcherTimer.
    /// - Cursor.Position -> pointer screen position tracked via pointer events.
    /// - RotateFlip on shared Image objects -> a per-window horizontal RenderTransform.
    /// </summary>
    public partial class PetWindow : Window
    {
        private int AnimationStep;
        private TAnimation CurrentAnimation;

        /// <summary>Handle to the X11 window the pet is currently standing on (IntPtr.Zero = not on a window).</summary>
        private IntPtr hwndWindow = IntPtr.Zero;
        private IntPtr hwndFullscreenWindow = IntPtr.Zero;
        private XRect currentWindowSize;

        private bool IsMovingLeft = true;
        private readonly Animations Animations;
        private readonly Xml Xml;
        private bool IsDragging = false;
        private bool IsLeaving = false;
        private double OffsetY = 0.0;
        private double PositionX = 0.0;
        private double PositionY = 0.0;
        private double PrevPositionX = 0.0;
        private double PrevPositionY = 0.0;
        private Vector2 TossForce = Vector2.Zero;
        private bool IsTossing = false;
        private double tossVertVel = 0.0;
        private int DisplayIndex = 0;
        private bool IsChild = false;
        private string _petName = "sheep";

        private readonly List<Bitmap> _frames = new();
        private readonly DispatcherTimer timer1 = new();
        private double killOpacity = 1.0;

        private readonly List<PetWindow> childs = new();

        private XRect ScreenBounds => ScreenInfo.Bounds[DisplayIndex];
        private XRect ScreenArea => ScreenInfo.WorkAreas[DisplayIndex];

        /// <summary>Constructor for parameterless XAML loading only.</summary>
        public PetWindow()
        {
            InitializeComponent();
            Animations = null!;
            Xml = null!;
        }

        /// <summary>Called when a new sheep is generated.</summary>
        public PetWindow(Animations animations, Xml xml)
        {
            Animations = animations;
            Xml = xml;
            InitializeComponent();
            IsVisible = false;
            Opacity = 0.0;
            DisplayIndex = ScreenInfo.PrimaryIndex;
            HookEvents();
        }

        /// <summary>Called when a Child is generated.</summary>
        public PetWindow(Animations animations, Xml xml, PixelPoint parentPos, bool parentFlipped, int parentDisplay)
        {
            Animations = animations;
            Xml = xml;
            Xml.parentX = parentPos.X;
            Xml.parentY = parentPos.Y;
            Xml.parentFlipped = parentFlipped;
            DisplayIndex = parentDisplay;
            IsMovingLeft = !parentFlipped;
            IsChild = true;
            InitializeComponent();
            IsVisible = false;
            Opacity = 0.0;
            HookEvents();
        }

        private void HookEvents()
        {
            timer1.Tick += Timer1_Tick;
            Sprite.PointerPressed += Sprite_PointerPressed;
            Sprite.PointerMoved += Sprite_PointerMoved;
            Sprite.PointerReleased += Sprite_PointerReleased;
            Opened += (_, _) =>
            {
                if (TryGetPlatformHandle() is { } handle)
                {
                    X11Interop.SetPetWindowHints(handle.Handle);
                }
            };
        }

        public void AddImage(Bitmap im)
        {
            _frames.Add(im);
        }

        /// <summary>Sets the pet size and shows the (still invisible/transparent) window.</summary>
        public void ShowPet(int w, int h)
        {
            Width = w;
            Height = h;
            AnimationStep = 0;
            Show();
        }

        private void ApplyFlipTransform()
        {
            Sprite.RenderTransform = IsMovingLeft ? null : new Avalonia.Media.ScaleTransform(-1, 1);
        }

        public void Play(bool first, int forceSpawn = -1)
        {
            timer1.IsEnabled = false;

            AnimationStep = 0;
            hwndWindow = IntPtr.Zero;

            if (Program.MyData.GetMultiscreen())
            {
                Random rand = new Random();
                DisplayIndex = rand.Next(0, ScreenInfo.Bounds.Count);
            }

            TSpawn spawn;
            if (forceSpawn < 0) spawn = Animations.GetRandomSpawn();
            else
            {
                var k = Animations.SheepSpawn.Keys.ToList();
                spawn = Animations.SheepSpawn[k[forceSpawn]];
            }

            double left = ScreenBounds.Left + spawn.Start.X.GetValue(DisplayIndex);
            double top = ScreenBounds.Top + spawn.Start.Y.GetValue(DisplayIndex);
            if (!IsMovingLeft)
            {
                left = ScreenBounds.Left - (spawn.Start.X.GetValue(DisplayIndex) - ScreenBounds.Width) - Width;
            }
            Position = new PixelPoint((int)left, (int)top);
            PositionX = left;
            PositionY = top;
            OffsetY = 0.0;
            IsLeaving = false;
            SetNewAnimation(spawn.Next);
            IsVisible = true;
            Opacity = 0.0;
            timer1.IsEnabled = true;
            Topmost = true;
        }

        public void PlayChild(int aniID, TChild child)
        {
            timer1.IsEnabled = false;

            AnimationStep = 0;
            hwndWindow = IntPtr.Zero;

            double left = ScreenBounds.Left + child.Position.X.GetValue(DisplayIndex);
            double top = ScreenBounds.Top + child.Position.Y.GetValue(DisplayIndex);
            Position = new PixelPoint((int)left, (int)top);
            PositionX = left;
            PositionY = top;
            OffsetY = 0.0;
            IsVisible = true;
            Opacity = 1.0;
            IsLeaving = false;

            SetNewAnimation(child.Next);

            timer1.IsEnabled = true;
        }

        public void Kill()
        {
            foreach (var c in childs)
            {
                c?.Close();
            }
            if (Animations.AnimationKill > 1)
            {
                SetNewAnimation(Animations.AnimationKill);
            }
            else
            {
                Close();
            }
        }

        public void Sync()
        {
            if (Animations.AnimationSync > 1)
                SetNewAnimation(Animations.AnimationSync);
        }

        private void Timer1_Tick(object? sender, EventArgs e)
        {
            timer1.IsEnabled = false;
            if (AnimationStep < 0) AnimationStep = 0;
            try
            {
                NextStep();
                AnimationStep++;
                timer1.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal pet error: " + ex);
            }
        }

        private void SetNewAnimation(int id)
        {
            if (CurrentAnimation.ID == Animations.AnimationKill) return;
            if (id < 0)
            {
                Play(false);
            }
            else
            {
                AnimationStep = -1;
                CurrentAnimation = Animations.GetAnimation(id);
                CurrentAnimation.UpdateValues(DisplayIndex);

                if (Program.MyData.GetStealTaskbarFocus() && CurrentAnimation.Start.OffsetY != 0 && CurrentAnimation.Start.X.Value != 0)
                {
                    Topmost = true;
                }

                if (Animations.HasAnimationChild(id) && !IsChild)
                {
                    foreach (TChild childInfo in Animations.GetAnimationChild(id))
                    {
                        // Use PositionX/PositionY (our own tracked double fields) rather than the
                        // Avalonia Position property: immediately after Play()/NextStep() sets
                        // Position, reading it back can still return a stale (0,0) if the window
                        // hasn't been realized/mapped yet, which used to spawn children (e.g. the
                        // Pingus parachute) at the top-left corner instead of next to the parent.
                        PetWindow child = new PetWindow(Animations, Xml, new PixelPoint((int)(ScreenBounds.Left + PositionX), (int)(ScreenBounds.Top + PositionY)), !IsMovingLeft, DisplayIndex);
                        foreach (var f in _frames) child.AddImage(f);

                        // IMPORTANT: PlayChild() must run before ShowPet(). The child's constructor
                        // stashed the parent's position into the shared Xml.parentX/parentY/parentFlipped
                        // fields (used by the "imageX"/"imageY" formulas), and PlayChild() consumes them
                        // right away. Avalonia's Show() (called by ShowPet()) can pump the UI dispatcher,
                        // which lets another pet's queued timer tick run in between - and if that pet also
                        // spawns a child at that moment, it overwrites the same shared fields first. That
                        // race is exactly why balloons/children only appeared attached some of the time.
                        child.PlayChild(id, childInfo);
                        child.ShowPet((int)Width, (int)Height);

                        childs.Add(child);
                    }
                }

                timer1.Interval = TimeSpan.FromMilliseconds(Math.Max(1, CurrentAnimation.Start.Interval.GetValue()));
            }
        }

        private void NextStep()
        {
            if (AnimationStep < CurrentAnimation.Sequence.Frames.Count)
            {
                SetFrame(CurrentAnimation.Sequence.Frames[AnimationStep]);
            }
            else
            {
                int index = ((AnimationStep - CurrentAnimation.Sequence.Frames.Count + CurrentAnimation.Sequence.RepeatFrom) % (CurrentAnimation.Sequence.Frames.Count - CurrentAnimation.Sequence.RepeatFrom)) + CurrentAnimation.Sequence.RepeatFrom;
                SetFrame(CurrentAnimation.Sequence.Frames[index]);
            }

            if (!IsTossing)
                timer1.Interval = TimeSpan.FromMilliseconds(Math.Max(1, CurrentAnimation.Start.Interval.Value + ((CurrentAnimation.End.Interval.Value - CurrentAnimation.Start.Interval.Value) * AnimationStep / Math.Max(1, CurrentAnimation.Sequence.TotalSteps))));
            Opacity = CurrentAnimation.Start.Opacity + (CurrentAnimation.End.Opacity - CurrentAnimation.Start.Opacity) * AnimationStep / Math.Max(1, CurrentAnimation.Sequence.TotalSteps);
            OffsetY = CurrentAnimation.Start.OffsetY + (double)((CurrentAnimation.End.OffsetY - CurrentAnimation.Start.OffsetY) * AnimationStep / Math.Max(1, CurrentAnimation.Sequence.TotalSteps));

            if (IsDragging)
            {
                PrevPositionX = PositionX;
                PrevPositionY = PositionY;

                PositionX = _lastPointerScreen.X - Width / 2;
                PositionY = _lastPointerScreen.Y - 2;
                Position = new PixelPoint((int)PositionX, (int)PositionY);
                return;
            }

            if (IsTossing)
            {
                bool hittingLeftBorder = PositionX + TossForce.X <= ScreenArea.Left;
                bool hittingRightBorder = PositionX + TossForce.X >= ScreenArea.Left + ScreenArea.Width - Width;
                bool hittingTaskbar = PositionY + tossVertVel >= ScreenArea.Top + ScreenArea.Height - Height;
                int iWindowTop = FallDetect((int)tossVertVel);

                if (hittingLeftBorder || hittingRightBorder)
                {
                    TossForce = new Vector2(-TossForce.X * .3f, TossForce.Y);
                    PositionX = hittingLeftBorder ? ScreenArea.Left : ScreenArea.Left + ScreenArea.Width - Width;
                    Position = new PixelPoint((int)PositionX, (int)PositionY);
                    return;
                }

                if (hittingTaskbar || iWindowTop > 0)
                {
                    PositionY = hittingTaskbar ? ScreenArea.Top + ScreenArea.Height - Height : iWindowTop - Height;

                    if ((TossForce.X < 0 && !IsMovingLeft) || (TossForce.X > 0 && IsMovingLeft))
                    {
                        IsMovingLeft = !IsMovingLeft;
                        ApplyFlipTransform();
                    }

                    SetNewAnimation(tossVertVel < 40 ? Animations.AnimationFallSoft : Animations.AnimationFallHard);
                    SetFrame(CurrentAnimation.Sequence.Frames[0]);
                    IsTossing = false;
                    Position = new PixelPoint((int)PositionX, (int)PositionY);
                    return;
                }

                PositionX += TossForce.X;
                PositionY += tossVertVel;
                tossVertVel += 1.5f;
                Position = new PixelPoint((int)PositionX, (int)PositionY);
                return;
            }

            double x = CurrentAnimation.Start.X.Value;
            double y = CurrentAnimation.Start.Y.Value;
            if (CurrentAnimation.Sequence.TotalSteps > 1)
            {
                x += ((CurrentAnimation.End.X.Value - CurrentAnimation.Start.X.Value) * (double)AnimationStep / (CurrentAnimation.Sequence.TotalSteps - 1.0));
                y += ((CurrentAnimation.End.Y.Value - CurrentAnimation.Start.Y.Value) * (double)AnimationStep / (CurrentAnimation.Sequence.TotalSteps - 1.0));
            }
            bool bNewAnimation = false;
            bool bLeavingScreen = false;
            if (!IsMovingLeft) x = -x;

            if (x < 0)
            {
                if (hwndWindow == IntPtr.Zero)
                {
                    CheckFullScreen();
                    if (PositionX + x < ScreenArea.Left)
                    {
                        int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.VERTICAL);
                        if (iBorderAnimation >= 0)
                        {
                            PositionX = ScreenArea.Left;
                            x = 0;
                            SetNewAnimation(iBorderAnimation);
                            bNewAnimation = true;
                        }
                        else bLeavingScreen = true;
                    }
                }
                else
                {
                    if (X11Interop.TryGetWindowRect(hwndWindow, out var rct))
                    {
                        if (PositionX + x < rct.Left)
                        {
                            int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.WINDOW);
                            if (iBorderAnimation >= 0)
                            {
                                PositionX = rct.Left;
                                x = 0;
                                SetNewAnimation(iBorderAnimation);
                                bNewAnimation = true;
                            }
                            else hwndWindow = IntPtr.Zero;
                        }
                    }
                }
            }
            else if (x > 0)
            {
                if (hwndWindow == IntPtr.Zero)
                {
                    CheckFullScreen();
                    if (PositionX + x + Width > ScreenArea.Left + ScreenArea.Width)
                    {
                        int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.VERTICAL);
                        if (iBorderAnimation >= 0)
                        {
                            PositionX = ScreenArea.Left + ScreenArea.Width - Width;
                            x = 0;
                            SetNewAnimation(iBorderAnimation);
                            bNewAnimation = true;
                        }
                        else bLeavingScreen = true;
                    }
                }
                else
                {
                    if (X11Interop.TryGetWindowRect(hwndWindow, out var rct))
                    {
                        if (PositionX + x + Width > rct.Right)
                        {
                            int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.WINDOW);
                            if (iBorderAnimation >= 0)
                            {
                                PositionX = rct.Right - Width;
                                x = 0;
                                SetNewAnimation(iBorderAnimation);
                                bNewAnimation = true;
                            }
                            else hwndWindow = IntPtr.Zero;
                        }
                    }
                }
            }

            if (bNewAnimation || bLeavingScreen)
            {
                // don't check y this tick
            }
            else if (y > 0)
            {
                int bottomY = ScreenArea.Top + ScreenArea.Height;

                if (PositionY + y > bottomY - Height)
                {
                    int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.TASKBAR);
                    if (iBorderAnimation >= 0)
                    {
                        PositionY = bottomY - Height;
                        OffsetY = 0;
                        y = 0;
                        SetNewAnimation(iBorderAnimation);
                        bNewAnimation = true;
                    }
                }
                else
                {
                    int iWindowTop = FallDetect((int)y);
                    if (iWindowTop > 0)
                    {
                        int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.WINDOW);
                        if (iBorderAnimation >= 0)
                        {
                            PositionY = iWindowTop - Height;
                            OffsetY = 0;
                            y = 0;
                            SetNewAnimation(iBorderAnimation);
                            bNewAnimation = true;
                            if (CurrentAnimation.Start.Y.Value != 0)
                            {
                                hwndWindow = IntPtr.Zero;
                            }
                        }
                    }
                }
            }
            else if (y < 0)
            {
                if (PositionY + y < ScreenArea.Top)
                {
                    int iBorderAnimation = Animations.SetNextBorderAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.HORIZONTAL);
                    if (iBorderAnimation >= 0)
                    {
                        PositionY = ScreenArea.Top;
                        y = 0;
                        SetNewAnimation(iBorderAnimation);
                        bNewAnimation = true;
                    }
                    else bLeavingScreen = true;
                }
            }

            if (AnimationStep >= CurrentAnimation.Sequence.TotalSteps)
            {
                int iNextAni;
                if (CurrentAnimation.Sequence.Action == "flip")
                {
                    IsMovingLeft = !IsMovingLeft;
                    ApplyFlipTransform();
                }
                if (hwndWindow != IntPtr.Zero)
                {
                    iNextAni = Animations.SetNextSequenceAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.WINDOW);
                }
                else
                {
                    if (Position.X < ScreenBounds.Left - Width || Position.X > ScreenBounds.Left + ScreenBounds.Width)
                    {
                        iNextAni = -1;
                    }
                    else if (Position.Y < ScreenBounds.Top - Height || Position.Y > ScreenBounds.Top + ScreenBounds.Height)
                    {
                        iNextAni = -1;
                    }
                    else
                    {
                        iNextAni = Animations.SetNextSequenceAnimation(
                            CurrentAnimation.ID,
                            PositionY + Height + y >= ScreenArea.Top + ScreenArea.Height - 2 ? TNextAnimation.TOnly.TASKBAR : TNextAnimation.TOnly.NONE
                        );
                    }
                }
                if (CurrentAnimation.ID == Animations.AnimationKill)
                {
                    killOpacity -= 0.1;
                    Opacity = killOpacity;
                    if (killOpacity <= 0.1)
                    {
                        Close();
                        return;
                    }
                }
                else if (iNextAni >= 0)
                {
                    SetNewAnimation(iNextAni);
                    bNewAnimation = true;
                }
                else
                {
                    if (IsChild)
                    {
                        StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "removing child");
                        Close();
                        return;
                    }
                    else
                    {
                        Play(false);
                        return;
                    }
                }
            }
            else if (CurrentAnimation.Gravity)
            {
                if (hwndWindow == IntPtr.Zero)
                {
                    if (PositionY + y < ScreenArea.Top + ScreenArea.Height - Height)
                    {
                        if (PositionY + y + 3 >= ScreenArea.Top + ScreenArea.Height - Height)
                        {
                            y = ScreenArea.Top + ScreenArea.Height - (int)PositionY - Height;
                        }
                        else
                        {
                            SetNewAnimation(Animations.SetNextGravityAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.NONE));
                            bNewAnimation = true;
                        }
                    }
                }
                else
                {
                    if (AnimationStep > 0 && CheckTopWindow(true))
                    {
                        if (CurrentAnimation.Start.X.Value != 0 && FollowWindow())
                        {
                            PositionX = Position.X;
                            PositionY = Position.Y - OffsetY;
                            return;
                        }
                        else
                        {
                            hwndWindow = IntPtr.Zero;
                            SetNewAnimation(Animations.SetNextGravityAnimation(CurrentAnimation.ID, TNextAnimation.TOnly.WINDOW));
                            bNewAnimation = true;
                        }
                    }
                }
            }

            if (bNewAnimation)
            {
                timer1.Interval = TimeSpan.FromMilliseconds(1);
                SetFrame(CurrentAnimation.Sequence.Frames[0]);
            }

            PositionX += x;
            PositionY += y;

            if (bLeavingScreen)
            {
                IsLeaving = true;
                // Simplified vs. original: skip the progressive pixel-cut effect, just close
                // once fully off-screen. Keeps the port tractable while preserving the "leaves
                // the screen and respawns" behavior.
                if (PositionX < ScreenArea.Left - Width || PositionX > ScreenArea.Left + ScreenArea.Width ||
                    PositionY < ScreenArea.Top - Height || PositionY > ScreenArea.Top + ScreenArea.Height)
                {
                    Play(false);
                    return;
                }
            }
            else if (IsLeaving)
            {
                IsLeaving = false;
            }

            Position = new PixelPoint((int)PositionX, (int)(PositionY + OffsetY));
        }

        private void SetFrame(int frameIndex)
        {
            if (frameIndex >= 0 && frameIndex < _frames.Count)
            {
                Sprite.Source = _frames[frameIndex];
            }
        }

        /// <summary>Detect if pet is still falling or if the screen bottom/window was hit.</summary>
        private int FallDetect(int y)
        {
            CheckFullScreen();

            var windows = X11Interop.GetVisibleWindows(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

            foreach (var win in windows)
            {
                var rct = win.Rect;
                if (PositionY + Height < rct.Top && PositionY + Height + y >= rct.Top &&
                    PositionX >= rct.Left - Width / 2 && PositionX + Width <= rct.Right + Width / 2 &&
                    PositionY > 20 + ScreenArea.Top)
                {
                    hwndWindow = win.Handle;
                    currentWindowSize = rct;

                    if (!CheckTopWindow(false))
                    {
                        return rct.Top;
                    }
                    else
                    {
                        hwndWindow = IntPtr.Zero;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Checks if the window under the pet is fullscreen (covers the whole primary screen);
        /// if so, drop Topmost so the fullscreen app isn't obscured.
        /// </summary>
        private void CheckFullScreen()
        {
            // Best-effort: without a reliable cross-WM "foreground window" concept via EWMH here,
            // this is a light stub that keeps Topmost on. A fuller implementation could read
            // _NET_ACTIVE_WINDOW and compare its geometry to the screen bounds.
        }

        private bool FollowWindow()
        {
            if (hwndWindow != IntPtr.Zero)
            {
                if (!X11Interop.TryGetWindowRect(hwndWindow, out var rctO))
                {
                    return false;
                }

                if (rctO.Top == 0 && rctO.Bottom == 0)
                {
                    return false;
                }

                if (currentWindowSize.Top != rctO.Top || currentWindowSize.Left != rctO.Left || currentWindowSize.Right != rctO.Right)
                {
                    if (rctO.Right - rctO.Left == currentWindowSize.Right - currentWindowSize.Left)
                    {
                        Position = new PixelPoint(Position.X - (currentWindowSize.Left - rctO.Left), Position.Y - (currentWindowSize.Top - rctO.Top));
                    }
                    else
                    {
                        int newLeft = rctO.Left + (Position.X - currentWindowSize.Left) * (rctO.Right - rctO.Left) / Math.Max(1, currentWindowSize.Right - currentWindowSize.Left);
                        Position = new PixelPoint(newLeft, Position.Y - (currentWindowSize.Top - rctO.Top));
                    }
                    currentWindowSize = rctO;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if current window handle is still valid/uncovered (replaces the original
        /// Z-order walk via GetTopWindow/GetWindow using the EWMH stacking list instead).
        /// </summary>
        private bool CheckTopWindow(bool bCheck)
        {
            if (hwndWindow != IntPtr.Zero)
            {
                if (!X11Interop.TryGetWindowRect(hwndWindow, out var rctO)) return false;

                if (bCheck)
                {
                    if (currentWindowSize.Top != rctO.Top || currentWindowSize.Left != rctO.Left || currentWindowSize.Right != rctO.Right)
                    {
                        return true;
                    }
                }

                if (!X11Interop.IsWindowVisible(hwndWindow))
                {
                    return false;
                }

                // Best-effort occlusion check: see if any OTHER visible window's bounds fully
                // overlap the strip directly above the pet (approximates "something is drawn on
                // top of the window we're standing on, right where we are").
                var windows = X11Interop.GetVisibleWindows(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                foreach (var win in windows)
                {
                    if (win.Handle == hwndWindow) continue;
                    var rct = win.Rect;
                    if (rct.Top < rctO.Top && rct.Bottom > rctO.Top)
                    {
                        if (rct.Left < PositionX && rct.Right > PositionX + 40)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private PixelPoint _lastPointerScreen;

        private void Sprite_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(Sprite).Properties.IsLeftButtonPressed && !IsChild)
            {
                hwndWindow = IntPtr.Zero;
                Topmost = false;
                Topmost = true;
                IsDragging = true;
                IsTossing = false;
                SetNewAnimation(Animations.AnimationDrag);
                e.Pointer.Capture(Sprite);
            }
        }

        private void Sprite_PointerMoved(object? sender, PointerEventArgs e)
        {
            var screenPoint = this.PointToScreen(e.GetPosition(this));
            _lastPointerScreen = screenPoint;
        }

        private void Sprite_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!IsChild)
            {
                Vector2 rawTossForce = new Vector2((float)(PositionX - PrevPositionX), (float)(PositionY - PrevPositionY));
                TossForce = rawTossForce / (float)Math.Max(1, timer1.Interval.TotalMilliseconds) * 10;

                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "Toss force: (" + TossForce.X + ", " + TossForce.Y + ")");

                if (TossForce.Length() > 5)
                {
                    if (Animations.AnimationToss != -1)
                        SetNewAnimation(Animations.AnimationToss);

                    IsTossing = true;
                    tossVertVel = TossForce.Y;
                    timer1.Interval = TimeSpan.FromMilliseconds(30);
                }
                else
                {
                    SetNewAnimation(Animations.AnimationFall);
                }
            }

            if (IsDragging)
            {
                for (var k = 0; k < ScreenInfo.Bounds.Count; k++)
                {
                    var bounds = ScreenInfo.Bounds[k];
                    if (Position.X + Width / 2 >= bounds.Left && Position.X + Width / 2 <= bounds.Left + bounds.Width)
                    {
                        if (Position.Y + Height / 2 >= bounds.Top && Position.Y + Height <= bounds.Top + bounds.Height)
                        {
                            DisplayIndex = k;
                            break;
                        }
                    }
                }
            }
            IsDragging = false;
            e.Pointer.Capture(null);
        }
    }
}
