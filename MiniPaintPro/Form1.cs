using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MiniPaintPro   // <-- ha más a projekt neve, ezt is írd át
{
    public class Form1 : Form
    {
        enum Tool { Pen, Eraser, Line, Rect, Ellipse }

        PictureBox canvas;
        ToolStrip ts;
        TrackBar tbSize;
        CheckBox cbFill, cbGrid, cbNeon;
        Label lblSize;

        Bitmap bmp;              // fő vászon
        Bitmap preview;          // előnézet alakzat rajzolás közben
        Stack<Bitmap> undo = new Stack<Bitmap>();
        Stack<Bitmap> redo = new Stack<Bitmap>();

        bool isDrawing = false;
        Tool currentTool = Tool.Pen;
        Color currentColor = Color.DeepSkyBlue;
        int brushSize = 8;
        Point startPt, lastPt;

        public Form1()
        {
            // FONTOS: nincs InitializeComponent()
            Text = "MiniPaint Pro - WinForms (.NET Framework 4.7.2)";
            Width = 1200; Height = 800;
            DoubleBuffered = true;

            // TOOLSTRIP
            ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            var btnNew = new ToolStripButton("Új") { ToolTipText = "Új (Ctrl+N)" };
            var btnOpen = new ToolStripButton("Megnyitás") { ToolTipText = "Megnyitás (Ctrl+O)" };
            var btnSave = new ToolStripButton("Mentés") { ToolTipText = "Mentés (Ctrl+S)" };
            var btnUndo = new ToolStripButton("Visszavonás") { ToolTipText = "Visszavonás (Ctrl+Z)" };
            var btnRedo = new ToolStripButton("Újra") { ToolTipText = "Újra (Ctrl+Y)" };

            var ddTools = new ToolStripDropDownButton("Eszköz");
            ddTools.DropDownItems.Add("Ecset (1)", null, (s, e) => SetTool(Tool.Pen));
            ddTools.DropDownItems.Add("Radír (2)", null, (s, e) => SetTool(Tool.Eraser));
            ddTools.DropDownItems.Add("Vonal (3)", null, (s, e) => SetTool(Tool.Line));
            ddTools.DropDownItems.Add("Téglalap (4)", null, (s, e) => SetTool(Tool.Rect));
            ddTools.DropDownItems.Add("Ellipszis (5)", null, (s, e) => SetTool(Tool.Ellipse));

            var btnColor = new ToolStripButton("Szín") { ToolTipText = "Szín választás" };
            ts.Items.AddRange(new ToolStripItem[] { btnNew, btnOpen, btnSave, new ToolStripSeparator(), btnUndo, btnRedo, new ToolStripSeparator(), ddTools, btnColor });
            Controls.Add(ts);

            btnNew.Click += (s, e) => NewCanvas();
            btnOpen.Click += (s, e) => OpenImage();
            btnSave.Click += (s, e) => SaveImage();
            btnUndo.Click += (s, e) => Undo();
            btnRedo.Click += (s, e) => Redo();
            btnColor.Click += (s, e) => PickColor();

            // OLDALSÁV
            var rightPanel = new Panel { Dock = DockStyle.Right, Width = 220, Padding = new Padding(10) };
            lblSize = new Label { Text = $"Ecset méret: {brushSize}px", AutoSize = true, Top = 10, Left = 10 };
            tbSize = new TrackBar { Minimum = 1, Maximum = 60, Value = brushSize, TickFrequency = 5, Width = 180, Left = 10, Top = 35 };
            cbFill = new CheckBox { Text = "Kitöltés alakzatoknál", Left = 10, Top = 100, AutoSize = true };
            cbGrid = new CheckBox { Text = "Rács (25px)", Left = 10, Top = 130, AutoSize = true };
            cbNeon = new CheckBox { Text = "Neon hatás", Left = 10, Top = 160, AutoSize = true };
            rightPanel.Controls.AddRange(new Control[] { lblSize, tbSize, cbFill, cbGrid, cbNeon });
            Controls.Add(rightPanel);

            tbSize.ValueChanged += (s, e) => { brushSize = tbSize.Value; lblSize.Text = $"Ecset méret: {brushSize}px"; canvas?.Invalidate(); };

            // CANVAS
            canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White };
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Paint += Canvas_Paint;
            canvas.Resize += Canvas_Resize;
            Controls.Add(canvas);

            // Hotkeyk
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            // FIGYELEM: NEM hívunk itt CreateCanvas-t!
            // A vásznat az OnLoad-ban hozzuk létre, amikor már van érvényes méret.
        }

        // A vászon és a bitképek biztos inicializálása
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int w = Math.Max(800, canvas.ClientSize.Width);
            int h = Math.Max(600, canvas.ClientSize.Height);
            CreateCanvas(w, h, clearUndo: true);
        }

        void SetTool(Tool t) { currentTool = t; StatusText(); }
        void StatusText() => Text = $"MiniPaint Pro - {currentTool} | {brushSize}px | Szín: #{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}";

        void CreateCanvas(int w, int h, bool clearUndo)
        {
            if (w <= 0 || h <= 0) { w = 800; h = 600; } // biztonsági alapméret

            var newBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(newBmp)) g.Clear(Color.White);

            if (bmp != null)
            {
                using (var g = Graphics.FromImage(newBmp)) g.DrawImage(bmp, Point.Empty);
                bmp.Dispose();
            }
            bmp = newBmp;

            if (preview != null) preview.Dispose();
            preview = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            if (clearUndo)
            {
                foreach (var b in undo) b.Dispose(); undo.Clear();
                foreach (var b in redo) b.Dispose(); redo.Clear();
            }

            canvas.Invalidate();
        }

        void PushUndo()
        {
            if (bmp == null) return;
            undo.Push((Bitmap)bmp.Clone());
            while (undo.Count > 20) { var drop = undo.Pop(); drop.Dispose(); }
            foreach (var b in redo) b.Dispose(); redo.Clear();
        }

        void Undo()
        {
            if (undo.Count == 0 || bmp == null) return;
            redo.Push((Bitmap)bmp.Clone());
            var back = undo.Pop();
            bmp.Dispose();
            bmp = (Bitmap)back.Clone();
            back.Dispose();
            canvas.Invalidate();
        }

        void Redo()
        {
            if (redo.Count == 0 || bmp == null) return;
            undo.Push((Bitmap)bmp.Clone());
            var fwd = redo.Pop();
            bmp.Dispose();
            bmp = (Bitmap)fwd.Clone();
            fwd.Dispose();
            canvas.Invalidate();
        }

        void NewCanvas()
        {
            if (MessageBox.Show("Biztos új képet nyitsz? A jelenlegi tartalom el fog veszni.", "Figyelem", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                return;
            int w = Math.Max(800, canvas.ClientSize.Width);
            int h = Math.Max(600, canvas.ClientSize.Height);
            CreateCanvas(w, h, clearUndo: true);
        }

        void OpenImage()
        {
            if (MessageBox.Show("Biztos képet nyitsz? A jelenlegi tartalom el fog veszni.", "Figyelem", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                return;

            using (var ofd = new OpenFileDialog { Filter = "Képek|*.png;*.jpg;*.jpeg;*.bmp" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    using (var img = Image.FromFile(ofd.FileName))
                    {
                        CreateCanvas(Math.Max(800, img.Width), Math.Max(600, img.Height), clearUndo: true);
                        using (var g = Graphics.FromImage(bmp))
                            g.DrawImage(img, 0, 0, bmp.Width, bmp.Height);
                        canvas.Invalidate();
                    }
                }
            }
        }

        void SaveImage()
        {
            if (bmp == null) return;
            using (var sfd = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp", FileName = "rajz.png" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    var ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                    var fmt = ext == ".bmp" ? ImageFormat.Bmp
                              : (ext == ".jpg" || ext == ".jpeg") ? ImageFormat.Jpeg
                              : ImageFormat.Png;
                    bmp.Save(sfd.FileName, fmt);
                }
            }
        }

        void PickColor()
        {
            using (var cd = new ColorDialog { Color = currentColor, FullOpen = true })
                if (cd.ShowDialog() == DialogResult.OK) { currentColor = cd.Color; StatusText(); }
        }

        // ====== RAJZOLÁS ======

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || bmp == null) return;
            isDrawing = true;
            startPt = lastPt = e.Location;
            PushUndo();
            if (currentTool == Tool.Pen || currentTool == Tool.Eraser) DrawStroke(lastPt, e.Location);
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDrawing || bmp == null) return;

            if (currentTool == Tool.Pen || currentTool == Tool.Eraser)
            {
                DrawStroke(lastPt, e.Location);
                lastPt = e.Location;
            }
            else
            {
                if (preview != null) preview.Dispose();
                preview = (Bitmap)bmp.Clone();
                using (var g = Graphics.FromImage(preview)) DrawShape(g, startPt, e.Location, previewMode: true);
                canvas.Invalidate();
            }
        }

        void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isDrawing || bmp == null) return;
            isDrawing = false;

            if (currentTool != Tool.Pen && currentTool != Tool.Eraser)
                using (var g = Graphics.FromImage(bmp)) DrawShape(g, startPt, e.Location, previewMode: false);

            canvas.Invalidate();
        }

        void DrawStroke(Point from, Point to)
        {
            if (bmp == null) return;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                if (currentTool == Tool.Eraser)
                {
                    using (var p = new Pen(Color.White, brushSize) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                        g.DrawLine(p, from, to);
                }
                else
                {
                    if (cbNeon.Checked)
                        for (int i = 3; i >= 1; i--)
                        {
                            int w = brushSize + i * 4;
                            var c = Color.FromArgb(40 + i * 20, currentColor);
                            using (var p = new Pen(c, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                                g.DrawLine(p, from, to);
                        }
                    using (var p = new Pen(currentColor, brushSize) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                        g.DrawLine(p, from, to);
                }
            }
            canvas.Invalidate();
        }

        Rectangle RectFromPoints(Point a, Point b)
            => new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        void DrawShape(Graphics g, Point a, Point b, bool previewMode)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = RectFromPoints(a, b);

            if (currentTool == Tool.Line)
            {
                if (cbNeon.Checked && !previewMode)
                    for (int i = 3; i >= 1; i--)
                    {
                        int w = brushSize + i * 4;
                        var c = Color.FromArgb(40 + i * 20, currentColor);
                        using (var p = new Pen(c, w)) g.DrawLine(p, a, b);
                    }
                using (var p = new Pen(currentColor, brushSize)) g.DrawLine(p, a, b);
            }
            else if (currentTool == Tool.Rect)
            {
                if (cbFill.Checked) using (var br = new SolidBrush(currentColor)) g.FillRectangle(br, r);
                else
                {
                    if (cbNeon.Checked && !previewMode)
                        for (int i = 3; i >= 1; i--)
                        {
                            int w = brushSize + i * 4;
                            var c = Color.FromArgb(40 + i * 20, currentColor);
                            using (var p = new Pen(c, w)) g.DrawRectangle(p, r);
                        }
                    using (var p = new Pen(currentColor, brushSize)) g.DrawRectangle(p, r);
                }
            }
            else if (currentTool == Tool.Ellipse)
            {
                if (cbFill.Checked) using (var br = new SolidBrush(currentColor)) g.FillEllipse(br, r);
                else
                {
                    if (cbNeon.Checked && !previewMode)
                        for (int i = 3; i >= 1; i--)
                        {
                            int w = brushSize + i * 4;
                            var c = Color.FromArgb(40 + i * 20, currentColor);
                            using (var p = new Pen(c, w)) g.DrawEllipse(p, r);
                        }
                    using (var p = new Pen(currentColor, brushSize)) g.DrawEllipse(p, r);
                }
            }
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (bmp == null) return;
            var g = e.Graphics;
            var toDraw = (isDrawing && (currentTool == Tool.Line || currentTool == Tool.Rect || currentTool == Tool.Ellipse) && preview != null)
                         ? preview : bmp;
            g.DrawImageUnscaled(toDraw, 0, 0);

            if (cbGrid.Checked)
                using (var p = new Pen(Color.FromArgb(40, 0, 0, 0), 1))
                {
                    for (int x = 0; x < canvas.Width; x += 25) g.DrawLine(p, x, 0, x, canvas.Height);
                    for (int y = 0; y < canvas.Height; y += 25) g.DrawLine(p, 0, y, canvas.Width, y);
                }
        }

        void Canvas_Resize(object sender, EventArgs e)
        {
            if (canvas.Width <= 0 || canvas.Height <= 0) return;
            if (bmp == null) { CreateCanvas(Math.Max(800, canvas.ClientSize.Width), Math.Max(600, canvas.ClientSize.Height), clearUndo: false); return; }
            var old = (Bitmap)bmp.Clone();
            CreateCanvas(canvas.ClientSize.Width, canvas.ClientSize.Height, clearUndo: false);
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(old, 0, 0);
            old.Dispose();
            canvas.Invalidate();
        }

        void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.S) { SaveImage(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenImage(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.N) { NewCanvas(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.D1) SetTool(Tool.Pen);
            else if (e.KeyCode == Keys.D2) SetTool(Tool.Eraser);
            else if (e.KeyCode == Keys.D3) SetTool(Tool.Line);
            else if (e.KeyCode == Keys.D4) SetTool(Tool.Rect);
            else if (e.KeyCode == Keys.D5) SetTool(Tool.Ellipse);
        }
    }
}
