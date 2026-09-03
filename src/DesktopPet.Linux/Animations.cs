using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DesktopPet.Linux
{
    public struct TValue
    {
        public bool IsDynamic;
        public bool IsScreen;
        public string Compute;
        public int Value;

        public int GetValue(int screenIndex = -1)
        {
            if (IsDynamic)
            {
                return Animations.Xml.ParseValue(Compute, "Animations.GetValue()", screenIndex);
            }
            else if (IsScreen && screenIndex >= 0)
            {
                return Animations.Xml.ParseValue(Compute, "Animations.GetValue()", screenIndex);
            }
            else
            {
                return Value;
            }
        }
    }

    public struct TMovement
    {
        public TValue X;
        public TValue Y;
        public TValue Interval;
        public int OffsetY;
        public double Opacity;
    }

    public struct TNextAnimation
    {
        public enum TOnly
        {
            NONE = 0x7F,
            TASKBAR = 0x01,
            WINDOW = 0x02,
            HORIZONTAL = 0x04,
            HORIZONTAL_ = 0x06,
            VERTICAL = 0x08,
        }
        public int ID;
        public int Probability;
        public TOnly only;
        public TNextAnimation(int id, int probability, TOnly where)
        {
            ID = id;
            Probability = probability;
            only = where;
        }
    }

    public struct TSequence
    {
        public TValue Repeat;
        public int RepeatFrom;
        public List<int> Frames;
        public int TotalSteps { get; set; }
        public string Action;

        public int CalculateTotalSteps(int screenIndex = -1)
        {
            return Frames.Count + (Frames.Count - RepeatFrom) * Repeat.GetValue(screenIndex);
        }
    }

    public struct TAnimation
    {
        public TMovement Start;
        public TMovement End;
        public string Name;
        public List<TNextAnimation> EndAnimation;
        public List<TNextAnimation> EndBorder;
        public List<TNextAnimation> EndGravity;
        public TSequence Sequence;
        public bool Gravity;
        public bool Border;
        public int ID;

        public TAnimation(string name, int id)
        {
            Start = new TMovement();
            End = new TMovement();
            Name = name;
            EndAnimation = new List<TNextAnimation>(8);
            EndBorder = new List<TNextAnimation>(8);
            EndGravity = new List<TNextAnimation>(8);
            Sequence = new TSequence
            {
                Frames = new List<int>(16)
            };
            Gravity = false;
            Border = false;
            ID = id;
        }

        public void UpdateValues(int screenIndex = -1)
        {
            if (Sequence.Repeat.IsDynamic)
            {
                Sequence.TotalSteps = Sequence.CalculateTotalSteps(screenIndex);
            }
            if (Start.Interval.IsDynamic || Start.X.IsDynamic || Start.Y.IsDynamic)
            {
                Start.Interval.Value = Start.Interval.GetValue(screenIndex);
                Start.X.Value = Start.X.GetValue(screenIndex);
                Start.Y.Value = Start.Y.GetValue(screenIndex);
            }
            if (End.Interval.IsDynamic || End.X.IsDynamic || End.Y.IsDynamic)
            {
                End.Interval.Value = End.Interval.GetValue(screenIndex);
                End.X.Value = End.X.GetValue(screenIndex);
                End.Y.Value = End.Y.GetValue(screenIndex);
            }
            if (Program.MyData.GetScale() > 1)
            {
                int scale = Program.MyData.GetScale();
                Start.X.Value *= scale;
                Start.Y.Value *= scale;
                End.X.Value *= scale;
                End.Y.Value *= scale;

                Start.OffsetY *= scale;
                End.OffsetY *= scale;
            }
        }
    }

    public struct TSpawn
    {
        public TMovement Start;
        public int Probability;
        public int Next;

        public TSpawn(int probability)
        {
            Start = new TMovement();
            Probability = probability;
            Next = 1;
        }
    }

    public struct TChild
    {
        public TMovement Position;
        public int AnimationID;
        public int Next;
    }

    /// <summary>
    /// Sound structure. A sound that can be played together with the animation.
    /// Uses AudioPlayer (ffplay-backed) instead of the original NAudio.Wave classes.
    /// </summary>
    public struct TSound
    {
        public int AnimationID;
        public int Probability;
        public int Loop;

        private AudioPlayer? Audio;

        public void Load(byte[] buff)
        {
            try
            {
                Audio = new AudioPlayer(buff);
            }
            catch (Exception e)
            {
                Program.MyData.SetVolume(0.0);
                Program.Mainthread.ErrorMessages.AudioErrorMessage = e.Message;
            }
        }

        public void Play(int loopCount)
        {
            if (Program.MyData.GetVolume() > 0.0 && Audio != null)
            {
                try
                {
                    Audio.Volume = Program.MyData.GetVolume();
                    Audio.Play(loopCount);
                }
                catch (Exception e)
                {
                    Program.MyData.SetVolume(0.0);
                    Program.Mainthread.ErrorMessages.AudioErrorMessage = e.Message;
                }
            }
        }
    }

    public class Animations
    {
        public Dictionary<int, TAnimation> SheepAnimations;
        public Dictionary<int, TSpawn> SheepSpawn;
        public Dictionary<int, List<TChild>> SheepChild;
        public Dictionary<int, TSound> SheepSound;

        private readonly Random rand;
        public static Xml Xml = null!;

        public int AnimationDrag = 1;
        public int AnimationFall = 1;
        public int AnimationKill = -1;
        public int AnimationSync = 1;
        public int AnimationToss = -1;
        public int AnimationFallSoft = 1;
        public int AnimationFallHard = 1;

        public Animations(Xml xml)
        {
            SheepAnimations = new Dictionary<int, TAnimation>(64);
            SheepSpawn = new Dictionary<int, TSpawn>(8);
            SheepChild = new Dictionary<int, List<TChild>>(8);
            SheepSound = new Dictionary<int, TSound>(8);
            rand = new Random();
            Xml = xml;
        }

        public TAnimation AddAnimation(int ID, string name)
        {
            try
            {
                SheepAnimations.Add(ID, new TAnimation(name, ID));
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "adding animation: " + name);
            }
            catch (Exception ex)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "unable to add animation: " + ex.Message);
            }
            return SheepAnimations[ID];
        }

        public void SaveAnimation(TAnimation animation, int ID)
        {
            SheepAnimations[ID] = animation;
        }

        public TSpawn AddSpawn(int ID, int probability)
        {
            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "adding spawn: " + ID.ToString());
            SheepSpawn.Add(ID, new TSpawn(probability));
            return SheepSpawn[ID];
        }

        public void SaveSpawn(TSpawn spawn, int ID)
        {
            SheepSpawn[ID] = spawn;
        }

        public TChild AddChild(int ID)
        {
            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "adding child: ani." + ID.ToString());
            if (!SheepChild.ContainsKey(ID))
            {
                SheepChild.Add(ID, new List<TChild>(1));
            }
            SheepChild[ID].Add(new TChild());
            return SheepChild[ID].Last();
        }

        public void SaveChild(TChild child, int ID)
        {
            SheepChild[ID][SheepChild[ID].Count - 1] = child;
        }

        public void AddSound(int ID, int Probability, int Loop, string Base64)
        {
            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "adding sound (ani." + ID.ToString() + ")");

            try
            {
                if (Base64.IndexOf(";base64,") > 0)
                    Base64 = Base64.Substring(Base64.IndexOf(";base64,") + 8);

                TSound sound = new TSound();
                sound.Load(Convert.FromBase64String(Base64));
                sound.AnimationID = ID;
                sound.Probability = Probability;
                sound.Loop = Loop;
                SheepSound.Add(ID, sound);
            }
            catch (Exception ex)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "can't open sound:" + ex.Message);
            }
        }

        public TSpawn GetRandomSpawn()
        {
            int percent = 0;
            int randValue;
            foreach (TSpawn spawn in SheepSpawn.Values)
            {
                percent += spawn.Probability;
            }
            randValue = rand.Next(0, percent);

            percent = 0;
            foreach (TSpawn spawn in SheepSpawn.Values)
            {
                percent += spawn.Probability;
                if (percent >= randValue)
                {
                    return spawn;
                }
            }

            if (SheepSpawn.Count > 0)
            {
                return SheepSpawn.First().Value;
            }
            else
            {
                TSpawn retSpawn;
                if (SheepAnimations.Count > 0)
                    retSpawn.Next = SheepAnimations.First().Key;
                else
                    retSpawn.Next = 1;
                retSpawn.Probability = 100;
                retSpawn.Start.X.Compute = "0";
                retSpawn.Start.X.IsDynamic = false;
                retSpawn.Start.X.IsScreen = false;
                retSpawn.Start.X.Value = 0;
                retSpawn.Start.Y.Compute = "0";
                retSpawn.Start.Y.IsDynamic = false;
                retSpawn.Start.Y.IsScreen = false;
                retSpawn.Start.Y.Value = 0;
                retSpawn.Start.Opacity = 1.0;
                retSpawn.Start.Interval.Compute = "1000";
                retSpawn.Start.Interval.IsDynamic = false;
                retSpawn.Start.Interval.IsScreen = false;
                retSpawn.Start.Interval.Value = 1000;
                retSpawn.Start.OffsetY = 0;
                return retSpawn;
            }
        }

        public TAnimation GetAnimation(int id)
        {
            if (!SheepAnimations.ContainsKey(id))
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "animation not found: " + id);
                TAnimation tempAnimation = new TAnimation("NULL", 0);
                tempAnimation.Start.Interval.Value = 1000;
                tempAnimation.End.Interval.Value = 1000;
                return tempAnimation;
            }
            return SheepAnimations[id];
        }

        public List<TChild> GetAnimationChild(int id)
        {
            return SheepChild[id];
        }

        public bool HasAnimationChild(int id)
        {
            return SheepChild.ContainsKey(id);
        }

        public int SetNextBorderAnimation(int animationID, TNextAnimation.TOnly where)
        {
            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "border detected");
            return SetNextGeneralAnimation(SheepAnimations[animationID].EndBorder, where);
        }

        public int SetNextSequenceAnimation(int animationID, TNextAnimation.TOnly where)
        {
            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "animation is over");
            return SetNextGeneralAnimation(SheepAnimations[animationID].EndAnimation, where);
        }

        public int SetNextGravityAnimation(int animationID, TNextAnimation.TOnly where)
        {
            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "gravity detected");
            return SetNextGeneralAnimation(SheepAnimations[animationID].EndGravity, where);
        }

        private int SetNextGeneralAnimation(List<TNextAnimation> list, TNextAnimation.TOnly where)
        {
            int iDefaultID = -1;
            if (list.Count > 0)
            {
                int iVal;
                int iSum = 0;
                int iRandMax = 0;
                foreach (TNextAnimation anim in list)
                {
                    if (anim.only != TNextAnimation.TOnly.NONE && (anim.only & where) == 0) continue;

                    iRandMax += anim.Probability;
                }
                iVal = rand.Next(1, iRandMax + 1);
                foreach (TNextAnimation anim in list)
                {
                    if (anim.only != TNextAnimation.TOnly.NONE && (anim.only & where) == 0) continue;

                    iSum += anim.Probability;
                    if (iSum >= iVal)
                    {
                        iDefaultID = anim.ID;
                        break;
                    }
                }
                if (iDefaultID > 0)
                {
                    UpdateAnimationValues(iDefaultID);
                    if (SheepSound.ContainsKey(iDefaultID))
                    {
                        if (rand.Next(0, 100) < SheepSound[iDefaultID].Probability)
                        {
                            SheepSound[iDefaultID].Play(SheepSound[iDefaultID].Loop);
                        }
                    }
                }
                return iDefaultID;
            }
            else
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "no next animation found");
                return -1;
            }
        }

        private void UpdateAnimationValues(int id)
        {
            bool bUpdated = false;
            TAnimation ani = SheepAnimations[id];
            if (ani.Sequence.Repeat.IsDynamic)
            {
                ani.Sequence.TotalSteps = ani.Sequence.CalculateTotalSteps();
                bUpdated = true;
            }
            if (ani.Start.Interval.IsDynamic || ani.Start.X.IsDynamic || ani.Start.Y.IsDynamic)
            {
                ani.Start.Interval.Value = ani.Start.Interval.GetValue();
                ani.Start.X.Value = ani.Start.X.GetValue();
                ani.Start.Y.Value = ani.Start.Y.GetValue();
                bUpdated = true;
            }
            if (ani.End.Interval.IsDynamic || ani.End.X.IsDynamic || ani.End.Y.IsDynamic)
            {
                ani.End.Interval.Value = ani.End.Interval.GetValue();
                ani.End.X.Value = ani.End.X.GetValue();
                ani.End.Y.Value = ani.End.Y.GetValue();
                bUpdated = true;
            }

            if (bUpdated)
            {
                SheepAnimations[id] = ani;
            }

            StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "new animation: " + ani.Name + " (" + ani.ID + ")");
        }

        public List<TNextAnimation> GetNextAnimations(int currentID, bool includeNext, bool includeBorder, bool includeGravity)
        {
            List<TNextAnimation> list = new List<TNextAnimation>();

            if (includeNext)
                list.AddRange(SheepAnimations[currentID].EndAnimation);
            if (includeBorder)
                list.AddRange(SheepAnimations[currentID].EndBorder);
            if (includeGravity)
                list.AddRange(SheepAnimations[currentID].EndGravity);

            return list;
        }

        public List<TSpawn> GetNextSpawns()
        {
            List<TSpawn> list = new List<TSpawn>();

            for (int i = 0; i < SheepSpawn.Keys.Count; i++)
            {
                list.Add(SheepSpawn[SheepSpawn.Keys.ElementAt(i)]);
            }
            return list;
        }
    }
}
