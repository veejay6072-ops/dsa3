using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Dsa3
{
    // A custom panel that prevents flickering (DoubleBuffered) and supports transparency
    class CanvasPanel : Panel
    {
        public CanvasPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
    }

    class Step
    {
        public string Text = "";
        public int Bit;
    }

    enum Phase { Idle, Divide, Pop, Done }

    public partial class Form1 : Form
    {
        // Cartoon Color Palette
        static readonly Color Sky = Color.FromArgb(190, 225, 255);
        static readonly Color Wall = Color.FromArgb(240, 110, 80);       // Vibrant brick orange
        static readonly Color WallDark = Color.FromArgb(190, 70, 50);    // Brick outline
        static readonly Color RoofColor = Color.FromArgb(60, 75, 100);   // Dark slate roof
        static readonly Color RoofOutline = Color.FromArgb(30, 45, 60);  // Darker outline
        static readonly Color WindowLit = Color.FromArgb(255, 220, 80);  // Glowing yellow for non-zero
        static readonly Color WindowDim = Color.FromArgb(180, 230, 250); // Light blue glass for zero
        static readonly Color TextDark = Color.FromArgb(40, 40, 40);

        // Controls
        TextBox txtNumber = new TextBox();
        Button btnConvert = new Button();
        Button btnStep = new Button();
        Button btnReset = new Button();
        ComboBox cmbBase = new ComboBox();
        Label lblStatus = new Label();
        Label lblFinal = new Label();
        Panel pnlCenter = new Panel();
        FlowLayoutPanel floors = new FlowLayoutPanel();
        FlowLayoutPanel output = new FlowLayoutPanel();
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        // State
        Stack<Step> stack = new Stack<Step>();
        List<Step> plan = new List<Step>();
        List<int> popped = new List<int>();
        int index;
        int radix = 2;
        string baseName = "Binary";

        static string Dg(int v) { return "0123456789ABCDEF"[v].ToString(); }
        Phase phase = Phase.Idle;

        public Form1()
        {
            InitializeComponent();

            Text = "Stack Converter Visualizer";
            ClientSize = new Size(680, 760);
            MinimumSize = new Size(540, 600);
            Font = new Font("Segoe UI", 10f);
            BackColor = Color.White;

            // Top bar: input, buttons, status
            var top = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(15) };
            top.Paint += DrawPanelShadow;

            var lbl = new Label { Text = "Decimal number:", Left = 15, Top = 12, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            txtNumber.SetBounds(15, 34, 120, 32);
            txtNumber.Font = new Font("Segoe UI", 13f);
            txtNumber.Text = "13";
            txtNumber.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnConvert.PerformClick(); }
            };

            var lblBase = new Label { Text = "Convert to:", Left = 150, Top = 12, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            cmbBase.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBase.Font = new Font("Segoe UI", 12f);
            cmbBase.Items.AddRange(new object[] { "Binary (base 2)", "Octal (base 8)", "Hexadecimal (base 16)" });
            cmbBase.SelectedIndex = 0;
            cmbBase.SetBounds(150, 34, 180, 32);

            StyleButton(btnConvert, "Convert", 350, true);
            StyleButton(btnStep, "Next step", 450, false);
            StyleButton(btnReset, "Reset", 550, false);

            btnConvert.Click += (s, e) => { if (Start()) timer.Start(); };
            btnStep.Click += (s, e) =>
            {
                timer.Stop();
                if (phase == Phase.Idle || phase == Phase.Done) { if (!Start()) return; }
                Advance();
            };
            btnReset.Click += (s, e) => ResetAll();

            lblStatus.SetBounds(15, 74, 520, 22);
            lblStatus.Font = new Font("Segoe UI", 10f, FontStyle.Italic);
            lblStatus.Text = "Enter a number and press Convert to start building the stack.";
            top.Controls.AddRange(new Control[] { lbl, txtNumber, lblBase, cmbBase, btnConvert, btnStep, btnReset, lblStatus });

            // Bottom: popped bits and final result
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 140, Padding = new Padding(15) };
            bottom.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(Color.LightGray, 2), 0, 0, bottom.Width, 0);
            };

            var lblOut = new Label { Text = "Popped values (read left to right):", Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            output.Dock = DockStyle.Top;
            output.Height = 65;
            output.WrapContents = false;
            output.AutoScroll = true;

            lblFinal.Dock = DockStyle.Top;
            lblFinal.Height = 40;
            lblFinal.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            lblFinal.ForeColor = Color.FromArgb(20, 120, 212);

            bottom.Controls.Add(lblFinal);
            bottom.Controls.Add(output);
            bottom.Controls.Add(lblOut);

            // Center: the building background and layout
            pnlCenter.Dock = DockStyle.Fill;
            pnlCenter.BackColor = Sky; // Cartoon sky background
            pnlCenter.AutoScroll = true;

            floors.FlowDirection = FlowDirection.BottomUp;
            floors.WrapContents = false;
            floors.Width = 360;
            floors.Margin = new Padding(0);
            floors.BackColor = Color.Transparent;

            pnlCenter.Controls.Add(floors);
            pnlCenter.Resize += (s, e) => LayoutBuilding();

            Controls.Add(pnlCenter);
            Controls.Add(bottom);
            Controls.Add(top);

            timer.Interval = 750;
            timer.Tick += (s, e) => { if (!Advance()) timer.Stop(); };

            LayoutBuilding();
            Render();
        }

        void DrawPanelShadow(object sender, PaintEventArgs e)
        {
            var p = sender as Panel;
            e.Graphics.DrawLine(new Pen(Color.LightGray, 2), 0, p.Height - 1, p.Width, p.Height - 1);
        }

        static void StyleButton(Button b, string text, int left, bool primary)
        {
            b.Text = text;
            b.SetBounds(left, 32, 92, 36);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 2;
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(15, 95, 175) : Color.LightGray;
            b.BackColor = primary ? Color.FromArgb(20, 120, 212) : Color.White;
            b.ForeColor = primary ? Color.White : TextDark;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
        }

        void LayoutBuilding()
        {
            floors.Height = Math.Max(pnlCenter.ClientSize.Height, 200);
            floors.Left = Math.Max(0, (pnlCenter.ClientSize.Width - floors.Width) / 2);
            floors.Top = 0;
        }

        bool Start()
        {
            timer.Stop();
            if (!int.TryParse(txtNumber.Text.Trim(), out int n) || n < 0 || n > 65535)
            {
                lblStatus.Text = "Please enter a positive whole number (0 to 65535).";
                return false;
            }

            radix = new[] { 2, 8, 16 }[cmbBase.SelectedIndex];
            baseName = new[] { "Binary", "Octal", "Hexadecimal" }[cmbBase.SelectedIndex];

            plan.Clear();
            stack.Clear();
            popped.Clear();
            index = 0;

            if (n == 0)
            {
                plan.Add(new Step { Text = "Input is 0 (edge case)", Bit = 0 });
            }
            else
            {
                while (n > 0)
                {
                    plan.Add(new Step { Text = $"{n} ÷ {radix} = {n / radix} r {n % radix}", Bit = n % radix });
                    n /= radix;
                }
            }

            phase = Phase.Divide;
            lblStatus.Text = $"Building the stack... Dividing by {radix}.";
            Render();
            return true;
        }

        bool Advance()
        {
            if (phase == Phase.Idle || phase == Phase.Done) return false;

            if (phase == Phase.Divide)
            {
                Step s = plan[index++];
                stack.Push(s);
                if (index == plan.Count)
                {
                    phase = Phase.Pop;
                    lblStatus.Text = "Building complete! Now let's pop the stack from the top.";
                }
                else
                {
                    lblStatus.Text = $"Pushed remainder {Dg(s.Bit)} onto the stack.";
                }
            }
            else
            {
                int bit = stack.Pop().Bit;
                popped.Add(bit);
                if (stack.Count == 0)
                {
                    phase = Phase.Done;
                    lblStatus.Text = "Stack is completely empty. Conversion is done!";
                }
                else
                {
                    lblStatus.Text = $"Popped {Dg(bit)} from the top of the stack.";
                }
            }

            Render();
            return phase != Phase.Done;
        }

        void ResetAll()
        {
            timer.Stop();
            stack.Clear();
            plan.Clear();
            popped.Clear();
            phase = Phase.Idle;
            lblStatus.Text = "Enter a number and press Convert to start building the stack.";
            Render();
        }

        void Render()
        {
            floors.SuspendLayout();
            floors.Controls.Clear();

            floors.Controls.Add(MakeLobby());
            foreach (Step s in stack.ToArray().Reverse()) floors.Controls.Add(MakeFloor(s));
            floors.Controls.Add(MakeRoof());

            floors.ResumeLayout();

            output.SuspendLayout();
            output.Controls.Clear();
            foreach (int b in popped)
            {
                var lbl = new Label
                {
                    Text = Dg(b),
                    Size = new Size(50, 50),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                    BackColor = b != 0 ? WindowLit : WindowDim,
                    ForeColor = TextDark,
                    Margin = new Padding(0, 0, 10, 0)
                };

                // Draw a cute window frame border around the popped items
                lbl.Paint += (s, e) => {
                    e.Graphics.DrawRectangle(new Pen(WallDark, 4), 0, 0, lbl.Width - 1, lbl.Height - 1);
                };
                output.Controls.Add(lbl);
            }
            output.ResumeLayout();

            lblFinal.Text = phase == Phase.Done ? $"{baseName}: " + string.Concat(popped.Select(Dg)) : "";
        }

        Control MakeFloor(Step s)
        {
            var p = new CanvasPanel { Size = new Size(360, 60), Margin = new Padding(0) };

            p.Paint += (obj, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw the floor block
                g.FillRectangle(new SolidBrush(Wall), 40, 0, 280, 60);
                g.DrawRectangle(new Pen(WallDark, 4), 40, 0, 280, 60);

                // Line separating floors
                g.DrawLine(new Pen(WallDark, 4), 40, 0, 320, 0);
            };

            var lblText = new Label
            {
                Text = s.Text,
                Left = 55,
                Top = 15,
                Width = 200,
                Height = 30,
                Font = new Font("Consolas", 12f, FontStyle.Bold), // Monospace for math
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblBit = new Label
            {
                Text = Dg(s.Bit),
                Left = 260,
                Top = 12,
                Size = new Size(40, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                BackColor = s.Bit != 0 ? WindowLit : WindowDim,
                ForeColor = TextDark
            };

            // Draw window frame for the remainder bit
            lblBit.Paint += (obj, e) => {
                e.Graphics.DrawRectangle(new Pen(WallDark, 3), 0, 0, lblBit.Width - 1, lblBit.Height - 1);
            };

            p.Controls.Add(lblBit);
            p.Controls.Add(lblText);
            return p;
        }

        Control MakeRoof()
        {
            var p = new CanvasPanel { Size = new Size(360, 70), Margin = new Padding(0) };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw triangular roof
                Point[] pts = { new Point(20, 70), new Point(180, 10), new Point(340, 70) };
                g.FillPolygon(new SolidBrush(RoofColor), pts);
                g.DrawPolygon(new Pen(RoofOutline, 5), pts);
            };
            return p;
        }

        Control MakeLobby()
        {
            var p = new CanvasPanel { Size = new Size(360, 80), Margin = new Padding(0) };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Main lobby structure
                g.FillRectangle(new SolidBrush(Wall), 40, 0, 280, 80);
                g.DrawRectangle(new Pen(WallDark, 4), 40, 0, 280, 80);

                // Double Doors
                g.FillRectangle(new SolidBrush(Color.FromArgb(140, 90, 60)), 150, 30, 60, 50);
                g.DrawRectangle(new Pen(Color.FromArgb(90, 50, 30), 4), 150, 30, 60, 50);

                // Door split and knobs
                g.DrawLine(new Pen(Color.FromArgb(90, 50, 30), 3), 180, 30, 180, 80);
                g.FillEllipse(new SolidBrush(Color.Gold), 170, 55, 6, 6); // Left knob
                g.FillEllipse(new SolidBrush(Color.Gold), 184, 55, 6, 6); // Right knob
            };
            return p;
        }
    }
}