using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>보스 기믹 컷신 공용(10-14) - 1층 멧돼지 기둥 컷신을 일반화. 입력 잠금 → 대사·흔들림 → (화면 어둡게 → 위치 옮기기) →
    /// 소품·몹을 하나씩 등장(이펙트) → 안내 → 입력 풀기. 봇 실행 중엔 연출 없이 모든 단계를 바로 실행한다.</summary>
    public partial class RoomController
    {
        public sealed class GimmickCutscene
        {
            public string Line;                 // 시작 대사(ShowMessage)
            public bool Fade;                   // 화면을 어둡게 해서 그 사이에 Relocate
            public Action Relocate;
            public readonly List<Func<bool>> Steps = new List<Func<bool>>(); // 하나씩 등장 - 뭔가 나왔으면 true(그때만 쉬어 간다)
            public float StepDelay = 0.32f;
            public readonly List<float> StepDelays = new List<float>(); // 단계별 기다림(없거나 짧으면 StepDelay) - 연출 클립을 다 보여 줄 때
            public Action Finish;               // 다 나온 뒤(안내 문구·타이머 시작)
            public GridActor Focus;             // 컷신 동안 카메라가 비출 대상 - 없으면 살아 있는 보스
        }

        private const float AppearFxSeconds = 8f / 16f; // 등장 이펙트 8프레임 × 16fps

        public void PlayGimmickCutscene(GimmickCutscene c)
        {
            if (DamagePopup.Suppressed)
            {
                if (c.Line != null)
                    ShowMessage(c.Line);
                c.Relocate?.Invoke();
                foreach (var step in c.Steps)
                    step();
                c.Finish?.Invoke();
                return;
            }
            StartCoroutine(GimmickCutsceneRoutine(c));
        }

        /// <summary>기믹 컷신이 돌고 있다(소품·거울상이 아직 다 안 나왔다) - 10층 단계 확인이 "벌써 끝났다"로 착각하지 않게.</summary>
        private bool _gimmickCutscenePlaying;

        private IEnumerator GimmickCutsceneRoutine(GimmickCutscene c)
        {
            _gimmickCutscenePlaying = true;
            IsInputLocked = true;
            ClearAllTelegraphs();
            CameraShake.Focus(c.Focus != null ? c.Focus : _enemies.FirstOrDefault(e => e != null && e.IsBoss && !e.Stats.IsDead)); // 10-14 사용자: 컷신 동안 보스를 비추게
            if (c.Line != null)
                ShowMessage(c.Line);
            CameraShake.Shake(0.35f, 0.6f);
            SfxPlayer.Play(Sfx.Slam);
            yield return new WaitForSeconds(0.5f);
            if (c.Fade)
            {
                ScreenFlash.Play(Color.black, 1f, 1.2f); // 0.18초쯤 가장 어두움 → 천천히 밝아짐
                yield return new WaitForSeconds(0.18f);
                c.Relocate?.Invoke();
                yield return new WaitForSeconds(0.55f);
            }
            else
                c.Relocate?.Invoke();
            for (var i = 0; i < c.Steps.Count; i++)
            {
                if (_player == null || _player.Stats.IsDead)
                    break;
                if (!c.Steps[i]())
                    continue;
                CameraShake.Shake(0.15f, 0.3f);
                SfxPlayer.Play(Sfx.Slam);
                yield return new WaitForSeconds(i < c.StepDelays.Count && c.StepDelays[i] > 0f ? c.StepDelays[i] : c.StepDelay);
            }
            c.Finish?.Invoke();
            _gimmickCutscenePlaying = false;
            yield return new WaitForSeconds(0.35f);
            CameraShake.Focus(null);
            IsInputLocked = false;
        }

        // ------------------------------------------------------------------ 하나씩 등장시키는 단계 도우미
        /// <summary>벽 소품(바위·수정·용암·항아리 등) - spawn 뒤 새로 생긴 벽 오브젝트에 등장 이펙트(이펙트가 도는 동안 소품은 숨김).</summary>
        private Func<bool> WallStep(Func<bool> spawn, string fx) => () =>
        {
            var before = new HashSet<Vector2Int>(_wallObjects.Keys);
            if (!spawn())
                return false;
            var added = _wallObjects.Keys.Where(k => !before.Contains(k)).ToList();
            foreach (var p in added)
                Appear(_wallObjects[p], fx, hide: true);
            FocusOn(added.Select(p => _wallObjects[p])); // 새로 나온 자리를 비춘다
            return true;
        };

        /// <summary>바닥 소품(촛불·빛버섯) - into에 새로 생긴 것에 등장 이펙트.</summary>
        private Func<bool> PropStep(Dictionary<Vector2Int, GameObject> into, Func<bool> spawn, string fx) => () =>
        {
            var before = new HashSet<Vector2Int>(into.Keys);
            if (!spawn())
                return false;
            var added = into.Keys.Where(k => !before.Contains(k)).ToList();
            foreach (var p in added)
                Appear(into[p], fx, hide: true);
            FocusOn(added.Select(p => into[p]));
            return true;
        };

        /// <summary>몹(졸개·분신) - 새로 생긴 몹 위에 이펙트(몹은 그대로 보임).</summary>
        private Func<bool> EnemyStep(Func<bool> spawn, string fx) => () =>
        {
            var before = new HashSet<EnemyActor>(_enemies);
            if (!spawn())
                return false;
            var added = _enemies.Where(x => x != null && !before.Contains(x)).ToList();
            if (fx != null)
                foreach (var e in added)
                    Appear(e.gameObject, fx, hide: false);
            FocusOn(added.Select(e => e.gameObject));
            return true;
        };

        /// <summary>컷신에서 하나씩 나타날 때 카메라가 그 자리를 비춘다(여럿이면 가운데) - 봇 실행 중엔 아무것도 안 한다.</summary>
        private static void FocusOn(IEnumerable<GameObject> objects)
        {
            if (DamagePopup.Suppressed)
                return;
            var list = objects.Where(o => o != null).ToList();
            if (list.Count == 0)
                return;
            var sum = Vector3.zero;
            foreach (var o in list)
                sum += o.transform.position;
            CameraShake.FocusPoint(sum / list.Count);
        }

        private void Appear(GameObject go, string fx, bool hide)
        {
            if (go == null || fx == null || DamagePopup.Suppressed)
                return;
            var shown = Fx.Play(fx, go.transform.position, 1f, fps: 16f, sortingOrder: hide ? -6 : 9);
            if (shown == null || !hide)
                return;
            var renderers = go.GetComponentsInChildren<SpriteRenderer>();
            foreach (var r in renderers)
                r.enabled = false;
            StartCoroutine(ShowAfter(renderers, AppearFxSeconds));
        }

        private static IEnumerator ShowAfter(SpriteRenderer[] renderers, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            foreach (var r in renderers)
                if (r != null)
                    r.enabled = true;
        }

        // ------------------------------------------------------------------ 층별 컷신
        /// <summary>2층 오우거 - 구멍 단계 시작: 몽둥이로 내리치며 첫 구멍 1개.</summary>
        public void StartOgreHolePhase(EnemyActor ogre)
        {
            var c = new GimmickCutscene { Line = Loc.T("<color=#FFB060>오우거가 분노해 몽둥이로 땅을 내리친다!</color> 이제 강타마다 던전이 흔들려 바닥에 구멍이 뚫린다") };
            c.Steps.Add(() => { var before = _ogreHoles.Count; OpenOgreHoles(ogre, 1); return _ogreHoles.Count > before; }); // 첫 구멍 1개(10-14 사용자: 두 개씩은 많다)
            c.Finish = () => ShowMessage(Loc.F("구멍에 붙어 그쪽으로 움직이면 메울 수 있다 - <color=#FF6060>구멍이 {0}개가 되면 바닥이 무너진다</color>", OgreHoleCollapseCount));
            PlayGimmickCutscene(c);
        }

        /// <summary>3층 골렘 - 물결(한 번에 여러 물결을 지났으면 합쳐서): 바위가 하나씩 떨어지고, 수정이 하나씩 솟는다.</summary>
        /// <param name="crystals">수정마다 (폭발까지 턴, 물결 번호)</param>
        /// <param name="rubbleWaves">바위마다 물결 번호 - 수정이 터지면 같은 물결 바위만 깨진다</param>
        public void StartGolemWave(EnemyActor golem, IReadOnlyList<(int Fuse, int Wave)> crystals, IReadOnlyList<int> rubbleWaves, bool firstWave)
        {
            var c = new GimmickCutscene
            {
                Line = Loc.F("<color=#7FE0D8>골렘이 땅을 울리자 수정 {0}개가 솟고 천장에서 바위가 떨어졌다!</color>", crystals.Count),
                StepDelay = 0.34f, // 0.22 → 0.34(10-16 사용자 "생길 때마다 그 자리를 비추게" - 카메라가 따라갈 틈)
            };
            foreach (var wave in rubbleWaves)
                c.Steps.Add(WallStep(() => DropGolemRubbleAround(golem, 1, wave) > 0, "rubble_fall"));
            foreach (var crystal in crystals)
                c.Steps.Add(WallStep(() => SpawnGolemCrystals(golem, _golemCrystals.Count + 1, crystal.Fuse, crystal.Wave) > 0, "crystal_rise"));
            c.Finish = () =>
            {
                if (firstWave && crystals.Count > 0)
                    ShowMessage(Loc.F("수정은 {0}턴 뒤 방 전체를 날려 버린다 - <color=#FF6060>붙어서 깨거나, 바위 뒤에 숨자</color> (바위는 폭발에 깨진다)", crystals[0].Fuse));
            };
            PlayGimmickCutscene(c);
        }
    }
}
