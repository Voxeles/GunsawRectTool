using System;

namespace GunsawRectTool;

internal static class RectMath
{
    internal static float UniformScale(float anchorX, float anchorY, float diagonalX, float diagonalY,
        float desiredX, float desiredY, float grid, float minimum)
    {
        float lengthSquared = diagonalX * diagonalX + diagonalY * diagonalY;
        if (lengthSquared <= 0.00000001f) return 1f;
        float scale = (desiredX * diagonalX + desiredY * diagonalY) / lengthSquared;
        // Keep the corner on the original diagonal; independently snapping X/Y would distort the ratio.
        scale += SnapAlongAxis(anchorX + diagonalX * scale, anchorY + diagonalY * scale,
            diagonalX, diagonalY, grid);
        return Math.Max(minimum, scale);
    }

    internal static float MapFromAnchor(float position, float anchor, float scale) =>
        anchor + (position - anchor) * scale;

    // Solve the local-axis scales that produce the requested world bounding box.
    // A rotated rectangle cannot always fill an arbitrary box without shear.
    internal static void FitGroupSize(float xx, float xy, float yx, float yy,
        float scaleX, float scaleY, out float localX, out float localY)
    {
        float width = xx + yx, height = xy + yy;
        float desiredWidth = width * scaleX, desiredHeight = height * scaleY;
        float determinant = xx * yy - yx * xy;
        if (Math.Abs(determinant) > 0.000001f * Math.Max(width * height, 0.000001f))
        {
            localX = (desiredWidth * yy - yx * desiredHeight) / determinant;
            localY = (xx * desiredHeight - desiredWidth * xy) / determinant;
            if (localX > 0.00001f && localY > 0.00001f) return;
        }
        localX = localY = Math.Min(scaleX, scaleY);
    }

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
