using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoopRogue
{
    /// <summary>테스트 배포용 상세 플레이 기록 - 실행할 때마다 exe 옆 PlayLogs/PlayLog_날짜_시각.txt 하나(에디터에선 프로젝트 폴더,
    /// 못 쓰면 persistentDataPath). 방 입장·클리어, 사망(누구에게 몇 대 맞고 죽었는지·직전 메시지·남은 몹·보스 체력),
    /// 사망 뒤 선택, 레벨업 카드, 유물, 아이템 사용, 퍼즐·이벤트 결과, 로비 구매(가챠·영약·슬롯·의뢰), 업적·도감을 시각과 함께 적고,
    /// 끝낼 때 이번 실행 요약(층별 일반 방/보스방 사망, 층에서 보낸 시간, 행동 횟수)을 붙인다. 한 줄마다 바로 파일에 써서
    /// 강제 종료돼도 거기까지는 남는다. 받는 쪽에서 읽기 쉽게 한국어 고정. 자동 플레이 봇이 도는 동안은 안 적는다(봇은 BotLogs).</summary>
    public static class PlayLog
    {
        public const string FolderName = "PlayLogs";

        private static StreamWriter _writer;
        private static bool _failed;
        private static DateTime _sessionStart;
        private static string _filePath;

        private static bool Enabled => !GameHUD.AutoPlayActive && !_failed;

        /// <summary>기록 폴더 - exe 옆(에디터는 프로젝트 폴더). 폴더 열기 버튼용.</summary>
        public static string Folder => _filePath != null ? Path.GetDirectoryName(_filePath) : PreferredFolder;

        private static string PreferredFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", FolderName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _writer = null;
            _failed = false;
            _filePath = null;
            _room = null;
            _stageDeaths.Clear();
            _stageEnterTime.Clear();
            _stageTime.Clear();
            _totals.Clear();
            _itemTotals.Clear();
            _killers.Clear();
            _recentMessages.Clear();
            _recentHits.Clear();
            _roomsCleared = _puzzleOk = _puzzleFail = _bossKills = _deaths = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            _sessionStart = DateTime.Now;
            var go = new GameObject("PlayLog");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
            SceneManager.sceneLoaded += (scene, _) => Write($"=== 장면: {SceneLabel(scene.name)} ===");
            Achievements.Unlocked += def => Write($"업적 달성: {def.Name}");
            Codex.Discovered += entry => Write($"도감 발견: [{entry.Category}] {entry.Name}");
            PlayerActor.EnemyKilled += _ => { if (_room != null) _room.Kills++; };
            Write($"=== 장면: {SceneLabel(SceneManager.GetActiveScene().name)} ===");
        }

        private static string SceneLabel(string name) => name switch
        {
            "Title" => "타이틀",
            "Lobby" => "로비",
            "Main" => "던전",
            _ => name,
        };

        // ---- 파일 ----
        private static bool EnsureWriter()
        {
            if (_writer != null)
                return true;
            if (_failed)
                return false;
            foreach (var folder in new[] { PreferredFolder, Path.Combine(Application.persistentDataPath, FolderName) })
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, $"PlayLog_{_sessionStart:yyyyMMdd_HHmmss}.txt");
                    _writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
                    _filePath = path;
                    _writer.WriteLine($"LoopRogue 플레이 기록 - 빌드 {Playtest.BuildLabel} / 시작 {_sessionStart:yyyy-MM-dd HH:mm:ss}");
                    _writer.WriteLine($"언어 {Loc.Current} / 화면 {Screen.width}x{Screen.height} / OS {SystemInfo.operatingSystem}");
                    _writer.WriteLine($"시작 시점 기록: {Playtest.RecordLine()}");
                    _writer.WriteLine(new string('-', 60));
                    return true;
                }
                catch (Exception)
                {
                    // 다음 후보 폴더로
                }
            }
            _failed = true; // 기록은 부가 기능 - 못 쓰면 조용히 끈다
            return false;
        }

        private static void Write(string line)
        {
            if (!Enabled || !EnsureWriter())
                return;
            try
            {
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            }
            catch (Exception)
            {
                _failed = true;
            }
        }

        private static readonly Regex RichTag = new Regex("<[^>]+>");
        private static string Plain(string text) => string.IsNullOrEmpty(text) ? string.Empty : RichTag.Replace(text, string.Empty).Replace("\n", " / ").Trim();

        private static string Pct(float value, float max) => max <= 0f ? "-" : $"{value / max * 100f:0}%";

        private static string StatLine(PlayerActor player)
        {
            if (player == null || player.Stats == null)
                return string.Empty;
            var st = player.Stats;
            return $"Lv{player.Levels.Level} HP {st.CurrentHealth:0}/{st.MaxHealth:0} ATK {st.AttackPower:0} 치명 {st.EffectiveCriticalChance * 100f:0}% 골드 {GoldWallet.Gold}";
        }

        // ---- 방 단위 집계 ----
        private sealed class RoomVisit
        {
            public string Where;
            public bool IsBoss;
            public float StartTime;
            public int Turns;
            public int Kills;
            public float DamageTaken;
            public readonly Dictionary<string, int> Acts = new Dictionary<string, int>();
        }

        private static RoomVisit _room;
        private static int _attemptTurns;
        private static string _where = "-";

        private static readonly Dictionary<int, (int Normal, int Boss)> _stageDeaths = new Dictionary<int, (int, int)>();
        private static readonly Dictionary<int, float> _stageEnterTime = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _stageTime = new Dictionary<int, float>();
        private static readonly Dictionary<string, int> _totals = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _itemTotals = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _killers = new Dictionary<string, int>();
        private static readonly Queue<string> _recentMessages = new Queue<string>();
        private static readonly Queue<string> _recentHits = new Queue<string>();
        private static int _roomsCleared, _puzzleOk, _puzzleFail, _bossKills, _deaths;

        private static void Bump(Dictionary<string, int> dict, string key, int by = 1) =>
            dict[key] = dict.TryGetValue(key, out var v) ? v + by : by;

        private static string ActsLine(Dictionary<string, int> acts) =>
            acts.Count == 0 ? "행동 없음" : string.Join(" ", acts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}{kv.Value}"));

        /// <summary>던전 시도 시작(Main 입장 또는 사망 뒤 계속하기).</summary>
        public static void AttemptStarted(int stage, PlayerActor player, string how)
        {
            if (!Enabled)
                return;
            _attemptTurns = 0;
            if (!_stageEnterTime.ContainsKey(stage))
                _stageEnterTime[stage] = Time.unscaledTime;
            var curses = string.Join(",", Curses.ActiveDeals.Select(c => c.Name));
            Write($"--- {stage}층 시도 {StageProgress.AttemptsThisStage}번째 시작 ({how}) | {StatLine(player)} | " +
                  $"장비 {Grade(ItemSlot.Weapon)}/{Grade(ItemSlot.Armor)}/{Grade(ItemSlot.Accessory)} | 유물 {RelicList()}" +
                  (curses.Length > 0 ? $" | 저주 {curses}" : string.Empty));
        }

        private static string Grade(ItemSlot slot)
        {
            var grade = EquipmentWallet.GetEquipped(slot);
            return grade.HasValue ? EquipmentData.Grades[grade.Value].Name + EquipmentEnhance.Suffix(slot) : "-";
        }

        private static string RelicList()
        {
            var owned = Relics.OwnedRelics().Select(Relics.Name).ToList();
            return owned.Count == 0 ? "없음" : string.Join(",", owned);
        }

        /// <summary>시작 방 고르기 창 뒤에 깔아 두는 자리 표시용 방 - 그동안은 입장으로 안 적는다(LoopManager).</summary>
        public static bool SkipNextRoomEntry;

        /// <summary>방에 들어갔다 - kind는 "전투"/"보스"/"퍼즐: 얼음" 처럼.</summary>
        public static void RoomEntered(int stage, RoomDefinition def, string kind, PlayerActor player)
        {
            if (SkipNextRoomEntry || !Enabled)
                return;
            _where = def == null ? $"{stage}층" : def.IsBossRoom ? $"{stage}층 보스방" : $"{stage}층 방{def.RoomNumber}";
            _room = new RoomVisit { Where = _where, IsBoss = def != null && def.IsBossRoom, StartTime = Time.unscaledTime };
            _recentHits.Clear();
            Write($"{_where} 입장 - {kind} | HP {Pct(player.Stats.CurrentHealth, player.Stats.MaxHealth)}");
        }

        public static string DescribeRoom(RoomDefinition def, bool cleared)
        {
            if (def == null)
                return "?";
            if (cleared)
                return "이미 깬 방(출구만)";
            if (def.IsBossRoom)
                return $"보스 {BossBrain.BossName(def.BossStage > 0 ? def.BossStage : StageProgress.CurrentStage)}";
            var kinds = def.EnemyKinds.GroupBy(k => k).Select(g => $"{g.Key}x{g.Count()}");
            var text = $"전투 몹 {def.EnemyCount}({string.Join(",", kinds)})";
            if (def.HasEvent)
                text += $" + 이벤트 {def.EventType}";
            if (GlitchRoom.IsActive)
                text += $" + 오류방 {GlitchRoom.Active}";
            return text;
        }

        /// <summary>플레이어 행동 하나(이동/공격/대기/대시/회전 베기/기믹 조작...).</summary>
        public static void Act(string kind)
        {
            if (!Enabled)
                return;
            Bump(_totals, kind);
            if (_room != null)
                Bump(_room.Acts, kind);
        }

        /// <summary>플레이어 턴이 끝났다(몹 차례로).</summary>
        public static void Turn()
        {
            if (!Enabled)
                return;
            _attemptTurns++;
            if (_room != null)
                _room.Turns++;
        }

        public static void ItemUsed(ItemType type, bool saved)
        {
            var name = ItemInfo.Name(type);
            Act("아이템");
            Bump(_itemTotals, name);
            Write($"{_where} 아이템 사용: {name}{(saved ? " (절약 - 안 줄어듦)" : string.Empty)}");
        }

        /// <summary>플레이어가 맞았다 - 누가(source) 얼마나. 사망 원인 기록용.</summary>
        public static void PlayerHit(string source, float dealt, CharacterStats stats)
        {
            if (!Enabled)
                return;
            if (_room != null)
                _room.DamageTaken += dealt;
            _recentHits.Enqueue($"{source} {dealt:0}(→HP {stats.CurrentHealth:0})");
            while (_recentHits.Count > 4)
                _recentHits.Dequeue();
        }

        public static string EnemyLabel(EnemyActor enemy) =>
            enemy == null ? "?" : enemy.IsBoss ? enemy.DisplayName : $"{enemy.DisplayName}({enemy.Kind})";

        /// <summary>화면 아래 메시지 - 마지막 몇 개를 사망 기록에 같이 붙인다(기믹 즉사 이유 등).</summary>
        public static void Message(string message)
        {
            if (!Enabled || string.IsNullOrEmpty(message))
                return;
            _recentMessages.Enqueue(Plain(message));
            while (_recentMessages.Count > 3)
                _recentMessages.Dequeue();
        }

        public static void InstantKill(string reason)
        {
            if (!Enabled)
                return;
            _recentHits.Enqueue($"기믹 즉사({Plain(reason)})");
            while (_recentHits.Count > 4)
                _recentHits.Dequeue();
        }

        public static void RoomCleared(PlayerActor player, bool puzzle)
        {
            if (!Enabled)
                return;
            if (_room == null)
                return;
            _roomsCleared++;
            Write($"{_room.Where} 클리어{(puzzle ? "(퍼즐)" : string.Empty)} - {_room.Turns}턴 {Time.unscaledTime - _room.StartTime:0}초 | 처치 {_room.Kills} | " +
                  $"받은 피해 {_room.DamageTaken:0} | 남은 HP {Pct(player.Stats.CurrentHealth, player.Stats.MaxHealth)} | {ActsLine(_room.Acts)}");
        }

        public static void Death(PlayerActor player, RoomController room, int goldPenalty)
        {
            if (!Enabled)
                return;
            var stage = StageProgress.CurrentStage;
            var boss = _room != null && _room.IsBoss;
            var d = _stageDeaths.TryGetValue(stage, out var v) ? v : (Normal: 0, Boss: 0);
            _stageDeaths[stage] = boss ? (d.Normal, d.Boss + 1) : (d.Normal + 1, d.Boss);
            _deaths++;
            var killer = _recentHits.Count > 0 ? _recentHits.Last() : "알 수 없음";
            var killerName = killer.Split('(')[0].Trim();
            if (killerName.Length > 0)
                Bump(_killers, killerName.StartsWith("기믹 즉사") ? killer.Split(')')[0] + ")" : killerName);

            var sb = new StringBuilder();
            sb.Append($"★ 사망 ({_where}) - 이 층 {_stageDeaths[stage].Normal + _stageDeaths[stage].Boss}번째, 이번 실행 {_deaths}번째 | ");
            sb.Append($"이 방 {_room?.Turns ?? 0}턴 · 이번 시도 {_attemptTurns}턴 | ");
            if (room != null)
            {
                var alive = room.Enemies.Where(e => e != null && !e.Stats.IsDead).ToList();
                var bossActor = alive.FirstOrDefault(e => e.IsBoss);
                if (bossActor != null)
                    sb.Append($"보스 남은 HP {Pct(bossActor.Stats.CurrentHealth, bossActor.Stats.MaxHealth)} | ");
                var others = alive.Where(e => !e.IsBoss).GroupBy(e => e.IsMinion ? "졸개" : e.Kind.ToString()).Select(g => $"{g.Key}x{g.Count()}").ToList();
                sb.Append($"남은 몹 {(others.Count == 0 ? "없음" : string.Join(",", others))} | ");
            }
            sb.Append($"최근 피격 {(_recentHits.Count == 0 ? "없음" : string.Join(" → ", _recentHits))} | ");
            if (_recentMessages.Count > 0)
                sb.Append($"직전 메시지 \"{string.Join("\" \"", _recentMessages)}\" | ");
            if (_room != null)
                sb.Append($"이 방 행동 {ActsLine(_room.Acts)} | ");
            var held = Enumerable.Range(0, ItemInfo.Count).Where(i => Inventory.Has((ItemType)i)).Select(i => ItemInfo.Name((ItemType)i)).ToList();
            sb.Append($"잃은 아이템 {(held.Count == 0 ? "없음" : string.Join(",", held))} | 골드 손실 {goldPenalty} | {StatLine(player)}");
            Write(sb.ToString());
        }

        public static void DeathChoice(bool toLobby, int startRoom) =>
            Write(toLobby ? "사망 후 선택: 로비로" : $"사망 후 선택: 계속하기 → 방{startRoom + 1}부터");

        public static void Retreat(string where) => Write($"중도 포기: {where} (Esc 메뉴, 데스 패널티 적용)");

        public static void BossDefeated(int stage, PlayerActor player)
        {
            if (!Enabled)
                return;
            _bossKills++;
            var d = _stageDeaths.TryGetValue(stage, out var v) ? v : (Normal: 0, Boss: 0);
            var minutes = _stageEnterTime.TryGetValue(stage, out var t) ? (Time.unscaledTime - t) / 60f : 0f;
            _stageTime[stage] = minutes;
            Write($"■ {stage}층 보스 처치! - 보스방 {_room?.Turns ?? 0}턴 | 이번 실행에서 이 층 사망 일반 {d.Normal}·보스 {d.Boss} | " +
                  $"이 층 {minutes:0.0}분 | 남은 HP {Pct(player.Stats.CurrentHealth, player.Stats.MaxHealth)} | {StatLine(player)}");
        }

        public static void LevelUpOffered(List<UpgradeOption> options) =>
            Write($"레벨업 카드 보기: {string.Join(" / ", options.Select(o => o.Title))}");

        public static void LevelUpChosen(UpgradeOption option, int level) =>
            Write($"레벨업 카드 선택: {option.Title} (Lv{level})");

        public static void RelicChosen(List<RelicType> offer, RelicType picked) =>
            Write($"유물 선택: {Relics.Name(picked)} (보기: {string.Join(", ", offer.Select(Relics.Name))})");

        public static void PuzzleResult(PuzzleType type, bool success, string reason)
        {
            if (!Enabled)
                return;
            if (success)
                _puzzleOk++;
            else
                _puzzleFail++;
            Write(success ? $"{_where} 퍼즐 성공: {type}" : $"{_where} 퍼즐 실패: {type} - {Plain(reason)}");
        }

        public static void PuzzleChoice(PuzzleType type, bool retry) =>
            Write(retry ? $"퍼즐 다시 도전: {type}" : $"퍼즐 포기 → 이전 방으로: {type}");

        public static void EventTriggered(RoomEventType type, string message) =>
            Write($"{_where} 이벤트: {type}{(string.IsNullOrEmpty(message) ? string.Empty : " - " + Plain(message))}");

        public static void Shop(string what) => Write($"{_where} 상인 구매: {what}");
        public static void Lobby(string what) => Write($"로비: {what}");
        public static void Note(string what) => Write(what);

        public static void Ending(int clears) => Write($"■■ 엔딩! {clears}번째 클리어 - 이번 실행 사망 {_deaths}");

        // ---- 끝낼 때 요약 ----
        private static void WriteSummary()
        {
            if (_writer == null)
                return; // 아무것도 안 적은 실행(봇만 돌렸다 등)은 요약도 없다
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(new string('=', 60));
                sb.AppendLine($"이번 실행 요약 ({_sessionStart:HH:mm} ~ {DateTime.Now:HH:mm}, {(DateTime.Now - _sessionStart).TotalMinutes:0}분)");
                sb.AppendLine($"사망 {_deaths} | 방 클리어 {_roomsCleared} | 보스 처치 {_bossKills} | 퍼즐 성공 {_puzzleOk}·실패 {_puzzleFail}");
                foreach (var stage in _stageDeaths.Keys.Union(_stageTime.Keys).OrderBy(s => s))
                {
                    var d = _stageDeaths.TryGetValue(stage, out var v) ? v : (Normal: 0, Boss: 0);
                    var time = _stageTime.TryGetValue(stage, out var t) ? $"{t:0.0}분 만에 클리어" : "클리어 못 함";
                    sb.AppendLine($"  {stage}층: 사망 일반 방 {d.Normal} · 보스방 {d.Boss} | {time}");
                }
                if (_killers.Count > 0)
                    sb.AppendLine($"죽인 것: {string.Join(", ", _killers.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}");
                sb.AppendLine($"행동: {ActsLine(_totals)}");
                if (_itemTotals.Count > 0)
                    sb.AppendLine($"아이템: {string.Join(", ", _itemTotals.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}");
                sb.AppendLine($"끝날 때 기록: {Playtest.RecordLine()}");
                _writer.Write(sb.ToString());
                _writer.Flush();
            }
            catch (Exception)
            {
                // 끝나는 중 - 무시
            }
        }

        private sealed class Runner : MonoBehaviour
        {
            private void OnApplicationQuit()
            {
                WriteSummary();
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
