using System;
using System.IO;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>테스트 배포용 - 타이틀의 "설문 참여"(설문 링크 열기)와 "내 기록 복사"(설문에 붙여 넣을 한 줄 기록), 그리고 누적 플레이 시간.
    /// 설문 링크는 exe 옆 survey_url.txt(에디터에선 프로젝트 폴더)가 있으면 그걸, 없으면 DefaultSurveyUrl - 파일만 바꾸면 다시 빌드 안 해도 된다.</summary>
    public static class Playtest
    {
        public const string DefaultSurveyUrl = "";
        private const string UrlFileName = "survey_url.txt";
        private const string PlayTimeKey = "LoopRogue_PlayTimeSeconds";
        private const float SaveEverySeconds = 30f;

        /// <summary>이 빌드를 만든 날(기록에 같이 적어 어느 빌드로 했는지 구분).</summary>
        public const string BuildLabel = "2026-10-13";

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

        /// <summary>기록을 클립보드에 복사하고 그 줄을 돌려준다.</summary>
        public static string CopyRecord()
        {
            Flush();
            var line = RecordLine();
            GUIUtility.systemCopyBuffer = line;
            return line;
        }
    }
}
