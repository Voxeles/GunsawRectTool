using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GunsawRectTool;

public sealed class RectTool : MonoBehaviour
{
    private enum RectOperation
    {
        ResizeCorner,
        ResizeEdge,
        Rotate,
        Move
    }

    private const int MoveHandle = 4;
    private LevelEditor editor = null!;
    private SpriteRenderer? target;
    private int handle = -1;
    private RectOperation operation;
    private bool changed;
    private Vector3 startMouse, startScale, startPosition, pivot, anchor;
    private Quaternion startRotation;
    private Matrix4x4 startInverse;
    private Matrix4x4 startMatrix;
    private Bounds startBounds;
    private Vector2 direction;
    private float startAngle;
    private Vector2 operationMouseScreen;
    private bool moved;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly Vector3[] handles = new Vector3[4];
    private readonly Vector3[] startCorners = new Vector3[4];
    private sealed class GroupItem
    {
        internal SpriteRenderer Renderer = null!;
        internal Vector3 Position, Center, Scale;
        internal Quaternion Rotation;
        internal Vector2 Size, LocalSize;
        internal Vector2 WorldX, WorldY;
    }
    private readonly List<GroupItem> groupItems = new List<GroupItem>();
    private bool IsGroup => groupItems.Count > 1;

    internal bool IsDragging => handle >= 0;
    private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    private static bool Unsnapped => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    private static RectOperation Operation => Ctrl ? RectOperation.Rotate :
        Shift ? RectOperation.ResizeEdge : RectOperation.ResizeCorner;
    private static bool Typing => EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
        (EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null ||
         EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null);
    private bool Available => editor != null && editor.isActiveAndEnabled && editor.mode == 3 &&
        !editor.isCarryingTemplate && editor.currentPaintTool == LevelEditor.PaintToolMode.None &&
        (editor.miniHeldObj == null || editor.miniHeldObj.sprite == null) && Camera.main != null;

    internal void BeforeEditorUpdate(LevelEditor instance)
    {
        editor = instance;
        if (!Typing)
        {
            // Handle mode switches before the editor dispatches this frame's mouse input.
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchMode(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchMode(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchMode(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                SwitchMode(3);
                editor.SetInfoText("Rect tool: drag inside to move; corners to resize; Shift: edges; Ctrl: rotate; Alt: no snap");
            }
        }
        if (IsDragging)
        {
            if (!Available || Typing || !SelectionMatches()) Finish();
            else if (!Input.GetMouseButton(0))
            {
                // Include motion on the release frame, including a fast down/up gesture.
                if (Input.GetMouseButtonUp(0)) Drag();
                Finish();
            }
        }
    }

    private void SwitchMode(int mode)
    {
        if (editor.mode != mode)
        {
            Finish();
            editor.primarySelectedObject = null!;
            editor.SelectMode(mode);
        }
    }

    internal bool HandleInput()
    {
        if (!Available || Typing)
            return false;
        if (IsDragging)
        {
            if (!SelectionMatches()) Finish();
            else if (Input.GetMouseButton(0)) Drag();
            else Finish();
            return true;
        }
        if (!Input.GetMouseButtonDown(0) || Input.GetKey(KeyCode.Space) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || !Geometry())
            return false;
        int hit = -1;
        float distance = 12f * 12f;
        for (int i = 0; i < handles.Length; ++i)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(handles[i]);
            float candidate = ((Vector2)screen - (Vector2)Input.mousePosition).sqrMagnitude;
            if (screen.z > 0 && candidate <= distance) { hit = i; distance = candidate; }
        }
        var selection = editor.selectedObjects;
        if (hit < 0)
        {
            if (selection.Count == 1 ? !InsideRect(selection[0].GetComponent<SpriteRenderer>()) : !InsideGroupRect())
                return false;
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
        var selection = editor.selectedObjects;
        if (!IsGroup) return target != null && selection.Count == 1 && selection[0] == target.gameObject;
        if (selection.Count != groupItems.Count) return false;
        foreach (var item in groupItems)
            if (item.Renderer == null || !selection.Contains(item.Renderer.gameObject)) return false;
        return true;
    }

    private bool InsideGroupRect()
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.forward, corners[0]);
        if (!plane.Raycast(ray, out float distance)) return false;
        Vector3 point = ray.GetPoint(distance);
        return point.x >= corners[0].x && point.x <= corners[2].x &&
            point.y >= corners[0].y && point.y <= corners[2].y;
    }

    private static bool InsideRect(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null) return false;
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(renderer.transform.forward, renderer.transform.position);
        if (!plane.Raycast(ray, out float distance)) return false;
        Vector3 point = renderer.transform.InverseTransformPoint(ray.GetPoint(distance));
        Bounds bounds = LocalBounds(renderer);
        return point.x >= bounds.min.x && point.x <= bounds.max.x &&
            point.y >= bounds.min.y && point.y <= bounds.max.y;
    }

    private Vector3 Mouse()
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.forward, new Vector3(0, 0, target != null ? target.transform.position.z : 0));
        return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : startMouse;
    }

    private static Vector2 Direction(int index, bool edges)
    {
        if (edges)
            return index == 0 ? Vector2.down : index == 1 ? Vector2.right : index == 2 ? Vector2.up : Vector2.left;
        return new Vector2(index == 0 || index == 3 ? -1 : 1, index < 2 ? -1 : 1);
    }

    private void BeginOperation()
    {
        if (IsGroup)
        {
            BeginGroupOperation();
            return;
        }
        if (target == null) return;
        operation = handle == MoveHandle ? RectOperation.Move : Operation;
        direction = Direction(handle, operation == RectOperation.ResizeEdge);
        startBounds = LocalBounds(target);
        startInverse = target.transform.worldToLocalMatrix;
        startMatrix = target.transform.localToWorldMatrix;
        for (int i = 0; i < 4; ++i)
            startCorners[i] = startMatrix.MultiplyPoint3x4(startBounds.center + Vector3.Scale(startBounds.extents, Direction(i, false)));
        startScale = target.transform.localScale;
        startPosition = target.transform.position;
        startRotation = target.transform.rotation;
        startMouse = Mouse();
        operationMouseScreen = Input.mousePosition;
        moved = false;
        pivot = target.transform.TransformPoint(startBounds.center);
        anchor = target.transform.TransformPoint(startBounds.center - Vector3.Scale(startBounds.extents, direction));
        startAngle = Mathf.Atan2(startMouse.y - pivot.y, startMouse.x - pivot.x) * Mathf.Rad2Deg;
    }

    private void BeginGroupOperation()
    {
        operation = handle == MoveHandle ? RectOperation.Move : Operation;
        direction = Direction(handle, operation == RectOperation.ResizeEdge);
        Bounds bounds = default;
        bool first = true;
        foreach (var item in groupItems)
        {
            var transform = item.Renderer.transform;
            Bounds local = LocalBounds(item.Renderer);
            item.Position = transform.position;
            item.Center = transform.TransformPoint(local.center);
            item.Rotation = transform.rotation;
            item.Scale = transform.localScale;
            item.Size = item.Renderer.size;
            item.LocalSize = local.size;
            item.WorldX = transform.TransformVector(Vector3.right);
            item.WorldY = transform.TransformVector(Vector3.up);
            for (int i = 0; i < 4; ++i)
            {
                Vector3 corner = transform.TransformPoint(local.center + Vector3.Scale(local.extents, Direction(i, false)));
                if (first) { bounds = new Bounds(corner, Vector3.zero); first = false; }
                else bounds.Encapsulate(corner);
            }
        }
        startBounds = bounds;
        startMatrix = startInverse = Matrix4x4.identity;
        for (int i = 0; i < 4; ++i)
            startCorners[i] = bounds.center + Vector3.Scale(bounds.extents, Direction(i, false));
        startMouse = Mouse();
        operationMouseScreen = Input.mousePosition;
        moved = false;
        pivot = bounds.center;
        anchor = bounds.center - Vector3.Scale(bounds.extents, direction);
        startAngle = Mathf.Atan2(startMouse.y - pivot.y, startMouse.x - pivot.x) * Mathf.Rad2Deg;
    }

    private void DragGroup()
    {
        if (handle != MoveHandle && operation != Operation) BeginGroupOperation();
        moved |= ((Vector2)Input.mousePosition - operationMouseScreen).sqrMagnitude > 9f;
        if (!moved) return;
        Vector3 mouse = Mouse();
        Vector3 delta = mouse - startMouse;
        float grid = Unsnapped ? 0 : editor.altGridSnapValue;
        Vector2 ratio = Vector2.one;
        Quaternion rotation = Quaternion.identity;
        if (operation == RectOperation.Move)
        {
            delta.x = RectMath.SnapMove(delta.x, startCorners[0].x, startCorners[1].x, startCorners[2].x, startCorners[3].x, grid);
            delta.y = RectMath.SnapMove(delta.y, startCorners[0].y, startCorners[1].y, startCorners[2].y, startCorners[3].y, grid);
            delta.z = 0;
        }
        else if (operation == RectOperation.Rotate)
        {
            float angle = Mathf.DeltaAngle(startAngle, Mathf.Atan2(mouse.y - pivot.y, mouse.x - pivot.x) * Mathf.Rad2Deg);
            if (!Unsnapped) angle = Mathf.Round(angle / 15f) * 15f;
            rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
        else
        {
            if (startBounds.size.x <= 0.00001f || startBounds.size.y <= 0.00001f) return;
            delta = SnapResizeDelta(delta, direction, grid);
            ratio.x = RectMath.Resize(startBounds.size.x, delta.x, direction.x, 0.1f) / startBounds.size.x;
            ratio.y = RectMath.Resize(startBounds.size.y, delta.y, direction.y, 0.1f) / startBounds.size.y;
        }

        bool frameChanged = false;
        foreach (var item in groupItems)
        {
            var renderer = item.Renderer;
            var transform = renderer.transform;
            Vector3 oldPosition = transform.position, oldScale = transform.localScale;
            Quaternion oldRotation = transform.rotation;
            Vector2 oldSize = renderer.size;
            if (operation == RectOperation.Move)
                transform.position = item.Position + delta;
            else if (operation == RectOperation.Rotate)
            {
                transform.position = pivot + rotation * (item.Position - pivot);
                transform.rotation = rotation * item.Rotation;
            }
            else
            {
                RectMath.FitGroupSize(Mathf.Abs(item.WorldX.x) * item.LocalSize.x,
                    Mathf.Abs(item.WorldX.y) * item.LocalSize.x,
                    Mathf.Abs(item.WorldY.x) * item.LocalSize.y,
                    Mathf.Abs(item.WorldY.y) * item.LocalSize.y,
                    ratio.x, ratio.y, out float localX, out float localY);
                if (renderer.drawMode == SpriteDrawMode.Simple)
                    transform.localScale = new Vector3(item.Scale.x * localX, item.Scale.y * localY, item.Scale.z);
                else renderer.size = new Vector2(item.Size.x * localX, item.Size.y * localY);
                Vector3 center = new Vector3(RectMath.MapFromAnchor(item.Center.x, anchor.x, ratio.x),
                    RectMath.MapFromAnchor(item.Center.y, anchor.y, ratio.y), item.Center.z);
                transform.position += center - transform.TransformPoint(LocalBounds(renderer).center);
            }
            frameChanged |= oldPosition != transform.position || oldRotation != transform.rotation ||
                oldScale != transform.localScale || oldSize != renderer.size;
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
        if (target == null) return;
        if (handle != MoveHandle && operation != Operation) BeginOperation();
        moved |= ((Vector2)Input.mousePosition - operationMouseScreen).sqrMagnitude > 9f;
        if (!moved) return;
        var transform = target.transform;
        Vector3 oldPosition = transform.position, oldScale = transform.localScale;
        Quaternion oldRotation = transform.rotation;
        Vector2 oldSize = target.size;
        Vector3 mouse = Mouse();
        if (operation == RectOperation.Move)
        {
            Vector3 delta = mouse - startMouse;
            if (!Unsnapped && editor.altGridSnapValue > 0)
            {
                float grid = editor.altGridSnapValue;
                delta.x = RectMath.SnapMove(delta.x, startCorners[0].x, startCorners[1].x, startCorners[2].x, startCorners[3].x, grid);
                delta.y = RectMath.SnapMove(delta.y, startCorners[0].y, startCorners[1].y, startCorners[2].y, startCorners[3].y, grid);
            }
            Vector3 position = startPosition + delta;
            position.z = startPosition.z;
            transform.position = position;
        }
        else if (operation == RectOperation.Rotate)
        {
            float angle = Mathf.DeltaAngle(startAngle, Mathf.Atan2(mouse.y - pivot.y, mouse.x - pivot.x) * Mathf.Rad2Deg);
            if (!Unsnapped) angle = Mathf.Round(angle / 15f) * 15f;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward) * startRotation;
            transform.position += pivot - transform.TransformPoint(LocalBounds(target).center);
        }
        else
        {
            var part = target.GetComponent<LevelPartGame>();
            if (part == null || !part.showSize) return;
            Vector3 delta = startInverse.MultiplyVector(mouse - startMouse);
            Vector2 axes = direction;
            if (part.fixedYSize && !editor.bypassFixedSize) axes.y = 0;
            float grid = Unsnapped ? 0 : editor.altGridSnapValue;
            delta = SnapResizeDelta(delta, axes, grid);
            float minY = part.fullName == "Asphalt" ? 0.143f : part.fullName == "Girder" ? 1.28f :
                part.fullName == "One way platform" ? 0.57f : part.fullName == "Conveyor belt" ? 0.36f : 0.1f;
            Vector2 size = new Vector2(
                RectMath.Resize(startBounds.size.x, delta.x, axes.x, 0.1f),
                RectMath.Resize(startBounds.size.y, delta.y, axes.y, minY));
            if (target.drawMode != SpriteDrawMode.Simple) target.size = size;
            else
                transform.localScale = new Vector3(startScale.x * size.x / startBounds.size.x,
                    startScale.y * size.y / startBounds.size.y, startScale.z);
            // Recompute the opposite handle after resizing to account for noncentral pivots and flips.
            Bounds bounds = LocalBounds(target);
            Vector3 opposite = bounds.center - Vector3.Scale(bounds.extents, direction);
            transform.position += anchor - transform.TransformPoint(opposite);
        }
        if (oldPosition != transform.position || oldRotation != transform.rotation || oldScale != transform.localScale || oldSize != target.size)
        {
            changed = true;
            editor.UpdateSelectionVisuals();
            RefreshFields();
        }
    }

    private Vector3 SnapResizeDelta(Vector3 delta, Vector2 axes, float grid)
    {
        if (grid <= 0) return delta;
        if (axes.x != 0 && axes.y != 0)
        {
            Vector3 corner = startCorners[handle];
            Vector3 desired = corner + startMatrix.MultiplyVector(delta);
            desired.x = RectMath.Snap(desired.x, grid);
            desired.y = RectMath.Snap(desired.y, grid);
            return startInverse.MultiplyVector(desired - corner);
        }
        if (axes.x == 0 && axes.y == 0) return delta;

        // Shift/fixed-height resizing must stay on its local axis. Snap an endpoint
        // to a world grid line without projecting a snapped midpoint off that line.
        Vector3 localAxis = axes.x != 0 ? Vector3.right : Vector3.up;
        Vector3 worldAxis = startMatrix.MultiplyVector(localAxis);
        Vector3 displacement = worldAxis * (axes.x != 0 ? delta.x : delta.y);
        float correction = float.PositiveInfinity;
        for (int i = 0; i < 4; ++i)
        {
            Vector2 cornerDirection = Direction(i, false);
            if (axes.x != 0 ? cornerDirection.x != axes.x : cornerDirection.y != axes.y) continue;
            Vector3 desired = startCorners[i] + displacement;
            float candidate = RectMath.SnapAlongAxis(desired.x, desired.y, worldAxis.x, worldAxis.y, grid);
            if (Mathf.Abs(candidate) < Mathf.Abs(correction)) correction = candidate;
        }
        return delta + localAxis * correction;
    }

    private void RefreshFields()
    {
        if (target == null) return;
        editor.posXField.SetTextWithoutNotify(target.transform.position.x.ToString());
        editor.posYField.SetTextWithoutNotify(target.transform.position.y.ToString());
        editor.rotField.SetTextWithoutNotify(target.transform.eulerAngles.z.ToString());
        editor.sizeXField.SetTextWithoutNotify(target.size.x.ToString());
        editor.sizeYField.SetTextWithoutNotify(target.size.y.ToString());
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
                foreach (var item in groupItems)
                    if (item.Renderer != null) objects.Add(item.Renderer.gameObject);
            }
            else if (target != null) objects.Add(target.gameObject);
            editor.SendTransformAction(objects);
        }
        target = null;
        groupItems.Clear();
    }

    private void OnDisable() => Finish();

    private static Bounds LocalBounds(SpriteRenderer renderer)
    {
        Bounds bounds = renderer.sprite.bounds;
        if (renderer.drawMode != SpriteDrawMode.Simple)
        {
            bounds.center = new Vector3(RectMath.PivotCenter(bounds.center.x, bounds.size.x, renderer.size.x, false),
                RectMath.PivotCenter(bounds.center.y, bounds.size.y, renderer.size.y, false), 0);
            bounds.size = renderer.size;
        }
        Vector3 center = bounds.center;
        if (renderer.flipX) center.x = -center.x;
        if (renderer.flipY) center.y = -center.y;
        bounds.center = center;
        return bounds;
    }

    private bool Geometry()
    {
        var selection = editor.selectedObjects;
        if (selection == null || selection.Count == 0) return false;
        bool found = false;
        Bounds group = default;
        foreach (var obj in selection)
        {
            if (obj == null) continue;
            var renderer = obj.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) continue;
            if (selection.Count == 1)
            {
                var bounds = LocalBounds(renderer);
                if (bounds.size.x <= 0.00001f || bounds.size.y <= 0.00001f) return false;
                for (int i = 0; i < 4; ++i)
                    corners[i] = obj.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, Direction(i, false)));
            }
            else if (!found) group = renderer.bounds;
            else group.Encapsulate(renderer.bounds);
            found = true;
        }
        if (!found) return false;
        if (selection.Count > 1)
            for (int i = 0; i < 4; ++i)
                corners[i] = group.center + Vector3.Scale(group.extents, Direction(i, false));
        for (int i = 0; i < 4; ++i)
            handles[i] = Operation == RectOperation.ResizeEdge ? (corners[i] + corners[(i + 1) % 4]) * 0.5f : corners[i];
        return true;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !Available || !Geometry()) return;
        Color previous = GUI.color;
        GUI.color = new Color(0.3f, 0.85f, 1f);
        for (int i = 0; i < 4; ++i)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(handles[i]);
            if (screen.z <= 0) continue;
            DrawLine(Camera.main.WorldToScreenPoint(corners[i]), Camera.main.WorldToScreenPoint(corners[(i + 1) % 4]));
            GUI.DrawTexture(new Rect(screen.x - 5, Screen.height - screen.y - 5, 10, 10), Texture2D.whiteTexture);
        }
        GUI.color = previous;
    }

    private static void DrawLine(Vector3 a, Vector3 b)
    {
        if (a.z <= 0 || b.z <= 0) return;
        Vector2 start = new Vector2(a.x, Screen.height - a.y);
        Vector2 delta = new Vector2(b.x - a.x, a.y - b.y);
        Matrix4x4 previous = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.DrawTexture(new Rect(start.x, start.y, delta.magnitude, 1), Texture2D.whiteTexture);
        GUI.matrix = previous;
    }
}
