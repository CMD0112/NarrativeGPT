namespace ChatGPTWrapper.Format;

public sealed class FormatNumericBounds
{
    public required double RecommendedMin { get; init; }

    public required double RecommendedMax { get; init; }

    public double? AbsoluteMin { get; init; }

    public double? AbsoluteMax { get; init; }

    public bool HardClamp { get; init; }

    public static double ClampAbsolute(double value, FormatNumericBounds bounds)
    {
        if (bounds.HardClamp)
        {
            var min = bounds.AbsoluteMin ?? bounds.RecommendedMin;
            var max = bounds.AbsoluteMax ?? bounds.RecommendedMax;
            return Math.Clamp(value, min, max);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
            return bounds.RecommendedMin;

        if (bounds.AbsoluteMin is not null)
            value = Math.Max(value, bounds.AbsoluteMin.Value);
        if (bounds.AbsoluteMax is not null)
            value = Math.Min(value, bounds.AbsoluteMax.Value);

        return value;
    }

    public static bool IsOutsideRecommended(double value, FormatNumericBounds bounds) =>
        value < bounds.RecommendedMin || value > bounds.RecommendedMax;
}
