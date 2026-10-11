using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    public enum NpcType
    {
        Merchant, // 떠돌이 상인 - 판매 후보 중 3개를 골드로
        Wanderer, // 길 잃은 모험가 - 방 1에서 의뢰, 방 10에서 보고
        Curse,    // 그림자 거래상 - 대가를 치르는 저주 계약(Curses)
    }

    /// <summary>방 안의 NPC - 일반 방을 클리어하면 가끔 그 방에 나타난다(RoomController.TrySpawnRoomClearNpc). 칸 하나를 차지하고
    /// (아무도 못 지나감) 플레이어가 주변 8칸에서 F를 누르면 대화창을 연다. 대화는 턴을 쓰지 않는다.
    /// 도트 그림은 Resources/Sprites/{Merchant|Wanderer}(4방향 대기 4프레임, 가까이 온 플레이어 쪽을 본다) - 없으면 단색 사각형.</summary>
    public class NpcActor : GridActor
    {
        private const int ItemPriceBase = 35;      // 1회용 아이템 - 스테이지 보상 배율 곱하기 전
        private const int SlotTicketBundle = 10;   // 슬롯 이용권 묶음 장수
        private const float SlotTicketDiscount = 0.5f; // 일반 베팅 10회 값의 절반
        private const int StockCount = 3;
        private const float MarkerBobSpeed = 4f;

        private static readonly System.Random Rng = new System.Random();

        /// <summary>상인 품목 종류 - 봇 구매 우선순위(값이 작을수록 먼저)와 통계에 쓴다.</summary>
        public enum StockKind
        {
            Potion,
            GachaTicket,
            SlotTicket,
            Item,
        }

        private sealed class StockEntry
        {
            public StockKind Kind;
            public string Name;
            public Func<string> Description; // 보유 장수처럼 사고 나면 바뀌는 글이 있어서 그때그때
            public int Price;
            public bool Sold;
            public Func<string> BlockedReason; // 지금 못 사는 이유(없으면 null)
            public Action Apply;
        }

        public NpcType Type { get; private set; }

        public string DisplayName => Type switch
        {
            NpcType.Merchant => Loc.T("떠돌이 상인"),
            NpcType.Wanderer => Loc.T("길 잃은 모험가"),
            _ => Loc.T("그림자 거래상"),
        };

        private RoomController _room;
        private PlayerActor _player;
        private readonly List<StockEntry> _stock = new List<StockEntry>();
        private bool _talkedOnce;
        private Transform _marker;
        private float _markerBaseY;

        /// <summary>도트 그림 크기 - 48x48 캔버스에 몸이 작게 그려져 있어 방패병·폭발병처럼 키운다.</summary>
        private const float SpriteScale = 1.3f;
        private const float FacePlayerRange = 3; // 이 칸 수 안에 플레이어가 오면 그쪽을 본다

        private SpriteAnimator _animator;

        /// <summary>모험가가 방 10에 보고 받으러 나온 경우 - 말을 걸면 의뢰 보상을 준다.</summary>
        private bool _isReport;

        public void Initialize(NpcType type, int stage, RoomController room, PlayerActor player, bool isReport = false)
        {
            Type = type;
            _room = room;
            _player = player;
            _isReport = isReport;
            Stats = new CharacterStats(1f, 0f);

            // 도트 그림(Resources/Sprites/Merchant|Wanderer, 4방향 대기) - 그림 파일이 없으면 단색 사각형.
            var setName = type.ToString();
            var sprite = SpriteAnimator.FirstFrame(setName);
            float size, markerHeight;
            if (sprite != null)
            {
                size = SpriteScale;
                markerHeight = 0.36f; // 그림 머리(모자·지팡이) 바로 위
                var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, sprite, GridConstants.CellSize * size, sortingOrder: 1);
                _animator = gameObject.AddComponent<SpriteAnimator>();
                _animator.Setup(setName, spriteRenderer, BodyColor(type));
            }
            else
            {
                size = 0.6f;
                markerHeight = 0.62f;
                var color = BodyColor(type);
                VisualUtil.CreateSquareVisual(gameObject, color, GridConstants.CellSize * size, sortingOrder: 1);
            }

            // 머리 위 노란 마름모 - 상인은 아직 말을 안 걸었으면, 모험가는 줄 의뢰나 받을 보고가 남아 있으면 보인다.
            var marker = new GameObject("TalkMarker");
            marker.transform.SetParent(transform, false);
            var parentScale = GridConstants.CellSize * size;
            _markerBaseY = GridConstants.CellSize * markerHeight / parentScale;
            marker.transform.localPosition = new Vector3(0f, _markerBaseY, 0f);
            _marker = marker.transform;
            // 노란 느낌표 도트 그림 - 없거나 봇 중이면 예전 노란 마름모.
            var alert = Fx.Play("npc_alert", marker.transform.position + Vector3.up * GridConstants.CellSize * 0.15f, 0.35f,
                fps: 6f, loop: true, sortingOrder: 9, parent: marker.transform);
            if (alert == null)
            {
                VisualUtil.CreateSquareVisual(marker, new Color(1f, 0.85f, 0.25f), GridConstants.CellSize * 0.2f / parentScale, sortingOrder: 2);
                marker.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            if (type == NpcType.Merchant)
                RollStock(stage);
            if (type == NpcType.Curse)
                _deals = Curses.RollOffer();
        }

        private static Color BodyColor(NpcType type) => type switch
        {
            NpcType.Merchant => new Color(0.95f, 0.7f, 0.3f),
            NpcType.Wanderer => new Color(0.45f, 0.85f, 0.55f),
            _ => new Color(0.45f, 0.2f, 0.6f),
        };

        // ---- 등장 연출 ----
        private const float AppearSeconds = 0.4f;
        private Vector3 _baseScale;
        private float _appearStart = -1f;

        /// <summary>칸에 놓인 직후(RoomController) - 연기가 퍼지고 몸이 0에서 살짝 튀어 오르며 커진다 + 등장음. 봇 중엔 생략.</summary>
        public void PlayAppearEffect()
        {
            if (DamagePopup.Suppressed)
                return;
            var smoke = Type == NpcType.Curse ? new Color(0.35f, 0.15f, 0.45f, 0.9f) : new Color(0.75f, 0.75f, 0.8f, 0.85f);
            DeathBurst.Spawn(transform.position, smoke, 14, GridConstants.CellSize * 2.2f, GridConstants.CellSize * 0.18f);
            DeathBurst.Spawn(transform.position + new Vector3(0f, GridConstants.CellSize * 0.3f, 0f), smoke, 8, GridConstants.CellSize * 1.2f, GridConstants.CellSize * 0.14f);
            SfxPlayer.Play(Type == NpcType.Curse ? Sfx.Curse : Sfx.Appear);
            _baseScale = transform.localScale;
            transform.localScale = Vector3.zero;
            _appearStart = Time.time;
        }

        private void AnimateAppear()
        {
            if (_appearStart < 0f)
                return;
            var t = (Time.time - _appearStart) / AppearSeconds;
            if (t >= 1f)
            {
                transform.localScale = _baseScale;
                _appearStart = -1f;
                return;
            }
            // 살짝 넘쳤다가 돌아오는 튕김(ease-out-back)
            const float c = 1.7f;
            var u = t - 1f;
            var s = 1f + (c + 1f) * u * u * u + c * u * u;
            transform.localScale = new Vector3(_baseScale.x * s, _baseScale.y * Mathf.Lerp(1.3f, 1f, t) * s, 1f);
        }

        private void Update()
        {
            AnimateAppear();
            var show = Type != NpcType.Wanderer ? !_talkedOnce : _isReport ? RunQuest.Active : !RunQuest.Active;
            if (_marker.gameObject.activeSelf != show)
                _marker.gameObject.SetActive(show);
            if (show)
                _marker.localPosition = new Vector3(0f, _markerBaseY + Mathf.Sin(Time.time * MarkerBobSpeed) * 0.05f, 0f);

            // 플레이어가 가까이 오면 그쪽을 바라본다(멀면 마지막 방향 그대로).
            if (_animator != null && _player != null)
            {
                var d = _player.GridPos - GridPos;
                if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= FacePlayerRange)
                    _animator.Face(d);
            }
        }

        /// <summary>플레이어가 주변 칸에서 F를 눌렀을 때(PlayerActor).</summary>
        public void Talk(PlayerActor player)
        {
            _player = player;
            if (NpcDialogUI.Instance == null)
                return;
            if (Type == NpcType.Merchant)
                ShowShop(_talkedOnce ? Loc.T("또 왔나? 천천히 골라 보게.") : MerchantGreeting());
            else if (Type == NpcType.Wanderer)
                ShowWanderer();
            else
                ShowCurse();
            _talkedOnce = true;
        }

        // ===================== 그림자 거래상 =====================

        private List<CurseDeal> _deals;

        private static readonly LocCache<string[]> CurseGreetingsCache = new LocCache<string[]>(() => new string[]

        {
            Loc.T("...살아 있는 자의 냄새로군. 힘이 필요하지 않나? 값은... 조금만 받지."),
            Loc.T("...살아 있는 자의 냄새로군. 힘이 필요하지 않나? 값은... 조금만 받지."),
            Loc.T("또 왔군. 고리를 도는 자들은 결국 나를 찾게 되어 있지."),
            Loc.T("네 죽음의 냄새가 짙어졌어. {0}번... 거래하기 딱 좋은 숫자야."),
            Loc.T("{0}번이나 죽고도 아직 계약을 망설이나? 흐흐."),
            Loc.T("고리의 단골이여. 오늘은 무엇을 내어 주겠나?"),
        });

        private static string[] CurseGreetings => CurseGreetingsCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private void ShowCurse()
        {
            var choices = _deals.Select(d => new NpcDialogUI.Choice(
                Loc.F("<color=#C890FF>{0}</color>  <color=#FF8080>대가: {1}</color>  <color=#8CE08C>얻는 것: {2}</color>", d.Name, d.Cost, d.Gain),
                () => TakeDeal(d))).ToList();
            choices.Add(new NpcDialogUI.Choice(Loc.T("거절한다"), null));
            NpcDialogUI.Instance.Show(DisplayName,
                Loc.F("{0}\n<size=13><color=#9AA0AA>계약은 하나만. 배율 효과는 이번 시도 동안(죽거나 층을 넘기면 풀린다).</color></size>", LoopLine(CurseGreetings)),
                choices);
        }

        private void TakeDeal(CurseDeal deal)
        {
            var message = Curses.Take(deal, _player, _room.RoomClearGold);
            if (!DamagePopup.Suppressed)
                SfxPlayer.Play(Sfx.Curse);
            if (NpcDialogUI.Instance != null)
                NpcDialogUI.Instance.Close();
            _room.ShowMessage($"<color=#C890FF>{message}</color>");
            _room.DismissNpc(this); // 계약이 끝나면 연기 속으로 사라진다
        }

        // ---- 루프 회차별 대사(LoopRecord.Tier: 0번 / 1~4 / 5~19 / 20~49 / 50~99 / 100번 이상 죽음) ----

        private static readonly LocCache<string[]> MerchantGreetingsCache = new LocCache<string[]>(() => new string[]

        {
            Loc.T("어이, 이런 곳에서 산 사람을 다 보는군! 오늘 들고 온 건 이것뿐이네."),
            Loc.T("어이, 이런 곳에서 산 사람을 다 보는군! 오늘 들고 온 건 이것뿐이네."),
            Loc.T("또 자넨가! 이번 고리에서도 살아 있군. 물건 보고 가게."),
            Loc.T("단골이 다 됐군. 자네 쓰러지는 소리는 이제 벽 너머에서도 알아듣는다네."),
            Loc.T("{0}번이나 죽고도 지갑을 들고 다니다니, 대단한 손님이야."),
            Loc.T("자네 몫으로 고리 저편에서 물건을 챙겨 왔지. 농담 아니야. ...{0}번째 손님이니 말이야."),
        });

        private static string[] MerchantGreetings => MerchantGreetingsCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private static readonly LocCache<string[]> WandererOpeningsCache = new LocCache<string[]>(() => new string[]

        {
            Loc.T("...너도 이 고리에 갇혔구나."),
            Loc.T("...너도 이 고리에 갇혔구나. 아직 몇 번 안 돌았지? 눈빛이 맑아."),
            Loc.T("...어디서 본 얼굴인데. 너, 몇 번째야? 나는... 세는 걸 잊었어."),
            Loc.T("또 만났네. 넌 기억하는구나... 다행이다. 난 자꾸 잊어버리거든."),
            Loc.T("{0}번이라고? ...그럼 이번엔 정말 끝까지 갈 수 있을지도 몰라."),
            Loc.T("{0}번이나 돌아온 사람은 처음 봐. 아니, 처음이 아닌가... 모르겠어."),
        });

        private static string[] WandererOpenings => WandererOpeningsCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private static readonly LocCache<string[]> WandererWaitingCache = new LocCache<string[]>(() => new string[]

        {
            Loc.T("몇 번을 쓰러져도 괜찮아. 끝까지만 가 줘."),
            Loc.T("몇 번을 쓰러져도 괜찮아. 끝까지만 가 줘."),
            Loc.T("쓰러져도 돌아오잖아. 그러니까 괜찮아, 천천히 가."),
            Loc.T("쓰러질 때마다 네가 조금씩 강해지는 게 보여."),
            Loc.T("{0}번... 그만큼 넘어졌으면 이제 일어서는 법도 알겠지."),
            Loc.T("넌 이미 고리보다 질겨. 끝에서 기다릴게."),
        });

        private static string[] WandererWaiting => WandererWaitingCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private static string LoopLine(string[] lines) => string.Format(lines[LoopRecord.Tier], LoopRecord.TotalDeaths);

        private static string MerchantGreeting() => LoopLine(MerchantGreetings);

        // ===================== 떠돌이 상인 =====================

        /// <summary>판매 후보(슬롯 이용권 묶음 / 영약 3종 / 뽑기권 / 안 가진 1회용 아이템) 전체에서 StockCount개를 고른다.</summary>
        private void RollStock(int stage)
        {
            var pool = new List<StockEntry>
            {
                new StockEntry
                {
                    Kind = StockKind.SlotTicket,
                    Name = Loc.F("슬롯 이용권 {0}장", SlotTicketBundle),
                    Description = () => Loc.F("로비 도박장 일반 베팅 {0}회 무료 (보유 {1}장)", SlotTicketBundle, SlotMachine.Tickets),
                    Price = Mathf.RoundToInt(SlotMachine.GetBet(false) * SlotTicketBundle * SlotTicketDiscount),
                    Apply = () => SlotMachine.AddTickets(SlotTicketBundle),
                },
                new StockEntry
                {
                    Kind = StockKind.GachaTicket,
                    Name = Loc.T("뽑기권"),
                    Description = () => Loc.F("로비 뽑기 상점에서 검·갑옷·반지 아무거나 1회 무료 (보유 {0}장)", GachaSystem.Tickets),
                    Price = GachaSystem.TicketPrice,
                    Apply = () => GachaSystem.AddTickets(1),
                },
            };

            foreach (PotionType type in Enum.GetValues(typeof(PotionType)))
            {
                if (!StatPotionWallet.IsUnlocked(type))
                    continue; // 치명타 영약은 로비처럼 해금 층부터
                var potion = type;
                pool.Add(new StockEntry
                {
                    Kind = StockKind.Potion,
                    Name = Loc.F("{0} 영약", StatPotionWallet.Name(potion)),
                    Description = () => StatPotionWallet.Description(potion),
                    Price = StatPotionWallet.FirstCost,
                    Apply = () => ApplyPotion(potion),
                });
            }

            var itemPrice = Mathf.RoundToInt(ItemPriceBase * StageScaling.RewardMultiplier(stage));
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var item = (ItemType)i;
                if (Inventory.Has(item))
                    continue;
                pool.Add(new StockEntry
                {
                    Kind = StockKind.Item,
                    Name = ItemInfo.Name(item),
                    Description = () => ItemInfo.Description(item),
                    Price = itemPrice,
                    BlockedReason = () => Inventory.Has(item) ? Loc.T("이미 가지고 있음") : null,
                    Apply = () => Inventory.Give(item),
                });
            }

            _stock.AddRange(pool.OrderBy(_ => Rng.Next()).Take(StockCount));
        }

        /// <summary>영약은 영구 스탯이라 저장하고, 지금 런의 스탯에도 바로 반영한다(다음 저장/복원 때 이중으로 더해지지 않게 고정 몫을 새로 계산).</summary>
        private void ApplyPotion(PotionType type)
        {
            StatPotionWallet.AddPotion(type);
            var stats = _player.Stats;
            switch (type)
            {
                case PotionType.Attack:
                    stats.FixedAttack = RunProgress.CurrentPermanentAttack();
                    break;
                case PotionType.Health:
                    var before = stats.FixedMaxHealth;
                    stats.FixedMaxHealth = RunProgress.CurrentPermanentHealth();
                    stats.Heal(stats.FixedMaxHealth - before); // 늘어난 만큼 현재 체력도
                    break;
                default:
                    stats.CriticalChanceRate += StatPotionWallet.CriticalChancePerPotion;
                    break;
            }
        }

        private void ShowShop(string line)
        {
            var choices = new List<NpcDialogUI.Choice>();
            foreach (var entry in _stock)
            {
                var e = entry;
                var blocked = e.Sold ? Loc.T("품절") : e.BlockedReason?.Invoke();
                var affordable = GoldWallet.Gold >= e.Price;
                var suffix = blocked != null ? $"  <color=#888888>({blocked})</color>"
                    : affordable ? string.Empty : Loc.T("  <color=#FF8080>(골드 부족)</color>");
                choices.Add(new NpcDialogUI.Choice(
                    $"{e.Name}  <color=#FFD966>{e.Price}G</color>  <size=12><color=#AAB0BA>{e.Description()}</color></size>{suffix}",
                    () => Buy(e),
                    enabled: blocked == null && affordable));
            }
            choices.Add(new NpcDialogUI.Choice(Loc.T("떠난다"), null));

            NpcDialogUI.Instance.Show(DisplayName,
                Loc.F("{0}\n<color=#FFD966>보유 골드: {1}</color>  <size=13><color=#9AA0AA>(이용권·뽑기권·영약은 영구, 1회용 아이템은 죽으면 잃는다)</color></size>", line, GoldWallet.Gold),
                choices);
        }

        private void Buy(StockEntry entry)
        {
            if (!TryPurchase(entry))
                return;
            ShowShop(Loc.F("{0}, 좋은 선택이야. 또 필요한 건 없나?", entry.Name));
        }

        private bool TryPurchase(StockEntry entry)
        {
            if (entry.Sold || entry.BlockedReason?.Invoke() != null || !GoldWallet.TrySpend(entry.Price))
                return false;
            entry.Sold = true;
            entry.Apply();
            Achievements.AddMerchantBuy(); // "단골손님" 업적
            return true;
        }

        // ===================== 자동 플레이 봇 =====================

        /// <summary>봇의 저주 계약 선호 순서 - 골드·화력 위주, 체력을 깎는 계약은 뒤로.</summary>
        private static readonly string[] BotCursePriority = { "Reckless", "Gold", "Rage", "Dry", "Glutton", "Blood" };

        /// <summary>자동 플레이 봇용 - 대화창 없이 말을 건 것과 같은 일을 한다. 상인: 살 수 있는 걸 StockKind 순서(영약 > 뽑기권 > 슬롯 이용권 > 아이템)로
        /// 전부 산다. 모험가: 방 10이면 보고(보상), 아니면 의뢰를 아직 안 들고 있을 때 받는다. 산 품목을 돌려준다(모험가는 빈 목록).</summary>
        public List<(StockKind Kind, string Name, int Price)> BotInteract(PlayerActor player)
        {
            _player = player;
            _talkedOnce = true;
            var bought = new List<(StockKind, string, int)>();
            if (Type == NpcType.Curse)
            {
                // 계약 뒤 체력이 절반 이상 남는 것 중 BotCursePriority 순서로 하나(없으면 거절).
                var hpRate = player.Stats.CurrentHealth / Mathf.Max(1f, player.Stats.MaxHealth);
                var pick = _deals.OrderBy(d => Array.IndexOf(BotCursePriority, d.Id))
                    .FirstOrDefault(d => hpRate * (1f - d.HealthCost) >= 0.5f);
                if (pick != null)
                {
                    Curses.Take(pick, player, _room.RoomClearGold);
                    _room.DismissNpc(this);
                }
                return bought;
            }
            if (Type == NpcType.Wanderer)
            {
                if (_isReport)
                    RunQuest.Complete();
                else if (!RunQuest.Active)
                    RunQuest.Accept();
                return bought;
            }
            foreach (var entry in _stock.OrderBy(e => e.Kind))
                if (TryPurchase(entry))
                    bought.Add((entry.Kind, entry.Name, entry.Price));
            return bought;
        }

        // ===================== 길 잃은 모험가 =====================

        private void ShowWanderer()
        {
            var ui = NpcDialogUI.Instance;
            var leave = new NpcDialogUI.Choice(Loc.T("떠난다"), null);

            // 방 10 - 보고 받으러 나온 모험가
            if (_isReport)
            {
                if (!RunQuest.Active)
                {
                    ui.Show(DisplayName, Loc.T("고마워, 정말로. ...이번 고리에서는 너를 기억할 수 있을 것 같아."), new[] { leave });
                    return;
                }
                ui.Show(DisplayName,
                    Loc.F("정말 여기까지 왔구나! 보스 방이 바로 저 너머야.\n약속한 대로 <color=#FFD966>{0}</color>를 줄게.", RunQuest.RewardText),
                    new[] { new NpcDialogUI.Choice(Loc.T("보상을 받는다"), ClaimReward), leave });
                return;
            }

            if (RunQuest.Active)
            {
                ui.Show(DisplayName,
                    Loc.F("내 부탁 잊지 않았지? <color=#FFD966>{0}</color>\n{1}", RunQuest.Goal, LoopLine(WandererWaiting)),
                    new[] { leave });
                return;
            }

            ui.Show(DisplayName,
                Loc.F("{0} 부탁 하나만 하자. 이 층 끝, 보스 방 바로 앞인 <color=#FFD966>방 {1}</color>까지 가 줘.\n", LoopLine(WandererOpenings), RunQuest.ReportRoom) +
                Loc.F("거기서 기다릴게. 오면 <color=#FFD966>{0}</color>를 줄게.  ", RunQuest.RewardText) +
                Loc.T("<size=13><color=#9AA0AA>(죽어도 의뢰는 남는다)</color></size>"),
                new[]
                {
                    new NpcDialogUI.Choice(Loc.T("의뢰를 받는다"), AcceptQuest),
                    new NpcDialogUI.Choice(Loc.T("거절한다"), null),
                });
        }

        private void AcceptQuest()
        {
            RunQuest.Accept();
            _room.ShowMessage(Loc.F("의뢰를 받았다: {0}", RunQuest.Goal));
            NpcDialogUI.Instance.Show(DisplayName, Loc.F("고마워! 방 {0}에서 기다릴게. 먼저 가 있을게... 어떻게 가냐고? 나도 몰라.", RunQuest.ReportRoom),
                new[] { new NpcDialogUI.Choice(Loc.T("떠난다"), null) });
        }

        private void ClaimReward()
        {
            var message = RunQuest.Complete();
            if (message != null)
                _room.ShowMessage(message);
            NpcDialogUI.Instance.Show(DisplayName, Loc.T("받아. ...보스한테 지지 마. 다음 고리에서 또 부탁할지도 모르니까."),
                new[] { new NpcDialogUI.Choice(Loc.T("떠난다"), null) });
        }
    }
}
