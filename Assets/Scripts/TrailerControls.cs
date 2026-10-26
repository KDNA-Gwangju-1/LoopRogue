using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LoopRogue
{
    /// <summary>트레일러 장면 신호 - 게임 코드 곳곳(보스 패턴·기믹)이 TrailerControls.Cue로 알린다. 트레일러가 아니면 아무 일도 안 한다.</summary>
    public enum TrailerCue
    {
        MobsCleared,  // 잡몹 방을 비웠다
        BoarCrash,    // 1층 멧돼지가 기둥에 박았다
        HoleFilled,   // 2층 오우거 구멍을 메웠다
        TripleWave,   // 2층 오우거 3색 파동 한 물결
        CrystalBlast, // 3층 골렘 수정 폭발
        TurretWave,   // 4층 벽 슬라임 파동 발사
        Mushroom,     // 5층 빛버섯을 먹었다
        Candle,       // 6층 촛불이 꺼졌다
        Pattern,      // 예고 패턴이 터졌다(7층은 이걸 3번)
        WallBreak,    // 8층 도끼 충격파가 벽을 부쉈다
        DoomOrbs,     // 9층 영혼 구체를 리치에게 먹였다
        MirrorFakes,  // 10층 거울상을 다 깼다(트레일러 장면 전환엔 안 씀 - 10층은 죽어서 끝)
    }

    /// <summary>트레일러 촬영 모드(BossRush.Trailer, 10-26) - 짜인 순서대로 장면이 넘어간다(사용자 콘티):
    /// 튜토리얼 7방 → 1층 멧돼지 돌진이 기둥에 박힘 → 2층 오우거 구멍을 메우고 3색 파동 피하기 → 3층 골렘 수정 폭발 → 4층 벽 슬라임 파동 → 5층 빛버섯 먹기
    /// → 6층 촛불 하나 끄기 → 7층 암살자 패턴 3번 피하기 → 8층 미노타우로스가 벽 부수기 → 9층 영혼 구체를 리치에게 먹이기
    /// → 10층은 무적이 풀려 보스에게 죽고 사망 연출로 끝. 장면이 나오면 NextDelay초 뒤 다음 방으로.
    /// 키: F9 = 지금 방 건너뛰기 / F10 = 화면 UI 전부 숨기기·보이기 / F8 = 느리게(1 → 0.5 → 0.25배).</summary>
    public class TrailerControls : MonoBehaviour
    {
        private const float NextDelay = 0.5f;   // 장면이 나온 뒤 다음 방까지(10-26 사용자 1.6초 → 0.5초)
        private const int AssassinPatterns = 3;
        private const int OgreWaves = 3;         // 3색 파동 = 물결 3번 - 구멍을 메운 뒤 이만큼 연달아 안 맞고 피해야(10-26 사용자)

        private static readonly float[] Speeds = { 1f, 0.5f, 0.25f };
        private static TrailerControls _instance;
        private bool _hideUi;
        private int _speed;
        private int _count;    // 이 방에서 받은 신호 수(7층 패턴·2층 피한 물결)
        private bool _holeFilled; // 2층 - 구멍을 하나라도 메웠나
        private bool _advancing;

        public static void Create()
        {
            var go = new GameObject("TrailerControls");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TrailerControls>();
            Debug.Log("[트레일러] 장면이 나오면 자동으로 다음 층 · F9 다음 방 · F10 UI 숨기기 · F8 느리게");
        }

        /// <summary>방이 새로 깔릴 때(RoomController) - 신호 수를 처음부터.</summary>
        public static void OnRoomLoaded()
        {
            if (_instance == null)
                return;
            _instance._count = 0;
            _instance._holeFilled = false;
            _instance._advancing = false;
        }

        /// <param name="playerHit">예고 패턴에 맞았는지(2층 - 맞으면 피한 물결 수를 처음부터)</param>
        public static void Cue(TrailerCue cue, bool playerHit = false)
        {
            if (!BossRush.Trailer || _instance == null || _instance._advancing)
                return;
            var room = FindFirstObjectByType<RoomController>();
            if (room == null)
                return;
            var stage = room.IsBossRoom ? room.Stage : 0;
            var wanted = stage switch
            {
                0 => cue == TrailerCue.MobsCleared,
                1 => cue == TrailerCue.BoarCrash,
                2 => _instance.OgreStep(cue, playerHit),
                3 => cue == TrailerCue.CrystalBlast,
                4 => cue == TrailerCue.TurretWave,
                5 => cue == TrailerCue.Mushroom,
                6 => cue == TrailerCue.Candle,
                7 => (cue == TrailerCue.Pattern || cue == TrailerCue.TripleWave) && ++_instance._count >= AssassinPatterns,
                8 => cue == TrailerCue.WallBreak,
                9 => cue == TrailerCue.DoomOrbs,
                _ => false, // 10층 - 넘어가지 않는다(보스에게 죽어 사망 연출로 끝)
            };
            if (!wanted)
                return;
            _instance._advancing = true;
            _instance.StartCoroutine(_instance.NextAfterDelay(room));
        }

        /// <summary>2층 - 구멍을 메운 다음 3색 파동 세 물결을 연달아 안 맞고 피하면 장면 성공.</summary>
        private bool OgreStep(TrailerCue cue, bool playerHit)
        {
            if (cue == TrailerCue.HoleFilled)
                _holeFilled = true;
            if (cue != TrailerCue.TripleWave || !_holeFilled)
                return false;
            _count = playerHit ? 0 : _count + 1;
            return _count >= OgreWaves;
        }

        private IEnumerator NextAfterDelay(RoomController room)
        {
            yield return new WaitForSeconds(NextDelay);
            // 컷신(입력 잠금) 중이면 끝날 때까지 기다렸다 넘긴다
            while (room != null && room.IsInputLocked)
                yield return null;
            if (room != null && room.Loop != null && !room.Loop.IsLastRoom)
                room.Loop.AdvanceToNextRoom();
        }

        private void Update()
        {
            if (GameInput.Down(Key.F9))
                SkipRoom();
            if (GameInput.Down(Key.F10))
            {
                _hideUi = !_hideUi;
                SetCanvases(!_hideUi);
            }
            else if (_hideUi)
                SetCanvases(false);
            if (GameInput.Down(Key.F8))
            {
                _speed = (_speed + 1) % Speeds.Length;
                Time.timeScale = Speeds[_speed];
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (_instance == this)
                _instance = null;
        }

        private static void SkipRoom()
        {
            var room = FindFirstObjectByType<RoomController>();
            if (room == null || room.Loop == null || room.IsInputLocked || room.Loop.IsLastRoom)
                return;
            room.Loop.AdvanceToNextRoom();
        }

        private static void SetCanvases(bool visible)
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.enabled != visible)
                    canvas.enabled = visible;
        }
    }
}
