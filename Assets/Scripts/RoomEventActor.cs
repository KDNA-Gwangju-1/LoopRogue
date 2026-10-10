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
    }

    /// <summary>방 이벤트 칸 - 한 시도에 스테이지당 보물상자 1 + (확률로) 다른 이벤트 1, 방 3~9 중. 칸 하나를 차지하는 액터라
    /// 몹은 못 지나가고, 플레이어가 그 칸으로 이동하면 발동하고 사라진다(피의 제단은 고르기 전까지 남는다).
    /// 그림은 Resources/Effects/event_{종류}(반복 재생) - 없거나 봇 중이면 색 마름모.</summary>
    public class RoomEventActor : GridActor
    {
        /// <summary>보물상자 골드 = 방 클리어 골드의 이 배수, 저주받은 상자 대박은 그 두 배.</summary>
        public const int TreasureGoldMultiplier = 5;
        public const int CursedJackpotMultiplier = 10;
        public const float CursedHealthLossRate = 0.3f;
        public const int DiceGoldMultiplier = 4;
        public const float DiceHealRate = 0.5f;
        public const float DiceHealthLossRate = 0.25f;
        public const int DiceExtraEnemies = 2;

        private static readonly System.Random Rng = new System.Random();

        /// <summary>보물상자 말고 나오는 이벤트와 가중치 - 기존 셋은 자주, 새 이벤트 중 센 것(모래시계·영약의 샘)은 드물게.</summary>
        private static readonly (RoomEventType Type, int Weight)[] OtherEvents =
        {
            (RoomEventType.HealingSpring, 3), (RoomEventType.BlessingAltar, 3), (RoomEventType.CursedChest, 3),
            (RoomEventType.Mimic, 2), (RoomEventType.BloodAltar, 2), (RoomEventType.ChallengeFlag, 2),
            (RoomEventType.Hourglass, 1), (RoomEventType.FateDice, 3), (RoomEventType.ElixirSpring, 1), (RoomEventType.Whetstone, 2),
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
            _ => Loc.T("숫돌"),
        };

        public void Initialize(RoomEventType type)
        {
            Type = type;
            Stats = new CharacterStats(1f, 0f);

            // 도트 그림(제자리에서 반짝이는 반복) - 미믹은 보물상자 그림 그대로.
            var look = type == RoomEventType.Mimic ? RoomEventType.TreasureChest : type;
            var fx = Fx.Play($"event_{look}", transform.position, 0.95f, fps: 6f, loop: true, sortingOrder: 0, parent: transform);
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
                _ => new Color(0.6f, 0.65f, 0.7f),
            };
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
                    var gold = roomClearGold * TreasureGoldMultiplier;
                    GoldWallet.Add(gold);
                    var item = Inventory.GiveRandomMissing(); // 없는 아이템 중 하나
                    var extra = Relics.Has(RelicType.TreasureHunter) ? Inventory.GiveRandomMissing() : null; // 유물 "보물 사냥꾼"
                    return item.HasValue
                        ? Loc.F("보물상자! 골드 +{0}, {1}{2} 획득", gold, ItemInfo.Name(item.Value), (extra.HasValue ? $", {ItemInfo.Name(extra.Value)}" : ""))
                        : Loc.F("보물상자! 골드 +{0} (아이템은 이미 다 있다)", gold);
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
            var attackBefore = RunProgress.CurrentPermanentAttack();
            var healthBefore = RunProgress.CurrentPermanentHealth();
            var criticalBefore = RunProgress.CurrentPermanentCritical();
            StatPotionWallet.AddPotion(type);
            var stats = player.Stats;
            stats.FixedAttack += RunProgress.CurrentPermanentAttack() - attackBefore;
            var healthGain = RunProgress.CurrentPermanentHealth() - healthBefore;
            stats.FixedMaxHealth += healthGain;
            stats.Heal(healthGain);
            stats.CriticalChanceRate += RunProgress.CurrentPermanentCritical() - criticalBefore;
            HitFeedback.OnHeal(player);
            return Loc.F("영약의 샘! {0} 1개 (영구)", StatPotionWallet.Name(type));
        }
    }
}
