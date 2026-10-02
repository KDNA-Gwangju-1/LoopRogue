using UnityEngine;

namespace LoopRogue
{
    /// <summary>모든 저장 데이터(골드/스테이지/장비/영약/가챠 횟수/진행 중인 런) 초기화 - 타이틀의 "초기화"
    /// 버튼용. 이 게임은 PlayerPrefs에 LoopRogue 데이터만 쓰므로 DeleteAll로 한 번에 지우고, 각 저장
    /// 클래스가 메모리에 캐싱해둔 값도 다시 읽게 한다(안 그러면 같은 Play 세션 안에선 옛 값이 남는다).</summary>
    public static class SaveReset
    {
        public static void ResetAll()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            GoldWallet.Reload();
            EquipmentWallet.Reload();
            StatPotionWallet.Reload();
            StageProgress.Reload();
            GachaSystem.Reload();
            Inventory.Reload();
            Relics.Reload();
            RunProgress.Reload(); // 기본값 계산에 위 지갑들을 쓰므로 마지막에.
        }
    }
}
