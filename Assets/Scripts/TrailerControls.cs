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
        TripleWave,   // 2층 오우거 3색 파동 한 물결
        CrystalBlast, // 3층 골렘 수정 폭발
        TurretWave,   // 4층 벽 슬라임 파동 발사
        Mushroom,     // 5층 빛버섯을 먹었다
        Candle,       // 6층 촛불이 꺼졌다
        Pattern,      // 예고 패턴이 터졌다(7층은 이걸 3번)
        WallBreak,    // 8층 도끼 충격파가 벽을 부쉈다
        DoomOrbs,     // 9층 영혼 구체 소환
        MirrorFakes,  // 10층 거울상을 다 깼다
    }

    /// <summary>트레일러 촬영 모드(BossRush.Trailer, 10-26) - 짜인 순서대로 장면이 넘어간다(사용자 콘티):
    /// 잡몹 4마리 → 1층 멧돼지 돌진이 기둥에 박힘 → 2층 오우거 3색 파동 → 3층 골렘 수정 폭발 → 4층 벽 슬라임 파동 → 5층 빛버섯 먹기
    /// → 6층 촛불 하나 끄기 → 7층 암살자 패턴 3번 피하기 → 8층 미노타우로스가 벽 부수기 → 9층 리치 영혼 구체 소환
    /// → 10층 거울상 다 깨면 기사가 산산조각(끝). 장면이 나오면 NextDelay초 뒤 다음 방으로.
    /// 키: F9 = 지금 방 건너뛰기 / F10 = 화면 UI 전부 숨기기·보이기 / F8 = 느리게(1 → 0.5 → 0.25배).</summary>
    public class TrailerControls : MonoBehaviour
    {
        private const float NextDelay = 1.6f;   // 장면이 나온 뒤 다음 방까지(그 장면 연출을 다 보여 주게)
        private const float FinaleDelay = 0.4f; // 10층 - 거울상이 다 깨지고 기사가 쓰러지기까지
        private const int AssassinPatterns = 3;
        private const int OgreWaves = 3;         // 3색 파동 = 물결 3번

        private static readonly float[] Speeds = { 1f, 0.5f, 0.25f };
        private static TrailerControls _instance;
        private bool _hideUi;
        private int _speed;
        private int _count;    // 이 방에서 받은 신호 수(7층 패턴·2층 물결)
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
            _instance._advancing = false;
        }

        public static void Cue(TrailerCue cue)
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
                2 => cue == TrailerCue.TripleWave && ++_instance._count >= OgreWaves,
                3 => cue == TrailerCue.CrystalBlast,
                4 => cue == TrailerCue.TurretWave,
                5 => cue == TrailerCue.Mushroom,
                6 => cue == TrailerCue.Candle,
                7 => (cue == TrailerCue.Pattern || cue == TrailerCue.TripleWave) && ++_instance._count >= AssassinPatterns,
                8 => cue == TrailerCue.WallBreak,
                9 => cue == TrailerCue.DoomOrbs,
                _ => cue == TrailerCue.MirrorFakes,
            };
            if (!wanted)
                return;
            _instance._advancing = true;
            _instance.StartCoroutine(stage >= BossBrain.MirrorStage ? _instance.Finale(room) : _instance.NextAfterDelay(room));
        }

        private IEnumerator NextAfterDelay(RoomController room)
        {
            yield return new WaitForSeconds(NextDelay);
            // 컷신(입력 잠금) 중이면 끝날 때까지 기다렸다 넘긴다 - 9층은 구체가 컷신 도중에 나온다
            while (room != null && room.IsInputLocked)
                yield return null;
            if (room != null && room.Loop != null && !room.Loop.IsLastRoom)
                room.Loop.AdvanceToNextRoom();
        }

        private IEnumerator Finale(RoomController room)
        {
            yield return new WaitForSeconds(FinaleDelay);
            if (room != null)
                room.TrailerFinishMirrorKnight();
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
