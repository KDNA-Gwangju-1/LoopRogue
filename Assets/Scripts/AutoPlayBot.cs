#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoopRogue
{
    /// <summary>에디터 전용 자동 플레이 봇 - 메뉴 "LoopRogue/자동 플레이 봇으로 시작"으로 켠다. 한 번 실행에
    /// RunsPerSession판을 연속으로 돈다: 매 판 저장 데이터를 초기화한 뒤 타이틀 → 로비(쇼핑) → Main(이동/전투/
    /// 레벨업 카드) → 사망/보스 클리어 → 로비를 반복. 한 판은 스테이지 10 보스 격파, 한 스테이지에서
    /// MaxDeathsPerStage번 사망(=벽), 한 시도가 MaxTurnsPerAttempt턴 초과(=끼임) 중 하나로 끝난다. 전부 끝나면
    /// 판별 결과 + 평균을 프로젝트 폴더 BotLogs/에 쓰고 Play를 멈춘다. 게임 코드와는 공개 API(PlayerActor.BotAct,
    /// GameHUD.ChooseUpgrade 등)로만 통신한다.</summary>
    public class AutoPlayBot : MonoBehaviour
    {
        public const string EnabledPrefKey = "LoopRogue_AutoPlayBot_Enabled";

        private const int RunsPerSession = 30;
        private const int ActionsPerFrame = 200;     // 한 프레임에 최대 몇 턴 진행할지(초고속)
        private const int MaxDeathsPerStage = 40;    // 이보다 많이 죽으면 "벽"으로 보고 그 판 종료
        private const float MaxRealSeconds = 10800f; // 전체 실행 제한(3시간)

        /// <summary>판 수가 많으면 방/레벨업/로비 같은 상세 로그는 끄고 판 결과와 요약만 남긴다(100판이면 파일이 수십 MB).</summary>
        private static readonly bool DetailedLog = RunsPerSession <= 10;
        private const int MaxTurnsPerAttempt = 1500; // 한 번 시도가 이보다 길면 끼인 걸로 보고 그 판 종료
        private const int EventGiveUpTurns = 20;     // 방에 들어와서 이 턴 안에 이벤트 칸을 못 밟으면 그 방에선 포기

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        /// <summary>사망 시 로그에 남길 "죽기 직전" 맵 개수(+ 죽은 순간 1장).</summary>
        private const int MapFramesBeforeDeath = 3;

        /// <summary>방 입장 시 시작 배치 맵도 남길지(상세 로그 모드일 때만).</summary>
        private const bool LogMapOnRoomStart = true;

        private readonly Queue<string> _recentMaps = new Queue<string>();
        private readonly Queue<string> _recentDecisions = new Queue<string>(); // 끼임 진단용 - 최근 봇 판단 이유
        private string _lastDecision;

        /// <summary>레벨업 카드 우선순위(앞일수록 선호) - "적당히 괜찮게 고르는 플레이어" 흉내.
        /// 체력이 40% 미만이면 완전 회복을 최우선으로 고른다.</summary>
        private static readonly string[] CardPriority =
        {
            "공격력 강화", "흡혈", "체력 강화", "[도박] 광폭화", "날카로움", "처치 회복", "힘", "체력",
            "균형 성장", "재생", "학습", "수집", "방어", "[도박] 철갑", "[도박] 황금 욕심", "완전 회복",
        };

        /// <summary>한 판의 결과 - 스테이지 배열은 인덱스 0 = 스테이지 1, 클리어 못 한 스테이지는 -1.</summary>
        private class RunResult
        {
            public int Index;
            public string EndReason;
            public bool Completed;
            public int ReachedStage;
            public int TotalTurns;
            public int TotalDeaths;
            public int GoldEarned;
            public int FinalLevel;
            public float Seconds;
            public readonly int[] StageDeaths = Enumerable.Repeat(-1, StageProgress.MaxStage).ToArray();
            public readonly int[] StageTurns = Enumerable.Repeat(-1, StageProgress.MaxStage).ToArray();
            public readonly int[] StageClearLevel = Enumerable.Repeat(-1, StageProgress.MaxStage).ToArray();

            // 구매/성장 통계
            public readonly int[] SinglePulls = new int[3];  // ItemSlot 순서, 1회 뽑기 횟수
            public readonly int[] MultiPulls = new int[3];   // 10연차 횟수
            public readonly int[] TotalPulls = new int[3];   // 실제 뽑은 장비 수(1회 + 10연차×10)
            public readonly int[] ShopLevels = new int[3];
            public readonly int[] Potions = new int[3];      // 공격력/체력/치명타
            public int GoldSpentGacha;
            public int GoldSpentPotion;
            public int GoldLostDeath;
            public int LevelUps;
            public int EternalCount;
            public float FinalAttack;
            public float FinalMaxHealth;
            public float FinalCritChance;
            public float FinalCritMultiplier;

            // 방 랜덤화/보스 패턴/이벤트 통계
            public int PatternResolves;   // 보스 예고 공격 발동 횟수
            public int PatternHits;       // 그중 맞은 횟수
            public int Dodges;            // 봇이 예고 칸에서 피한 횟수
            public int Waits;             // 봇이 대기한 횟수
            public readonly int[] Events = new int[4]; // RoomEventType 순서
        }

        private static readonly string[] EventNames = { "보물상자", "회복 샘", "축복 제단", "저주받은 상자" };

        private static readonly PotionType[] PotionOrder = { PotionType.Attack, PotionType.Health, PotionType.Critical };
        private static readonly string[] SlotNames = { "검", "갑옷", "반지" };

        private StreamWriter _log;
        private float _sessionStartTime;
        private float _runStartTime;
        private string _lastScene;
        private bool _sceneHandled;
        private bool _sessionFinished;
        private bool _runEnding; // 판이 끝나서 다음 판으로 넘어가는 중(이번 프레임엔 더 아무것도 안 함)

        // Main 씬 참조(씬이 바뀔 때마다 다시 찾음)
        private PlayerActor _player;
        private RoomController _room;
        private GameHUD _hud;
        private string _lastRoomName;

        // 현재 판 통계
        private RunResult _run;
        private int _attemptTurns;
        private int _roomTurns;
        private int _stageDeaths;
        private int _stageTurns;
        private int _enteredStage;
        private int _lastGold;
        private int _lastKnownLevel = 1;
        private int _lastEventCount;

        // 전체 통계
        private readonly List<RunResult> _results = new List<RunResult>();
        private readonly Dictionary<string, int> _cardPicks = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!UnityEditor.EditorPrefs.GetBool(EnabledPrefKey, false))
                return;

            var go = new GameObject("AutoPlayBot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPlayBot>();
        }

        private void Start()
        {
            var dir = Path.Combine(Application.dataPath, "..", "BotLogs");
            Directory.CreateDirectory(dir);
            var path = Path.GetFullPath(Path.Combine(dir, $"bot_{DateTime.Now:yyyyMMdd_HHmmss}.txt"));
            _log = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            Debug.Log($"[AutoPlayBot] 시작 ({RunsPerSession}판) - 로그: {path}");

            DamagePopup.Suppressed = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            _sessionStartTime = Time.realtimeSinceStartup;
            Log($"=== 자동 플레이 {RunsPerSession}판 연속 시작 ===");
            BeginRun();
        }

        private void OnDestroy()
        {
            DamagePopup.Suppressed = false;
            _log?.Dispose();
        }

        private void Detail(string message)
        {
            if (DetailedLog)
                Log(message);
        }

        private void Log(string message)
        {
            var t = Time.realtimeSinceStartup - _sessionStartTime;
            _log?.WriteLine($"[{t,7:0.0}s] {message}");
        }

        /// <summary>새 판 시작 - 저장 초기화 후 타이틀부터(첫 판은 이미 타이틀에서 시작하므로 씬 로드 생략).</summary>
        private void BeginRun()
        {
            SaveReset.ResetAll();

            _run = new RunResult { Index = _results.Count + 1 };
            BossBrain.PatternResolveCount = 0;
            BossBrain.PatternHitCount = 0;
            RoomController.EventTriggeredCount = 0;
            _lastEventCount = 0;
            _runStartTime = Time.realtimeSinceStartup;
            _attemptTurns = 0;
            _roomTurns = 0;
            _stageDeaths = 0;
            _stageTurns = 0;
            _enteredStage = StageProgress.CurrentStage;
            _lastGold = GoldWallet.Gold;
            _lastKnownLevel = 1;
            _runEnding = false;

            Detail("");
            Detail($"########## {_run.Index}판 시작 (저장 데이터 초기화됨) ##########");

            if (_results.Count > 0)
            {
                _lastScene = null;
                SceneManager.LoadScene("Title");
            }
        }

        private void Update()
        {
            if (_sessionFinished)
                return;

            if (_runEnding)
            {
                _runEnding = false;
                if (_results.Count >= RunsPerSession)
                    FinishSession("요청한 판 수 완료");
                else
                    BeginRun();
                return;
            }

            TrackGold();

            if (Time.realtimeSinceStartup - _sessionStartTime > MaxRealSeconds)
            {
                EndRun($"전체 시간 제한({MaxRealSeconds:0}초) 도달", false);
                FinishSession("시간 제한");
                return;
            }

            var scene = SceneManager.GetActiveScene().name;
            if (scene != _lastScene)
            {
                _lastScene = scene;
                _sceneHandled = false;
                _player = null;
                _room = null;
                _hud = null;
                _lastRoomName = null;
            }

            switch (scene)
            {
                case "Title":
                    if (!_sceneHandled)
                    {
                        _sceneHandled = true;
                        SceneManager.LoadScene("Lobby");
                    }
                    break;
                case "Lobby":
                    if (!_sceneHandled)
                    {
                        _sceneHandled = true;
                        DoShopping();
                        SceneManager.LoadScene("Main");
                    }
                    break;
                case "Main":
                    UpdateMain();
                    break;
            }
        }

        private void TrackGold()
        {
            var gold = GoldWallet.Gold;
            if (gold > _lastGold)
                _run.GoldEarned += gold - _lastGold;
            _lastGold = gold;
        }

        // ===================== 로비 =====================

        private void DoShopping()
        {
            var goldBefore = GoldWallet.Gold;
            var bought = new List<string>();

            // 가챠(10연차 되면 10연차)와 영약을 번갈아 산다 - 한 바퀴 돌며 아무것도 못 사면 종료.
            var steps = new Func<string>[]
            {
                () => BuyGacha(ItemSlot.Weapon),
                () => BuyPotion(PotionType.Attack, "공격력영약"),
                () => BuyGacha(ItemSlot.Armor),
                () => BuyPotion(PotionType.Health, "체력영약"),
                () => BuyGacha(ItemSlot.Accessory),
                () => BuyPotion(PotionType.Critical, "치명타영약"),
            };

            var safety = 0;
            bool anyThisRound;
            do
            {
                anyThisRound = false;
                foreach (var step in steps)
                {
                    var result = step();
                    if (result == null)
                        continue;
                    bought.Add(result);
                    anyThisRound = true;
                }
            } while (anyThisRound && ++safety < 500);

            var summary = bought.GroupBy(b => b).Select(g => $"{g.Key}x{g.Count()}");
            Detail($"[로비] 스테이지 {StageProgress.CurrentStage} | 골드 {goldBefore} → {GoldWallet.Gold} | 구매: {string.Join(", ", summary)}");
            Detail($"       장비: {EquipLine()} | 상점Lv: 검{GachaSystem.GetShopLevel(ItemSlot.Weapon)}/갑옷{GachaSystem.GetShopLevel(ItemSlot.Armor)}/반지{GachaSystem.GetShopLevel(ItemSlot.Accessory)}" +
                $" | 영약: 공{StatPotionWallet.GetCount(PotionType.Attack)}/체{StatPotionWallet.GetCount(PotionType.Health)}/치{StatPotionWallet.GetCount(PotionType.Critical)}");
            _lastGold = GoldWallet.Gold;
        }

        private string BuyGacha(ItemSlot slot)
        {
            var name = EquipmentData.Templates[slot].BaseName;
            var multiCost = GachaSystem.GetMultiPullCost(slot);
            if (GoldWallet.Gold >= multiCost)
            {
                if (GachaSystem.PullMulti(slot) == null)
                    return null;
                _run.MultiPulls[(int)slot]++;
                _run.GoldSpentGacha += multiCost;
                return $"{name}10연차";
            }

            var cost = GachaSystem.GetPullCost(slot);
            if (GachaSystem.Pull(slot) == null)
                return null;
            _run.SinglePulls[(int)slot]++;
            _run.GoldSpentGacha += cost;
            return $"{name}뽑기";
        }

        private string BuyPotion(PotionType type, string label)
        {
            var cost = StatPotionWallet.GetNextCost(type);
            if (!StatPotionWallet.IsUnlocked(type) || !StatPotionWallet.TryBuy(type))
                return null;
            _run.GoldSpentPotion += cost;
            return label;
        }

        private static string EquipLine()
        {
            var parts = new List<string>();
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var grade = EquipmentWallet.GetEquipped(slot);
                parts.Add(grade.HasValue ? EquipmentData.DisplayName(slot, grade.Value) : $"{EquipmentData.Templates[slot].BaseName}(없음)");
            }
            return string.Join(" ", parts);
        }

        // ===================== Main =====================

        private void UpdateMain()
        {
            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerActor>();
                _room = FindFirstObjectByType<RoomController>();
                _hud = FindFirstObjectByType<GameHUD>();
                if (_player == null || _room == null || _hud == null || _player.Stats == null)
                    return;

                if (_room.Stage != _enteredStage)
                {
                    _enteredStage = _room.Stage;
                    _stageDeaths = 0;
                    _stageTurns = 0;
                }
                _attemptTurns = 0;
                Detail($"[입장] 스테이지 {_room.Stage} | {StatLine()}");
            }

            for (var i = 0; i < ActionsPerFrame; i++)
            {
                if (!StepMain())
                    break;
            }
        }

        /// <summary>한 번의 판단/행동. 계속 진행해도 되면 true, 이번 프레임은 그만(씬 전환 등)이면 false.</summary>
        private bool StepMain()
        {
            if (_runEnding || _player == null || _hud == null)
                return false;

            _lastKnownLevel = _player.Levels.Level;

            if (_room.RoomName != _lastRoomName)
            {
                if (_lastRoomName != null && !_player.Stats.IsDead)
                    Detail($"  방 클리어: {_lastRoomName} ({_roomTurns}턴, HP {_player.Stats.CurrentHealth:0}/{_player.Stats.MaxHealth:0}, Lv{_player.Levels.Level})");
                _lastRoomName = _room.RoomName;
                _roomTurns = 0;
                _recentMaps.Clear();
                _recentDecisions.Clear();
                if (LogMapOnRoomStart && DetailedLog)
                    Log(RenderMap("방 시작"));
            }

            var options = _hud.PendingUpgradeOptions;
            if (options != null)
            {
                PickUpgrade(options);
                return true;
            }

            if (_hud.IsDeathChoiceOpen)
            {
                OnDeath();
                return false; // 로비로 씬 전환(또는 판 종료)
            }

            if (_hud.IsStageClearOpen)
            {
                OnStageClear();
                return false;
            }

            if (!_player.CanAct)
                return false;

            if (_attemptTurns >= MaxTurnsPerAttempt)
            {
                Log($"  !! {_room.RoomName}에서 한 시도가 {MaxTurnsPerAttempt}턴을 넘김 - 끼임 의심 | {StatLine()}");
                Log(RenderMap("끼인 순간"));
                Log($"    이벤트 칸: {(_room.Event != null ? $"{_room.Event.DisplayName} ({_room.Event.GridPos.x},{_room.Event.GridPos.y})" : "없음")} | 이 방 턴 {_roomTurns}");
                Log($"    최근 봇 판단: {string.Join(" → ", _recentDecisions)}");
                EndRun("한 시도에서 턴 제한 초과(길찾기/진행 막힘 의심)", false);
                return false;
            }

            var dir = ChooseDirection();
            _recentDecisions.Enqueue($"{_lastDecision}@({_player.GridPos.x},{_player.GridPos.y})");
            while (_recentDecisions.Count > 16)
                _recentDecisions.Dequeue();
            if (dir.HasValue)
                _player.BotAct(dir.Value);
            else
            {
                _player.BotWait(); // 움직일 수 있는 칸이 전부 예고 칸이면 제자리 대기(저격 2발 등)
                _run.Waits++;
            }
            if (RoomController.EventTriggeredCount != _lastEventCount)
            {
                _lastEventCount = RoomController.EventTriggeredCount;
                _run.Events[(int)RoomController.LastEventType]++;
                Detail($"  이벤트: {EventNames[(int)RoomController.LastEventType]} ({_room.RoomName})");
            }
            RememberMap();
            _run.TotalTurns++;
            _attemptTurns++;
            _roomTurns++;
            _stageTurns++;
            return true;
        }

        private void PickUpgrade(IReadOnlyList<UpgradeOption> options)
        {
            var lowHp = _player.Stats.CurrentHealth < _player.Stats.MaxHealth * 0.4f;
            var bestIndex = 0;
            var bestScore = int.MaxValue;
            for (var i = 0; i < options.Count; i++)
            {
                var score = Array.IndexOf(CardPriority, options[i].Title);
                if (score < 0) score = CardPriority.Length;
                if (lowHp && options[i].Title == "완전 회복") score = -1;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            var chosen = options[bestIndex].Title;
            _run.LevelUps++;
            _cardPicks[chosen] = _cardPicks.TryGetValue(chosen, out var c) ? c + 1 : 1;
            Detail($"  레벨업 Lv{_player.Levels.Level}: [{string.Join(" / ", options.Select(o => o.Title))}] → {chosen}");
            _hud.ChooseUpgrade(bestIndex);
        }

        private void OnDeath()
        {
            _run.TotalDeaths++;
            _stageDeaths++;
            _run.GoldLostDeath += _hud.LastDeathGoldPenalty;

            var bossInfo = string.Empty;
            if (_room.IsBossRoom)
            {
                var boss = _room.Enemies.FirstOrDefault(e => e != null && e.IsBoss);
                if (boss != null)
                    bossInfo = $" | 보스 남은 HP {boss.Stats.CurrentHealth:0}/{boss.Stats.MaxHealth:0} ({boss.Stats.CurrentHealth / boss.Stats.MaxHealth * 100f:0}%)";
            }

            Detail($"[사망 #{_stageDeaths}] {_room.RoomName} ({_attemptTurns}턴){bossInfo} | 데스 패널티 골드 -{_hud.LastDeathGoldPenalty} | {StatLine()}");
            LogDeathMaps();

            if (_stageDeaths >= MaxDeathsPerStage)
            {
                EndRun($"스테이지 {_room.Stage}에서 {MaxDeathsPerStage}번 사망 - 넘기 힘든 벽", false);
                return;
            }

            // 죽으면 항상 로비로 - 어차피 방1부터 다시라서, 번 골드를 바로 쓰는 게 이득.
            _hud.ChooseDeathLobby();
        }

        private void OnStageClear()
        {
            var stage = _room.Stage;
            var idx = stage - 1;
            _run.StageDeaths[idx] = _stageDeaths;
            _run.StageTurns[idx] = _stageTurns;
            _run.StageClearLevel[idx] = _player.Levels.Level;

            Detail($"[보스 격파] 스테이지 {stage,2}: 사망 {_stageDeaths,2}회, {_stageTurns,5}턴, 클리어 시 Lv{_player.Levels.Level}, " +
                $"ATK {_player.Stats.AttackPower:0}, 최대HP {_player.Stats.MaxHealth:0}, 보유골드 {GoldWallet.Gold}");

            if (stage >= StageProgress.MaxStage)
            {
                EndRun("스테이지 10 보스 격파 - 완주!", true);
                return;
            }

            _hud.ConfirmStageClear();
        }

        /// <summary>1) 보스 예고 칸 위에 서 있으면 안전한 옆 칸으로 피한다 2) 인접한 적이 있으면 체력이 제일 낮은 적을
        /// 때린다 3) 이벤트 칸이 있으면 밟으러 간다(EventGiveUpTurns 안에서만) 4) 없으면 BFS로 "적 옆 칸"까지 최단 경로의 첫 걸음
        /// (예고 칸은 밟지 않음). 경로가 없으면 아무 빈 칸으로.</summary>
        private Vector2Int? ChooseDirection()
        {
            var map = _room.Map;
            var start = _player.GridPos;
            var danger = _room.DangerTiles;

            if (danger.Contains(start))
            {
                Vector2Int? safest = null;
                var fewestAdjacent = int.MaxValue;
                foreach (var d in Directions)
                {
                    var next = start + d;
                    if (!map.IsWalkable(next) || danger.Contains(next))
                        continue;
                    var adjacentEnemies = Directions.Count(dd => map.GetActorAt(next + dd) is EnemyActor);
                    if (adjacentEnemies < fewestAdjacent)
                    {
                        fewestAdjacent = adjacentEnemies;
                        safest = d;
                    }
                }
                if (safest.HasValue)
                {
                    _run.Dodges++;
                    _lastDecision = "회피";
                    return safest.Value;
                }
            }

            EnemyActor target = null;
            var targetDir = Vector2Int.zero;
            foreach (var d in Directions)
            {
                if (map.GetActorAt(start + d) is EnemyActor e && !e.Stats.IsDead &&
                    (target == null || e.Stats.CurrentHealth < target.Stats.CurrentHealth))
                {
                    target = e;
                    targetDir = d;
                }
            }
            if (target != null)
            {
                _lastDecision = "공격";
                return targetDir;
            }

            // 이벤트 칸 - 옆에 적이 없을 때만, 그리고 이 방에서 EventGiveUpTurns턴 안에 못 밟으면 포기
            // (몹이 길을 막았다 비켰다 하면 이벤트만 쫓다가 영원히 왔다 갔다 하던 문제).
            if (_room.Event != null && _roomTurns < EventGiveUpTurns)
            {
                var eventPos = _room.Event.GridPos;
                var step = FirstStepTo(c => c == eventPos, c => c == eventPos || (map.IsWalkable(c) && !danger.Contains(c)));
                if (step.HasValue)
                {
                    _lastDecision = "이벤트로";
                    return step.Value;
                }
                _lastDecision = "이벤트경로없음";
            }

            // 적에게 가는 길에 이벤트 칸이 있으면 그냥 밟고 지나간다.
            var eventCell = _room.Event != null ? _room.Event.GridPos : (Vector2Int?)null;
            var toEnemy = FirstStepTo(
                c => Directions.Any(d => map.GetActorAt(c + d) is EnemyActor),
                c => (map.IsWalkable(c) || c == eventCell) && !danger.Contains(c));
            if (toEnemy.HasValue)
            {
                _lastDecision = "적에게";
                return toEnemy.Value;
            }

            // 갈 길이 없으면 예고 칸이 아닌 아무 빈 칸으로, 그것도 없으면 대기.
            foreach (var d in Directions)
            {
                if (map.IsWalkable(start + d) && !danger.Contains(start + d))
                {
                    _lastDecision = "경로없음-아무데나";
                    return d;
                }
            }
            _lastDecision = "대기";
            return null;
        }

        /// <summary>플레이어 칸에서 passable 칸만 밟는 BFS로, isGoal을 만족하는 가장 가까운 칸까지의 첫 걸음 방향.</summary>
        private Vector2Int? FirstStepTo(Func<Vector2Int, bool> isGoal, Func<Vector2Int, bool> passable)
        {
            var start = _player.GridPos;
            var firstStep = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var d in Directions)
            {
                var next = start + d;
                if (!passable(next))
                    continue;
                firstStep[next] = d;
                queue.Enqueue(next);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (isGoal(cur))
                    return firstStep[cur];
                foreach (var d in Directions)
                {
                    var next = cur + d;
                    if (next == start || firstStep.ContainsKey(next) || !passable(next))
                        continue;
                    firstStep[next] = firstStep[cur];
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        // ===================== 맵 스냅샷 =====================

        /// <summary>매 턴 행동 직후(몹 턴까지 끝난 상태) 맵을 버퍼에 쌓는다 - 사망 시 "죽기 직전 몇 턴"을 보여주기 위해.
        /// 마지막 한 장은 죽은 순간 그대로라 MapFramesBeforeDeath + 1장을 유지한다.</summary>
        private void RememberMap()
        {
            if (_room == null || _room.Map == null)
                return;
            _recentMaps.Enqueue(RenderMap($"턴 {_attemptTurns + 1}"));
            while (_recentMaps.Count > MapFramesBeforeDeath + 1)
                _recentMaps.Dequeue();
        }

        private void LogDeathMaps()
        {
            if (_recentMaps.Count == 0)
                return;
            Log($"  --- {_run.Index}판 사망 #{_stageDeaths} 직전 맵 ({_room.RoomName}) ---");
            foreach (var frame in _recentMaps)
                Log(frame);
            _recentMaps.Clear();
        }

        /// <summary>현재 방을 글자로 그린다. 위쪽이 y가 큰 쪽(화면과 같은 방향).
        /// P 플레이어(!는 예고 칸 위) / M 근접 몹 / A 궁수 / B 보스 / m 졸개 / E 이벤트 / # 벽 / x 보스 예고 칸 / . 빈 칸,
        /// 아래에 몹별 좌표와 HP.</summary>
        private string RenderMap(string title)
        {
            var map = _room.Map;
            var sb = new StringBuilder();
            sb.Append($"  [{title}] {_room.RoomName} | 플레이어 ({_player.GridPos.x},{_player.GridPos.y}) " +
                      $"HP {_player.Stats.CurrentHealth:0}/{_player.Stats.MaxHealth:0}\n");

            for (var y = map.Height - 1; y >= 0; y--)
            {
                sb.Append("    ");
                for (var x = 0; x < map.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    var actor = map.GetActorAt(cell);
                    var c = actor switch
                    {
                        PlayerActor _ => _room.DangerTiles.Contains(cell) ? '!' : 'P',
                        EnemyActor e when e.IsBoss => 'B',
                        EnemyActor e when e.IsMinion => 'm',
                        EnemyActor e when e.Kind == EnemyKind.Ranged => 'A',
                        EnemyActor _ => 'M',
                        RoomEventActor _ => 'E',
                        _ => map.IsWall(cell) ? '#' : _room.DangerTiles.Contains(cell) ? 'x' : '.',
                    };
                    sb.Append(c).Append(' ');
                }
                sb.Append('\n');
            }

            var enemies = _room.Enemies.Where(e => e != null && !e.Stats.IsDead)
                .Select(e => $"{(e.IsBoss ? 'B' : e.Kind == EnemyKind.Ranged ? 'A' : 'M')}({e.GridPos.x},{e.GridPos.y}) HP{e.Stats.CurrentHealth:0}");
            sb.Append($"    적: {string.Join("  ", enemies)}");
            return sb.ToString();
        }

        private string StatLine()
        {
            var s = _player.Stats;
            return $"Lv{_player.Levels.Level} HP {s.CurrentHealth:0}/{s.MaxHealth:0} ATK {s.AttackPower:0.#} 치명 {s.CriticalChanceRate * 100f:0}%(배율 {s.CriticalDamageMultiplier * 100f:0}%) " +
                   $"피감 {s.DamageReductionRate * 100f:0}% 흡혈 {s.LifeStealRate * 100f:0}% 재생 {s.RegenPerTurnRate * 100f:0.#}% 처치회복 {s.KillHealRate * 100f:0}% " +
                   $"경험치+{s.EffectiveExpBonus * 100f:0}% 골드+{s.EffectiveGoldBonus * 100f:0}% | 골드 {GoldWallet.Gold}";
        }

        // ===================== 판 종료 / 세션 종료 =====================

        /// <summary>현재 판을 결과에 기록하고, 다음 프레임에 다음 판(또는 세션 종료)으로 넘어가게 표시한다.</summary>
        private void EndRun(string reason, bool completed)
        {
            if (_run == null || _runEnding)
                return;

            _run.EndReason = reason;
            _run.Completed = completed;
            _run.ReachedStage = StageProgress.CurrentStage;
            _run.FinalLevel = _lastKnownLevel;
            _run.Seconds = Time.realtimeSinceStartup - _runStartTime;
            CollectFinalState(_run);
            _results.Add(_run);

            Debug.Log($"[AutoPlayBot] {_run.Index}/{RunsPerSession}판 종료 - 사망 {_run.TotalDeaths}회");
            Log($"{_run.Index,3}판 종료: {reason} | {_run.Seconds:0.0}초, {_run.TotalTurns}턴, 사망 {_run.TotalDeaths}회, " +
                $"획득 골드 {_run.GoldEarned}, 최종 Lv{_run.FinalLevel} | 최종 장비: {EquipLine()}");
            Log($"      {PurchaseLine(_run)}");

            _runEnding = true;
        }

        private void CollectFinalState(RunResult r)
        {
            // 판 시작 때 골드 0으로 초기화하므로 "번 골드 = 남은 골드 + 쓴 골드 + 뺏긴 골드"가 정확하다
            // (프레임 단위 증가 추적은 한 프레임 안에서 벌고 뺏기면 덜 잡힌다).
            r.GoldEarned = GoldWallet.Gold + r.GoldSpentGacha + r.GoldSpentPotion + r.GoldLostDeath;

            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var i = (int)slot;
                r.TotalPulls[i] = GachaSystem.GetPullCount(slot);
                r.ShopLevels[i] = GachaSystem.GetShopLevel(slot);
                if (EquipmentWallet.GetEquipped(slot) == ItemGrade.Eternal)
                    r.EternalCount++;
            }
            for (var i = 0; i < PotionOrder.Length; i++)
                r.Potions[i] = StatPotionWallet.GetCount(PotionOrder[i]);

            r.PatternResolves = BossBrain.PatternResolveCount;
            r.PatternHits = BossBrain.PatternHitCount;

            if (_player != null && _player.Stats != null)
            {
                r.FinalAttack = _player.Stats.AttackPower;
                r.FinalMaxHealth = _player.Stats.MaxHealth;
                r.FinalCritChance = _player.Stats.CriticalChanceRate;
                r.FinalCritMultiplier = _player.Stats.CriticalDamageMultiplier;
            }
        }

        private static string PurchaseLine(RunResult r)
        {
            var pulls = string.Join(" ", Enumerable.Range(0, 3).Select(i =>
                $"{SlotNames[i]} {r.TotalPulls[i]}개(1회 {r.SinglePulls[i]}/10연차 {r.MultiPulls[i]}, 상점Lv{r.ShopLevels[i]})"));
            return $"뽑기: {pulls} | 영약: 공{r.Potions[0]} 체{r.Potions[1]} 치{r.Potions[2]} | 레벨업 {r.LevelUps}회 | " +
                   $"골드 사용: 뽑기 {r.GoldSpentGacha} / 영약 {r.GoldSpentPotion} / 데스패널티 {r.GoldLostDeath} | " +
                   $"최종 ATK {r.FinalAttack:0} 최대HP {r.FinalMaxHealth:0} 치명 {r.FinalCritChance * 100f:0}%(배율 {r.FinalCritMultiplier * 100f:0}%) | " +
                   $"보스 예고공격 {r.PatternResolves}회 중 {r.PatternHits}회 맞음(회피 이동 {r.Dodges}, 대기 {r.Waits}) | " +
                   $"이벤트: {string.Join(" ", Enumerable.Range(0, 4).Select(i => $"{EventNames[i]} {r.Events[i]}"))}";
        }

        private static float Percentile(List<float> values, float p)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var idx = Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[idx];
        }

        private void FinishSession(string reason)
        {
            if (_sessionFinished)
                return;
            _sessionFinished = true;

            var n = _results.Count;
            Log("");
            Log($"================ 종료: {reason} ({n}판) ================");

            if (n > 0)
            {
                if (DetailedLog)
                    Log("--- 판별 요약 ---");
                foreach (var r in DetailedLog ? _results : new List<RunResult>())
                    Log($"{r.Index}판: {(r.Completed ? "완주" : "실패")} | 도달 스테이지 {r.ReachedStage} | 사망 {r.TotalDeaths,3} | {r.TotalTurns,5}턴 | " +
                        $"최종 Lv{r.FinalLevel} | 골드 {r.GoldEarned} | {r.EndReason}");

                Log("");
                Log($"완주율 {_results.Count(r => r.Completed)}/{n} | 평균 사망 {_results.Average(r => r.TotalDeaths):0.0}회 | " +
                    $"평균 {_results.Average(r => r.TotalTurns):0}턴 | 평균 최종 Lv{_results.Average(r => r.FinalLevel):0.0} | " +
                    $"평균 획득 골드 {_results.Average(r => r.GoldEarned):0}");
                var deaths = _results.Select(r => (float)r.TotalDeaths).ToList();
                Log($"총 사망 분포: 최소 {deaths.Min():0} / 하위10% {Percentile(deaths, 0.1f):0} / 중앙값 {Percentile(deaths, 0.5f):0} / " +
                    $"상위10% {Percentile(deaths, 0.9f):0} / 최대 {deaths.Max():0}");
                var failed = _results.Where(r => !r.Completed).GroupBy(r => r.EndReason).Select(g => $"{g.Key} x{g.Count()}").ToList();
                if (failed.Count > 0)
                    Log($"실패 사유: {string.Join(", ", failed)}");

                Log("");
                Log("--- 구매/성장 평균 (판당) ---");
                for (var i = 0; i < 3; i++)
                {
                    var slot = i;
                    Log($"{SlotNames[slot]} 뽑기: 장비 {_results.Average(r => r.TotalPulls[slot]):0}개 (1회 {_results.Average(r => r.SinglePulls[slot]):0.0}번, " +
                        $"10연차 {_results.Average(r => r.MultiPulls[slot]):0.0}번) | 최종 상점Lv {_results.Average(r => r.ShopLevels[slot]):0.0}");
                }
                Log($"영약: 공격력 {_results.Average(r => r.Potions[0]):0.0}개 / 체력 {_results.Average(r => r.Potions[1]):0.0}개 / 치명타 {_results.Average(r => r.Potions[2]):0.0}개");
                Log($"레벨업 {_results.Average(r => r.LevelUps):0.0}회 | 최종 영원 장비 {_results.Average(r => r.EternalCount):0.0}/3개 " +
                    $"(3개 모두 영원인 판 {_results.Count(r => r.EternalCount == 3)}/{n})");
                Log($"골드: 획득 {_results.Average(r => r.GoldEarned):0} / 뽑기 사용 {_results.Average(r => r.GoldSpentGacha):0} / " +
                    $"영약 사용 {_results.Average(r => r.GoldSpentPotion):0} / 데스 패널티로 잃음 {_results.Average(r => r.GoldLostDeath):0}");
                var resolves = _results.Sum(r => r.PatternResolves);
                var hits = _results.Sum(r => r.PatternHits);
                Log($"보스 예고 공격: 판당 {_results.Average(r => r.PatternResolves):0}회 발동, 적중률 {(resolves > 0 ? hits * 100f / resolves : 0f):0}%");
                Log($"방 이벤트(판당): {string.Join(" / ", Enumerable.Range(0, 4).Select(i => $"{EventNames[i]} {_results.Average(r => r.Events[i]):0.0}"))}");
                Log($"최종 스탯: ATK {_results.Average(r => r.FinalAttack):0} / 최대HP {_results.Average(r => r.FinalMaxHealth):0} / " +
                    $"치명 {_results.Average(r => r.FinalCritChance) * 100f:0}% (배율 {_results.Average(r => r.FinalCritMultiplier) * 100f:0}%)");

                Log("");
                Log("--- 스테이지별 (클리어한 판 기준) ---");
                Log("스테이지 | 클리어 | 사망 평균(최소~최대) | 중앙값 | 상위10% | 턴 평균 | 클리어 Lv 평균");
                for (var i = 0; i < StageProgress.MaxStage; i++)
                {
                    var cleared = _results.Where(r => r.StageDeaths[i] >= 0).ToList();
                    if (cleared.Count == 0)
                    {
                        Log($"   {i + 1,2}    |  0/{n}  | -");
                        continue;
                    }
                    var sd = cleared.Select(r => (float)r.StageDeaths[i]).ToList();
                    Log($"   {i + 1,2}    | {cleared.Count,3}/{n} | {sd.Average(),4:0.0} ({sd.Min():0}~{sd.Max():0})" +
                        $"      | {Percentile(sd, 0.5f),4:0} | {Percentile(sd, 0.9f),4:0} | {cleared.Average(r => r.StageTurns[i]),5:0} | Lv{cleared.Average(r => r.StageClearLevel[i]):0.0}");
                }
            }

            Log("");
            Log("--- 레벨업 카드 선택 횟수 (전체 판 합계) ---");
            foreach (var pair in _cardPicks.OrderByDescending(p => p.Value))
                Log($"{pair.Key}: {pair.Value}");

            Debug.Log($"[AutoPlayBot] 종료: {reason} ({n}판)");
            UnityEditor.EditorPrefs.SetBool(EnabledPrefKey, false); // 다음 Play는 평소처럼
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
