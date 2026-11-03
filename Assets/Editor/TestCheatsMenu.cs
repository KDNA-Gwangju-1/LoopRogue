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

        /// <summary>마석 유물 뽑기 연출만 미리 본다(10-29) - 무작위 일반 유물 그림으로, 저장(마석·유물)은 안 바뀐다. Play 중에만.</summary>
        [MenuItem("LoopRogue/테스트: 마석 유물 뽑기 연출 보기 (Play 중)")]
        private static void PreviewRelicDraw()
        {
            var relic = (RelicType)UnityEngine.Random.Range(Relics.BossRelicCount, System.Enum.GetValues(typeof(RelicType)).Length);
            GachaReveal.PlayRelicDraw(relic, () => Debug.Log($"[테스트] 마석 유물 뽑기 연출 끝 ({relic})"));
        }

        [MenuItem("LoopRogue/테스트: 마석 유물 뽑기 연출 보기 (Play 중)", true)]
        private static bool PreviewRelicDrawValid() => EditorApplication.isPlaying;

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

        // ---- 특성 테스트(11-02) - 포인트·로비 화면이 생기기 전까지 여기서 켜고 끈다. ----
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/대시 1 분신 대시")] private static void TraitDashClone() => ToggleTrait(TraitType.DashClone);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/대시 2 되돌아오기")] private static void TraitDashReturn() => ToggleTrait(TraitType.DashReturn);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/대시 3 연속 대시")] private static void TraitDashChain() => ToggleTrait(TraitType.DashChain);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/대시 4 대시 파동")] private static void TraitDashWave() => ToggleTrait(TraitType.DashWave);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/대시 5 표식 폭발")] private static void TraitDashMark() => ToggleTrait(TraitType.DashMark);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/회전 1 삼연 회전")] private static void TraitSpinHold() => ToggleTrait(TraitType.SpinHold);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/회전 2 회전 중 행동")] private static void TraitSpinFree() => ToggleTrait(TraitType.SpinFree);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/회전 3 긴 회전")] private static void TraitSpinLong() => ToggleTrait(TraitType.SpinLong);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/회전 4 칼날 방패")] private static void TraitSpinGuard() => ToggleTrait(TraitType.SpinGuard);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/회전 5 끝없는 회전")] private static void TraitSpinStorm() => ToggleTrait(TraitType.SpinStorm);

        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/탐색 1 넓은 시야")] private static void TraitExploreVision() => ToggleTrait(TraitType.ExploreVision);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/탐색 2 길잡이")] private static void TraitExploreExit() => ToggleTrait(TraitType.ExploreExit);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/탐색 3 보물 감각")] private static void TraitExploreChest() => ToggleTrait(TraitType.ExploreChest);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/탐색 4 퍼즐 달인")] private static void TraitExplorePuzzle() => ToggleTrait(TraitType.ExplorePuzzle);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/탐색 5 노다지")] private static void TraitExploreFind() => ToggleTrait(TraitType.ExploreFind);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/상인 1 흥정")] private static void TraitTradeMerchant() => ToggleTrait(TraitType.TradeMerchant);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/상인 2 단골")] private static void TraitTradeShop() => ToggleTrait(TraitType.TradeShop);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/상인 3 큰손")] private static void TraitTradeGold() => ToggleTrait(TraitType.TradeGold);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/상인 4 보험")] private static void TraitTradeInsurance() => ToggleTrait(TraitType.TradeInsurance);
        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/상인 5 행운의 손님")] private static void TraitTradeLuck() => ToggleTrait(TraitType.TradeLuck);

        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/특성 포인트 +1", false, 100)]
        private static void AddTraitPoint()
        {
            Traits.AddBonusPoints(1);
            PlayerPrefs.Save();
            Debug.Log($"[테스트] 특성 포인트 +1 → 남은 {Traits.FreePoints} / 전체 {Traits.EarnedPoints}");
        }

        [MenuItem("LoopRogue/테스트: 특성 켜고 끄기/특성 전부 되돌리기", false, 101)]
        private static void ResetTraits()
        {
            Traits.ResetAll();
            Debug.Log("[테스트] 특성 전부 되돌림");
        }

        private static void ToggleTrait(TraitType type)
        {
            Traits.Set(type, !Traits.Has(type));
            PlayerPrefs.Save();
            Debug.Log($"[테스트] 특성 {type} {(Traits.Has(type) ? "켬" : "끔")}");
        }

        /// <summary>10층 클리어 표시(로비 층 선택) 켜고 끄기 - 엔딩을 안 보고도 로비 문 층 선택 창을 확인할 수 있다.</summary>
        [MenuItem("LoopRogue/테스트: 10층 클리어 표시 켜고 끄기 (로비 층 선택)")]
        private static void ToggleFinalCleared()
        {
            if (StageProgress.FinalBossCleared)
                StageProgress.ClearFinalBossClearedForTest();
            else
                StageProgress.MarkFinalBossCleared();
            Debug.Log($"[테스트] 10층 클리어 표시 {(StageProgress.FinalBossCleared ? "켬" : "끔")} - 로비를 다시 열면 문이 {(StageProgress.FinalBossCleared ? "층 선택" : "지금 층 시작")}");
        }

        /// <summary>그림자 걸음 이펙트를 실제 게임 화면에서 재생하며 프레임마다 PNG로 찍는다(11-02 - 이펙트 확인용). 던전에서 Play 중에만.
        /// 가장 가까운 몹(없으면 플레이어 오른쪽 칸)에 벤 순간 → 잠시 뒤 다시 베기. 저장 위치: 프로젝트/Screenshots/shadowstep_00.png…</summary>
        [MenuItem("LoopRogue/테스트: 그림자 걸음 이펙트 캡처 (던전 Play 중)")]
        private static void CaptureShadowStep()
        {
            var player = Object.FindFirstObjectByType<PlayerActor>();
            if (player == null)
            {
                Debug.LogWarning("[테스트] 던전에서 Play 중에만 됩니다(플레이어가 없음)");
                return;
            }
            EnemyActor target = null;
            var best = float.MaxValue;
            foreach (var e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                if (e.Stats == null || e.Stats.IsDead)
                    continue;
                var d = (e.transform.position - player.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    target = e;
                }
            }
            var pos = target != null ? target.transform.position : player.transform.position + Vector3.right * GridConstants.CellSize;
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", "Screenshots");
            FxCapture.Run(folder, "shadowstep", 0.9f, 0.06f,
                () => player.PreviewShadowMark(pos, target),
                () => player.PreviewShadowStrike(pos, target));
        }

        [MenuItem("LoopRogue/테스트: 그림자 걸음 이펙트 캡처 (던전 Play 중)", true)]
        private static bool CaptureShadowStepValid() => EditorApplication.isPlaying;

        /// <summary>특성 "노다지" 연출을 실제 화면에서 재생하며 PNG로 찍는다(골드는 안 준다). 프로젝트/Screenshots/bonanza_00.png…</summary>
        [MenuItem("LoopRogue/테스트: 노다지 연출 캡처 (던전 Play 중)")]
        private static void CaptureBonanza()
        {
            var player = Object.FindFirstObjectByType<PlayerActor>();
            var room = Object.FindFirstObjectByType<RoomController>();
            if (player == null || room == null)
            {
                Debug.LogWarning("[테스트] 던전에서 Play 중에만 됩니다");
                return;
            }
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", "Screenshots");
            FxCapture.Run(folder, "bonanza", 1.4f, 0.06f, () =>
            {
                room.ShowMessage(Loc.F("<color=#FFD966>노다지!</color> 골드 +{0}", room.RoomClearGold * Traits.ExploreFindGoldRooms));
                HitFeedback.OnBonanza(player);
            });
        }

        [MenuItem("LoopRogue/테스트: 노다지 연출 캡처 (던전 Play 중)", true)]
        private static bool CaptureBonanzaValid() => EditorApplication.isPlaying;

        /// <summary>대시 연출 캡처(11-03 분신 속도선 확인용) - 본체 대시 연출 + 분신 두 갈래 + 대시 파동 그림자를 실제 화면에서 재생하며 0.04초마다 PNG.
        /// 플레이어는 움직이지 않고 연출만(피해 없음). 프로젝트/Screenshots/dashfx_00.png…</summary>
        [MenuItem("LoopRogue/테스트: 대시·분신 연출 캡처 (던전 Play 중)")]
        private static void CaptureDashFx()
        {
            var player = Object.FindFirstObjectByType<PlayerActor>();
            if (player == null)
            {
                Debug.LogWarning("[테스트] 던전에서 Play 중에만 됩니다");
                return;
            }
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", "Screenshots");
            var cell = GridConstants.CellSize;
            var here = player.GridWorldPosition;
            FxCapture.Run(folder, "dashfx", 0.8f, 0.04f, () =>
            {
                var from = here - new Vector3(2f * cell, 0f, 0f);
                HitFeedback.OnDash(player, from); // 본체 대시(왼쪽 2칸에서 지금 자리로)
                HitFeedback.OnDashClone(player, from, from + new Vector3(2f * cell, 2f * cell, 0f)); // 분신 - 오른쪽 위
                HitFeedback.OnDashClone(player, from, from + new Vector3(2f * cell, -2f * cell, 0f)); // 분신 - 오른쪽 아래
                HitFeedback.OnDashWaveShadows(player, from, here, new Vector3(0f, cell, 0f)); // 대시 파동 그림자
            });
        }

        [MenuItem("LoopRogue/테스트: 대시·분신 연출 캡처 (던전 Play 중)", true)]
        private static bool CaptureDashFxValid() => EditorApplication.isPlaying;

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

        // ---- 테스트: 장비 등급(11-03 사용자 "부위별 장비 장착 가능하게") - 등급과 상관없이 그대로 끼운다. Play 중이면 용사 그림이 바로 바뀐다(능력치는 다음 방부터) ----
        private const string GearMenu = "LoopRogue/테스트: 장비 등급/";

        [MenuItem(GearMenu + "무기/미장착", false, 0)] private static void GearWeaponNone() => SetGear(ItemSlot.Weapon, null);
        [MenuItem(GearMenu + "무기/01 일반", false, 1)] private static void GearWeaponCommon() => SetGear(ItemSlot.Weapon, ItemGrade.Common);
        [MenuItem(GearMenu + "무기/02 고급", false, 2)] private static void GearWeaponUncommon() => SetGear(ItemSlot.Weapon, ItemGrade.Uncommon);
        [MenuItem(GearMenu + "무기/03 희귀", false, 3)] private static void GearWeaponRare() => SetGear(ItemSlot.Weapon, ItemGrade.Rare);
        [MenuItem(GearMenu + "무기/04 영웅", false, 4)] private static void GearWeaponEpic() => SetGear(ItemSlot.Weapon, ItemGrade.Epic);
        [MenuItem(GearMenu + "무기/05 전설", false, 5)] private static void GearWeaponLegendary() => SetGear(ItemSlot.Weapon, ItemGrade.Legendary);
        [MenuItem(GearMenu + "무기/06 신화", false, 6)] private static void GearWeaponMythic() => SetGear(ItemSlot.Weapon, ItemGrade.Mythic);
        [MenuItem(GearMenu + "무기/07 고대", false, 7)] private static void GearWeaponAncient() => SetGear(ItemSlot.Weapon, ItemGrade.Ancient);
        [MenuItem(GearMenu + "무기/08 초월", false, 8)] private static void GearWeaponTranscendent() => SetGear(ItemSlot.Weapon, ItemGrade.Transcendent);
        [MenuItem(GearMenu + "무기/09 영원", false, 9)] private static void GearWeaponEternal() => SetGear(ItemSlot.Weapon, ItemGrade.Eternal);
        [MenuItem(GearMenu + "무기/10 Unknown", false, 10)] private static void GearWeaponUnknown() => SetGear(ItemSlot.Weapon, ItemGrade.Unknown);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/미장착", false, 20)] private static void GearArmorNone() => SetGear(ItemSlot.Armor, null);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/01 일반", false, 21)] private static void GearArmorCommon() => SetGear(ItemSlot.Armor, ItemGrade.Common);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/02 고급", false, 22)] private static void GearArmorUncommon() => SetGear(ItemSlot.Armor, ItemGrade.Uncommon);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/03 희귀", false, 23)] private static void GearArmorRare() => SetGear(ItemSlot.Armor, ItemGrade.Rare);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/04 영웅", false, 24)] private static void GearArmorEpic() => SetGear(ItemSlot.Armor, ItemGrade.Epic);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/05 전설", false, 25)] private static void GearArmorLegendary() => SetGear(ItemSlot.Armor, ItemGrade.Legendary);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/06 신화", false, 26)] private static void GearArmorMythic() => SetGear(ItemSlot.Armor, ItemGrade.Mythic);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/07 고대", false, 27)] private static void GearArmorAncient() => SetGear(ItemSlot.Armor, ItemGrade.Ancient);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/08 초월", false, 28)] private static void GearArmorTranscendent() => SetGear(ItemSlot.Armor, ItemGrade.Transcendent);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/09 영원", false, 29)] private static void GearArmorEternal() => SetGear(ItemSlot.Armor, ItemGrade.Eternal);
        [MenuItem(GearMenu + "방어구(투구+갑옷)/10 Unknown", false, 30)] private static void GearArmorUnknown() => SetGear(ItemSlot.Armor, ItemGrade.Unknown);
        [MenuItem(GearMenu + "반지/미장착", false, 40)] private static void GearAccessoryNone() => SetGear(ItemSlot.Accessory, null);
        [MenuItem(GearMenu + "반지/01 일반", false, 41)] private static void GearAccessoryCommon() => SetGear(ItemSlot.Accessory, ItemGrade.Common);
        [MenuItem(GearMenu + "반지/02 고급", false, 42)] private static void GearAccessoryUncommon() => SetGear(ItemSlot.Accessory, ItemGrade.Uncommon);
        [MenuItem(GearMenu + "반지/03 희귀", false, 43)] private static void GearAccessoryRare() => SetGear(ItemSlot.Accessory, ItemGrade.Rare);
        [MenuItem(GearMenu + "반지/04 영웅", false, 44)] private static void GearAccessoryEpic() => SetGear(ItemSlot.Accessory, ItemGrade.Epic);
        [MenuItem(GearMenu + "반지/05 전설", false, 45)] private static void GearAccessoryLegendary() => SetGear(ItemSlot.Accessory, ItemGrade.Legendary);
        [MenuItem(GearMenu + "반지/06 신화", false, 46)] private static void GearAccessoryMythic() => SetGear(ItemSlot.Accessory, ItemGrade.Mythic);
        [MenuItem(GearMenu + "반지/07 고대", false, 47)] private static void GearAccessoryAncient() => SetGear(ItemSlot.Accessory, ItemGrade.Ancient);
        [MenuItem(GearMenu + "반지/08 초월", false, 48)] private static void GearAccessoryTranscendent() => SetGear(ItemSlot.Accessory, ItemGrade.Transcendent);
        [MenuItem(GearMenu + "반지/09 영원", false, 49)] private static void GearAccessoryEternal() => SetGear(ItemSlot.Accessory, ItemGrade.Eternal);
        [MenuItem(GearMenu + "반지/10 Unknown", false, 50)] private static void GearAccessoryUnknown() => SetGear(ItemSlot.Accessory, ItemGrade.Unknown);
        [MenuItem(GearMenu + "세 부위 전부/미장착", false, 80)] private static void GearAllNone() => SetAllGear(null);
        [MenuItem(GearMenu + "세 부위 전부/01 일반", false, 81)] private static void GearAllCommon() => SetAllGear(ItemGrade.Common);
        [MenuItem(GearMenu + "세 부위 전부/02 고급", false, 82)] private static void GearAllUncommon() => SetAllGear(ItemGrade.Uncommon);
        [MenuItem(GearMenu + "세 부위 전부/03 희귀", false, 83)] private static void GearAllRare() => SetAllGear(ItemGrade.Rare);
        [MenuItem(GearMenu + "세 부위 전부/04 영웅", false, 84)] private static void GearAllEpic() => SetAllGear(ItemGrade.Epic);
        [MenuItem(GearMenu + "세 부위 전부/05 전설", false, 85)] private static void GearAllLegendary() => SetAllGear(ItemGrade.Legendary);
        [MenuItem(GearMenu + "세 부위 전부/06 신화", false, 86)] private static void GearAllMythic() => SetAllGear(ItemGrade.Mythic);
        [MenuItem(GearMenu + "세 부위 전부/07 고대", false, 87)] private static void GearAllAncient() => SetAllGear(ItemGrade.Ancient);
        [MenuItem(GearMenu + "세 부위 전부/08 초월", false, 88)] private static void GearAllTranscendent() => SetAllGear(ItemGrade.Transcendent);
        [MenuItem(GearMenu + "세 부위 전부/09 영원", false, 89)] private static void GearAllEternal() => SetAllGear(ItemGrade.Eternal);
        [MenuItem(GearMenu + "세 부위 전부/10 Unknown", false, 90)] private static void GearAllUnknown() => SetAllGear(ItemGrade.Unknown);

        private static void SetGear(ItemSlot slot, ItemGrade? grade)
        {
            EquipmentWallet.ForceForTest(slot, grade);
            Debug.Log($"[테스트] {slot} = {(grade.HasValue ? grade.Value.ToString() : "미장착")}" + (EditorApplication.isPlaying ? " (용사 그림 바로 바뀜, 능력치는 다음 방부터)" : ""));
        }

        private static void SetAllGear(ItemGrade? grade)
        {
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                EquipmentWallet.ForceForTest(slot, grade);
            Debug.Log($"[테스트] 세 부위 전부 = {(grade.HasValue ? grade.Value.ToString() : "미장착")}");
        }
    }
}
