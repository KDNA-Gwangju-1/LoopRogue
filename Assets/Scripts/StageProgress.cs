using UnityEngine;

namespace LoopRogue
{
    /// <summary>지금 도전 중인 스테이지(1~MaxStage) - 보스를 잡아야만 다음 스테이지로 넘어간다
    /// ("스테이지를 10개 만들어서 보스 클리어하면 로비로, 그다음 스테이지로 이동" 요청). 죽는 건
    /// 스테이지 진행에 영향이 없다 - 같은 스테이지 안에서 방1로 리셋될 뿐(레벨/스탯이 유지되는
    /// 것과 같은 원칙, LoopManager.OnPlayerDied 참고). 골드/장비처럼 PlayerPrefs로 영구 저장.</summary>
    public static class StageProgress
    {
        public const int MaxStage = 10;
        private const string StageKey = "LoopRogue_CurrentStage";

        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }
        private static int _stage;

        public static int CurrentStage
        {
            get
            {
                EnsureLoaded();
                return _stage;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _stage = PlayerPrefs.GetInt(StageKey, 1);
            _loaded = true;
        }

        /// <summary>MaxStage에서 더 안 올라간다 - 마지막 스테이지 보스를 계속 다시 잡아도 그냥
        /// 그 스테이지가 반복될 뿐(2주 프로토타입 범위 - "진짜 엔딩" 연출은 나중에).</summary>
        public static void AdvanceStage()
        {
            EnsureLoaded();
            _stage = Mathf.Min(_stage + 1, MaxStage);
            PlayerPrefs.SetInt(StageKey, _stage);
            PlayerPrefs.Save();
        }
    }
}
