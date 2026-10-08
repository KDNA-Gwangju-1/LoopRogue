using UnityEngine;

namespace LoopRogue
{
    /// <summary>로비 허브의 NPC "시간지기 노인" - 장기 의뢰(LobbyQuests) 보상 받기 / 의뢰 현황 / 세계관 이야기(스테이지마다 바뀜) /
    /// 고리에 대해(누적 회차로 열리는 기억의 조각) / 조언. 인사는 누적 회차(LoopRecord)에 따라 바뀌고, 몇몇 회차엔 한 번만 하는 말이 있다.
    /// 로비엔 걸어 다니는 캐릭터가 없어서 허브의 버튼이나 4 키로 말을 건다(LobbyBootstrap).</summary>
    public static class LobbyGuide
    {
        public const string Name = "시간지기 노인";

        private static readonly System.Random Rng = new System.Random();

        /// <summary>보스방에서 죽었을 때 사망 창에 먼저 띄우는 조언.</summary>
        private static readonly string[] BossTips =
        {
            "보스방엔 안개가 없네. 바닥의 빨간 칸은 다음 턴에 맞는 자리니 비켜서게. 움직일 데가 없으면 Space로 버티는 것도 방법이야.",
            "<color=#FFD966>섬광탄</color>은 보스의 예고 공격을 취소하고 잠깐 기절시키지. 아껴 두다 죽으면 그게 제일 아까운 걸세.",
            "<color=#FFD966>반사 부적</color>을 켜 두면 보스의 예고 공격 한 번을 그대로 되돌려 주지.",
            "보스가 부른 졸개는 보스를 쓰러뜨리면 같이 사라지네. 졸개만 쫓다 지치지 말게.",
            "보스한테 계속 막힌다면 뽑기와 영약으로 몸부터 키우게. 버틸 체력이 있어야 패턴도 보이는 법이지.",
        };

        private static readonly string[] Tips =
        {
            "방을 다 비우면 <color=#7FD4FF>파란 출구 칸</color>이 열리네. 안개 속에 숨어 있으니 직접 찾아야 해.",
            "층마다 보물상자가 하나는 꼭 숨어 있네. 출구로 나가기 전에 방을 한 바퀴 둘러보게.",
            "방을 비우면 최대 체력의 10%가 돌아오네. 많이 깎였다면 다음 방은 조심스럽게 들어가게.",
            "층에 들어갈 때나 죽은 뒤엔 시작할 방을 고를 수 있네. B를 누르면 보스방으로 바로 갈 수도 있지. 자신 있다면 말이야.",
            "같은 층을 오래 돌수록 일반 몹 보상이 조금씩 줄어드네(절반까지). 버틸 만하면 앞으로 나아가게.",
            "불붙은 폭발병 주변의 주황 칸은 다음 턴에 터지네. 터지기 전에 잡으면 불발이야.",
            "거미줄에 묶이면 이동과 대시가 막히지만 공격과 회전 베기는 되네. <color=#FFD966>정화제</color>로 바로 풀 수도 있고.",
            "궁수는 같은 줄에서만 쏜다네. 대각선으로 돌아서 다가가게. 벽 뒤에선 못 쏘지.",
            "층 보스를 처음 쓰러뜨리면 유물을 하나 고를 수 있네. 유물은 영구라 신중하게 고르게.",
            "뽑기는 10연차가 9번 값이라 한 번이 공짜일세.",
            "영약은 살수록 5골드씩 비싸지네. 떠돌이 상인은 첫 구매 값에 파니, 만나면 꼭 들여다보게.",
            "아이템은 종류마다 하나씩만 들 수 있네. 아끼다 죽으면 다 잃으니 쓰는 게 이득이야.",
            "<color=#FFD966>횃불</color>을 놓아두면 그 주변이 계속 밝게 보이네. 넓은 방에서 출구를 찾을 때 좋지.",
            "방을 비우면 가끔 <color=#FFD966>떠돌이 상인</color>이 나타나네. 영약이나 뽑기권, 슬롯 이용권을 싸게 파는 날도 있지.",
            "보랏빛 연기와 함께 <color=#C890FF>그림자 거래상</color>이 나타나거든 조심하게. 힘을 주지만 반드시 값을 받아 가지. 죽으면 계약도 풀리긴 하네만.",
            "층마다 첫 방을 비우면 <color=#FFD966>길 잃은 모험가</color>가 나올 때가 있네. 방 10까지 가면 거기서 기다렸다가 보상을 준다더군.",
            "Space로 제자리에서 기다릴 수 있네. 보스가 예고한 칸에서 비켜서 한 박자 쉬는 것도 실력이지.",
            "아이템은 퀵슬롯(1/2/3)에 올린 것만 쓸 수 있어. I로 가방을 열어 미리 등록해 두게.",
            "Q 대시는 달려들면서 한 대, E 회전 베기는 둘러싸였을 때. 쿨타임을 늘 보고 있게.",
            "방패병은 정면으로 때리면 거의 안 들어가. 옆으로 돌아가거나 회전 베기로 방패를 무시하게.",
            "장비 뽑기는 많이 뽑을수록 상점 레벨이 올라서 좋은 등급이 잘 나온다네.",
            "죽거나 도중에 나오면 그 시도에서 번 골드 일부를 잃어. 벌 만큼 벌었다 싶으면 보스 전에 정리하는 것도 방법이지.",
            "조언? 덜 죽게. ...농담일세. 진짜 조언은 이거야: 체력이 반 아래면 욕심내지 말게.",
            "내 조언을 다 귀담아들었다면 자네는 지금쯤 10층에 있어야 하지 않나? 허허.",
        };

        // ---- 루프 회차별 대사(LoopRecord) ----

        /// <summary>특정 누적 사망 수에 처음 말을 걸 때 한 번만 하는 말 - LoopRecord.Milestones와 같은 순서.</summary>
        private static readonly string[] MilestoneLines =
        {
            "...처음으로 죽었군. 아팠나? 그 아픔을 기억해 두게. 이 고리에서 자네가 가지고 나갈 수 있는 건 그것뿐이니까.",
            "열 번. 이쯤 되면 다들 묻지. '왜 끝나지 않느냐'고. 답은... 아직 이르네.",
            "서른 번째군. 혹시 꿈에서 같은 방을 본 적 있나? 고리가 자네 안으로 스며들고 있다는 뜻일세.",
            "쉰 번. 예전에 나도 딱 이쯤에서 세는 걸 그만뒀지. 자네는 그러지 말게. 세는 동안은 아직 자네 자신이니까.",
            "백 번째 귀환이군. ...이건 비밀인데, 나도 처음엔 자네처럼 저 문으로 내려갔었네. 그리고 백 번째 죽음에서, 돌아가지 않기로 했지.",
            "이백. 자네는 이미 나보다 멀리 왔어. 이 고리를 끊는 자가 있다면, 그건 분명 자네일세.",
        };

        /// <summary>회차 구간(LoopRecord.Tier)별 인사 - {0} = 누적 사망 수. 구간마다 몇 개 중 하나.</summary>
        private static readonly string[][] TierGreetings =
        {
            new[] { "...새 얼굴이군. 이 고리에 들어온 걸 환영하네. 죽어도 끝나지 않는 곳이니, 너무 겁먹지는 말게." },
            new[]
            {
                "또 돌아왔군. 처음엔 다들 그렇게 쓰러진다네. 부끄러워할 것 없어.",
                "돌아왔나. 죽음이 끝이 아니라는 게 아직 낯설지?",
                "벌써 왔나? 차 한 잔 우릴 시간도 안 됐는데.",
                "허허, 그 표정. 처음 죽은 녀석들은 다 그 얼굴이지.",
            },
            new[]
            {
                "자네 얼굴이 이제 낯설지 않군. {0}번째 귀환이던가?",
                "또 왔군. 문 여는 소리만 들어도 자네인 줄 알겠어.",
                "{0}번째라... 문지기 녀석들이 자네를 '단골'이라고 부른다더군.",
                "또 졌나? 아니, 말 안 해도 돼. 얼굴에 다 쓰여 있어.",
            },
            new[]
            {
                "{0}번... 그 정도면 고리가 자네를 기억하기 시작했을 걸세.",
                "돌아올 때마다 눈빛이 조금씩 달라지는군. 좋은 쪽인지는 모르겠지만.",
                "{0}번. 이쯤이면 죽는 데에도 요령이 붙었겠군. 사는 요령은 아직인가?",
                "자네가 죽을 때마다 내 찻잔에 금이 하나씩 간다네. 슬슬 새 잔을 사야겠어.",
            },
            new[]
            {
                "자네가 문을 열고 들어올 때마다 바람 냄새가 조금씩 달라져. {0}번이나 돌았으니 그럴 만도 하지.",
                "{0}번. 이젠 자네가 고리를 도는 건지, 고리가 자네를 도는 건지 모르겠군.",
                "{0}번이나 죽었으면 몬스터들한테 밥이라도 얻어먹어야 하는 거 아닌가?",
                "내가 젊었을 땐 말이야... 아니, 그때도 자네보단 덜 죽었네.",
            },
            new[]
            {
                "{0}번. 이젠 내가 자네를 기다리고 있었다는 걸 숨기지 않겠네.",
                "어서 오게. ...자네가 돌아오지 않는 날이 오면, 그게 고리가 끊긴 날이겠지.",
                "{0}번이라... 고리가 자네를 놓아주지 않는 게 아니라, 자네가 고리를 못 놓는 것 같군.",
                "또 왔나. 이번엔 무슨 핑계인가? 함정? 운? 아니면 그냥 실력?",
            },
        };

        /// <summary>상황 도발 - 인사 뒤에 가끔 한 줄 덧붙인다(TauntChance). 조건이 맞는 것 중 하나.</summary>
        private const double TauntChance = 0.5;
        private const int StuckAttempts = 5; // 한 층에서 이만큼 시도했으면 "막혔다"로 본다

        private static string SituationalTaunt()
        {
            var options = new System.Collections.Generic.List<string>();
            var stage = StageProgress.CurrentStage;
            var attempts = StageProgress.AttemptsThisStage;
            if (attempts >= StuckAttempts)
            {
                options.Add($"{stage}층에서만 벌써 {attempts}번째군. 그 층 몬스터들이 자네 이름을 외웠겠어.");
                options.Add($"{stage}층 문지기가 자네 안부를 묻더군. 언제 또 오냐고.");
            }
            if (GoldWallet.Gold < GachaSystem.TicketPrice)
            {
                options.Add("주머니가 가볍군. 죽을 때마다 흘리고 다니니 그렇지.");
                options.Add("골드가 그것뿐인가? 도박장 근처엔 얼씬도 하지 말게.");
            }
            if (SlotMachine.Tickets > 0)
                options.Add($"슬롯 이용권을 {SlotMachine.Tickets}장이나 쥐고 있군. 아껴 두면 이자라도 붙는 줄 아나?");
            return options.Count > 0 && Rng.NextDouble() < TauntChance ? options[Rng.Next(options.Count)] : null;
        }

        /// <summary>"고리에 대해" - 누적 사망 수가 쌓일수록 하나씩 열리는 기억의 조각.</summary>
        private static readonly (int Deaths, string Text)[] Fragments =
        {
            (5, "이 고리는 누군가의 '후회'로 만들어졌다고 하네. 같은 순간을 몇 번이고 다시 살고 싶었던 누군가의."),
            (15, "던전의 몬스터들도 고리에 갇혀 있네. 그래서 같은 자리에서, 같은 모습으로 자네를 기다리는 거지. 녀석들도 지쳤을 걸세."),
            (30, "떠돌이 상인? 그 친구는 고리 사이를 오가는 몇 안 되는 존재야. 어떻게 그러는지는 나도 몰라. 묻지 않는 게 예의지."),
            (60, "길 잃은 모험가는... 자네가 처음 들어오기 전에도 1층에 있었네. 늘 같은 붕대, 같은 지팡이로. 그 녀석은 자기가 몇 번째인지 기억 못 해."),
            (100, "고리를 만든 자는 10층 아래에 있다고들 하지. 하지만 내 생각엔... 고리를 만든 건 들어온 사람들 자신일지도 모르네. 돌아오고 싶은 마음이 문을 연 거야."),
        };

        public static void Talk()
        {
            var milestone = LoopRecord.PendingMilestone;
            if (milestone.HasValue && NpcDialogUI.Instance != null)
            {
                LoopRecord.MarkMilestoneSeen(milestone.Value);
                var line = MilestoneLines[System.Array.IndexOf(LoopRecord.Milestones, milestone.Value)];
                NpcDialogUI.Instance.Show(Name, line, new[] { new NpcDialogUI.Choice("...", () => ShowRoot(null)) });
                return;
            }
            ShowRoot(null);
        }

        /// <summary>노인에게 아직 못 들은 특별한 말이 있는지(로비 버튼 표시용).</summary>
        public static bool HasSomethingToSay => LoopRecord.PendingMilestone.HasValue;

        private static NpcDialogUI.Choice Back => new NpcDialogUI.Choice("돌아간다", () => ShowRoot("또 궁금한 게 있나?"));
        private static NpcDialogUI.Choice Leave => new NpcDialogUI.Choice("떠난다", null);

        private static void ShowRoot(string line)
        {
            var ui = NpcDialogUI.Instance;
            if (ui == null)
                return;

            var pending = LobbyQuests.PendingCount;
            var body = line ?? Greeting();
            if (pending > 0)
                body += $"\n<color=#FFD966>약속한 보상이 {pending}건 쌓여 있네.</color>";

            var unlocked = UnlockedFragments;
            ui.Show(Name, body, new[]
            {
                new NpcDialogUI.Choice(pending > 0 ? $"의뢰 보상 받기 <color=#FFD966>({pending}건)</color>" : "의뢰 보상 받기 <color=#888888>(받을 보상 없음)</color>",
                    Claim, enabled: pending > 0),
                new NpcDialogUI.Choice("의뢰 현황", ShowQuests),
                new NpcDialogUI.Choice("이 던전에 대해", ShowLore),
                new NpcDialogUI.Choice(unlocked > 0 ? $"고리에 대해 묻는다 <color=#B9A0FF>(기억의 조각 {unlocked}/{Fragments.Length})</color>"
                        : "고리에 대해 묻는다 <color=#888888>(아직 들려줄 게 없다)</color>",
                    () => ShowFragment(unlocked - 1), enabled: unlocked > 0),
                new NpcDialogUI.Choice("조언을 구한다", ShowTip),
                Leave,
            });
        }

        private static string Greeting()
        {
            var deaths = LoopRecord.TotalDeaths;
            var lines = TierGreetings[LoopRecord.Tier];
            var greeting = string.Format(lines[Rng.Next(lines.Length)], deaths);
            if (deaths == 0)
                return greeting;

            // 도발 아니면 지금 상태에 맞는 조언 한 줄(둘 다 붙이면 말이 너무 길어진다).
            var taunt = SituationalTaunt();
            var tip = taunt == null ? SituationalTip() : null;
            if (taunt != null)
                greeting += $"\n<color=#FFB080>{taunt}</color>";
            else if (tip != null)
                greeting += $"\n<color=#9FD8FF>{tip}</color>";

            var stage = StageProgress.CurrentStage;
            var stageLine = stage >= StageProgress.MaxStage
                ? "여기까지 왔나. 고리의 끝이 가깝네."
                : $"지금은 {stage}층이던가?";
            return $"{greeting}\n<size=13><color=#9AA0AA>{stageLine}  (지금까지의 귀환: {deaths}번)</color></size>";
        }

        private static int UnlockedFragments
        {
            get
            {
                var count = 0;
                foreach (var f in Fragments)
                    if (LoopRecord.TotalDeaths >= f.Deaths)
                        count++;
                return count;
            }
        }

        /// <summary>기억의 조각 하나 - 열린 조각끼리 앞뒤로 넘겨 본다. 다 안 열렸으면 다음 조각까지 남은 회차를 귀띔.</summary>
        private static void ShowFragment(int index)
        {
            var unlocked = UnlockedFragments;
            index = Mathf.Clamp(index, 0, unlocked - 1);
            var text = $"<color=#B9A0FF>기억의 조각 {index + 1}</color>\n{Fragments[index].Text}";
            if (index == unlocked - 1 && unlocked < Fragments.Length)
                text += $"\n<size=13><color=#9AA0AA>...나머지는 자네가 {Fragments[unlocked].Deaths - LoopRecord.TotalDeaths}번 더 돌아오면 이야기하지.</color></size>";

            NpcDialogUI.Instance.Show(Name, text, new[]
            {
                new NpcDialogUI.Choice("이전 조각", () => ShowFragment(index - 1), enabled: index > 0),
                new NpcDialogUI.Choice("다음 조각", () => ShowFragment(index + 1), enabled: index < unlocked - 1),
                Back,
                Leave,
            });
        }

        private static void Claim()
        {
            var gold = LobbyQuests.ClaimAll();
            NpcDialogUI.Instance.Show(Name, $"약속은 약속이지. 받게.\n<color=#FFD966>골드 +{gold}</color>", new[] { Back, Leave });
        }

        private static void ShowQuests()
        {
            var stage = StageProgress.CurrentStage;
            var stageLine = stage >= StageProgress.MaxStage
                ? "<b>층 돌파 의뢰</b> - 마지막 층까지 왔네. 더 줄 건 없어."
                : $"<b>층 돌파 의뢰</b> - {stage}층 보스를 쓰러뜨리고 {stage + 1}층에 닿으면 보상";
            var killLine = $"<b>토벌 의뢰</b> - 누적 처치 {LobbyQuests.LifetimeKills}마리, " +
                           $"다음 보상까지 {LobbyQuests.KillsToNextMilestone}마리 ({LobbyQuests.KillMilestone}마리마다 보상)";
            var wandererLine = RunQuest.Active
                ? $"\n<b>모험가의 부탁</b> - {RunQuest.Goal} <color=#FFD966>(진행 중, 지금 {stage}층)</color>"
                : string.Empty;
            NpcDialogUI.Instance.Show(Name,
                $"{stageLine}\n{killLine}{wandererLine}\n<size=13><color=#9AA0AA>내 보상은 나한테 와서 받아 가게. 죽어도 사라지지 않네.</color></size>",
                new[] { Back, Leave });
        }

        private static void ShowLore()
        {
            var stage = StageProgress.CurrentStage;
            var text = stage <= 3
                ? "이 던전은 죽어도 끝나지 않아. 쓰러지면 같은 층 입구로 돌아가지... 대신 네가 쌓은 힘은 그대로 남네. 그게 이 고리의 유일한 자비야."
                : stage <= 6
                    ? "층마다 문지기가 있네. 그 녀석을 쓰러뜨려야만 고리가 한 칸 앞으로 나아가지. 그 전엔 몇 번을 죽든 같은 층이야."
                    : stage < StageProgress.MaxStage
                        ? "깊이 내려갈수록 고리를 만든 자의 숨결이 느껴질 걸세. 나도 한때는 자네처럼 내려갔었지... 지금은 이렇게 문 앞에 남아 있지만."
                        : "마지막 층이군. 그 끝에서 무엇을 보든, 이 고리를 끊을 수 있는 건 자네뿐이야.";
            NpcDialogUI.Instance.Show(Name, text, new[] { Back, Leave });
        }

        // ---- 조언(팁) ----

        private static int _lastTip = -1;

        /// <summary>일반 조언 하나 - 바로 앞에 한 것과 겹치지 않게.</summary>
        private static string RandomGeneralTip()
        {
            var i = Rng.Next(Tips.Length);
            if (i == _lastTip)
                i = (i + 1) % Tips.Length;
            _lastTip = i;
            return Tips[i];
        }

        /// <summary>사망 창에 띄우는 노인의 한마디(GameHUD) - 보스방에서 죽었으면 보스 조언을 먼저.</summary>
        public static string DeathTip(bool bossRoom) =>
            bossRoom && Rng.NextDouble() < 0.7 ? BossTips[Rng.Next(BossTips.Length)] : RandomGeneralTip();

        /// <summary>지금 상태를 보고 하는 조언(빈 장비 칸, 안 쓴 뽑기권, 해금된 영약 등) - 해당하는 게 없으면 null.</summary>
        private static string SituationalTip()
        {
            var options = new System.Collections.Generic.List<string>();
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                if (!EquipmentWallet.GetEquipped(slot).HasValue)
                    options.Add($"아직 <color=#FFD966>{EquipmentData.Templates[slot].BaseName}</color>이(가) 없군. 맨몸으로 내려가지 말고 뽑기 상점부터 들르게.");
            if (GachaSystem.Tickets > 0)
                options.Add($"뽑기권이 {GachaSystem.Tickets}장 있군. 뽑기 상점에서 1회 뽑기를 누르면 골드 대신 뽑기권이 먼저 쓰이네.");
            if (StatPotionWallet.IsUnlocked(PotionType.Critical) && StatPotionWallet.GetCount(PotionType.Critical) == 0)
                options.Add("이제 <color=#FFD966>치명타 영약</color>을 살 수 있네. 한 방이 세지면 보스전이 훨씬 짧아지지.");
            if (StageProgress.AttemptsThisStage >= StuckAttempts)
                options.Add($"{StageProgress.CurrentStage}층에서 막혔다면 영약과 장비를 먼저 올리게. 몸이 버텨야 실력도 나오는 법이지.");
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
            {
                if (GoldWallet.Gold >= GachaSystem.GetMultiPullCost(slot))
                {
                    options.Add($"골드가 {GoldWallet.Gold}이나 있군. 뽑기는 10연차가 1회분 공짜니 한 번에 돌리게.");
                    break;
                }
            }
            return options.Count > 0 ? options[Rng.Next(options.Count)] : null;
        }

        /// <summary>첫 조언은 지금 상태에 맞는 것부터, "다른 조언도"는 일반 조언.</summary>
        private static void ShowTip() => ShowTipText(SituationalTip() ?? RandomGeneralTip());

        private static void ShowTipText(string tip) =>
            NpcDialogUI.Instance.Show(Name, tip,
                new[] { new NpcDialogUI.Choice("다른 조언도", () => ShowTipText(RandomGeneralTip())), Back, Leave });
    }
}
