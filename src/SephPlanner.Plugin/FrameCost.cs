using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 플러그인이 <b>메인 스레드에서</b> 실제로 쓰는 시간.
    ///
    /// 배치 풀이는 백그라운드로 넘겼지만 폴링은 여전히 게임 루프 안에서 돈다 - 씬 전수 탐색
    /// 세 번(<c>Sephirite</c>는 비활성까지 포함)과 시뮬레이터 대조가 거기에 있다. 그것이 몇
    /// 밀리초인지는 <b>게임을 켜야만</b> 알 수 있고, 모르는 채로 고치면 또 짐작이 된다.
    /// 그래서 스스로 재어 F10 덤프에 적고 가끔 로그에 남긴다.
    ///
    /// 재는 값은 세션 누적이다. 평균은 평소가 어떤지를, 최악은 끊김이 어디서 오는지를 말한다.
    /// 재는 비용 자체는 구간마다 타임스탬프 두 번이라 무시할 수 있다.
    ///
    /// <b>최악값이 왜 나왔는지까지 가른다.</b> 제보 셋에서 폴링 최악값은 세션 초반에 한 번 찍히고
    /// 그 뒤 늘지 않았는데, 짚이는 것이 첫 호출 JIT·GC·카탈로그 짓기 셋이라 시간만으로는 가를
    /// 수 없었다. 그래서 구간마다 <b>최악값이 찍힌 폴링 번호</b>를, 그리고 폴링마다 <b>0세대
    /// 수집 횟수</b>를 함께 적는다. 세션 초반의 낮은 번호면 첫 호출 JIT 이고, 그 폴링 안에서
    /// 수집이 돌았으면 GC 이고, 번호가 세션 내내 갱신되면 자료가 늘어난 것이다.
    ///
    /// 모두 메인 스레드에서만 손대므로 잠금이 없다.
    /// </summary>
    internal static class FrameCost
    {
        /// <summary>한 구간의 누적. 평균과 최악을 함께 들고 있어야 끊김을 설명할 수 있다.</summary>
        internal sealed class Step
        {
            private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

            public Step(string name) => Name = name;

            public string Name { get; }
            public int Count { get; private set; }
            public double TotalMs { get; private set; }
            public double WorstMs { get; private set; }

            /// <summary>
            /// <see cref="WorstMs"/>가 찍혔을 때 돌고 있던 폴링 번호. 매 프레임 도는 구간도 이것으로
            /// 세션의 어디쯤이었는지 말한다.
            /// </summary>
            public int WorstPoll { get; private set; }

            public double AverageMs => Count == 0 ? 0 : TotalMs / Count;

            /// <summary><see cref="Now"/> 로 받아 둔 시각을 넘긴다.</summary>
            public void Add(long startedAt)
            {
                var elapsed = (Stopwatch.GetTimestamp() - startedAt) * MsPerTick;
                var poll = PollNumber;
                Count++;
                TotalMs += elapsed;
                if (elapsed <= WorstMs) return;
                WorstMs = elapsed;
                WorstPoll = poll;
            }

            public string Describe() =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-28} {1,7}회  평균 {2,7:0.00}ms  최악 {3,8:0.00}ms(폴링 {5,6})  합계 {4,8:0.0}ms",
                    Name, Count, AverageMs, WorstMs, TotalMs, WorstPoll);
        }

        public static long Now => Stopwatch.GetTimestamp();

        public static readonly Step Poll = new Step("폴링 전체");
        public static readonly Step Read = new Step("  게임 상태 읽기");
        public static readonly Step Inventory = new Step("    가방 읽기");
        public static readonly Step Mixer = new Step("    합성기 찾기(씬 탐색)");
        public static readonly Step Sephirites = new Step("    세피라이트 찾기(씬 탐색)");
        public static readonly Step Chests = new Step("    상자·상점 찾기(씬 탐색)");

        /// <summary>
        /// <c>상자·상점 찾기</c>를 가른 것. 이렇게 갈라 재서 4번의 답이 나왔다 - 셋 중
        /// <c>FindObjectsByType</c> 하나가 99% 였고 나머지 둘은 합쳐서 1% 였다. 지금은 씬을 훑지
        /// 않으므로 <see cref="ChestAlive"/>가 <see cref="NetworkRegistry"/> 순회 비용이다.
        /// </summary>
        public static readonly Step ChestAlive = new Step("      등록부 훑기");

        /// <summary>셋(상자·세피라이트·합성기)의 대조 탐색을 한데 모아 잰다.</summary>
        public static readonly Step RegistryCheck = new Step("  등록부 대조(옛 씬 탐색)");
        public static readonly Step ChestFilter = new Step("      거리·보임 판정");
        public static readonly Step ChestCollect = new Step("      내용 담기");

        /// <summary>
        /// 마지막 폴링에서 들고 있던 인벤토리 수와, 세션 최대. 위의 훑기가 <c>O(n)</c> 이라
        /// 시간만으로는 "무엇이 많아서" 인지 알 수 없다. 기계와 무관한 수라 전후 비교에도 쓴다.
        /// </summary>
        public static int InventoriesHeld { get; private set; }
        public static int InventoriesHeldMost { get; private set; }
        public static int InventoriesNear { get; private set; }
        public static int InventoriesNearMost { get; private set; }

        /// <summary>
        /// 등록부가 찾은 것과 씬 전수 탐색이 찾은 것이 어긋난 횟수. <b>0 이 아니면 등록부로
        /// 바꾼 것이 무언가를 놓치고 있다는 뜻이다</b> - 특히 풀에서 나온 바닥 꾸러미가 Mirror 에
        /// 등록되지 않는 경우가 그렇다. 대조는 가끔만 돌므로 이 수는 그 대조 안에서만 는다.
        /// </summary>
        public static int RegistryMismatches { get; private set; }
        public static int RegistryChecks { get; private set; }
        public static string RegistryFirstMismatch { get; private set; } = "";

        public static void CountRegistryCheck(int missing, string detail)
        {
            RegistryChecks++;
            if (missing == 0) return;
            RegistryMismatches++;
            if (RegistryFirstMismatch.Length == 0) RegistryFirstMismatch = detail;
        }

        public static void CountInventories(int held, int near)
        {
            InventoriesHeld = held;
            InventoriesNear = near;
            if (held > InventoriesHeldMost) InventoriesHeldMost = held;
            if (near > InventoriesNearMost) InventoriesNearMost = near;
        }
        public static readonly Step Simulation = new Step("  시뮬레이터 대조");
        public static readonly Step Feed = new Step("  지문 계산과 제출");
        public static readonly Step Panel = new Step("화면 갱신(매 프레임)");

        /// <summary>
        /// 카탈로그를 짓느라 쓴 시간. 부팅 직후에는 지역화가 준비될 때까지 실패하며 여러 번
        /// 시도하므로, 횟수와 합계가 시작 화면의 체감 지연을 설명한다.
        /// </summary>
        public static readonly Step Catalog = new Step("카탈로그 짓기(시작 시)");

        /// <summary>플러그인이 붙은 시점의 0세대 수집 횟수. 세션 합계를 여기서부터 센다.</summary>
        private static readonly int CollectionsAtStart = GC.CollectionCount(0);

        private static int _collectionsAtPollStart = CollectionsAtStart;

        /// <summary>Update 가 돈 횟수.</summary>
        public static int Frames { get; private set; }

        /// <summary>화면을 실제로 다시 그린 횟수. 프레임 수와의 차이가 곧 더티 검사의 효과다.</summary>
        public static int Draws { get; private set; }

        /// <summary>지금 돌고 있는 폴링의 번호(1부터). 폴링 밖에서 읽으면 다음 폴링의 번호다.</summary>
        public static int PollNumber => Poll.Count + 1;

        /// <summary>
        /// 세션 동안 일어난 0세대 수집 횟수. 유니티의 Boehm 은 세대가 하나뿐이라 이것이 전부다.
        /// 백그라운드 풀이가 만든 쓰레기까지 여기에 잡히므로, 할당을 줄이는 수정의 인게임 이득을
        /// 최악값으로 에둘러 보지 않고 이 수로 바로 읽는다.
        /// </summary>
        public static int Collections => GC.CollectionCount(0) - CollectionsAtStart;

        /// <summary>
        /// 폴링 최악값이 찍힌 그 폴링 <b>안에서</b> 일어난 0세대 수집 횟수. 0이면 그 끊김은
        /// GC 가 아니다.
        /// </summary>
        public static int WorstPollCollections { get; private set; }

        /// <summary>
        /// 백그라운드 풀이의 계측을 가져오는 자리. 실행기는 카탈로그가 준비된 뒤에야 생기고 F9
        /// 뒤에는 새로 지어지므로, 들고 있지 않고 그때그때 묻는다. 없으면 그 줄을 아예 안 적는다.
        /// </summary>
        public static Func<PlanRunnerStats> PlanStats;

        public static void CountFrame() => Frames++;
        public static void CountDraw() => Draws++;

        /// <summary>폴링 하나를 연다. 시작 시각을 돌려주고, 그 사이의 수집을 셀 기준을 잡는다.</summary>
        public static long BeginPoll()
        {
            _collectionsAtPollStart = GC.CollectionCount(0);
            return Now;
        }

        /// <summary>
        /// 폴링 하나를 닫는다. 걸린 시간과 그 사이에 돈 수집을 함께 적어야, 최악값이 산수인지
        /// 남이 세운 것인지 나중에 읽을 수 있다.
        /// </summary>
        public static void FinishPoll(long startedAt)
        {
            var collected = GC.CollectionCount(0) - _collectionsAtPollStart;
            var number = PollNumber;
            Poll.Add(startedAt);
            if (Poll.WorstPoll == number) WorstPollCollections = collected;
        }

        /// <summary>
        /// 지금까지의 요약 한 줄. 로그에 남길 것이라 짧아야 한다.
        /// </summary>
        public static string Summary() =>
            string.Format(
                CultureInfo.InvariantCulture,
                "메인 스레드 부담 - 폴링 {0}회 평균 {1:0.00}ms/최악 {2:0.00}ms(폴링 {15}) " +
                "(가방 {3:0.00} 합성기 {4:0.00} 세피라이트 {5:0.00} 상자 {6:0.00} " +
                "대조 {7:0.00} 지문 {8:0.00}), " +
                "화면 평균 {9:0.000}ms, 프레임 {10}회 중 다시 그린 것 {11}회, " +
                "카탈로그 짓기 {12}회 합계 {13:0.0}ms (시도 {14}회), " +
                "0세대 수집 {16}회(최악 폴링 안에서 {17}회), " +
                "상자 = 등록부 {18:0.00} 판정 {20:0.00} 담기 {21:0.00} (인벤토리 {22}개), 대조 {19:0.00}ms 어긋남 {23}/{24}",
                Poll.Count, Poll.AverageMs, Poll.WorstMs,
                Inventory.AverageMs, Mixer.AverageMs, Sephirites.AverageMs, Chests.AverageMs,
                Simulation.AverageMs, Feed.AverageMs,
                Panel.AverageMs, Frames, Draws,
                Catalog.Count, Catalog.TotalMs, CatalogSource.Attempts,
                Poll.WorstPoll, Collections, WorstPollCollections,
                ChestAlive.AverageMs, RegistryCheck.AverageMs, ChestFilter.AverageMs, ChestCollect.AverageMs,
                InventoriesHeld, RegistryMismatches, RegistryChecks) + PlanSummary();

        /// <summary>
        /// 풀이와 게시를 요약한 꼬리. 폴링 비용만으로는 "계산이 안 끝난다" 를 설명할 수 없어
        /// 같은 줄에 붙인다. 밀린 세대가 0 이 아닌 채로 이어지면 답이 요청을 못 따라가는 것이다.
        /// </summary>
        private static string PlanSummary()
        {
            var stats = PlanStats?.Invoke();
            if (stats == null) return "";

            return string.Format(
                CultureInfo.InvariantCulture,
                ", 계획 풀이 - 배치 {0}회 평균 {1:0}ms/최악 {2:0}ms(풀이 {3}) 버린 것 {4}회, " +
                "조언 {5}회 평균 {6:0}ms, 게시 {7}회 지연 최근 {8:0}ms/최악 {9:0}ms, " +
                "밀린 세대 {10}(최악 {11})",
                stats.Placement.Count, stats.Placement.AverageMs, stats.Placement.WorstMs,
                stats.Placement.WorstRun, stats.Placement.Discarded,
                stats.Advice.Count, stats.Advice.AverageMs,
                stats.Published, stats.PublishDelayMs, stats.WorstPublishDelayMs,
                stats.Backlog, stats.WorstBacklog);
        }

        private static string Describe(string name, PlanSolveStat stat) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0,-28} {1,7}회  평균 {2,7:0.00}ms  최악 {3,8:0.00}ms(풀이 {4,6})  버린 것 {5}회",
                name, stat.Count, stat.AverageMs, stat.WorstMs, stat.WorstRun, stat.Discarded);

        private static void WritePlan(StringBuilder text)
        {
            var stats = PlanStats?.Invoke();
            if (stats == null) return;

            text.AppendLine();
            text.AppendLine("[perf] 계획 풀이 (백그라운드 스레드)");
            text.AppendLine("  " + Describe("배치 풀이", stats.Placement));
            text.AppendLine("  " + Describe("조언 풀이", stats.Advice) +
                            (stats.Advice.Count == 0 ? "  (아직 배치와 함께 푼다)" : ""));
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  게시 {0}회 - 요청에서 게시까지 최근 {1:0.0}ms, 최악 {2:0.0}ms({3}번째 게시)",
                    stats.Published, stats.PublishDelayMs, stats.WorstPublishDelayMs,
                    stats.WorstPublishRun));
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  밀린 세대 - 지금 {0}, 세션 최악 {1}",
                    stats.Backlog, stats.WorstBacklog));
        }

        public static void Write(StringBuilder text)
        {
            text.AppendLine();
            text.AppendLine("[perf] 메인 스레드에서 쓴 시간 (세션 누적)");
            text.AppendLine("  " + Poll.Describe());
            text.AppendLine("  " + Read.Describe());
            text.AppendLine("  " + Inventory.Describe());
            text.AppendLine("  " + Mixer.Describe());
            text.AppendLine("  " + Sephirites.Describe());
            text.AppendLine("  " + Chests.Describe());
            text.AppendLine("  " + ChestAlive.Describe());
            text.AppendLine("  " + ChestFilter.Describe());
            text.AppendLine("  " + ChestCollect.Describe());
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "      인벤토리 - 등록부 {0}개(최대 {1}), 사정거리 안 {2}개(최대 {3})",
                    InventoriesHeld, InventoriesHeldMost, InventoriesNear, InventoriesNearMost));
            text.AppendLine("  " + RegistryCheck.Describe());
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  등록부 대조 - {0}회 중 어긋남 {1}회{2}",
                    RegistryChecks, RegistryMismatches,
                    RegistryFirstMismatch.Length == 0 ? "" : " (" + RegistryFirstMismatch + ")"));
            text.AppendLine("  " + Simulation.Describe());
            text.AppendLine("  " + Feed.Describe());
            text.AppendLine("  " + Panel.Describe());
            text.AppendLine("  " + Catalog.Describe() +
                            "  시도 " + CatalogSource.Attempts + "회");
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  0세대 수집 - 세션 동안 {0}회, 최악 폴링({1}번째) 안에서 {2}회",
                    Collections, Poll.WorstPoll, WorstPollCollections));
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  프레임 {0}회 중 화면을 다시 그린 것 {1}회 ({2:0.0}%)",
                    Frames, Draws, Frames == 0 ? 0 : 100.0 * Draws / Frames));

            WritePlan(text);
        }
    }
}
