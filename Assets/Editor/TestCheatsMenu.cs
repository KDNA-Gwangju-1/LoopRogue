using UnityEditor;
using UnityEngine;

namespace LoopRogue.EditorTools
{
    /// <summary>테스트용 치트 - 연출 확인 등에 쓴다. 저장(PlayerPrefs)에 바로 반영되고 플레이 중에도 된다.</summary>
    public static class TestCheatsMenu
    {
        /// <summary>아이템 10종을 전부 1개씩(종류마다 최대 1개 규칙이라 그 이상은 못 넣음). 빈 퀵슬롯엔 자동 등록.</summary>
        [MenuItem("LoopRogue/테스트: 아이템 10종 전부 채우기")]
        private static void FillAllItems()
        {
            Inventory.EnsureLoaded();
            var given = 0;
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var type = (ItemType)i;
                if (Inventory.Has(type))
                    continue;
                Inventory.Give(type);
                given++;
            }
            PlayerPrefs.Save();
            Debug.Log($"[테스트] 아이템 {given}종 채움 - 이제 {ItemInfo.Count}종 전부 있음");
        }
    }
}
