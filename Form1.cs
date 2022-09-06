using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Minimal_Painting_Demo;

public partial class Form1 : Form
{
    private static readonly Random random = new ();

    private readonly Bitmap bmp = new(2048, 2048, PixelFormat.Format32bppArgb);
    private BitmapData bmpData;
    private unsafe byte* scan0;
    private bool bmpLocked;

    private readonly Matrix canvasTransform = new();
    private readonly Rectangle canvasRect;
    private bool mouseDown, prevMouseDown, panning;
    private Point mouseDownPos;
    private PointF prevDabPos;
    private Color dabColor;

    public Form1()
    {
        InitializeComponent();
        // Some events are not accessible from designer
        pictureBox1.MouseWheel += pictureBox1_MouseWheel;

        canvasRect = new Rectangle(Point.Empty, bmp.Size);
    }

    private unsafe void DoDrawing(PointF mousePos)
    {
        var invertedCanvasMatrix = canvasTransform.Clone();
        invertedCanvasMatrix.Invert();
        var canvasMousePos = new PointF(
            (mousePos.X * invertedCanvasMatrix.Elements[0] + mousePos.Y * invertedCanvasMatrix.Elements[2]) + invertedCanvasMatrix.Elements[4],
            (mousePos.X * invertedCanvasMatrix.Elements[1] + mousePos.Y * invertedCanvasMatrix.Elements[3]) + invertedCanvasMatrix.Elements[5]);

        if (!canvasRect.Contains((int)canvasMousePos.X, (int)canvasMousePos.Y))
        {
            return;
        }

        if (!bmpLocked)
        {
            bmpLocked = true;
            bmpData = bmp.LockBits(canvasRect, ImageLockMode.ReadWrite, bmp.PixelFormat);
            scan0 = (byte*)bmpData.Scan0.ToPointer();
        }

        const int dabSize = 128;

        if (!prevMouseDown) // Mouse was just pressed 
        {
            prevDabPos = canvasMousePos;
            Dab((int)canvasMousePos.X, (int)canvasMousePos.Y, dabSize, dabColor);
            pictureBox1.Invalidate();
        }
        else
        {
            var dabDistance = (float)Math.Sqrt(
                ((prevDabPos.X - canvasMousePos.X) * (prevDabPos.X - canvasMousePos.X)) +
                ((prevDabPos.Y - canvasMousePos.Y) * (prevDabPos.Y - canvasMousePos.Y)));

            const float spacing = .5f;

            if (dabDistance < spacing)
            {
                return;
            }

            var nx = prevDabPos.X;
            var ny = prevDabPos.Y;
            var df = spacing / dabDistance;
            for (var f = df; f <= 1f; f += df)
            {
                nx = (f * canvasMousePos.X) + ((1f - f) * prevDabPos.X);
                ny = (f * canvasMousePos.Y) + ((1f - f) * prevDabPos.Y);
                Dab((int)nx, (int)ny, dabSize, dabColor);
            }

            prevDabPos.X = nx;
            prevDabPos.Y = ny;
            pictureBox1.Invalidate(); // TODO - We should only be invalidating the area that was updated
        }
    }

    private unsafe void Dab(int centerX, int centerY, int size, Color color)
    {
        size /= 2;
        for (var x = -size; x < size; x++)
        {
            for (var y = -size; y < size; y++)
            {
                var nx = centerX + x;
                var ny = centerY + y;
                if (!canvasRect.Contains(nx, ny))
                {
                    continue;
                }

                const int bytesPerPixel = 4;
                var data = scan0 + (ny * bmpData.Stride) + (nx * bytesPerPixel);

                ref var a = ref data[3];
                ref var r = ref data[2];
                ref var g = ref data[1];
                ref var b = ref data[0];

                AlphaBlend(color.A, color.R, color.G, color.B, a, r, g, b, out var aOut, out var rOut, out var gOut, out var bOut);

                a = aOut;
                r = rOut;
                g = gOut;
                b = bOut;
            }
        }
    }

    // https://stackoverflow.com/a/64655571/9286324
    private static void AlphaBlend(
        byte aA, byte rA, byte gA, byte bA,
        byte aB, byte rB, byte gB, byte bB,
        out byte aOut, out byte rOut, out byte gOut, out byte bOut)
    {
        aOut = (byte)(aA + aB * (255 - aA) / 255);
        if (aOut == 0)
        {
            rOut = rB;
            gOut = gB;
            bOut = bB;
            aOut = aB;
            return;
        }

        rOut = (byte)Math.Clamp((rA * aA + rB * aB * (255 - aA) / 255) / aOut, 0, 255);
        gOut = (byte)Math.Clamp((gA * aA + gB * aB * (255 - aA) / 255) / aOut, 0, 255);
        bOut = (byte)Math.Clamp((bA * aA + bB * aB * (255 - aA) / 255) / aOut, 0, 255);
    }

    #region pictureBox1 events

    private void pictureBox1_Paint(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        e.Graphics.Transform = canvasTransform;
        e.Graphics.FillRectangle(Brushes.White, canvasRect);

        if (bmpLocked)
        {
            bmpLocked = false;
            bmp.UnlockBits(bmpData);
        }
        e.Graphics.DrawImageUnscaled(bmp, canvasRect);
    }

    private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
    {
        if (!mouseDown)
        {
            return;
        }

        if (panning)
        {
            canvasTransform.Translate(e.Location.X - mouseDownPos.X, e.Location.Y - mouseDownPos.Y, MatrixOrder.Append);
            mouseDownPos = e.Location;
            pictureBox1.Invalidate();
        }
        else
        {
            DoDrawing(e.Location);
        }
    }

    private void pictureBox1_MouseWheel(object sender, MouseEventArgs e)
    {
        void ZoomAt(float scale, PointF center)
        {
            canvasTransform.Multiply(new Matrix(scale, 0.0f, 0.0f, scale, center.X - scale * center.X, center.Y - scale * center.Y), MatrixOrder.Append);
            pictureBox1.Invalidate();
        }

        const float zoomStep = 0.8f;

        if (e.Delta > 0)
        {
            ZoomAt(1f / zoomStep, e.Location);
        }
        else if (e.Delta < 0)
        {
            ZoomAt(zoomStep, e.Location);
        }
    }

    // A stylus will also trigger the control's mouse events.
    // An implementation of Wintab will be necessary for the stylus to handle smoothly though.

    private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            panning = true;

            pictureBox1.Cursor = Cursors.Hand;
        }

        prevMouseDown = mouseDown;
        mouseDown = true;
        mouseDownPos = e.Location;
        
        dabColor = Color.FromArgb(16, random.Next(255), random.Next(255), random.Next(255));
    }

    private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            panning = false;
            pictureBox1.Cursor = Cursors.Default;
        }
        prevMouseDown = mouseDown;
        mouseDown = false;
    }


    #endregion
}