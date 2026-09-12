using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Planning
{
    /// <summary>
    /// 풀 배치가 없을 때 왜 없는지.
    ///
    /// 답이 없다는 것과 아직 못 구했다는 것은 다르다. 그냥 <c>null</c> 만 돌려주면 화면이 둘을
    /// 가릴 수 없어 "계산 중"에 영영 멈춰 있는 것처럼 보인다 - 실제로 그렇게 보였다.
    /// </summary>
    public enum PlanBlocker
    {
        /// <summary>막힌 것이 없다. 배치가 나왔거나 아직 계산 전이다.</summary>
        None,

        /// <summary>탐험 중이 아니거나 가방을 읽지 못했다.</summary>
        NoInventory,

        /// <summary>격자가 비어 있다. 아직 아무것도 줍지 않았다.</summary>
        NoCharms,

        /// <summary>격자에 물건은 있는데 카탈로그가 아는 아티팩트가 하나도 없다.</summary>
        UnknownItems,
    }

    /// <summary>
    /// 1단계(배치)가 2단계(조언)에게 넘기는 중간물.
    ///
    /// 조언은 배치가 세운 <see cref="PlacementProblem"/> 을 그대로 쓴다. 그것을 다시 짓게 하면
    /// 가방을 통째로 다시 읽어 슬롯을 만드는 일을 조언마다 되풀이하게 된다. 나머지(최선 배치·
    /// 검증·무기·값어치)는 <see cref="Plan"/> 과 설정에 이미 있으므로 여기 담지 않는다.
    /// </summary>
    internal sealed class PlacementWork
    {
        public PlacementWork(PlacementProblem problem) => Problem = problem;

        public PlacementProblem Problem { get; }
    }

    /// <summary>스냅샷과 카탈로그를 합쳐 현재 배치를 채점하고 더 나은 배치를 찾는다.</summary>
    public static class PlanBuilder
    {
        /// <summary>
        /// 다 키우면 다른 아티팩트가 되는 것은 그만큼 값어치를 끌어올린다. 목표치는 스냅샷에 적힌
        /// 관측값을 먼저 믿고, 없으면 카탈로그의 정의를 쓴다.
        /// </summary>
        private static CharmWorth ProjectGrowth(
            CharmWorth worth, CharmDefinition definition, PlacedItem item, ICatalog catalog, CharmValueBook values)
        {
            var goal = item.GrowthGoal > 0 ? item.GrowthGoal : definition.GrowthQuestGoal;
            var bucket = GrowthWorth.Bucket(item.GrowthProgress, goal);
            if (bucket <= 0 || definition.GrowthRewardEntityId == 0) return worth;

            var grown = catalog.Charm(definition.GrowthRewardEntityId);
            if (grown is null) return worth;

            return GrowthWorth.Project(
                worth, CharmWorth.Resolve(grown, values.Of(grown)), bucket,
                Math.Max(definition.MaxLevel, grown.MaxLevel));
        }

        public static Plan? Build(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences? preferences = null,
            Plan? previous = null) =>
            Build(snapshot, catalog, preferences, out _, previous);

        /// <param name="previous">
        /// 직전에 내놓은 계획. 동점 배치 사이에서 저번에 말한 쪽을 고르는 앵커로만 쓰이고,
        /// 점수가 실제로 나은 배치를 이기지는 못한다.
        /// </param>
        /// <param name="layouts">
        /// 계획 사이에 빔 탐색을 돌려 쓸 자리. <c>PlanRunner</c>가 들고 넘긴다.
        /// </param>
        /// <param name="cancellation">
        /// 이 계획이 이미 쓸모없어졌다는 신호. 켜지면 풀이가 그 자리에서
        /// <see cref="OperationCanceledException"/> 을 던지고, 여기서 그것을 <c>null</c> 로 바꾼다 -
        /// 중간까지 푼 것은 애초에 돌아오지 않는다. 폴링이 풀이보다 빠를 때 버릴 답을 끝까지
        /// 계산하지 않으려는 것이다.
        /// </param>
        public static Plan? Build(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences? preferences,
            out PlanBlocker blocker, Plan? previous = null, LayoutCache? layouts = null,
            CancellationToken cancellation = default)
        {
            layouts ??= new LayoutCache();
            var placement = BuildPlacement(
                snapshot, catalog, preferences, out blocker, previous, layouts, cancellation: cancellation);
            return placement is null ? null : BuildAdvice(placement, snapshot, catalog, preferences, layouts, cancellation);
        }

        /// <summary>
        /// 1단계. 배치와 그것을 설명하는 것까지만 만든다 - 조언 셋은 비어 있고
        /// <see cref="Plan.AdviceStatus"/> 가 그것이 "없음" 인지 "아직" 인지를 말한다.
        ///
        /// <b>이것이 사용자가 기다리는 시간이다.</b> 조언은 배치보다 몇 배 비싼데 배치와 한 덩어리로
        /// 묶여 있어서, 판이 조금만 커도 다 끝날 때까지 화면이 갱신되지 않았다. 게다가 세피라이트
        /// 창을 여닫기만 해도 조언 쪽 지문이 바뀌어 그 덩어리가 통째로 취소됐다.
        /// </summary>
        /// <param name="settled">
        /// 지금 놓여 있는 것이 이미 최선이라고 부르는 쪽이 아는 경우. 자동 배치가 방금 끝나
        /// 그 결과가 그대로 들어왔을 때가 그렇다(<see cref="AppliedPlacement"/>). 그때는 탐색을
        /// 건너뛰고 지금 배치를 채점해 그대로 쓴다 - 같은 문제를 다시 풀어 같은 답을 얻는 일이다.
        /// 채점·검증·경고는 그대로 도므로 게임이 우리 모델과 다르면 여전히 검증에서 걸린다.
        /// </param>
        public static Plan? BuildPlacement(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences? preferences,
            out PlanBlocker blocker, Plan? previous = null, LayoutCache? layouts = null,
            bool settled = false, CancellationToken cancellation = default)
        {
            try
            {
                return Attempt(snapshot, catalog, preferences, out blocker, previous, layouts, settled, cancellation);
            }
            catch (OperationCanceledException)
            {
                blocker = PlanBlocker.None;
                return null;
            }
        }

        /// <summary>
        /// 2단계. 1단계가 낸 배치에 합성·후보·제거 조언을 붙인 <b>새 계획</b>을 돌려준다.
        /// 넘긴 계획은 건드리지 않는다 - 이미 게시돼 화면이 읽고 있을 수 있다.
        ///
        /// 취소되면 <c>null</c> 이다. 그때 배치는 그대로 살아 있고 조언만 다시 풀면 된다.
        /// </summary>
        public static Plan? BuildAdvice(
            Plan placement, GameSnapshot snapshot, ICatalog catalog, PlanPreferences? preferences,
            LayoutCache? layouts = null, CancellationToken cancellation = default)
        {
            if (placement is null) throw new ArgumentNullException(nameof(placement));
            preferences ??= PlanPreferences.None;

            // 1단계가 만든 계획이 아니면 붙일 것이 없다. 실행기에 다른 조립기를 끼운 경우가 그렇다.
            if (placement.Work is null) return placement;
            if (!preferences.Recommendations)
            {
                return placement.AdviceStatus == AdviceStatus.NotRequested && placement.Offers.Count == 0 &&
                       placement.Mixes.Count == 0 && placement.Discards.Count == 0 && placement.SkippedOffers == 0
                    ? placement
                    : placement.WithAdvice(
                        new List<OfferAdvice>(), new List<MixAdvice>(), new List<DiscardAdvice>(),
                        0, AdviceStatus.NotRequested);
            }

            try
            {
                return Advise(placement, snapshot, catalog, preferences, layouts, cancellation);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static Plan Advise(
            Plan placement, GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
            LayoutCache? layouts, CancellationToken cancellation)
        {
            var problem = placement.Work!.Problem;
            var values = preferences.CharmValues;
            layouts ??= new LayoutCache();
            layouts.BeginPlan();

            var mixes = new List<MixAdvice>();

            // 합성기를 이미 썼으면 이 층에서는 더 권할 것이 없고, 멀리 있으면 아직 권할 때가
            // 아니다. 재지 않은(null) 자료는 전처럼 돈다.
            if (snapshot.Mixer is { Used: false } mixer && mixer.Near != false)
            {
                mixes = TabletMixAdvisor.Rank(
                    problem, catalog, mixer.Cost, snapshot.Run?.Gold ?? int.MaxValue,
                    layouts: layouts, cancellation: cancellation);
            }

            var candidates = Candidates(snapshot, catalog, placement.ExpectedWeaponId, out var skippedOffers);
            var offers = OfferAdvisor.Rank(
                problem, candidates, snapshot.Run?.Gold ?? int.MaxValue,
                snapshot.Inventory?.ComboCounts ?? new Dictionary<string, int>(), catalog.Combo,
                preferences.PriorityCategories, preferences.PresetCharms, values, layouts, cancellation);

            // 후보마다 이미 배치를 다 풀어 두었다. 그 결과를 버리지 않고 화면이 쓸 모양으로
            // 옮겨 두면, 증가분이라는 숫자 하나 대신 무엇이 어떻게 달라지는지 보여줄 수 있다.
            // 기준 배치는 조언이 이미 푼 것을 그대로 받는다 - 여기서 다시 풀지 않는다.
            if (offers.Count > 0)
                FillPreviews(offers, problem, layouts.Baseline(problem, SolverOptions.ForAdvice(cancellation)));

            var discards = placement.Verification.Passed
                ? DiscardAdvisor.Rank(problem, placement.Best, layouts, cancellation)
                : new List<DiscardAdvice>();

            return placement.WithAdvice(offers, mixes, discards, skippedOffers, AdviceStatus.Ready);
        }

        private static Plan? Attempt(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences? preferences,
            out PlanBlocker blocker, Plan? previous, LayoutCache? layouts, bool settled,
            CancellationToken cancellation)
        {
            blocker = PlanBlocker.None;
            preferences ??= PlanPreferences.None;

            // 기준 배치와 두 조언이 같은 탐색을 나눠 쓴다. 따로 풀면 같은 탐색을 두 번 돌리는
            // 셈이고, 그 한 번이 실측에서 백 밀리초대다. 부르는 쪽이 들고 있으면 그 나눠 쓰기가
            // 계획 사이까지 이어진다.
            layouts ??= new LayoutCache();
            layouts.BeginPlan();
            var values = preferences.CharmValues;
            var inventory = snapshot.Inventory;
            if (inventory is null || inventory.Storage <= 0)
            {
                blocker = PlanBlocker.NoInventory;
                return null;
            }

            var grid = new GridSpec(inventory.Width, inventory.Height, inventory.Storage);
            var problem = new PlacementProblem
            {
                Grid = grid,
                Scale = catalog.Scale,
                DeactivationAllowed = new HashSet<int>(preferences.DeactivationAllowed),
                PinnedCharms = new Dictionary<int, int>(preferences.PinnedCharms),
                RetainedCharms = new HashSet<int>(preferences.RetainedCharms),
            };

            // 빔 서치는 후보를 살펴보는 순서에 따라 같은 점수의 다른 배치를 내놓는다. 스냅샷의 순서는
            // 석판을 옮기면 바뀌므로, 여기서 한 번 고정해 두어야 제안이 흔들리지 않는다.
            var orderedTablets = inventory.Tablets
                .OrderBy(t => t.DefinitionId).ThenBy(t => t.InstanceId)
                .ToList();

            var layout = new List<TabletPlacement>();
            foreach (var tablet in orderedTablets)
            {
                var definition = catalog.Tablet(tablet.DefinitionId) ?? new TabletDefinition();
                var slot = new TabletSlot
                {
                    Definition = definition,
                    InstanceId = tablet.InstanceId,
                    InstanceQuery = tablet.Query,
                    InstanceConditionQuery = tablet.ConditionQuery,
                    InstanceName = tablet.Name,
                    Rotatable = tablet.IsRotatable ?? definition.IsRotatable,
                };
                problem.Tablets.Add(slot);
                layout.Add(slot.At(tablet.Position, tablet.Rotation));
                problem.CurrentTablets[slot.InstanceId] = new TabletSpot(tablet.Position, tablet.Rotation);
            }

            problem.FixedEffects.AddRange(inventory.FixedEffects);
            problem.ComboCounts = inventory.ComboCounts;
            problem.Combos = catalog.Combo;
            problem.PriorityCategories = new System.Collections.Generic.HashSet<string>(preferences.PriorityCategories);

            foreach (var engraving in inventory.Engravings)
            {
                var definition = catalog.Tablet(engraving.DefinitionId) ?? new TabletDefinition();
                problem.FixedTablets.Add(new TabletPlacement
                {
                    Definition = definition,
                    Position = engraving.Position,
                    Rotation = engraving.Rotation,
                    InstanceQuery = engraving.Query,
                    InstanceConditionQuery = engraving.ConditionQuery,
                    InstanceName = engraving.Name,
                });
            }

            // 무기 연동 아티팩트는 해당 무기를 들고 있어야 효과가 켜진다. 무기를 모르면 판정하지 않는다.
            var weapon = snapshot.Run?.WeaponId ?? "";

            var positions = new Dictionary<int, GridPos>();
            foreach (var item in inventory.Items
                         .OrderBy(i => i.DefinitionId).ThenBy(i => i.InstanceId))
            {
                if (!grid.Contains(item.Position)) continue;

                // 아티팩트가 아닌 아이템도 칸을 차지한다. 빼놓으면 솔버가 그 자리를 비어 있다고 본다.
                var definition = catalog.Charm(item.DefinitionId);
                var slot = new CharmSlot
                {
                    Definition = definition ?? new CharmDefinition(),
                    InstanceId = item.InstanceId,
                    IsAttackable = item.IsAttackable,
                    ObservedCategories = item.ObservedCategories,
                    Enchant = definition is null ? 0 : item.Enchant,
                    IsFiller = definition is null,
                    Immovable = item.Immovable,
                    IsDormant = definition is not null && WeaponMatch.IsDormant(definition, weapon),
                    Weight = definition is not null &&
                             preferences.PinnedCharms.TryGetValue(item.DefinitionId, out var pin)
                        ? PlanPreferences.WeightOf(pin)
                        : 1,
                    Held = definition is not null && preferences.HeldCharms.Contains(item.DefinitionId),
                    Retained = definition is not null && preferences.RetainedCharms.Contains(item.DefinitionId),
                    AllowDeactivation = definition is not null && preferences.DeactivationAllowed.Contains(item.DefinitionId),
                };
                if (definition is not null)
                {
                    var worth = CharmWorth.Resolve(definition, values.Of(definition));
                    slot.Worth = ProjectGrowth(worth, definition, item, catalog, values);
                }
                problem.Charms.Add(slot);
                positions[item.InstanceId] = item.Position;
                problem.CurrentCharms[item.InstanceId] = item.Position;
            }

            if (problem.Charms.All(charm => charm.IsFiller))
            {
                // 격자가 빈 것과, 물건은 있는데 우리가 하나도 못 알아본 것은 사람이 할 일이 다르다.
                // 뒤엣것은 대개 카탈로그가 낡아서이므로 다시 덤프하라고 일러야 한다.
                blocker = problem.Charms.Count > 0 ? PlanBlocker.UnknownItems : PlanBlocker.NoCharms;
                return null;
            }

            if (previous is not null)
            {
                foreach (var target in previous.Targets)
                {
                    if (target.IsTablet)
                        problem.PlannedTablets[target.InstanceId] = new TabletSpot(target.To, target.Rotation);
                    else
                        problem.PlannedCharms[target.InstanceId] = target.To;
                }
            }

            var current = PlacementSolver.Score(problem, layout, positions);
            var verification = Verify(inventory, current, grid);

            // 이미 최선인 것을 아는 경우(자동 배치 직후)에는 탐색을 건너뛴다. 답을 아는 문제를
            // 다시 푸는 일이고, 하필 그 한 번이 회전이 바뀐 탓에 빔 캐시를 못 쓰는 cold 다.
            var best = settled
                ? current
                : PlacementSolver.Solve(problem, new SolverOptions { Cancellation = cancellation }, layouts);

            // 조건부 배정은 수렴하지 않을 수 있으므로 현재 배치도 같은 우선순위로 비교한다.
            if (PriorityComboPlacement.Compare(best, current) < 0) best = current;

            var moves = Moves(problem, current, best, out var manualMovesAvailable);
            var unapprovedDeactivation = !ActivationPolicy.AllowsTransition(current, best);
            var targets = Targets(problem, best);
            var hasPlacementChanges = targets.Any(target =>
                target.From != target.To || target.IsTablet && target.FromRotation != target.Rotation);

            if (best.UnplacedTablets > 0 || !verification.Passed || best.UnretainedCharms.Count > 0 || best.WrongSideCharms.Count > 0 || unapprovedDeactivation) targets.Clear();
            if (best.UnretainedCharms.Count > 0 || best.WrongSideCharms.Count > 0 || unapprovedDeactivation)
            {
                moves.Clear();
                manualMovesAvailable = false;
            }

            return new Plan
            {
                Verification = verification,
                Current = current,
                Best = best,
                Moves = moves,
                ComboPlacementWarnings = ComboPlacementWarnings(problem, best),
                RetentionWarnings = problem.Charms.Where(charm => best.UnretainedCharms.Contains(charm.InstanceId))
                    .Select(charm => RetentionWarning(charm, best)).ToList(),
                SupportWarnings = problem.Charms.Where(charm => best.UnlinkedCharms.Contains(charm.InstanceId) && !charm.Retained)
                    .Select(charm => Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트") + ": " +
                        Explain.SupportMissing(charm.Definition)).ToList(),
                HasUnapprovedDeactivation = unapprovedDeactivation,
                ActivationWarnings = problem.Charms.Where(charm => best.UnpreservedCharms.Contains(charm.InstanceId) &&
                        !charm.Retained && !best.UnlinkedCharms.Contains(charm.InstanceId))
                    .Select(charm => Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트") +
                        ": 활성 배치를 찾지 못했습니다. " +
                        (best.CharmPositions.TryGetValue(charm.InstanceId, out var cell) && best.InactiveCells.TryGetValue(cell, out var reason)
                            ? Explain.InactiveReason(reason) : "놓을 자리가 부족합니다.") +
                        (unapprovedDeactivation ? " 끄기 허용 없이 새로 비활성화하는 배치는 적용하지 않습니다." : ""))
                    .Concat(best.WrongSideCharms.Select(_ => "대립의 천칭: 현재 놓인 쪽을 유지하는 배치를 찾지 못했습니다. 직접 원하는 쪽으로 옮긴 뒤 다시 계산하세요."))
                    .Concat(DormantPreferences(problem)).ToList(),
                ManualMoveInstructionsAvailable = manualMovesAvailable,
                HasPlacementChanges = hasPlacementChanges,
                InventoryWidth = inventory.Width,
                InventoryHeight = inventory.Height,
                InventoryStorage = inventory.Storage,
                ExpectedWeaponId = weapon,
                AdviceStatus = preferences.Recommendations ? AdviceStatus.Pending : AdviceStatus.NotRequested,
                Work = new PlacementWork(problem),
                Names = NamesByCell(problem, best),
                Charms = CharmsByCell(problem, best),
                Targets = targets,
            };
        }

        private static string RetentionWarning(CharmSlot charm, Arrangement best)
        {
            var reason = best.CharmPositions.TryGetValue(charm.InstanceId, out var cell)
                ? best.InactiveCells.TryGetValue(cell, out var inactive) ? Explain.InactiveReason(inactive) : ""
                : "놓을 자리를 확보하지 못했습니다.";
            if (best.UnlinkedCharms.Contains(charm.InstanceId)) reason = Explain.SupportMissing(charm.Definition);
            return Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트") +
                ": 사용 유지 배치를 찾지 못했습니다. " + reason;
        }

        /// <summary>
        /// 연동 무기를 안 들어 꺼져 있는데 강화 우선을 지정한 아티팩트. 자리는 지정대로 잡아
        /// 주지만 지금 점수에는 한 푼도 들어가지 않으므로, 말하지 않으면 점수가 왜 그대로인지
        /// 알 수 없다. 무기 연동은 견고 쪽에 몰려 있어 그쪽을 쓰는 사람이 자주 만난다.
        /// </summary>
        internal static List<string> DormantPreferences(PlacementProblem problem) =>
            problem.Charms
                .Where(charm => charm.IsDormant && !charm.IsFiller && charm.Weight > 1)
                .Select(charm => Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트") + ": " +
                    "연동된 무기를 들고 있지 않아 꺼져 있습니다. 강화 우선(★) 지정대로 자리는 잡아 두었으니 " +
                    "무기를 바꾸면 그대로 켜집니다. 그때까지는 점수에 들어가지 않습니다.")
                .ToList();

        private static List<string> ComboPlacementWarnings(PlacementProblem problem, Arrangement best)
        {
            var warnings = new List<string>();
            foreach (var charm in problem.Charms)
            {
                if (!best.UnmatchedComboCharms.Contains(charm.InstanceId)) continue;
                var name = Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트");
                warnings.Add(name + ": " + PriorityComboPlacement.FailureReason(problem, charm));
            }
            return warnings;
        }

        /// <summary>
        /// 후보를 집었을 때의 격자를 채운다. 달라지는 칸도 함께 표시한다 - 미리보기의 요점은
        /// 배치 전체가 아니라 무엇이 바뀌는가이기 때문이다.
        ///
        /// 견주는 것은 <paramref name="baseline"/>, 곧 후보를 하나도 집지 않은 판을 후보와 <b>같은
        /// 강도로</b> 푼 배치다. 화면에 늘 떠 있는 <c>Best</c>는 더 촘촘한 탐색에서 나오는데,
        /// 두 탐색이 동점 배치를 다르게 고르면 후보 때문이 아닌 칸까지 금색으로 짚게 된다.
        /// </summary>
        private static void FillPreviews(
            List<OfferAdvice> offers, PlacementProblem problem, Arrangement baseline)
        {
            var baseNames = NamesByCell(problem, baseline);
            var baseTablets = new HashSet<(GridPos Position, int Rotation)>();
            foreach (var placement in baseline.Tablets)
                baseTablets.Add((placement.Position, placement.Rotation));

            foreach (var advice in offers)
            {
                if (advice.Trial is not { } trial || advice.Solved is not { } solved) continue;

                var preview = new PlanPreview
                {
                    Score = solved.Score,
                    Names = NamesByCell(trial, solved),
                    Charms = CharmsByCell(trial, solved),
                };
                preview.Tablets.AddRange(solved.Tablets);
                foreach (var pair in solved.Levels) preview.Levels[pair.Key] = pair.Value;
                foreach (var pair in solved.EffectiveLevels) preview.EffectiveLevels[pair.Key] = pair.Value;
                foreach (var pair in solved.InactiveCells) preview.InactiveCells[pair.Key] = pair.Value;

                foreach (var pair in preview.Names)
                {
                    if (!baseNames.TryGetValue(pair.Key, out var was) || was != pair.Value)
                        preview.Changed.Add(pair.Key);
                }
                foreach (var placement in solved.Tablets)
                {
                    if (!baseTablets.Contains((placement.Position, placement.Rotation)))
                        preview.Changed.Add(placement.Position);
                }

                advice.Preview = preview;
                advice.Trial = null;
                advice.Solved = null;
            }
        }

        /// <summary>자동 배치 명령에 실을 최종 배치. 걸음 순서는 살아 있는 상태를 아는 플러그인이 정한다.</summary>
        private static List<PlanTarget> Targets(PlacementProblem problem, Arrangement best)
        {
            var targets = new List<PlanTarget>();
            for (var i = 0; i < problem.Tablets.Count && i < best.Tablets.Count; i++)
            {
                targets.Add(new PlanTarget
                {
                    InstanceId = problem.Tablets[i].InstanceId,
                    From = problem.CurrentTablets[problem.Tablets[i].InstanceId].Position,
                    To = best.Tablets[i].Position,
                    IsTablet = true,
                    FromRotation = problem.CurrentTablets[problem.Tablets[i].InstanceId].Rotation,
                    Rotation = best.Tablets[i].Rotation,
                });
            }

            foreach (var charm in problem.Charms)
            {
                if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var position)) continue;
                targets.Add(new PlanTarget
                {
                    InstanceId = charm.InstanceId,
                    From = problem.CurrentCharms[charm.InstanceId],
                    To = position,
                    Immovable = charm.Immovable,
                });
            }
            return targets;
        }

        private static Dictionary<GridPos, string> NamesByCell(PlacementProblem problem, Arrangement best)
        {
            var names = new Dictionary<GridPos, string>();
            foreach (var charm in problem.Charms)
            {
                if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var position)) continue;
                names[position] = Naming.Of(
                    charm.Definition.Names, charm.Definition.Id, charm.IsFiller ? "아이템" : "아티팩트");
            }
            return names;
        }

        /// <summary>격자 칸에 ★ 를 그리고 쪽지를 띄우려면 그 칸의 아티팩트가 무엇인지 알아야 한다.</summary>
        private static Dictionary<GridPos, int> CharmsByCell(PlacementProblem problem, Arrangement best)
        {
            var cells = new Dictionary<GridPos, int>();
            foreach (var charm in problem.Charms)
            {
                if (charm.IsFiller || charm.Definition.EntityId == 0) continue;
                if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var position)) continue;
                cells[position] = charm.Definition.EntityId;
            }
            return cells;
        }

        /// <summary>후보가 많으면 한 번에 다 풀기에는 무거워, 종류가 같은 것은 하나로 묶고 수를 제한한다.</summary>
        private const int MaxCandidates = 8;

        private static List<OfferCandidate> Candidates(
            GameSnapshot snapshot, ICatalog catalog, string weapon, out int skipped)
        {
            var candidates = new List<OfferCandidate>();
            var seen = new HashSet<int>();
            skipped = 0;

            foreach (var offer in snapshot.Offers
                         .OrderBy(value => value.DefinitionId)
                         .ThenBy(value => value.Kind)
                         .ThenBy(value => value.Price)
                         .ThenBy(value => value.SlotIndex))
            {
                if (!seen.Add(offer.DefinitionId)) continue;

                var charm = offer.Kind == "charm" ? catalog.Charm(offer.DefinitionId) : null;
                var tablet = offer.Kind == "tablet" ? catalog.Tablet(offer.DefinitionId) : null;
                if (charm is null && tablet is null) continue;

                // 상한은 평가할 수 있는 것에만 건다. 포션처럼 애초에 평가 대상이 아닌 물건을
                // 먼저 세면, 상점에 포션이 늘어선 것만으로 "몇 개는 평가하지 못했습니다"라고
                // 알리게 된다. 실제로 빠진 것이 없는데 경고만 뜨는 셈이다.
                if (candidates.Count >= MaxCandidates)
                {
                    skipped++;
                    continue;
                }

                candidates.Add(new OfferCandidate
                {
                    DefinitionId = offer.DefinitionId,
                    Kind = offer.Kind,
                    Name = Naming.Of(charm?.Names ?? tablet!.Names, charm?.Id ?? tablet!.Id, "?"),
                    Price = offer.Price,
                    Charm = charm,
                    Tablet = tablet,
                    CharmIsDormant = charm is not null && WeaponMatch.IsDormant(charm, weapon),
                });
            }
            return candidates;
        }

        private static PlanVerification Verify(InventoryState inventory, Arrangement current, GridSpec grid)
        {
            if (inventory.LevelMatrix is null || inventory.DisabledCells is null)
            {
                return new PlanVerification
                {
                    Status = PlanVerificationStatus.Unavailable,
                    Reason = "게임의 레벨 또는 비활성 행렬을 읽지 못해 계획을 검증할 수 없습니다.",
                };
            }

            var levelMismatches = 0;
            foreach (var pair in current.CellLevels)
            {
                var key = pair.Key.X + "," + pair.Key.Y;
                inventory.LevelMatrix.TryGetValue(key, out var reported);
                if (reported != pair.Value) levelMismatches++;
            }

            var reportedDisabled = new HashSet<GridPos>();
            foreach (var key in inventory.DisabledCells)
            {
                if (!TryParseCell(key, out var cell))
                {
                    return new PlanVerification
                    {
                        Status = PlanVerificationStatus.Failed,
                        LevelMismatches = levelMismatches,
                        DisabledMismatches = 1,
                        Reason = "게임의 비활성 칸 좌표를 해석할 수 없어 자동 배치를 잠갔습니다.",
                    };
                }
                if (grid.Contains(cell)) reportedDisabled.Add(cell);
            }

            var disabledMismatches = 0;
            for (var index = 0; index < grid.Storage; index++)
            {
                var cell = grid.ToPosition(index);
                if (reportedDisabled.Contains(cell) != current.DisabledCells.Contains(cell))
                    disabledMismatches++;
            }

            var effectiveLevelMismatches = 0;
            var itemDisabledMismatches = 0;
            foreach (var item in inventory.Items)
            {
                if (!grid.Contains(item.Position)) continue;
                if (!current.Levels.TryGetValue(item.Position, out var level) || level != item.EffectiveLevel)
                    effectiveLevelMismatches++;

                var expectedActive = !current.DisabledCells.Contains(item.Position);
                if (item.IsActive != expectedActive) itemDisabledMismatches++;
            }
            disabledMismatches += itemDisabledMismatches;

            var tabletMismatches = 0;
            foreach (var tablet in inventory.Tablets)
            {
                if (!current.AppliedTablets.TryGetValue(tablet.InstanceId, out var applied) ||
                    applied != tablet.IsApplied)
                    tabletMismatches++;
            }

            var total = levelMismatches + effectiveLevelMismatches + disabledMismatches + tabletMismatches;
            return new PlanVerification
            {
                Status = total == 0 ? PlanVerificationStatus.Passed : PlanVerificationStatus.Failed,
                LevelMismatches = levelMismatches,
                EffectiveLevelMismatches = effectiveLevelMismatches,
                DisabledMismatches = disabledMismatches,
                TabletMismatches = tabletMismatches,
                Reason = total == 0
                    ? "게임 상태와 계획 시뮬레이션이 일치합니다."
                    : $"게임 상태와 계산 결과의 검증 항목 {total}개가 달라 자동 배치를 잠갔습니다.",
            };
        }

        private static bool TryParseCell(string key, out GridPos cell)
        {
            cell = default;
            if (key is null) return false;

            var comma = key.IndexOf(',');
            if (comma <= 0 || comma >= key.Length - 1 ||
                !int.TryParse(key.AsSpan(0, comma), out var x) ||
                !int.TryParse(key.AsSpan(comma + 1), out var y))
                return false;

            cell = new GridPos(x, y);
            return true;
        }

        /// <summary>
        /// 무엇을 어디로 옮길지, 그리고 그것을 실제로 따라 할 수 있는 순서로 세운다.
        /// 순서를 정하는 일은 <see cref="MoveOrder"/>가 맡는다.
        /// </summary>
        private static List<Move> Moves(
            PlacementProblem problem, Arrangement current, Arrangement best, out bool complete)
        {
            var pending = new List<Relocation>();
            var stationary = new List<GridPos>();

            for (var i = 0; i < problem.Tablets.Count && i < best.Tablets.Count && i < current.Tablets.Count; i++)
            {
                var from = current.Tablets[i];
                var to = best.Tablets[i];

                if (from.Position == to.Position && from.Rotation == to.Rotation)
                {
                    stationary.Add(from.Position);
                    continue;
                }

                pending.Add(new Relocation
                {
                    Name = Naming.OfTablet(to),
                    From = from.Position,
                    To = to.Position,
                    FromRotation = from.Rotation,
                    ToRotation = to.Rotation,
                });
            }

            foreach (var charm in problem.Charms)
            {
                if (!current.CharmPositions.TryGetValue(charm.InstanceId, out var from)) continue;
                if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var to)) continue;

                if (from == to)
                {
                    stationary.Add(from);
                    continue;
                }

                pending.Add(new Relocation
                {
                    Name = Naming.Of(
                        charm.Definition.Names, charm.Definition.Id, charm.IsFiller ? "아이템" : "아티팩트"),
                    From = from,
                    To = to,
                });
            }

            return MoveOrder.Sequence(problem.Grid, pending, stationary, out complete);
        }
    }
}
