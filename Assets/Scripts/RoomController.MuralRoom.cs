using System.Collections;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>횃불 → 잊힌 벽화의 방(10-19 사용자). 층마다 방 2~9 중 한 곳(StageProgress.AltarRoom)에 "불 꺼진 제단"(벽처럼 막힌 칸)이 놓인다.
    /// 방을 비운 뒤 제단 쪽으로 횃불을 쓰면 제단에 불이 붙고 벽화의 방으로 옮겨 간다 - 벽에 그 층의 벽화(고리를 만든 자의 이야기 한 장),
    /// 가운데 벽화의 상자(골드 + 영약 2개). 문으로 나가면 원래 다음 방으로 이어진다. 층마다 한 번(StageProgress.AltarUsed).
    /// 봇은 횃불을 안 쓰니 제단은 그냥 막힌 칸이다.</summary>
    public partial class RoomController
    {
        private const int MuralWidth = 9, MuralHeight = 7;
        private Vector2Int? _altar;

        public bool IsAltar(Vector2Int p) => _altar.HasValue && _altar.Value == p;

        /// <summary>제단에 불을 붙일 수 있는지 - 몹이 남아 있으면 안 된다(벽화의 방에 다녀오면 이 방은 끝난 걸로 치고 다음 방으로 가기 때문).</summary>
        public bool AltarReady(out string why)
        {
            why = null;
            if (_enemies.Any(e => e != null && !e.Stats.IsDead))
            {
                why = Loc.T("몹이 남아 있는 동안엔 제단에 불이 붙지 않는다");
                return false;
            }
            return true;
        }

        private void TrySpawnAltar(RoomDefinition def, RoomLayout layout)
        {
            _altar = null;
            if (MuralTrial.Active && !def.IsBossRoom)
            {
                PlaceAltarNear(layout); // 에디터 "벽화의 방 체험" - 몹 없는 방에 제단을 반드시
                return;
            }
            if (_loadCleared || def.IsBossRoom || def.IsTestRoom || def.IsTutorialRoom || TestMap.Active || BossRush.Active || TutorialStage.Active)
                return;
            if (StageProgress.AltarUsed || def.RoomNumber != StageProgress.AltarRoom(GameBootstrap.RoomsPerStage))
                return;
            PlaceAltarNear(layout);
            if (_altar.HasValue && !DamagePopup.Suppressed)
                ShowMessage(Loc.T("<color=#A8B4D8>어딘가에 불 꺼진 제단이 있다...</color>"));
        }

        private void PlaceAltarNear(RoomLayout layout)
        {
            // 비밀 벽처럼 - 방 가장자리(또는 벽 옆) 빈 칸, 시작 칸에서 3칸 이상, 세워도 방이 갈라지지 않는 칸
            var cells = Enumerable.Range(0, Map.Width).SelectMany(x => Enumerable.Range(0, Map.Height).Select(y => new Vector2Int(x, y)))
                .Where(p => Map.IsWalkable(p) && p != ExitPosition
                            && Mathf.Max(Mathf.Abs(p.x - layout.PlayerStart.x), Mathf.Abs(p.y - layout.PlayerStart.y)) >= 3
                            && (p.x == 0 || p.y == 0 || p.x == Map.Width - 1 || p.y == Map.Height - 1 || Directions4.Any(d => Map.IsWall(p + d)))
                            && Directions4.Count(d => Map.IsWalkable(p + d)) >= 1)
                .OrderBy(_ => Rng.Next());
            foreach (var c in cells)
            {
                if (!WallKeepsConnected(c))
                    continue;
                AddWallProp(c, "Altar_Unlit", new Color(0.4f, 0.38f, 0.45f), "Altar");
                _altar = c;
                return;
            }
        }

        /// <summary>제단 쪽으로 횃불을 썼다(PlayerActor) - 불이 붙고 잠시 뒤 벽화의 방으로.</summary>
        public void LightAltar()
        {
            if (!_altar.HasValue)
                return;
            if (!MuralTrial.Active)
                StageProgress.MarkAltarUsed(); // 체험 중엔 몇 번이고
            AltarLitCount++;
            var at = new Vector3(_altar.Value.x * GridConstants.CellSize, _altar.Value.y * GridConstants.CellSize, 0f);
            if (_wallObjects.TryGetValue(_altar.Value, out var go) && go != null && go.TryGetComponent<SpriteRenderer>(out var r))
            {
                var lit = Resources.Load<Sprite>("Sprites/Props/Altar_Lit");
                if (lit != null)
                    r.sprite = lit;
            }
            ShowMessage(Loc.T("<color=#9CD8FF>제단에 푸른 불이 붙었다!</color> 벽 너머로 길이 열린다..."));
            if (HitFeedback.Enabled)
            {
                Fx.Play("soul_summon", at, 1.4f, fps: 14f, sortingOrder: 8);
                ScreenFlash.Play(new Color(0.5f, 0.75f, 1f), 0.3f, 0.5f);
            }
            IsInputLocked = true;
            StartCoroutine(EnterMuralAfter(DamagePopup.Suppressed ? 0f : 0.7f));
        }

        /// <summary>봇·통계용 - 제단에 불을 붙인 횟수.</summary>
        public static int AltarLitCount;

        private IEnumerator EnterMuralAfter(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            LoadMuralRoom();
        }

        /// <summary>잊힌 벽화의 방 - 몹 없는 작은 방(벽화 둘레에서 F = 큰 그림 다시 보기). 위쪽 벽 가운데 벽화(3칸), 양옆 불 붙은 제단, 가운데 벽화의 상자, 출구는 처음부터 열림.
        /// _current(원래 방)는 그대로 두어 출구를 밟으면 원래 다음 방으로 간다.</summary>
        private void LoadMuralRoom()
        {
            ClearRoom();
            _altar = null;
            var layout = new RoomLayout { Width = MuralWidth, Height = MuralHeight, PlayerStart = new Vector2Int(MuralWidth / 2, 1) };
            Map = new GridMap(layout.Width, layout.Height);
            BuildFloor(layout, false);
            _fitRoom = new Vector2Int(layout.Width, layout.Height); // 방 전체가 한 화면에

            var top = MuralHeight - 1;
            var mid = MuralWidth / 2;
            // 3칸짜리 그림 - 가운데 칸 기준, 그 층 큰 벽화(Murals/Mural{층})를 줄인 것(창으로 보는 그림과 같게). 없으면 공용 벽화.
            var muralSprite = $"Mural{Mathf.Clamp(_stage, 1, 10)}";
            if (Resources.Load<Sprite>($"Sprites/Props/{muralSprite}") == null)
                muralSprite = "Mural";
            AddWallProp(new Vector2Int(mid, top), muralSprite, new Color(0.6f, 0.55f, 0.45f), "Mural");
            Map.AddWall(new Vector2Int(mid - 1, top));
            Map.AddWall(new Vector2Int(mid + 1, top));
            AddWallProp(new Vector2Int(mid - 3, top), "Altar_Lit", new Color(0.5f, 0.7f, 1f), "MuralAltar");
            AddWallProp(new Vector2Int(mid + 3, top), "Altar_Lit", new Color(0.5f, 0.7f, 1f), "MuralAltar");

            Map.PlaceActor(_player, layout.PlayerStart);
            if (_player.TryGetComponent<SpriteAnimator>(out var anim))
                anim.SnapToGrid();
            PositionCamera(layout);
            SpawnEvent(new Vector2Int(mid, top - 2), RoomEventType.MuralChest);
            _roomCleared = true;
            IsInputLocked = false;
            SpawnExit();
            ShowMural(_stage);
        }

        // ---- 벽화 이야기(층마다 한 장) - 고리를 만든 자(10층 거울 가면 기사)의 이야기를 조금씩. 본 벽화는 저장(나중에 도감 등에서 다시 보기용).
        private const string MuralSeenKey = "LoopRogue_MuralSeen"; // 본 층 비트

        public static bool MuralSeen(int stage) => (PlayerPrefs.GetInt(MuralSeenKey, 0) & (1 << stage)) != 0;

        public static string MuralText(int stage) => Mathf.Clamp(stage, 1, 10) switch
        {
            1 => Loc.T("무릎 꿇은 기사 앞에 꺼져 가는 등불 하나가 그려져 있다.\n물감 아래 새겨진 글씨: <i>\"그날, 나는 늦었다.\"</i>"),
            2 => Loc.T("기사가 거대한 시곗바늘을 붙잡아 거꾸로 돌리고 있다. 바늘 끝에 손이 베여 붉은 물감이 번졌다.\n<i>\"한 번만 더. 딱 한 번만 더 그 순간으로.\"</i>"),
            3 => Loc.T("같은 문을 열고 들어가는 기사가 수십 겹으로 겹쳐 그려져 있다. 모두 같은 자세, 같은 걸음.\n<i>\"몇 번째인지 세는 것을 그만두었다.\"</i>"),
            4 => Loc.T("기사 곁에 붕대를 감은 모험가가 서 있다. 둘은 서로를 보지 않는다.\n<i>\"그는 길을 잃은 게 아니다. 나와 같은 것을 원했을 뿐.\"</i>"),
            5 => Loc.T("커다란 짐을 진 상인이 두 고리 사이의 틈으로 걸어 나가고 있다. 기사는 그 뒷모습을 바라만 본다.\n<i>\"틈을 지나는 법을 그는 알았다. 나는 끝내 배우지 않았다.\"</i>"),
            6 => Loc.T("젊은 사내가 기사에게 등을 돌리고 문 앞에 앉아 있다. 손에는 모래시계.\n<i>\"그는 나를 따라 내려오지 않았다. 대신 문을 지키겠다고 했다.\"</i>"),
            7 => Loc.T("기사의 그림자가 몸에서 떨어져 나와 따로 걷는다. 그림자마다 다른 짐승의 모습이다.\n<i>\"후회는 그림자를 낳고, 그림자는 문지기가 되었다.\"</i>"),
            8 => Loc.T("기사가 자기 얼굴에 거울 가면을 씌우고 있다. 거울 속에는 다른 이들의 얼굴만 비친다.\n<i>\"내 얼굴을 보지 않으려고 거울을 썼다. 그러자 거울은 남의 얼굴만 비추었다.\"</i>"),
            9 => Loc.T("무너지는 고리 바깥, 아주 작은 등불이 하나 켜져 있다. 그 불은 꺼지지 않았다.\n<i>\"고리를 끊는 방법은 하나. 그 순간이 지나가게 두는 것.\"</i>"),
            _ => Loc.T("벽화가 비어 있다. 가운데 손바닥 자국 하나만 찍혀 있다 - 크기가 꼭 내 손만 하다.\n<i>\"다음 벽화는 네가 그린다.\"</i>"),
        };

        private void ShowMural(int stage)
        {
            PlayerPrefs.SetInt(MuralSeenKey, PlayerPrefs.GetInt(MuralSeenKey, 0) | (1 << Mathf.Clamp(stage, 1, 10)));
            PlayerPrefs.Save();
            var title = Loc.F("잊힌 벽화 - {0}층", stage);
            if (GameHUD.AutoPlayActive || NpcDialogUI.Instance == null)
            {
                ShowMessage(title);
                return;
            }
            NpcDialogUI.Instance.Show(title, MuralText(stage), new[] { new NpcDialogUI.Choice(Loc.T("..."), () => NpcDialogUI.Instance.Close()) });
            MuralViewer.Show(stage); // 큰 그림 창(대화창이 닫히면 같이 닫힌다)
        }

        /// <summary>F - 벽화의 방에서 벽화(가운데 3칸) 둘레에 서 있으면 큰 그림 창을 다시 연다. 열었으면 true.</summary>
        public bool TryViewMural(Vector2Int from)
        {
            var center = new Vector2Int(MuralWidth / 2, MuralHeight - 1);
            if (!_wallObjects.TryGetValue(center, out var go) || go == null || go.name != "Mural")
                return false; // 벽화의 방이 아니다
            if (Mathf.Abs(from.x - center.x) > 2 || Mathf.Abs(from.y - center.y) > 1)
            {
                ShowMessage(Loc.T("벽화에 더 가까이 가자"));
                return true;
            }
            ShowMural(_stage);
            return true;
        }
    }
}
