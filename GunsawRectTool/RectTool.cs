using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GunsawRectTool;

internal sealed class RectTool : MonoBehaviour
{
    private LevelEditor editor = null!;
    private SpriteRenderer? target;

    private enum RectOperation
    {
        ResizeCorner,
        ResizeEdge,
        ResizeUniform,
        Rotate,
        Move
    }
    private RectOperation operation;

    private bool changed = false;
    private const int MoveHandle = 8;
    private int handle = -1;

    private Vector3 startMouse;
    private Vector3 startScale;
    private Vector3 startPosition;
    private Vector3 pivot;
    private Vector3 anchor;

    private Quaternion startRotation;
    private Matrix4x4 startInverse;
    private Bounds startBounds;
    private Vector2 direction;
    private float startAngle;
    private Vector2 operationMouseScreen;
    private bool moved;

    private readonly Vector3[] corners = new Vector3[4];
    private readonly Vector3[] handles = new Vector3[8];
    private readonly Vector3[] startCorners = new Vector3[4];

    private sealed class GroupItem
    {
        internal SpriteRenderer Renderer = null!;
        internal Vector3 Position;
        internal Vector3 Center;
        internal Vector3 Scale;
        internal Quaternion Rotation;
        internal Vector2 Size;
        internal Vector2 LocalSize;
        internal Vector2 WorldX;
        internal Vector2 WorldY;
    }
    private readonly List<GroupItem> groupItems = [];

    private bool IsGroup => groupItems.Count > 1;
    internal bool IsDragging => handle >= 0;
    private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    private static bool Unsnapped => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

    private static int VisibleHandleCount => Shift ? 4 : 8;
    private static bool Typing
        => EventSystem.current != null
           && EventSystem.current.currentSelectedGameObject != null
           && (EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null
               || EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null);
    private bool Available
        => editor != null
           && editor is { isActiveAndEnabled: true, mode: 3, isCarryingTemplate: false, currentPaintTool: LevelEditor.PaintToolMode.None }
           && (editor.miniHeldObj == null || editor.miniHeldObj.sprite == null)
           && Camera.main != null;

    private RectOperation GetCurrentOperation()
    {
        if (handle == MoveHandle)
            return RectOperation.Move;
        if (Ctrl)
            return RectOperation.Rotate;
        if (handle >= 4)
            return RectOperation.ResizeEdge;
        if (Shift)
            return RectOperation.ResizeUniform;
        return RectOperation.ResizeCorner;
    }

    internal void BeforeEditorUpdate(LevelEditor instance)
    {
        editor = instance;

        if (!Typing)
        {
            // Handle mode switches before the editor dispatches this frame's mouse input
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                SwitchMode(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                SwitchMode(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                SwitchMode(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                SwitchMode(3);
                editor.SetInfoText("Rect tool: drag inside to move; corners to resize; Shift: preserve ratio; Ctrl: rotate; Alt: no snap");
            }

            if (editor.mode == 3)
            {
                bool increase = Input.GetKeyDown(KeyCode.Equals)
                    || Input.GetKeyDown(KeyCode.Plus)
                    || Input.GetKeyDown(KeyCode.KeypadPlus);
                bool decrease = Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus);
                if (increase != decrease)
                {
                    RectMath.ChangeGrid(increase ? 1 : -1);
                    editor.SetInfoText($"Rect grid: positions {RectMath.PositionGrid}, sizes {RectMath.SizeGrid}");
                }
            }
        }

        if (IsDragging)
        {
            if (!Available || Typing || !SelectionMatches())
            {
                Finish();
            }
            else if (!Input.GetMouseButton(0))
            {
                // Include motion on the release frame, including a fast down/up gesture
                if (Input.GetMouseButtonUp(0))
                    Drag();
                Finish();
            }
        }
    }

    private void SwitchMode(int mode)
    {
        if (editor.mode == mode)
            return;

        Finish();
        editor.primarySelectedObject = null!;
        editor.SelectMode(mode);
    }

    internal bool HandleInput()
    {
        if (!Available || Typing)
            return false;

        if (IsDragging)
        {
            if (!SelectionMatches())
                Finish();
            else if (Input.GetMouseButton(0))
                Drag();
            else
                Finish();

            return true;
        }

        if (!Input.GetMouseButtonDown(0)
            || Input.GetKey(KeyCode.Space)
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            || !UpdateGeometry())
        {
            return false;
        }

        int hit = -1;

        float distance = 12f * 12f;
        for (var i = 0; i < VisibleHandleCount; ++i)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(handles[i]);
            float candidate = ((Vector2)screen - (Vector2)Input.mousePosition).sqrMagnitude;
            if (screen.z > 0f && candidate <= distance)
            {
                hit = i;
                distance = candidate;
            }
        }

        List<GameObject> selection = editor.selectedObjects;
        if (hit < 0)
        {
            if (selection.Count == 1)
            {
                if (!InsideRect(selection[0].GetComponent<SpriteRenderer>()))
                    return false;
            }
            else
            {
                if (!InsideGroupRect())
                    return false;
            }

            hit = MoveHandle;
        }

        editor.primarySelectedObject = null!;
        handle = hit;
        target = selection.Count == 1 ? selection[0].GetComponent<SpriteRenderer>() : null;
        groupItems.Clear();
        if (selection.Count > 1)
        {
            foreach (var obj in selection)
            {
                var renderer = obj != null ? obj.GetComponent<SpriteRenderer>() : null;
                if (renderer == null || renderer.sprite == null)
                {
                    handle = -1;
                    groupItems.Clear();
                    return false;
                }
                groupItems.Add(new GroupItem { Renderer = renderer });
            }
        }

        changed = false;
        BeginOperation();
        return true;
    }

    private bool SelectionMatches()
    {
        List<GameObject> selection = editor.selectedObjects;

        if (!IsGroup)
            return target != null && selection.Count == 1 && selection[0] == target.gameObject;

        if (selection.Count != groupItems.Count)
            return false;

        foreach (GroupItem item in groupItems)
        {
            if (item.Renderer == null || !selection.Contains(item.Renderer.gameObject))
                return false;
        }

        return true;
    }

    private bool InsideGroupRect()
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.forward, corners[0]);
        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3 point = ray.GetPoint(distance);
        return point.x >= corners[0].x
               && point.x <= corners[2].x
               && point.y >= corners[0].y
               && point.y <= corners[2].y;
    }

    private static bool InsideRect(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null)
            return false;

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(renderer.transform.forward, renderer.transform.position);
        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3 point = renderer.transform.InverseTransformPoint(ray.GetPoint(distance));
        Bounds bounds = LocalBounds(renderer);
        return point.x >= bounds.min.x
               && point.x <= bounds.max.x
               && point.y >= bounds.min.y
               && point.y <= bounds.max.y;
    }

    private Vector3 GetMousePos()
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.forward, new Vector3(0, 0, target != null ? target.transform.position.z : 0f));
        return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : startMouse;
    }

    private static Vector2 Direction(int index, bool edges)
    {
        if (edges)
        {
            return index switch
            {
                0 => Vector2.down,
                1 => Vector2.right,
                2 => Vector2.up,
                _ => Vector2.left
            };
        }

        return new Vector2(index is 0 or 3 ? -1 : 1, index < 2 ? -1 : 1);
    }

    private void BeginOperation()
    {
        if (IsGroup)
        {
            BeginGroupOperation();
            return;
        }

        if (target == null)
            return;

        operation = GetCurrentOperation();
        direction = Direction(handle % 4, handle is >= 4 and < MoveHandle);
        startBounds = LocalBounds(target);
        startInverse = target.transform.worldToLocalMatrix;
        for (var i = 0; i < 4; ++i)
            startCorners[i] = target.transform.TransformPoint(startBounds.center + Vector3.Scale(startBounds.extents, Direction(i, false)));
        startScale = target.transform.localScale;
        startPosition = target.transform.position;
        startRotation = target.transform.rotation;
        startMouse = GetMousePos();
        operationMouseScreen = Input.mousePosition;
        moved = false;
        pivot = target.transform.TransformPoint(startBounds.center);
        anchor = target.transform.TransformPoint(startBounds.center - Vector3.Scale(startBounds.extents, direction));
        startAngle = Mathf.Atan2(startMouse.y - pivot.y, startMouse.x - pivot.x) * Mathf.Rad2Deg;
    }

    private void BeginGroupOperation()
    {
        operation = GetCurrentOperation();
        direction = Direction(handle % 4, handle is >= 4 and < MoveHandle);

        Bounds bounds = default;
        var first = true;
        foreach (GroupItem item in groupItems)
        {
            Transform transform = item.Renderer.transform;

            Bounds local = LocalBounds(item.Renderer);
            item.Position = transform.position;
            item.Center = transform.TransformPoint(local.center);
            item.Rotation = transform.rotation;
            item.Scale = transform.localScale;
            item.Size = item.Renderer.size;
            item.LocalSize = local.size;
            item.WorldX = transform.TransformVector(Vector3.right);
            item.WorldY = transform.TransformVector(Vector3.up);

            for (var i = 0; i < 4; ++i)
            {
                Vector3 corner = transform.TransformPoint(local.center + Vector3.Scale(local.extents, Direction(i, false)));
                if (first)
                {
                    bounds = new Bounds(corner, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(corner);
                }
            }
        }

        startBounds = bounds;
        startInverse = Matrix4x4.identity;
        for (var i = 0; i < 4; ++i)
            startCorners[i] = bounds.center + Vector3.Scale(bounds.extents, Direction(i, false));
        startMouse = GetMousePos();
        operationMouseScreen = Input.mousePosition;
        moved = false;
        pivot = bounds.center;
        anchor = bounds.center - Vector3.Scale(bounds.extents, direction);
        startAngle = Mathf.Atan2(startMouse.y - pivot.y, startMouse.x - pivot.x) * Mathf.Rad2Deg;
    }

    private void DragGroup()
    {
        UpdateOperation();

        moved |= ((Vector2)Input.mousePosition - operationMouseScreen).sqrMagnitude > 9f;
        if (!moved)
            return;

        Vector3 mouse = GetMousePos();
        Vector3 delta = mouse - startMouse;
        float grid = Unsnapped ? 0f : RectMath.SizeGrid;
        Vector2 ratio = Vector2.one;
        Vector3 centerOffset = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        if (operation == RectOperation.Move)
        {
            // Snap a member the same way as a single object, so members already on the grid stay on it
            Vector3 reference = groupItems[0].Position;
            delta = SnapPosition(reference + delta) - reference;
            delta.z = 0f;
        }
        else if (operation == RectOperation.Rotate)
        {
            float angle = Mathf.DeltaAngle(startAngle, Mathf.Atan2(mouse.y - pivot.y, mouse.x - pivot.x) * Mathf.Rad2Deg);
            if (!Unsnapped)
                angle = Mathf.Round(angle / 15f) * 15f;
            rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
        else // Resize
        {
            if (operation == RectOperation.ResizeUniform)
            {
                ratio = Vector2.one * UniformResizeScale(mouse, grid, 0.1f);
            }
            else
            {
                ratio.x = RectMath.Resize(startBounds.size.x, delta.x, direction.x, 0.1f, grid) / startBounds.size.x;
                ratio.y = RectMath.Resize(startBounds.size.y, delta.y, direction.y, 0.1f, grid) / startBounds.size.y;
            }

            var center = new Vector3(
                RectMath.MapFromAnchor(pivot.x, anchor.x, ratio.x),
                RectMath.MapFromAnchor(pivot.y, anchor.y, ratio.y),
                pivot.z);
            centerOffset = SnapResizePosition(center, direction, Quaternion.identity) - center;
        }

        var frameChanged = false;
        foreach (GroupItem item in groupItems)
        {
            SpriteRenderer renderer = item.Renderer;
            Transform transform = renderer.transform;
            Vector3 oldPosition = transform.position;
            Vector3 oldScale = transform.localScale;
            Quaternion oldRotation = transform.rotation;
            Vector2 oldSize = renderer.size;

            if (operation == RectOperation.Move)
            {
                transform.position = item.Position + delta;
            }
            else if (operation == RectOperation.Rotate)
            {
                transform.position = pivot + rotation * (item.Position - pivot);
                transform.rotation = rotation * item.Rotation;
            }
            else // Resize
            {
                RectMath.FitGroupSize(
                    Mathf.Abs(item.WorldX.x) * item.LocalSize.x,
                    Mathf.Abs(item.WorldX.y) * item.LocalSize.x,
                    Mathf.Abs(item.WorldY.x) * item.LocalSize.y,
                    Mathf.Abs(item.WorldY.y) * item.LocalSize.y,
                    ratio.x,
                    ratio.y,
                    out float localX,
                    out float localY);

                if (renderer.drawMode == SpriteDrawMode.Simple)
                    transform.localScale = new Vector3(item.Scale.x * localX, item.Scale.y * localY, item.Scale.z);
                else
                    renderer.size = new Vector2(item.Size.x * localX, item.Size.y * localY);

                var center = new Vector3(
                    RectMath.MapFromAnchor(item.Center.x, anchor.x, ratio.x),
                    RectMath.MapFromAnchor(item.Center.y, anchor.y, ratio.y),
                    item.Center.z);
                transform.position += center + centerOffset - transform.TransformPoint(LocalBounds(renderer).center);
            }
            frameChanged |= oldPosition != transform.position
                || oldRotation != transform.rotation
                || oldScale != transform.localScale
                || oldSize != renderer.size;
        }

        if (frameChanged)
        {
            changed = true;
            editor.UpdateSelectionVisuals();
        }
    }

    private void Drag()
    {
        if (IsGroup)
        {
            DragGroup();
            return;
        }

        if (target == null)
            return;

        UpdateOperation();

        moved |= ((Vector2)Input.mousePosition - operationMouseScreen).sqrMagnitude > 9f;
        if (!moved)
            return;

        Transform transform = target.transform;
        Vector3 oldPosition = transform.position;
        Vector3 oldScale = transform.localScale;
        Quaternion oldRotation = transform.rotation;
        Vector2 oldSize = target.size;
        Vector3 mouse = GetMousePos();

        if (operation == RectOperation.Move)
        {
            Vector3 position = SnapPosition(startPosition + mouse - startMouse);
            position.z = startPosition.z;
            transform.position = position;
        }
        else if (operation == RectOperation.Rotate)
        {
            float angle = Mathf.DeltaAngle(startAngle, Mathf.Atan2(mouse.y - pivot.y, mouse.x - pivot.x) * Mathf.Rad2Deg);
            if (!Unsnapped)
                angle = Mathf.Round(angle / 15f) * 15f;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward) * startRotation;
            transform.position += pivot - transform.TransformPoint(LocalBounds(target).center);
        }
        else
        {
            var part = target.GetComponent<LevelPartGame>();
            if (part == null || !part.showSize)
                return;

            float grid = Unsnapped ? 0f : RectMath.SizeGrid;
            Vector3 delta = startInverse.MultiplyVector(mouse - startMouse);

            Vector2 axes = direction;
            if (part.fixedYSize && !editor.bypassFixedSize)
                axes.y = 0f;

            float minY = part.fullName switch
            {
                "Asphalt" => 0.143f,
                "Girder" => 1.28f,
                "One way platform" => 0.57f,
                "Conveyor belt" => 0.36f,
                _ => 0.1f
            };

            Vector2 size;
            if (operation == RectOperation.ResizeUniform)
            {
                // A fixed height and a fixed aspect ratio leave no resizing freedom
                if (axes.y == 0f)
                    return;

                size = (Vector2)startBounds.size * UniformResizeScale(mouse, grid, minY);
            }
            else
            {
                size = new Vector2(
                    RectMath.Resize(startBounds.size.x, delta.x, axes.x, 0.1f, grid),
                    RectMath.Resize(startBounds.size.y, delta.y, axes.y, minY, grid));
            }

            if (target.drawMode != SpriteDrawMode.Simple)
            {
                target.size = size;
            }
            else
            {
                transform.localScale = new Vector3(
                    startScale.x * size.x / startBounds.size.x,
                    startScale.y * size.y / startBounds.size.y,
                    startScale.z);
            }

            // Recompute the opposite handle after resizing to account for noncentral pivots and flips
            Bounds bounds = LocalBounds(target);
            Vector3 opposite = bounds.center - Vector3.Scale(bounds.extents, direction);
            transform.position += anchor - transform.TransformPoint(opposite);
            transform.position = SnapResizePosition(transform.position, axes, transform.rotation);
        }

        if (oldPosition != transform.position
            || oldRotation != transform.rotation
            || oldScale != transform.localScale
            || oldSize != target.size)
        {
            changed = true;
            editor.UpdateSelectionVisuals();
            RefreshFields();
        }
    }

    private static Vector3 SnapPosition(Vector3 position)
    {
        if (Unsnapped)
            return position;

        position.x = RectMath.Snap(position.x, RectMath.PositionGrid);
        position.y = RectMath.Snap(position.y, RectMath.PositionGrid);
        return position;
    }

    private static Vector3 SnapResizePosition(Vector3 position, Vector2 axes, Quaternion rotation)
    {
        Vector3 correction = SnapPosition(position) - position;

        // Dragging an edge only snaps along its axis, so the rect doesn't shift sideways
        if (axes.x == 0f || axes.y == 0f)
            correction = Vector3.Project(correction, rotation * axes);

        return position + correction;
    }

    private void UpdateOperation()
    {
        RectOperation next = GetCurrentOperation();
        if (operation == next)
            return;

        bool cornerResize = operation is RectOperation.ResizeCorner or RectOperation.ResizeUniform;
        bool nextCornerResize = next is RectOperation.ResizeCorner or RectOperation.ResizeUniform;
        if (cornerResize && nextCornerResize)
        {
            // Do not change the drag's original geometry or anchor (avoids shifting around while resizing)
            // On release discard the initial click offset (avoids the cursor being way off after releasing shift)
            if (next == RectOperation.ResizeCorner)
                startMouse = startCorners[handle];
            operation = next;
            moved = true;
        }
        else
        {
            BeginOperation();
        }
    }

    private float UniformResizeScale(Vector3 mouse, float grid, float minimumHeight)
    {
        Vector3 diagonal = Vector3.Scale(startBounds.size, direction);
        Vector3 desired = diagonal + startInverse.MultiplyVector(mouse - startMouse);
        float minimum = Mathf.Max(0.1f / startBounds.size.x, minimumHeight / startBounds.size.y);
        return RectMath.UniformScale(diagonal.x, diagonal.y, desired.x, desired.y, grid, minimum);
    }

    private void RefreshFields()
    {
        if (target == null)
            return;

        // The editor parses these with the current culture
        editor.posXField.SetTextWithoutNotify(target.transform.position.x.ToString(CultureInfo.CurrentCulture));
        editor.posYField.SetTextWithoutNotify(target.transform.position.y.ToString(CultureInfo.CurrentCulture));
        editor.rotField.SetTextWithoutNotify(target.transform.eulerAngles.z.ToString(CultureInfo.CurrentCulture));
        editor.sizeXField.SetTextWithoutNotify(target.size.x.ToString(CultureInfo.CurrentCulture));
        editor.sizeYField.SetTextWithoutNotify(target.size.y.ToString(CultureInfo.CurrentCulture));
    }

    private void Finish()
    {
        bool commit = IsDragging && changed && (target != null || IsGroup) && editor != null;
        handle = -1;
        changed = false;
        if (commit)
        {
            editor!.SaveState();

            var objects = new List<GameObject>();
            if (IsGroup)
            {
                foreach (GroupItem item in groupItems)
                {
                    if (item.Renderer != null)
                        objects.Add(item.Renderer.gameObject);
                }
            }
            else if (target != null)
            {
                objects.Add(target.gameObject);
            }

            editor.SendTransformAction(objects);
        }
        target = null;
        groupItems.Clear();
    }

    private void OnDisable()
    {
        Finish();
    }

    private static Bounds LocalBounds(SpriteRenderer renderer)
    {
        Bounds bounds = renderer.sprite.bounds;
        if (renderer.drawMode != SpriteDrawMode.Simple)
        {
            // The editor's size fields accept negative and zero sizes
            Vector2 size = renderer.size;
            size.x = size.x == 0f ? 0.1f : Mathf.Abs(size.x);
            size.y = size.y == 0f ? 0.1f : Mathf.Abs(size.y);
            bounds.center = new Vector3(RectMath.PivotCenter(bounds.center.x, bounds.size.x, size.x, false),
                RectMath.PivotCenter(bounds.center.y, bounds.size.y, size.y, false), 0);
            bounds.size = size;
        }

        Vector3 center = bounds.center;
        if (renderer.flipX)
            center.x = -center.x;
        if (renderer.flipY)
            center.y = -center.y;
        bounds.center = center;

        return bounds;
    }

    private bool UpdateGeometry()
    {
        List<GameObject> selection = editor.selectedObjects;
        if (selection == null || selection.Count == 0)
            return false;

        bool found = false;
        Bounds group = default;
        foreach (var obj in selection)
        {
            if (obj == null)
                continue;

            var renderer = obj.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null)
                continue;

            if (selection.Count == 1)
            {
                Bounds bounds = LocalBounds(renderer);
                for (var i = 0; i < 4; ++i)
                    corners[i] = obj.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, Direction(i, false)));
            }
            else if (!found)
            {
                group = renderer.bounds;
            }
            else
            {
                group.Encapsulate(renderer.bounds);
            }

            found = true;
        }

        if (!found)
            return false;

        if (selection.Count > 1)
        {
            for (var i = 0; i < 4; ++i)
                corners[i] = group.center + Vector3.Scale(group.extents, Direction(i, false));
        }

        for (var i = 0; i < 4; ++i)
        {
            handles[i] = corners[i];
            handles[i + 4] = (corners[i] + corners[(i + 1) % 4]) * 0.5f;
        }

        return true;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !Available || !UpdateGeometry())
            return;

        Color previous = GUI.color;
        GUI.color = new Color(0.3f, 0.85f, 1f);

        for (var i = 0; i < 4; ++i)
            DrawLine(Camera.main.WorldToScreenPoint(corners[i]), Camera.main.WorldToScreenPoint(corners[(i + 1) % 4]));

        for (var i = 0; i < VisibleHandleCount; ++i)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(handles[i]);
            if (screen.z <= 0f)
                continue;
            GUI.DrawTexture(new Rect(screen.x - 5, Screen.height - screen.y - 5, 10, 10), Texture2D.whiteTexture);
        }

        GUI.color = previous;
    }

    private static void DrawLine(Vector3 a, Vector3 b)
    {
        if (a.z <= 0f || b.z <= 0f)
            return;

        var start = new Vector2(a.x, Screen.height - a.y);
        var delta = new Vector2(b.x - a.x, a.y - b.y);
        Matrix4x4 previous = GUI.matrix;

        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.DrawTexture(new Rect(start.x, start.y, delta.magnitude, 1), Texture2D.whiteTexture);

        GUI.matrix = previous;
    }
}
