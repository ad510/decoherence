﻿// game form
// Copyright (c) 2013 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SlimDX;
using SlimDX.D3DCompiler;
using SlimDX.Direct3D9;
using SlimDX.Windows;
using SlimDX.DirectSound;
using SlimDX.DirectInput;

namespace Decoherence
{
    public partial class App : Form
    {
        public const string ErrStr = ". The program will exit now."; // display this during an initialization or drawing crash
        public const double SelBoxMin = 100;
        public const float FntSize = 1f / 40;

        string appPath;
        string modPath = "mod\\";
        SlimDX.Direct3D9.Device d3dOriginalDevice;
        int runMode;
        float winDiag;
        DX.Img2D imgSelect;
        DX.Img2D[] imgParticle;
        DX.Poly2D tlTile;
        DX.Poly2D tlPoly;
        SlimDX.Direct3D9.Font fnt;
        Random rand;
        int selMatter;
        List<int> selParticles;
        bool paused;

        public App()
        {
            InitializeComponent();
        }

        private void App_Load(object sender, EventArgs e)
        {
            int i, i2, i3;
            System.Collections.Hashtable json;
            System.Collections.ArrayList jsonA;
            string str;
            bool b = false;
            appPath = Application.ExecutablePath.Substring(0, Application.ExecutablePath.LastIndexOf("bin\\"));
            rand = new Random();
            if (!DX.init(this.Handle, true))
            {
                MessageBox.Show("Couldn't set up DirectX. Make sure your video and audio drivers are up-to-date" + ErrStr + "\n\nError description: " + DX.dxErr);
                Application.Exit();
                return;
            }
            DX.setDefaultRes();
            this.Width = DX.mode.Width;
            this.Height = DX.mode.Height;
            winDiag = new Vector2(DX.mode.Width, DX.mode.Height).Length();
            this.Show();
            this.Focus();
            if (!DX.init3d(out d3dOriginalDevice, this.Handle, DX.mode.Width, DX.mode.Height, DX.mode.Format, new Vector3(), new Vector3(), (float)(Math.PI / 4), 1000))
            {
                MessageBox.Show("Couldn't set up Direct3D. Make sure your video and audio drivers are up-to-date and that no other programs are currently using DirectX" + ErrStr + "\n\nError description: " + DX.dxErr);
                Application.Exit();
                return;
            }
            // fonts (TODO: make font, size, and color customizable by mod)
            fnt = new SlimDX.Direct3D9.Font(DX.d3dDevice, new System.Drawing.Font("Arial", DX.sy * FntSize, GraphicsUnit.Pixel));
            // selected particle image (TODO: make this customizable by mod)
            imgSelect.init();
            if (!imgSelect.open(appPath + modPath + "select.png", Color.White.ToArgb())) MessageBox.Show("Warning: failed to load " + modPath + "select.png");
            imgSelect.rotCenter.X = imgSelect.srcWidth / 2;
            imgSelect.rotCenter.Y = imgSelect.srcHeight / 2;
            // load universe from file
            // if this ever supports multiplayer games, host should load file & send data to other players, otherwise json double parsing may not match
            str = new System.IO.StreamReader(appPath + modPath + "univ.json").ReadToEnd();
            json = (System.Collections.Hashtable)Procurios.Public.JSON.JsonDecode(str, ref b);
            if (!b)
            {
                MessageBox.Show("Universe failed to load" + ErrStr);
                this.Close();
                return;
            }
            Sim.u.mapSize = jsonFP(json, "mapSize");
            Sim.u.camSpeed = jsonFP(json, "camSpeed");
            Sim.u.camPos = jsonFPVector(json, "camPos", new FP.Vector(Sim.u.mapSize / 2, Sim.u.mapSize / 2));
            Sim.u.drawScl = (float)jsonDouble(json, "drawScl");
            Sim.u.drawSclMin = (float)jsonDouble(json, "drawSclMin");
            Sim.u.drawSclMax = (float)jsonDouble(json, "drawSclMax");
            Sim.u.backCol = jsonColor4(json, "backCol");
            Sim.u.borderCol = jsonColor4(json, "borderCol");
            Sim.u.noVisCol = jsonColor4(json, "noVisCol");
            Sim.u.visCol = jsonColor4(json, "visCol");
            Sim.u.coherentCol = jsonColor4(json, "coherentCol");
            //Sim.g.music = jsonString(json, "music");
            jsonA = jsonArray(json, "matterTypes");
            if (jsonA != null)
            {
                foreach (System.Collections.Hashtable jsonO in jsonA)
                {
                    Sim.MatterType matterT = new Sim.MatterType();
                    matterT.name = jsonString(jsonO, "name");
                    matterT.isPlayer = jsonBool(jsonO, "isPlayer");
                    matterT.player = (short)jsonDouble(jsonO, "player");
                    Sim.u.nMatterT++;
                    Array.Resize(ref Sim.u.matterT, Sim.u.nMatterT);
                    Sim.u.matterT[Sim.u.nMatterT - 1] = matterT;
                }
            }
            jsonA = jsonArray(json, "particleTypes");
            if (jsonA != null)
            {
                foreach (System.Collections.Hashtable jsonO in jsonA)
                {
                    Sim.ParticleType particleT = new Sim.ParticleType();
                    particleT.name = jsonString(jsonO, "name");
                    particleT.imgPath = jsonString(jsonO, "imgPath");
                    particleT.speed = jsonFP(jsonO, "speed");
                    particleT.visRadius = jsonFP(jsonO, "visRadius");
                    particleT.selRadius = jsonDouble(jsonO, "selRadius");
                    if (particleT.visRadius > Sim.maxVisRadius) Sim.maxVisRadius = particleT.visRadius;
                    Sim.u.nParticleT++;
                    Array.Resize(ref Sim.u.particleT, Sim.u.nParticleT);
                    Sim.u.particleT[Sim.u.nParticleT - 1] = particleT;
                }
            }
            imgParticle = new DX.Img2D[Sim.u.nParticleT * Sim.u.nMatterT];
            for (i = 0; i < Sim.u.nParticleT; i++)
            {
                for (i2 = 0; i2 < Sim.u.nMatterT; i2++)
                {
                    i3 = i * Sim.u.nParticleT + i2;
                    imgParticle[i3].init();
                    if (!imgParticle[i3].open(appPath + modPath + Sim.u.matterT[i2].name + '.' + Sim.u.particleT[i].imgPath, Color.White.ToArgb())) MessageBox.Show("Warning: failed to load " + modPath + Sim.u.matterT[i2].name + '.' + Sim.u.particleT[i].imgPath);
                    imgParticle[i3].rotCenter.X = imgParticle[i3].srcWidth / 2;
                    imgParticle[i3].rotCenter.Y = imgParticle[i3].srcHeight / 2;
                }
            }
            // TODO: load particles from file too
            Sim.nParticles = 20;
            Sim.p = new Sim.Particle[Sim.nParticles];
            for (i = 0; i < Sim.nParticles; i++)
            {
                Sim.p[i] = new Sim.Particle(0, i / (Sim.nParticles / 2), 0, new FP.Vector((long)(rand.NextDouble() * Sim.u.mapSize), (long)(rand.NextDouble() * Sim.u.mapSize)));
            }
            selParticles = new List<int>();
            // set up visibility and coherence tiles
            Sim.particleVis = new List<int>[Sim.tileLen(), Sim.tileLen()];
            Sim.matterVis = new List<long>[Sim.u.nMatterT, Sim.tileLen(), Sim.tileLen()];
            Sim.coherence = new List<long>[Sim.u.nMatterT, Sim.tileLen(), Sim.tileLen()];
            for (i = 0; i < Sim.tileLen(); i++)
            {
                for (i2 = 0; i2 < Sim.tileLen(); i2++)
                {
                    Sim.particleVis[i, i2] = new List<int>();
                    for (i3 = 0; i3 < Sim.u.nMatterT; i3++)
                    {
                        Sim.matterVis[i3, i, i2] = new List<long>();
                        Sim.coherence[i3, i, i2] = new List<long>();
                    }
                }
            }
            tlTile.primitive = PrimitiveType.TriangleList;
            tlTile.setNPoly(0);
            tlTile.nV[0] = Sim.tileLen() * Sim.tileLen() * 2;
            tlTile.poly[0].v = new DX.TLVertex[tlTile.nV[0] * 3];
            for (i = 0; i < tlTile.poly[0].v.Length; i++)
            {
                tlTile.poly[0].v[i].rhw = 1;
                tlTile.poly[0].v[i].z = 0;
            }
            // start game
            Sim.timeSim = -1;
            DX.timeNow = Environment.TickCount;
            DX.timeStart = DX.timeNow;
            runMode = 1;
            gameLoop();
            this.Close();
        }

        private void App_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && runMode > 0)
            {
                runMode = 0;
                e.Cancel = true;
            }
        }

        private void App_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            DX.mouseDblClk();
        }

        private void App_MouseDown(object sender, MouseEventArgs e)
        {
            int button = (int)e.Button / 0x100000;
            DX.mouseDown(button, e.X, e.Y);
        }

        private void App_MouseMove(object sender, MouseEventArgs e)
        {
            int button = (int)e.Button / 0x100000;
            int i;
            i = DX.mouseMove(button, e.X, e.Y);
            if (i != -1)
            {
                if (DX.mouseState[i] == 0)
                {
                    App_MouseUp(this, new System.Windows.Forms.MouseEventArgs((MouseButtons)(button * 0x100000), 0, e.X, e.Y, 0));
                }
                else
                {
                    App_MouseDown(this, new System.Windows.Forms.MouseEventArgs((MouseButtons)(button * 0x100000), 0, e.X, e.Y, 0));
                }
            }
        }

        private void App_MouseUp(object sender, MouseEventArgs e)
        {
            int button = (int)e.Button / 0x100000;
            int mousePrevState = DX.mouseState[button];
            FP.Vector mouseSimPos = drawToSimPos(new Vector3(e.X, e.Y, 0));
            FP.Vector goal;
            Vector3 curPos;
            long spacing;
            int i;
            DX.mouseUp(button, e.X, e.Y);
            if (button == 1) // select
            {
                if (!DX.diKeyState.IsPressed(Key.LeftControl) && !DX.diKeyState.IsPressed(Key.LeftShift)) selParticles.Clear();
                for (i = 0; i < Sim.nParticles; i++)
                {
                    if (selMatter == Sim.p[i].matter)
                    {
                        curPos = simToDrawPos(Sim.p[i].calcPos(DX.timeNow - DX.timeStart));
                        if (curPos.X + Sim.u.particleT[Sim.p[i].type].selRadius >= Math.Min(DX.mouseDX[1], DX.mouseX)
                            && curPos.X - Sim.u.particleT[Sim.p[i].type].selRadius <= Math.Max(DX.mouseDX[1], DX.mouseX)
                            && curPos.Y + Sim.u.particleT[Sim.p[i].type].selRadius >= Math.Min(DX.mouseDY[1], DX.mouseY)
                            && curPos.Y - Sim.u.particleT[Sim.p[i].type].selRadius <= Math.Max(DX.mouseDY[1], DX.mouseY))
                        {
                            if (selParticles.Contains(i))
                            {
                                selParticles.Remove(i);
                            }
                            else
                            {
                                selParticles.Add(i);
                            }
                            if (SelBoxMin > Math.Pow(DX.mouseDX[1] - DX.mouseX, 2) + Math.Pow(DX.mouseDY[1] - DX.mouseY, 2)) break;
                        }
                    }
                }
            }
            else if (button == 2) // move
            {
                if (mouseSimPos.x >= 0 && mouseSimPos.x <= Sim.u.mapSize && mouseSimPos.y >= 0 && mouseSimPos.y <= Sim.u.mapSize)
                {
                    i = 0;
                    foreach (int id in selParticles)
                    {
                        if (DX.timeNow - DX.timeStart >= Sim.timeSim || (DX.timeNow - DX.timeStart >= Sim.p[id].timeCohere
                            && Sim.coherent(selMatter, (int)(Sim.p[id].calcPos(DX.timeNow - DX.timeStart).x >> FP.Precision), (int)(Sim.p[id].calcPos(DX.timeNow - DX.timeStart).y >> FP.Precision), DX.timeNow - DX.timeStart)))
                        {
                            // TODO: instead of using maxVisRadius, should use smallest radius of selected particles
                            if (DX.diKeyState.IsPressed(Key.LeftControl))
                            {
                                spacing = FP.mul(Sim.maxVisRadius, FP.fromDouble(Math.Sqrt(2))) >> FP.Precision << FP.Precision;
                            }
                            else
                            {
                                spacing = 1 << FP.Precision;
                            }
                            goal = mouseSimPos + new FP.Vector((i % (int)Math.Ceiling(Math.Sqrt(selParticles.Count))) * spacing, (long)Math.Floor(i / Math.Ceiling(Math.Sqrt(selParticles.Count))) * spacing);
                            if (goal.x < 0) goal.x = 0;
                            if (goal.x > Sim.u.mapSize) goal.x = Sim.u.mapSize;
                            if (goal.y < 0) goal.y = 0;
                            if (goal.y > Sim.u.mapSize) goal.y = Sim.u.mapSize;
                            Sim.p[id].addMove(Sim.ParticleMove.fromSpeed(DX.timeNow - DX.timeStart, Sim.u.particleT[Sim.p[id].type].speed, Sim.p[id].calcPos(DX.timeNow - DX.timeStart), goal));
                            i++;
                        }
                    }
                }
            }
        }

        private void gameLoop()
        {
            while (runMode == 1)
            {
                updateTime();
                Sim.update(DX.timeNow - DX.timeStart);
                inputHandle();
                draw();
            }
        }

        private void updateTime()
        {
            DX.doEventsX();
            if (paused)
            {
                DX.timeStart += DX.timeNow - DX.timeLast;
            }
            else if (DX.diKeyState != null && DX.diKeyState.IsPressed(Key.R))
            {
                DX.timeStart += 2 * (DX.timeNow - DX.timeLast);
            }
            // TODO: cap time difference to a max amount
        }

        private void inputHandle()
        {
            int i;
            DX.keyboardUpdate();
            // handle changed keys
            for (i = 0; i < DX.diKeysChanged.Count; i++)
            {
                if (DX.diKeysChanged[i] == Key.Escape && DX.diKeyState.IsPressed(DX.diKeysChanged[i]))
                {
                    App_KeyDown(this, new System.Windows.Forms.KeyEventArgs(Keys.Escape));
                }
                else if (DX.diKeysChanged[i] == Key.Space && DX.diKeyState.IsPressed(DX.diKeysChanged[i]))
                {
                    selMatter = (selMatter + 1) % Sim.u.nMatterT;
                    selParticles.Clear();
                }
                else if (DX.diKeysChanged[i] == Key.P && DX.diKeyState.IsPressed(DX.diKeysChanged[i]))
                {
                    paused = !paused;
                }
            }
            // move camera
            if (DX.diKeyState.IsPressed(Key.LeftArrow) || DX.mouseX == 0 || (this.Left > 0 && DX.mouseX <= 15))
            {
                Sim.u.camPos.x -= Sim.u.camSpeed * (DX.timeNow - DX.timeLast);
                if (Sim.u.camPos.x < 0) Sim.u.camPos.x = 0;
            }
            if (DX.diKeyState.IsPressed(Key.RightArrow) || DX.mouseX == DX.sx - 1 || (this.Left + this.Width < Screen.PrimaryScreen.Bounds.Width && DX.mouseX >= DX.sx - 15))
            {
                Sim.u.camPos.x += Sim.u.camSpeed * (DX.timeNow - DX.timeLast);
                if (Sim.u.camPos.x > Sim.u.mapSize) Sim.u.camPos.x = Sim.u.mapSize;
            }
            if (DX.diKeyState.IsPressed(Key.UpArrow) || DX.mouseY == 0 || (this.Top > 0 && DX.mouseY <= 15))
            {
                Sim.u.camPos.y -= Sim.u.camSpeed * (DX.timeNow - DX.timeLast);
                if (Sim.u.camPos.y < 0) Sim.u.camPos.y = 0;
            }
            if (DX.diKeyState.IsPressed(Key.DownArrow) || DX.mouseY == DX.sy - 1 || (this.Top + this.Height < Screen.PrimaryScreen.Bounds.Height && DX.mouseY >= DX.sy - 15))
            {
                Sim.u.camPos.y += Sim.u.camSpeed * (DX.timeNow - DX.timeLast);
                if (Sim.u.camPos.y > Sim.u.mapSize) Sim.u.camPos.y = Sim.u.mapSize;
            }
        }

        private void draw()
        {
            Vector3 vec, vec2;
            FP.Vector fpVec;
            int col;
            int i, i2, tX, tY;
            DX.d3dDevice.Clear(ClearFlags.Target | ClearFlags.ZBuffer, Sim.u.backCol, 1, 0);
            DX.d3dDevice.BeginScene();
            DX.d3dDevice.SetTexture(0, null);
            // visibility tiles
            // TODO: don't draw tiles off map
            for (tX = 0; tX < Sim.tileLen(); tX++)
            {
                for (tY = 0; tY < Sim.tileLen(); tY++)
                {
                    vec = simToDrawPos(new FP.Vector(tX << FP.Precision, tY << FP.Precision));
                    vec2 = simToDrawPos(new FP.Vector((tX + 1) << FP.Precision, (tY + 1) << FP.Precision));
                    i = (tX * Sim.tileLen() + tY) * 6;
                    tlTile.poly[0].v[i].x = vec.X;
                    tlTile.poly[0].v[i].y = vec.Y;
                    tlTile.poly[0].v[i + 1].x = vec2.X;
                    tlTile.poly[0].v[i + 1].y = vec2.Y;
                    tlTile.poly[0].v[i + 2].x = vec2.X;
                    tlTile.poly[0].v[i + 2].y = vec.Y;
                    tlTile.poly[0].v[i + 3].x = vec.X;
                    tlTile.poly[0].v[i + 3].y = vec.Y;
                    tlTile.poly[0].v[i + 4].x = vec.X;
                    tlTile.poly[0].v[i + 4].y = vec2.Y;
                    tlTile.poly[0].v[i + 5].x = vec2.X;
                    tlTile.poly[0].v[i + 5].y = vec2.Y;
                    if (Sim.matterVisWhen(selMatter, tX, tY, DX.timeNow - DX.timeStart))
                    {
                        if (Sim.coherent(selMatter, tX, tY, DX.timeNow - DX.timeStart))
                        {
                            col = Sim.u.coherentCol.ToArgb();
                        }
                        else
                        {
                            col = Sim.u.visCol.ToArgb();
                        }
                    }
                    else
                    {
                        col = Sim.u.noVisCol.ToArgb();
                    }
                    for (i2 = i; i2 < i + 6; i2++)
                    {
                        tlTile.poly[0].v[i2].color = col;
                    }
                }
            }
            tlTile.draw();
            // map border
            tlPoly.primitive = PrimitiveType.LineStrip;
            tlPoly.setNPoly(0);
            tlPoly.nV[0] = 4;
            tlPoly.poly[0].v = new DX.TLVertex[tlPoly.nV[0] + 1];
            for (i = 0; i < 4; i++)
            {
                tlPoly.poly[0].v[i].color = Sim.u.borderCol.ToArgb();
                tlPoly.poly[0].v[i].rhw = 1;
                tlPoly.poly[0].v[i].z = 0;
            }
            vec = simToDrawPos(new FP.Vector());
            vec2 = simToDrawPos(new FP.Vector(Sim.u.mapSize, Sim.u.mapSize));
            tlPoly.poly[0].v[0].x = vec.X;
            tlPoly.poly[0].v[0].y = vec.Y;
            tlPoly.poly[0].v[1].x = vec2.X;
            tlPoly.poly[0].v[1].y = vec.Y;
            tlPoly.poly[0].v[2].x = vec2.X;
            tlPoly.poly[0].v[2].y = vec2.Y;
            tlPoly.poly[0].v[3].x = vec.X;
            tlPoly.poly[0].v[3].y = vec2.Y;
            tlPoly.poly[0].v[4] = tlPoly.poly[0].v[0];
            tlPoly.draw();
            // particles
            // TODO: scale particle images
            for (i = 0; i < Sim.nParticles; i++)
            {
                if (DX.timeNow - DX.timeStart < Sim.p[i].m[0].timeStart) continue;
                i2 = Sim.p[i].type * Sim.u.nParticleT + Sim.p[i].matter;
                fpVec = Sim.p[i].calcPos(DX.timeNow - DX.timeStart);
                if (selMatter != Sim.p[i].matter && !Sim.matterVisWhen(selMatter, (int)(fpVec.x >> FP.Precision), (int)(fpVec.y >> FP.Precision), DX.timeNow - DX.timeStart)) continue;
                if (Sim.p[i].n > Sim.p[i].mLive + 1 && DX.timeNow - DX.timeStart >= Sim.p[i].m[Sim.p[i].mLive + 1].timeStart)
                {
                    imgParticle[i2].color = new Color4(0.5f, 1, 1, 1).ToArgb(); // TODO: make transparency amount customizable
                }
                else
                {
                    imgParticle[i2].color = new Color4(1, 1, 1, 1).ToArgb();
                }
                imgParticle[i2].pos = simToDrawPos(fpVec);
                imgParticle[i2].draw();
                if (selParticles.Contains(i))
                {
                    imgSelect.pos = imgParticle[i2].pos;
                    imgSelect.draw();
                }
            }
            // select box (if needed)
            // TODO: make color customizable by mod?
            if (DX.mouseState[1] > 0 && SelBoxMin <= Math.Pow(DX.mouseDX[1] - DX.mouseX, 2) + Math.Pow(DX.mouseDY[1] - DX.mouseY, 2))
            {
                DX.d3dDevice.SetTexture(0, null);
                tlPoly.primitive = PrimitiveType.LineStrip;
                tlPoly.setNPoly(0);
                tlPoly.nV[0] = 4;
                tlPoly.poly[0].v = new DX.TLVertex[tlPoly.nV[0] + 1];
                for (i = 0; i < 4; i++)
                {
                    tlPoly.poly[0].v[i].color = DX.ColWhite;
                    tlPoly.poly[0].v[i].rhw = 1;
                    tlPoly.poly[0].v[i].z = 0;
                }
                tlPoly.poly[0].v[0].x = DX.mouseDX[1];
                tlPoly.poly[0].v[0].y = DX.mouseDY[1];
                tlPoly.poly[0].v[1].x = DX.mouseX;
                tlPoly.poly[0].v[1].y = DX.mouseDY[1];
                tlPoly.poly[0].v[2].x = DX.mouseX;
                tlPoly.poly[0].v[2].y = DX.mouseY;
                tlPoly.poly[0].v[3].x = DX.mouseDX[1];
                tlPoly.poly[0].v[3].y = DX.mouseY;
                tlPoly.poly[0].v[4] = tlPoly.poly[0].v[0];
                tlPoly.draw();
            }
            // text
            DX.textDraw(fnt, new Color4(1, 1, 1, 1), (DX.timeNow - DX.timeStart >= Sim.timeSim) ? "LIVE" : "TIME TRAVELING", 0, 0);
            if (paused) DX.textDraw(fnt, new Color4(1, 1, 1, 1), "PAUSED", 0, (int)(DX.sy * FntSize));
            DX.d3dDevice.EndScene();
            DX.d3dDevice.Present();
        }

        private void App_KeyDown(object sender, KeyEventArgs e)
        {
            // in case DirectInput isn't working
            if (e.KeyCode == Keys.Escape)
            {
                runMode = 0;
            }
        }

        private string jsonString(System.Collections.Hashtable json, string key, string defaultVal = "")
        {
            if (json.ContainsKey(key) && json[key] is string) return (string)json[key];
            return defaultVal;
        }

        private double jsonDouble(System.Collections.Hashtable json, string key, double defaultVal = 0)
        {
            if (json.ContainsKey(key) && json[key] is double) return (double)json[key];
            return defaultVal;
        }

        private bool jsonBool(System.Collections.Hashtable json, string key, bool defaultVal = false)
        {
            if (json.ContainsKey(key) && json[key] is bool) return (bool)json[key];
            return defaultVal;
        }

        private long jsonFP(System.Collections.Hashtable json, string key, long defaultVal = 0)
        {
            if (json.ContainsKey(key))
            {
                if (json[key] is double) return FP.fromDouble((double)json[key]);
                if (json[key] is string)
                {
                    // parse as hex string, so no rounding errors when converting from double
                    // allow beginning string with '-' to specify negative number, as alternative to prepending with f's
                    long ret;
                    if (long.TryParse(((string)json[key]).TrimStart('-'), System.Globalization.NumberStyles.HexNumber, null, out ret))
                    {
                        return ((string)json[key])[0] == '-' ? -ret : ret;
                    }
                    return defaultVal;
                }
            }
            return defaultVal;
        }

        private System.Collections.Hashtable jsonObject(System.Collections.Hashtable json, string key)
        {
            if (json.ContainsKey(key) && json[key] is System.Collections.Hashtable) return (System.Collections.Hashtable)json[key];
            return null;
        }

        private System.Collections.ArrayList jsonArray(System.Collections.Hashtable json, string key)
        {
            if (json.ContainsKey(key) && json[key] is System.Collections.ArrayList) return (System.Collections.ArrayList)json[key];
            return null;
        }

        private FP.Vector jsonFPVector(System.Collections.Hashtable json, string key, FP.Vector defaultVal = new FP.Vector())
        {
            if (json.ContainsKey(key) && json[key] is System.Collections.Hashtable)
            {
                return new FP.Vector(jsonFP((System.Collections.Hashtable)json[key], "x", defaultVal.x),
                    jsonFP((System.Collections.Hashtable)json[key], "y", defaultVal.y),
                    jsonFP((System.Collections.Hashtable)json[key], "z", defaultVal.z));
            }
            return defaultVal;
        }

        private Color4 jsonColor4(System.Collections.Hashtable json, string key)
        {
            if (json.ContainsKey(key) && json[key] is System.Collections.Hashtable)
            {
                return new Color4((float)jsonDouble((System.Collections.Hashtable)json[key], "a", 1),
                    (float)jsonDouble((System.Collections.Hashtable)json[key], "r", 0),
                    (float)jsonDouble((System.Collections.Hashtable)json[key], "g", 0),
                    (float)jsonDouble((System.Collections.Hashtable)json[key], "b", 0));
            }
            return new Color4();
        }

        private float simToDrawScl(long coor)
        {
            return (float)(FP.toDouble(coor) * Sim.u.drawScl * winDiag);
        }

        private long drawToSimScl(float coor)
        {
            return FP.fromDouble(coor / winDiag / Sim.u.drawScl);
        }

        private Vector3 simToDrawScl(FP.Vector vec)
        {
            return new Vector3(simToDrawScl(vec.x), simToDrawScl(vec.y), simToDrawScl(vec.z));
        }

        private FP.Vector drawToSimScl(Vector3 vec)
        {
            return new FP.Vector(drawToSimScl(vec.X), drawToSimScl(vec.Y), drawToSimScl(vec.Z));
        }

        private Vector3 simToDrawPos(FP.Vector vec)
        {
            return new Vector3(simToDrawScl(vec.x - Sim.u.camPos.x), simToDrawScl(vec.y - Sim.u.camPos.y), 0f) + new Vector3(DX.sx / 2, DX.sy / 2, 0f);
        }

        private FP.Vector drawToSimPos(Vector3 vec)
        {
            return new FP.Vector(drawToSimScl(vec.X - DX.sx / 2), drawToSimScl(vec.Y - DX.sy / 2)) + Sim.u.camPos;
        }
    }
}
