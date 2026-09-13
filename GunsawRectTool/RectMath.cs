using System;

namespace GunsawRectTool;

internal static class RectMath
{
    internal static float PivotCenter(float spriteCenter, float spriteSize, float size, bool flipped) =>
        spriteCenter / spriteSize * size * (flipped ? -1f : 1f);

    // Snap the resulting dimension, leaving the opposite side anchored.
    internal static float Resize(float initial, float delta, float direction, float grid, float minimum)
    {
        if (direction == 0f)
            return initial;
        var size = initial + delta * direction;
        if (grid > 0f)
            size = (float)Math.Round(size / grid, MidpointRounding.ToEven) * grid;
        return Math.Max(minimum, size);
    }
}
