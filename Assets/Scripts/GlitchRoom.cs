using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>오류방 - 고리의 이음새가 터진 방(사용자 구상, 스토리는 나중에). 층마다 StageChance 확률로 일반 방 하나가 오류방이 된다
    /// (보스방·이벤트 방·퍼즐 방·테스트 맵 제외, 층마다 최대 1개 - StageProgress.GlitchRoom). 들어가면 화면이 지직거리고 바닥이 깨진 색으로
    /// 물들고 몹 이름이 깨지며, 뒤틀린 규칙 하나가 걸린다. 처음 깨면 골드(방 클리어 골드 × ClearGoldRooms)와 오류 조각 1개.
    /// 한 번 깬 오류방은 그 층에서 다시 와도 일반 방이다.</summary>
    public static class GlitchRoom
    {
        public enum Rule
        {
            ReverseControls, // 방향키 반대(사람만 - 봇은 이 규칙을 안 뽑는다)
            Revive,          // 쓰러진 몹이 한 번 되살아난다(체력 절반, 보상 없음)
            DoubleTurn,      // 몹이 두 턴에 한 번 더 움직인다
        }

        public const float StageChance = 0.35f;
        public const int ClearGoldRooms = 8;
        public const float ReviveHealthRatio = 0.5f;
        private const string FragmentsKey = "LoopRogue_GlitchFragments";

        /// <summary>에디터 메뉴 "오류방 테스트 모드"(EditorPrefs) - 켜면 일반 방(이벤트·보스 제외)마다 오류방, 규칙은 차례로.</summary>
        public const string TestModePrefKey = "LoopRogue_GlitchTestMode";
        private static int _testRule;

        public static bool TestMode
        {
            get
            {
#if UNITY_EDITOR
                return !GameHUD.AutoPlayActive && UnityEditor.EditorPrefs.GetBool(TestModePrefKey, false);
#else
                return false;
#endif
            }
        }

        public static Rule? Active { get; private set; }
        public static bool IsActive => Active.HasValue;

        /// <summary>몹 턴을 몇 번 돌았는지(DoubleTurn - 짝수 번째마다 한 번 더).</summary>
        public static int EnemyPhase;

        /// <summary>지금까지 모은 오류 조각(스토리용 재화 - 저장에 남는다).</summary>
        public static int Fragments => PlayerPrefs.GetInt(FragmentsKey, 0);

        public static int AddFragment()
        {
            var total = Fragments + 1;
            PlayerPrefs.SetInt(FragmentsKey, total);
            PlayerPrefs.Save();
            return total;
        }

        /// <summary>이 방이 지금 오류방이어야 하는지 - 층마다 한 번 굴려 정한 방 번호와 같고 아직 안 깼으면.</summary>
        public static bool ShouldGlitch(RoomDefinition def, int roomCount)
        {
            if (def == null || def.IsBossRoom || def.IsTestRoom || def.HasEvent || TestMap.Active)
                return false;
            if (TestMode)
                return true;
            var room = StageProgress.GlitchRoom(roomCount, StageChance);
            return room > 0 && room == def.RoomNumber && !StageProgress.GlitchCleared;
        }

        public static Rule PickRule(System.Random rng)
        {
            if (TestMode)
                return (Rule)(_testRule++ % 3);
            if (GameHUD.AutoPlayActive)
                return rng.Next(2) == 0 ? Rule.Revive : Rule.DoubleTurn;
            return (Rule)rng.Next(3);
        }

        public static void Begin(Rule rule)
        {
            Active = rule;
            EnemyPhase = 0;
        }

        public static void End() => Active = null;

        public static string RuleText(Rule rule) => rule switch
        {
            Rule.ReverseControls => Loc.T("방향이 뒤집혔다!"),
            Rule.Revive => Loc.T("쓰러진 몹이 한 번 되살아난다!"),
            _ => Loc.T("몹이 두 턴에 한 번 더 움직인다!"),
        };

        private static readonly string[] Noise = { "?", "#", "%", "@", "▒", "░", "▓", "§", "¿", "0", "1" };

        /// <summary>깨진 몹 이름 - 같은 몹은 늘 같은 모양(seed), 글자 수는 원래 이름과 비슷하게.</summary>
        public static string Corrupt(string name, int seed)
        {
            var rng = new System.Random(seed);
            var length = Mathf.Clamp(name.Length, 2, 6);
            var chars = new System.Text.StringBuilder();
            for (var i = 0; i < length; i++)
                chars.Append(rng.NextDouble() < 0.3 && i < name.Length ? name[i].ToString() : Noise[rng.Next(Noise.Length)]);
            return chars.ToString();
        }
    }

    /// <summary>오류방 연출 - 바닥·벽 일부를 깨진 색(보라·청록·초록)으로 물들이고, 몇 초마다 화면이 지직거리며(옅은 번쩍임 + 흔들림)
    /// 물든 칸이 바뀐다. 오류를 바로잡으면(Restore) 원래 색으로. 봇 실행 중엔 아무것도 안 한다.</summary>
    public class GlitchVisual : MonoBehaviour
    {
        private static readonly Color[] GlitchColors =
        {
            new Color(1f, 0.25f, 0.9f), new Color(0.2f, 1f, 0.95f), new Color(0.4f, 1f, 0.3f), new Color(0.6f, 0.3f, 1f),
        };

        private readonly List<SpriteRenderer> _tiles = new List<SpriteRenderer>();
        private readonly Dictionary<SpriteRenderer, Color> _original = new Dictionary<SpriteRenderer, Color>();
        private readonly System.Random _rng = new System.Random();
        private float _nextPulse;
        private bool _restored;

        public void Init(IEnumerable<SpriteRenderer> tiles)
        {
            foreach (var t in tiles)
                if (t != null)
                {
                    _tiles.Add(t);
                    _original[t] = t.color;
                }
            Recolor(0.16f);
            _nextPulse = Time.time + 0.6f;
        }

        private void Recolor(float fraction)
        {
            foreach (var t in _tiles)
            {
                if (t == null)
                    continue;
                t.color = _rng.NextDouble() < fraction
                    ? Color.Lerp(_original[t], GlitchColors[_rng.Next(GlitchColors.Length)], 0.55f)
                    : _original[t];
            }
        }

        public void Restore()
        {
            if (_restored)
                return;
            _restored = true;
            foreach (var t in _tiles)
                if (t != null)
                    t.color = _original[t];
        }

        private void Update()
        {
            if (_restored || DamagePopup.Suppressed || Time.time < _nextPulse)
                return;
            _nextPulse = Time.time + 1.8f + (float)_rng.NextDouble() * 1.8f;
            ScreenFlash.Play(GlitchColors[_rng.Next(GlitchColors.Length)], 0.16f, 0.14f);
            CameraShake.Shake(0.05f, 0.1f);
            Recolor(0.12f + (float)_rng.NextDouble() * 0.1f);
        }
    }
}
