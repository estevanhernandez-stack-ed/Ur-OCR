namespace RoRoRo.UrOcr.Engine;

/// <summary>A block size read off a frame, in that frame's pixels. AlongX and AlongY are the axes that
/// qualified (null for one that did not); Confidence is their mean autocorrelation score, 0 to 1.</summary>
public sealed record PitchEstimate(int Pitch, double Confidence, int? AlongX, int? AlongY);

/// <summary>What each axis read on its own, before the combine rule: the lag (null when the axis has
/// no pattern at all) and its score.</summary>
public sealed record PitchAxes(int? AlongX, double ConfidenceX, int? AlongY, double ConfidenceY);

/// <summary>
/// The block size on a calm frame (spec "Block size is read every pass"). The camera pulls in to the
/// first wall behind the character, so a block is 22 px on the open surface and 170 px in a shaft.
/// Block edges repeat at the block size: the summed luminance edges along each axis, in a square
/// around the character (not on it) that stays off the HUD, are detrended and autocorrelated. The
/// axis's block size is the smallest local peak scoring within 10% of the best peak, so neither a
/// harmonic nor a smooth brightness slope (which scores highest at the shortest lag without peaking)
/// wins. A lag must repeat three times in view. Pure and deterministic.
/// </summary>
public static class PitchEstimator
{
    /// <summary>An axis below this autocorrelation score shows no clear pattern.</summary>
    public const double MinConfidence = 0.3;
    /// <summary>A peak scoring within this share of the best counts as the best.</summary>
    public const double HarmonicSlack = 0.10;
    /// <summary>The two axes agree when their lags are within this share of the larger.</summary>
    public const double AxisAgreement = 0.15;

    /// <summary>The client size the HUD bounds below are measured in; they scale with the frame.</summary>
    public const int HudClientW = 800;
    public const int HudClientH = 599;
    /// <summary>Left of this: the icon column (gifts, pickaxe, Leagues, currencies).</summary>
    public const int HudLeft = 160;
    /// <summary>Above this: the top bar and the "Go to Top" button.</summary>
    public const int HudTop = 70;
    /// <summary>Below this: the hotbar (slots about 78 px apart, the strongest repeat on screen) and the bottom icons.</summary>
    public const int HudBottom = 470;
    /// <summary>The region's largest half-size, whatever the block size searched for.</summary>
    public const int MaxHalf = 260;
    /// <summary>The moving average subtracted from each edge profile: slopes longer than this go.</summary>
    public const int DetrendWindow = 200;
    /// <summary>A lag counts only when the profile holds this many repeats of it.</summary>
    public const int MinRepeats = 3;
    /// <summary>The disc around the character left out of a read (the default minPitch): the
    /// character, not the blocks.</summary>
    public const int CharacterRadius = 16;

    /// <summary>The HUD in this frame, scaled from the measured client: a read stays right of Left,
    /// below Top and above Bottom.</summary>
    public static (int Left, int Top, int Bottom) HudBounds(FramePixels frame) =>
        (HudLeft * frame.Width / HudClientW,
         HudTop * frame.Height / HudClientH,
         Math.Min(frame.Height - 1, HudBottom * frame.Height / HudClientH));

    public static PitchEstimate? Estimate(FramePixels frame, int centerX, int centerY, int minPitch = CharacterRadius, int maxPitch = 240)
    {
        var a = EstimateAxes(frame, centerX, centerY, minPitch, maxPitch);
        var x = a.AlongX is { } lx && a.ConfidenceX >= MinConfidence ? lx : (int?)null;
        var y = a.AlongY is { } ly && a.ConfidenceY >= MinConfidence ? ly : (int?)null;

        if (x is { } px && y is { } py)
        {
            if (Math.Abs(px - py) > AxisAgreement * Math.Max(px, py)) return null;
            var pitch = (int)Math.Round((px + py) / 2.0, MidpointRounding.AwayFromZero);
            return new PitchEstimate(pitch, (a.ConfidenceX + a.ConfidenceY) / 2, px, py);
        }
        if (x is { } only) return new PitchEstimate(only, a.ConfidenceX, only, null);
        if (y is { } onlyY) return new PitchEstimate(onlyY, a.ConfidenceY, null, onlyY);
        return null;
    }

    /// <summary>Each axis on its own, before the confidence floor and the agreement rule.</summary>
    public static PitchAxes EstimateAxes(FramePixels frame, int centerX, int centerY, int minPitch = CharacterRadius, int maxPitch = 240)
    {
        // The HUD, scaled from the measured client into this frame.
        var (left, top, bottom) = HudBounds(frame);
        var right = frame.Width - 1;
        if (centerX <= left || centerX >= right || centerY <= top || centerY >= bottom) return new PitchAxes(null, 0, null, 0);

        var half = Math.Min(MaxHalf, Math.Min(Math.Min(centerX - left, right - centerX), Math.Min(centerY - top, bottom - centerY)));
        if (half < 2 * minPitch) return new PitchAxes(null, 0, null, 0);
        var hx = half;
        var hy = half;

        int x0 = centerX - hx, y0 = centerY - hy, nx = 2 * hx + 1, ny = 2 * hy + 1;
        var lum = new double[nx, ny];
        var skip = new bool[nx, ny];
        var disc = (long)minPitch * minPitch;
        for (var j = 0; j < ny; j++)
            for (var i = 0; i < nx; i++)
            {
                var c = frame.At(x0 + i, y0 + j);
                lum[i, j] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                long dx = i - hx, dy = j - hy;
                skip[i, j] = dx * dx + dy * dy < disc;   // the character
            }

        var alongX = new double[nx - 1];
        var alongY = new double[ny - 1];
        for (var j = 0; j < ny; j++)
            for (var i = 0; i < nx - 1; i++)
                if (!skip[i, j] && !skip[i + 1, j]) alongX[i] += Math.Abs(lum[i + 1, j] - lum[i, j]);
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny - 1; j++)
                if (!skip[i, j] && !skip[i, j + 1]) alongY[j] += Math.Abs(lum[i, j + 1] - lum[i, j]);

        var (lagX, confX) = Period(Detrend(alongX, DetrendWindow), minPitch, maxPitch);
        var (lagY, confY) = Period(Detrend(alongY, DetrendWindow), minPitch, maxPitch);
        return new PitchAxes(lagX, confX, lagY, confY);
    }

    /// <summary>The profile less its centred moving average over <paramref name="window"/> samples.</summary>
    private static double[] Detrend(double[] p, int window)
    {
        var n = p.Length;
        var half = window / 2;
        var prefix = new double[n + 1];
        for (var i = 0; i < n; i++) prefix[i + 1] = prefix[i] + p[i];
        var r = new double[n];
        for (var i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - half), b = Math.Min(n, i + half + 1);
            r[i] = p[i] - (prefix[b] - prefix[a]) / (b - a);
        }
        return r;
    }

    /// <summary>The profile's repeat: the smallest local peak of the normalised autocorrelation that
    /// scores within HarmonicSlack of the best peak, over lags seen MinRepeats times.</summary>
    private static (int? Lag, double Score) Period(double[] profile, int minLag, int maxLag)
    {
        var n = profile.Length;
        var mean = profile.Average();
        var p = profile.Select(v => v - mean).ToArray();

        var last = Math.Min(maxLag, n / MinRepeats);
        if (last < minLag) return (null, 0);
        var scores = new double[last + 2];
        var best = 0.0;
        for (var lag = minLag - 1; lag <= Math.Min(last + 1, n - 1); lag++)
        {
            double ab = 0, aa = 0, bb = 0;
            for (var i = 0; i + lag < n; i++)
            {
                ab += p[i] * p[i + lag];
                aa += p[i] * p[i];
                bb += p[i + lag] * p[i + lag];
            }
            scores[lag] = aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
        }
        bool IsPeak(int lag) => (scores[lag] >= scores[lag - 1] && scores[lag] >= scores[lag + 1]);
        for (var lag = minLag; lag <= last; lag++)
            if (IsPeak(lag)) best = Math.Max(best, scores[lag]);
        if (best <= 0) return (null, 0);
        for (var lag = minLag; lag <= last; lag++)
            if (IsPeak(lag) && scores[lag] >= (1 - HarmonicSlack) * best) return (lag, scores[lag]);
        return (null, 0);
    }
}
