using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>4~9층 보스 기믹(10-14 사용자 설계) - 체력 50% 이하에서 시작(BossBrain.BeginStageGimmick 호출), 못 막으면 즉사(보호막·부활 무시).
    /// 4 슬라임 킹: 졸개가 킹에게 기어가 붙어 있으면 흡수(킹 회복) - 3번 흡수되면 즉사. 졸개를 먼저 잡거나 킹의 짓누르기에 휘말리게 하면 터지며 킹이 다친다.
    /// 5 버섯 군주: 파동에 맞거나 12턴마다 감염 +1 - 감염 3이면 즉사. 파동 뒤 피는 빛버섯을 밟으면 해독.
    /// 6 망령 궁수: 영혼 촛불 4개가 켜져 있는 동안 무적 - 저격(1·2발)이 촛불 칸을 덮으면 꺼진다. 120턴 안에 다 못 끄면 즉사.
    /// 7 그림자 암살자: 칼이 안 통하는 그림자 분신 3개가 있는 동안 무적 - X자 베기가 분신을 베면 사라진다. 120턴 안에 다 못 지우면 즉사.
    /// 8 미노타우로스: 도끼 내려찍기가 용암 균열(벽)을 남긴다 - 돌진이 균열에 막히면 빠져서 큰 피해·3턴 기절. 균열이 8개 쌓이면 분출 즉사.
    /// 9 리치: 영혼 항아리 3개(벽)가 있는 동안 무적 - 십자 마법 줄이 항아리에 닿으면 깨진다. 120턴 안에 다 못 깨면 즉사.
    /// 남은 상황은 보스 머리 위 글자(CreateBossLabel). 봇은 AutoPlayBot.TryStageGimmick이 같은 규칙으로 대응한다.</summary>
    public partial class RoomController
    {
        public const int GimmickTurnLimit = 120;
        public const int CandleTurnLimit = 100;        // 6층 촛불 - 120 → 60(10-15 "너무 쉽다") → 100(봇 100판 bot_20261015_122831: 저격·포위 폭발 번갈아 + 60턴에 6층 제한 시간 즉사 판당 35회)
        public const int CloneTurnLimit = 30;          // 7층 분신 물결마다(10-15 사용자 - 120에서)
        public const int SlimeAbsorbLimit = 3;
        private const int SlimeAbsorbTurns = 2;        // 킹에게 붙어서 이만큼 버티면 흡수
        private const float SlimeAbsorbHeal = 0.1f;    // 흡수 한 번에 킹 최대 체력 ×
        private const float SlimeSlamBurstDamage = 0.08f;
        public const int InfectionLimit = 3;
        private const int InfectionEveryTurns = 12;
        private const int MaxMushrooms = 3;
        private const int CandleCount = 4;
        private const int CloneCount = 4;              // 3 → 4(10-15 사용자 - 분신 동안 X자만 넣은 뒤 7층 보스방 사망 2.4~3.2, 목표 8)
        public const int LavaEruptCount = 8;
        private const int LavaPerSlam = 2;
        private const float LavaFallDamage = 0.15f;
        private const int LavaFallStunTurns = 3;
        private const int JarCount = 3;
        private const int GimmickBreakStunTurns = 3;   // 촛불·분신·항아리를 다 없애면 보스 기절

        /// <summary>봇 통계용 - 층별 기믹 즉사 횟수(인덱스 = 층).</summary>
        public static readonly int[] GimmickKills = new int[11];

        private int _gimmickStage;   // 0 = 없음
        private EnemyActor _gimmickBoss;
        private int _gimmickTurnsLeft;
        private TextMesh _gimmickLabel;

        private int _absorbed;
        private readonly Dictionary<EnemyActor, int> _absorbTimer = new Dictionary<EnemyActor, int>();
        private int _infection;
        private int _infectionClock;
        private readonly Dictionary<Vector2Int, GameObject> _mushrooms = new Dictionary<Vector2Int, GameObject>();
        private readonly Dictionary<Vector2Int, GameObject> _candles = new Dictionary<Vector2Int, GameObject>();
        private readonly List<EnemyActor> _clones = new List<EnemyActor>();
        private readonly HashSet<Vector2Int> _lava = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _jars = new HashSet<Vector2Int>();

        public int GimmickStage => _gimmickStage;
        public int Infection => _infection;
        public IReadOnlyCollection<Vector2Int> Mushrooms => _mushrooms.Keys;
        public IReadOnlyCollection<Vector2Int> Candles => _candles.Keys;
        public IReadOnlyList<EnemyActor> Clones => _clones;
        public IReadOnlyCollection<Vector2Int> LavaCracks => _lava;
        public IReadOnlyCollection<Vector2Int> LichJars => _jars;
        public bool IsLichJar(Vector2Int p) => _jars.Contains(p);

        /// <summary>6·7·9층 - 촛불·분신·항아리가 남아 있는 동안 보스는 무적(BossBrain.ArmorMultiplier).</summary>
        public bool BossInvulnerable =>
            (_gimmickStage == 6 && _candles.Count > 0) || (_gimmickStage == 7 && _clones.Count > 0) || (_gimmickStage == 9 && _jars.Count > 0);

        /// <summary>4층 흡수 단계 - 졸개는 플레이어 대신 킹에게 기어간다(EnemyActor.TakeTurn).</summary>
        public Vector2Int? MinionTarget(EnemyActor minion) =>
            _gimmickStage == 4 && minion.IsMinion && _gimmickBoss != null && !_gimmickBoss.Stats.IsDead ? _gimmickBoss.GridPos : (Vector2Int?)null;

        /// <summary>4층 흡수 단계면 졸개를 킹에게서 멀리 소환(SpawnMinions) - 바로 옆에 나오면 기어갈 틈도 없이 흡수된다.</summary>
        private bool SpawnMinionsFar => _gimmickStage == 4;

        // ------------------------------------------------------------------ 시작
        public void BeginStageGimmick(int stage, EnemyActor boss)
        {
            // 7층은 체력 10%마다 분신 물결이 다시 온다(BossBrain.CloneWaveHealth) - 두 번째부터는 지우지 않고 분신만 더한다
            var cloneAgain = stage == 7 && _gimmickStage == 7 && _gimmickBoss == boss;
            if (!cloneAgain)
                ClearStageGimmick();
            _gimmickStage = stage;
            _gimmickBoss = boss;
            var c = new GimmickCutscene();
            string after = null;
            switch (stage)
            {
                case 4:
                    c.Line = Loc.T("<color=#FF7070>슬라임 킹이 졸개를 불러들인다!</color> 졸개가 킹에게 붙어 있으면 흡수된다");
                    for (var i = 0; i < BossBrain.MaxMinions; i++)
                        c.Steps.Add(EnemyStep(() =>
                        {
                            var alive = _enemies.Count(e => e != null && e.IsMinion && !e.Stats.IsDead);
                            SpawnMinions(boss, alive + 1); // 소환 이펙트는 SpawnMinions가(HitFeedback.OnSummon)
                            return _enemies.Count(e => e != null && e.IsMinion && !e.Stats.IsDead) > alive;
                        }, null));
                    after = Loc.F("졸개를 먼저 잡거나 킹의 짓누르기에 휘말리게 하자 - <color=#FF6060>{0}번 흡수되면 끝장이다</color>", SlimeAbsorbLimit);
                    break;
                case 5:
                    _infectionClock = InfectionEveryTurns;
                    c.Line = Loc.T("<color=#C080FF>버섯 군주가 포자를 흩뿌린다!</color> 파동에 맞거나 시간이 지나면 감염된다");
                    c.Steps.Add(PropStep(_mushrooms, SpawnMushroomAnywhere, "mushroom_sprout"));
                    after = Loc.F("파동 뒤에 피는 빛버섯을 밟으면 해독 - <color=#FF6060>감염 {0}이면 끝장이다</color>", InfectionLimit);
                    break;
                case 6:
                    _gimmickTurnsLeft = CandleTurnLimit;
                    c.Line = Loc.T("<color=#A0E0FF>망령 궁수가 모습을 감추고 영혼 촛불이 켜졌다!</color> 촛불이 켜져 있는 동안 궁수는 무적");
                    for (var i = 0; i < CandleCount; i++)
                        c.Steps.Add(PropStep(_candles, () =>
                        {
                            var before = _candles.Count;
                            SpawnFloorProps(_candles, before + 1, "Spirit_Candle", new Color(0.7f, 0.9f, 1f), boss, 3);
                            return _candles.Count > before;
                        }, "candle_light"));
                    after = Loc.F("촛불 옆에 서서 저격이 촛불을 덮게 하자 - <color=#FF6060>{0}턴 안에 다 못 끄면 끝장이다</color>", CandleTurnLimit);
                    break;
                case 7:
                    _gimmickTurnsLeft = CloneTurnLimit;
                    c.Line = cloneAgain
                        ? Loc.T("<color=#B090D0>그림자 암살자가 다시 분신을 만들었다!</color> 분신이 있는 동안 암살자는 무적")
                        : Loc.T("<color=#B090D0>그림자 암살자가 분신을 만들었다!</color> 분신에겐 칼이 안 통하고, 분신이 있는 동안 암살자는 무적");
                    for (var i = 0; i < CloneCount; i++)
                        c.Steps.Add(EnemyStep(() => SpawnClones(boss, 1) > 0, "shadow_split"));
                    after = cloneAgain ? null
                        : Loc.F("분신과 대각선 줄에 서서 X자 베기가 분신을 베게 하자 - <color=#FF6060>{0}턴 안에 다 못 지우면 끝장이다</color>", CloneTurnLimit);
                    break;
                case 8:
                    c.Line = Loc.T("<color=#FF9040>미노타우로스의 도끼가 바닥을 갈라 용암이 솟는다!</color>");
                    for (var i = 0; i < LavaPerSlam; i++)
                        c.Steps.Add(WallStep(() =>
                        {
                            var before = _lava.Count;
                            AddLava(System.Array.Empty<Vector2Int>(), 1);
                            return _lava.Count > before;
                        }, "lava_burst"));
                    after = Loc.F("균열 뒤에 서서 돌진을 균열에 빠뜨리자 - <color=#FF6060>균열이 {0}개 쌓이면 용암이 분출한다</color>", LavaEruptCount);
                    break;
                case 9:
                    _gimmickTurnsLeft = GimmickTurnLimit;
                    c.Line = Loc.T("<color=#C090FF>리치가 영혼을 항아리에 숨겼다!</color> 항아리가 남아 있는 동안 리치는 무적");
                    for (var i = 0; i < JarCount; i++)
                        c.Steps.Add(WallStep(() => SpawnJars(boss, _jars.Count + 1) > 0, "soul_summon"));
                    after = Loc.F("리치와 같은 줄, 항아리 뒤에 서서 십자 마법이 항아리를 깨게 하자 - <color=#FF6060>{0}턴 안에 다 못 깨면 끝장이다</color>", GimmickTurnLimit);
                    break;
            }
            c.Finish = () =>
            {
                if (after != null)
                    ShowMessage(after);
                UpdateGimmickLabel();
            };
            UpdateGimmickLabel();
            PlayGimmickCutscene(c);
        }

        /// <summary>5층 시작 컷신 - 빛버섯 하나(보스에서 2칸 이상, 빈 칸 아무 데나).</summary>
        private bool SpawnMushroomAnywhere()
        {
            if (_gimmickBoss == null || _mushrooms.Count >= MaxMushrooms)
                return false;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && !_mushrooms.ContainsKey(p) && ExitPosition != p && p != _player.GridPos
                    && GridMap.FootprintChebyshev(_gimmickBoss, p) >= 2)
                    cells.Add(p);
            }
            if (cells.Count == 0)
                return false;
            SpawnFloorProp(_mushrooms, cells[Rng.Next(cells.Count)], "Glow_Mushroom", new Color(0.6f, 1f, 0.75f));
            return true;
        }

        // ------------------------------------------------------------------ 보스 패턴 발동 때(BossBrain.Resolve)
        /// <summary>4층 짓누르기 - 범위 안 졸개는 터지며 킹이 다친다.</summary>
        public void OnSlimeSlam(IEnumerable<Vector2Int> tiles)
        {
            if (_gimmickStage != 4 || _gimmickBoss == null)
                return;
            var set = new HashSet<Vector2Int>(tiles);
            var burst = _enemies.Where(e => e != null && e.IsMinion && !e.Stats.IsDead && set.Contains(e.GridPos)).ToList();
            foreach (var m in burst)
                DespawnEnemy(m, new Color(1f, 0.4f, 0.4f));
            if (burst.Count == 0)
                return;
            ShowMessage(Loc.F("<color=#FF7070>짓누르기에 졸개 {0}마리가 터졌다!</color> 킹이 산성에 다친다", burst.Count));
            DamageGimmickBoss(_gimmickBoss.Stats.MaxHealth * SlimeSlamBurstDamage * burst.Count, new Color(0.6f, 1f, 0.4f));
        }

        /// <summary>5층 파동 - 맞았으면 감염 +1, 파동이 지나간 자리 근처에 빛버섯 하나. 감염으로 죽었으면 true(사망 처리는 몹 턴 루프).</summary>
        public bool OnSporeRing(IEnumerable<Vector2Int> tiles, bool playerHit)
        {
            if (_gimmickStage != 5)
                return false;
            if (_mushrooms.Count < MaxMushrooms)
            {
                var ring = new HashSet<Vector2Int>(tiles);
                var cells = new List<Vector2Int>();
                for (var x = 0; x < Map.Width; x++)
                for (var y = 0; y < Map.Height; y++)
                {
                    var p = new Vector2Int(x, y);
                    if (Map.IsWalkable(p) && !ring.Contains(p) && !_mushrooms.ContainsKey(p) && ExitPosition != p && p != _player.GridPos
                        && GridMap.FootprintChebyshev(_gimmickBoss, p) >= 2)
                        cells.Add(p);
                }
                if (cells.Count > 0)
                    SpawnFloorProp(_mushrooms, cells[Rng.Next(cells.Count)], "Glow_Mushroom", new Color(0.6f, 1f, 0.75f));
            }
            if (!playerHit || !AddInfection(Loc.T("포자에 감염됐다!")))
                return false;
            GimmickKills[5]++;
            GimmickDeathFx();
            KillPlayerInstantly(); // 몹 턴 도중 - 사망 처리는 RunEnemyTurnsCore가
            return true;
        }

        /// <summary>6층 저격(1·2발) - 덮은 칸의 촛불이 꺼진다.</summary>
        public void OnSnipeTiles(IEnumerable<Vector2Int> tiles)
        {
            if (_gimmickStage != 6)
                return;
            var hit = tiles.Where(_candles.ContainsKey).ToList();
            foreach (var c in hit)
                RemoveFloorProp(_candles, c, new Color(0.7f, 0.9f, 1f));
            if (hit.Count == 0)
                return;
            ShowMessage(_candles.Count > 0
                ? Loc.F("<color=#A0E0FF>화살이 촛불을 꺼뜨렸다!</color> 남은 촛불 {0}", _candles.Count)
                : Loc.T("<color=#A0E0FF>촛불이 모두 꺼졌다!</color> 망령 궁수가 실체를 드러내며 비틀거린다"));
            if (_candles.Count == 0)
                OnGimmickBroken();
            UpdateGimmickLabel();
        }

        /// <summary>7층 X자 베기 - 범위 안 분신이 베여 사라진다.</summary>
        public void OnDiagonalTiles(IEnumerable<Vector2Int> tiles)
        {
            if (_gimmickStage != 7)
                return;
            var set = new HashSet<Vector2Int>(tiles);
            var cut = _clones.Where(c => c != null && set.Contains(c.GridPos)).ToList();
            foreach (var c in cut)
            {
                _clones.Remove(c);
                DespawnEnemy(c, new Color(0.5f, 0.3f, 0.7f));
            }
            if (cut.Count == 0)
                return;
            ShowMessage(_clones.Count > 0
                ? Loc.F("<color=#B090D0>암살자의 칼날이 분신을 베었다!</color> 남은 분신 {0}", _clones.Count)
                : Loc.T("<color=#B090D0>분신이 모두 사라졌다!</color> 그림자 암살자가 비틀거린다"));
            if (_clones.Count == 0)
                OnGimmickBroken();
            UpdateGimmickLabel();
        }

        /// <summary>8층 도끼 내려찍기 - 충격이 퍼져 보스 몸에서 2~4칸 떨어진 칸 몇 곳에 용암 균열(벽). 분출 개수에 닿으면 즉사시키고 true.
        /// (처음엔 내려찍은 테두리 칸 = 몸 바로 둘레에 깔아서 보스가 구석에서 균열에 갇혀 못 움직였다 - 10-14 봇 갇힘 3판)</summary>
        public bool AddLava(IEnumerable<Vector2Int> tiles, int count = LavaPerSlam)
        {
            if (_gimmickStage != 8 || _gimmickBoss == null)
                return false;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var c = new Vector2Int(x, y);
                var d = GridMap.FootprintChebyshev(_gimmickBoss, c);
                if (d >= 2 && d <= 4 && Map.IsWalkable(c) && c != _player.GridPos && ExitPosition != c)
                    cells.Add(c);
            }
            var placed = 0;
            foreach (var p in cells.OrderBy(_ => Rng.Next()))
            {
                if (placed >= count)
                    break;
                if (!WallKeepsConnected(p))
                    continue;
                AddWallProp(p, "Lava_Crack", new Color(0.9f, 0.35f, 0.1f), "LavaCrack");
                _lava.Add(p);
                placed++;
            }
            UpdateGimmickLabel();
            if (_lava.Count >= LavaEruptCount)
            {
                GimmickKills[8]++;
                ShowMessage(Loc.T("<color=#FF4040>용암이 한꺼번에 분출했다!</color>"));
                GimmickDeathFx();
                KillPlayerInstantly();
                return true;
            }
            if (placed > 0 && _lava.Count >= LavaEruptCount - 2)
                ShowMessage(Loc.F("<color=#FF6060>바닥이 끓어오른다...</color> 균열 {0}/{1}", _lava.Count, LavaEruptCount));
            return false;
        }

        /// <summary>8층 돌진이 막힌 칸에 용암 균열이 있으면 빠진다 - 균열이 메워지고 큰 피해 + 기절. 빠졌으면 true.</summary>
        public bool TryLavaFall(IEnumerable<Vector2Int> cells, EnemyActor boss)
        {
            if (_gimmickStage != 8)
                return false;
            var hit = cells.Where(_lava.Contains).ToList();
            if (hit.Count == 0)
                return false;
            foreach (var p in hit)
            {
                _lava.Remove(p);
                RemoveWallObject(p);
            }
            // 기절은 BossBrain이 돌진 충돌 기절(BoarCrashStunTurns = LavaFallStunTurns)로 건다 - 여기서 또 걸면 두 배가 된다
            ShowMessage(Loc.T("<color=#FF9040>미노타우로스가 용암 균열에 빠졌다!</color> 크게 다쳐 비틀거린다"));
            DamageGimmickBoss(boss.Stats.MaxHealth * LavaFallDamage, new Color(1f, 0.5f, 0.2f));
            UpdateGimmickLabel();
            return true;
        }

        /// <summary>9층 십자 마법 - 줄 끝에 닿은 항아리가 깨진다.</summary>
        public void BreakLichJars(IEnumerable<Vector2Int> hits)
        {
            if (_gimmickStage != 9)
                return;
            var broken = hits.Where(_jars.Contains).Distinct().ToList();
            foreach (var j in broken)
            {
                _jars.Remove(j);
                RemoveWallObject(j);
            }
            if (broken.Count == 0)
                return;
            ShowMessage(_jars.Count > 0
                ? Loc.F("<color=#C090FF>마법이 영혼 항아리를 깨뜨렸다!</color> 남은 항아리 {0}", _jars.Count)
                : Loc.T("<color=#C090FF>항아리가 모두 깨졌다!</color> 리치의 영혼이 흔들린다"));
            if (_jars.Count == 0)
                OnGimmickBroken();
            UpdateGimmickLabel();
        }

        // ------------------------------------------------------------------ 몹 턴 끝(RunEnemyTurnsCore)
        /// <summary>턴마다 - 흡수·감염·제한 턴·빛버섯 줍기. 즉사했으면 사망 처리까지 하고 true.</summary>
        private bool TickStageGimmick()
        {
            if (_gimmickStage == 0)
                return false;
            if (_roomCleared || _gimmickBoss == null || _gimmickBoss.Stats.IsDead)
            {
                ClearStageGimmick();
                return false;
            }

            var dead = false;
            switch (_gimmickStage)
            {
                case 4:
                    foreach (var m in _enemies.Where(e => e != null && e.IsMinion && !e.Stats.IsDead).ToList())
                    {
                        if (GridMap.FootprintDistance(_gimmickBoss, m.GridPos) != 1)
                        {
                            _absorbTimer.Remove(m);
                            continue;
                        }
                        _absorbTimer.TryGetValue(m, out var t);
                        if (++t < SlimeAbsorbTurns)
                        {
                            _absorbTimer[m] = t;
                            continue;
                        }
                        _absorbTimer.Remove(m);
                        DespawnEnemy(m, new Color(1f, 0.3f, 0.3f));
                        _absorbed++;
                        _gimmickBoss.Stats.Heal(_gimmickBoss.Stats.MaxHealth * SlimeAbsorbHeal);
                        ShowMessage(Loc.F("<color=#FF7070>슬라임 킹이 졸개를 흡수했다!</color> 흡수 {0}/{1}", _absorbed, SlimeAbsorbLimit));
                        if (_absorbed >= SlimeAbsorbLimit)
                        {
                            ShowMessage(Loc.T("<color=#FF4040>슬라임 킹이 거대해져 모든 것을 삼켰다!</color>"));
                            dead = true;
                            break;
                        }
                    }
                    break;
                case 5:
                    if (_mushrooms.ContainsKey(_player.GridPos))
                    {
                        RemoveFloorProp(_mushrooms, _player.GridPos, new Color(0.6f, 1f, 0.75f));
                        if (_infection > 0)
                            ShowMessage(Loc.T("<color=#80FFB0>빛버섯을 먹었다!</color> 감염이 씻겨 나갔다"));
                        _infection = 0;
                    }
                    if (--_infectionClock <= 0)
                    {
                        _infectionClock = InfectionEveryTurns;
                        dead = AddInfection(Loc.T("공기 중의 포자에 감염됐다!"));
                    }
                    break;
                case 6:
                case 7:
                case 9:
                    if (!BossInvulnerable)
                        break; // 이미 다 없앴다
                    if (--_gimmickTurnsLeft <= 0)
                    {
                        ShowMessage(_gimmickStage switch
                        {
                            6 => Loc.T("<color=#FF4040>망령 궁수의 화살비가 쏟아졌다!</color>"),
                            7 => Loc.T("<color=#FF4040>그림자들이 한꺼번에 덮쳤다!</color>"),
                            _ => Loc.T("<color=#FF4040>리치가 죽음의 주문을 완성했다!</color>"),
                        });
                        dead = true;
                    }
                    else if (System.Array.IndexOf(GimmickWarnAt, _gimmickTurnsLeft) >= 0)
                        ShowMessage(Loc.F("<color=#FF6060>시간이 얼마 없다...</color> {0}턴!", _gimmickTurnsLeft));
                    break;
            }
            UpdateGimmickLabel();
            if (!dead)
                return false;
            GimmickKills[_gimmickStage]++;
            GimmickDeathFx();
            KillPlayerInstantly();
            ClearAllTelegraphs();
            _loopManager.OnPlayerDied();
            return true;
        }

        private static readonly int[] GimmickWarnAt = { 60, 30, 10, 5, 3, 2, 1 };

        /// <summary>감염 +1 - 한계에 닿았으면 true(즉사 처리는 호출부가).</summary>
        private bool AddInfection(string reason)
        {
            _infection++;
            ShowMessage($"<color=#C080FF>{reason}</color> " + Loc.F("감염 {0}/{1}", _infection, InfectionLimit));
            if (HitFeedback.Enabled && _player != null)
                ActorJuice.Get(_player).Flash(new Color(0.75f, 0.45f, 1f));
            UpdateGimmickLabel();
            if (_infection < InfectionLimit)
                return false;
            ShowMessage(Loc.T("<color=#FF4040>포자가 온몸을 뒤덮었다!</color>"));
            return true;
        }

        /// <summary>촛불·분신·항아리를 다 없앴다 - 무적이 풀리고 보스가 기절.</summary>
        private void OnGimmickBroken()
        {
            _gimmickTurnsLeft = 0;
            if (_gimmickBoss != null && !_gimmickBoss.Stats.IsDead)
                _gimmickBoss.Stun(GimmickBreakStunTurns);
            if (HitFeedback.Enabled)
            {
                CameraShake.Shake(0.25f, 0.45f);
                SfxPlayer.Play(Sfx.BossDown);
            }
        }

        private void GimmickDeathFx()
        {
            if (!HitFeedback.Enabled)
                return;
            CameraShake.Shake(0.5f, 0.9f);
            SfxPlayer.Play(Sfx.Explosion);
        }

        private void DamageGimmickBoss(float damage, Color color)
        {
            var boss = _gimmickBoss;
            if (boss == null || boss.Stats.IsDead)
                return;
            boss.Stats.TakeDamage(damage);
            DamagePopup.Spawn(boss.transform.position, damage, color, isCritical: true);
            HandleBossDamagedByItem(boss); // 그걸로 죽었으면 처치 처리
        }

        // ------------------------------------------------------------------ 표시
        private void UpdateGimmickLabel()
        {
            if (DamagePopup.Suppressed || _gimmickStage == 0 || _gimmickBoss == null || _gimmickBoss.Stats.IsDead)
                return;
            string text = null;
            var danger = false;
            switch (_gimmickStage)
            {
                case 4:
                    text = Loc.F("흡수 {0}/{1}", _absorbed, SlimeAbsorbLimit);
                    danger = _absorbed >= SlimeAbsorbLimit - 1;
                    break;
                case 5:
                    text = Loc.F("감염 {0}/{1} · {2}턴", _infection, InfectionLimit, _infectionClock);
                    danger = _infection >= InfectionLimit - 1;
                    break;
                case 6:
                    text = _candles.Count > 0 ? Loc.F("촛불 {0} · {1}턴", _candles.Count, _gimmickTurnsLeft) : null;
                    danger = _gimmickTurnsLeft <= 20;
                    break;
                case 7:
                    text = _clones.Count > 0 ? Loc.F("분신 {0} · {1}턴", _clones.Count, _gimmickTurnsLeft) : null;
                    danger = _gimmickTurnsLeft <= 20;
                    break;
                case 8:
                    text = Loc.F("균열 {0}/{1}", _lava.Count, LavaEruptCount);
                    danger = _lava.Count >= LavaEruptCount - 2;
                    break;
                case 9:
                    text = _jars.Count > 0 ? Loc.F("항아리 {0} · {1}턴", _jars.Count, _gimmickTurnsLeft) : null;
                    danger = _gimmickTurnsLeft <= 20;
                    break;
            }
            if (text == null)
            {
                if (_gimmickLabel != null)
                {
                    Destroy(_gimmickLabel.gameObject);
                    _gimmickLabel = null;
                }
                return;
            }
            if (_gimmickLabel == null)
                _gimmickLabel = CreateBossLabel(_gimmickBoss, "GimmickLabel");
            _gimmickLabel.text = text;
            _gimmickLabel.color = danger ? new Color(1f, 0.3f, 0.3f) : Color.white;
        }

        // ------------------------------------------------------------------ 소품·분신
        private void SpawnFloorProps(Dictionary<Vector2Int, GameObject> into, int count, string sprite, Color fallback, EnemyActor boss, int minFromBoss)
        {
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && ExitPosition != p && p != _player.GridPos && GridMap.FootprintChebyshev(boss, p) >= minFromBoss
                    && into.Keys.All(o => Mathf.Max(Mathf.Abs(o.x - p.x), Mathf.Abs(o.y - p.y)) >= 3))
                    cells.Add(p);
            }
            foreach (var p in cells.OrderBy(_ => Rng.Next()))
            {
                if (into.Count >= count)
                    break;
                if (into.Keys.Any(o => Mathf.Max(Mathf.Abs(o.x - p.x), Mathf.Abs(o.y - p.y)) < 3))
                    continue;
                SpawnFloorProp(into, p, sprite, fallback);
            }
        }

        private void SpawnFloorProp(Dictionary<Vector2Int, GameObject> into, Vector2Int p, string sprite, Color fallback)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
            if (VisualUtil.TryCreateProp(go, sprite, -6) == null)
                VisualUtil.CreateSquareVisual(go, fallback, GridConstants.CellSize * 0.5f, sortingOrder: -6);
            _roomObjects.Add(go);
            into[p] = go;
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(go.transform.position, fallback, 8, GridConstants.CellSize * 1.1f, GridConstants.CellSize * 0.1f);
        }

        private void RemoveFloorProp(Dictionary<Vector2Int, GameObject> from, Vector2Int p, Color burst)
        {
            if (!from.TryGetValue(p, out var go))
                return;
            from.Remove(p);
            if (go != null)
            {
                if (!DamagePopup.Suppressed)
                    DeathBurst.Spawn(go.transform.position, burst, 12, GridConstants.CellSize * 1.5f, GridConstants.CellSize * 0.12f);
                Destroy(go);
            }
        }

        private void AddWallProp(Vector2Int p, string sprite, Color fallback, string goName)
        {
            Map.AddWall(p);
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
            if (VisualUtil.TryCreateProp(go, sprite, -7) == null)
                VisualUtil.CreateSquareVisual(go, fallback, GridConstants.CellSize * 0.8f, sortingOrder: -7);
            _roomObjects.Add(go);
            _wallObjects[p] = go;
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(go.transform.position, fallback, 10, GridConstants.CellSize * 1.3f, GridConstants.CellSize * 0.12f);
        }

        /// <summary>항아리를 target개가 되도록 - 놓은 개수.</summary>
        private int SpawnJars(EnemyActor boss, int target = JarCount)
        {
            var before = _jars.Count;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && ExitPosition != p && p != _player.GridPos && GridMap.FootprintChebyshev(boss, p) >= 3)
                    cells.Add(p);
            }
            foreach (var p in cells.OrderBy(_ => Rng.Next()))
            {
                if (_jars.Count >= target)
                    break;
                if (_jars.Any(o => Mathf.Max(Mathf.Abs(o.x - p.x), Mathf.Abs(o.y - p.y)) < 3) || !WallKeepsConnected(p))
                    continue;
                AddWallProp(p, "Lich_Jar", new Color(0.6f, 0.35f, 0.85f), "LichJar");
                _jars.Add(p);
            }
            return _jars.Count - before;
        }

        /// <summary>분신 count개 - 만든 개수.</summary>
        private int SpawnClones(EnemyActor boss, int count = CloneCount)
        {
            var made = 0;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && ExitPosition != p && GridMap.FootprintChebyshev(boss, p) >= 2
                    && Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) >= 3)
                    cells.Add(p);
            }
            foreach (var p in cells.OrderBy(_ => Rng.Next()).Take(count))
            {
                made++;
                var go = new GameObject("ShadowClone");
                go.transform.SetParent(transform, false);
                var clone = go.AddComponent<EnemyActor>();
                clone.Initialize(boss.Stats.MaxHealth * BossBrain.MinionHealthRatio, boss.Stats.AttackPower * BossBrain.MinionAttackRatio,
                    isBoss: false, kind: EnemyKind.Assassin);
                clone.MarkAsMinion();     // 보상 없음, 보스가 죽으면 같이 사라짐
                clone.MarkAsShadowClone(); // 칼이 안 통함 + 어두운 반투명
                Map.PlaceActor(clone, p);
                HitFeedback.OnSummon(new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f));
                _enemies.Add(clone);
                _clones.Add(clone);
            }
            return made;
        }

        /// <summary>보상 없이 몹 하나를 치운다(흡수·터짐·분신 베임) - 방이 비었는지는 보지 않는다(보스가 살아 있을 때만 쓴다).</summary>
        private void DespawnEnemy(EnemyActor e, Color burst)
        {
            if (e == null)
                return;
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(e.transform.position, burst, 14, GridConstants.CellSize * 1.6f, GridConstants.CellSize * 0.14f);
            e.Stats.TakeDamage(e.Stats.CurrentHealth + 1f);
            Map.RemoveActor(e);
            _enemies.Remove(e);
            _absorbTimer.Remove(e);
            Destroy(e.gameObject);
        }

        /// <summary>방을 새로 깔 때·보스가 죽었을 때.</summary>
        private void ClearStageGimmick()
        {
            _gimmickStage = 0;
            _gimmickBoss = null;
            _gimmickTurnsLeft = 0;
            _absorbed = 0;
            _absorbTimer.Clear();
            _infection = 0;
            _infectionClock = 0;
            foreach (var go in _mushrooms.Values.Concat(_candles.Values))
                if (go != null)
                    Destroy(go);
            _mushrooms.Clear();
            _candles.Clear();
            _clones.Clear();
            _lava.Clear();
            _jars.Clear();
            if (_gimmickLabel != null)
                Destroy(_gimmickLabel.gameObject);
            _gimmickLabel = null;
        }
    }
}
