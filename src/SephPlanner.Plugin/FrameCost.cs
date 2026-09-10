using System.Diagnostics;
using System.Globalization;
using System.Text;

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

            public double AverageMs => Count == 0 ? 0 : TotalMs / Count;

            /// <summary><see cref="Now"/> 로 받아 둔 시각을 넘긴다.</summary>
            public void Add(long startedAt)
            {
                var elapsed = (Stopwatch.GetTimestamp() - startedAt) * MsPerTick;
                Count++;
                TotalMs += elapsed;
                if (elapsed > WorstMs) WorstMs = elapsed;
            }

            public string Describe() =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-28} {1,7}회  평균 {2,7:0.00}ms  최악 {3,8:0.00}ms  합계 {4,8:0.0}ms",
                    Name, Count, AverageMs, WorstMs, TotalMs);
        }

        public static long Now => Stopwatch.GetTimestamp();

        public static readonly Step Poll = new Step("폴링 전체");
        public static readonly Step Read = new Step("  게임 상태 읽기");
        public static readonly Step Inventory = new Step("    가방 읽기");
        public static readonly Step Mixer = new Step("    합성기 찾기(씬 탐색)");
        public static readonly Step Sephirites = new Step("    세피라이트 찾기(씬 탐색)");
        public static readonly Step Chests = new Step("    상자·상점 찾기(씬 탐색)");
        public static readonly Step Simulation = new Step("  시뮬레이터 대조");
        public static readonly Step Feed = new Step("  지문 계산과 제출");
        public static readonly Step Panel = new Step("화면 갱신(매 프레임)");
        public static readonly Step BuildWindow = new Step("F2 내용 다시 그리기");

        /// <summary>
        /// 카탈로그를 짓느라 쓴 시간. 부팅 직후에는 지역화가 준비될 때까지 실패하며 여러 번
        /// 시도하므로, 횟수와 합계가 시작 화면의 체감 지연을 설명한다.
        /// </summary>
        public static readonly Step Catalog = new Step("카탈로그 짓기(시작 시)");

        /// <summary>Update 가 돈 횟수.</summary>
        public static int Frames { get; private set; }

        /// <summary>화면을 실제로 다시 그린 횟수. 프레임 수와의 차이가 곧 더티 검사의 효과다.</summary>
        public static int Draws { get; private set; }

        public static void CountFrame() => Frames++;
        public static void CountDraw() => Draws++;

        /// <summary>
        /// 지금까지의 요약 한 줄. 로그에 남길 것이라 짧아야 한다.
        /// </summary>
        public static string Summary() =>
            string.Format(
                CultureInfo.InvariantCulture,
                "메인 스레드 부담 - 폴링 {0}회 평균 {1:0.00}ms/최악 {2:0.00}ms " +
                "(가방 {3:0.00} 합성기 {4:0.00} 세피라이트 {5:0.00} 상자 {6:0.00} " +
                "대조 {7:0.00} 지문 {8:0.00}), " +
                "화면 평균 {9:0.000}ms, 프레임 {10}회 중 다시 그린 것 {11}회, " +
                "카탈로그 짓기 {12}회 합계 {13:0.0}ms (시도 {14}회)",
                Poll.Count, Poll.AverageMs, Poll.WorstMs,
                Inventory.AverageMs, Mixer.AverageMs, Sephirites.AverageMs, Chests.AverageMs,
                Simulation.AverageMs, Feed.AverageMs,
                Panel.AverageMs, Frames, Draws,
                Catalog.Count, Catalog.TotalMs, CatalogSource.Attempts);

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
            text.AppendLine("  " + Simulation.Describe());
            text.AppendLine("  " + Feed.Describe());
            text.AppendLine("  " + Panel.Describe());
            text.AppendLine("  " + BuildWindow.Describe());
            text.AppendLine("  " + Catalog.Describe() +
                            "  시도 " + CatalogSource.Attempts + "회");
            text.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  프레임 {0}회 중 화면을 다시 그린 것 {1}회 ({2:0.0}%)",
                    Frames, Draws, Frames == 0 ? 0 : 100.0 * Draws / Frames));
        }
    }
}
