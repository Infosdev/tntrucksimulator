using UnityEngine;

namespace TNLorry.UI
{
    /// <summary>
    /// Keeps a panel inside the display's safe area (section O.3).
    ///
    /// Mandatory for this game rather than a nicety: the controls live in the screen corners,
    /// and on a notched or hole-punch phone in landscape the notch sits exactly where the
    /// steering buttons go. Without this, the cheapest and the most expensive phones both end
    /// up with an unreachable control.
    ///
    /// Put it on a full-screen child of the Canvas and parent the controls to it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("Apply the left/right insets. Off for a panel that should span the full width.")]
        [SerializeField] bool applyHorizontal = true;
        [Tooltip("Apply the top/bottom insets.")]
        [SerializeField] bool applyVertical = true;
        [Tooltip("Extra inset in pixels, on top of the reported safe area.")]
        [SerializeField] float padding;

        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastResolution;
        ScreenOrientation _lastOrientation;

        void Awake()
        {
            _rect = GetComponent<RectTransform>();
            Apply();
        }

        void Update()
        {
            // Polling rather than an event: Unity gives no safe-area-changed callback, and a
            // rotation or a multi-window resize changes it. The comparison is three cheap
            // struct checks, so this costs nothing.
            if (Screen.safeArea == _lastSafeArea &&
                Screen.width == _lastResolution.x &&
                Screen.height == _lastResolution.y &&
                Screen.orientation == _lastOrientation)
                return;

            Apply();
        }

        void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();

            var safeArea = Screen.safeArea;
            _lastSafeArea = safeArea;
            _lastResolution = new Vector2Int(Screen.width, Screen.height);
            _lastOrientation = Screen.orientation;

            if (Screen.width <= 0 || Screen.height <= 0) return;

            // Inset in pixels, then normalised to anchor space.
            var min = safeArea.position;
            var max = safeArea.position + safeArea.size;

            if (padding > 0f)
            {
                min += Vector2.one * padding;
                max -= Vector2.one * padding;
            }

            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            var anchorMin = _rect.anchorMin;
            var anchorMax = _rect.anchorMax;

            if (applyHorizontal)
            {
                anchorMin.x = min.x;
                anchorMax.x = max.x;
            }
            if (applyVertical)
            {
                anchorMin.y = min.y;
                anchorMax.y = max.y;
            }

            // A degenerate safe area (some editor/simulator states report zero) would collapse
            // the panel and hide every control, so refuse to apply it.
            if (anchorMax.x - anchorMin.x < 0.1f || anchorMax.y - anchorMin.y < 0.1f) return;

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
