using System;

namespace GunsawRectTool;

internal static class RectMath
{
    internal static float PivotCenter(float spriteCenter, float spriteSize, float size, bool flipped) =>
        spriteCenter / spriteSize * size * (flipped ? -1f : 1f);

    internal static float Snap(float position, float grid) => grid > 0f
        ? (float)Math.Round(position / grid, MidpointRounding.ToEven) * grid : position;

    // Translate without changing size: align whichever corner needs the least correction.
    internal static float SnapMove(float delta, float a, float b, float c, float d, float grid)
    {
        float correction = Snap(a + delta, grid) - (a + delta);
        foreach (float corner in new[] { b, c, d })
        {
            float candidate = Snap(corner + delta, grid) - (corner + delta);
            if (Math.Abs(candidate) < Math.Abs(correction)) correction = candidate;
        }
        return delta + correction;
    }

    // Distance along a constrained world-space axis to the closest grid line.
    internal static float SnapAlongAxis(float x, float y, float axisX, float axisY, float grid)
    {
        if (grid <= 0f) return 0f;
        float dx = Math.Abs(axisX) > 0.0001f ? (Snap(x, grid) - x) / axisX : float.PositiveInfinity;
        float dy = Math.Abs(axisY) > 0.0001f ? (Snap(y, grid) - y) / axisY : float.PositiveInfinity;
        return float.IsPositiveInfinity(dx) && float.IsPositiveInfinity(dy) ? 0f :
            Math.Abs(dx) <= Math.Abs(dy) ? dx : dy;
    }

    // Corner positions have already been snapped in world space; never round the size.
    internal static float Resize(float initial, float delta, float direction, float minimum)
    {
        if (direction == 0f)
            return initial;
        var size = initial + delta * direction;
        return Math.Max(minimum, size);
    }
}
