using UnityEngine;

namespace LoopRogue
{
    public enum RoomEventType
    {
        TreasureChest, // 보물상자 - 골드
        HealingSpring, // 회복 샘 - 체력 전부 회복
        BlessingAltar, // 축복 제단 - 레벨업 카드 1장 추가 선택
        CursedChest,   // 저주받은 상자 - 50%: 골드 대박 / 50%: 현재 체력 30% 감소
    }

    /// <summary>방 이벤트 칸 - 한 시도에 스테이지당 한 방(방 3~9 중 랜덤)에만 생긴다. 칸 하나를 차지하는 액터라
    /// 몹은 못 지나가고, 플레이어가 그 칸으로 이동하면 발동하고 사라진다. 그 방 몹을 다 잡으면 방이 바로
    /// 넘어가므로, 먹으려면 몹을 다 잡기 전에 밟아야 한다.</summary>
    public class RoomEventActor : GridActor
    {
        /// <summary>보물상자 골드 = 방 클리어 골드의 이 배수, 저주받은 상자 대박은 그 두 배.</summary>
        public const int TreasureGoldMultiplier = 5;
        public const int CursedJackpotMultiplier = 10;
        public const float CursedHealthLossRate = 0.3f;

        private static readonly System.Random Rng = new System.Random();

        public RoomEventType Type { get; private set; }

        public string DisplayName => Type switch
        {
            RoomEventType.TreasureChest => "보물상자",
            RoomEventType.HealingSpring => "회복 샘",
            RoomEventType.BlessingAltar => "축복 제단",
            _ => "저주받은 상자",
        };

        public void Initialize(RoomEventType type)
        {
            Type = type;
            Stats = new CharacterStats(1f, 0f);

            var color = type switch
            {
                RoomEventType.TreasureChest => new Color(1f, 0.8f, 0.2f),
                RoomEventType.HealingSpring => new Color(0.3f, 0.7f, 1f),
                RoomEventType.BlessingAltar => new Color(0.85f, 0.5f, 1f),
                _ => new Color(0.45f, 0.15f, 0.55f),
            };
            var renderer = VisualUtil.CreateSquareVisual(gameObject, color, GridConstants.CellSize * 0.45f, sortingOrder: 0);
            transform.rotation = Quaternion.Euler(0f, 0f, 45f); // 마름모 - 몹(사각형)과 구분
            renderer.color = color;
        }

        /// <summary>보물상자를 뺀 나머지 셋 중 하나(보물상자는 층마다 따로 1개 확정).</summary>
        public static RoomEventType RollNonChestType() => (RoomEventType)(1 + Rng.Next(3));

        /// <summary>플레이어가 밟았을 때. 결과 안내 문구를 돌려준다.</summary>
        public string Trigger(PlayerActor player, int roomClearGold)
        {
            switch (Type)
            {
                case RoomEventType.TreasureChest:
                {
                    var gold = roomClearGold * TreasureGoldMultiplier;
                    GoldWallet.Add(gold);
                    var item = Inventory.GiveRandomMissing(); // 없는 아이템 중 하나
                    var extra = Relics.Has(RelicType.TreasureHunter) ? Inventory.GiveRandomMissing() : null; // 유물 "보물 사냥꾼"
                    return item.HasValue
                        ? $"보물상자! 골드 +{gold}, {ItemInfo.Name(item.Value)}{(extra.HasValue ? $", {ItemInfo.Name(extra.Value)}" : "")} 획득"
                        : $"보물상자! 골드 +{gold} (아이템은 이미 다 있다)";
                }
                case RoomEventType.HealingSpring:
                    player.Stats.FullHeal();
                    return "회복 샘! 체력이 전부 회복됐다";
                case RoomEventType.BlessingAltar:
                    player.Levels.GrantBonusUpgrade();
                    return "축복 제단! 레벨업 카드를 한 장 더 고른다";
                default:
                    if (Rng.NextDouble() < 0.5)
                    {
                        var gold = roomClearGold * CursedJackpotMultiplier;
                        GoldWallet.Add(gold);
                        return $"저주받은 상자... 대박! 골드 +{gold}";
                    }
                    player.Stats.LoseCurrentHealthPercent(CursedHealthLossRate);
                    return $"저주받은 상자... 저주! 현재 체력 -{CursedHealthLossRate * 100f:0}%";
            }
        }
    }
}
