#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;
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

        private const int RunsPerSession = 100;

        /// <summary>"층별 순수 난이도 측정" 모드(EditorPrefs ModePrefKey = StageTestMode) - 일반 모드가 남긴 기준 상태
        /// (BotLogs/reference_snapshots.tsv, 층마다 "그 층에 들어갈 때 전체 저장 상태"의 중앙값 판)를 불러와서 각 층만
        /// TrialsPerStage번씩 반복한다. 모든 시도가 같은 상태로 시작하니 이전 층 파밍량과 무관한 그 층 자체의 난이도가 나온다.</summary>
        public const string ModePrefKey = "LoopRogue_AutoPlayBot_Mode";
        public const string StageTestMode = "stage_test";

        /// <summary>"보스 배율 찾기" 모드 - 층마다 그 층 입장 상태(실제 플레이어가 들어오는 상태, 일반 모드가 남긴 기준 파일)로
        /// 시작해서 보스 세기를 SweepScales만큼 바꿔가며 재고, 목표 사망 수(TargetDeaths)에 맞는 배율을 보간해서 새
        /// BossPatternBalance 값을 제안한다. 값을 바꾸면 다음 층들 입장 상태도 바뀌므로 "배율 찾기 → 일반 모드"를 몇 번 반복한다.
        /// (처음엔 한 층 앞 입장 상태로 쟀는데, 실제 게임은 앞 층에서 파밍한 상태로 들어와서 곡선이 목표와 크게 어긋났다.)</summary>
        public const string BossSweepMode = "boss_sweep";
        private static readonly float[] SweepScales = { 0.7f, 0.85f, 1f, 1.2f, 1.45f };
        private const int SweepTrials = 20;

        /// <summary>목표 층별 평균 사망(실제 게임 곡선 = 그 층 입장 상태로 시작) - 사용자가 고른 리듬
        /// "5층·10층이 벽": 2~4층 완만 → 5층 중간 보스 → 6층 숨 돌리기 → 다시 오르다 10층 최종 보스.</summary>
        private static readonly Dictionary<int, float> TargetDeaths = new Dictionary<int, float>
        {
            { 2, 3 }, { 3, 4 }, { 4, 5 }, { 5, 15 }, { 6, 5 }, { 7, 8 }, { 8, 10 }, { 9, 12 }, { 10, 20 },
        };

        /// <summary>GameBootstrap.BossPatternBalance의 현재 값(제안값 계산용 - 바꾸면 여기도 같이).</summary>
        private static readonly Dictionary<int, float> CurrentBossBalance = new Dictionary<int, float>
        {
            { 1, 1.32f }, { 2, 1.24f }, { 3, 0.99f }, { 4, 1.33f }, { 5, 1.27f }, { 6, 0.84f }, { 7, 0.72f }, { 8, 0.82f }, { 9, 0.57f }, { 10, 0.7f },
        };

        private bool _sweep;
        private const int TrialsPerStage = 30;
        private const string ReferenceFileName = "reference_snapshots.tsv";

        private bool _stageTest;
        private int TrialsPerCase => _sweep ? SweepTrials : TrialsPerStage;
        private int SessionRuns => _stageTest ? _testCases.Count * TrialsPerCase : RunsPerSession;

        /// <summary>각 층을 "몇 층 입장 상태로" 시험할지 - 0 = 그 층 입장 상태, -1 = 한 층 앞 입장 상태.
        /// 그 층 입장 상태에는 바로 앞 층에서 죽으며 파밍한 성장이 이미 들어있어서, 앞 층 상태로도 같이 재야
        /// "앞 층 파밍 덕에 쉬운 건지, 원래 쉬운 층인지"를 구분할 수 있다.</summary>
        private static readonly int[] TestPowerOffsets = { -1, 0 };

        // 기준 상태: 층 -> (키 -> 값(int 또는 float))
        private readonly Dictionary<int, Dictionary<string, object>> _reference = new Dictionary<int, Dictionary<string, object>>();
        private readonly List<(int Stage, int Power, float Scale)> _testCases = new List<(int, int, float)>();

        // 일반 모드에서 모은 "층 입장 시 저장 상태"(판, 층, 상태)
        private readonly List<(int Run, int Stage, Dictionary<string, object> Prefs)> _entrySnapshots =
            new List<(int, int, Dictionary<string, object>)>();
        /// <summary>한 프레임에 턴을 몇 개 돌릴지를 개수 대신 시간으로 정한다 - 프레임 자체(에디터 화면 갱신 등)에 드는
        /// 고정 비용을 최대한 많은 턴에 나눠 쓰려는 것. 너무 키우면 에디터가 멈춘 것처럼 보이니 50ms 정도로 둔다.</summary>
        private const double FrameBudgetMs = 50.0;

        /// <summary>사망 시 로비 씬을 실제로 다녀오지 않고 제자리에서 쇼핑 후 방1부터 다시(LobbyTripInPlace).
        /// 로비/씬 전환 자체를 검증하고 싶을 땐 false로.</summary>
        private const bool FastDeathLobby = true;
        private const int MaxDeathsPerStage = 40;    // 이보다 많이 죽으면 "벽"으로 보고 그 판 종료
        private const float MaxRealSeconds = 10800f; // 전체 실행 제한(3시간)

        /// <summary>판 수가 많으면 방/레벨업/로비 같은 상세 로그는 끄고 판 결과와 요약만 남긴다(100판이면 파일이 수십 MB).</summary>
        private static readonly bool DetailedLog = RunsPerSession <= 10; // 층 테스트 모드는 항상 요약만
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
            public int TestStage;            // 층 테스트 모드에서 이 시도가 측정한 층
            public int TestPower;            // 층 테스트 모드 - 몇 층 입장 상태로 시작했는지
            public float TestScale = 1f;     // 보스 배율 찾기 모드 - 보스 세기에 곱한 값
            public int TestBossDeaths;       // 층 테스트 모드 - 그중 보스방 사망
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
            public int Enrages;           // 보스 광폭화 횟수
            public int PatternHits;       // 그중 맞은 횟수
            public int Dodges;            // 봇이 예고 칸에서 피한 횟수
            public int CrossfireDetours;  // 궁수 십자포화를 피하려고 최단 경로와 다른 칸으로 간 횟수
            public int Waits;             // 봇이 대기한 횟수
            public int Spins;             // 회전 베기 사용
            public int DashStrikes;       // 대시로 몹에게 붙어 때린 횟수
            public int DashDodges;        // 한 칸 이동으로는 예고 칸을 못 벗어나 대시로 피한 횟수
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
        private readonly List<float> _hpHistory = new List<float>(); // 최근 몇 턴 체력(사망 직전 체력 기록용)
        private int _turnsSinceProgress; // 적 체력 합이 마지막으로 줄어든 뒤 지난 턴(십자포화 회피 포기 판단용)
        private float _lastEnemyHealthSum;
        private int _stageDeaths;
        private int _stageTurns;
        private int _enteredStage;
        private int _lastGold;
        private int _lastKnownLevel = 1;
        private int _lastEventCount;

        // 속도 측정(요약에 표시)
        private readonly Stopwatch _frameWatch = new Stopwatch();
        private double _simMs;
        private int _simFrames;
        private int _sceneLoads;

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
            var mode = UnityEditor.EditorPrefs.GetString(ModePrefKey, "");
            _sweep = mode == BossSweepMode;
            _stageTest = mode == StageTestMode || _sweep;
            if (_stageTest)
            {
                path = Path.Combine(Path.GetDirectoryName(path), (_sweep ? "bosssweep_" : "stagetest_") + Path.GetFileName(path).Substring(4));
                if (!LoadReference(Path.Combine(dir, ReferenceFileName)))
                {
                    Debug.LogError($"[AutoPlayBot] 기준 상태 파일({ReferenceFileName})이 없습니다 - 일반 모드 봇을 먼저 한 번 돌리세요.");
                    _sessionFinished = true;
                    UnityEditor.EditorPrefs.SetBool(EnabledPrefKey, false);
                    UnityEditor.EditorPrefs.SetString(ModePrefKey, "");
                    UnityEditor.EditorApplication.isPlaying = false;
                    return;
                }
            }
            _log = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            _events = new StreamWriter(Path.ChangeExtension(path, null) + "_events.jsonl", false, new UTF8Encoding(false));
            Debug.Log($"[AutoPlayBot] 시작 ({(_stageTest ? "층별 순수 난이도 측정, " : "")}{SessionRuns}판) - 로그: {path}");

            DamagePopup.Suppressed = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true; // 에디터 창이 포커스를 잃어도 느려지지 않게

            _sessionStartTime = Time.realtimeSinceStartup;
            Log(_stageTest
                ? $"=== {(_sweep ? "보스 배율 찾기" : "층별 순수 난이도 측정")}: {_testCases.Count}조합 × {TrialsPerCase}회 (기준 상태에서 시작, 그 층 보스를 잡으면 종료) ==="
                : $"=== 자동 플레이 {RunsPerSession}판 연속 시작 ===");
            BeginRun();
        }

        private void OnDestroy()
        {
            DamagePopup.Suppressed = false;
            _log?.Dispose();
            _events?.Dispose();
        }

        // ===================== 이벤트 기록(JSONL) =====================
        // 텍스트 로그는 판 수가 많으면 상세 내용을 끄지만, 이 파일엔 모든 판의 모든 이벤트를 한 줄에 하나씩 남긴다(분석용).
        // 모든 줄 공통: ev(종류), run(판), turn(판 누적 턴), stage, try(이 스테이지 시도 번호), room, lv, hp, maxhp, atk, crit, gold.
        // ev 종류: run_start / lobby(구매 내역 한 번에) / room_clear / levelup / event / death / stage_clear / run_end

        private StreamWriter _events;

        private void Ev(string type, params (string Key, object Value)[] fields)
        {
            if (_events == null || _run == null)
                return;
            var sb = new StringBuilder(256);
            sb.Append("{\"ev\":").Append(Json(type));
            sb.Append(",\"run\":").Append(_run.Index).Append(",\"turn\":").Append(_run.TotalTurns);
            sb.Append(",\"stage\":").Append(StageProgress.CurrentStage);
            sb.Append(",\"try\":").Append(StageProgress.AttemptsThisStage);
            if (_room != null)
                sb.Append(",\"room\":").Append(Json(_room.RoomName));
            if (_player != null && _player.Stats != null)
            {
                var st = _player.Stats;
                sb.Append(",\"lv\":").Append(_player.Levels.Level)
                  .Append(",\"hp\":").Append(Json(st.CurrentHealth)).Append(",\"maxhp\":").Append(Json(st.MaxHealth))
                  .Append(",\"atk\":").Append(Json(st.AttackPower)).Append(",\"crit\":").Append(Json(st.CriticalChanceRate));
            }
            sb.Append(",\"gold\":").Append(GoldWallet.Gold);
            foreach (var (key, value) in fields)
                sb.Append(',').Append(Json(key)).Append(':').Append(Json(value));
            sb.Append('}');
            _events.WriteLine(sb.ToString());
        }

        private static string Json(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case float f: return Math.Round(f, 3).ToString(CultureInfo.InvariantCulture);
                case double d: return Math.Round(d, 3).ToString(CultureInfo.InvariantCulture);
                case string str:
                    return "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
                case System.Collections.IDictionary dict:
                {
                    var parts = new List<string>();
                    foreach (System.Collections.DictionaryEntry e in dict)
                        parts.Add(Json(e.Key.ToString()) + ":" + Json(e.Value));
                    return "{" + string.Join(",", parts) + "}";
                }
                case System.Collections.IEnumerable list:
                {
                    var parts = new List<string>();
                    foreach (var item in list)
                        parts.Add(Json(item));
                    return "[" + string.Join(",", parts) + "]";
                }
                default: return Json(value.ToString());
            }
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
            BossBrain.EnrageCount = 0;
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
            Ev("run_start");

            if (_stageTest)
            {
                var testCase = _testCases[Mathf.Min(_results.Count / TrialsPerCase, _testCases.Count - 1)];
                _run.TestStage = testCase.Stage;
                _run.TestPower = testCase.Power;
                _run.TestScale = testCase.Scale;
                GameBootstrap.BossScaleForBotTest = testCase.Scale;
                ApplyPrefs(_reference[testCase.Power], testCase.Stage);
                _enteredStage = StageProgress.CurrentStage;
                _lastGold = GoldWallet.Gold;
                if (_results.Count > 0)
                {
                    _lastScene = null;
                    SceneManager.LoadScene("Main");
                }
                return;
            }

            if (_results.Count > 0)
            {
                _lastScene = null;
                SceneManager.LoadScene("Title");
            }
        }

        // ===================== 층 입장 상태 저장/불러오기 =====================

        private static IEnumerable<(string Key, bool IsFloat)> SaveKeys()
        {
            foreach (var k in new[] { "Gold", "CurrentStage", "StageAttempts", "Run_HasSaved", "Run_Level", "Run_Exp", "Run_ExpToNext",
                         "Potion_AttackCount", "Potion_HealthCount", "Potion_CriticalCount" })
                yield return ("LoopRogue_" + k, false);
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                yield return ($"LoopRogue_Equip_{slot}", false);
                yield return ($"LoopRogue_GachaPulls_{slot}", false);
            }
            foreach (var k in new[] { "AttackPower", "MaxHealth", "CurrentHealth", "CriticalChance", "PermAttack", "PermHealth", "PermCritical",
                         "DamageReduction", "LifeSteal", "Regen", "KillHeal", "ExpBonus", "GoldBonus" })
                yield return ("LoopRogue_Run_" + k, true);
        }

        private static Dictionary<string, object> CapturePrefs()
        {
            PlayerPrefs.Save();
            var d = new Dictionary<string, object>();
            foreach (var (key, isFloat) in SaveKeys())
            {
                if (!PlayerPrefs.HasKey(key))
                    continue;
                d[key] = isFloat ? (object)PlayerPrefs.GetFloat(key) : PlayerPrefs.GetInt(key);
            }
            return d;
        }

        /// <summary>저장을 전부 지우고 기준 상태를 써넣은 뒤 각 저장 클래스가 다시 읽게 한다(SaveReset.ResetAll과 같은 순서).</summary>
        private static void ApplyPrefs(Dictionary<string, object> prefs, int stage)
        {
            PlayerPrefs.DeleteAll();
            foreach (var pair in prefs)
            {
                if (pair.Value is float f)
                    PlayerPrefs.SetFloat(pair.Key, f);
                else
                    PlayerPrefs.SetInt(pair.Key, (int)pair.Value);
            }
            PlayerPrefs.SetInt("LoopRogue_StageAttempts", 0);
            PlayerPrefs.SetInt("LoopRogue_CurrentStage", stage); // 한 층 앞 상태로 시험할 땐 층만 바꿔 끼운다
            PlayerPrefs.Save();
            GoldWallet.Reload();
            EquipmentWallet.Reload();
            StatPotionWallet.Reload();
            StageProgress.Reload();
            GachaSystem.Reload();
            RunProgress.Reload();
        }

        private static float Stat(Dictionary<string, object> prefs, string key) =>
            prefs.TryGetValue("LoopRogue_Run_" + key, out var v) ? (float)v : 0f;

        /// <summary>일반 모드 종료 시 - 층마다 입장 상태들 중 전투력(공격력 × 최대체력) 중앙값인 판 하나를 기준으로 저장.</summary>
        private void WriteReference()
        {
            var lines = new List<string>();
            foreach (var group in _entrySnapshots.GroupBy(e => e.Stage).OrderBy(g => g.Key))
            {
                var sorted = group.ToList();
                var byAtk = sorted.OrderBy(e => Stat(e.Prefs, "AttackPower")).Select(e => e.Run).ToList();
                var byHp = sorted.OrderBy(e => Stat(e.Prefs, "MaxHealth")).Select(e => e.Run).ToList();
                var mid = sorted.Count / 2;
                var pick = sorted.OrderBy(e => Math.Abs(byAtk.IndexOf(e.Run) - mid) + Math.Abs(byHp.IndexOf(e.Run) - mid)).First();
                if (pick.Prefs.Count == 0)
                    lines.Add($"{group.Key}\t-\t-\t-"); // 1층 = 완전히 새로 시작한 상태(저장된 키가 하나도 없음)
                foreach (var pair in pick.Prefs)
                    lines.Add($"{group.Key}\t{pair.Key}\t{(pair.Value is float ? "f" : "i")}\t{Convert.ToString(pair.Value, CultureInfo.InvariantCulture)}");
                Log($"기준 상태 {group.Key}층: {pick.Run}판 입장 시점(입장 {sorted.Count}판 중 공격력·최대HP 순위가 둘 다 중앙에 가장 가까운 판) - " +
                    $"Lv{PrefText(pick.Prefs, "LoopRogue_Run_Level")} 공격력 {PrefText(pick.Prefs, "LoopRogue_Run_AttackPower")} " +
                    $"최대HP {PrefText(pick.Prefs, "LoopRogue_Run_MaxHealth")}");
            }
            var dir = Path.Combine(Application.dataPath, "..", "BotLogs");
            File.WriteAllLines(Path.Combine(dir, ReferenceFileName), lines, new UTF8Encoding(false));
        }

        private static string PrefText(Dictionary<string, object> prefs, string key) =>
            prefs.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : "-";

        private bool LoadReference(string file)
        {
            if (!File.Exists(file))
                return false;
            foreach (var line in File.ReadAllLines(file))
            {
                var parts = line.Split('\t');
                if (parts.Length != 4)
                    continue;
                var stage = int.Parse(parts[0], CultureInfo.InvariantCulture);
                if (!_reference.TryGetValue(stage, out var d))
                    _reference[stage] = d = new Dictionary<string, object>();
                if (parts[1] == "-")
                    continue;
                d[parts[1]] = parts[2] == "f"
                    ? (object)float.Parse(parts[3], CultureInfo.InvariantCulture)
                    : int.Parse(parts[3], CultureInfo.InvariantCulture);
            }
            foreach (var stage in _reference.Keys.OrderBy(k => k))
            {
                if (_sweep)
                {
                    if (!TargetDeaths.ContainsKey(stage))
                        continue;
                    foreach (var scale in SweepScales)
                        _testCases.Add((stage, stage, scale));
                    continue;
                }
                foreach (var offset in TestPowerOffsets)
                {
                    if (_reference.ContainsKey(stage + offset))
                        _testCases.Add((stage, stage + offset, 1f));
                }
            }
            return _testCases.Count > 0;
        }

        private void Update()
        {
            if (_sessionFinished)
                return;

            if (_runEnding)
            {
                _runEnding = false;
                if (_results.Count >= SessionRuns)
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
                _sceneLoads++;
                DisableRendering();
            }

            switch (scene)
            {
                case "Title":
                    if (!_sceneHandled)
                    {
                        _sceneHandled = true;
                        SceneManager.LoadScene(_stageTest ? "Main" : "Lobby");
                    }
                    break;
                case "Lobby":
                    if (!_sceneHandled)
                    {
                        _sceneHandled = true;
                        DoShopping();
                        // 사망은 제자리 쇼핑(LobbyTripInPlace)이라 실제 로비는 판 시작/스테이지 클리어 직후 = 새 층 입장 때뿐.
                        if (!_stageTest)
                        {
                            var prefs = CapturePrefs();
                            _entrySnapshots.Add((_run.Index, StageProgress.CurrentStage, prefs));
                            Ev("stage_enter", ("prefs", prefs));
                        }
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

        // 이번 로비 방문의 구매 기록(이벤트 로그용) - BuyGacha/BuyPotion이 채운다.
        private readonly List<Dictionary<string, object>> _purchases = new List<Dictionary<string, object>>();

        private void DoShopping()
        {
            var goldBefore = GoldWallet.Gold;
            var bought = new List<string>();
            _purchases.Clear();

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

            var equip = new Dictionary<string, object>();
            var shopLv = new Dictionary<string, object>();
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var g = EquipmentWallet.GetEquipped(slot);
                equip[slot.ToString()] = g.HasValue ? g.Value.ToString() : null;
                shopLv[slot.ToString()] = GachaSystem.GetShopLevel(slot);
            }
            Ev("lobby", ("gold_before", goldBefore), ("spent", goldBefore - GoldWallet.Gold), ("buys", _purchases.ToList()),
                ("equip", equip), ("shop_lv", shopLv),
                ("potions", new Dictionary<string, object>
                {
                    { "Attack", StatPotionWallet.GetCount(PotionType.Attack) },
                    { "Health", StatPotionWallet.GetCount(PotionType.Health) },
                    { "Critical", StatPotionWallet.GetCount(PotionType.Critical) },
                }));
        }

        private void RecordPull(string kind, ItemSlot slot, int cost, IEnumerable<GachaResult> results)
        {
            var list = results.ToList();
            _purchases.Add(new Dictionary<string, object>
            {
                { "kind", kind }, { "slot", slot.ToString() }, { "cost", cost },
                { "grades", list.Select(r => r.Grade.ToString()).ToList() },
                { "equipped", list.Where(r => r.Equipped).Select(r => r.Grade.ToString()).ToList() },
            });
        }

        private string BuyGacha(ItemSlot slot)
        {
            var name = EquipmentData.Templates[slot].BaseName;
            var multiCost = GachaSystem.GetMultiPullCost(slot);
            if (GoldWallet.Gold >= multiCost)
            {
                var multi = GachaSystem.PullMulti(slot);
                if (multi == null)
                    return null;
                RecordPull("multi", slot, multiCost, multi);
                _run.MultiPulls[(int)slot]++;
                _run.GoldSpentGacha += multiCost;
                return $"{name}10연차";
            }

            var cost = GachaSystem.GetPullCost(slot);
            var single = GachaSystem.Pull(slot);
            if (single == null)
                return null;
            RecordPull("single", slot, cost, new[] { single.Value });
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
            _purchases.Add(new Dictionary<string, object>
            {
                { "kind", "potion" }, { "type", type.ToString() }, { "cost", cost }, { "count", StatPotionWallet.GetCount(type) },
            });
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

            _frameWatch.Restart();
            while (_frameWatch.Elapsed.TotalMilliseconds < FrameBudgetMs)
            {
                if (!StepMain())
                    break;
            }
            _simMs += _frameWatch.Elapsed.TotalMilliseconds;
            _simFrames++;
        }

        /// <summary>봇은 화면을 안 보니 카메라와 UI 그리기를 끈다(게임 로직/HUD 상태는 GameObject 활성 여부로 돌아가서 영향 없음).
        /// 씬이 바뀔 때마다 새로 만들어지므로 매번 다시 끈다.</summary>
        private static void DisableRendering()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                cam.enabled = false;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                canvas.enabled = false;
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
                {
                    Detail($"  방 클리어: {_lastRoomName} ({_roomTurns}턴, HP {_player.Stats.CurrentHealth:0}/{_player.Stats.MaxHealth:0}, Lv{_player.Levels.Level})");
                    Ev("room_clear", ("cleared", _lastRoomName), ("room_turns", _roomTurns));
                }
                _lastRoomName = _room.RoomName;
                _roomTurns = 0;
                _turnsSinceProgress = 0;
                _lastEnemyHealthSum = float.MaxValue;
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

            var enemyHealthSum = _room.Enemies.Where(e => e != null && !e.Stats.IsDead).Sum(e => e.Stats.CurrentHealth);
            if (enemyHealthSum < _lastEnemyHealthSum)
                _turnsSinceProgress = 0;
            else
                _turnsSinceProgress++;
            _lastEnemyHealthSum = enemyHealthSum;

            var decisionPos = _player.GridPos;
            if (!TryUseSkill())
            {
                var dir = ChooseDirection();
                if (dir.HasValue)
                    _player.BotAct(dir.Value);
                else
                {
                    _player.BotWait(); // 움직일 수 있는 칸이 전부 예고 칸이면 제자리 대기(저격 2발 등)
                    _run.Waits++;
                }
            }
            _recentDecisions.Enqueue($"{_lastDecision}@({decisionPos.x},{decisionPos.y})");
            while (_recentDecisions.Count > 16)
                _recentDecisions.Dequeue();
            if (RoomController.EventTriggeredCount != _lastEventCount)
            {
                _lastEventCount = RoomController.EventTriggeredCount;
                _run.Events[(int)RoomController.LastEventType]++;
                Detail($"  이벤트: {EventNames[(int)RoomController.LastEventType]} ({_room.RoomName})");
                Ev("event", ("type", RoomController.LastEventType.ToString()));
            }
            RememberMap();
            _hpHistory.Add(_player.Stats.CurrentHealth);
            if (_hpHistory.Count > 3)
                _hpHistory.RemoveAt(0);
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
            Ev("levelup", ("options", options.Select(o => o.Title).ToList()), ("chosen", chosen));
            _hud.ChooseUpgrade(bestIndex);
        }

        private void OnDeath()
        {
            _run.TotalDeaths++;
            _stageDeaths++;
            _run.GoldLostDeath += _hud.LastDeathGoldPenalty;

            var bossInfo = string.Empty;
            float? bossHp = null, bossMaxHp = null, bossAtk = null;
            if (_room.IsBossRoom)
            {
                var boss = _room.Enemies.FirstOrDefault(e => e != null && e.IsBoss);
                if (boss != null)
                {
                    bossInfo = $" | 보스 남은 HP {boss.Stats.CurrentHealth:0}/{boss.Stats.MaxHealth:0} ({boss.Stats.CurrentHealth / boss.Stats.MaxHealth * 100f:0}%)";
                    bossHp = boss.Stats.CurrentHealth;
                    bossMaxHp = boss.Stats.MaxHealth;
                    bossAtk = boss.Stats.AttackPower;
                }
            }
            var alive = _room.Enemies.Where(e => e != null && !e.Stats.IsDead).ToList();
            var prevHp = _hpHistory.Count >= 2 ? _hpHistory[_hpHistory.Count - 2] : (float?)null;
            if (_room.IsBossRoom)
                _run.TestBossDeaths++;
            Ev("death", ("stage_death", _stageDeaths), ("attempt_turns", _attemptTurns), ("room_turns", _roomTurns),
                ("boss_room", _room.IsBossRoom), ("boss_hp", bossHp), ("boss_maxhp", bossMaxHp), ("boss_atk", bossAtk),
                ("hp_prev_turn", prevHp), ("penalty", _hud.LastDeathGoldPenalty),
                ("alive_melee", alive.Count(e => !e.IsBoss && !e.IsMinion && e.Kind == EnemyKind.Melee)),
                ("alive_ranged", alive.Count(e => e.Kind == EnemyKind.Ranged)),
                ("alive_minion", alive.Count(e => e.IsMinion)));

            Detail($"[사망 #{_stageDeaths}] {_room.RoomName} ({_attemptTurns}턴){bossInfo} | 데스 패널티 골드 -{_hud.LastDeathGoldPenalty} | {StatLine()}");
            LogDeathMaps();

            if (_stageDeaths >= MaxDeathsPerStage)
            {
                EndRun($"스테이지 {_room.Stage}에서 {MaxDeathsPerStage}번 사망 - 넘기 힘든 벽", false);
                return;
            }

            // 죽으면 항상 로비로 - 어차피 방1부터 다시라서, 번 골드를 바로 쓰는 게 이득.
            if (FastDeathLobby)
                LobbyTripInPlace();
            else
                _hud.ChooseDeathLobby();
        }

        /// <summary>사망 → 로비 → Main을 씬 로드 없이 흉내 낸다. 로비에서 하는 일(DoShopping)은 전부 정적 지갑만 건드리고,
        /// Main이 다시 뜰 때 PlayerActor.Initialize가 하는 일은 "스냅샷 + 그 사이 새로 산 영구 보너스 차이 더하기"라서,
        /// 그 차이를 지금 플레이어에 직접 더하고 "계속하기"(방1 재시작)를 고르면 결과가 같다.
        /// 씬 로드 한 번에 약 0.15초라(봇 30판 기준 전체 시간의 90% 이상) 이게 제일 큰 속도 개선이다.</summary>
        private void LobbyTripInPlace()
        {
            var attackBefore = RunProgress.CurrentPermanentAttack();
            var healthBefore = RunProgress.CurrentPermanentHealth();
            var criticalBefore = RunProgress.CurrentPermanentCritical();

            DoShopping();

            var stats = _player.Stats;
            stats.FixedAttack += RunProgress.CurrentPermanentAttack() - attackBefore;
            stats.FixedMaxHealth += RunProgress.CurrentPermanentHealth() - healthBefore;
            stats.CriticalChanceRate += RunProgress.CurrentPermanentCritical() - criticalBefore;

            _hud.ChooseDeathContinue(); // FullHeal + 방1 다시 로드 + 새 시도 시작(BeginAttempt/이벤트 방 다시 뽑기)
            _attemptTurns = 0;
            _lastRoomName = null; // 방1에서 죽었어도 새 방으로 취급(맵 기록/판단 기록 초기화)
        }

        private void OnStageClear()
        {
            var stage = _room.Stage;
            var idx = stage - 1;
            _run.StageDeaths[idx] = _stageDeaths;
            _run.StageTurns[idx] = _stageTurns;
            _run.StageClearLevel[idx] = _player.Levels.Level;

            Ev("stage_clear", ("cleared_stage", stage), ("stage_deaths", _stageDeaths), ("stage_turns", _stageTurns),
                ("boss_turns", _roomTurns));
            Detail($"[보스 격파] 스테이지 {stage,2}: 사망 {_stageDeaths,2}회, {_stageTurns,5}턴, 클리어 시 Lv{_player.Levels.Level}, " +
                $"ATK {_player.Stats.AttackPower:0}, 최대HP {_player.Stats.MaxHealth:0}, 보유골드 {GoldWallet.Gold}");

            if (_stageTest)
            {
                EndRun($"{stage}층 보스 격파", true);
                return;
            }

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
        /// <summary>스킬 사용 규칙(단순하게): 1) 예고 칸 위인데 한 칸 이동으로 안전한 칸이 없으면 대시로 탈출
        /// 2) 주변 8칸에 몹 2마리 이상이면 회전 베기 3) 붙어 있는 몹이 없고 대시 경로 끝에 몹이 있으면(착지 칸이 안전할 때) 대시로 붙어서 한 대.
        /// 썼으면 true(이번 턴 행동 끝).</summary>
        private bool TryUseSkill()
        {
            var map = _room.Map;
            var start = _player.GridPos;
            var danger = _room.DangerTiles;

            if (danger.Contains(start))
            {
                var canStepOut = Directions.Any(d => map.IsWalkable(start + d) && !danger.Contains(start + d));
                if (canStepOut || _player.DashCooldown > 0)
                    return false; // 평소처럼 한 칸 회피(ChooseDirection)
                foreach (var d in Directions)
                {
                    if (_player.PreviewDash(d, out var landing, out _) && landing != start && !danger.Contains(landing) && _player.BotDash(d))
                    {
                        _run.DashDodges++;
                        _lastDecision = "대시 회피";
                        return true;
                    }
                }
                return false;
            }

            if (_player.SpinCooldown == 0 && _player.CountSpinTargets() >= 2 && _player.BotSpin())
            {
                _run.Spins++;
                _lastDecision = "회전 베기";
                return true;
            }

            if (_player.DashCooldown == 0 && !Directions.Any(d => map.GetActorAt(start + d) is EnemyActor))
            {
                foreach (var d in Directions)
                {
                    if (_player.PreviewDash(d, out var landing, out var hit) && hit != null && landing != start &&
                        !danger.Contains(landing) && _player.BotDash(d))
                    {
                        _run.DashStrikes++;
                        _lastDecision = "대시 공격";
                        return true;
                    }
                }
            }
            return false;
        }

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
            Func<Vector2Int, bool> isEnemyAdjacent = c => Directions.Any(d => map.GetActorAt(c + d) is EnemyActor);
            Func<Vector2Int, bool> passable = c => (map.IsWalkable(c) || c == eventCell) && !danger.Contains(c);

            // 궁수가 2마리 이상 살아 있으면 "여러 궁수가 동시에 쏠 수 있는 칸"을 비싸게 쳐서 돌아간다(한 줄로 선 궁수
            // 둘을 정면으로 걸어가다 3턴 연속 두 발씩 맞고 죽던 문제 - 7층/10층 일반 방 사망의 대부분). 궁수 1마리가
            // 쏘는 칸은 추가 비용 없음 - 1:1이면 맞더라도 그냥 다가가서 때린다(안 그러면 도망 다니는 궁수를 못 잡고 턴 제한에 걸린다).
            // 비용이지 금지가 아니라서 돌아갈 길이 없으면 맞으면서라도 간다.
            // 단, 두 경우엔 우회를 포기하고 맞으면서 돌진한다(100판 테스트에서 붙어 다니는 궁수 둘 앞에서 매 턴 경로가 바뀌어
            // 제자리 왕복하다 93판이 턴 제한에 걸렸다): 1) 궁수들이 한 턴에 주는 피해로 CrossfireDangerTurns턴을 맞아도 안 죽을
            // 만큼 체력이 넉넉할 때 2) 적 체력이 CrossfireGiveUpTurns턴 동안 전혀 안 줄었을 때(진행이 막힘).
            var archers = _room.Enemies.Where(e => e != null && !e.Stats.IsDead && e.Kind == EnemyKind.Ranged).ToList();
            var archerVolley = archers.Select(a => a.Stats.AttackPower).OrderByDescending(a => a).Take(2).Sum();
            var crossfireDangerous = _player.Stats.CurrentHealth < archerVolley * CrossfireDangerTurns;
            Vector2Int? toEnemy = null;
            if (archers.Count >= 2 && crossfireDangerous && _turnsSinceProgress < CrossfireGiveUpTurns)
            {
                toEnemy = CheapestFirstStepTo(isEnemyAdjacent, passable, c =>
                {
                    var shooters = archers.Count(a => ArcherCanShoot(map, a.GridPos, c));
                    return shooters >= 2 ? CrossfireStepCost * shooters : 0;
                });
                if (toEnemy.HasValue)
                {
                    _lastDecision = "적에게(십자포화 고려)";
                    if (toEnemy != FirstStepTo(isEnemyAdjacent, passable))
                    {
                        _run.CrossfireDetours++;
                        _lastDecision = "적에게(십자포화 우회)";
                    }
                }
            }
            if (!toEnemy.HasValue)
            {
                toEnemy = FirstStepTo(isEnemyAdjacent, passable);
                _lastDecision = "적에게";
            }
            if (toEnemy.HasValue)
                return toEnemy.Value;

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

        /// <summary>궁수 여러 마리가 동시에 쏠 수 있는 칸을 지날 때 한 칸당 추가 비용(쏠 수 있는 궁수 수만큼 곱함).
        /// 한 칸 우회가 1이니, 4면 "두 발 맞는 칸 하나 = 8칸 돌아가기"만큼 피한다.</summary>
        private const int CrossfireStepCost = 4;

        /// <summary>궁수 두 마리의 한 턴 피해 × 이 턴 수보다 체력이 많으면 십자포화를 무시하고 돌진한다.</summary>
        private const int CrossfireDangerTurns = 4;

        /// <summary>적 체력 합이 이 턴 수 동안 안 줄면 십자포화 회피를 포기하고 돌진한다(제자리 왕복 방지).</summary>
        private const int CrossfireGiveUpTurns = 12;

        /// <summary>EnemyActor.CanShoot과 같은 규칙 - 같은 줄, 거리 2~RangedAttackRange, 사이에 벽 없음(몹은 통과).</summary>
        private static bool ArcherCanShoot(GridMap map, Vector2Int from, Vector2Int target)
        {
            var diff = target - from;
            if (diff.x != 0 && diff.y != 0)
                return false;
            var dist = Mathf.Abs(diff.x) + Mathf.Abs(diff.y);
            if (dist < 2 || dist > EnemyActor.RangedAttackRange)
                return false;
            var step = new Vector2Int(Math.Sign(diff.x), Math.Sign(diff.y));
            for (var p = from + step; p != target; p += step)
            {
                if (map.IsWall(p))
                    return false;
            }
            return true;
        }

        /// <summary>FirstStepTo의 가중치 버전(다익스트라) - 칸을 밟을 때마다 1 + extraCost(칸). 방이 최대 14x14라
        /// 우선순위 큐 없이 매번 최소값을 선형 탐색해도 충분히 빠르다.</summary>
        private Vector2Int? CheapestFirstStepTo(Func<Vector2Int, bool> isGoal, Func<Vector2Int, bool> passable,
            Func<Vector2Int, int> extraCost)
        {
            var start = _player.GridPos;
            var cost = new Dictionary<Vector2Int, int>();
            var firstStep = new Dictionary<Vector2Int, Vector2Int>();
            var done = new HashSet<Vector2Int>();
            foreach (var d in Directions)
            {
                var next = start + d;
                if (!passable(next))
                    continue;
                cost[next] = 1 + extraCost(next);
                firstStep[next] = d;
            }

            while (true)
            {
                var found = false;
                var cur = default(Vector2Int);
                var best = int.MaxValue;
                foreach (var pair in cost)
                {
                    if (pair.Value < best && !done.Contains(pair.Key))
                    {
                        best = pair.Value;
                        cur = pair.Key;
                        found = true;
                    }
                }
                if (!found)
                    return null;
                if (isGoal(cur))
                    return firstStep[cur];
                done.Add(cur);

                foreach (var d in Directions)
                {
                    var next = cur + d;
                    if (next == start || done.Contains(next) || !passable(next))
                        continue;
                    var c = best + 1 + extraCost(next);
                    if (!cost.TryGetValue(next, out var old) || c < old)
                    {
                        cost[next] = c;
                        firstStep[next] = firstStep[cur];
                    }
                }
            }
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
            Ev("run_end", ("reason", reason), ("completed", completed), ("deaths", _run.TotalDeaths),
                ("gold_earned", _run.GoldEarned), ("seconds", _run.Seconds));
            _events?.Flush();

            Debug.Log($"[AutoPlayBot] {_run.Index}/{SessionRuns}판 종료 - 사망 {_run.TotalDeaths}회");
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
            r.Enrages = BossBrain.EnrageCount;
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

        private void LogStageTestSummary()
        {
            Log("층 | 시작 상태 | 보스 배율 | 시도 | 클리어 | 사망 평균 | 중앙값 | 상위10% | 무사망 클리어 | 보스방 사망 비율 | 턴 평균");
            foreach (var group in _results.GroupBy(r => (r.TestStage, r.TestPower, r.TestScale))
                         .OrderBy(g => g.Key.TestStage).ThenBy(g => g.Key.TestPower).ThenBy(g => g.Key.TestScale))
            {
                var list = group.ToList();
                var deaths = list.Select(r => (float)r.TotalDeaths).ToList();
                var total = list.Sum(r => r.TotalDeaths);
                Log($"{group.Key.TestStage,2} | {group.Key.TestPower,2}층 입장 | ×{group.Key.TestScale:0.00} | {list.Count,3} | {list.Count(r => r.Completed),3} | {deaths.Average(),6:0.0} | {Percentile(deaths, 0.5f),4:0} | " +
                    $"{Percentile(deaths, 0.9f),4:0} | {list.Count(r => r.Completed && r.TotalDeaths == 0),3} | " +
                    $"{(total > 0 ? list.Sum(r => r.TestBossDeaths) * 100f / total : 0f),4:0}% | {list.Average(r => r.TotalTurns),6:0}");
            }
        }

        private void LogSweepRecommendation()
        {
            Log("");
            Log("--- 목표 사망 수에 맞는 보스 배율 (BossPatternBalance 제안값) ---");
            Log("층 | 목표 사망 | 측정(배율:사망) | 찾은 배율 | 현재 값 → 제안 값");
            foreach (var group in _results.GroupBy(r => r.TestStage).OrderBy(g => g.Key))
            {
                var stage = group.Key;
                var points = group.GroupBy(r => r.TestScale).OrderBy(g => g.Key)
                    .Select(g => (Scale: g.Key, Deaths: (float)g.Average(r => r.TotalDeaths))).ToList();
                var target = TargetDeaths[stage];
                float found;
                if (target <= points[0].Deaths)
                    found = points[0].Scale;           // 가장 약하게 해도 목표보다 많이 죽음(범위 밖)
                else if (target >= points[points.Count - 1].Deaths)
                    found = points[points.Count - 1].Scale; // 가장 세게 해도 목표보다 적게 죽음(범위 밖)
                else
                {
                    found = points[0].Scale;
                    for (var i = 0; i < points.Count - 1; i++)
                    {
                        var (s0, d0) = points[i];
                        var (s1, d1) = points[i + 1];
                        if (target < Mathf.Min(d0, d1) || target > Mathf.Max(d0, d1) || Mathf.Approximately(d0, d1))
                            continue;
                        var t = (target - d0) / (d1 - d0);
                        found = Mathf.Exp(Mathf.Lerp(Mathf.Log(s0), Mathf.Log(s1), t));
                        break;
                    }
                }
                var edge = found <= points[0].Scale || found >= points[points.Count - 1].Scale ? " (측정 범위 끝 - 범위 넓혀 재측정 필요)" : "";
                var current = CurrentBossBalance[stage];
                Log($"{stage,2} | {target,4:0} | {string.Join(" ", points.Select(p => $"{p.Scale:0.00}:{p.Deaths:0.0}"))} | ×{found:0.00} | " +
                    $"{current:0.00} → {current * found:0.00}{edge}");
            }
        }

        private void FinishSession(string reason)
        {
            if (_sessionFinished)
                return;
            _sessionFinished = true;

            var n = _results.Count;
            Log("");
            Log($"================ 종료: {reason} ({n}판) ================");
            if (_stageTest)
            {
                LogStageTestSummary();
                if (_sweep)
                    LogSweepRecommendation();
                GameBootstrap.BossScaleForBotTest = 1f;
                _events?.Flush();
                Debug.Log($"[AutoPlayBot] 층별 순수 난이도 측정 종료 ({n}회)");
                UnityEditor.EditorPrefs.SetBool(EnabledPrefKey, false);
                UnityEditor.EditorPrefs.SetString(ModePrefKey, "");
                UnityEditor.EditorApplication.isPlaying = false;
                return;
            }
            WriteReference();
            var totalSec = Time.realtimeSinceStartup - _sessionStartTime;
            var totalTurns = _results.Sum(r => r.TotalTurns);
            Log($"속도: 전체 {totalSec:0}초 | 초당 {totalTurns / Mathf.Max(1f, totalSec):0}턴 | 턴 처리에 쓴 시간 {_simMs / 1000.0:0}초({_simMs / 10.0 / Mathf.Max(1f, totalSec):0}%) | " +
                $"프레임 {_simFrames}개(프레임당 {totalTurns / (float)Mathf.Max(1, _simFrames):0}턴) | 씬 로드 {_sceneLoads}회");

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
                Log($"보스 예고 공격: 판당 {_results.Average(r => r.PatternResolves):0}회 발동, 적중률 {(resolves > 0 ? hits * 100f / resolves : 0f):0}% | 광폭화 판당 {_results.Average(r => r.Enrages):0}회");
                Log($"궁수 십자포화 우회: 판당 {_results.Average(r => r.CrossfireDetours):0}회");
                Log($"스킬(판당): 회전 베기 {_results.Average(r => r.Spins):0}회 / 대시 공격 {_results.Average(r => r.DashStrikes):0}회 / 대시 회피 {_results.Average(r => r.DashDodges):0.0}회");
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
