﻿using System;    
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

using OpenTK;
using OpenTK.Graphics.OpenGL;


namespace XLibrary
{
    public partial class GibsonView : GLControl
    {
        public ViewModel Model;

        bool GLLoaded = false;

        bool ShowingOutside;
        bool ShowingExternal;

        Timer LogicTimer;
        int LogicFPS = 20;

        HashSet<int> DependentClasses = new HashSet<int>();
        HashSet<int> IndependentClasses = new HashSet<int>();

        public float PlatformHeight = 5.0f;
       
        bool MouseLook;

        Point MidWindow = new Point();

        FpsCamera FpsCam = new FpsCamera();

        IColorProfile XColors = new GibsonColorProfile();

        Vbo TreeMapVbo = new Vbo();


        public GibsonView(ViewModel model)
        {
            Model = model;

            Load += new EventHandler(this.GLView_Load);
            Paint += new PaintEventHandler(this.GLView_Paint);
            Resize += new EventHandler(this.GLView_Resize);

            KeyDown += new KeyEventHandler(GLView_KeyDown);
            KeyUp += new KeyEventHandler(GLView_KeyUp);

            LogicTimer = new Timer();
            LogicTimer.Interval = 1000 / LogicFPS;
            LogicTimer.Tick += new EventHandler(LogicTimer_Tick);
            LogicTimer.Enabled = true;
        }

        public void Redraw()
        {
            Model.DoRedraw = true;
            Invalidate();
        }

        float[] light_position = { 1f, 1f, 1f, 0f };
        
        private void GLView_Load(object sender, EventArgs e)
        {
            SetupViewport();

            GL.ClearColor(System.Drawing.Color.Black);
            GL.ShadeModel(ShadingModel.Smooth);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ColorMaterial);

            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);

            GL.Enable(EnableCap.Lighting);
            GL.Enable(EnableCap.Light0);


            float[] mat_emissive = { 0f, 0f, 0f, 1f };
            float[] mat_specular = { 1f, 1f, 1f, 1.0f };

            float[] light_diffuse = { .7f, .7f, .7f, .7f };
            float[] light_specular = { 1.0f, 1.0f, 1.0f, 1.0f };
           
            float[] light_ambient = { .5f, .5f, .35f, .5f };

            GL.Light(LightName.Light0, LightParameter.Diffuse, light_diffuse);  
            GL.Light(LightName.Light0, LightParameter.Ambient, light_ambient);
            GL.Light(LightName.Light0, LightParameter.Specular, light_specular);
            GL.Light(LightName.Light0, LightParameter.Position, light_position);

            GLLoaded = true;

            GL.GenBuffers(1, out TreeMapVbo.VboID);
            GL.GenBuffers(1, out TreeMapVbo.EboID);
        }

        private void GLView_Resize(object sender, EventArgs e)
        {
            if (!GLLoaded)
                return;

            SetupViewport();
        }

        internal void SetupViewport()
        {
            GL.Viewport(0, 0, Width, Height);

            MidWindow = new Point(Width / 2, Height / 2);

            GL.MatrixMode(MatrixMode.Projection);

            float aspect_ratio = (float)Width / (float)Height;
            Matrix4 perpective = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, aspect_ratio, 1, 4000);
            GL.LoadMatrix(ref perpective);

            Invalidate();
        }

        private void GLView_Paint(object sender, PaintEventArgs e)
        {
            if (!GLLoaded)
                return;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();

            // Move the camera to our location in space
            FpsCam.SetupCamera();

            // keeps light from movine, but kills contrast in current version
            //GL.Light(LightName.Light0, LightParameter.Position, light_position);


            RedrawTreeMap();

            TreeMapVbo.Load();

            TreeMapVbo.Draw();

            if (MouseLook)
                FpsCam.DrawHud(Width, Height, MidWindow, Color.White);

            SwapBuffers();

            Model.FpsCount++;
        }


        void LogicTimer_Tick(object sender, EventArgs e)
        {
            if (!MouseLook)
                return;

            FpsCam.MoveTick();

            FpsCam.LookTick(PointToClient(Cursor.Position), MidWindow);

            // Reset the mouse position to the centre of the window each frame
            Cursor.Position = PointToScreen(MidWindow);
            Invalidate();
        }

        void GLView_KeyDown(object sender, KeyEventArgs e)
        {
            FpsCam.KeyDown(e);

            if (e.KeyCode == Keys.M || e.KeyCode == Keys.Escape)
            {
                if (e.KeyCode == Keys.Escape)
                    MouseLook = false;
                else
                    MouseLook = !MouseLook;

                if (MouseLook)
                    Cursor.Hide();
                else
                    Cursor.Show();

                Cursor.Position = PointToScreen(MidWindow);
            }
        }

        void GLView_KeyUp(object sender, KeyEventArgs e)
        {
            FpsCam.KeyUp(e);
        }


        float PanelBorderWidth = 4;
        float NodeBorderWidth = 4;
        float MapWidth = 1000;
        float MapHeight = 1000;

        private void RedrawTreeMap()
        {
            MapWidth = 1000;
            MapHeight = 1000;

            ShowingOutside = Model.ShowOutside && Model.CurrentRoot != Model.InternalRoot;
            ShowingExternal = Model.ShowExternal && !Model.CurrentRoot.XNode.External;

            if (Model.DoRevalue ||
                (Model.ShowLayout != ShowNodes.All && XRay.CoverChange) ||
                (Model.ShowLayout == ShowNodes.Instances && XRay.InstanceChange))
            {
                Model.RecalcCover(Model.InternalRoot);
                Model.RecalcCover(Model.ExternalRoot);

                XRay.CoverChange = false;
                XRay.InstanceChange = false;

                Model.DoRevalue = false;
                Model.DoResize = true;
            }


            if (Model.DoResize)
            {
                float offset = 0.0f;
                float centerWidth = MapWidth;

                Model.PositionMap.Clear();
                Model.CenterMap.Clear();

                if (ShowingOutside)
                {
                    offset = MapWidth * 1.0f / 4.0f;
                    centerWidth -= offset;

                    Model.InternalRoot.SetArea(new RectangleF(0, 0, offset - PanelBorderWidth, MapHeight));
                    Model.PositionMap[Model.InternalRoot.ID] = Model.InternalRoot;
                    SizeNode(Model.InternalRoot, Model.CurrentRoot, false);
                }
                if (ShowingExternal)
                {
                    float extWidth = MapWidth * 1.0f / 4.0f;
                    centerWidth -= extWidth;

                    Model.ExternalRoot.SetArea(new RectangleF(offset + centerWidth + PanelBorderWidth, 0, extWidth - PanelBorderWidth, MapHeight));
                    Model.PositionMap[Model.ExternalRoot.ID] = Model.ExternalRoot;
                    SizeNode(Model.ExternalRoot, null, false);
                }

                Model.CurrentRoot.SetArea(new RectangleF(offset, 0, centerWidth, MapHeight));
                Model.PositionMap[Model.CurrentRoot.ID] = Model.CurrentRoot;
                SizeNode(Model.CurrentRoot, null, true);

                Model.DoResize = false;
            }

            TreeMapVbo.Reset();

            if (ShowingOutside)
            {
                FillRectangle(XColors.BorderColor, Model.InternalRoot.AreaF.Width, 0, PanelBorderWidth, Model.InternalRoot.AreaF.Height, 0, PlatformHeight);
                DrawNode(Model.InternalRoot, 0, true, PlatformHeight);
            }

            if (ShowingExternal)
            {
                FillRectangle(XColors.BorderColor, Model.ExternalRoot.AreaF.X - PanelBorderWidth, 0, PanelBorderWidth, Model.ExternalRoot.AreaF.Height, 0, PlatformHeight);
                DrawNode(Model.ExternalRoot, 0, true, PlatformHeight);
            }

            DrawNode(Model.CurrentRoot, 0, true, PlatformHeight);
        }

        void FillRectangle(Color color, RectangleF rect, float floor, float ceiling)
        {
            FillRectangle(color, rect.X, rect.Y, rect.Width, rect.Height, floor, ceiling);
        }

        void FillRectangle(Color color, float x, float z, float width, float length, float floor, float ceiling)
        {
            var v1 = new Vector3(x, floor, z);
            var v2 = new Vector3(x + width, floor, z);
            var v3 = new Vector3(x + width, floor, z + length);
            var v4 = new Vector3(x, floor, z + length);

            var v5 = new Vector3(x, floor + ceiling, z);
            var v6 = new Vector3(x + width, floor + ceiling, z);
            var v7 = new Vector3(x + width, floor + ceiling, z + length);
            var v8 = new Vector3(x, floor + ceiling, z + length);

            // bottom vertices
            var normal = new Vector3(0, -1, 0);
            TreeMapVbo.AddVertex(v1, color, normal);
            TreeMapVbo.AddVertex(v2, color, normal);
            TreeMapVbo.AddVertex(v3, color, normal);
            TreeMapVbo.AddVertex(v4, color, normal);

            // top vertices
            normal = new Vector3(0, 1, 0);
            TreeMapVbo.AddVertex(v8, color, normal);
            TreeMapVbo.AddVertex(v7, color, normal);     
            TreeMapVbo.AddVertex(v6, color, normal);
            TreeMapVbo.AddVertex(v5, color, normal);

            // -z facing vertices
            normal = new Vector3(0, 0, -1);
            TreeMapVbo.AddVertex(v5, color, normal);
            TreeMapVbo.AddVertex(v6, color, normal);
            TreeMapVbo.AddVertex(v2, color, normal);
            TreeMapVbo.AddVertex(v1, color, normal);

            // x facing vertices
            normal = new Vector3(1, 0, 0);
            TreeMapVbo.AddVertex(v6, color, normal);
            TreeMapVbo.AddVertex(v7, color, normal);
            TreeMapVbo.AddVertex(v3, color, normal);
            TreeMapVbo.AddVertex(v2, color, normal);

            // z facing vertices
            normal = new Vector3(0, 0, 1);
            TreeMapVbo.AddVertex(v4, color, normal);
            TreeMapVbo.AddVertex(v3, color, normal);
            TreeMapVbo.AddVertex(v7, color, normal);
            TreeMapVbo.AddVertex(v8, color, normal);
           
            // -x facing vertices
            normal = new Vector3(-1, 0, 0);
            TreeMapVbo.AddVertex(v1, color, normal);
            TreeMapVbo.AddVertex(v4, color, normal);
            TreeMapVbo.AddVertex(v8, color, normal);
            TreeMapVbo.AddVertex(v5, color, normal);
        }

        private void SizeNode(NodeModel root, NodeModel exclude, bool center)
        {
            if (!root.Show)
                return;

            RectangleF insideArea = root.AreaF;

            /*if (ShowLabels)
            {
                // check if enough room in root box for label
                RectangleF label = new RectangleF(root.AreaF.Location, buffer.MeasureString(root.Name, TextFont));

                float minHeight = (root.Nodes.Count > 0) ? label.Height * 2.0f : label.Height;

                if (root.AreaF.Height > minHeight && root.AreaF.Width > label.Width + LabelPadding * 2.0f)
                {
                    label.X += LabelPadding;
                    label.Y += LabelPadding;

                    insideArea.Y += label.Height;
                    insideArea.Height -= label.Height;

                    root.RoomForLabel = true;
                    root.LabelRect = label;
                }
            }*/

            List<Sector> sectors = new TreeMap(root, exclude, insideArea.Size).Results;

            foreach (Sector sector in sectors)
            {
                var node = sector.OriginalValue;

                sector.Rect = RectangleExtensions.Contract(sector.Rect, NodeBorderWidth);

                if (sector.Rect.X < NodeBorderWidth) sector.Rect.X = NodeBorderWidth;
                if (sector.Rect.Y < NodeBorderWidth) sector.Rect.Y = NodeBorderWidth;
                if (sector.Rect.X > insideArea.Width - NodeBorderWidth) sector.Rect.X = insideArea.Width - NodeBorderWidth;
                if (sector.Rect.Y > insideArea.Height - NodeBorderWidth) sector.Rect.Y = insideArea.Height - NodeBorderWidth;

                sector.Rect.X += insideArea.X;
                sector.Rect.Y += insideArea.Y;

                node.SetArea(sector.Rect);
                Model.PositionMap[node.ID] = node;

                node.RoomForLabel = false; // cant do above without graphic artifacts

                if (center)
                    Model.CenterMap[node.ID] = node;

                if (sector.Rect.Width > 1.0f && sector.Rect.Height > 1.0f)
                    SizeNode(node, exclude, center);
            }
        }

        private void DrawNode(NodeModel node, int depth, bool drawChildren, float z)
        {
            if (!node.Show)
                return;

            //bool pointBorder = node.AreaF.Width < 3.0f || node.AreaF.Height < 3.0f;

            // use a circle for external/outside nodes in the call map
            bool rect = Model.ViewLayout == LayoutType.ThreeD || Model.ViewLayout == LayoutType.TreeMap || Model.CenterMap.ContainsKey(node.ID);

            float zheight = PlatformHeight;
            if (node.ObjType == XObjType.Method)
                zheight = Math.Max(250f * (float)node.SecondaryValue / (float)Model.MaxSecondaryValue, 1);

            var xNode = node.XNode;

            Color pen;

            /*if (FilteredNodes.ContainsKey(node.ID))
                pen = FilteredPen;
            else if (IgnoredNodes.ContainsKey(node.ID))
                pen = IgnoredPen;*/

            if (Model.FocusedNodes.Contains(node))
                pen = XColors.ObjColors[(int)node.ObjType];
            else
                pen = XColors.ObjColors[(int)node.ObjType];


            // blue selection area
            if (node.Hovered)
            {
                if (depth > XColors.OverColors.Length - 1)
                    depth = XColors.OverColors.Length - 1;

                BlendColors(XColors.OverColors[depth], ref pen);
            }
            //else
            //    FillRectangle(NothingBrush, node.AreaF, z);

            // check if function is an entry point or holding
            if (XRay.FlowTracking && xNode.StillInside > 0)
            {
                if (xNode.EntryPoint > 0)
                {
                    if (XRay.ThreadTracking && xNode.ConflictHit > 0)
                        BlendColors(XColors.MultiEntryColor, ref pen);
                    else
                        BlendColors(XColors.EntryColor, ref pen);
                }
                else
                {
                    if (XRay.ThreadTracking && xNode.ConflictHit > 0)
                        BlendColors(XColors.MultiHoldingColor, ref pen);
                    else
                        BlendColors(XColors.HoldingColor, ref pen);
                }
            }

            // not an else if, draw over holding or entry
            if (xNode.ExceptionHit > 0)
                BlendColors(XColors.ExceptionColors[xNode.FunctionHit], ref pen);

            else if (xNode.FunctionHit > 0)
            {
                if (XRay.ThreadTracking && xNode.ConflictHit > 0)
                    BlendColors(XColors.MultiHitColors[xNode.FunctionHit], ref pen);

                else if (node.ObjType == XObjType.Field)
                {
                    if (xNode.LastFieldOp == FieldOp.Set)
                        BlendColors(XColors.FieldSetColors[xNode.FunctionHit], ref pen);
                    else
                        BlendColors(XColors.FieldGetColors[xNode.FunctionHit], ref pen);
                }
                else
                    BlendColors(XColors.HitColors[xNode.FunctionHit], ref pen);
            }

            if (Model.FocusedNodes.Count > 0 && node.ObjType == XObjType.Class)
            {
                bool dependent = DependentClasses.Contains(node.ID);
                bool independent = IndependentClasses.Contains(node.ID);

                if (dependent && independent)
                    BlendColors(XColors.InterdependentColor, ref pen);

                else if (dependent)
                    BlendColors(XColors.DependentColor, ref pen);

                else if (independent)
                    BlendColors(XColors.IndependentColor, ref pen);
            }

            if (node.SearchMatch && !Model.SearchStrobe)
                BlendColors(XColors.SearchMatchColor, ref pen);

            // if just a point, drawing a border messes up pixels
            /*if (pointBorder)
            {
                if (FilteredNodes.ContainsKey(node.ID))
                    fillFunction(FilteredBrush);
                else if (IgnoredNodes.ContainsKey(node.ID))
                    fillFunction(IgnoredBrush);

                else if (needBorder) // dont draw the point if already lit up
                    fillFunction(ObjBrushes[(int)node.ObjType]);
            }
            else
            {*/

                try
                {
                    //if (rect)
                        FillRectangle(pen, node.AreaF, z, zheight);
                    //else
                    //    FillEllipse(pen, node.AreaF, z, zheight);
                }
                catch (Exception ex)
                {
                    File.WriteAllText("debug.txt", string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}\r\n", ex, node.AreaF.X, node.AreaF.Y, node.AreaF.Width, node.AreaF.Height));
                }
            //}

            // draw label
            /*if (ShowLabels && node.RoomForLabel)
            {
                buffer.FillRectangle(LabelBgBrush, node.LabelRect);
                buffer.DrawString(node.Name, TextFont, ObjBrushes[(int)node.ObjType], node.LabelRect);
            }*/


            if (Model.MapMode == TreeMapMode.Dependencies && node.ObjType == XObjType.Class)
                drawChildren = false;

            if (drawChildren && node.AreaF.Width > 1 && node.AreaF.Height > 1)
                foreach (var sub in node.Nodes)
                    DrawNode(sub, depth + 1, drawChildren, z + zheight);
            

            // after drawing children, draw instance tracking on top of it all
            /*if (XRay.InstanceTracking && node.ObjType == XObjType.Class)
            {
               if (XRay.InstanceCount[node.ID] > 0)
                {
                    string count = XRay.InstanceCount[node.ID].ToString();
                    Rectangle x = new Rectangle(node.Area.Location, buffer.MeasureString(count, InstanceFont).ToSize());

                    if (node.Area.Contains(x))
                    {
                        buffer.FillRectangle(NothingBrush, x);
                        buffer.DrawString(count, InstanceFont, InstanceBrush, node.Area.Location.X + 2, node.Area.Location.Y + 2);
                    }
                }
            }*/
        }

        void BlendColors(Color src, ref Color tgt)
        {
            int a = ((src.A * src.A) >> 8) + ((tgt.A * (255 - src.A)) >> 8);
            int r = ((src.R * src.A) >> 8) + ((tgt.R * (255 - src.A)) >> 8);
            int g = ((src.G * src.A) >> 8) + ((tgt.G * (255 - src.A)) >> 8);
            int b = ((src.B * src.A) >> 8) + ((tgt.B * (255 - src.A)) >> 8);

            tgt = Color.FromArgb(a, r, g, b);
        }
    }
}
