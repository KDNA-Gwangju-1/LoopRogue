using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LoopRogue
{
    /// <summary>로비를 걸어 다니는 던전 방으로(10-14 사용자 요청) - 큰 방 하나에 NPC 6명(뽑기·영약·도박·노인·업적·도감)이 벽을 따라 서 있고,
    /// 플레이어는 던전처럼 방향키로 한 칸씩 걷는다. NPC에게 부딪히거나 옆에서 Space/Enter로 말을 걸면 그 NPC의 화면(예전 허브 카드와 같은 기능)이 열리고,
    /// 위쪽 열린 문으로 들어가면 던전 시작. NPC나 바닥을 클릭(터치)하면 그 자리까지 걸어간다.
    /// 화면·창 열기는 LobbyBootstrap이 넘겨준 콜백으로만 한다(이 클래스는 방과 걷기만 안다).</summary>
    public class LobbyRoom : MonoBehaviour
    {
        public const int Width = 11;
        public const int Height = 8;
        private const float SpriteScale = 1.9f;       // 48x48 캔버스 그림 - 던전 NPC(1.3)보다 크게, 플레이어와 키가 비슷하게(로비의 주인공들이라)
        private const float PlayerSpriteScale = 1.25f; // PlayerActor와 같게
        private const float StepInterval = 0.16f;     // 키를 누르고 있을 때·클릭으로 걸어갈 때 한 칸 간격
        private const float RepeatDelay = 0.28f;

        public static readonly Vector2Int DoorCell = new Vector2Int(Width / 2, Height - 1);
        private static readonly Vector2Int StartCell = new Vector2Int(Width / 2, 1);

        public sealed class Npc
        {
            public string SpriteSet;
            public Vector2Int Cell;
            public Func<string> Name;
            public Func<string> Badge; // 이름 아래 한 줄(받을 보상 수 등) - 빈 문자열이면 안 보임
            public Action Open;
            public TextMesh Label;
            public string ShownLabel;
        }

        private readonly List<Npc> _npcs = new List<Npc>();
        private readonly HashSet<Vector2Int> _blocked = new HashSet<Vector2Int>();
        private Action _onDoor;
        /// <summary>문이 잠겼는지(튜토리얼 뒤 첫 구매 전, LobbyBootstrap) - 잠겼으면 문 칸으로 안 들어가고 OnDoorLocked만.</summary>
        public Func<bool> DoorLocked;
        public Action OnDoorLocked;
        private Walker _player;
        private SpriteRenderer _playerRenderer;
        private Vector2Int _facing = Vector2Int.up;
        private readonly Queue<Vector2Int> _path = new Queue<Vector2Int>();
        private Npc _pathTarget;
        private bool _pathToDoor;
        private float _nextStepTime;
        private Vector2Int? _heldDir;
        private float _heldSince;
        private readonly System.Random _rng = new System.Random(20261014);

        /// <summary>걸어 다니는 플레이어 - SpriteAnimator가 칸이 바뀐 걸 보고 걷기 모션을 넣도록 GridActor를 쓴다.</summary>
        private sealed class Walker : GridActor { }

        /// <summary>플레이어 바로 옆(상하좌우)에 있는 NPC - 바라보는 쪽을 먼저. 없으면 null.</summary>
        public Npc Nearby
        {
            get
            {
                Npc any = null;
                foreach (var npc in _npcs)
                {
                    var d = npc.Cell - _player.GridPos;
                    if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1)
                        continue;
                    if (d == _facing)
                        return npc;
                    any ??= npc;
                }
                return any;
            }
        }

        public bool NearDoor => _player.GridPos == DoorCell + Vector2Int.down;

        private void EnterDoor()
        {
            if (DoorLocked != null && DoorLocked())
                OnDoorLocked?.Invoke();
            else
                _onDoor();
        }

        public static LobbyRoom Create(Action onDoor, string doorLabel)
        {
            var go = new GameObject("LobbyRoom");
            var room = go.AddComponent<LobbyRoom>();
            room._onDoor = onDoor;
            room._doorLabel = doorLabel;
            return room;
        }

        private string _doorLabel;

        public void AddNpc(string spriteSet, Vector2Int cell, Func<string> name, Func<string> badge, Action open)
        {
            var npc = new Npc { SpriteSet = spriteSet, Cell = cell, Name = name, Badge = badge, Open = open };
            _npcs.Add(npc);
            _blocked.Add(cell);

            var go = new GameObject($"Npc_{spriteSet}");
            go.transform.SetParent(transform, false);
            go.transform.position = CellWorld(cell);
            var first = SpriteAnimator.FirstFrame(spriteSet);
            if (first != null)
            {
                var renderer = VisualUtil.CreateSpriteVisual(go, first, GridConstants.CellSize * SpriteScale, SortingFor(cell));
                go.AddComponent<SpriteAnimator>().Setup(spriteSet, renderer, Color.white);
            }
            else
                VisualUtil.CreateSquareVisual(go, new Color(0.8f, 0.7f, 0.4f), GridConstants.CellSize * 0.6f, SortingFor(cell));

            // 이름표는 그림과 따로(그림 오브젝트는 배율이 걸려 있다) - 머리 위
            npc.Label = CreateLabel(CellWorld(cell) + new Vector3(0f, GridConstants.CellSize * 0.66f, -1f));
            RefreshLabel(npc);
        }

        private TextMesh CreateLabel(Vector3 position)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.position = position;
            var text = labelGo.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64;          // 10-14 사용자 "글씨가 너무 작다" - 예전 40 x 0.075의 약 1.5배, 글꼴 해상도도 올려 흐리지 않게
            text.characterSize = 0.07f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.richText = true;
            var meshRenderer = labelGo.GetComponent<MeshRenderer>();
            meshRenderer.material = text.font.material;
            meshRenderer.sortingOrder = 50;
            return text;
        }

        public void Build()
        {
            BuildTiles();

            var playerGo = new GameObject("LobbyPlayer");
            playerGo.transform.SetParent(transform, false);
            _player = playerGo.AddComponent<Walker>();
            _player.GridPos = StartCell;
            _player.SyncTransform();
            var sprite = SpriteAnimator.FirstFrame("Player");
            if (sprite != null)
            {
                _playerRenderer = VisualUtil.CreateSpriteVisual(playerGo, sprite, GridConstants.CellSize * PlayerSpriteScale, SortingFor(StartCell));
                playerGo.AddComponent<SpriteAnimator>().Setup("Player", _playerRenderer, new Color(0.4f, 0.5f, 0.75f));
            }
            else
                _playerRenderer = VisualUtil.CreateSquareVisual(playerGo, new Color(0.3f, 0.5f, 1f), GridConstants.CellSize * 0.65f, SortingFor(StartCell));

            PositionCamera();
        }

        /// <summary>바닥·벽·문 - 던전 방(RoomController.BuildTiledRoom)과 같은 타일. 그림이 없으면 단색.</summary>
        private void BuildTiles()
        {
            var tiled = DungeonTiles.Available;
            for (var x = -1; x <= Width; x++)
            for (var y = -1; y <= Height; y++)
            {
                var cell = new Vector2Int(x, y);
                var inside = x >= 0 && y >= 0 && x < Width && y < Height;
                var go = new GameObject(inside ? "Floor" : "Wall");
                go.transform.SetParent(transform, false);
                go.transform.position = CellWorld(cell);
                if (!inside)
                {
                    var face = y == Height && x >= 0 && x < Width; // 바로 아래가 방 바닥인 윗벽만 벽돌 앞면
                    if (tiled)
                        VisualUtil.CreateSpriteVisual(go, DungeonTiles.Wall(face, _rng), GridConstants.CellSize, -8);
                    else
                        VisualUtil.CreateSquareVisual(go, new Color(0.25f, 0.24f, 0.28f), GridConstants.CellSize, -8);
                }
                else if (tiled)
                    VisualUtil.CreateSpriteVisual(go, DungeonTiles.Floor(_rng), GridConstants.CellSize, -10);
                else
                    VisualUtil.CreateSquareVisual(go, (x + y) % 2 == 0 ? new Color(0.16f, 0.16f, 0.2f) : new Color(0.19f, 0.19f, 0.23f), GridConstants.CellSize, -10);
            }

            // 던전으로 가는 열린 문(던전 출구와 같은 그림) - 문 칸에 올라서면 시작
            var door = new GameObject("Door");
            door.transform.SetParent(transform, false);
            door.transform.position = CellWorld(DoorCell);
            var doorSprite = Resources.Load<Sprite>("Tiles/ExitDoor") ?? Resources.Load<Sprite>("Tiles/Stairs");
            if (doorSprite != null)
                VisualUtil.CreateSpriteVisual(door, doorSprite, GridConstants.CellSize, -7);
            else
                VisualUtil.CreateSquareVisual(door, new Color(0.3f, 0.6f, 1f), GridConstants.CellSize * 0.8f, -7);
            // 문 이름표는 윗벽 위(벽돌 앞면에 겹치지 않게)
            CreateLabel(CellWorld(DoorCell) + new Vector3(0f, GridConstants.CellSize * 1.05f, -1f)).text = $"<color=#9FE8A8>{_doorLabel}</color>";
        }

        /// <summary>방 전체가 위 제목 줄·아래 안내 줄 사이에 들어오게 고정 카메라.</summary>
        private void PositionCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            const float topReservePx = 24f, bottomReservePx = 56f, refHeight = 720f; // 위 제목·골드 줄은 상점에서만(10-14)
            var cs = GridConstants.CellSize;
            var roomHeight = (Height + 2) * cs; // 둘레 벽 포함
            var roomWidth = (Width + 2) * cs;
            var visible = refHeight - topReservePx - bottomReservePx;
            var half = Mathf.Max(roomHeight * refHeight / visible, roomWidth / Mathf.Max(0.1f, cam.aspect)) / 2f;
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.transform.rotation = Quaternion.identity;
            var worldPerPx = half * 2f / refHeight;
            var center = new Vector3((Width - 1) * cs / 2f, (Height - 1) * cs / 2f, -10f);
            center.y += (topReservePx - bottomReservePx) / 2f * worldPerPx;
            cam.transform.position = center;
        }

        /// <summary>LobbyBootstrap이 허브 화면에서만(창이 안 열려 있을 때) 매 프레임 부른다.</summary>
        public void Tick()
        {
            foreach (var npc in _npcs)
                RefreshLabel(npc);

            var dir = ReadDirection(out var pressed);
            if (dir.HasValue)
            {
                _path.Clear(); // 키를 누르면 클릭 이동은 취소
                _pathTarget = null;
                _pathToDoor = false;
                if (pressed || (Time.time - _heldSince >= RepeatDelay && Time.time >= _nextStepTime))
                    StepOrBump(dir.Value);
                return;
            }

            if (_path.Count > 0)
            {
                if (Time.time >= _nextStepTime)
                    FollowPath();
                return;
            }

            if (GameInput.Down(Key.Space) || GameInput.Down(Key.Enter)) // F 키는 10-28에 뺐다(부딪히거나 Space)
            {
                var npc = Nearby;
                if (npc != null)
                    Talk(npc);
                else if (NearDoor)
                    EnterDoor();
            }
        }

        /// <summary>UI 버튼에 안 맞은 클릭 - NPC면 옆까지 걸어가서 말 걸기, 문이면 걸어가서 시작, 바닥이면 거기까지 걷기.</summary>
        public void HandleClick(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var world = cam.ScreenToWorldPoint(screenPos);
            var cell = new Vector2Int(Mathf.RoundToInt(world.x / GridConstants.CellSize), Mathf.RoundToInt(world.y / GridConstants.CellSize));
            // NPC 그림이 칸보다 커서 머리를 눌러도 맞게 한 칸 아래도 본다
            var npc = _npcs.Find(n => n.Cell == cell) ?? _npcs.Find(n => n.Cell == cell + Vector2Int.down);
            _path.Clear();
            _pathTarget = null;
            _pathToDoor = false;
            if (npc != null)
            {
                if (Mathf.Abs(npc.Cell.x - _player.GridPos.x) + Mathf.Abs(npc.Cell.y - _player.GridPos.y) == 1)
                {
                    _facing = npc.Cell - _player.GridPos;
                    Talk(npc);
                    return;
                }
                if (PathTo(c => Mathf.Abs(npc.Cell.x - c.x) + Mathf.Abs(npc.Cell.y - c.y) == 1))
                    _pathTarget = npc;
                return;
            }
            if (cell == DoorCell)
            {
                if (_player.GridPos == DoorCell + Vector2Int.down)
                    StepOrBump(Vector2Int.up);
                else if (PathTo(c => c == DoorCell + Vector2Int.down))
                    _pathToDoor = true;
                return;
            }
            if (InRoom(cell) && !_blocked.Contains(cell))
                PathTo(c => c == cell);
        }

        private void FollowPath()
        {
            var next = _path.Dequeue();
            StepOrBump(next - _player.GridPos);
            if (_path.Count > 0)
                return;
            if (_pathTarget != null)
            {
                _facing = _pathTarget.Cell - _player.GridPos;
                Talk(_pathTarget);
                _pathTarget = null;
            }
            else if (_pathToDoor)
            {
                _pathToDoor = false;
                _nextStepTime = Time.time + StepInterval;
                _path.Enqueue(DoorCell);
            }
        }

        private void StepOrBump(Vector2Int dir)
        {
            _facing = dir;
            _nextStepTime = Time.time + StepInterval;
            var target = _player.GridPos + dir;
            if (target == DoorCell)
            {
                if (DoorLocked != null && DoorLocked())
                {
                    OnDoorLocked?.Invoke();
                    return;
                }
                MoveTo(target);
                _onDoor();
                return;
            }
            var npc = _npcs.Find(n => n.Cell == target);
            if (npc != null)
            {
                Talk(npc); // 던전 NPC처럼 부딪히면 말 걸기
                return;
            }
            if (InRoom(target) && !_blocked.Contains(target))
                MoveTo(target);
        }

        private void MoveTo(Vector2Int cell)
        {
            _player.GridPos = cell; // 그림 위치는 SpriteAnimator가 걸어가듯 옮긴다
            if (_playerRenderer != null)
                _playerRenderer.sortingOrder = SortingFor(cell);
        }

        private void Talk(Npc npc)
        {
            _heldDir = null; // 창을 닫은 뒤 누르고 있던 방향키로 바로 또 걷지 않게
            npc.Open();
        }

        /// <summary>목표 조건을 만족하는 가장 가까운 칸까지 BFS 경로를 _path에 넣는다. 갈 수 없으면 false.</summary>
        private bool PathTo(Func<Vector2Int, bool> goal)
        {
            var start = _player.GridPos;
            if (goal(start))
                return true;
            var prev = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (prev.ContainsKey(n) || !InRoom(n) || _blocked.Contains(n) || n == DoorCell)
                        continue;
                    prev[n] = c;
                    if (goal(n))
                    {
                        var steps = new List<Vector2Int>();
                        for (var p = n; p != start; p = prev[p])
                            steps.Add(p);
                        steps.Reverse();
                        foreach (var s in steps)
                            _path.Enqueue(s);
                        _nextStepTime = Time.time;
                        return true;
                    }
                    queue.Enqueue(n);
                }
            }
            return false;
        }

        private Vector2Int? ReadDirection(out bool pressed)
        {
            pressed = false;
            Vector2Int? down = null;
            if (GameInput.Down(Key.W) || GameInput.Down(Key.UpArrow))
                down = Vector2Int.up;
            else if (GameInput.Down(Key.S) || GameInput.Down(Key.DownArrow))
                down = Vector2Int.down;
            else if (GameInput.Down(Key.A) || GameInput.Down(Key.LeftArrow))
                down = Vector2Int.left;
            else if (GameInput.Down(Key.D) || GameInput.Down(Key.RightArrow))
                down = Vector2Int.right;
            if (down.HasValue)
            {
                pressed = true;
                _heldDir = down;
                _heldSince = Time.time;
                return down;
            }
            // 누르고 있으면 계속 걷는다(던전과 달리 턴이 없어서)
            if (_heldDir.HasValue && IsHeld(_heldDir.Value))
                return _heldDir;
            _heldDir = null;
            return null;
        }

        private static bool IsHeld(Vector2Int d)
        {
            if (d == Vector2Int.up)
                return GameInput.Held(Key.W) || GameInput.Held(Key.UpArrow);
            if (d == Vector2Int.down)
                return GameInput.Held(Key.S) || GameInput.Held(Key.DownArrow);
            if (d == Vector2Int.left)
                return GameInput.Held(Key.A) || GameInput.Held(Key.LeftArrow);
            return GameInput.Held(Key.D) || GameInput.Held(Key.RightArrow);
        }

        private void RefreshLabel(Npc npc)
        {
            var badge = npc.Badge?.Invoke() ?? string.Empty;
            var near = _player != null && Nearby == npc;
            var name = near ? $"<color=#FFE066>{npc.Name()}</color>" : npc.Name();
            var content = badge.Length > 0 ? $"{badge}\n{name}" : name;
            if (content == npc.ShownLabel)
                return;
            npc.ShownLabel = content;
            npc.Label.text = content;
        }

        private static bool InRoom(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

        private static Vector3 CellWorld(Vector2Int c) => new Vector3(c.x * GridConstants.CellSize, c.y * GridConstants.CellSize, 0f);

        /// <summary>아래 칸에 있는 것이 위 칸 것보다 앞에 그려지게.</summary>
        private static int SortingFor(Vector2Int c) => 1 + (Height - c.y);
    }
}
