using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비 Tab 능력치 창(11-03 사용자) - 던전 HUD 상태창과 같은 글(GameHUD.BuildStatText)을 "지금 던전에 들어가면 시작할 스탯"
    /// (PlayerActor.CreateStartStats - 진행 중인 판이 있으면 그 판, 없으면 새 판)으로 보여 준다. 로비에서 장비·영약을 사면 바로 바뀐다.
    /// 유물·저주 계약이 있으면(진행 중인 판) 왼쪽에 같이. 자리는 오른쪽 위 골드·마석 칸 아래.</summary>
    public class LobbyStatPanel : MonoBehaviour
    {
        private const float Top = 132f;         // 골드·마석 칸과 [Tab] 스탯 버튼 아래
        private const float BottomMargin = 64f; // 아래 안내 줄 위
        private const float StatWidth = 340f;
        private const float RefreshSeconds = 0.25f;

        private RectTransform _canvasRect;
        private GameHUD.ScrollTextPanel _stat;
        private GameHUD.ScrollTextPanel _relic;
        private float _nextRefresh;

        public bool IsOpen => _stat != null && _stat.Root.activeSelf;

        public static LobbyStatPanel Create()
        {
            var canvasGo = new GameObject("LobbyStatCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // 로비 화면 위
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // LobbyCanvas와 같게

            var panel = canvasGo.AddComponent<LobbyStatPanel>();
            panel._canvasRect = canvasGo.GetComponent<RectTransform>();
            panel._stat = new GameHUD.ScrollTextPanel(canvasGo.transform, "StatPanel", StatWidth, new Vector2(-16f, -Top));
            panel._relic = new GameHUD.ScrollTextPanel(canvasGo.transform, "OwnedRelicPanel", 300f, new Vector2(-16f - StatWidth - 8f, -Top));
            return panel;
        }

        public void Toggle()
        {
            if (IsOpen)
                Hide();
            else
                Show();
        }

        public void Show()
        {
            _stat.Root.SetActive(true);
            _stat.ResetScroll();
            _relic.ResetScroll();
            Refresh();
        }

        public void Hide()
        {
            _stat.Root.SetActive(false);
            _relic.Root.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen)
                return;
            var mouse = Pointer.current;
            _stat.HandleMouse(mouse);
            if (_relic.Root.activeSelf)
                _relic.HandleMouse(mouse);
            if (Time.unscaledTime >= _nextRefresh)
                Refresh();
        }

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            var canvasHeight = _canvasRect.rect.height > 200f ? _canvasRect.rect.height : 720f;
            var maxHeight = canvasHeight - Top - BottomMargin;

            var stats = PlayerActor.CreateStartStats(out var levels);
            _stat.SetText(GameHUD.BuildStatText(stats, levels), maxHeight);

            var hasRelics = Relics.OwnedCount > 0 || Curses.ActiveDeals.Count > 0;
            if (_relic.Root.activeSelf != hasRelics)
                _relic.Root.SetActive(hasRelics);
            if (hasRelics)
                _relic.SetText(GameHUD.BuildRelicText(), maxHeight);
        }
    }
}
