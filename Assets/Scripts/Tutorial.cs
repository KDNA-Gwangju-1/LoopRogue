using System;
using UnityEngine;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>첫 판 안내(튜토리얼) - 게임 화면 아래 가운데에 작은 안내 상자를 띄운다. 게임을 멈추지 않는다.
    /// 1) 기본 조작 6단계(이동 → 공격 → 대시 → 회전 베기 → 대기 → 능력치 창)를 차례로 - 직접 해 보면 다음 단계로. 1층에서만, 공격·스킬 단계는 몹이 있을 때만 보인다.
    /// 2) 상황 안내 - 처음 겪을 때 한 번씩(출구, 아이템, 레벨업 카드, 빨간 예고 칸, NPC, 보스방, 첫 사망). 몇 초 뒤 저절로 사라진다.
    /// 진행은 저장(엔딩 뒤 새 게임에도 남김 - SaveReset.KeptInts). [건너뛰기]로 기본 조작을 끝낼 수 있고, 설정의 "튜토리얼 다시 보기"로 처음부터.
    /// 상태는 매 프레임 RoomController·PlayerActor를 들여다보고 판단한다(대기·공격만 PlayerActor가 알려 준다). 봇·테스트 맵에선 안 만든다.</summary>
    public class Tutorial : MonoBehaviour
    {
        private enum Basic
        {
            Move,
            Attack,
            Dash,
            Spin,
            Wait,
            Stats, // Tab 능력치 창
            Count,
        }

        [Flags]
        private enum Hint
        {
            None = 0,
            Exit = 1 << 0,
            Item = 1 << 1,
            LevelUp = 1 << 2,
            Danger = 1 << 3,
            Npc = 1 << 4,
            Boss = 1 << 5,
            Death = 1 << 6,
        }

        public const string BasicsKey = "LoopRogue_TutorialBasics"; // 끝낸 기본 단계 비트(Basic) - 전부면 끝
        public const string HintsKey = "LoopRogue_TutorialHints";   // 보여준 상황 안내 비트(Hint)
        private const int AllBasics = (1 << (int)Basic.Count) - 1;
        private const float HintSeconds = 7f;
        private const float PraiseSeconds = 0.9f;

        private static Tutorial _instance;
        private static bool _waitedThisFrame;

        private RoomController _room;
        private PlayerActor _player;
        private GameHUD _hud;

        private GameObject _box;
        private Text _title;
        private Text _body;
        private RectTransform _skipRect;

        private Vector2Int _lastPos;
        private int _lastDashCooldown;
        private int _lastSpinCooldown;
        private int _lastDeaths;
        private bool _attackedSinceShown;
        private Hint _activeHint;
        private float _hintUntil;
        private float _praiseUntil;

        public static bool BasicsDone => (PlayerPrefs.GetInt(BasicsKey, 0) & AllBasics) == AllBasics;

        /// <summary>설정 "튜토리얼 다시 보기" - 기본 조작과 상황 안내를 처음부터.</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(BasicsKey);
            PlayerPrefs.DeleteKey(HintsKey);
            PlayerPrefs.Save();
        }

        /// <summary>메인 씬에서 한 번(LoopManager.Initialize) - 이미 있으면 참조만 바꾼다.</summary>
        public static void Attach(RoomController room, PlayerActor player, GameHUD hud)
        {
            if (GameHUD.AutoPlayActive || TestMap.Active)
                return;
            if (_instance == null)
            {
                var go = new GameObject("Tutorial");
                _instance = go.AddComponent<Tutorial>();
                _instance.BuildUI();
            }
            _instance._room = room;
            _instance._player = player;
            _instance._hud = hud;
            _instance._lastPos = player.GridPos;
            _instance._lastDeaths = LoopRecord.TotalDeaths;
            player.OnAttackedEnemy -= _instance.OnAttacked;
            player.OnAttackedEnemy += _instance.OnAttacked;
        }

        /// <summary>PlayerActor.Wait - 스페이스로 한 턴 쉬었다.</summary>
        public static void NotifyWaited() => _waitedThisFrame = true;

        private void OnAttacked(EnemyActor enemy) => _attackedSinceShown = true;

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            if (_player != null)
                _player.OnAttackedEnemy -= OnAttacked;
        }

        private static bool Touch => TouchControls.Enabled;

        private static int Basics => PlayerPrefs.GetInt(BasicsKey, 0);
        private static bool IsDone(Basic b) => (Basics & (1 << (int)b)) != 0;

        private static void MarkDone(Basic b)
        {
            PlayerPrefs.SetInt(BasicsKey, Basics | (1 << (int)b));
            PlayerPrefs.Save();
        }

        private static Hint Hints => (Hint)PlayerPrefs.GetInt(HintsKey, 0);
        private static bool Shown(Hint h) => (Hints & h) != 0;

        private static void MarkShown(Hint h)
        {
            PlayerPrefs.SetInt(HintsKey, (int)(Hints | h));
            PlayerPrefs.Save();
        }

        // ---- 매 프레임 ----
        private void Update()
        {
            if (_room == null || _player == null)
            {
                SetVisible(false);
                return;
            }

            HandleSkipClick();
            var waited = _waitedThisFrame;
            _waitedThisFrame = false;
            var moved = _player.GridPos != _lastPos;
            _lastPos = _player.GridPos;
            var dashed = _player.DashCooldown > _lastDashCooldown;
            var spun = _player.SpinCooldown > _lastSpinCooldown;
            _lastDashCooldown = _player.DashCooldown;
            _lastSpinCooldown = _player.SpinCooldown;

            // 첫 사망 - 되살아난 뒤 안내
            if (LoopRecord.TotalDeaths > _lastDeaths)
            {
                _lastDeaths = LoopRecord.TotalDeaths;
                _pendingDeathHint = !Shown(Hint.Death);
            }

            // 어느 단계든 해 본 건 바로 끝낸 것으로(순서보다 먼저 해 봐도 다시 시키지 않는다)
            if (moved && !dashed)
                CompleteBasic(Basic.Move);
            if (_attackedSinceShown)
            {
                _attackedSinceShown = false;
                if (!dashed && !spun)
                    CompleteBasic(Basic.Attack);
            }
            if (dashed)
                CompleteBasic(Basic.Dash);
            if (spun)
                CompleteBasic(Basic.Spin);
            if (waited)
                CompleteBasic(Basic.Wait);
            if (_hud != null && _hud.IsStatPanelOpen)
                CompleteBasic(Basic.Stats);

            if (Time.unscaledTime < _praiseUntil)
                return; // "좋아요!" 잠깐 보여주는 중

            if (_room.Puzzle != null || NpcDialogUI.IsOpen || PauseMenu.BlocksInput || _player.Stats.IsDead)
            {
                SetVisible(false); // 퍼즐은 자기 설명 창이 있고, 대화·메뉴 중엔 가리지 않는다
                return;
            }

            // 상황 안내가 떠 있으면 시간이 다 될 때까지
            if (_activeHint != Hint.None)
            {
                if (Time.unscaledTime < _hintUntil && !HintResolved(_activeHint))
                    return;
                _activeHint = Hint.None;
            }

            // 기본 조작이 남아 있으면 그게 먼저 - 단 급한 안내(빨간 칸·레벨업 카드·첫 사망)는 바로
            var hint = NextHint();
            if (hint != Hint.None && CurrentBasic().HasValue && hint != Hint.Danger && hint != Hint.LevelUp && hint != Hint.Death)
                hint = Hint.None;
            if (hint != Hint.None)
            {
                ShowHint(hint);
                return;
            }

            ShowBasicOrHide();
        }

        private bool _pendingDeathHint;

        private void CompleteBasic(Basic b)
        {
            if (IsDone(b))
                return;
            var wasCurrent = _box.activeSelf && _activeHint == Hint.None && CurrentBasic() == b;
            MarkDone(b);
            if (!wasCurrent)
                return;
            _title.text = BasicsDone ? Loc.T("<color=#8CE08C>기본 조작 끝!</color>") : Loc.T("<color=#8CE08C>좋아요!</color>");
            _body.text = BasicsDone ? Loc.T("이제 방을 비우고 출구를 찾아 내려가세요. 처음 보는 것들은 그때그때 알려 드릴게요.") : "";
            _skipRect.gameObject.SetActive(false);
            _praiseUntil = Time.unscaledTime + (BasicsDone ? PraiseSeconds * 3f : PraiseSeconds);
            SfxPlayer.Play(Sfx.Hit);
        }

        /// <summary>지금 보여줄 기본 단계 - 1층에서만, 공격·스킬은 몹이 있을 때만(없으면 대기·다음 방으로 넘김).</summary>
        private Basic? CurrentBasic()
        {
            if (BasicsDone || (_room.Stage != 1 && !TutorialStage.Active) || _room.IsBossRoom || _room.IsInputLocked)
                return null;
            var enemies = AliveEnemies();
            for (var b = Basic.Move; b < Basic.Count; b++)
            {
                if (IsDone(b))
                    continue;
                if ((b == Basic.Attack || b == Basic.Dash || b == Basic.Spin) && enemies == 0)
                    continue;
                return b;
            }
            return null;
        }

        private int AliveEnemies()
        {
            var n = 0;
            foreach (var e in _room.Enemies)
                if (e != null && !e.Stats.IsDead)
                    n++;
            return n;
        }

        private void ShowBasicOrHide()
        {
            var b = CurrentBasic();
            if (!b.HasValue)
            {
                SetVisible(false);
                return;
            }
            var index = (int)b.Value + 1;
            _title.text = Loc.F("<color=#FFD966>기본 조작 {0}/{1}</color>  {2}", index, (int)Basic.Count, BasicName(b.Value));
            _body.text = BasicText(b.Value);
            _skipRect.gameObject.SetActive(true);
            SetVisible(true);
        }

        private static string BasicName(Basic b) => b switch
        {
            Basic.Move => Loc.T("이동"),
            Basic.Attack => Loc.T("공격"),
            Basic.Dash => Loc.T("대시"),
            Basic.Spin => Loc.T("회전 베기"),
            Basic.Wait => Loc.T("대기"),
            _ => Loc.T("능력치"),
        };

        private static string BasicText(Basic b) => b switch
        {
            Basic.Move => Touch
                ? Loc.T("왼쪽 아래 방향 버튼으로 한 칸씩 움직여요. 내가 한 번 움직이면 몹도 한 번 움직여요(턴제).")
                : Loc.T("<b>WASD</b> 또는 <b>방향키</b>로 한 칸씩 움직여요. 내가 한 번 움직이면 몹도 한 번 움직여요(턴제)."),
            Basic.Attack => Touch
                ? Loc.T("몹이 있는 쪽으로 방향 버튼을 누르면 공격해요. 붙어서 때려 보세요.")
                : Loc.T("몹이 있는 쪽으로 <b>이동 키</b>를 누르면 공격해요. 붙어서 때려 보세요."),
            Basic.Dash => Touch
                ? Loc.T("아래의 <b>Q 칸</b>을 누르고 방향 → 몇 칸을 단숨에 달려가며 처음 만난 몹을 때려요. 쓰고 나면 몇 턴 쉬어야 해요.")
                : Loc.T("<b>Q</b>를 누른 뒤 <b>방향 키</b> → 몇 칸을 단숨에 달려가며 처음 만난 몹을 때려요. 쓰고 나면 몇 턴 쉬어야 해요."),
            Basic.Spin => Touch
                ? Loc.T("아래의 <b>E 칸</b>을 누르면 주변 8칸의 몹을 한꺼번에 베요. 둘러싸였을 때 좋아요.")
                : Loc.T("<b>E</b>를 누르면 주변 8칸의 몹을 한꺼번에 베요. 둘러싸였을 때 좋아요."),
            Basic.Stats => Touch
                ? Loc.T("<b>능력치</b> 버튼을 눌러 보세요. 공격력·체력·장비·유물을 한눈에 볼 수 있어요. 한 번 더 누르면 닫혀요.")
                : Loc.T("<b>Tab</b>을 눌러 능력치 창을 열어 보세요. 공격력·체력·장비·유물을 한눈에 볼 수 있어요. 한 번 더 누르면 닫혀요."),
            _ => Touch
                ? Loc.T("<b>대기</b> 버튼으로 제자리에서 한 턴 쉬어요. 몹이 다가오길 기다리거나 위험한 칸을 넘길 때 써요.")
                : Loc.T("<b>Space</b>로 제자리에서 한 턴 쉬어요. 몹이 다가오길 기다리거나 위험한 칸을 넘길 때 써요."),
        };

        // ---- 상황 안내 ----
        private Hint NextHint()
        {
            if (_room.IsInputLocked)
                return Hint.None;
            if (_pendingDeathHint)
                return Hint.Death;
            if (!Shown(Hint.Danger) && _room.DangerTiles.Count > 0)
                return Hint.Danger;
            if (!Shown(Hint.Boss) && _room.IsBossRoom)
                return Hint.Boss;
            if (!Shown(Hint.LevelUp) && _hud != null && _hud.PendingUpgradeOptions != null)
                return Hint.LevelUp;
            if (!Shown(Hint.Npc) && _room.Npc != null)
                return Hint.Npc;
            if (!Shown(Hint.Exit) && _room.ExitPosition.HasValue && AliveEnemies() == 0)
                return Hint.Exit;
            if (!Shown(Hint.Item) && HasAnyItem())
                return Hint.Item;
            return Hint.None;
        }

        private static bool HasAnyItem()
        {
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
                if (Inventory.Has(t))
                    return true;
            return false;
        }

        /// <summary>안내한 상황이 끝나면 시간 전이라도 내린다(예: 레벨업 카드를 골랐다, 예고 칸이 사라졌다).</summary>
        private bool HintResolved(Hint h) => h switch
        {
            Hint.LevelUp => _hud == null || _hud.PendingUpgradeOptions == null,
            Hint.Danger => _room.DangerTiles.Count == 0 && Time.unscaledTime > _hintUntil - HintSeconds + 2.5f,
            Hint.Item => InventoryUI.IsOpen,
            _ => false,
        };

        private void ShowHint(Hint h)
        {
            MarkShown(h);
            if (h == Hint.Death)
                _pendingDeathHint = false;
            _activeHint = h;
            _hintUntil = Time.unscaledTime + HintSeconds;
            _title.text = Loc.F("<color=#7FD4FF>안내</color>  {0}", HintName(h));
            _body.text = HintText(h);
            _skipRect.gameObject.SetActive(false);
            SetVisible(true);
        }

        private static string HintName(Hint h) => h switch
        {
            Hint.Exit => Loc.T("출구"),
            Hint.Item => Loc.T("아이템"),
            Hint.LevelUp => Loc.T("레벨업"),
            Hint.Danger => Loc.T("빨간 칸"),
            Hint.Npc => Loc.T("NPC"),
            Hint.Boss => Loc.T("보스"),
            _ => Loc.T("죽어도 괜찮아요"),
        };

        private static string HintText(Hint h) => h switch
        {
            Hint.Exit => Loc.T("방을 비우면 <b>출구(계단)</b>가 열려요. 안개 속에 있으니 찾아가서 밟으세요. 방을 비울 때마다 체력도 조금 회복돼요."),
            Hint.Item => Touch
                ? Loc.T("아이템을 얻었어요! <b>가방 칸</b>을 눌러 퀵슬롯(1/2/3)에 올린 뒤, 숫자 칸을 눌러 써요. 아끼다 죽으면 잃어요.")
                : Loc.T("아이템을 얻었어요! <b>I</b>로 가방을 열어 퀵슬롯(1/2/3)에 올린 뒤, <b>숫자 키</b>로 써요. 아끼다 죽으면 잃어요."),
            Hint.LevelUp => Touch
                ? Loc.T("레벨업! 카드 하나를 골라 강해지세요. <b>능력치</b> 버튼으로 지금 상태를 볼 수 있어요.")
                : Loc.T("레벨업! 카드 하나를 골라(<b>1/2/3</b>) 강해지세요. <b>Tab</b>으로 지금 능력치·장비를 볼 수 있어요."),
            Hint.Danger => Loc.T("바닥의 <b>빨간 칸</b>은 다음 턴에 공격이 떨어지는 자리예요. 그 칸에서 비켜서세요!"),
            Hint.Npc => Touch
                ? Loc.T("누군가 나타났어요. 옆에 가서 <b>대화</b> 버튼을 누르세요. 대화는 턴을 쓰지 않아요.")
                : Loc.T("누군가 나타났어요. 옆에 가서 <b>F</b>로 말을 걸어 보세요. 대화는 턴을 쓰지 않아요."),
            Hint.Boss => Loc.T("층의 끝, <b>보스방</b>이에요. 보스는 바닥에 공격할 칸을 먼저 보여 주고 다음 턴에 때려요. 보스방엔 안개가 없어요."),
            _ => Loc.T("죽어도 <b>레벨과 능력치는 그대로</b>예요. 그 층 처음부터 다시 도전해요(골드는 조금 잃어요). 로비에서 뽑기·영약으로 더 강해질 수도 있어요."),
        };

        // ---- 화면 ----
        private void SetVisible(bool visible)
        {
            if (_box != null && _box.activeSelf != visible)
                _box.SetActive(visible);
        }

        private void HandleSkipClick()
        {
            if (_box == null || !_box.activeSelf || !_skipRect.gameObject.activeSelf || !GameInput.PointerDown)
                return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_skipRect, GameInput.PointerPosition, null))
                return;
            PlayerPrefs.SetInt(BasicsKey, AllBasics); // 기본 조작 건너뛰기(상황 안내는 계속)
            PlayerPrefs.Save();
            SetVisible(false);
        }

        private void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35; // HUD 위, 화면 번쩍임(45)·대화창(50)·Esc 메뉴(100) 아래
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _box = new GameObject("TutorialBox", typeof(RectTransform));
            _box.transform.SetParent(transform, false);
            var rect = (RectTransform)_box.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(600f, 92f);
            rect.anchoredPosition = new Vector2(0f, ActionBarLayout.StatusLineBottom + 34f); // 상태 안내 줄 위
            var image = _box.AddComponent<Image>();
            image.raycastTarget = false;
            if (PixelUi.Slice(image, "Panel"))
                image.color = new Color(0.75f, 0.85f, 1f, 0.95f);
            else
                image.color = new Color(0.08f, 0.1f, 0.16f, 0.88f);

            _title = Label("Title", 17, FontStyle.Bold, new Vector2(16f, -10f), new Vector2(-110f, -34f), TextAnchor.UpperLeft);
            _body = Label("Body", 15, FontStyle.Normal, new Vector2(16f, -36f), new Vector2(-16f, -84f), TextAnchor.UpperLeft);

            var skip = new GameObject("Skip", typeof(RectTransform));
            skip.transform.SetParent(_box.transform, false);
            _skipRect = (RectTransform)skip.transform;
            _skipRect.anchorMin = new Vector2(1f, 1f);
            _skipRect.anchorMax = new Vector2(1f, 1f);
            _skipRect.pivot = new Vector2(1f, 1f);
            _skipRect.sizeDelta = new Vector2(96f, 26f);
            _skipRect.anchoredPosition = new Vector2(-10f, -8f);
            var skipImage = skip.AddComponent<Image>();
            skipImage.color = new Color(0f, 0f, 0f, 0.35f);
            var skipText = MakeText(skip.transform, "SkipLabel", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
            skipText.text = Loc.T("건너뛰기");
            skipText.color = new Color(0.8f, 0.82f, 0.88f);
            Stretch(skipText.rectTransform, Vector2.zero, Vector2.zero);

            _box.SetActive(false);
        }

        private Text Label(string name, int size, FontStyle style, Vector2 offsetMin, Vector2 offsetMax, TextAnchor anchor)
        {
            var text = MakeText(_box.transform, name, size, style, anchor);
            var r = text.rectTransform;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.offsetMin = new Vector2(offsetMin.x, offsetMax.y);
            r.offsetMax = new Vector2(offsetMax.x, offsetMin.y);
            return text;
        }

        private static Text MakeText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        private static void Stretch(RectTransform r, Vector2 min, Vector2 max)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = min;
            r.offsetMax = max;
        }
    }
}
