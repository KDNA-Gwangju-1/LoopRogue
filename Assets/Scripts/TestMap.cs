using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LoopRogue
{
    /// <summary>테스트 맵(에디터 메뉴 "LoopRogue/테스트 맵") - 지금 층의 방 대신, 벽 없는 넓은 방 하나에 방 이벤트 11종(미믹 포함)을 전부 깔고
    /// 약한 몹 3마리(근접·궁수·방패병)를 둔다. 몹을 다 잡으면 출구가 열리고, 출구로 가면 지금 층 보스방.
    /// [R] 테스트 방을 처음 상태로 다시 깐다(이벤트 전부 다시 + 체력 회복). 저장은 메뉴가 시작 전에 백업했다가 Play를 멈추면 되돌린다.</summary>
    public class TestMap : MonoBehaviour
    {
        public static bool Active { get; private set; }

        private const int Size = 15;
        private const float EnemyHealth = 6f;
        private const float EnemyAttack = 1f;

        /// <summary>플레이어(가운데)를 둘러싼 이벤트 자리 - 서로 2칸씩 떨어져서 하나씩 밟아볼 수 있게.</summary>
        private static readonly (Vector2Int Pos, RoomEventType Type)[] EventSpots =
        {
            (new Vector2Int(3, 11), RoomEventType.TreasureChest),
            (new Vector2Int(6, 11), RoomEventType.HealingSpring),
            (new Vector2Int(9, 11), RoomEventType.BlessingAltar),
            (new Vector2Int(12, 11), RoomEventType.CursedChest),
            (new Vector2Int(3, 8), RoomEventType.Mimic),
            (new Vector2Int(12, 8), RoomEventType.BloodAltar),
            (new Vector2Int(3, 5), RoomEventType.ChallengeFlag),
            (new Vector2Int(12, 5), RoomEventType.Hourglass),
            (new Vector2Int(5, 2), RoomEventType.FateDice),
            (new Vector2Int(8, 2), RoomEventType.ElixirSpring),
            (new Vector2Int(11, 2), RoomEventType.Whetstone),
        };

        private static readonly Vector2Int PlayerStart = new Vector2Int(7, 7);
        private static readonly Vector2Int[] EnemySpots = { new Vector2Int(1, 14), new Vector2Int(13, 14), new Vector2Int(7, 14) };

        private static RoomDefinition _testRoom;
        private RoomController _room;
        private PlayerActor _player;

#if UNITY_EDITOR
        public const string PrefKey = "LoopRogue_TestMap";

        /// <summary>Play 시작(타이틀 씬) - 메뉴로 켰으면 바로 Main으로. 한 번 쓰면 꺼서 다음 Play는 평소처럼.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            UnityEditor.EditorPrefs.SetBool(PrefKey, false);
            if (!Active)
                return;
            Debug.Log("[테스트 맵] 시작 - Main으로 바로 이동");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
                UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        }
#endif

        /// <summary>GameBootstrap - 지금 층 방 목록 대신 [테스트 방, 지금 층 보스방].</summary>
        public static List<RoomDefinition> BuildRooms(List<RoomDefinition> stageRooms)
        {
            var first = stageRooms[0];
            _testRoom = new RoomDefinition
            {
                RoomName = "Test Room",
                Width = Size,
                Height = Size,
                EnemyCount = EnemySpots.Length,
                EnemyKinds = new List<EnemyKind> { EnemyKind.Melee, EnemyKind.Ranged, EnemyKind.Shield },
                EnemyMaxHealth = EnemyHealth,
                EnemyAttackPower = EnemyAttack,
                IsTestRoom = true,
            };
            return new List<RoomDefinition> { _testRoom, stageRooms.Last(r => r.IsBossRoom) };
        }

        /// <summary>RoomController - 테스트 방 배치(벽 없음, 가운데 시작, 몹은 위쪽 끝).</summary>
        public static RoomLayout Layout()
        {
            var layout = new RoomLayout { Width = Size, Height = Size, PlayerStart = PlayerStart };
            layout.EnemyPositions.AddRange(EnemySpots);
            return layout;
        }

        public static IEnumerable<(Vector2Int Pos, RoomEventType Type)> Events => EventSpots;

        public static void Attach(RoomController room, PlayerActor player)
        {
            if (!Active)
                return;
            var tm = room.gameObject.AddComponent<TestMap>();
            tm._room = room;
            tm._player = player;
        }

        private void Start()
        {
            _room.ShowMessage("테스트 맵 - 이벤트 11종 / [R] 다시 깔기 / 몹을 다 잡고 출구로 가면 보스방");
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.rKey.wasPressedThisFrame || _testRoom == null || _room.RoomName != _testRoom.RoomName)
                return; // 보스방에선 안 됨(방 순서가 꼬인다)
            if (_room.IsInputLocked || NpcDialogUI.BlocksInput || _player.Stats.IsDead || _player.Levels.IsChoosingUpgrade)
                return;
            RunBuffs.Reset();
            _player.Stats.FullHeal();
            _room.LoadRoom(_testRoom);
            _room.ShowMessage("테스트 방을 다시 깔았다(효과 초기화, 체력 회복)");
        }
    }
}
