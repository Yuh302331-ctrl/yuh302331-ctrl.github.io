using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MakeIcon
{
    public static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<byte[]> blobs = new List<byte[]>();
        foreach (int size in sizes) blobs.Add(size <= 64 ? RenderDib(size) : RenderPng(size));

        using (FileStream fs = new FileStream(output, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)blobs[i].Length);
                w.Write((uint)offset);
                offset += blobs[i].Length;
            }
            foreach (byte[] blob in blobs) w.Write(blob);
        }
        Console.WriteLine("wrote " + output + " with " + sizes.Length + " sizes");
    }

    // Returns a 32bpp BGRA bottom-up DIB (BITMAPINFOHEADER) including the AND mask.
    private static byte[] RenderDib(int size)
    {
        using (Bitmap bmp = Render(size))
        {
            int stride = size * 4;
            int maskStride = ((size + 31) / 32) * 4;
            byte[] xor = new byte[stride * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color c = bmp.GetPixel(x, size - 1 - y);
                    int o = y * stride + x * 4;
                    xor[o] = c.B; xor[o + 1] = c.G; xor[o + 2] = c.R; xor[o + 3] = c.A;
                }
            byte[] mask = new byte[maskStride * size];
            if (size <= 48)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (bmp.GetPixel(x, size - 1 - y).A < 8)
                            mask[y * maskStride + (x / 8)] |= (byte)(0x80 >> (x % 8));

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(size); w.Write(size * 2);
                w.Write((ushort)1); w.Write((ushort)32); w.Write(0);
                w.Write(xor.Length + mask.Length);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                w.Write(xor); w.Write(mask);
                return ms.ToArray();
            }
        }
    }

    private static byte[] RenderPng(int size)
    {
        using (Bitmap bmp = Render(size))
        using (MemoryStream ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    private static Bitmap Render(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size / 256f;

            Color background = Color.FromArgb(255, 11, 15, 20);
            using (SolidBrush brush = new SolidBrush(background))
                g.FillRectangle(brush, 0, 0, size, size);

            float pad = 34f * s;
            float stroke = Math.Max(1.4f, 26f * s);
            RectangleF ring = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

            using (Pen track = new Pen(Color.FromArgb(255, 42, 58, 78), stroke))
            {
                track.StartCap = LineCap.Round; track.EndCap = LineCap.Round;
                g.DrawEllipse(track, ring);
            }
            using (Pen arc = new Pen(Color.FromArgb(255, 57, 211, 83), stroke))
            {
                arc.StartCap = LineCap.Round; arc.EndCap = LineCap.Round;
                g.DrawArc(arc, ring, -90f, 248f);
            }

            if (size >= 32)
            {
                float dot = Math.Max(1.6f, 15f * s);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 230, 237, 243)))
                    g.FillEllipse(brush, size / 2f - dot / 2f, size / 2f - dot / 2f, dot, dot);
            }
        }
        return bmp;
    }
}
