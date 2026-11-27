// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Drawing;
using System.Drawing.Drawing2D;
using FunkyLauncher.Core;
using FunkyLauncher.Services;
using WinForms = System.Windows.Forms;

namespace FunkyLauncher.Views;

/// <summary>
/// Funky Screen area picker: freezes the screen under the mouse and lets the player drag a frame over it.
/// Runs on its own thread in real-pixel mode, so it is exact on any monitor scale. Esc / right click — cancel.
/// </summary>
public static class ScreenAreaPicker
{
    /// <summary>The picked part of the screen, or null if cancelled.</summary>
    public static Task<Bitmap?> PickAsync()
    {
        var done = new TaskCompletionSource<Bitmap?>();
        var t = new Thread(() =>
        {
            try
            {
                FunkyScreen.InRealPixels(() =>
                {
                    var screen = FunkyScreen.MonitorUnderMouse();
                    var shot = FunkyScreen.Grab(screen);
                    using var form = new PickForm(shot, screen);
                    WinForms.Application.Run(form);
                    var sel = form.Selection;
                    if (sel is { Width: >= 2, Height: >= 2 } r) done.TrySetResult(shot.Clone(r, shot.PixelFormat));
                    else done.TrySetResult(null);
                    shot.Dispose();
                    return 0;
                });
            }
            catch (Exception e) { done.TrySetException(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return done.Task;
    }

    private sealed class PickForm : WinForms.Form
    {
        private readonly Bitmap _shot;
        private Point? _start;
        private Point _now;
        public Rectangle? Selection { get; private set; }

        public PickForm(Bitmap shot, Rectangle screen)
        {
            _shot = shot;
            FormBorderStyle = WinForms.FormBorderStyle.None;
            StartPosition = WinForms.FormStartPosition.Manual;
            AutoScaleMode = WinForms.AutoScaleMode.None;
            Bounds = screen;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = WinForms.Cursors.Cross;
            Text = "Funky Screen";
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }

        // Client point → picture pixel (the window covers the monitor, so this is 1:1 unless Windows scales it).
        private Point ToShot(Point p) => new(
            Math.Clamp(p.X * _shot.Width / Math.Max(1, ClientSize.Width), 0, _shot.Width),
            Math.Clamp(p.Y * _shot.Height / Math.Max(1, ClientSize.Height), 0, _shot.Height));

        private Rectangle Frame()
        {
            if (_start is not { } s) return Rectangle.Empty;
            return Rectangle.FromLTRB(Math.Min(s.X, _now.X), Math.Min(s.Y, _now.Y), Math.Max(s.X, _now.X), Math.Max(s.Y, _now.Y));
        }

        protected override void OnMouseDown(WinForms.MouseEventArgs e)
        {
            if (e.Button == WinForms.MouseButtons.Right) { Close(); return; }
            _start = _now = e.Location; Invalidate();
        }

        protected override void OnMouseMove(WinForms.MouseEventArgs e)
        {
            _now = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(WinForms.MouseEventArgs e)
        {
            if (_start == null || e.Button != WinForms.MouseButtons.Left) return;
            _now = e.Location;
            var f = Frame();
            var a = ToShot(f.Location); var b = ToShot(new Point(f.Right, f.Bottom));
            Selection = Rectangle.FromLTRB(a.X, a.Y, b.X, b.Y);
            Close();
        }

        protected override void OnKeyDown(WinForms.KeyEventArgs e)
        {
            if (e.KeyCode == WinForms.Keys.Escape) Close();
            else if (e.KeyCode == WinForms.Keys.Enter) { Selection = new Rectangle(0, 0, _shot.Width, _shot.Height); Close(); }
        }

        protected override void OnPaintBackground(WinForms.PaintEventArgs e) { }

        protected override void OnPaint(WinForms.PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(_shot, ClientRectangle);
            var f = Frame();
            using var dim = new SolidBrush(Color.FromArgb(120, 7, 7, 13));
            using (var outside = new Region(ClientRectangle))
            {
                if (f.Width > 0 && f.Height > 0) outside.Exclude(f);
                g.FillRegion(dim, outside);
            }
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
            if (f.Width > 0 && f.Height > 0)
            {
                using var pen = new Pen(Color.FromArgb(255, 0xFF, 0x3F, 0xD2), 2);
                g.DrawRectangle(pen, f);
                var a = ToShot(f.Location); var b = ToShot(new Point(f.Right, f.Bottom));
                var size = $"{b.X - a.X} × {b.Y - a.Y}";
                var at = new PointF(f.X, Math.Max(0, f.Y - 20));
                var sz = g.MeasureString(size, font);
                using var bg = new SolidBrush(Color.FromArgb(220, 7, 7, 13));
                g.FillRectangle(bg, at.X, at.Y, sz.Width + 8, sz.Height + 2);
                g.DrawString(size, font, Brushes.White, at.X + 4, at.Y + 1);
            }
            else
            {
                var hint = Loc.T("screen.pick.hint");
                var sz = g.MeasureString(hint, font);
                using var bg = new SolidBrush(Color.FromArgb(220, 7, 7, 13));
                var x = (ClientSize.Width - sz.Width) / 2; var y = 24f;
                g.FillRectangle(bg, x - 12, y - 6, sz.Width + 24, sz.Height + 12);
                g.DrawString(hint, font, Brushes.White, x, y);
            }
        }
    }
}
