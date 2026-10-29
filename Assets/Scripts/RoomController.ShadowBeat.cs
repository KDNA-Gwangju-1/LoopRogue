using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>7층 그림자 암살자 두 번째 기믹 "그림자 박자"(10-23 사용자) - 분신이 없을 때 체력 BossBrain.ShadowBeatHealth(75%·35%) 아래에서 한 번씩.
    /// 암살자가 그림자 속에 숨고 방이 어두워진다. 시작 컷신에서 ShadowBeatCount박자의 빈 칸을 순서대로 한 번씩 번쩍여 보여 준 뒤,
    /// 매 몹 턴 방 전체를 벤다 - 비는 칸은 그 박자의 빈 칸 하나뿐(내 자리와 상하좌우 다섯 칸 중 하나). 박자 중엔 예고가 없어서
    /// 본 순서를 기억해 움직여야 한다(10-23 사용자 "안전지대를 순서대로 5번 보여 주고 이동"). 한 번이라도 베이면 즉사, 다 버티면 기절.
    /// 빈 칸 길은 시작할 때 미리 정한다(제자리는 한 번까지, 막힌 칸·몹 칸·출구는 빼고, 방금 온 칸으로 바로 되돌아가지 않게).
    /// 판정(위험 칸)은 박자마다 그대로 깔리니 봇은 영향 없음.
    /// 체력 75%·35%에 한 번씩. 35%는 하얀 표식이 ShadowBeatWhiteCount개 섞인다(10-23 사용자) - 하얀 표식은 거울에 비친 칸이라
    /// 그 박자엔 표식과 반대 방향(위↔아래, 왼↔오)으로 밟아야 한다. 표식은 늘 "그 박자 직전 내 자리" 기준으로 찍는다.</summary>
    public partial class RoomController
    {
        public const int ShadowBeatCount = 5;
        /// <summary>시작 컷신에서 빈 칸 하나를 보여 주는 시간 - 다음 칸 사이에 짧게 꺼진다(제자리가 두 번 이어져도 구분되게).</summary>
        public const float ShadowBeatShowSeconds = 0.45f;
        private const float ShadowBeatGapSeconds = 0.2f;
        private static readonly Color ShadowBeatMarkColor = new Color(0.8f, 0.65f, 1f, 1f);
        private static readonly Color ShadowBeatWhiteColor = new Color(1f, 1f, 1f, 1f);
        public const int ShadowBeatWhiteCount = 2;
        private static readonly Color ShadowBeatDarkColor = new Color(0.02f, 0.01f, 0.05f, 0.62f);

        private bool _beatActive;
        private int _beatIndex;
        private readonly List<Vector2Int> _beatSafe = new List<Vector2Int>();
        private readonly List<Vector2Int> _beatShown = new List<Vector2Int>(); // 번쩍일 칸(보라 = 빈 칸 그대로, 하얀 = 거울에 비친 칸)
        private readonly List<bool> _beatWhite = new List<bool>();
        private readonly HashSet<Vector2Int> _beatTiles = new HashSet<Vector2Int>();
        private GameObject _beatDark;

        public bool ShadowBeatActive => _gimmickStage == 7 && _beatActive;
        /// <summary>이번 박자에 안 베이는 칸(봇용 - 화면엔 안 보인다).</summary>
        public Vector2Int? ShadowBeatSafe => ShadowBeatActive && _beatIndex < _beatSafe.Count ? _beatSafe[_beatIndex] : (Vector2Int?)null;

        /// <summary>BossBrain이 7층 체력 75%·35% 아래(분신이 없을 때)에서 한 번씩 부른다 - mixWhite면 하얀 표식이 섞인다.</summary>
        public void BeginShadowBeat(EnemyActor boss, bool mixWhite = false)
        {
            _gimmickStage = 7;
            _gimmickBoss = boss;
            _gimmickJustStarted = true;
            _beatActive = true;
            _beatIndex = 0;
            BuildBeatPath(_player.GridPos, mixWhite);
            var c = new GimmickCutscene { Focus = _player }; // 빈 칸은 내 둘레라 나를 비춘다
            c.Steps.Add(() =>
            {
                if (!DamagePopup.Suppressed)
                {
                    Fx.Play("shadow_split", boss.transform.position, 3f, fps: 16f, sortingOrder: 8);
                    ShowBeatDark();
                }
                return true;
            });
            c.StepDelays.Add(0.8f);
            for (var i = 0; i < _beatSafe.Count; i++)
            {
                var cell = _beatShown[i];
                var white = _beatWhite[i];
                c.Steps.Add(() =>
                {
                    if (!DamagePopup.Suppressed)
                        StartCoroutine(FlashBeatMark(cell, white));
                    return true;
                });
                c.StepDelays.Add(ShadowBeatShowSeconds + ShadowBeatGapSeconds);
            }
            c.Finish = () =>
            {
                // 시작 안내는 없음(10-21 사용자 - 힌트 없이 직접 깨기, 규칙은 도감에)
                SetBeatDanger();
                UpdateGimmickLabel();
            };
            UpdateGimmickLabel();
            PlayGimmickCutscene(c);
        }

        private void BuildBeatPath(Vector2Int start, bool mixWhite)
        {
            _beatSafe.Clear();
            _beatShown.Clear();
            _beatWhite.Clear();
            // 하얀 박자 자리 - 첫 박자는 빼고(규칙을 보고 나서 바로 꼬이지 않게) 무작위 ShadowBeatWhiteCount개
            var whiteAt = new HashSet<int>();
            if (mixWhite)
                foreach (var i in Enumerable.Range(1, ShadowBeatCount - 1).OrderBy(_ => Rng.Next()).Take(ShadowBeatWhiteCount))
                    whiteAt.Add(i);
            var pos = start;
            Vector2Int? prev = null;
            var stays = 0;
            for (var i = 0; i < ShadowBeatCount; i++)
            {
                var white = whiteAt.Contains(i);
                var options = new List<Vector2Int>();
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var n = pos + d;
                    var mirror = pos - d; // 하얀 표식이 찍힐 칸 - 벽이 아니어야 보인다
                    if (Map.IsWalkable(n) && n != ExitPosition && n != prev && (!white || (Map.IsInBounds(mirror) && !Map.IsWall(mirror))))
                        options.Add(n);
                }
                // 제자리는 한 번까지만(하얀 박자엔 안 씀 - 거울에 비쳐도 제자리라 구분이 없다), 묶여 있으면 첫 박자는 제자리
                var rooted = i == 0 && _player.RootedTurns > 0;
                if (rooted || options.Count == 0 || (!white && stays == 0 && Rng.Next(5) == 0))
                {
                    _beatSafe.Add(pos);
                    _beatShown.Add(pos);
                    _beatWhite.Add(false);
                    stays++;
                    prev = null;
                    continue;
                }
                var next = options[Rng.Next(options.Count)];
                _beatSafe.Add(next);
                _beatShown.Add(white ? pos - (next - pos) : next);
                _beatWhite.Add(white);
                prev = pos;
                pos = next;
            }
        }

        /// <summary>시작 컷신 - 빈 칸 하나를 잠깐 번쩍이고 끈다.</summary>
        private IEnumerator FlashBeatMark(Vector2Int cell, bool white)
        {
            var mark = SpawnTelegraphTile(cell, white ? ShadowBeatWhiteColor : ShadowBeatMarkColor, -4, "tel_beat_next");
            SfxPlayer.Play(Sfx.Warning);
            yield return new WaitForSeconds(ShadowBeatShowSeconds);
            Fx.Release(mark); // 풀에서 꺼낸 예고 그림 - 지우지 않고 돌려준다
        }

        /// <summary>이번 박자의 위험 칸(빈 칸 하나만 빼고 방 전체) - 판정·봇용, 그림은 없다.</summary>
        private void SetBeatDanger()
        {
            _beatTiles.Clear();
            var safe = _beatSafe[_beatIndex];
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (p != safe && !Map.IsWall(p) && (_gimmickBoss == null || !GridMap.Occupies(_gimmickBoss, p)))
                    _beatTiles.Add(p);
            }
            RebuildDangerTiles();
        }

        /// <summary>방 전체를 덮는 어둠(바닥·벽 위, 예고 칸·액터 아래).</summary>
        private void ShowBeatDark()
        {
            if (_beatDark != null)
                return;
            _beatDark = new GameObject("ShadowBeatDark");
            _beatDark.transform.SetParent(transform, false);
            VisualUtil.CreateSquareVisual(_beatDark, ShadowBeatDarkColor, 1f, sortingOrder: -7);
            _beatDark.transform.localScale = new Vector3((Map.Width + 2) * GridConstants.CellSize, (Map.Height + 2) * GridConstants.CellSize, 1f);
            _beatDark.transform.position = new Vector3((Map.Width - 1) * GridConstants.CellSize * 0.5f, (Map.Height - 1) * GridConstants.CellSize * 0.5f, 0f);
        }

        /// <summary>몹 턴 끝마다 한 박자 - 빈 칸이 아니면 즉사(true).</summary>
        private bool TickShadowBeat()
        {
            var safe = _beatSafe[_beatIndex];
            if (HitFeedback.Enabled)
            {
                foreach (var t in _beatTiles)
                    if (Mathf.Max(Mathf.Abs(t.x - _player.GridPos.x), Mathf.Abs(t.y - _player.GridPos.y)) <= 2)
                        Fx.Play("boss_diag", new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f), 1.3f, fps: 18f, sortingOrder: 5);
                CameraShake.Shake(0.15f, 0.3f);
                SfxPlayer.Play(Sfx.Slam);
            }
            if (_player.GridPos != safe)
            {
                DeathCause.Set("g7_beat"); // 사망 대사
                ShowMessage(Loc.T("<color=#FF4040>그림자 칼날이 몸을 갈랐다!</color>"));
                ClearShadowBeat();
                return true;
            }
            _beatIndex++;
            if (_beatIndex < _beatSafe.Count)
            {
                SetBeatDanger();
                return false;
            }
            ClearShadowBeat();
            OnGimmickBroken();
            return false;
        }

        private void ClearShadowBeat()
        {
            _beatTiles.Clear();
            RebuildDangerTiles();
            if (_beatDark != null)
                Destroy(_beatDark);
            _beatDark = null;
            _beatActive = false;
            _beatIndex = 0;
            _beatSafe.Clear();
            _beatShown.Clear();
            _beatWhite.Clear();
        }
    }
}
