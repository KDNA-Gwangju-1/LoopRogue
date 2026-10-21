using UnityEngine;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>잊힌 벽화 큰 그림 창(10-19 사용자 "벽화에 상호작용하면 더 좋은 그림을 창으로") - Resources/Murals/Mural{층}(200x112)을
    /// 화면 위 띠와 대화창 사이 빈자리에 최대 3배로 띄운다(대화가 길어 창이 커지면 그림을 줄여서 겹치지 않게). 이야기 글은 NpcDialogUI(화면 아래)가 맡고, 입력 막기·닫기도 그쪽을 따른다 - 대화창이 닫히면 같이 닫힌다.
    /// 자체 캔버스를 HUD 위, 대화창 바로 아래에 그려서 뒤를 어둡게 덮는다.</summary>
    public class MuralViewer : MonoBehaviour
    {
        private const float MaxScale = 3f, MinScale = 1f, FramePad = 14f, Margin = 8f;
        private static MuralViewer _instance;
        private GameObject _root;
        private RectTransform _rootRect;
        private Image _picture;
        private RectTransform _pictureRect;
        private RectTransform _frameRect;

        public static void Show(int stage)
        {
            var sprite = Resources.Load<Sprite>($"Murals/Mural{Mathf.Clamp(stage, 1, 10)}");
            if (sprite == null)
                return;
            if (_instance == null)
                _instance = Create();
            _instance._picture.sprite = sprite;
            _instance._root.SetActive(true);
            _instance.Fit();
        }

        private static MuralViewer Create()
        {
            var canvasGo = new GameObject("MuralViewerCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 49; // HUD 위, 대화창(50) 바로 아래
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var viewer = canvasGo.AddComponent<MuralViewer>();

            viewer._root = new GameObject("Root", typeof(RectTransform));
            viewer._root.transform.SetParent(canvasGo.transform, false);
            viewer._rootRect = (RectTransform)viewer._root.transform;
            Stretch(viewer._rootRect);

            var dim = HudUi.CreateImage(viewer._root.transform, "Dim", new Color(0f, 0f, 0f, 0.72f));
            dim.raycastTarget = false;
            Stretch(dim.rectTransform);

            // 액자 - 어두운 나무 테 + 안쪽 짙은 홈(금테는 사용자가 싫어함)
            var frame = HudUi.CreateImage(viewer._root.transform, "Frame", new Color(0.22f, 0.15f, 0.1f, 1f));
            frame.raycastTarget = false;
            viewer._frameRect = frame.rectTransform;
            viewer._frameRect.anchorMin = viewer._frameRect.anchorMax = new Vector2(0.5f, 0f);
            var groove = HudUi.CreateImage(frame.transform, "Groove", new Color(0.09f, 0.06f, 0.04f, 1f));
            groove.raycastTarget = false;
            groove.rectTransform.anchorMin = Vector2.zero;
            groove.rectTransform.anchorMax = Vector2.one;
            groove.rectTransform.offsetMin = new Vector2(8f, 8f);
            groove.rectTransform.offsetMax = new Vector2(-8f, -8f);

            viewer._picture = HudUi.CreateImage(frame.transform, "Picture", Color.white);
            viewer._picture.raycastTarget = false;
            viewer._picture.preserveAspect = true;
            viewer._pictureRect = viewer._picture.rectTransform;
            viewer._pictureRect.anchorMin = viewer._pictureRect.anchorMax = new Vector2(0.5f, 0.5f);
            viewer._pictureRect.anchoredPosition = Vector2.zero;

            viewer._root.SetActive(false);
            return viewer;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (!_root.activeSelf)
                return;
            if (!NpcDialogUI.IsOpen)
                _root.SetActive(false); // 이야기 창을 닫으면 같이
            else
                Fit(); // 대화 글 길이에 따라 창 높이가 바뀐다
        }

        /// <summary>위 띠 아래 ~ 대화창 위 사이에 들어가는 가장 큰 배율(최대 3배)로 그림·액자 크기와 자리를 잡는다.</summary>
        private void Fit()
        {
            var sprite = _picture.sprite;
            if (sprite == null)
                return;
            var canvas = _rootRect.rect.size;
            var top = canvas.y - ActionBarLayout.TopBarHeight - Margin;
            var bottom = NpcDialogUI.PanelTop + Margin;
            var w = sprite.rect.width;
            var h = sprite.rect.height;
            var scale = Mathf.Clamp(Mathf.Min((top - bottom - FramePad * 2f) / h, (canvas.x - Margin * 2f - FramePad * 2f) / w), MinScale, MaxScale);
            var picture = new Vector2(Mathf.Round(w * scale), Mathf.Round(h * scale));
            _pictureRect.sizeDelta = picture;
            _frameRect.sizeDelta = picture + new Vector2(FramePad * 2f, FramePad * 2f);
            _frameRect.anchoredPosition = new Vector2(0f, Mathf.Round(Mathf.Max(bottom + _frameRect.sizeDelta.y * 0.5f, (top + bottom) * 0.5f)));
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
