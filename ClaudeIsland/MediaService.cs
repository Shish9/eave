using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace ClaudeIsland;

/// Now-playing info, album art, progress and controls via the Windows media session.
public class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? mgr;
    GlobalSystemMediaTransportControlsSession? cur;

    public string Title = "", Artist = "";
    public bool Playing, HasTrack;
    public BitmapImage? Art;
    public event Action? Changed; // raised on a background thread

    TimeSpan position, end;
    DateTimeOffset updated;

    /// 0..1, or -1 when the player doesn't report a length.
    public double Progress()
    {
        if (end.TotalSeconds <= 1) return -1;
        var pos = position + (Playing ? DateTimeOffset.Now - updated : TimeSpan.Zero);
        return Math.Clamp(pos.TotalSeconds / end.TotalSeconds, 0, 1);
    }

    public async Task InitAsync()
    {
        try
        {
            mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.CurrentSessionChanged += (_, _) => Attach();
            Attach();
        }
        catch { }
    }

    void Attach()
    {
        if (cur != null)
        {
            cur.MediaPropertiesChanged -= OnChange;
            cur.PlaybackInfoChanged -= OnChange;
            cur.TimelinePropertiesChanged -= OnChange;
        }
        cur = mgr?.GetCurrentSession();
        if (cur != null)
        {
            cur.MediaPropertiesChanged += OnChange;
            cur.PlaybackInfoChanged += OnChange;
            cur.TimelinePropertiesChanged += OnChange;
        }
        _ = Update();
    }

    void OnChange(GlobalSystemMediaTransportControlsSession s, object a) => _ = Update();

    async Task Update()
    {
        try
        {
            if (cur == null) HasTrack = false;
            else
            {
                var p = await cur.TryGetMediaPropertiesAsync();
                string title = p.Title ?? "";
                bool newTrack = title != Title || Art == null;
                Title = title;
                Artist = p.Artist ?? "";
                HasTrack = Title != "";
                Playing = cur.GetPlaybackInfo().PlaybackStatus ==
                          GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var tl = cur.GetTimelineProperties();
                position = tl.Position;
                end = tl.EndTime;
                updated = tl.LastUpdatedTime;

                if (newTrack)
                {
                    Art = await LoadArt(p);
                    ArtColor = Art != null ? Dominant(Art) : DefaultGlow;
                }
            }
        }
        catch { HasTrack = false; }
        Changed?.Invoke();
    }

    static readonly Color DefaultGlow = Color.FromRgb(0x4F, 0x6F, 0xA8);
    public Color ArtColor = DefaultGlow;

    /// The most vivid colour in the album art, boosted so it reads as light on a dark background.
    static Color Dominant(BitmapSource src)
    {
        try
        {
            var small = new TransformedBitmap(src, new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
            var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);

            double r = 0, g = 0, b = 0, wt = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double pb = px[i], pg = px[i + 1], pr = px[i + 2];
                double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                double sat = max == 0 ? 0 : (max - min) / max;
                double weight = sat * sat * (0.25 + max / 255.0);
                r += pr * weight; g += pg * weight; b += pb * weight; wt += weight;
            }
            if (wt < 0.5) return DefaultGlow;     // grey or empty art

            r /= wt; g /= wt; b /= wt;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            double s = mx == 0 ? 0 : (mx - mn) / mx;
            double boost = Math.Max(s, 0.6);
            double v = Math.Max(mx / 255.0, 0.85);
            // rebuild the colour at the boosted saturation and value, keeping the hue
            double c = v * boost, hue;
            double d = mx - mn;
            if (d < 1e-6) return DefaultGlow;
            if (mx == r) hue = ((g - b) / d) % 6;
            else if (mx == g) hue = (b - r) / d + 2;
            else hue = (r - g) / d + 4;
            if (hue < 0) hue += 6;
            double x = c * (1 - Math.Abs(hue % 2 - 1)), m = v - c;
            (double R, double G, double B) = (int)hue switch
            {
                0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
                3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
            };
            return Color.FromRgb((byte)((R + m) * 255), (byte)((G + m) * 255), (byte)((B + m) * 255));
        }
        catch { return DefaultGlow; }
    }

    static async Task<BitmapImage?> LoadArt(GlobalSystemMediaTransportControlsSessionMediaProperties p)
    {
        try
        {
            if (p.Thumbnail == null) return null;
            using var rs = await p.Thumbnail.OpenReadAsync();
            using var s = rs.AsStreamForRead();
            var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public void PlayPause() { try { _ = cur?.TryTogglePlayPauseAsync(); } catch { } }
    public void Next() { try { _ = cur?.TrySkipNextAsync(); } catch { } }
    public void Previous() { try { _ = cur?.TrySkipPreviousAsync(); } catch { } }
}
