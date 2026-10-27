using System;
using System.IO;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>테스트 배포용 - 타이틀의 "설문 참여"(설문 링크 열기)와 "내 기록 복사"(설문에 붙여 넣을 한 줄 기록), 그리고 누적 플레이 시간.
    /// 설문 링크는 exe 옆 survey_url.txt(에디터에선 프로젝트 폴더)가 있으면 그걸, 없으면 DefaultSurveyUrl - 파일만 바꾸면 다시 빌드 안 해도 된다.</summary>
    public static class Playtest
    {
        public const string DefaultSurveyUrl = "https://forms.gle/qGV6PRwxohFE54pG7"; // 3차 테스트 설문(10-27)
        private const string UrlFileName = "survey_url.txt";
        private const string PlayTimeKey = "LoopRogue_PlayTimeSeconds";
        private const float SaveEverySeconds = 30f;

        /// <summary>이 빌드를 만든 시각(기록에 같이 적어 어느 빌드로 했는지 구분) - 빌드할 때 자동으로 적힌다(SaveReset.BuildStamp). 에디터는 "editor".</summary>
        public static string BuildLabel => SaveReset.BuildStamp ?? "editor";

        public static string SurveyUrl
        {
            get
            {
                try
                {
                    var path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", UrlFileName);
                    if (File.Exists(path))
                    {
                        var url = File.ReadAllText(path).Trim();
                        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return url;
                    }
                }
                catch (Exception)
                {
                    // 파일을 못 읽으면 기본 링크
                }
                return DefaultSurveyUrl;
            }
        }

        /// <summary>설문 링크를 브라우저로 연다. 링크가 없으면 false.</summary>
        /// <summary>[설문 참여](10-27 사용자) - 기록을 먼저 클립보드에 복사하고 설문을 연다(첫 칸에 Ctrl+V만 하면 되게). 링크가 없으면 false.</summary>
        public static bool CopyAndOpenSurvey()
        {
            if (string.IsNullOrEmpty(SurveyUrl))
                return false;
            CopyRecord();
            return OpenSurvey();
        }

        /// <summary>종료하려 할 때 설문을 권하는 창이 떠 있는지 - 뒤의 타이틀·Esc 메뉴는 입력을 받지 않는다.</summary>
        public static bool QuitPromptOpen => QuitPrompt.Instance != null;

        /// <summary>게임 종료 버튼(타이틀·Esc 메뉴)에서 - 설문 링크가 있으면 한 번 묻는 창을 띄우고 true(창이 quit을 부른다).
        /// 링크가 없거나(출시 빌드) 이번 실행에 이미 물었으면 false - 호출부가 바로 끈다.</summary>
        public static bool TryAskSurveyBeforeQuit(Action quit)
        {
            if (_askedThisSession || string.IsNullOrEmpty(SurveyUrl) || QuitPrompt.Instance != null)
                return false;
            _askedThisSession = true;
            QuitPrompt.Show(quit);
            return true;
        }

        private static bool _askedThisSession;

        /// <summary>"설문에 참여해 주시겠어요?" 창 - 자기 캔버스로 맨 위에. [설문 참여 후 종료](Enter) / [그냥 종료] / [취소](Esc).</summary>
        private sealed class QuitPrompt : MonoBehaviour
        {
            public static QuitPrompt Instance;
            private Action _quit;
            private readonly System.Collections.Generic.List<(RectTransform Rect, UnityEngine.UI.Image Image, Color Color, Action Act)> _buttons =
                new System.Collections.Generic.List<(RectTransform, UnityEngine.UI.Image, Color, Action)>();
            private int _openFrame;

            public static void Show(Action quit)
            {
                var go = new GameObject("SurveyQuitPrompt");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<QuitPrompt>();
                Instance._quit = quit;
                Instance._openFrame = Time.frameCount;
                Instance.Build();
            }

            private void Build()
            {
                var canvasGo = new GameObject("Canvas", typeof(RectTransform));
                canvasGo.transform.SetParent(transform, false);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 900; // 설정·Esc 메뉴 위
                var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>(); // 뒤쪽 UI 버튼 클릭 막기

                var dim = HudUi.CreateImage(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.6f));
                HudUi.Stretch(dim.rectTransform, 0f);
                var panel = HudUi.CreateImage(canvasGo.transform, "Panel", new Color(0.08f, 0.09f, 0.12f, 0.97f));
                PixelUi.Slice(panel, "Panel", 2f);
                panel.rectTransform.sizeDelta = new Vector2(560f, 250f);

                var title = HudUi.CreateText(panel.transform, "Title", 24, TextAnchor.MiddleCenter);
                title.text = Loc.T("설문에 참여해 주시겠어요?");
                title.rectTransform.sizeDelta = new Vector2(520f, 40f);
                title.rectTransform.anchoredPosition = new Vector2(0f, 76f);
                var body = HudUi.CreateText(panel.transform, "Body", 16, TextAnchor.MiddleCenter);
                body.text = Loc.T("이번 빌드에서 플레이한 기록이 자동으로 복사돼요.\n설문 첫 칸에 Ctrl+V로 붙여 넣기만 하면 돼요.");
                body.color = new Color(0.8f, 0.82f, 0.88f);
                body.rectTransform.sizeDelta = new Vector2(520f, 60f);
                body.rectTransform.anchoredPosition = new Vector2(0f, 22f);

                AddButton(panel.transform, Loc.T("설문 참여 후 종료  [Enter]"), new Vector2(0f, -40f), 300f, new Color(0.2f, 0.4f, 0.3f, 0.95f), Participate);
                AddButton(panel.transform, Loc.T("그냥 종료"), new Vector2(-80f, -92f), 140f, new Color(0.35f, 0.18f, 0.18f, 0.95f), Quit);
                AddButton(panel.transform, Loc.T("취소  [Esc]"), new Vector2(80f, -92f), 140f, new Color(0.22f, 0.24f, 0.3f, 0.95f), Close);
            }

            private void AddButton(Transform parent, string label, Vector2 pos, float width, Color color, Action act)
            {
                var image = HudUi.CreateImage(parent, "Button", color);
                image.rectTransform.sizeDelta = new Vector2(width, 40f);
                image.rectTransform.anchoredPosition = pos;
                var text = HudUi.CreateText(image.transform, "Text", 16, TextAnchor.MiddleCenter);
                HudUi.Stretch(text.rectTransform, 0f);
                text.text = label;
                _buttons.Add((image.rectTransform, image, color, act));
            }

            private void Update()
            {
                if (Time.frameCount == _openFrame)
                    return; // 종료 버튼을 누른 그 클릭·키를 이 창이 또 읽지 않게
                if (GameInput.Down(UnityEngine.InputSystem.Key.Escape))
                {
                    Close();
                    return;
                }
                if (GameInput.Down(UnityEngine.InputSystem.Key.Enter) || GameInput.Down(UnityEngine.InputSystem.Key.NumpadEnter))
                {
                    Participate();
                    return;
                }
                var pos = GameInput.PointerPosition;
                foreach (var (rect, image, color, act) in _buttons)
                {
                    var over = RectTransformUtility.RectangleContainsScreenPoint(rect, pos, null);
                    image.color = over ? Color.Lerp(color, Color.white, 0.25f) : color;
                    if (over && GameInput.PointerDown)
                    {
                        act();
                        return;
                    }
                }
            }

            private void Participate()
            {
                CopyAndOpenSurvey();
                Quit();
            }

            private void Quit()
            {
                var quit = _quit;
                Close();
                quit?.Invoke();
            }

            private void Close()
            {
                Instance = null;
                Destroy(gameObject);
            }
        }

        public static bool OpenSurvey()
        {
            var url = SurveyUrl;
            if (string.IsNullOrEmpty(url))
                return false;
            Application.OpenURL(url);
            return true;
        }

        // ---- 누적 플레이 시간 ----
        private static float _unsaved;

        /// <summary>저장된 시간 + 아직 저장 안 한 시간(초).</summary>
        public static float PlayTimeSeconds => PlayerPrefs.GetFloat(PlayTimeKey, 0f) + _unsaved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartTimer()
        {
            var go = new GameObject("PlaytestTimer");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Timer>();
        }

        private static void Flush()
        {
            if (_unsaved <= 0f)
                return;
            PlayerPrefs.SetFloat(PlayTimeKey, PlayerPrefs.GetFloat(PlayTimeKey, 0f) + _unsaved);
            PlayerPrefs.Save();
            _unsaved = 0f;
        }

        private sealed class Timer : MonoBehaviour
        {
            private float _sinceSave;

            private void Update()
            {
                if (GameHUD.AutoPlayActive)
                    return; // 봇이 도는 시간은 사람 플레이 시간이 아니다
                _unsaved += Time.unscaledDeltaTime;
                _sinceSave += Time.unscaledDeltaTime;
                if (_sinceSave >= SaveEverySeconds)
                {
                    _sinceSave = 0f;
                    Flush();
                }
            }

            private void OnApplicationPause(bool paused)
            {
                if (paused)
                    Flush();
            }

            private void OnApplicationQuit() => Flush();
        }

        // ---- 내 기록 ----
        private static string GradeName(ItemSlot slot)
        {
            var grade = EquipmentWallet.GetEquipped(slot);
            return grade.HasValue ? EquipmentData.Grades[grade.Value].Name : "-";
        }

        /// <summary>설문에 붙여 넣을 한 줄 기록(한국어 고정 - 받는 쪽에서 읽기 쉽게).</summary>
        public static string RecordLine()
        {
            var minutes = Mathf.FloorToInt(PlayTimeSeconds / 60f);
            return $"[LoopRogue {BuildLabel}] 층 {StageProgress.CurrentStage}/{StageProgress.MaxStage} | 클리어 {SaveReset.GameClears}회 | " +
                   $"누적 사망 {LoopRecord.TotalDeaths} | 레벨 {Mathf.Max(1, RunProgress.Level)} | 플레이 {minutes / 60}시간 {minutes % 60}분 | " +
                   $"장비 {GradeName(ItemSlot.Weapon)}/{GradeName(ItemSlot.Armor)}/{GradeName(ItemSlot.Accessory)} | 유물 {Relics.OwnedCount} | " +
                   $"도감 {Codex.FoundCount()}/{Codex.TotalCount()} | 오류 조각 {GlitchRoom.Fragments} | 기사 {(MemoryKnight.EverDefeated ? "처치" : "-")}";
        }

        /// <summary>설문 칸 하나에 붙여 넣을 수 있는 최대 글자 수 - 구글 시트 칸 한도(5만 자)보다 넉넉히 작게.</summary>
        private const int MaxCopyChars = 45000;

        /// <summary>한 줄 기록 + 이 빌드의 플레이 기록(PlayLogs 폴더) 전체를 클립보드에 복사하고, 담은 기록 파일 수를 돌려준다(10-27 사용자 -
        /// 설문에 로그 전체를 붙여 넣게). 너무 길면 오래된 실행부터 "이번 실행 요약"만 남기고, 그래도 길면 가장 오래된 것부터 뺀다.</summary>
        public static int CopyRecord()
        {
            Flush();
            var logs = new System.Collections.Generic.List<string>();
            try
            {
                var folder = PlayLog.Folder;
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "PlayLog_*.txt");
                    Array.Sort(files, StringComparer.Ordinal); // 파일 이름이 시각순
                    foreach (var f in files)
                    {
                        string text;
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) // 지금 쓰는 중인 파일도 읽는다
                        using (var r = new StreamReader(fs, System.Text.Encoding.UTF8))
                            text = r.ReadToEnd();
                        if (text.Contains($"빌드 {BuildLabel} ")) // 이 빌드에서 한 것만(빌드가 바뀌면 저장도 처음부터라)
                            logs.Add(text.TrimEnd());
                    }
                }
            }
            catch (Exception)
            {
                // 기록을 못 읽어도 한 줄 기록은 복사한다
            }

            // 길면 오래된 것부터 요약만으로 → 그래도 길면 오래된 것부터 뺀다
            const string SummaryMark = "이번 실행 요약";
            int Total() { var n = 0; foreach (var l in logs) n += l.Length + 2; return n; }
            for (var i = 0; i < logs.Count - 1 && Total() > MaxCopyChars; i++)
            {
                var at = logs[i].IndexOf(SummaryMark, StringComparison.Ordinal);
                var head = logs[i].IndexOf('\n') is var h && h > 0 ? logs[i].Substring(0, h) : "";
                if (at > 0)
                    logs[i] = head + "\n(자세한 기록 생략)\n" + logs[i].Substring(at);
            }
            var dropped = 0;
            while (logs.Count > 1 && Total() > MaxCopyChars)
            {
                logs.RemoveAt(0);
                dropped++;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(RecordLine());
            if (dropped > 0)
                sb.AppendLine($"(오래된 실행 {dropped}개는 길어서 뺐음)");
            foreach (var l in logs)
            {
                sb.AppendLine();
                sb.AppendLine(l);
            }
            var all = sb.ToString();
            if (all.Length > MaxCopyChars)
                all = all.Substring(0, MaxCopyChars) + "\n(이후 생략)";
            GUIUtility.systemCopyBuffer = all;
            return logs.Count;
        }
    }
}
