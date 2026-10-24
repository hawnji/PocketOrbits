using PocketOrbits.Core.Simulation;
using UnityEngine;

namespace PocketOrbits.Rendering
{
    /// <summary>
    /// Fixed orthographic top-down camera. Fits the level bounds into the screen area left between the
    /// top and bottom HUD bars (given in pixels), respecting the device safe area.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Camera { get; private set; }
        Rect2 _bounds = new Rect2(-4.5, -8, 4.5, 8);
        float _topInset, _bottomInset;
        int _lastW, _lastH;

        public static CameraRig Create()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            var rig = cam.gameObject.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Camera = cam;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.SpaceBottom;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.allowMSAA = true;
            cam.allowHDR = false;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return rig;
        }

        public void Frame(Rect2 bounds, float topInsetPx, float bottomInsetPx)
        {
            _bounds = bounds;
            _topInset = topInsetPx;
            _bottomInset = bottomInsetPx;
            Apply();
        }

        void Apply()
        {
            if (Camera == null) return;
            _lastW = Screen.width;
            _lastH = Screen.height;
            const float margin = 0.35f;
            float w = (float)_bounds.Width + margin * 2, h = (float)_bounds.Height + margin * 2;
            float screenH = Mathf.Max(1, Screen.height);
            float availH = Mathf.Max(screenH * 0.3f, screenH - _topInset - _bottomInset);
            float aspect = Screen.width / screenH;
            // Ortho size is half the full screen height in world units.
            float sizeForHeight = h * 0.5f * screenH / availH;
            float sizeForWidth = w * 0.5f / aspect;
            float size = Mathf.Max(sizeForHeight, sizeForWidth);
            Camera.orthographicSize = size;
            float worldPerPx = size * 2f / screenH;
            // Shift so the bounds centre sits in the middle of the free area.
            float shift = (_topInset - _bottomInset) * 0.5f * worldPerPx;
            var c = _bounds.Center;
            transform.position = new Vector3((float)c.X, 30f, (float)c.Y + shift);
        }

        void LateUpdate()
        {
            if (Screen.width != _lastW || Screen.height != _lastH) Apply();
        }

        /// <summary>Screen point (pixels, origin bottom-left) to gameplay plane coordinates.</summary>
        public Vec2 ScreenToSim(Vector2 screen)
        {
            var w = Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 30f));
            return Plane2D.ToSim(w);
        }

        public Vector2 SimToScreen(Vec2 sim)
        {
            var s = Camera.WorldToScreenPoint(Plane2D.ToWorld(sim));
            return new Vector2(s.x, s.y);
        }

        /// <summary>World units per screen pixel.</summary>
        public float WorldPerPixel => Camera.orthographicSize * 2f / Mathf.Max(1, Screen.height);
    }
}
