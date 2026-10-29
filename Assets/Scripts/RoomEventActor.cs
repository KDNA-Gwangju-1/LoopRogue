using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>순서를 바꾸지 말 것 - 봇 통계 배열이 이 순서를 쓴다(새 이벤트는 맨 뒤에).</summary>
    public enum RoomEventType
    {
        TreasureChest, // 보물상자 - 골드 + 아이템
        HealingSpring, // 회복 샘 - 체력 전부 회복
        BlessingAltar, // 축복 제단 - 레벨업 카드 1장 추가 선택
        CursedChest,   // 저주받은 상자 - 50%: 골드 대박 / 50%: 현재 체력 30% 감소
        Mimic,         // 미믹 - 보물상자와 똑같이 생겼는데 밟으면 상자 괴물이 튀어나온다(잡으면 큰 보상)
        BloodAltar,    // 피의 제단 - 최대 체력 일부를 바치면 이번 시도 동안 주는 피해 증가(고를 수 있음)
        ChallengeFlag, // 도전의 깃발 - 정예 몹 한 무리가 더 나오고, 다 잡으면 골드 + 레벨업 카드
        Hourglass,     // 시간의 모래시계 - 이번 시도에서 한 번 죽어도 되살아난다
        FateDice,      // 운명의 주사위 - 1~6 무작위 결과
        ElixirSpring,  // 영약의 샘 - 무작위 영약 1개(영구)
        Whetstone,     // 숫돌 - 이번 시도 동안 주는 피해 증가
        SecretChest,   // 숨겨진 보물상자 - 금 간 벽(방마다 10%, 층마다 한 번)을 폭탄으로 부수면 나온다(RoomController.TrySpawnSecretWall)
        WishingWell,   // 소원의 우물(10-19) - 골드를 던지면 영약·아이템 2개·레벨업 카드·꽝 중 하나(던질지 고른다)
        Anvil,         // 모루(10-19) - 이번 시도 동안 받는 피해 감소(숫돌의 방어판)
        AdventurerRemains, // 모험가의 유해(10-19) - 뒤지면 아이템 2개 + 골드, 가끔 유해가 일어나 몹이 덤빈다
        MuralChest,    // 벽화의 상자(10-19) - 횃불로 연 잊힌 벽화의 방에만(골드 + 영약 2개, 무작위로는 안 나옴)
    }

    /// <summary>방 이벤트 칸 - 한 시도에 방 3~9를 두 방씩 묶어 묶음마다 하나(그중 하나는 보물상자 확정, LoopManager.RollRoomEvent). 칸 하나를 차지하는 액터라
    /// 몹은 못 지나가고, 플레이어가 그 칸으로 이동하면 발동하고 사라진다(피의 제단은 고르기 전까지 남는다).
    /// 그림은 Resources/Effects/event_{종류}(반복 재생) - 없거나 봇 중이면 색 마름모.</summary>
    public class RoomEventActor : GridActor
    {
        /// <summary>보물상자 골드 = 방 클리어 골드의 이 배수, 저주받은 상자 대박은 그 두 배.</summary>
        public const int TreasureGoldMultiplier = 5;
        public const int SecretChestGoldMultiplier = TreasureGoldMultiplier * 2; // 숨겨진 보물상자 - 보물상자 골드의 2배 + 아이템 2개
        public const int CursedJackpotMultiplier = 10;
        public const float CursedHealthLossRate = 0.3f;
        public const int DiceGoldMultiplier = 4;
        public const float DiceHealRate = 0.5f;
        public const float DiceHealthLossRate = 0.25f;
        public const int DiceExtraEnemies = 2;
        public const int WishCostRooms = 3;          // 소원의 우물 - 던지는 골드 = 방 클리어 골드 × 이만큼
        public const int RemainsGoldMultiplier = 2;  // 모험가의 유해 - 골드 = 방 클리어 골드 × 이만큼
        public const int MuralChestGoldMultiplier = 8; // 벽화의 상자 - 보물상자(×5)보다 넉넉히 + 영약 2개
        public const float RemainsAmbushChance = 0.3f;
        public const int RemainsAmbushEnemies = 3;

        private static readonly System.Random Rng = new System.Random();

        /// <summary>보물상자 말고 나오는 이벤트와 가중치 - 기존 셋은 자주, 새 이벤트 중 센 것(모래시계·영약의 샘)은 드물게.</summary>
        private static readonly (RoomEventType Type, int Weight)[] OtherEvents =
        {
            (RoomEventType.HealingSpring, 3), (RoomEventType.BlessingAltar, 3), (RoomEventType.CursedChest, 3),
            (RoomEventType.Mimic, 2), (RoomEventType.BloodAltar, 2), (RoomEventType.ChallengeFlag, 2),
            (RoomEventType.Hourglass, 1), (RoomEventType.FateDice, 3), (RoomEventType.ElixirSpring, 1), (RoomEventType.Whetstone, 2),
            (RoomEventType.WishingWell, 2), (RoomEventType.Anvil, 2), (RoomEventType.AdventurerRemains, 3), // 10-19 새 이벤트
        };

        public static int TypeCount => System.Enum.GetValues(typeof(RoomEventType)).Length;

        public RoomEventType Type { get; private set; }

        /// <summary>보이는 이름 - 미믹은 밟기 전까진 보물상자로 보인다.</summary>
        public string DisplayName => Name(Type == RoomEventType.Mimic ? RoomEventType.TreasureChest : Type);

        public static string Name(RoomEventType type) => type switch
        {
            RoomEventType.TreasureChest => Loc.T("보물상자"),
            RoomEventType.HealingSpring => Loc.T("회복 샘"),
            RoomEventType.BlessingAltar => Loc.T("축복 제단"),
            RoomEventType.CursedChest => Loc.T("저주받은 상자"),
            RoomEventType.Mimic => Loc.T("미믹"),
            RoomEventType.BloodAltar => Loc.T("피의 제단"),
            RoomEventType.ChallengeFlag => Loc.T("도전의 깃발"),
            RoomEventType.Hourglass => Loc.T("시간의 모래시계"),
            RoomEventType.FateDice => Loc.T("운명의 주사위"),
            RoomEventType.ElixirSpring => Loc.T("영약의 샘"),
            RoomEventType.SecretChest => Loc.T("숨겨진 보물상자"),
            RoomEventType.WishingWell => Loc.T("소원의 우물"),
            RoomEventType.Anvil => Loc.T("모루"),
            RoomEventType.AdventurerRemains => Loc.T("모험가의 유해"),
            RoomEventType.MuralChest => Loc.T("벽화의 상자"),
            _ => Loc.T("숫돌"),
        };

        public void Initialize(RoomEventType type)
        {
            Type = type;
            Stats = new CharacterStats(1f, 0f);

            // 도트 그림(제자리에서 반짝이는 반복) - 미믹은 보물상자 그림 그대로.
            var look = type == RoomEventType.Mimic || type == RoomEventType.SecretChest || type == RoomEventType.MuralChest ? RoomEventType.TreasureChest : type;
            // 벽화의 상자는 낡은 상자 그림(event_MuralChest, 10-27 사용자 - 녹슨 청동·금·이끼·거미줄, 푸른 먼지) - 없으면 보물상자 그림
            var fx = (type == RoomEventType.MuralChest ? Fx.Play("event_MuralChest", transform.position, 0.95f, fps: 4f, loop: true, sortingOrder: 0, parent: transform) : null)
                     ?? Fx.Play($"event_{look}", transform.position, 0.95f, fps: 6f, loop: true, sortingOrder: 0, parent: transform);
            if (fx != null)
                return;

            var color = look switch
            {
                RoomEventType.TreasureChest => new Color(1f, 0.8f, 0.2f),
                RoomEventType.HealingSpring => new Color(0.3f, 0.7f, 1f),
                RoomEventType.BlessingAltar => new Color(0.85f, 0.5f, 1f),
                RoomEventType.CursedChest => new Color(0.45f, 0.15f, 0.55f),
                RoomEventType.BloodAltar => new Color(0.75f, 0.1f, 0.15f),
                RoomEventType.ChallengeFlag => new Color(1f, 0.35f, 0.2f),
                RoomEventType.Hourglass => new Color(0.95f, 0.85f, 0.5f),
                RoomEventType.FateDice => new Color(0.95f, 0.95f, 0.95f),
                RoomEventType.ElixirSpring => new Color(1f, 0.5f, 0.8f),
                RoomEventType.WishingWell => new Color(0.3f, 0.55f, 0.95f),
                RoomEventType.Anvil => new Color(0.4f, 0.42f, 0.5f),
                RoomEventType.AdventurerRemains => new Color(0.85f, 0.8f, 0.65f),
                _ => new Color(0.6f, 0.65f, 0.7f),
            };
            // 이벤트 도트 그림(보물상자 / 회복 샘 / 축복 제단 / 저주받은 상자) - 없으면 예전 색 마름모
            if (VisualUtil.TryCreateProp(gameObject, $"Event_{look}", sortingOrder: 0) != null)
                return;
            var renderer = VisualUtil.CreateSquareVisual(gameObject, color, GridConstants.CellSize * 0.45f, sortingOrder: 0);
            transform.rotation = Quaternion.Euler(0f, 0f, 45f); // 마름모 - 몹(사각형)과 구분
            renderer.color = color;
        }

        /// <summary>보물상자를 뺀 나머지 중 하나(가중치) - 보물상자는 층마다 따로 1개 확정.</summary>
        public static RoomEventType RollNonChestType()
        {
            var roll = Rng.Next(OtherEvents.Sum(e => e.Weight));
            foreach (var (type, weight) in OtherEvents)
            {
                if (roll < weight)
                    return type;
                roll -= weight;
            }
            return RoomEventType.HealingSpring;
        }

        /// <summary>플레이어가 밟았을 때. 결과 안내 문구를 돌려준다. consumed = false면 칸이 남는다(피의 제단 - 고르는 창이 떴을 때).</summary>
        public string Trigger(PlayerActor player, RoomController room, out bool consumed)
        {
            consumed = true;
            var roomClearGold = room.RoomClearGold;
            switch (Type)
            {
                case RoomEventType.TreasureChest:
                {
                    var gold = roomClearGold * TreasureGoldMultiplier * (Relics.Has(RelicType.TreasureHunter) ? Relics.TreasureHunterChestGold : 1); // 유물 "오래된 보물지도"
                    GoldWallet.Add(gold);
                    var item = Inventory.GiveRandomMissing(); // 없는 아이템 중 하나
                    if (Relics.Has(RelicType.TreasureHunter)) Relics.Mark(RelicType.TreasureHunter);
                    var extra = Relics.Has(RelicType.TreasureHunter) ? Inventory.GiveRandomMissing() : null; // 유물 "오래된 보물지도"(TreasureHunter)
                    return item.HasValue
                        ? Loc.F("보물상자! 골드 +{0}, {1}{2} 획득", gold, ItemInfo.Name(item.Value), (extra.HasValue ? $", {ItemInfo.Name(extra.Value)}" : ""))
                        : Loc.F("보물상자! 골드 +{0} (아이템은 이미 다 있다)", gold);
                }
                case RoomEventType.SecretChest:
                {
                    var gold = roomClearGold * SecretChestGoldMultiplier * (Relics.Has(RelicType.TreasureHunter) ? Relics.TreasureHunterChestGold : 1);
                    GoldWallet.Add(gold);
                    var items = new[] { Inventory.GiveRandomMissing(), Inventory.GiveRandomMissing(),
                            Relics.Has(RelicType.TreasureHunter) ? Inventory.GiveRandomMissing() : null }
                        .Where(i => i.HasValue).Select(i => ItemInfo.Name(i.Value)).ToList();
                    HitFeedback.OnBonusReward(player);
                    return items.Count > 0
                        ? Loc.F("숨겨진 보물상자! 골드 +{0}, {1} 획득", gold, string.Join(", ", items))
                        : Loc.F("숨겨진 보물상자! 골드 +{0} (아이템은 이미 다 있다)", gold);
                }
                case RoomEventType.HealingSpring:
                    player.Stats.FullHeal();
                    HitFeedback.OnHeal(player);
                    return Loc.T("회복 샘! 체력이 전부 회복됐다");
                case RoomEventType.BlessingAltar:
                    player.Levels.GrantBonusUpgrade();
                    return Loc.T("축복 제단! 레벨업 카드를 한 장 더 고른다");
                case RoomEventType.CursedChest:
                    if (Rng.NextDouble() < 0.5)
                    {
                        var gold = roomClearGold * CursedJackpotMultiplier;
                        GoldWallet.Add(gold);
                        return Loc.F("저주받은 상자... 대박! 골드 +{0}", gold);
                    }
                    player.Stats.LoseCurrentHealthPercent(CursedHealthLossRate);
                    return Loc.F("저주받은 상자... 저주! 현재 체력 -{0:0}%", CursedHealthLossRate * 100f);
                case RoomEventType.Mimic:
                    room.SpawnMimic(GridPos);
                    return Loc.T("보물상자가 아니었다! 미믹이 덤벼든다 - 잡으면 큰 보상");
                case RoomEventType.BloodAltar:
                    return TriggerBloodAltar(player, room, out consumed);
                case RoomEventType.ChallengeFlag:
                    room.StartChallenge(GridPos);
                    return Loc.T("도전의 깃발! 정예 몹이 나타났다 - 전부 잡으면 골드 + 레벨업 카드");
                case RoomEventType.Hourglass:
                    RunBuffs.AddRevive();
                    return Loc.F("시간의 모래시계! 이번 시도에서 한 번, 쓰러져도 체력 {0:0}%로 되살아난다", RunBuffs.ReviveHealth * 100f);
                case RoomEventType.FateDice:
                    return RollFateDice(player, room, roomClearGold);
                case RoomEventType.ElixirSpring:
                    return DrinkElixir(player, roomClearGold);
                case RoomEventType.WishingWell:
                    return TriggerWishingWell(player, room, roomClearGold, out consumed);
                case RoomEventType.Anvil:
                    RunBuffs.AddDefense(RunBuffs.AnvilDamageTaken, "모루");
                    return Loc.F("모루! 갑옷을 두드려 폈다 - 이번 시도 동안 받는 피해 -{0:0}%", (1f - RunBuffs.AnvilDamageTaken) * 100f);
                case RoomEventType.AdventurerRemains:
                    return SearchRemains(room, roomClearGold);
                case RoomEventType.MuralChest:
                {
                    var gold = roomClearGold * MuralChestGoldMultiplier;
                    GoldWallet.Add(gold);
                    var first = DrinkElixir(player, roomClearGold);
                    var second = DrinkElixir(player, roomClearGold);
                    HitFeedback.OnBonusReward(player);
                    return Loc.F("벽화의 상자! 골드 +{0}\n{1}\n{2}", gold, first, second);
                }
                default:
                    RunBuffs.AddDamage(RunBuffs.WhetstoneDamage, "숫돌"); // 이름은 한국어 열쇠(효과 줄에서 번역)
                    return Loc.F("숫돌! 이번 시도 동안 주는 피해 +{0:0}%", (RunBuffs.WhetstoneDamage - 1f) * 100f);
            }
        }

        /// <summary>피의 제단 - 사람은 고르는 창(바친다/지나간다), 봇은 체력이 넉넉하면 바친다. 바치면 칸이 사라진다.</summary>
        private string TriggerBloodAltar(PlayerActor player, RoomController room, out bool consumed)
        {
            var cost = player.Stats.MaxHealth * RunBuffs.BloodAltarHealthCost;
            var gain = Loc.F("이번 시도 동안 주는 피해 +{0:0}%", (RunBuffs.BloodAltarDamage - 1f) * 100f);
            if (GameHUD.AutoPlayActive || NpcDialogUI.Instance == null)
            {
                consumed = true;
                if (player.Stats.CurrentHealth - cost < player.Stats.MaxHealth * 0.35f)
                    return Loc.T("피의 제단... 지금 체력으로는 바칠 수 없다(그냥 지나간다)");
                return OfferBlood(player, cost, gain);
            }

            consumed = false; // 고르기 전까지 칸은 그대로
            var canPay = player.Stats.CurrentHealth > cost;
            NpcDialogUI.Instance.Show(Loc.T("피의 제단"),
                Loc.F("검붉은 제단이 피를 원한다.\n<color=#FF8080>최대 체력의 {0:0}% ({1:0})</color>를 바치면 ", RunBuffs.BloodAltarHealthCost * 100f, cost) +
                $"<color=#FFD966>{gain}</color>.",
                new[]
                {
                    new NpcDialogUI.Choice(canPay ? Loc.F("피를 바친다 (체력 -{0:0})", cost) : Loc.T("피를 바친다 (체력이 모자라다)"), () =>
                    {
                        NpcDialogUI.Instance.Close();
                        room.ShowMessage(OfferBlood(player, cost, gain));
                        room.ConsumeEvent(this);
                    }, enabled: canPay),
                    new NpcDialogUI.Choice(Loc.T("그냥 지나간다"), () => NpcDialogUI.Instance.Close()),
                });
            return null;
        }

        /// <summary>소원의 우물 - 사람은 던질지 고르는 창, 봇은 골드가 있으면 던진다. 던지면 칸이 사라진다.</summary>
        private string TriggerWishingWell(PlayerActor player, RoomController room, int roomClearGold, out bool consumed)
        {
            var cost = Mathf.Max(1, roomClearGold * WishCostRooms);
            if (GameHUD.AutoPlayActive || NpcDialogUI.Instance == null)
            {
                consumed = true;
                return GoldWallet.TrySpend(cost) ? MakeWish(player, roomClearGold, cost) : Loc.T("소원의 우물... 던질 동전이 없다(그냥 지나간다)");
            }

            consumed = false; // 고르기 전까지 칸은 그대로
            var canPay = GoldWallet.Gold >= cost;
            NpcDialogUI.Instance.Show(Loc.T("소원의 우물"),
                Loc.F("맑은 물 밑에 동전이 반짝인다.\n<color=#FFD966>골드 {0}</color>를 던지면 무언가 돌아올지도 모른다.\n(영약 · 아이템 2개 · 레벨업 카드 · 꽝)", cost),
                new[]
                {
                    new NpcDialogUI.Choice(canPay ? Loc.F("동전을 던진다 (골드 -{0})", cost) : Loc.T("동전을 던진다 (골드가 모자라다)"), () =>
                    {
                        NpcDialogUI.Instance.Close();
                        if (!GoldWallet.TrySpend(cost))
                            return;
                        room.ShowMessage(MakeWish(player, roomClearGold, cost));
                        room.ConsumeEvent(this);
                    }, enabled: canPay),
                    new NpcDialogUI.Choice(Loc.T("그냥 지나간다"), () => NpcDialogUI.Instance.Close()),
                });
            return null;
        }

        private static string MakeWish(PlayerActor player, int roomClearGold, int cost)
        {
            var roll = Rng.NextDouble();
            if (roll < 0.25)
                return Loc.T("소원의 우물! ") + DrinkElixir(player, roomClearGold);
            if (roll < 0.5)
            {
                var items = new[] { Inventory.GiveRandomMissing(), Inventory.GiveRandomMissing() }.Where(i => i.HasValue).Select(i => ItemInfo.Name(i.Value)).ToList();
                if (items.Count > 0)
                    return Loc.F("소원의 우물! {0} 획득", string.Join(", ", items));
                GoldWallet.Add(cost * 2);
                return Loc.F("소원의 우물! 동전이 두 배로 돌아왔다 골드 +{0}", cost * 2);
            }
            if (roll < 0.7)
            {
                player.Levels.GrantBonusUpgrade();
                return Loc.T("소원의 우물! 레벨업 카드를 한 장 더 고른다");
            }
            return Loc.T("소원의 우물... 동전이 조용히 가라앉았다(꽝)");
        }

        /// <summary>모험가의 유해 - 대개 배낭에서 아이템 2개 + 골드, RemainsAmbushChance 확률로 유해가 일어나며 몹이 덤빈다(보상 없음).</summary>
        private static string SearchRemains(RoomController room, int roomClearGold)
        {
            if (Rng.NextDouble() < RemainsAmbushChance)
            {
                var spawned = room.SpawnEventEnemies(RemainsAmbushEnemies, 1f, 1f, elite: false);
                if (spawned > 0)
                    return Loc.F("모험가의 유해... 뼈가 덜그럭거리며 일어났다! 몹 {0}마리가 덤빈다", spawned);
            }
            var gold = roomClearGold * RemainsGoldMultiplier;
            GoldWallet.Add(gold);
            var items = new[] { Inventory.GiveRandomMissing(), Inventory.GiveRandomMissing() }.Where(i => i.HasValue).Select(i => ItemInfo.Name(i.Value)).ToList();
            return items.Count > 0
                ? Loc.F("모험가의 유해 - 낡은 배낭에서 {0}, 골드 +{1}", string.Join(", ", items), gold)
                : Loc.F("모험가의 유해 - 낡은 배낭에서 골드 +{0}", gold);
        }

        private static string OfferBlood(PlayerActor player, float cost, string gain)
        {
            player.Stats.LoseHealth(cost);
            RunBuffs.AddDamage(RunBuffs.BloodAltarDamage, "피의 제단");
            ActorJuice.Get(player).Flash(new Color(1f, 0.3f, 0.3f));
            return Loc.F("피의 제단! 체력 -{0:0}, {1}", cost, gain);
        }

        private static string RollFateDice(PlayerActor player, RoomController room, int roomClearGold)
        {
            var face = Rng.Next(1, 7);
            string result;
            switch (face)
            {
                case 1:
                    player.Stats.LoseCurrentHealthPercent(DiceHealthLossRate);
                    result = Loc.F("불운... 현재 체력 -{0:0}%", DiceHealthLossRate * 100f);
                    break;
                case 2:
                    var spawned = room.SpawnEventEnemies(DiceExtraEnemies, 1f, 1f, elite: false);
                    result = spawned > 0 ? Loc.F("몹 {0}마리가 굴러 나왔다!", spawned) : Loc.T("아무 일도 없었다");
                    break;
                case 3:
                    var gold = roomClearGold * DiceGoldMultiplier;
                    GoldWallet.Add(gold);
                    result = Loc.F("골드 +{0}", gold);
                    break;
                case 4:
                    player.Stats.Heal(player.Stats.MaxHealth * DiceHealRate);
                    HitFeedback.OnHeal(player);
                    result = Loc.F("체력 {0:0}% 회복", DiceHealRate * 100f);
                    break;
                case 5:
                    var item = Inventory.GiveRandomMissing();
                    result = item.HasValue ? Loc.F("{0} 획득", ItemInfo.Name(item.Value)) : Loc.T("아이템이 이미 가득하다");
                    break;
                default:
                    player.Levels.GrantBonusUpgrade();
                    result = Loc.T("대박! 레벨업 카드 1장");
                    break;
            }
            return Loc.F("운명의 주사위 [{0}] - {1}", face, result);
        }

        /// <summary>영약의 샘 - 해금된 영약 중 하나를 1개(로비에서 사는 것과 같은 영구 효과, 지금 스탯에도 바로 반영).
        /// 해금된 영약이 없으면 골드.</summary>
        private static string DrinkElixir(PlayerActor player, int roomClearGold)
        {
            var types = new[] { PotionType.Attack, PotionType.Health, PotionType.Critical }.Where(StatPotionWallet.IsUnlocked).ToList();
            if (types.Count == 0)
            {
                var gold = roomClearGold * DiceGoldMultiplier;
                GoldWallet.Add(gold);
                return Loc.F("영약의 샘... 아직 모르는 맛이다. 바닥의 동전 골드 +{0}", gold);
            }
            var type = types[Rng.Next(types.Count)];
            RunProgress.ApplyPermanentChange(player.Stats, () => StatPotionWallet.AddPotion(type));
            HitFeedback.OnHeal(player);
            return Loc.F("영약의 샘! {0} 1개 (영구)", StatPotionWallet.Name(type));
        }
    }
}
