using UnityEngine;

namespace GunsawRectTool;

internal static class RectMath
{
    private const float BasePositionGrid = 0.25f;

    private const float Epsilon = 0.000001f;
    private static int _gridStep = 0;
    private static float _gridMultiplier = 1f;

    internal static float PositionGrid => BasePositionGrid * _gridMultiplier;
    internal static float SizeGrid => PositionGrid * 2f;

    internal static void ChangeGrid(int direction)
    {
        _gridStep = Mathf.Clamp(_gridStep + direction, -1, 1);
        _gridMultiplier = _gridStep switch
        {
            < 0 => 0.5f,
            > 0 => 2f,
            _ => 1f
        };
    }

    internal static float UniformScale(float diagonalX, float diagonalY,
        float desiredX, float desiredY, float grid, float minimum)
    {
        float lengthSquared = diagonalX * diagonalX + diagonalY * diagonalY;
        float scale = (desiredX * diagonalX + desiredY * diagonalY) / lengthSquared;
        // Snap the longer dimension, keeping a single scale factor for both axes
        float dimension = Mathf.Max(Mathf.Abs(diagonalX), Mathf.Abs(diagonalY));
        return SnapSize(dimension * scale, grid, dimension * minimum) / dimension;
    }

    internal static float MapFromAnchor(float position, float anchor, float scale)
        => anchor + (position - anchor) * scale;

    // Find the local-axis scales that produce the requested world bounding box
    internal static void FitGroupSize(float xx, float xy, float yx, float yy,
        float scaleX, float scaleY, out float localX, out float localY)
    {
        float width = xx + yx;
        float height = xy + yy;
        float desiredWidth = width * scaleX;
        float desiredHeight = height * scaleY;
        float determinant = xx * yy - yx * xy;
        if (Mathf.Abs(determinant) > Epsilon * width * height)
        {
            localX = (desiredWidth * yy - yx * desiredHeight) / determinant;
            localY = (xx * desiredHeight - desiredWidth * xy) / determinant;
            if (localX > 0f && localY > 0f)
                return;
        }
        localX = localY = Mathf.Min(scaleX, scaleY);
    }

    internal static float PivotCenter(float spriteCenter, float spriteSize, float size, bool flipped)
        => spriteCenter / spriteSize * size * (flipped ? -1f : 1f);

    internal static float Snap(float position, float grid)
        => grid > 0f ? Mathf.Round(position / grid) * grid : position;

    internal static float SnapSize(float size, float grid, float minimum)
    {
        if (grid <= 0f)
            return Mathf.Max(minimum, size);

        return Mathf.Max(Mathf.Ceil(minimum / grid) * grid, Snap(size, grid));
    }

    internal static float Resize(float initial, float delta, float direction, float minimum, float grid)
    {
        if (direction == 0f)
            return initial;

        float size = initial + delta * direction;
        return SnapSize(size, grid, minimum);
    }
}
