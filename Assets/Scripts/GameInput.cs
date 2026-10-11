using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LoopRogue
{
    /// <summary>입력 한 곳 - 키보드와 화면 터치 버튼(TouchControls·HUD 칸)을 합쳐서 읽는다. 게임 코드는 Keyboard/Mouse를 직접 읽지 않고
    /// GameInput.Down(Key.W), GameInput.PointerDown(마우스 왼쪽 클릭 또는 터치) 등을 쓴다. 터치 버튼을 누른 탭은 "버튼이 먹었다"고 보고
    /// PointerDown으로는 안 넘긴다(버튼 뒤의 창이 같은 탭에 또 눌리지 않게). 모바일엔 Keyboard.current가 없어서 null이어도 동작한다.</summary>
    public static class GameInput
    {
        private sealed class Tap
        {
            public RectTransform Rect;
            public Key Key;
            public Func<bool> Active;
        }

        private static readonly List<Tap> Taps = new List<Tap>();
        private static readonly HashSet<Key> VirtualDown = new HashSet<Key>();
        private static readonly List<Key> PendingInject = new List<Key>();
        private static int _frame = -1;
        private static bool _pointerConsumed;

        /// <summary>이 사각형(오버레이 캔버스 UI)을 탭하면 key를 누른 것으로 친다 - 화면 버튼, HUD 스킬·퀵슬롯 칸.</summary>
        public static void RegisterTap(RectTransform rect, Key key, Func<bool> active = null)
        {
            if (rect != null)
                Taps.Add(new Tap { Rect = rect, Key = key, Active = active });
        }

        /// <summary>다음 프레임에 key를 한 번 누른 것으로 - 방향 패드 꾹 누르기 반복용.</summary>
        public static void Inject(Key key) => PendingInject.Add(key);

        private static void Refresh()
        {
            if (_frame == Time.frameCount)
                return;
            _frame = Time.frameCount;
            VirtualDown.Clear();
            foreach (var k in PendingInject)
                VirtualDown.Add(k);
            PendingInject.Clear();
            _pointerConsumed = false;
            LastTappedRect = null;

            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame)
                return;
            var pos = pointer.position.ReadValue();
            Taps.RemoveAll(t => t.Rect == null);
            // 나중에 등록한 것(위에 그려진 화면 버튼)부터
            for (var i = Taps.Count - 1; i >= 0; i--)
            {
                var t = Taps[i];
                if (!t.Rect.gameObject.activeInHierarchy || (t.Active != null && !t.Active()))
                    continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(t.Rect, pos, null))
                    continue;
                VirtualDown.Add(t.Key);
                _pointerConsumed = true;
                LastTappedRect = t.Rect;
                return;
            }
        }

        /// <summary>방금 눌린 터치 버튼(누른 동안 반짝임·꾹 누르기 반복용).</summary>
        public static RectTransform LastTappedRect { get; private set; }

        /// <summary>이번 프레임 탭 판정을 미리 돌린다(LastTappedRect를 먼저 알아야 할 때).</summary>
        public static void Prime() => Refresh();

        public static bool Down(Key key)
        {
            Refresh();
            var keyboard = Keyboard.current;
            return (keyboard != null && keyboard[key].wasPressedThisFrame) || VirtualDown.Contains(key);
        }

        public static bool Held(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].isPressed;
        }

        /// <summary>마우스 왼쪽 클릭 또는 터치 - 단, 화면 버튼을 누른 탭은 빼고.</summary>
        public static bool PointerDown
        {
            get
            {
                Refresh();
                var pointer = Pointer.current;
                return pointer != null && pointer.press.wasPressedThisFrame && !_pointerConsumed;
            }
        }

        public static bool PointerHeld => Pointer.current != null && Pointer.current.press.isPressed;
        public static Vector2 PointerPosition => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
        public static Vector2 PointerDelta => Pointer.current != null ? Pointer.current.delta.ReadValue() : Vector2.zero;

        /// <summary>마우스 휠(방향만 쓸 것 - 크기는 OS마다 다르다).</summary>
        public static float ScrollY => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        /// <summary>터치 손가락 끌기 - 이번 프레임 세로 이동(화면 픽셀, 위로 끌면 +). 누르고 있지 않으면 0.</summary>
        public static float TouchDragY
        {
            get
            {
                var touch = Touchscreen.current;
                return touch != null && touch.primaryTouch.press.isPressed ? touch.primaryTouch.delta.ReadValue().y : 0f;
            }
        }
    }
}
