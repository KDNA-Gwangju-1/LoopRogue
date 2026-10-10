using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>저주 계약 하나 - 대가(Cost)를 치르고 이득(Gain)을 얻는다. 배율 효과는 이번 시도 동안(죽거나 로비로 나가거나 층을 넘기면 끝),
    /// Instant는 계약하는 순간 한 번.</summary>
    public sealed class CurseDeal
    {
        public string Id;
        public string Name;
        public string Cost;
        public string Gain;
        public float DamageDealt = 1f; // 주는 피해 배율
        public float DamageTaken = 1f; // 받는 피해 배율
        public float GoldGain = 1f;    // 골드 획득 배율
        public float RoomHeal = 1f;    // 방 클리어 회복 배율
        public float HealthCost;       // 계약 순간 잃는 지금 체력 비율
        public Func<PlayerActor, int, string> Instant; // (플레이어, 방 클리어 골드) → 결과 문구(없으면 null)
    }

    /// <summary>그림자 거래상(NpcType.Curse)의 저주 계약 - 시도마다 초기화(LoopManager). 받은 계약들의 배율은 곱해서 적용한다:
    /// 주는 피해(PlayerActor.StrikeEnemy), 받는 피해(CharacterStats.TakeIncomingDamage), 골드 획득·방 클리어 회복(RoomController).</summary>
    public static class Curses
    {
        private static readonly LocCache<CurseDeal[]> AllCache = new LocCache<CurseDeal[]>(() => new CurseDeal[]
        {
            new CurseDeal { Id = "Rage", Name = Loc.T("분노의 계약"), Cost = Loc.T("받는 피해 +30%"), Gain = Loc.T("주는 피해 +40%"),
                DamageTaken = 1.3f, DamageDealt = 1.4f },
            new CurseDeal { Id = "Blood", Name = Loc.T("피의 계약"), Cost = Loc.T("지금 체력 40% 잃음"), Gain = Loc.T("레벨업 카드 1장"),
                HealthCost = 0.4f, Instant = (p, _) => { p.Levels.GrantBonusUpgrade(); return null; } },
            new CurseDeal { Id = "Gold", Name = Loc.T("황금의 저주"), Cost = Loc.T("골드 획득 -50%"), Gain = Loc.T("지금 골드 대량 획득"),
                GoldGain = 0.5f, Instant = (_, roomGold) => { var g = roomGold * GoldDealRooms; GoldWallet.Add(g); return Loc.F("골드 +{0}", g); } },
            new CurseDeal { Id = "Dry", Name = Loc.T("메마른 저주"), Cost = Loc.T("방 클리어 회복 없음"), Gain = Loc.T("주는 피해 +25%"),
                RoomHeal = 0f, DamageDealt = 1.25f },
            new CurseDeal { Id = "Glutton", Name = Loc.T("탐식의 계약"), Cost = Loc.T("지금 체력 25% 잃음"), Gain = Loc.T("없는 아이템 2개"),
                HealthCost = 0.25f, Instant = (_, __) =>
                {
                    var got = new[] { Inventory.GiveRandomMissing(), Inventory.GiveRandomMissing() }.Where(i => i.HasValue).Select(i => ItemInfo.Name(i.Value)).ToList();
                    return got.Count > 0 ? string.Join(", ", got) + Loc.T(" 획득") : Loc.T("아이템이 이미 가득하다");
                } },
            new CurseDeal { Id = "Reckless", Name = Loc.T("무모한 계약"), Cost = Loc.T("받는 피해 +20%"), Gain = Loc.T("골드 획득 +60%"),
                DamageTaken = 1.2f, GoldGain = 1.6f },
        });
        public static CurseDeal[] All => AllCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        /// <summary>황금의 저주로 바로 받는 골드 = 방 클리어 골드 × 이 값.</summary>
        private const int GoldDealRooms = 12;
        public const int OfferCount = 3;

        private static readonly List<CurseDeal> Active = new List<CurseDeal>();
        private static readonly System.Random Rng = new System.Random();

        public static IReadOnlyList<CurseDeal> ActiveDeals => Active;

        /// <summary>새 시도를 시작할 때(LoopManager) - 받아둔 저주가 전부 풀린다.</summary>
        public static void Reset() => Active.Clear();

        public static List<CurseDeal> RollOffer() => All.OrderBy(_ => Rng.Next()).Take(OfferCount).ToList();

        /// <summary>계약 - 체력 대가를 치르고 즉시 효과를 준 뒤 배율 효과를 이번 시도에 붙인다. 결과 문구를 돌려준다.</summary>
        public static string Take(CurseDeal deal, PlayerActor player, int roomClearGold)
        {
            if (deal.HealthCost > 0f)
                player.Stats.LoseCurrentHealthPercent(deal.HealthCost);
            var result = deal.Instant?.Invoke(player, roomClearGold);
            Active.Add(deal);
            return result != null ? Loc.F("{0} 성립! {1}", deal.Name, result) : Loc.F("{0} 성립!", deal.Name);
        }

        private static float Product(Func<CurseDeal, float> pick)
        {
            var m = 1f;
            foreach (var d in Active)
                m *= pick(d);
            return m;
        }

        public static float DamageDealtMultiplier => Product(d => d.DamageDealt);
        public static float DamageTakenMultiplier => Product(d => d.DamageTaken);
        public static float GoldMultiplier => Product(d => d.GoldGain);
        public static float RoomHealMultiplier => Product(d => d.RoomHeal);

        /// <summary>GameHUD 왼쪽 위 의뢰 줄 아래 - 받은 저주 요약. 없으면 null.</summary>
        public static string TrackerText =>
            Active.Count == 0 ? null : Loc.T("<color=#C890FF>저주:</color> ") + string.Join(" / ", Active.Select(d => d.Name));
    }
}
