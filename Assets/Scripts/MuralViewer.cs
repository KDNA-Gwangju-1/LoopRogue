using UnityEngine;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>잊힌 벽화 큰 그림 창(10-19 사용자 "벽화에 상호작용하면 더 좋은 그림을 창으로") - Resources/Murals/Mural{층}(200x112)을
    /// 화면 가운데 위쪽에 3배로 띄운다. 이야기 글은 NpcDialogUI(화면 아래)가 맡고, 입력 막기·닫기도 그쪽을 따른다 - 대화창이 닫히면 같이 닫힌다.
    /// 자체 캔버스를 HUD 위, 대화창 바로 아래에 그려서 뒤를 어둡게 덮는다.</summary>
    public class MuralViewer : MonoBehaviour
    {
        private const float Scale = 3f;
        private static MuralViewer _instance;
        private GameObject _root;
        private Image _picture;
        private RectTransform _pictureRect;

        public static void Show(int stage)
        {
            var sprite = Resources.Load<Sprite>($"Murals/Mural{Mathf.Clamp(stage, 1, 10)}");
            if (sprite == null)
                return;
            if (_instance == null)
                _instance = Create();
            _instance._picture.sprite = sprite;
            _instance._pictureRect.sizeDelta = new Vector2(sprite.rect.width * Scale, sprite.rect.height * Scale);
            _instance._root.SetActive(true);
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
            Stretch((RectTransform)viewer._root.transform);

            var dim = HudUi.CreateImage(viewer._root.transform, "Dim", new Color(0f, 0f, 0f, 0.72f));
            dim.raycastTarget = false;
            Stretch(dim.rectTransform);

            // 액자 - 어두운 나무 테 + 금 테
            var frame = HudUi.CreateImage(viewer._root.transform, "Frame", new Color(0.22f, 0.15f, 0.1f, 1f));
            frame.raycastTarget = false;
            var frameRect = frame.rectTransform;
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = new Vector2(0f, 95f);
            frameRect.sizeDelta = new Vector2(200f * Scale + 28f, 112f * Scale + 28f);
            var gold = HudUi.CreateImage(frame.transform, "Gold", new Color(0.82f, 0.66f, 0.27f, 1f));
            gold.raycastTarget = false;
            gold.rectTransform.anchorMin = Vector2.zero;
            gold.rectTransform.anchorMax = Vector2.one;
            gold.rectTransform.offsetMin = new Vector2(8f, 8f);
            gold.rectTransform.offsetMax = new Vector2(-8f, -8f);

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
            if (_root.activeSelf && !NpcDialogUI.IsOpen)
                _root.SetActive(false); // 이야기 창을 닫으면 같이
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
