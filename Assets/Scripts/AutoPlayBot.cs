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
        private const int MaxTurnsPerAttempt = 3000; // 한 번 시도가 이보다 길면 끼인 걸로 보고 그 판 종료

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

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
        }

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

        private static string BuyGacha(ItemSlot slot)
        {
            var name = EquipmentData.Templates[slot].BaseName;
            if (GoldWallet.Gold >= GachaSystem.GetMultiPullCost(slot))
                return GachaSystem.PullMulti(slot) != null ? $"{name}10연차" : null;
            return GachaSystem.Pull(slot) != null ? $"{name}뽑기" : null;
        }

        private static string BuyPotion(PotionType type, string label) =>
            StatPotionWallet.IsUnlocked(type) && StatPotionWallet.TryBuy(type) ? label : null;

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
                EndRun("한 시도에서 턴 제한 초과(길찾기/진행 막힘 의심)", false);
                return false;
            }

            var dir = ChooseDirection();
            _player.BotAct(dir);
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
            _cardPicks[chosen] = _cardPicks.TryGetValue(chosen, out var c) ? c + 1 : 1;
            Detail($"  레벨업 Lv{_player.Levels.Level}: [{string.Join(" / ", options.Select(o => o.Title))}] → {chosen}");
            _hud.ChooseUpgrade(bestIndex);
        }

        private void OnDeath()
        {
            _run.TotalDeaths++;
            _stageDeaths++;

            var bossInfo = string.Empty;
            if (_room.IsBossRoom)
            {
                var boss = _room.Enemies.FirstOrDefault(e => e != null && e.IsBoss);
                if (boss != null)
                    bossInfo = $" | 보스 남은 HP {boss.Stats.CurrentHealth:0}/{boss.Stats.MaxHealth:0} ({boss.Stats.CurrentHealth / boss.Stats.MaxHealth * 100f:0}%)";
            }

            Detail($"[사망 #{_stageDeaths}] {_room.RoomName} ({_attemptTurns}턴){bossInfo} | {StatLine()}");

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

        /// <summary>인접한 적이 있으면 체력이 제일 낮은 적을 때리고, 없으면 BFS로 "적 옆 칸"까지 최단 경로의
        /// 첫 걸음. 경로가 없으면(다른 적에 막힘) 아무 빈 칸으로.</summary>
        private Vector2Int ChooseDirection()
        {
            var map = _room.Map;
            var start = _player.GridPos;

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
                return targetDir;

            var firstStep = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var d in Directions)
            {
                var next = start + d;
                if (!map.IsWalkable(next))
                    continue;
                firstStep[next] = d;
                queue.Enqueue(next);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var d in Directions)
                {
                    if (map.GetActorAt(cur + d) is EnemyActor)
                        return firstStep[cur];
                }
                foreach (var d in Directions)
                {
                    var next = cur + d;
                    if (next == start || firstStep.ContainsKey(next) || !map.IsWalkable(next))
                        continue;
                    firstStep[next] = firstStep[cur];
                    queue.Enqueue(next);
                }
            }

            foreach (var d in Directions)
            {
                if (map.IsWalkable(start + d))
                    return d;
            }
            return Vector2Int.up;
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
            _results.Add(_run);

            Debug.Log($"[AutoPlayBot] {_run.Index}/{RunsPerSession}판 종료 - 사망 {_run.TotalDeaths}회");
            Log($"{_run.Index,3}판 종료: {reason} | {_run.Seconds:0.0}초, {_run.TotalTurns}턴, 사망 {_run.TotalDeaths}회, " +
                $"획득 골드 {_run.GoldEarned}, 최종 Lv{_run.FinalLevel} | 최종 장비: {EquipLine()}");

            _runEnding = true;
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
