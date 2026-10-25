using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>4~9층 보스 기믹(10-14 사용자 설계) - 체력 50% 이하에서 시작(BossBrain.BeginStageGimmick 호출), 못 막으면 즉사(보호막·부활 무시).
    /// 4 슬라임 킹: 2x2로 작아지며 분열 - 둘레 벽 위를 타고 따라다니는 포탑이 점액 파동을 쏘고 킹은 점프로 점액을 남긴다. 점액이 쌓이면 즉사(RoomController.SlimeKing, 10-16 사용자).
    /// 5 버섯 군주: 파동에 맞거나 12턴마다 감염 +1 - 감염 3이면 즉사. 파동 뒤 피는 빛버섯을 밟으면 해독.
    /// 6 망령 궁수: 영혼 촛불 4개가 켜져 있는 동안 무적 - 저격(1·2발)이 촛불 칸을 덮으면 꺼진다. 120턴 안에 다 못 끄면 즉사.
    ///   체력 25% 아래에선 영혼 과녁(RoomController.SoulTarget, 10-23) - 과녁 5개를 저격에서 지키며 30턴 버티기.
    /// 7 그림자 암살자: 칼이 안 통하는 그림자 분신 3개가 있는 동안 무적 - X자 베기가 분신을 베면 사라진다. 120턴 안에 다 못 지우면 즉사.
    ///   체력 75%·35% 아래(분신이 없을 때)엔 그림자 박자(RoomController.ShadowBeat, 10-23) - 빈 칸 순서를 보여 주고 5박자 동안 방 전체를 벤다(35%는 하얀 표식 섞임).
    /// 8 미노타우로스: 사방 벽이 조여 온다 - 도끼 충격파가 벽에 닿으면 물러난다. 닫히는 줄에 있거나 방이 너무 좁아지면 즉사(RoomController.Minotaur, 10-16 사용자).
    /// 9 리치: 체력 50%·25%에 죽음의 낙인 - 낙인이 있는 동안 무적, 쫓아오는 영혼 구체를 리치에게 흡수시켜 옮긴다(RoomController.LichDoom, 10-19 사용자).
    /// 남은 상황은 보스 머리 위 글자(CreateBossLabel). 봇은 AutoPlayBot.TryStageGimmick이 같은 규칙으로 대응한다.</summary>
    public partial class RoomController
    {
        public const int GimmickTurnLimit = 120;
        public const int CandleTurnLimit = 100;        // 6층 촛불 - 120 → 60(10-15 "너무 쉽다") → 100(봇 100판 bot_20261015_122831: 저격·포위 폭발 번갈아 + 60턴에 6층 제한 시간 즉사 판당 35회)
        public const int CloneTurnLimit = 30;          // 7층 분신 물결마다(10-15 사용자 - 120에서)
        public const int InfectionLimit = 3;
        public const int InfectionEveryTurns = 12;
        private const int MaxMushrooms = 3;
        private const int CandleCount = 4;
        private const int CloneCount = 4;              // 3 → 4(10-15 사용자 - 분신 동안 X자만 넣은 뒤 7층 보스방 사망 2.4~3.2, 목표 8)
        public const int LavaEruptCount = 8;
        private const int LavaPerSlam = 2;
        private const float LavaFallDamage = 0.15f;
        private const int LavaFallStunTurns = 3;
        private const int GimmickBreakStunTurns = 3;   // 촛불·분신·항아리를 다 없애면 보스 기절
        // 10층 거울 가면 기사 "진짜 찾기"(10-15 사용자): 체력 75%에 좌우가 뒤집힌 거울상 MirrorFakeCount개 - 칼이 안 통하고 진짜의 예고 공격에
        // 맞아야 깨진다. 진짜는 그동안에도 맞는다. MirrorFakeTurnLimit턴 안에 다 못 깨면 즉사.
        private const int MirrorFakeCount = 3; // 2 → 3(10-19 사용자 - 봇 300판 1단계 즉사 0.0회, 절차처럼 느껴짐)
        public const int MirrorFakeTurnLimit = 60;

        /// <summary>봇 통계용 - 층별 기믹 즉사 횟수(인덱스 = 층).</summary>
        public static readonly int[] GimmickKills = new int[11];

        private int _gimmickStage;   // 0 = 없음
        private EnemyActor _gimmickBoss;
        private int _gimmickTurnsLeft;
        /// <summary>이번 몹 턴에 기믹이 시작됐다 - 그 턴 끝 틱은 건너뛴다(예전엔 시작 턴에 제한 턴·감염 시계가 1 먼저 깎였다, 10-23).
        /// 몹 턴 시작 때 지운다(보스 러시는 입장 때 시작하니 첫 몹 턴부터 정상으로 깎인다).</summary>
        private bool _gimmickJustStarted;
        private TextMesh _gimmickLabel;

        private int _infection;
        private int _infectionClock;
        private readonly Dictionary<Vector2Int, GameObject> _mushrooms = new Dictionary<Vector2Int, GameObject>();
        private readonly Dictionary<Vector2Int, GameObject> _candles = new Dictionary<Vector2Int, GameObject>();
        private readonly List<EnemyActor> _clones = new List<EnemyActor>();
        private readonly HashSet<Vector2Int> _lava = new HashSet<Vector2Int>();
        private readonly List<EnemyActor> _mirrorFakes = new List<EnemyActor>();
        private readonly Dictionary<EnemyActor, TextMesh> _mirrorFakeLabels = new Dictionary<EnemyActor, TextMesh>();

        public int GimmickStage => _gimmickStage;
        public int Infection => _infection;
        public IReadOnlyCollection<Vector2Int> Mushrooms => _mushrooms.Keys;
        public IReadOnlyCollection<Vector2Int> Candles => _candles.Keys;
        public IReadOnlyList<EnemyActor> Clones => _clones;
        public IReadOnlyCollection<Vector2Int> LavaCracks => _lava;
        public IReadOnlyList<EnemyActor> MirrorFakes => _mirrorFakes;
        /// <summary>10층 거울상이 남아 있는 동안 - 보스 준비 동작 표시를 끈다(진짜만 숨쉬면 바로 들킨다).</summary>
        public bool MirrorFakesActive => _gimmickStage == BossBrain.MirrorStage && _mirrorFakes.Count > 0;

        /// <summary>폭탄에 안 부서지는 기믹 벽 - 8층 용암 균열, 10층 빛 단계 거울·조력자(10-16 사용자).
        /// 예전엔 폭탄이 벽·그림만 지우고 기믹 목록엔 남아서 (예전 9층) 항아리가 안 보이는데 리치가 계속 무적인 일이 생겼다.</summary>
        private bool IsUnbreakableGimmickWall(Vector2Int p) => _lava.Contains(p) || _lightWalls.Contains(p) || _crushWalls.ContainsKey(p);

        /// <summary>6·7층 - 촛불·분신이 남아 있는 동안, 9층 - 죽음의 낙인이 있는 동안 보스는 무적(BossBrain.ArmorMultiplier). 10층 - 빛 단계·발악 동안.</summary>
        public bool BossInvulnerable =>
            (_gimmickStage == 6 && (_candles.Count > 0 || _soulTargetActive)) || (_gimmickStage == 7 && (_clones.Count > 0 || _beatActive)) || DoomActive
            || (MirrorPhase == 3 && _lightActive) || MirrorLastStand;

        /// <summary>졸개가 플레이어 대신 갈 곳 - 예전 4층 흡수 기믹용(10-16에 분열 기믹으로 바뀌어 지금은 없음).</summary>
        public Vector2Int? MinionTarget(EnemyActor minion) => null;

        /// <summary>졸개를 보스에게서 멀리 소환할지 - 예전 4층 흡수 기믹용(지금은 없음).</summary>
        private bool SpawnMinionsFar => false;

        // ------------------------------------------------------------------ 시작
        public void BeginStageGimmick(int stage, EnemyActor boss)
        {
            // 7층은 체력 10%마다 분신 물결이 다시 온다(BossBrain.CloneWaveHealth) - 두 번째부터는 지우지 않고 분신만 더한다
            var cloneAgain = stage == 7 && _gimmickStage == 7 && _gimmickBoss == boss;
            if (!cloneAgain)
                ClearStageGimmick();
            _gimmickStage = stage;
            _gimmickBoss = boss;
            _gimmickJustStarted = true;
            var c = new GimmickCutscene();
            switch (stage)
            {
                case 4:
                    BeginSlimeSplit(boss, c);
                    break;
                case 5:
                    _infectionClock = InfectionEveryTurns;
                    c.Steps.Add(() => { SpawnSporeFog(); return !DamagePopup.Suppressed; }); // 독안개가 천천히 덮는다(10-16 사용자)
                    c.StepDelays.Add(0.9f);
                    c.Steps.Add(PropStep(_mushrooms, SpawnMushroomAnywhere, "mushroom_sprout"));
                    break;
                case 6:
                    _gimmickTurnsLeft = CandleTurnLimit;
                    for (var i = 0; i < CandleCount; i++)
                        c.Steps.Add(PropStep(_candles, () =>
                        {
                            var before = _candles.Count;
                            SpawnFloorProps(_candles, before + 1, "Spirit_Candle", new Color(0.7f, 0.9f, 1f), boss, 3);
                            return _candles.Count > before;
                        }, "candle_light"));
                    break;
                case 7:
                    _gimmickTurnsLeft = CloneTurnLimit;
                    for (var i = 0; i < CloneCount; i++)
                        c.Steps.Add(EnemyStep(() => SpawnClones(boss, 1) > 0, "shadow_split"));
                    break;
                case 8:
                    BeginCrusher(c);
                    break;
                case 9:
                    BeginLichDoom(c);
                    break;
                case BossBrain.MirrorStage:
                    _gimmickTurnsLeft = MirrorFakeTurnLimit;
                    for (var i = 0; i < MirrorFakeCount; i++)
                        c.Steps.Add(EnemyStep(() => SpawnMirrorFakes(boss, 1) > 0, "morph"));
                    c.Steps.Add(() => ShuffleMirror(boss)); // 마지막에 진짜와 가짜 하나가 자리를 바꾼다 - 눈으로 따라가면 안 된다
                    break;
            }
            c.Finish = UpdateGimmickLabel;
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
            if (_soulTargetActive)
            {
                SnipeSoulTargets(tiles); // 영혼 과녁 - 덮이면 부서진다(다 부서지면 즉사)
                return;
            }
            var hit = tiles.Where(_candles.ContainsKey).ToList();
            foreach (var c in hit)
            {
                if (HitFeedback.Enabled && _candles[c] != null)
                    Fx.Play("candle_out", _candles[c].transform.position, 1.1f, fps: 12f, sortingOrder: 6); // 불꽃이 휘청이다 꺼지며 연기로(10-23)
                RemoveFloorProp(_candles, c, new Color(0.7f, 0.9f, 1f));
            }
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
                if (HitFeedback.Enabled)
                    Fx.Play("clone_vanish", c.transform.position, 2.4f, fps: 14f, sortingOrder: 6); // 두 조각으로 갈라져 연기로(10-23)
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

        /// <summary>10층 - 진짜의 예고 공격(어느 패턴이든)이 거울상 몸에 한 칸이라도 걸리면 그 거울상이 깨진다. 2단계면 거울 속 나에게 금이 간다.</summary>
        public void OnMirrorPatternTiles(IEnumerable<Vector2Int> tiles)
        {
            if (_gimmickStage != BossBrain.MirrorStage)
                return;
            var set = new HashSet<Vector2Int>(tiles);
            if (MirrorPhase == 2)
            {
                HitReflection(set);
                return;
            }
            if (_mirrorFakes.Count == 0)
                return;
            var hit = _mirrorFakes.Where(f => f != null && GridMap.Cells(f.GridPos, f.FootprintRadius).Any(set.Contains)).ToList();
            foreach (var f in hit)
            {
                _mirrorFakes.Remove(f);
                if (HitFeedback.Enabled)
                {
                    // 거울상이 그 모습 그대로 산산조각(10-19 사용자) - 그림이 없으면 예전 수정 깨짐
                    if (f.TryGetComponent<SpriteRenderer>(out var fakeRenderer) && fakeRenderer.sprite != null)
                    {
                        SpriteShatter.Spawn(fakeRenderer, pieceCells: 0.32f, force: 5f);
                        SfxPlayer.Play(Sfx.Crit);
                    }
                    else
                        Fx.Play("crystal_shatter", f.transform.position, 3.2f, fps: 14f, sortingOrder: 6);
                }
                DespawnEnemy(f, new Color(0.8f, 0.88f, 1f));
            }
            if (hit.Count == 0)
                return;
            ShowMessage(_mirrorFakes.Count > 0
                ? Loc.F("<color=#C8D8FF>기사의 공격이 거울상을 깨뜨렸다!</color> 남은 가짜 {0}", _mirrorFakes.Count)
                : Loc.T("<color=#C8D8FF>거울상이 모두 깨졌다!</color> 거울 가면 기사가 비틀거린다"));
            if (_mirrorFakes.Count == 0)
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
            if (_gimmickJustStarted)
            {
                _gimmickJustStarted = false;
                UpdateGimmickLabel();
                return false; // 방금 시작한 턴 - 제한 턴은 다음 몹 턴부터 깎는다
            }

            var dead = false;
            switch (_gimmickStage)
            {
                case 5:
                    TickCleanAir();
                    if (_mushrooms.ContainsKey(_player.GridPos))
                    {
                        RemoveFloorProp(_mushrooms, _player.GridPos, new Color(0.6f, 1f, 0.75f));
                        ShowMessage(_infection > 0
                            ? Loc.F("<color=#80FFB0>빛버섯을 먹었다!</color> 감염이 씻겨 나가고 둘레 공기가 맑아졌다({0}턴)", CleanAirTurns)
                            : Loc.F("<color=#80FFB0>빛버섯을 먹었다!</color> 둘레 공기가 맑아졌다({0}턴)", CleanAirTurns));
                        _infection = 0;
                        AddCleanAir(_player.GridPos); // 둘레 3x3 안전지대(10-16 사용자)
                    }
                    var breathed = false;
                    if (--_infectionClock <= 0)
                    {
                        _infectionClock = InfectionEveryTurns;
                        if (InCleanAir(_player.GridPos))
                            ShowMessage(Loc.T("<color=#80FFB0>맑은 공기 속이라 포자를 피했다</color>"));
                        else
                        {
                            breathed = true;
                            dead = AddInfection(Loc.T("독안개 속 포자를 들이마셨다!"));
                        }
                    }
                    UpdateSporeFog(breathed); // 감염되는 턴이 가까울수록 짙어지고, 감염되는 순간 번쩍
                    UpdateHallucination();    // 감염 중이면 화면 가장자리가 일렁인다
                    break;
                case 8:
                    dead = TickCrusher();
                    break;
                case BossBrain.MirrorStage:
                    dead = TickMirrorPhase(out var finished);
                    if (finished)
                        return true; // 발악을 버텨 기사가 쓰러졌다 - 처치 처리(방 전환)는 끝났다
                    break;
                case 9:
                    if (!DoomActive)
                        break; // 이미 옮겼다
                    if (TickDoomOrbs())
                        return true; // 구체에 맞아 죽었다 - 사망 처리까지 끝(기믹 즉사로 세지 않는다)
                    if (!DoomActive)
                        break; // 이번 턴에 옮겼다
                    if (--_doomTurnsLeft <= 0)
                    {
                        ShowMessage(Loc.T("<color=#FF4040>죽음의 낙인이 타올랐다!</color>"));
                        dead = true;
                    }
                    else if (System.Array.IndexOf(GimmickWarnAt, _doomTurnsLeft) >= 0)
                        ShowMessage(Loc.F("<color=#FF6060>낙인이 뜨겁게 달아오른다...</color> {0}턴!", _doomTurnsLeft));
                    break;
                case 6:
                case 7:
                    if (SoulTargetActive)
                    {
                        TickSoulTarget(); // 영혼 과녁 - 버티면 기절 + 피해 증가(실패 즉사는 저격 때)
                        break;
                    }
                    if (ShadowBeatActive)
                    {
                        dead = TickShadowBeat(); // 그림자 박자 - 빈 칸이 아니면 즉사
                        break;
                    }
                    if (_gimmickStage == 7)
                        _clones.RemoveAll(c => c == null || c.Stats.IsDead); // 칼날 말고 다른 길로 죽은 분신이 남아 무적이 안 풀리던 일 방지
                    if (!BossInvulnerable)
                        break; // 이미 다 없앴다
                    if (--_gimmickTurnsLeft <= 0)
                    {
                        ShowMessage(_gimmickStage switch
                        {
                            6 => Loc.T("<color=#FF4040>망령 궁수의 화살비가 쏟아졌다!</color>"),
                            _ => Loc.T("<color=#FF4040>그림자들이 한꺼번에 덮쳤다!</color>"),
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
            if (_gimmickStage == 5 && _infection == HallucinationInfection)
                ShowMessage(Loc.T("<color=#D9A0FF>환각 포자가 눈을 흐린다!</color> 예고 고리가 거꾸로 보인다(빨간 칸이 안전) - 빛버섯으로 해독"));
            if (_gimmickStage == 5 && _infection == ConfusionInfection)
                ShowMessage(Loc.T("<color=#D9A0FF>포자가 머릿속까지 번졌다!</color> 이제 방향키도 뒤집힌다 - 빛버섯을 서두르자"));
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
                    text = Loc.F("점액 {0}/{1}", _slimePuddles.Count, SlimePuddleLimit);
                    danger = _slimePuddles.Count >= SlimePuddleLimit - 4;
                    break;
                case 5:
                    text = Loc.F("감염 {0}/{1} · {2}턴", _infection, InfectionLimit, _infectionClock);
                    danger = _infection >= InfectionLimit - 1;
                    break;
                case 6 when SoulTargetActive:
                    text = SoulTargetLabelText(out danger);
                    break;
                case 6:
                    text = _candles.Count > 0 ? Loc.F("촛불 {0} · {1}턴", _candles.Count, _gimmickTurnsLeft)
                        : _soulTargetBonus > 0f ? Loc.F("받는 피해 +{0}%", Mathf.RoundToInt(_soulTargetBonus * 100f)) : null;
                    danger = _candles.Count > 0 && _gimmickTurnsLeft <= 20;
                    break;
                case 7 when ShadowBeatActive:
                    text = Loc.F("그림자 박자 {0}/{1}", _beatIndex + 1, ShadowBeatCount);
                    danger = true;
                    break;
                case 7:
                    text = _clones.Count > 0 ? Loc.F("분신 {0} · {1}턴", _clones.Count, _gimmickTurnsLeft) : null;
                    danger = _gimmickTurnsLeft <= 20;
                    break;
                case 8:
                    text = CrushActive ? CrushLabelText(out danger) : null;
                    break;
                case 9:
                    text = DoomActive ? Loc.F("낙인 {0}턴 · 흡수 {1}/{2}", _doomTurnsLeft, _doomAbsorbed, DoomAbsorbNeeded) : null;
                    danger = _doomTurnsLeft <= 10;
                    break;
                case BossBrain.MirrorStage when MirrorPhase >= 2:
                    text = MirrorLabelText(out danger);
                    break;
                case BossBrain.MirrorStage:
                    text = _mirrorFakes.Count > 0 ? Loc.F("거울상 {0} · {1}턴", _mirrorFakes.Count, _gimmickTurnsLeft) : null;
                    danger = _gimmickTurnsLeft <= 20;
                    // 같은 글자를 거울상 머리 위에도 - 진짜 위에만 뜨면 바로 들킨다
                    foreach (var f in _mirrorFakes)
                    {
                        if (f == null || text == null)
                            continue;
                        if (!_mirrorFakeLabels.TryGetValue(f, out var fl) || fl == null)
                            _mirrorFakeLabels[f] = fl = CreateBossLabel(f, "GimmickLabel");
                        fl.text = text;
                        fl.color = danger ? new Color(1f, 0.3f, 0.3f) : Color.white;
                    }
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

        /// <summary>10층 거울상 count개 - 진짜와 같은 3x3 몸, 플레이어에게서 3칸·진짜와 다른 거울상에게서 몇 칸 떨어진 자리(무작위). 만든 개수.</summary>
        private int SpawnMirrorFakes(EnemyActor boss, int count)
        {
            var made = 0;
            for (var n = 0; n < count; n++)
            {
                var go = new GameObject("MirrorFake");
                go.transform.SetParent(transform, false);
                var fake = go.AddComponent<EnemyActor>();
                fake.Initialize(boss.Stats.MaxHealth, 0f, isBoss: false, kind: EnemyKind.Melee); // 공격은 시늉만(EnemyActor.TakeMeleeTurn)
                fake.MarkAsMinion();          // 보상 없음, 진짜가 죽으면 같이 사라짐
                fake.MarkAsMirrorFake(boss);  // 3x3 몸 + 진짜 그림을 좌우 뒤집어 베낌 + 칼이 안 통함
                fake.Stats.CurrentHealth = boss.Stats.CurrentHealth; // 체력바를 봐도 같게
                bool Fits(Vector2Int c, int fromBoss, int fromFake) =>
                    Map.CanOccupy(fake, c) && GridMap.FootprintChebyshev(boss, c) >= fromBoss
                    && GridMap.Cells(c, 1).All(p => p != ExitPosition && Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) >= 3)
                    && _mirrorFakes.All(f => Mathf.Max(Mathf.Abs(f.GridPos.x - c.x), Mathf.Abs(f.GridPos.y - c.y)) >= fromFake);
                var cells = new List<Vector2Int>();
                for (var x = 0; x < Map.Width; x++)
                for (var y = 0; y < Map.Height; y++)
                    cells.Add(new Vector2Int(x, y));
                var shuffled = cells.OrderBy(_ => Rng.Next()).ToList();
                Vector2Int? spot = shuffled.Where(c => Fits(c, 3, 4)).Select(c => (Vector2Int?)c).FirstOrDefault()
                                   ?? shuffled.Where(c => Fits(c, 2, 3)).Select(c => (Vector2Int?)c).FirstOrDefault(); // 좁은 방이면 조금 가깝게
                if (!spot.HasValue)
                {
                    Destroy(go);
                    continue;
                }
                Map.PlaceActor(fake, spot.Value);
                fake.SyncTransform();
                HitFeedback.OnSummon(fake.transform.position);
                _enemies.Add(fake);
                _mirrorFakes.Add(fake);
                made++;
            }
            return made;
        }

        /// <summary>10층 컷신 끝 - 진짜와 거울상 하나가 자리를 바꾼다(번쩍이는 사이에). 바꿨으면 true.</summary>
        private bool ShuffleMirror(EnemyActor boss)
        {
            var fakes = _mirrorFakes.Where(f => f != null && !f.Stats.IsDead).ToList();
            if (boss == null || boss.Stats.IsDead || fakes.Count == 0)
                return false;
            var other = fakes[Rng.Next(fakes.Count)];
            var a = boss.GridPos;
            var b = other.GridPos;
            Map.RemoveActor(boss);
            Map.RemoveActor(other);
            Map.PlaceActor(boss, b);
            Map.PlaceActor(other, a);
            boss.SyncTransform();
            other.SyncTransform();
            if (boss.TryGetComponent<SpriteAnimator>(out var anim))
                anim.SnapToGrid();
            if (HitFeedback.Enabled)
            {
                ActorJuice.Get(boss).Flash(Color.white);
                ActorJuice.Get(other).Flash(Color.white);
                SfxPlayer.Play(Sfx.ChestOpen);
            }
            return true;
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

        /// <summary>보상 없이 몹 하나를 치운다(터짐·분신 베임) - 방이 비었는지는 보지 않는다(보스가 살아 있을 때만 쓴다).</summary>
        private void DespawnEnemy(EnemyActor e, Color burst)
        {
            if (e == null)
                return;
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(e.transform.position, burst, 14, GridConstants.CellSize * 1.6f, GridConstants.CellSize * 0.14f);
            e.Stats.TakeDamage(e.Stats.CurrentHealth + 1f);
            ClearBombTelegraph(e);
            Map.RemoveActor(e);
            _enemies.Remove(e);
            Destroy(e.gameObject);
        }

        /// <summary>방을 새로 깔 때·보스가 죽었을 때.</summary>
        private void ClearStageGimmick()
        {
            _gimmickStage = 0;
            _gimmickBoss = null;
            _gimmickTurnsLeft = 0;
            _infection = 0;
            _infectionClock = 0;
            foreach (var go in _mushrooms.Values.Concat(_candles.Values))
                if (go != null)
                    Destroy(go);
            _mushrooms.Clear();
            _candles.Clear();
            _clones.Clear();
            _mirrorFakes.Clear();
            _mirrorFakeLabels.Clear();
            ClearMirrorPhases();
            ClearSlimeKing();
            ClearCrusher();
            ClearSporeFog();
            _lava.Clear();
            ClearDoom();
            ClearSoulTarget();
            ClearShadowBeat();
            if (_gimmickLabel != null)
                Destroy(_gimmickLabel.gameObject);
            _gimmickLabel = null;
        }
    }
}
