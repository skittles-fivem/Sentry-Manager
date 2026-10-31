using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>
    /// A recording's picture (GET /api/vehicles/&lt;id&gt;/recordings/&lt;rid&gt;/thumbnail):
    /// cover-cropped in a rounded box; until it is in, and when there is none
    /// (an error, no ffmpeg, the render test mode), the box shows its fallback
    /// text, as the web's img[data-fallback]. Pictures are kept by URL while
    /// the page is open (one <see cref="ThumbCache"/> per page).
    /// </summary>
    public sealed class ThumbCache
    {
        readonly IJsonApi api;
        readonly Dictionary<string, Task<ImageSource>> byUrl = new Dictionary<string, Task<ImageSource>>();

        public ThumbCache(IJsonApi api)
        {
            this.api = api;
        }

        public Task<ImageSource> Load(string path)
        {
            Task<ImageSource> t;
            if (byUrl.TryGetValue(path, out t)) return t;
            t = Fetch(path);
            byUrl[path] = t;
            return t;
        }

        async Task<ImageSource> Fetch(string path)
        {
            if (api == null) return null;
            byte[] data;
            try
            {
                data = await api.GetBytes(path, CancellationToken.None);
            }
            catch (Exception)
            {
                return null;
            }
            if (data == null || data.Length == 0) return null;
            try
            {
                BitmapImage b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.StreamSource = new MemoryStream(data);
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The box: <paramref name="width"/> x <paramref name="height"/> (NaN to
        /// stretch), corner radius, background key, fallback text style.
        /// </summary>
        public FrameworkElement Box(string path, string fallback, double width, double height, double radius, string bgKey, string fallbackKey, double fallbackSize)
        {
            Grid g = new Grid();
            g.Width = width;
            g.Height = height;
            g.ClipToBounds = true;
            Border bg = U.Box(null, bgKey, null, 0, new CornerRadius(radius), new Thickness(0));
            g.Children.Add(bg);
            TextBlock fb = U.Text(fallback ?? "", fallbackSize, FontWeights.Normal, fallbackKey);
            fb.HorizontalAlignment = HorizontalAlignment.Center;
            fb.VerticalAlignment = VerticalAlignment.Center;
            fb.TextAlignment = TextAlignment.Center;
            fb.Margin = new Thickness(6);
            g.Children.Add(fb);
            Rectangle pic = new Rectangle();
            pic.RadiusX = radius;
            pic.RadiusY = radius;
            pic.Visibility = Visibility.Collapsed;
            g.Children.Add(pic);
            if (!string.IsNullOrEmpty(path))
            {
                Task<ImageSource> t = Load(path);
                Action show = delegate
                {
                    ImageSource src = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                    if (src == null) return;
                    ImageBrush brush = new ImageBrush(src);
                    brush.Stretch = Stretch.UniformToFill;
                    brush.AlignmentX = AlignmentX.Center;
                    brush.AlignmentY = AlignmentY.Center;
                    pic.Fill = brush;
                    pic.Visibility = Visibility.Visible;
                    fb.Visibility = Visibility.Collapsed;
                };
                if (t.IsCompleted) show();
                else t.ContinueWith(delegate { g.Dispatcher.BeginInvoke(show); });
            }
            return g;
        }
    }
}
