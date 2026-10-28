using UnityEditor;
using UnityEngine;

namespace LoopRogue.EditorTools
{
    /// <summary>테스트용 치트 - 연출 확인 등에 쓴다. 저장(PlayerPrefs)에 바로 반영되고 플레이 중에도 된다.</summary>
    public static class TestCheatsMenu
    {
        /// <summary>아이템 10종을 가질 수 있는 만큼(보통 1개, 연금술사의 주머니면 2개). 빈 퀵슬롯엔 자동 등록.</summary>
        [MenuItem("LoopRogue/테스트: 아이템 10종 전부 채우기")]
        private static void FillAllItems()
        {
            Inventory.EnsureLoaded();
            var given = 0;
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var type = (ItemType)i;
                while (Inventory.CanHold(type)) // 연금술사의 주머니가 있으면 2개씩
                {
                    Inventory.Give(type);
                    given++;
                }
            }
            PlayerPrefs.Save();
            Debug.Log($"[테스트] 아이템 {given}개 채움");
        }

        /// <summary>Unknown 벼리기 연출만 미리 본다(10-28) - 저장은 안 바뀐다. Play 중에만.</summary>
        [MenuItem("LoopRogue/테스트: Unknown 벼리기 연출 보기 (Play 중)")]
        private static void PreviewUnknownForge() => GachaReveal.PlayUnknownForge(() => Debug.Log("[테스트] Unknown 벼리기 연출 끝"));

        [MenuItem("LoopRogue/테스트: Unknown 벼리기 연출 보기 (Play 중)", true)]
        private static bool PreviewUnknownForgeValid() => EditorApplication.isPlaying;

        // ---- 유물 테스트(10-27) - 36개 전부(보스·일반·히든 하위 메뉴), 누를 때마다 켜고 끄기(콘솔에 켬/끔이 찍힌다). Play 중에도 바로 적용된다. 진짜 저장에 남으니 테스트 맵처럼
        // "끝나면 저장 되돌림" 메뉴 안에서 쓰는 게 안전하다. ----
        private const string RelicMenu = "LoopRogue/테스트: 유물 켜고 끄기/";

        [MenuItem(RelicMenu + "보스 유물/1층 돌진의 뿔")] private static void RelicChargeHorn() => ToggleRelic(RelicType.ChargeHorn);
        [MenuItem(RelicMenu + "보스 유물/2층 대지의 망치")] private static void RelicEarthHammer() => ToggleRelic(RelicType.EarthHammer);
        [MenuItem(RelicMenu + "보스 유물/3층 십자 문장")] private static void RelicCrossCrest() => ToggleRelic(RelicType.CrossCrest);
        [MenuItem(RelicMenu + "보스 유물/4층 망령의 깃발")] private static void RelicPhantomBanner() => ToggleRelic(RelicType.PhantomBanner);
        [MenuItem(RelicMenu + "보스 유물/5층 파동의 핵")] private static void RelicPulseCore() => ToggleRelic(RelicType.PulseCore);
        [MenuItem(RelicMenu + "보스 유물/6층 저격수의 눈")] private static void RelicSniperEye() => ToggleRelic(RelicType.SniperEye);
        [MenuItem(RelicMenu + "보스 유물/7층 관통의 인장")] private static void RelicPiercingSeal() => ToggleRelic(RelicType.PiercingSeal);
        [MenuItem(RelicMenu + "보스 유물/8층 폭군의 갑주")] private static void RelicTyrantPlate() => ToggleRelic(RelicType.TyrantPlate);
        [MenuItem(RelicMenu + "보스 유물/9층 사형 집행자")] private static void RelicExecutioner() => ToggleRelic(RelicType.Executioner);
        [MenuItem(RelicMenu + "보스 유물/10층 끝의 왕관")] private static void RelicCrownOfEnd() => ToggleRelic(RelicType.CrownOfEnd);
        [MenuItem(RelicMenu + "일반 유물/피의 계약")] private static void RelicBloodPact() => ToggleRelic(RelicType.BloodPact);
        [MenuItem(RelicMenu + "일반 유물/연쇄 폭발")] private static void RelicChainBlast() => ToggleRelic(RelicType.ChainBlast);
        [MenuItem(RelicMenu + "일반 유물/두 번째 숨")] private static void RelicSecondWind() => ToggleRelic(RelicType.SecondWind);
        [MenuItem(RelicMenu + "일반 유물/오래된 보물지도")] private static void RelicTreasureHunter() => ToggleRelic(RelicType.TreasureHunter);
        [MenuItem(RelicMenu + "일반 유물/회전 칼날")] private static void RelicSpinningBlade() => ToggleRelic(RelicType.SpinningBlade);
        [MenuItem(RelicMenu + "일반 유물/사냥꾼의 눈")] private static void RelicHunterEye() => ToggleRelic(RelicType.HunterEye);
        [MenuItem(RelicMenu + "일반 유물/탐욕")] private static void RelicGreed() => ToggleRelic(RelicType.Greed);
        [MenuItem(RelicMenu + "일반 유물/질풍")] private static void RelicGale() => ToggleRelic(RelicType.Gale);
        [MenuItem(RelicMenu + "일반 유물/재생의 이끼")] private static void RelicRegenMoss() => ToggleRelic(RelicType.RegenMoss);
        [MenuItem(RelicMenu + "일반 유물/철의 피부")] private static void RelicIronSkin() => ToggleRelic(RelicType.IronSkin);
        [MenuItem(RelicMenu + "일반 유물/광전사")] private static void RelicBerserker() => ToggleRelic(RelicType.Berserker);
        [MenuItem(RelicMenu + "일반 유물/첫 일격")] private static void RelicFirstStrike() => ToggleRelic(RelicType.FirstStrike);
        [MenuItem(RelicMenu + "일반 유물/그림자 걸음")] private static void RelicShadowStep() => ToggleRelic(RelicType.ShadowStep);
        [MenuItem(RelicMenu + "일반 유물/소용돌이")] private static void RelicWhirlwind() => ToggleRelic(RelicType.Whirlwind);
        [MenuItem(RelicMenu + "일반 유물/멈춘 회중시계")] private static void RelicStoppedWatch() => ToggleRelic(RelicType.StoppedWatch);
        [MenuItem(RelicMenu + "일반 유물/되풀이의 각인")] private static void RelicRecurrenceMark() => ToggleRelic(RelicType.RecurrenceMark);
        [MenuItem(RelicMenu + "일반 유물/퍼즐 장인의 장갑")] private static void RelicPuzzleGlove() => ToggleRelic(RelicType.PuzzleGlove);
        [MenuItem(RelicMenu + "일반 유물/상인의 인장")] private static void RelicMerchantSeal() => ToggleRelic(RelicType.MerchantSeal);
        [MenuItem(RelicMenu + "일반 유물/계약자의 낙인")] private static void RelicContractBrand() => ToggleRelic(RelicType.ContractBrand);
        [MenuItem(RelicMenu + "일반 유물/피의 사냥꾼")] private static void RelicBloodHunter() => ToggleRelic(RelicType.BloodHunter);
        [MenuItem(RelicMenu + "일반 유물/정예 사냥꾼")] private static void RelicEliteHunter() => ToggleRelic(RelicType.EliteHunter);
        [MenuItem(RelicMenu + "일반 유물/메아리 칼날")] private static void RelicEchoBlade() => ToggleRelic(RelicType.EchoBlade);
        [MenuItem(RelicMenu + "일반 유물/연금술사의 주머니")] private static void RelicAlchemistPouch() => ToggleRelic(RelicType.AlchemistPouch);
        [MenuItem(RelicMenu + "일반 유물/화약 장인")] private static void RelicPowderMaster() => ToggleRelic(RelicType.PowderMaster);
        [MenuItem(RelicMenu + "일반 유물/사냥꾼의 올가미")] private static void RelicSnareHunter() => ToggleRelic(RelicType.SnareHunter);
        [MenuItem(RelicMenu + "히든 유물/기사의 기억")] private static void RelicKnightMemory() => ToggleRelic(RelicType.KnightMemory);

        private static System.Collections.Generic.IEnumerable<RelicType> AllRelics => (RelicType[])System.Enum.GetValues(typeof(RelicType));

        [MenuItem(RelicMenu + "유물 전부 켜기", false, 100)]
        private static void AllRelicsOn()
        {
            foreach (var r in AllRelics)
                Grant(r);
            PlayerPrefs.Save();
            Debug.Log("[테스트] 유물 전부 켬");
        }

        [MenuItem(RelicMenu + "유물 전부 끄기", false, 101)]
        private static void AllRelicsOff()
        {
            foreach (var r in AllRelics)
                Relics.RemoveForTest(r);
            PlayerPrefs.Save();
            Debug.Log("[테스트] 유물 전부 끔");
        }

        [MenuItem(RelicMenu + "가진 유물 콘솔에 보기", false, 102)]
        private static void ListRelics() =>
            Debug.Log("[테스트] 가진 유물: " + string.Join(", ", System.Linq.Enumerable.Select(Relics.OwnedRelics(), Relics.Name)));

        private static void ToggleRelic(RelicType type)
        {
            if (Relics.Has(type))
                Relics.RemoveForTest(type);
            else
                Grant(type);
            PlayerPrefs.Save();
            Debug.Log($"[테스트] {Relics.Name(type)} {(Relics.Has(type) ? "켬" : "끔")}");
        }

        /// <summary>Play 중이면 진짜로 얻은 것처럼(피의 계약 최대 체력·끝의 왕관 스탯이 바로 반영) - 아니면 저장에만. 끌 때 그 스탯은 되돌리지 않는다.</summary>
        private static void Grant(RelicType type)
        {
            var player = Application.isPlaying ? Object.FindFirstObjectByType<PlayerActor>() : null;
            if (player != null)
                Relics.Acquire(type, player);
            else
                Relics.GrantForTest(type);
        }

        [MenuItem("LoopRogue/테스트: 골드 +10000")]
        private static void AddGold()
        {
            GoldWallet.Add(10000);
            PlayerPrefs.Save();
            Debug.Log($"[테스트] 골드 +10000 → {GoldWallet.Gold}");
        }

        [MenuItem("LoopRogue/테스트: 이 층 사망 수 +1 (되풀이의 각인 확인)")]
        private static void AddStageDeath()
        {
            StageProgress.AddStageDeath();
            PlayerPrefs.Save();
            Debug.Log($"[테스트] 이 층 사망 수 {StageProgress.DeathsThisStage}");
        }
    }
}
