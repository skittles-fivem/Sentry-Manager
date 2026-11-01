using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SentrySix.Core;

namespace SentrySix.Ui.Playback
{
    /// <summary>
    /// One camera's picture: an Image showing a WriteableBitmap (Bgr32, the
    /// decoders' bgr0), the frame copied in with one WritePixels when it
    /// changed. Stretch Uniform, mirrored with ScaleTransform(-1, 1) for the
    /// cameras the profile mirrors, zoom 1 to 4x with a pan (SetView). The bitmap is
    /// kept while frames keep their size (the next segment reuses it).
    /// </summary>
    public sealed class VideoSurface : Border
    {
        readonly Image image = new Image();
        readonly ScaleTransform flip = new ScaleTransform(1, 1);
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        readonly TranslateTransform pan = new TranslateTransform();
        WriteableBitmap bitmap;
        long shownIndex = -1;
        object shownStream;

        public VideoSurface()
        {
            ClipToBounds = true;
            Background = Brushes.Black;
            image.Stretch = Stretch.Uniform;
            // (video { pointer-events: none }: a zoomed picture past the tile is not the tile)
            image.IsHitTestVisible = false;
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            TransformGroup g = new TransformGroup();
            g.Children.Add(flip);
            g.Children.Add(zoom);
            g.Children.Add(pan);
            image.RenderTransform = g;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
            Child = image;
            SizeChanged += delegate { Action h = PixelSizeChanged; if (h != null) h(); };
        }

        /// <summary>The camera shown (the Player's slot key).</summary>
        public string Slot { get; set; }

        /// <summary>The tile's size changed (the Player restarts its decoder at the new size, debounced).</summary>
        public event Action PixelSizeChanged;

        public bool Mirrored
        {
            get { return flip.ScaleX < 0; }
            set { flip.ScaleX = value ? -1 : 1; }
        }

        /// <summary>The zoom (1 to 4; zoomPan.js's scale).</summary>
        public double Zoom { get { return zoom.ScaleX; } }

        /// <summary>
        /// zoomPan.js's transform: scale(zoom) translate(panX, panY) about the
        /// centre, the pan in the picture's own pixels (so it moves pan x zoom
        /// on screen); the bounds are CameraFocus's. While zoomed the picture
        /// is not clipped to the tile (.zoomed's overflow: visible: the grid
        /// clips it).
        /// </summary>
        public void SetView(double z, double panX, double panY)
        {
            if (double.IsNaN(z) || z < 1) z = 1;
            zoom.ScaleX = z;
            zoom.ScaleY = z;
            pan.X = double.IsNaN(panX) ? 0 : panX * z;
            pan.Y = double.IsNaN(panY) ? 0 : panY * z;
            ClipToBounds = z <= 1;
        }

        public double PanX { get { return pan.X / zoom.ScaleX; } }
        public double PanY { get { return pan.Y / zoom.ScaleY; } }

        /// <summary>The tile in device pixels (what the decoder is asked for, at most the source).</summary>
        public Size PixelSize
        {
            get
            {
                PresentationSource src = PresentationSource.FromVisual(this);
                double sx = 1, sy = 1;
                if (src != null && src.CompositionTarget != null)
                {
                    sx = src.CompositionTarget.TransformToDevice.M11;
                    sy = src.CompositionTarget.TransformToDevice.M22;
                }
                return new Size(ActualWidth * sx * Zoom, ActualHeight * sy * Zoom);
            }
        }

        /// <summary>
        /// Shows a frame: one copy into the bitmap, only when it is another
        /// frame than the one on screen. Call on the UI thread.
        /// </summary>
        public void Show(VideoFrame f, object stream)
        {
            if (f == null) return;
            if (f.Index == shownIndex && ReferenceEquals(stream, shownStream)) return;
            if (bitmap == null || bitmap.PixelWidth != f.Width || bitmap.PixelHeight != f.Height)
            {
                bitmap = new WriteableBitmap(f.Width, f.Height, 96, 96, PixelFormats.Bgr32, null);
                image.Source = bitmap;
            }
            bitmap.WritePixels(new Int32Rect(0, 0, f.Width, f.Height), f.Data, f.Stride, 0);
            ShownTime = f.Time;
            Shows++;
            shownIndex = f.Index;
            shownStream = stream;
        }

        /// <summary>A still picture (the render test mode's frame of a fixture clip).</summary>
        public void ShowStill(BitmapSource still)
        {
            image.Source = still;
            bitmap = null;
            shownIndex = -1;
            shownStream = null;
        }

        /// <summary>No picture (a camera without a clip in this segment).</summary>
        public void Clear()
        {
            image.Source = null;
            bitmap = null;
            ShownTime = double.NaN;
            shownIndex = -1;
            shownStream = null;
        }

        public bool HasPicture { get { return bitmap != null; } }

        /// <summary>The shown frame's time into its clip (NaN: none or a still); for the playback check's skew.</summary>
        public double ShownTime = double.NaN;

        /// <summary>Frames put on screen so far.</summary>
        public long Shows;
    }

    /// <summary>
    /// The kill-on-close Job Object playback's ffmpeg processes go in: when
    /// the app ends, however it ends, Windows ends them too. (The server's
    /// own job is ServerHost's; this one stays open for the app's life.)
    /// </summary>
    public static class PlaybackJob
    {
        static readonly object Gate = new object();
        static IntPtr job = IntPtr.Zero;

        /// <summary>Hooks <see cref="FfmpegTools.Started"/> (once).</summary>
        public static void Install()
        {
            if (FfmpegTools.Started == null) FfmpegTools.Started = Add;
        }

        public static void Add(System.Diagnostics.Process p)
        {
            try
            {
                lock (Gate)
                {
                    if (job == IntPtr.Zero)
                    {
                        IntPtr j = CreateJobObject(IntPtr.Zero, null);
                        if (j == IntPtr.Zero) return;
                        JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                        int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                        IntPtr ptr = Marshal.AllocHGlobal(size);
                        try
                        {
                            Marshal.StructureToPtr(info, ptr, false);
                            if (!SetInformationJobObject(j, 9, ptr, (uint)size)) return;
                        }
                        finally { Marshal.FreeHGlobal(ptr); }
                        job = j;
                    }
                    AssignProcessToJobObject(job, p.Handle);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("Playback job object: " + ex.Message);
            }
        }

        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
