using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GunsawRectTool;

public sealed class RectTool : MonoBehaviour
{
    private const int MoveHandle = 4;
    private LevelEditor editor = null!;
    private SpriteRenderer? target;
    private int handle = -1;
    private int operation;
    private bool changed;
    private Vector3 startMouse, startScale, startPosition, pivot, anchor;
    private Quaternion startRotation;
    private Matrix4x4 startInverse;
    private Bounds startBounds;
    private Vector2 direction;
    private float startAngle;
    private Vector2 operationMouseScreen;
    private bool moved;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly Vector3[] handles = new Vector3[4];

    internal bool IsDragging => handle >= 0;
    private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    private static bool Unsnapped => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    private static int Operation => Ctrl ? 2 : Shift ? 1 : 0;
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
            if (selection.Count != 1 || !InsideRect(selection[0].GetComponent<SpriteRenderer>()))
                return false;
            hit = MoveHandle;
        }
        editor.primarySelectedObject = null!;
        // Capture group handles too, so clicking one cannot clear or cycle the selection.
        handle = hit;
        target = selection.Count == 1 ? selection[0].GetComponent<SpriteRenderer>() : null;
        changed = false;
        if (target != null) BeginOperation();
        return true;
    }

    private bool SelectionMatches()
    {
        var selection = editor.selectedObjects;
        return target == null ? selection.Count > 1 : selection.Count == 1 && selection[0] == target.gameObject;
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
        if (target == null) return;
        operation = handle == MoveHandle ? 3 : Operation;
        direction = Direction(handle, operation == 1);
        startBounds = LocalBounds(target);
        startInverse = target.transform.worldToLocalMatrix;
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

    private void Drag()
    {
        if (target == null) return;
        if (handle != MoveHandle && operation != Operation) BeginOperation();
        moved |= ((Vector2)Input.mousePosition - operationMouseScreen).sqrMagnitude > 9f;
        if (!moved) return;
        var transform = target.transform;
        Vector3 oldPosition = transform.position, oldScale = transform.localScale;
        Quaternion oldRotation = transform.rotation;
        Vector2 oldSize = target.size;
        Vector3 mouse = Mouse();
        if (operation == 3)
        {
            Vector3 position = startPosition + mouse - startMouse;
            if (!Unsnapped && editor.altGridSnapValue > 0)
            {
                float grid = editor.altGridSnapValue;
                position.x = Mathf.Round(position.x / grid) * grid;
                position.y = Mathf.Round(position.y / grid) * grid;
            }
            position.z = startPosition.z;
            transform.position = position;
        }
        else if (operation == 2)
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
            float minY = part.fullName == "Asphalt" ? 0.143f : part.fullName == "Girder" ? 1.28f :
                part.fullName == "One way platform" ? 0.57f : part.fullName == "Conveyor belt" ? 0.36f : 0.1f;
            Vector2 size = new Vector2(
                RectMath.Resize(startBounds.size.x, delta.x, axes.x, grid, 0.1f),
                RectMath.Resize(startBounds.size.y, delta.y, axes.y, grid, minY));
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
        bool commit = IsDragging && changed && target != null && editor != null;
        handle = -1;
        changed = false;
        if (commit)
        {
            editor!.SaveState();
            editor.SendTransformAction(new List<GameObject> { target!.gameObject });
        }
        target = null;
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
            handles[i] = Operation == 1 ? (corners[i] + corners[(i + 1) % 4]) * 0.5f : corners[i];
        return true;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !Available || !Geometry()) return;
        Color previous = GUI.color;
        GUI.color = editor.selectedObjects.Count > 1 ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.3f, 0.85f, 1f);
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
