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
/// Block edges repeat at the block size: the summed luminance edges along each axis, around the
/// character (not on it), are autocorrelated, and the smallest lag scoring within 10% of the best
/// is that axis's block size, so a harmonic never beats the fundamental. Pure and deterministic.
/// </summary>
public static class PitchEstimator
{
    /// <summary>An axis below this autocorrelation score shows no clear pattern.</summary>
    public const double MinConfidence = 0.3;
    /// <summary>A lag scoring within this share of the best counts as the best.</summary>
    public const double HarmonicSlack = 0.10;
    /// <summary>The two axes agree when their lags are within this share of the larger.</summary>
    public const double AxisAgreement = 0.15;

    public static PitchEstimate? Estimate(FramePixels frame, int centerX, int centerY, int minPitch = 16, int maxPitch = 240)
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
    public static PitchAxes EstimateAxes(FramePixels frame, int centerX, int centerY, int minPitch = 16, int maxPitch = 240)
    {
        var cx = Math.Clamp(centerX, 0, frame.Width - 1);
        var cy = Math.Clamp(centerY, 0, frame.Height - 1);
        var toEdge = Math.Min(Math.Min(cx, cy), Math.Min(frame.Width - 1 - cx, frame.Height - 1 - cy));
        var half = Math.Min(2 * maxPitch, toEdge);
        if (half < 2 * minPitch) return new PitchAxes(null, 0, null, 0);

        var x0 = cx - half;
        var y0 = cy - half;
        var n = 2 * half + 1;
        var lum = new double[n, n];
        var skip = new bool[n, n];
        var disc = (long)minPitch * minPitch;
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var c = frame.At(x0 + i, y0 + j);
                lum[i, j] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                long dx = i - half, dy = j - half;
                skip[i, j] = dx * dx + dy * dy < disc;   // the character
            }

        var alongX = new double[n - 1];
        var alongY = new double[n - 1];
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n - 1; i++)
            {
                if (!skip[i, j] && !skip[i + 1, j]) alongX[i] += Math.Abs(lum[i + 1, j] - lum[i, j]);
                if (!skip[j, i] && !skip[j, i + 1]) alongY[i] += Math.Abs(lum[j, i + 1] - lum[j, i]);
            }

        var (lagX, confX) = Period(alongX, minPitch, maxPitch);
        var (lagY, confY) = Period(alongY, minPitch, maxPitch);
        return new PitchAxes(lagX, confX, lagY, confY);
    }

    /// <summary>The profile's repeat: the smallest lag scoring within HarmonicSlack of the best
    /// normalised autocorrelation, over lags with at least two repeats in view.</summary>
    private static (int? Lag, double Score) Period(double[] profile, int minLag, int maxLag)
    {
        var n = profile.Length;
        var mean = profile.Average();
        var p = profile.Select(v => v - mean).ToArray();

        var last = Math.Min(maxLag, n / 2);
        if (last < minLag) return (null, 0);
        var scores = new double[last + 1];
        var best = 0.0;
        for (var lag = minLag; lag <= last; lag++)
        {
            double ab = 0, aa = 0, bb = 0;
            for (var i = 0; i + lag < n; i++)
            {
                ab += p[i] * p[i + lag];
                aa += p[i] * p[i];
                bb += p[i + lag] * p[i + lag];
            }
            scores[lag] = aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
            best = Math.Max(best, scores[lag]);
        }
        if (best <= 0) return (null, 0);
        for (var lag = minLag; lag <= last; lag++)
            if (scores[lag] >= (1 - HarmonicSlack) * best) return (lag, scores[lag]);
        return (null, 0);
    }
}
