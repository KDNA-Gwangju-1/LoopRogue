using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>화면 위 기믹 게이지 한 줄 - Fill이 1에 가까울수록 "곧 터진다"(남은 턴 게이지는 차오르고, 쌓이는 수는 한도 대비).
    /// Buff면 반대로 남은 만큼(줄어드는 이로운 효과). 화면엔 글자 없이 아이콘 + 막대만(10-25 사용자) - Text는 봇·디버그용.
    /// Steps가 있으면 막대를 그만큼 칸으로 나눠 한 턴(한 개)마다 한 칸씩 찬다.</summary>
    public readonly struct HudTimer
    {
        public readonly string Text;
        public readonly float Fill;
        public readonly bool Danger;
        public readonly bool Buff;
        public readonly string Icon; // UI/Icons/Gimmick_{Icon}
        public readonly int Steps;   // 0 = 칸 없음(긴 시계)

        public HudTimer(string icon, string text, float fill, bool danger, bool buff = false, int steps = 0)
        {
            Steps = steps;
            Icon = icon;
            Text = text;
            Fill = Mathf.Clamp01(fill);
            Danger = danger;
            Buff = buff;
        }
    }

    public partial class RoomController
    {
        // 보스 머리 위 글자는 멀리 있으면 화면 밖이라 안 보인다(10-25 사용자 "5층 12턴마다 포자 감염 같은 걸 게이지로") -
        // 층마다 돌아가는 시계·쌓이는 수를 GameHUD가 매 프레임 이 목록으로 받아 타겟 체력바 아래에 막대로 그린다.
        private readonly List<HudTimer> _hudTimers = new List<HudTimer>();
        // 남은 턴 게이지의 "가득" 기준 - 처음 본 값(또는 다시 늘어난 값)을 최대로 잡는다. 시작 턴이 경우마다 다른 것(빛 거울·벽 조임)도 그대로 맞는다.
        private readonly Dictionary<string, (int Last, int Max)> _hudClockMax = new Dictionary<string, (int, int)>();
        private readonly HashSet<string> _hudClockSeen = new HashSet<string>();
        private const int MaxSteps = 20; // 이보다 긴 시계는 칸을 나누면 너무 촘촘해서 그냥 매끈한 막대

        public IReadOnlyList<HudTimer> HudTimers()
        {
            _hudTimers.Clear();
            _hudClockSeen.Clear();
            if (!_roomCleared && Map != null)
                CollectHudTimers();
            if (_hudClockMax.Count > _hudClockSeen.Count)
                foreach (var key in _hudClockMax.Keys.Where(k => !_hudClockSeen.Contains(k)).ToList())
                    _hudClockMax.Remove(key);
            return _hudTimers;
        }

        private void CollectHudTimers()
        {
            var bossAlive = _enemies.Any(e => e != null && e.IsBoss && !e.Stats.IsDead);
            if (!bossAlive)
                return;

            // 2층 오우거 - 구멍이 다 차면 바닥 붕괴, 메워서 얻는 기세
            if (_ogreHoles.Count > 0)
                AddCount("Hole", Loc.F("구멍 {0}/{1} · 다 차면 바닥 붕괴", _ogreHoles.Count, OgreHoleCollapseCount), _ogreHoles.Count, OgreHoleCollapseCount, 2);
            if (_ogreMightStacks > 0)
                _hudTimers.Add(new HudTimer("Might", Loc.F("기세 +{0:0}% ({1}턴)", (OgreMightMultiplier - 1f) * 100f, _ogreMightTurns),
                    _ogreMightTurns / (float)OgreMightTurns, false, buff: true, steps: OgreMightTurns));

            // 1층 멧돼지 - 기둥을 다 부수기 전에 시간이 끝나면 광폭
            if (BoarTurnsLeft > 0)
                AddClock("boar", "Pillar", Loc.F("기둥 {0}개 · 광폭까지 {1}턴", _boarPillars.Count, _boarTurnsLeft), _boarTurnsLeft, 20);

            // 3층 골렘 - 가장 먼저 터질 수정
            if (_crystalFuse.Count > 0)
            {
                var soonest = _crystalFuse.Values.Min();
                AddClock("crystal", "Crystal", Loc.F("수정 폭발까지 {0}턴 · 수정 {1}개", soonest, _crystalFuse.Count), soonest, CrystalWarnTurns + 3);
            }

            if (_gimmickStage == 0 || _gimmickBoss == null || _gimmickBoss.Stats.IsDead)
                return;
            switch (_gimmickStage)
            {
                case 4:
                    AddCount("Slime", Loc.F("점액 {0}/{1} · 다 차면 즉사", _slimePuddles.Count, SlimePuddleLimit), _slimePuddles.Count, SlimePuddleLimit, 4);
                    if (_slimeTurret != null && !_slimeTurret.Stats.IsDead)
                    {
                        if (_turretPending != null)
                            _hudTimers.Add(new HudTimer("SlimeWave", Loc.T("벽 슬라임 파동 발사!"), 1f, true));
                        else if (_turretCooldown > 0)
                            _hudTimers.Add(new HudTimer("SlimeWave", Loc.F("벽 슬라임 파동 준비 {0}턴", _turretCooldown),
                                1f - _turretCooldown / (float)(SlimeTurretCooldown + 1), false, steps: SlimeTurretCooldown + 1));
                        else
                            _hudTimers.Add(new HudTimer("SlimeWave", Loc.T("벽 슬라임 파동 조준 중"), 1f, true));
                    }
                    break;
                case 5:
                    AddClock("spore", "Spore", Loc.F("포자 감염까지 {0}턴 · 감염 {1}/{2}", _infectionClock, _infection, InfectionLimit), _infectionClock, 3,
                        InfectionEveryTurns);
                    AddCount("Infect", Loc.F("감염 {0}/{1}", _infection, InfectionLimit), _infection, InfectionLimit, 1); // 10-26 사용자 - 감염 상태 따로
                    break;
                case 6 when SoulTargetActive:
                    AddClock("soul", "Target", Loc.F("과녁 {0} · {1}턴", _soulTargets.Count, _soulTargetTurnsLeft), _soulTargetTurnsLeft, 10, SoulTargetTurns);
                    break;
                case 6 when _candles.Count > 0:
                    AddClock("candle", "Candle", Loc.F("촛불 {0} · {1}턴", _candles.Count, _gimmickTurnsLeft), _gimmickTurnsLeft, 20, CandleTurnLimit);
                    break;
                case 7 when ShadowBeatActive:
                    _hudTimers.Add(new HudTimer("Beat", Loc.F("그림자 박자 {0}/{1}", _beatIndex + 1, ShadowBeatCount), (_beatIndex + 1) / (float)ShadowBeatCount, true, steps: ShadowBeatCount));
                    break;
                case 7 when _clones.Count > 0:
                    AddClock("clone", "Clone", Loc.F("분신 {0} · {1}턴", _clones.Count, _gimmickTurnsLeft), _gimmickTurnsLeft, 10, CloneTurnLimit);
                    break;
                case 8 when CrushActive:
                    AddClock("crush", "Wall", Loc.F("벽 {0}턴 · 방 {1}x{2}", _crushCountdown, CrushInnerWidth, CrushInnerHeight), _crushCountdown, CrushWarnTurns);
                    break;
                case 9 when DoomActive:
                    AddClock("doom", "Doom", Loc.F("낙인 {0}턴 · 흡수 {1}/{2}", _doomTurnsLeft, _doomAbsorbed, DoomAbsorbNeeded), _doomTurnsLeft, 10, DoomTurns);
                    break;
                case BossBrain.MirrorStage when MirrorPhase == 4 && MirrorPhaseActive:
                    _hudTimers.Add(new HudTimer("Hourglass", Loc.F("발악 {0:0.0}초", Mathf.Max(0f, _lastStandSeconds)),
                        1f - _lastStandSeconds / LastStandSeconds, true));
                    break;
                case BossBrain.MirrorStage when MirrorPhase == 2 && MirrorPhaseActive:
                    AddClock("mirror", "Mirror", Loc.F("거울 속 나 {0}/{1} · {2}턴", _reflectionHitCount, ReflectionHits, _gimmickTurnsLeft), _gimmickTurnsLeft, 20);
                    break;
                case BossBrain.MirrorStage when MirrorPhase == 3 && MirrorPhaseActive:
                    AddClock("mirror", "Light", Loc.F("단단함 · 빛 {0}턴", _gimmickTurnsLeft), _gimmickTurnsLeft, 20);
                    break;
                case BossBrain.MirrorStage when MirrorPhase < 2 && _mirrorFakes.Count > 0:
                    AddClock("mirror", "Mirror", Loc.F("거울상 {0} · {1}턴", _mirrorFakes.Count, _gimmickTurnsLeft), _gimmickTurnsLeft, 20, MirrorFakeTurnLimit);
                    break;
            }
        }

        /// <summary>쌓이는 수(구멍·점액) - 한도에서 dangerGap 안쪽이면 빨갛게.</summary>
        private void AddCount(string icon, string text, int count, int limit, int dangerGap)
        {
            _hudTimers.Add(new HudTimer(icon, text, count / (float)Mathf.Max(1, limit), count >= limit - dangerGap, steps: limit));
        }

        /// <summary>남은 턴 - 0에 가까울수록 막대가 찬다. max를 모르면(0) 처음 본 값을 가득으로. 짧은 시계(MaxSteps턴 이하)는 턴마다 한 칸.</summary>
        private void AddClock(string key, string icon, string text, int left, int dangerAt, int max = 0)
        {
            _hudClockSeen.Add(key);
            if (!_hudClockMax.TryGetValue(key, out var seen) || left > seen.Last)
                seen.Max = Mathf.Max(left, max);
            seen.Last = left;
            seen.Max = Mathf.Max(seen.Max, left, 1);
            _hudClockMax[key] = seen;
            _hudTimers.Add(new HudTimer(icon, text, 1f - left / (float)seen.Max, left <= dangerAt, steps: seen.Max <= MaxSteps ? seen.Max : 0));
        }
    }
}
