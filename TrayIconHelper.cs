using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ATKLiteHub
{
    public static class TrayIconHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon CreateNumberIcon(int? percent, Color accentColor, bool charging = false, int currentProfile = 1)
        {
            return CreateCombinedIcon(percent, null, percent, charging, currentProfile);
        }

        public static Icon CreateCombinedIcon(int? mousePercent, int? headsetPercent, int? displayPercent, bool charging = false, int currentProfile = 1)
        {
            int size = 32;
            using var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color mouseBarColor = Color.FromArgb(255, 255, 130, 24); // Mouse: Vibrant Orange (#FF8218)
            Color headsetBarColor = Color.FromArgb(255, 239, 68, 68); // Headset: Bright Red (#EF4444)

            // Draw rounded dark card background
            using (var bgPath = CreateRoundedRect(new Rectangle(1, 1, size - 2, size - 2), 5))
            using (var bgBrush = new SolidBrush(Color.FromArgb(245, 16, 17, 21)))
            using (var borderPen = new Pen(Color.FromArgb(120, 48, 54, 66), 1.0f))
            {
                g.FillPath(bgBrush, bgPath);
                g.DrawPath(borderPen, bgPath);
            }

            // Line 1: Top bar for MOUSE (Orange line)
            using (var trackBrush = new SolidBrush(Color.FromArgb(140, 40, 44, 52)))
            using (var mouseFillBrush = new SolidBrush(mouseBarColor))
            {
                g.FillRectangle(trackBrush, 4, 3, size - 8, 3);
                if (mousePercent.HasValue)
                {
                    int fillW = (int)Math.Round(Math.Clamp(mousePercent.Value, 0, 100) / 100.0 * (size - 8));
                    if (fillW > 0)
                        g.FillRectangle(mouseFillBrush, 4, 3, fillW, 3);
                }
            }

            // Line 2: Middle bar for HEADSET (Red line)
            using (var trackBrush = new SolidBrush(Color.FromArgb(140, 40, 44, 52)))
            using (var headsetFillBrush = new SolidBrush(headsetBarColor))
            {
                g.FillRectangle(trackBrush, 4, 7, size - 8, 3);
                if (headsetPercent.HasValue)
                {
                    int fillW = (int)Math.Round(Math.Clamp(headsetPercent.Value, 0, 100) / 100.0 * (size - 8));
                    if (fillW > 0)
                        g.FillRectangle(headsetFillBrush, 4, 7, fillW, 3);
                }
            }

            // Text content: Lowest battery number
            string text = !displayPercent.HasValue ? "--" : (displayPercent.Value >= 100 ? "FL" : displayPercent.Value.ToString());
            float fontSize = text.Length <= 2 ? 12.0f : 9.5f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Point);

            Color textColor = Color.White;
            if (displayPercent.HasValue && displayPercent.Value <= 20 && !charging)
                textColor = Color.FromArgb(255, 255, 107, 129); // Alert soft red
            else if (charging)
                textColor = Color.FromArgb(255, 246, 229, 141); // Charging warm yellow
            else if (currentProfile == 2)
                textColor = Color.FromArgb(255, 255, 235, 230); // Clean bright text

            using var textBrush = new SolidBrush(textColor);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, font, textBrush, new RectangleF(0, 9, size, size - 8), sf);

            // If in Gaming Profile 2, draw small orange dot in bottom-right corner
            if (currentProfile == 2)
            {
                using var dotBrush = new SolidBrush(Color.FromArgb(255, 255, 78, 39));
                g.FillEllipse(dotBrush, size - 7, size - 7, 4, 4);
            }

            IntPtr hIcon = bmp.GetHicon();
            Icon cloned = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);
            return cloned;
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
