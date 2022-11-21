using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Minimal_Painting_Demo;

public partial class Form1 : Form
{
    private static readonly Random Random = new();

    // Using Format32bppPArgb (premultiplied alpha) should improve GDI+ image drawing speed, at the cost of more calculations per brush dab 
    private readonly Bitmap bmp = new(1000, 1000, PixelFormat.Format32bppArgb);
    private BitmapData bmpData;
    private unsafe byte* scan0;
    private bool bmpLocked;

    private readonly Matrix canvasTransform = new();
    /// <summary>
    /// Just holds the size of the bitmap, used as convenience
    /// </summary>
    private readonly Rectangle canvasRect;
    private bool mouseDown, prevMouseDown, panning;
    private Point mouseDownPos;
    private PointF prevDabPos;
    private Color dabColor;

    public Form1()
    {
        InitializeComponent();
        // Some events are not accessible from the designer
        pictureBox1.MouseWheel += pictureBox1_MouseWheel;
        
        canvasRect = new Rectangle(Point.Empty, bmp.Size);
    }

    private unsafe void DoPainting(PointF mousePos)
    {
        var invertedCanvasMatrix = canvasTransform.Clone();
        invertedCanvasMatrix.Invert();

        // Since there's no method to just transform one point, we'll make an array of size 1
        var tmpArray = new[] { mousePos };
        invertedCanvasMatrix.TransformPoints(tmpArray);
        var relativeMousePos = tmpArray[0];

        if (!canvasRect.Contains((int)relativeMousePos.X, (int)relativeMousePos.Y))
        {
            return;
        }

        if (!bmpLocked)
        {
            bmpLocked = true;
            bmpData = bmp.LockBits(canvasRect, ImageLockMode.ReadWrite, bmp.PixelFormat);
            scan0 = (byte*)bmpData.Scan0.ToPointer();
        }

        const int dabSize = 25;

        if (!prevMouseDown) // Mouse was just pressed 
        {
            prevDabPos = relativeMousePos;
            Dab((int)relativeMousePos.X, (int)relativeMousePos.Y, dabSize, dabColor);
            pictureBox1.Invalidate();
        }
        else
        {
            var dabDistance = (float)Math.Sqrt(
                ((prevDabPos.X - relativeMousePos.X) * (prevDabPos.X - relativeMousePos.X)) +
                ((prevDabPos.Y - relativeMousePos.Y) * (prevDabPos.Y - relativeMousePos.Y)));

            const float spacing = .5f;
            if (dabDistance < spacing)
            {
                return;
            }

            // Interpolate between strokes. This is standard in most brush engines
            var nx = prevDabPos.X;
            var ny = prevDabPos.Y;
            var df = spacing / dabDistance;
            for (var f = df; f <= 1f; f += df)
            {
                nx = (f * relativeMousePos.X) + ((1f - f) * prevDabPos.X);
                ny = (f * relativeMousePos.Y) + ((1f - f) * prevDabPos.Y);
                Dab((int)nx, (int)ny, dabSize, dabColor);
            }

            prevDabPos.X = nx;
            prevDabPos.Y = ny;
            // TODO: We should only be invalidating the area that was updated. This will slow down performance a lot as the bitmap size increases
            pictureBox1.Invalidate(); 
        }
    }

    // For maximum performance ideally we would have this function written in a C library, which can then be P/Invoked
    private unsafe void Dab(int centerX, int centerY, int size, Color color)
    {
        size /= 2;
        // Basic multithreading here still nets us more performance, obviously it can be refined
        Parallel.For(-size, size, x =>
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
        });
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
        // https://stackoverflow.com/a/11025428/9286324
        var g = e.Graphics;
        // TODO: On initialization, fill the bitmap with our canvas color instead of it being transparent
        //g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.SmoothingMode = SmoothingMode.HighSpeed;

        g.Transform = canvasTransform;
        // If our bitmap is filled beforehand we won't need to render this
        g.FillRectangle(Brushes.White, canvasRect);

        if (bmpLocked)
        {
            bmpLocked = false;
            bmp.UnlockBits(bmpData);
        }
        // GDI+ is slow to draw images, perhaps using GDI would be faster?
        g.DrawImageUnscaled(bmp, canvasRect);
    }

    private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
    {
        if (!mouseDown)
        {
            return;
        }

        prevMouseDown = true;

        if (panning)
        {
            canvasTransform.Translate(e.Location.X - mouseDownPos.X, e.Location.Y - mouseDownPos.Y, MatrixOrder.Append);
            mouseDownPos = e.Location;
            pictureBox1.Invalidate();
        }
        else
        {
            DoPainting(e.Location);
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
    // An implementation of Wintab or Windows Ink will be necessary for the stylus to handle smoothly though.

    private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
    {
        prevMouseDown = mouseDown;
        mouseDown = true;
        mouseDownPos = e.Location;
        if (e.Button == MouseButtons.Right)
        {
            panning = true;
            pictureBox1.Cursor = Cursors.Hand;
            return;
        }

        dabColor = Color.FromArgb(16, Random.Next(255), Random.Next(255), Random.Next(255));

        DoPainting(e.Location);
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