using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FH6AutomationShared;

namespace FH6SkillPointOcr
{
    internal sealed class VirtualListSimulationProgram
    {
        private const int Rows = 3;
        private const int VisibleColumns = 4;
        private const int MaxSteps = 2000;

        public static int Main(string[] args)
        {
            SimulationSuite suite = new SimulationSuite(Rows, VisibleColumns, MaxSteps);
            List<SimulationFinding> findings = suite.RunAll();

            foreach (string line in suite.Summaries)
            {
                Console.WriteLine(line);
            }

            foreach (SimulationFinding finding in findings)
            {
                Console.WriteLine();
                Console.WriteLine("[" + finding.Severity + "] " + finding.Title);
                Console.WriteLine(finding.Detail);
            }

            Console.WriteLine();
            Console.WriteLine("findings=" + findings.Count);
            return findings.Count == 0 ? 0 : 2;
        }

        private sealed class SimulationSuite
        {
            private readonly int rows;
            private readonly int visibleColumns;
            private readonly int maxSteps;
            private readonly List<string> summaries = new List<string>();

            public SimulationSuite(int rows, int visibleColumns, int maxSteps)
            {
                this.rows = rows;
                this.visibleColumns = visibleColumns;
                this.maxSteps = maxSteps;
            }

            public List<string> Summaries
            {
                get { return summaries; }
            }

            public List<SimulationFinding> RunAll()
            {
                List<SimulationFinding> findings = new List<SimulationFinding>();
                List<ScenarioDefinition> scenarios = new List<ScenarioDefinition>();

                scenarios.Add(new ScenarioDefinition(
                    "两轮完整流程：干扰车 + 目标连续段 + 买车追加到锚定车型后",
                    BuildBaselineWithInterference,
                    999,
                    2,
                    true,
                    true,
                    true));

                scenarios.Add(new ScenarioDefinition(
                    "技术点中途不够：第一轮只点一辆，刷到 999 后第二轮继续",
                    BuildTwoValidNewCars,
                    32,
                    2,
                    false,
                    true,
                    false));

                scenarios.Add(new ScenarioDefinition(
                    "开局当前可见页面没有 2/3/4，目标在后方",
                    BuildLateTargetAfterLongSubaruRun,
                    999,
                    1,
                    false,
                    true,
                    true));

                scenarios.Add(new ScenarioDefinition(
                    "没有目标车型：普通斯巴鲁后滚到跨制造商 0 后停止",
                    BuildNoTargetModel,
                    999,
                    1,
                    false,
                    true,
                    false));

                scenarios.Add(new ScenarioDefinition(
                    "开局技术点为 0：有 3 也不能点，不能误删或误滚成完成",
                    BuildVisibleValidNewCars,
                    0,
                    1,
                    false,
                    false,
                    false));

                scenarios.Add(new ScenarioDefinition(
                    "删车位移：连续 4 被删除后列表自动前移，后方 4 不能漏",
                    BuildDeleteShiftStress,
                    999,
                    1,
                    false,
                    true,
                    false));

                foreach (ScenarioDefinition scenario in scenarios)
                {
                    ScenarioRunner runner = new ScenarioRunner(rows, visibleColumns, maxSteps, scenario);
                    List<SimulationFinding> scenarioFindings = runner.Run();
                    findings.AddRange(scenarioFindings);
                    summaries.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "[SCENARIO] {0}: {1}",
                        scenario.Name,
                        scenarioFindings.Count == 0 ? "PASS" : "FAIL findings=" + scenarioFindings.Count));
                }

                List<SimulationFinding> boundaryFindings = RunDirectBoundaryChecks();
                findings.AddRange(boundaryFindings);
                summaries.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[SCENARIO] 建表边界：空格不能作为 0/1 边界，0/1 才能结束: {0}",
                    boundaryFindings.Count == 0 ? "PASS" : "FAIL findings=" + boundaryFindings.Count));

                List<SimulationFinding> observedOffsetFindings = RunObservedOffsetChecks();
                findings.AddRange(observedOffsetFindings);
                summaries.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[SCENARIO] 选择偏移：优先回到 OCR 实际观测 offset，不用未验证右对齐外推: {0}",
                    observedOffsetFindings.Count == 0 ? "PASS" : "FAIL findings=" + observedOffsetFindings.Count));

                List<SimulationFinding> randomizedFindings = RunRandomizedPropertyChecks();
                findings.AddRange(randomizedFindings);
                summaries.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[SCENARIO] 随机属性推演：定表正确后，点技能/删车/买车/找 900 分开车候选跨轮保持虚拟表一致: {0}",
                    randomizedFindings.Count == 0 ? "PASS" : "FAIL findings=" + randomizedFindings.Count));

                return findings;
            }

            private List<SimulationFinding> RunRandomizedPropertyChecks()
            {
                List<SimulationFinding> findings = new List<SimulationFinding>();
                for (int seed = 1; seed <= 120; seed++)
                {
                    int capturedSeed = seed;
                    ScenarioDefinition scenario = new ScenarioDefinition(
                        "随机属性 seed " + capturedSeed.ToString(CultureInfo.InvariantCulture),
                        delegate { return BuildRandomActual(capturedSeed); },
                        capturedSeed % 7 == 0 ? 32 : FH6AutomationConstants.SkillPoints.Max,
                        3,
                        true,
                        true,
                        true);
                    scenario.PersistentVirtualTable = true;

                    ScenarioRunner runner = new ScenarioRunner(rows, visibleColumns, maxSteps, scenario);
                    List<SimulationFinding> runFindings = runner.Run();
                    findings.AddRange(runFindings);
                    if (findings.Count > 0) return findings;
                }

                return findings;
            }

            private List<SimulationFinding> RunDirectBoundaryChecks()
            {
                List<SimulationFinding> findings = new List<SimulationFinding>();
                string tempDir = Path.Combine(Path.GetTempPath(), "fh6-virtual-list-sim");
                Directory.CreateDirectory(tempDir);

                string blankSnapshot = Path.Combine(tempDir, "table-boundary-blank.json");
                if (File.Exists(blankSnapshot)) File.Delete(blankSnapshot);
                VirtualVehicleList blankList = new VirtualVehicleList(rows, null, blankSnapshot, VirtualListLoadMode.None);
                HashSet<CellKey> target = new HashSet<CellKey>();
                target.Add(new CellKey(0, 1));
                HashSet<CellKey> blank = new HashSet<CellKey>();
                blank.Add(new CellKey(1, 1));
                blankList.ApplyFullObservation(
                    visibleColumns,
                    target,
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new Dictionary<CellKey, int>(),
                    blank);

                CellKey lastTarget;
                CellKey nextCell;
                if (blankList.IsTargetModelBoundaryReached(out lastTarget, out nextCell))
                {
                    findings.Add(new SimulationFinding
                    {
                        Severity = "bug",
                        Title = "建表边界错误：-1 空格被当作 0/1 边界",
                        Detail = "target=col1/row0, next=col1/row1 is blank; boundary must be false."
                    });
                }

                string subaruSnapshot = Path.Combine(tempDir, "table-boundary-subaru.json");
                if (File.Exists(subaruSnapshot)) File.Delete(subaruSnapshot);
                VirtualVehicleList subaruList = new VirtualVehicleList(rows, null, subaruSnapshot, VirtualListLoadMode.None);
                HashSet<CellKey> manufacturer = new HashSet<CellKey>();
                manufacturer.Add(new CellKey(1, 1));
                subaruList.ApplyFullObservation(
                    visibleColumns,
                    target,
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    manufacturer,
                    new Dictionary<CellKey, int>(),
                    new HashSet<CellKey>());

                if (!subaruList.IsTargetModelBoundaryReached(out lastTarget, out nextCell))
                {
                    findings.Add(new SimulationFinding
                    {
                        Severity = "bug",
                        Title = "建表边界错误：目标段后的 1 没有被当作边界",
                        Detail = "target=col1/row0, next=col1/row1 is Subaru non-target; boundary must be true."
                    });
                }

                return findings;
            }

            private List<SimulationFinding> RunObservedOffsetChecks()
            {
                List<SimulationFinding> findings = new List<SimulationFinding>();
                string tempDir = Path.Combine(Path.GetTempPath(), "fh6-virtual-list-sim");
                Directory.CreateDirectory(tempDir);

                string snapshot = Path.Combine(tempDir, "observed-offset-preference.json");
                if (File.Exists(snapshot)) File.Delete(snapshot);
                VirtualVehicleList list = new VirtualVehicleList(rows, null, snapshot, VirtualListLoadMode.None);

                list.ScrollDown(6);
                HashSet<CellKey> target = new HashSet<CellKey>();
                target.Add(new CellKey(1, 1));
                HashSet<CellKey> validNew = new HashSet<CellKey>();
                validNew.Add(new CellKey(1, 1));
                HashSet<CellKey> manufacturer = new HashSet<CellKey>();
                manufacturer.Add(new CellKey(1, 1));
                list.ApplyFullObservation(
                    visibleColumns,
                    target,
                    validNew,
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    new HashSet<CellKey>(),
                    manufacturer,
                    new Dictionary<CellKey, int>(),
                    new HashSet<CellKey>());

                list.ResetView();
                CellKey local;
                int offset;
                if (!list.TryGetPendingValidNewTargetAtOrAfterCurrent(visibleColumns, out local, out offset))
                {
                    findings.Add(new SimulationFinding
                    {
                        Severity = "bug",
                        Title = "选择偏移错误：已知状态 3 没有被规划出来",
                        Detail = "expected global col7,row1 observed at offset6 local col1."
                    });
                    return findings;
                }

                if (offset != 6 || local.Col != 1 || local.Row != 1)
                {
                    findings.Add(new SimulationFinding
                    {
                        Severity = "bug",
                        Title = "选择偏移错误：规划使用了未验证的右对齐外推",
                        Detail = string.Format(
                            CultureInfo.InvariantCulture,
                            "expected offset=6 local=col1,row1; actual offset={0} local=col{1},row{2}.",
                            offset,
                            local.Col,
                            local.Row)
                    });
                }

                if (offset > list.CurrentOffset)
                {
                    list.ScrollDown(offset - list.CurrentOffset);
                }

                list.KeyboardMoveViewRight(local.Col, "direct observed-offset selection check");
                CellKey selectedLocal = new CellKey(local.Row, 0);
                CellKey selectedGlobal = list.ToGlobal(selectedLocal);
                if (list.CurrentOffset != 7 || selectedGlobal.Col != 7 || selectedGlobal.Row != 1)
                {
                    findings.Add(new SimulationFinding
                    {
                        Severity = "bug",
                        Title = "键盘选择同步错误：Right 后虚拟 offset 没有跟实际滚动一致",
                        Detail = string.Format(
                            CultureInfo.InvariantCulture,
                            "expected offset=7 selected global col7,row1; actual offset={0} global=col{1},row{2}.",
                            list.CurrentOffset,
                            selectedGlobal.Col,
                            selectedGlobal.Row)
                    });
                }

                return findings;
            }

            private static ActualVehicleList BuildBaselineWithInterference()
            {
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());
                list.Add(ActualVehicle.GenericSubaru("front-noise"));
                list.Add(ActualVehicle.AnchorBeforeTarget());
                list.Add(ActualVehicle.TargetWithScore(917));
                list.Add(ActualVehicle.TargetWithScore(900));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Target));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Target));
                list.Add(ActualVehicle.GenericSubaru("after-target"));
                list.Add(ActualVehicle.GenericSubaru("after-target"));
                list.Add(ActualVehicle.OtherManufacturer());
                list.Add(ActualVehicle.OtherManufacturer());
                return list;
            }

            private static ActualVehicleList BuildTwoValidNewCars()
            {
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());
                list.Add(ActualVehicle.AnchorBeforeTarget());
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.GenericSubaru("after-target"));
                list.Add(ActualVehicle.OtherManufacturer());
                return list;
            }

            private static ActualVehicleList BuildLateTargetAfterLongSubaruRun()
            {
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());
                for (int i = 0; i < 13; i++)
                {
                    list.Add(ActualVehicle.GenericSubaru("front-noise-" + i.ToString(CultureInfo.InvariantCulture)));
                }
                list.Add(ActualVehicle.AnchorBeforeTarget());
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Target));
                list.Add(ActualVehicle.GenericSubaru("after-target"));
                list.Add(ActualVehicle.OtherManufacturer());
                return list;
            }

            private static ActualVehicleList BuildNoTargetModel()
            {
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());
                list.Add(ActualVehicle.GenericSubaru("front-noise"));
                list.Add(ActualVehicle.AnchorBeforeTarget());
                for (int i = 0; i < 8; i++)
                {
                    list.Add(ActualVehicle.GenericSubaru("subaru-not-target-" + i.ToString(CultureInfo.InvariantCulture)));
                }
                list.Add(ActualVehicle.OtherManufacturer());
                list.Add(ActualVehicle.OtherManufacturer());
                return list;
            }

            private static ActualVehicleList BuildVisibleValidNewCars()
            {
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());
                list.Add(ActualVehicle.AnchorBeforeTarget());
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                list.Add(ActualVehicle.GenericSubaru("after-target"));
                list.Add(ActualVehicle.OtherManufacturer());
                return list;
            }

            private static ActualVehicleList BuildDeleteShiftStress()
            {
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());
                list.Add(ActualVehicle.AnchorBeforeTarget());
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Deletable));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Deletable));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Deletable));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Target));
                list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.Deletable));
                list.Add(ActualVehicle.GenericSubaru("after-target"));
                list.Add(ActualVehicle.OtherManufacturer());
                return list;
            }

            private static ActualVehicleList BuildRandomActual(int seed)
            {
                Random random = new Random(seed);
                ActualVehicleList list = new ActualVehicleList();
                list.Add(ActualVehicle.Placeholder());

                int frontNoise = random.Next(0, 10);
                for (int i = 0; i < frontNoise; i++)
                {
                    list.Add(ActualVehicle.GenericSubaru("random-front-" + seed.ToString(CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture)));
                }

                list.Add(ActualVehicle.AnchorBeforeTarget());

                int targetCount = random.Next(0, 24);
                bool hasDrive900Candidate = false;
                bool hasValidNew = false;
                for (int i = 0; i < targetCount; i++)
                {
                    int pick = random.Next(0, 100);
                    int state;
                    if (pick < 20)
                    {
                        state = FH6AutomationConstants.VehicleState.Target;
                    }
                    else if (pick < 55)
                    {
                        state = FH6AutomationConstants.VehicleState.ValidNew;
                        hasValidNew = true;
                    }
                    else if (pick < 85)
                    {
                        state = FH6AutomationConstants.VehicleState.Deletable;
                    }
                    else
                    {
                        int score = 650 + random.Next(0, 300);
                        list.Add(ActualVehicle.TargetWithScore(score));
                        if (score == 900) hasDrive900Candidate = true;
                        continue;
                    }
                    list.Add(ActualVehicle.Target(state));
                }

                if (targetCount == 0 && seed % 3 != 0)
                {
                    list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                    hasValidNew = true;
                }
                if (!hasDrive900Candidate)
                {
                    list.Add(ActualVehicle.TargetWithScore(900));
                }
                if (!hasValidNew && seed % 5 == 0)
                {
                    list.Add(ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                }

                int tailNoise = random.Next(0, 6);
                for (int i = 0; i < tailNoise; i++)
                {
                    list.Add(ActualVehicle.GenericSubaru("random-tail-" + seed.ToString(CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture)));
                }

                int otherCount = random.Next(1, 5);
                for (int i = 0; i < otherCount; i++)
                {
                    list.Add(ActualVehicle.OtherManufacturer());
                }

                return list;
            }
        }

        private sealed class ScenarioDefinition
        {
            public readonly string Name;
            public readonly Func<ActualVehicleList> BuildActual;
            public readonly int InitialSkillPoints;
            public readonly int Rounds;
            public readonly bool BuyAfterRound;
            public readonly bool RunDelete;
            public readonly bool RunDrive;
            public bool PersistentVirtualTable;

            public ScenarioDefinition(
                string name,
                Func<ActualVehicleList> buildActual,
                int initialSkillPoints,
                int rounds,
                bool buyAfterRound,
                bool runDelete,
                bool runDrive)
            {
                Name = name;
                BuildActual = buildActual;
                InitialSkillPoints = initialSkillPoints;
                Rounds = rounds;
                BuyAfterRound = buyAfterRound;
                RunDelete = runDelete;
                RunDrive = runDrive;
                PersistentVirtualTable = true;
            }
        }

        private enum ActionKind
        {
            Select,
            Scroll,
            Observe,
            Stop,
            UseDefault
        }

        private sealed class Decision
        {
            public ActionKind Kind;
            public CellKey Target;
            public int ScrollTicks;
            public string Reason;

            public static Decision Select(CellKey target, string reason)
            {
                return new Decision { Kind = ActionKind.Select, Target = target, Reason = reason };
            }

            public static Decision Scroll(int ticks, string reason)
            {
                return new Decision { Kind = ActionKind.Scroll, ScrollTicks = ticks, Reason = reason };
            }

            public static Decision Observe(string reason)
            {
                return new Decision { Kind = ActionKind.Observe, Reason = reason };
            }

            public static Decision Stop(string reason)
            {
                return new Decision { Kind = ActionKind.Stop, Reason = reason };
            }

            public static Decision UseDefault(string reason)
            {
                return new Decision { Kind = ActionKind.UseDefault, Reason = reason };
            }
        }

        private sealed class ScenarioRunner
        {
            private readonly int rows;
            private readonly int visibleColumns;
            private readonly int maxSteps;
            private readonly ScenarioDefinition scenario;
            private readonly List<string> trace = new List<string>();
            private readonly ActualVehicleList actual;
            private VirtualVehicleList vehicleList;
            private int skillPoints;
            private bool subaruBoundaryReached;
            private string subaruBoundaryReason = "";

            public ScenarioRunner(int rows, int visibleColumns, int maxSteps, ScenarioDefinition scenario)
            {
                this.rows = rows;
                this.visibleColumns = visibleColumns;
                this.maxSteps = maxSteps;
                this.scenario = scenario;
                actual = scenario.BuildActual();
                skillPoints = scenario.InitialSkillPoints;
            }

            public List<SimulationFinding> Run()
            {
                List<SimulationFinding> findings = new List<SimulationFinding>();

                if (scenario.PersistentVirtualTable)
                {
                    StartFreshVirtualList(0);
                    BuildExactVirtualTableFromActual();
                    findings.AddRange(AssertKnownVirtualTableMatchesActual("initial exact table"));
                    if (findings.Count > 0) return findings;
                }

                for (int round = 1; round <= scenario.Rounds; round++)
                {
                    if (!scenario.PersistentVirtualTable)
                    {
                        StartFreshVirtualList(round);
                    }
                    else
                    {
                        vehicleList.ResetView();
                    }
                    trace.Add("round=" + round + " start skillPoints=" + skillPoints + " actual=" + actual.DebugString());

                    RunSkillPhase(round, findings);

                    if (scenario.RunDelete)
                    {
                        vehicleList.ResetView();
                        trace.Add("round=" + round + " handoff skill->delete reset view to offset=0");
                        RunDeletePhase(round, findings);
                    }

                    if (scenario.RunDrive)
                    {
                        vehicleList.ResetView();
                        trace.Add("round=" + round + " handoff delete->drive reset view to offset=0");
                        RunDrivePhase(round, findings);
                    }

                    if (round < scenario.Rounds)
                    {
                        if (scenario.BuyAfterRound)
                        {
                            int added = actual.TopUpValidNewTargetCars(31);
                            if (scenario.PersistentVirtualTable) vehicleList.AppendPurchasedValidNewVehicles(added);
                            trace.Add("round=" + round + " buy-top-up added=" + added + " actual=" + actual.DebugString());
                            if (scenario.PersistentVirtualTable)
                            {
                                findings.AddRange(AssertKnownVirtualTableMatchesActual("round " + round.ToString(CultureInfo.InvariantCulture) + " after buy append"));
                                if (findings.Count > 0) return findings;
                            }
                        }

                        skillPoints = FH6AutomationConstants.SkillPoints.Max;
                        trace.Add("round=" + round + " minute-loop skillPoints=999");
                    }
                }

                return findings;
            }

            private void StartFreshVirtualList(int round)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "fh6-virtual-list-sim");
                Directory.CreateDirectory(tempDir);
                string safeName = MakeSafeFileName(scenario.Name);
                string snapshot = Path.Combine(tempDir, safeName + "-round-" + round.ToString(CultureInfo.InvariantCulture) + ".json");
                if (File.Exists(snapshot)) File.Delete(snapshot);
                vehicleList = new VirtualVehicleList(rows, null, snapshot, VirtualListLoadMode.None);
                subaruBoundaryReached = false;
                subaruBoundaryReason = "";
            }

            private void BuildExactVirtualTableFromActual()
            {
                vehicleList.ResetView();
                vehicleList.SetPreviousManufacturerColumnVisibleForTableBuild("sim exact table build initial left");
                int scrollStep = Math.Max(1, visibleColumns - 1);
                int maxObservations = Math.Max(1, actual.ColumnCount(rows) + visibleColumns + 3);
                for (int i = 0; i < maxObservations; i++)
                {
                    ApplyFakeOcr(false, true, i == 0 ? 1 : 0);
                    CellKey lastTarget;
                    CellKey nextCell;
                    if (vehicleList.IsTargetModelBoundaryReached(out lastTarget, out nextCell))
                    {
                        vehicleList.ResetView();
                        trace.Add("exact-table built actual=" + actual.DebugString());
                        return;
                    }

                    vehicleList.KeyboardMoveViewRight(scrollStep, "exact table build aggressive scroll");
                }
                vehicleList.ResetView();
                trace.Add("exact-table built actual=" + actual.DebugString());
            }

            private void MoveVirtualViewToOffset(int targetOffset, string reason)
            {
                int delta = targetOffset - vehicleList.CurrentOffset;
                if (delta > 0) vehicleList.KeyboardMoveViewRight(delta, reason);
                else if (delta < 0) vehicleList.KeyboardMoveViewLeft(-delta, reason);
            }

            private void RunSkillPhase(int round, List<SimulationFinding> findings)
            {
                for (int step = 0; step < maxSteps; step++)
                {
                    if (skillPoints < FH6AutomationConstants.SkillPoints.PerVehicle)
                    {
                        trace.Add("skill round=" + round + " stop insufficient skillPoints=" + skillPoints);
                        return;
                    }

                    Decision decision = PlanSkill();
                    trace.Add("skill round=" + round + " step=" + step + " offset=" + vehicleList.CurrentOffset + " action=" + decision.Kind + " " + decision.Reason);

                    if (decision.Kind == ActionKind.Observe)
                    {
                        ApplyFakeOcr(true);
                        continue;
                    }

                    if (decision.Kind == ActionKind.Scroll)
                    {
                        vehicleList.ScrollDown(decision.ScrollTicks);
                        continue;
                    }

                    if (decision.Kind == ActionKind.Stop)
                    {
                        if (actual.HasState(FH6AutomationConstants.VehicleState.ValidNew))
                        {
                            findings.Add(Finding(
                                "bug",
                                scenario.Name + "：技能点阶段停止时实际列表仍有 3",
                                "round=" + round + "\r\nactual=" + actual.DebugString() + "\r\n" + TraceTail()));
                        }
                        return;
                    }

                    int actualState = actual.StateAtGlobal(decision.Target, rows);
                    if (actualState != FH6AutomationConstants.VehicleState.ValidNew)
                    {
                        findings.Add(Finding(
                            "error",
                            scenario.Name + "：技能点阶段选中了非 3 实际车辆",
                            "round=" + round + ", target=" + FormatCell(decision.Target) + ", actual=" + actualState + "\r\n" + TraceTail()));
                        return;
                    }

                    CellKey selectedLocal = SimulateKeyboardSelection(decision.Target, "skill");
                    actual.SetStateAt(vehicleList.CurrentOffset, selectedLocal, rows, FH6AutomationConstants.VehicleState.Deletable);
                    vehicleList.MarkProcessed(selectedLocal);
                    skillPoints -= FH6AutomationConstants.SkillPoints.PerVehicle;
                    trace.Add("skill round=" + round + " processed target=" + FormatCell(decision.Target) + " selected=" + FormatCell(selectedLocal) + " offset=" + vehicleList.CurrentOffset + " skillPoints=" + skillPoints);
                    findings.AddRange(AssertKnownVirtualTableMatchesActual("skill round " + round.ToString(CultureInfo.InvariantCulture) + " after process"));
                    if (findings.Count > 0) return;
                }

                findings.Add(Finding("error", scenario.Name + "：技能点阶段超过最大推演步数", TraceTail()));
            }

            private void RunDeletePhase(int round, List<SimulationFinding> findings)
            {
                for (int step = 0; step < maxSteps; step++)
                {
                    Decision decision = PlanDelete();
                    trace.Add("delete round=" + round + " step=" + step + " offset=" + vehicleList.CurrentOffset + " action=" + decision.Kind + " " + decision.Reason);

                    if (decision.Kind == ActionKind.Observe)
                    {
                        ApplyFakeOcr(true);
                        continue;
                    }

                    if (decision.Kind == ActionKind.Scroll)
                    {
                        vehicleList.ScrollDown(decision.ScrollTicks);
                        continue;
                    }

                    if (decision.Kind == ActionKind.Stop)
                    {
                        if (actual.HasState(FH6AutomationConstants.VehicleState.Deletable))
                        {
                            findings.Add(Finding(
                                "bug",
                                scenario.Name + "：删车阶段停止时实际列表仍有 4",
                                "round=" + round + "\r\nactual=" + actual.DebugString() + "\r\n" + TraceTail()));
                        }
                        return;
                    }

                    int actualState = actual.StateAtGlobal(decision.Target, rows);
                    if (actualState != FH6AutomationConstants.VehicleState.Deletable)
                    {
                        findings.Add(Finding(
                            "error",
                            scenario.Name + "：删车阶段选中了非 4 实际车辆",
                            "round=" + round + ", target=" + FormatCell(decision.Target) + ", actual=" + actualState + "\r\n" + TraceTail()));
                        return;
                    }

                    CellKey selectedLocal = SimulateKeyboardSelection(decision.Target, "delete");
                    actual.RemoveAt(vehicleList.CurrentOffset, selectedLocal, rows);
                    vehicleList.MarkDeletedAndShift(selectedLocal);
                    trace.Add("delete round=" + round + " removed target=" + FormatCell(decision.Target) + " selected=" + FormatCell(selectedLocal) + " offset=" + vehicleList.CurrentOffset);
                    findings.AddRange(AssertKnownVirtualTableMatchesActual("delete round " + round.ToString(CultureInfo.InvariantCulture) + " after remove"));
                    if (findings.Count > 0) return;
                }

                findings.Add(Finding("error", scenario.Name + "：删车阶段超过最大推演步数", TraceTail()));
            }

            private void RunDrivePhase(int round, List<SimulationFinding> findings)
            {
                for (int step = 0; step < maxSteps; step++)
                {
                    Decision decision = PlanDrive();
                    trace.Add("drive round=" + round + " step=" + step + " offset=" + vehicleList.CurrentOffset + " action=" + decision.Kind + " " + decision.Reason);

                    if (decision.Kind == ActionKind.Observe)
                    {
                        ApplyFakeOcr(true);
                        continue;
                    }

                    if (decision.Kind == ActionKind.Scroll)
                    {
                        vehicleList.ScrollDown(decision.ScrollTicks);
                        continue;
                    }

                    if (decision.Kind == ActionKind.UseDefault)
                    {
                        CellKey unusedTarget;
                        if (actual.TryGetFirstDrive900Candidate(rows, out unusedTarget))
                        {
                            findings.Add(Finding(
                                "bug",
                                scenario.Name + "：找开蓝图车使用默认车辆时，实际列表仍有 900 分状态 2 候选",
                                "round=" + round + "\r\nactual=" + actual.DebugString() + "\r\n" + TraceTail()));
                        }
                        return;
                    }

                    CellKey expectedTarget;
                    if (!actual.TryGetFirstDrive900Candidate(rows, out expectedTarget))
                    {
                        findings.Add(Finding(
                            "error",
                            scenario.Name + "：找开蓝图车阶段生成了目标，但实际列表没有 900 分状态 2 候选",
                            "round=" + round + ", target=" + FormatCell(decision.Target) + "\r\n" + TraceTail()));
                        return;
                    }
                    if (!decision.Target.Equals(expectedTarget))
                    {
                        findings.Add(Finding(
                            "error",
                            scenario.Name + "：找开蓝图车阶段没有选择列表最前的 900 分状态 2 候选",
                            "round=" + round + ", target=" + FormatCell(decision.Target) + ", expected=" + FormatCell(expectedTarget) + "\r\nactual=" + actual.DebugString() + "\r\n" + TraceTail()));
                        return;
                    }

                    CellKey selectedLocal = SimulateKeyboardSelection(decision.Target, "drive");
                    trace.Add("drive round=" + round + " selected target=" + FormatCell(decision.Target) + " selected=" + FormatCell(selectedLocal) + " offset=" + vehicleList.CurrentOffset);
                    return;
                }

                findings.Add(Finding("error", scenario.Name + "：找开蓝图车阶段超过最大推演步数", TraceTail()));
            }

            private Decision PlanSkill()
            {
                CellKey target;
                if (vehicleList.TryGetPendingValidNewGlobalTarget(out target))
                {
                    return Decision.Select(target, "虚拟表内已有状态 3，生成键盘路径");
                }

                CellKey lastTarget;
                CellKey nextCell;
                if (vehicleList.IsCompletionBoundaryReached(out lastTarget, out nextCell))
                {
                    return Decision.Stop("没有状态 3 且完成边界成立");
                }

                if (subaruBoundaryReached)
                {
                    return Decision.Scroll(1, subaruBoundaryReason + "，继续确认");
                }

                if (!vehicleList.IsVisibleSearchRangeObserved(visibleColumns))
                {
                    return Decision.Observe("当前可见目标段还有未知格子");
                }

                int skip;
                if (vehicleList.TryGetKnownNonPendingRunSkip(visibleColumns, out skip))
                {
                    return Decision.Scroll(skip, "跳过已知非 3 区间");
                }

                return Decision.Scroll(1, "当前可见范围已观察但没有状态 3");
            }

            private CellKey SimulateKeyboardSelection(CellKey target, string reason)
            {
                int dx = target.Col - vehicleList.CurrentOffset;
                if (dx > 0) vehicleList.KeyboardMoveViewRight(dx, "sim " + reason + " target path");
                else if (dx < 0) vehicleList.KeyboardMoveViewLeft(-dx, "sim " + reason + " target path");
                return new CellKey(target.Row, 0);
            }

            private Decision PlanDelete()
            {
                CellKey target;
                if (vehicleList.TryGetDeleteVehicleGlobalTarget(out target))
                {
                    return Decision.Select(target, "虚拟表内已有状态 4，生成键盘路径");
                }

                CellKey lastTarget;
                CellKey nextCell;
                if (vehicleList.IsDeleteCompletionBoundaryReached(out lastTarget, out nextCell))
                {
                    return Decision.Stop("没有状态 4 且删车完成边界成立");
                }

                if (subaruBoundaryReached)
                {
                    return Decision.Scroll(1, subaruBoundaryReason + "，继续确认");
                }

                if (!vehicleList.IsVisibleSearchRangeObserved(visibleColumns))
                {
                    return Decision.Observe("当前可见目标段还有未知格子");
                }

                int skip;
                if (vehicleList.TryGetKnownNonDeleteToUnknownSkip(visibleColumns, out skip))
                {
                    return Decision.Scroll(skip, "跳过已知不可删区间");
                }

                return Decision.Scroll(1, "当前可见范围已观察但没有可删车辆");
            }

            private Decision PlanDrive()
            {
                CellKey target;
                if (vehicleList.TryGetDriveVehicleGlobalTarget(out target))
                {
                    return Decision.Select(target, "虚拟表内已有 900 分状态 2 开车候选，按列表顺序生成键盘路径");
                }

                if (subaruBoundaryReached)
                {
                    return Decision.UseDefault("当前车辆列表区域没有识别到斯巴鲁");
                }

                if (vehicleList.VisibleHasOtherManufacturerOrUnknown(visibleColumns))
                {
                    return Decision.UseDefault("当前页已经出现目标车型完成边界");
                }

                if (!vehicleList.IsVisibleSearchRangeObserved(visibleColumns))
                {
                    return Decision.Observe("当前可见目标段还有未知格子");
                }

                int skip;
                if (vehicleList.TryGetKnownNonDriveToUnknownSkip(visibleColumns, out skip))
                {
                    return Decision.Scroll(skip, "跳过已知非 900 分状态 2 候选区间");
                }

                return Decision.Scroll(1, "当前可见范围已观察但没有 900 分状态 2 候选");
            }

            private void ApplyFakeOcr(bool skipSelectedCell)
            {
                ApplyFakeOcr(skipSelectedCell, false, 0);
            }

            private void ApplyFakeOcr(bool skipSelectedCell, bool tableBuild, int ignoredLeadingColumns)
            {
                HashSet<CellKey> targets = new HashSet<CellKey>();
                HashSet<CellKey> validNew = new HashSet<CellKey>();
                HashSet<CellKey> invalidNew = new HashSet<CellKey>();
                HashSet<CellKey> deletable = new HashSet<CellKey>();
                HashSet<CellKey> drive = new HashSet<CellKey>();
                HashSet<CellKey> manufacturers = new HashSet<CellKey>();
                Dictionary<CellKey, int> performanceScores = new Dictionary<CellKey, int>();

                for (int col = 0; col < visibleColumns; col++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        if (skipSelectedCell && row == 0 && col == 0) continue; // OCR 不能依赖当前选中格，长车名会滚动。

                        CellKey local = new CellKey(row, col);
                        ActualVehicle vehicle = actual.VehicleAt(vehicleList.CurrentOffset, local, rows);
                        int state = vehicle.State;
                        if (state == FH6AutomationConstants.VehicleState.UnknownOrNonTarget ||
                            state == FH6AutomationConstants.VehicleState.Target ||
                            state == FH6AutomationConstants.VehicleState.ValidNew ||
                            state == FH6AutomationConstants.VehicleState.Deletable)
                        {
                            manufacturers.Add(local);
                        }

                        if (state == FH6AutomationConstants.VehicleState.Target ||
                            state == FH6AutomationConstants.VehicleState.ValidNew ||
                            state == FH6AutomationConstants.VehicleState.Deletable)
                        {
                            targets.Add(local);
                        }

                        if (state == FH6AutomationConstants.VehicleState.ValidNew) validNew.Add(local);
                        else if (state == FH6AutomationConstants.VehicleState.Deletable) deletable.Add(local);

                        if (state == FH6AutomationConstants.VehicleState.Target ||
                            state == FH6AutomationConstants.VehicleState.ValidNew ||
                            state == FH6AutomationConstants.VehicleState.Deletable)
                        {
                            performanceScores[local] = vehicle.PerformanceScore > 0 ? vehicle.PerformanceScore : 600;
                        }
                    }
                }

                subaruBoundaryReached = manufacturers.Count == 0;
                subaruBoundaryReason = subaruBoundaryReached ? "当前车辆列表区域没有识别到斯巴鲁" : "";
                if (tableBuild)
                {
                    vehicleList.ApplyTableBuildObservation(visibleColumns, ignoredLeadingColumns, targets, validNew, invalidNew, deletable, drive, manufacturers, performanceScores, new HashSet<CellKey>());
                }
                else
                {
                    vehicleList.ApplyFullObservation(visibleColumns, targets, validNew, invalidNew, deletable, drive, manufacturers, performanceScores, new HashSet<CellKey>());
                }
                trace.Add("ocr offset=" + vehicleList.CurrentOffset + " tableBuild=" + tableBuild + " ignoredLeading=" + ignoredLeadingColumns + " manufacturers=" + manufacturers.Count + " targets=" + targets.Count + " 3=" + validNew.Count + " 4=" + deletable.Count + " scores=" + performanceScores.Count);
            }

            private List<SimulationFinding> AssertKnownVirtualTableMatchesActual(string context)
            {
                List<SimulationFinding> findings = new List<SimulationFinding>();
                Dictionary<CellKey, int> virtualStates = vehicleList.DebugStateCodes();
                foreach (KeyValuePair<CellKey, int> pair in virtualStates)
                {
                    int actualState = actual.StateAtGlobal(pair.Key, rows);
                    if (pair.Value != actualState)
                    {
                        findings.Add(Finding(
                            "error",
                            scenario.Name + "：虚拟表和实际列表不一致",
                            context + "\r\ncell=" + FormatCell(pair.Key) + ", virtual=" + pair.Value.ToString(CultureInfo.InvariantCulture) + ", actual=" + actualState.ToString(CultureInfo.InvariantCulture) + "\r\nactual=" + actual.DebugString() + "\r\n" + TraceTail()));
                        return findings;
                    }
                }
                return findings;
            }

            private SimulationFinding Finding(string severity, string title, string detail)
            {
                return new SimulationFinding
                {
                    Severity = severity,
                    Title = title,
                    Detail = detail
                };
            }

            private string TraceTail()
            {
                return string.Join("\r\n", trace.Skip(Math.Max(0, trace.Count - 70)).ToArray());
            }

            private static string FormatCell(CellKey cell)
            {
                return string.Format(CultureInfo.InvariantCulture, "local col={0}, row={1}", cell.Col, cell.Row);
            }

            private static string MakeSafeFileName(string text)
            {
                char[] chars = text.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    if (Path.GetInvalidFileNameChars().Contains(chars[i])) chars[i] = '_';
                    if (chars[i] > 127) chars[i] = '_';
                }
                string safe = new string(chars).Trim('_');
                return safe.Length == 0 ? "scenario" : safe;
            }
        }

        private sealed class SimulationFinding
        {
            public string Severity;
            public string Title;
            public string Detail;
        }

        private sealed class ActualVehicleList
        {
            private readonly List<ActualVehicle> vehicles = new List<ActualVehicle>();

            public void Add(ActualVehicle vehicle)
            {
                vehicles.Add(vehicle);
            }

            public int StateAt(int offset, CellKey local, int rows)
            {
                int index = ToIndex(offset, local, rows);
                if (index < 0 || index >= vehicles.Count) return FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown;
                return vehicles[index].State;
            }

            public ActualVehicle VehicleAt(int offset, CellKey local, int rows)
            {
                int index = ToIndex(offset, local, rows);
                if (index < 0 || index >= vehicles.Count) return ActualVehicle.OtherManufacturer();
                return vehicles[index];
            }

            public int StateAtGlobal(CellKey global, int rows)
            {
                int index = ToGlobalIndex(global, rows);
                if (index < 0 || index >= vehicles.Count) return FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown;
                return vehicles[index].State;
            }

            public void SetStateAt(int offset, CellKey local, int rows, int state)
            {
                int index = ToIndex(offset, local, rows);
                if (index < 0 || index >= vehicles.Count) throw new InvalidOperationException("actual index out of range");
                vehicles[index].State = state;
            }

            public void RemoveAt(int offset, CellKey local, int rows)
            {
                int index = ToIndex(offset, local, rows);
                if (index < 0 || index >= vehicles.Count) throw new InvalidOperationException("actual index out of range");
                vehicles.RemoveAt(index);
                vehicles.Add(ActualVehicle.OtherManufacturer());
            }

            public bool HasState(int state)
            {
                return vehicles.Any(v => v.State == state);
            }

            public bool TryGetFirstDrive900Candidate(int rows, out CellKey target)
            {
                int bestIndex = -1;
                for (int i = 0; i < vehicles.Count; i++)
                {
                    ActualVehicle vehicle = vehicles[i];
                    if (vehicle.Group != ActualVehicle.TargetGroup) continue;
                    if (vehicle.State != FH6AutomationConstants.VehicleState.Target) continue;
                    if (vehicle.PerformanceScore != 900) continue;

                    bestIndex = i;
                    break;
                }

                if (bestIndex < 0)
                {
                    target = new CellKey(0, 0);
                    return false;
                }

                target = new CellKey(bestIndex % rows, bestIndex / rows);
                return true;
            }

            public int TopUpValidNewTargetCars(int targetCount)
            {
                int current = vehicles.Count(v => v.Group == ActualVehicle.TargetGroup && v.State == FH6AutomationConstants.VehicleState.ValidNew);
                int addCount = Math.Max(0, targetCount - current);
                for (int i = 0; i < addCount; i++)
                {
                    int insertIndex = FindTargetAppendIndex();
                    vehicles.Insert(insertIndex, ActualVehicle.Target(FH6AutomationConstants.VehicleState.ValidNew));
                }
                return addCount;
            }

            public int ColumnCount(int rows)
            {
                return (vehicles.Count + rows - 1) / rows;
            }

            public string DebugString()
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < vehicles.Count; i++)
                {
                    string score = vehicles[i].PerformanceScore > 0 ? "#" + vehicles[i].PerformanceScore.ToString(CultureInfo.InvariantCulture) : "";
                    parts.Add(i.ToString(CultureInfo.InvariantCulture) + ":" + vehicles[i].State.ToString(CultureInfo.InvariantCulture) + score + "/" + vehicles[i].Group);
                }
                return string.Join(" ", parts.ToArray());
            }

            private int FindTargetAppendIndex()
            {
                List<int> targetIndexes = new List<int>();
                for (int i = 0; i < vehicles.Count; i++)
                {
                    if (vehicles[i].Group == ActualVehicle.TargetGroup) targetIndexes.Add(i);
                }

                if (targetIndexes.Count == 0) return 1;

                int lastNewOrDelete = targetIndexes
                    .Where(i => vehicles[i].State == FH6AutomationConstants.VehicleState.ValidNew || vehicles[i].State == FH6AutomationConstants.VehicleState.Deletable)
                    .DefaultIfEmpty(-1)
                    .Max();
                if (lastNewOrDelete >= 0) return lastNewOrDelete + 1;

                int lastAtOrAbove600 = targetIndexes
                    .Where(i => vehicles[i].PerformanceScore >= 600)
                    .DefaultIfEmpty(-1)
                    .Max();
                if (lastAtOrAbove600 >= 0) return lastAtOrAbove600 + 1;

                return targetIndexes[0];
            }

            private static int ToIndex(int offset, CellKey local, int rows)
            {
                return (offset + local.Col) * rows + local.Row;
            }

            private static int ToGlobalIndex(CellKey global, int rows)
            {
                return global.Col * rows + global.Row;
            }
        }

        private sealed class ActualVehicle
        {
            public const string PlaceholderGroup = "global-placeholder";
            public const string GenericSubaruGroup = "subaru-noise";
            public const string AnchorBeforeTargetGroup = "anchor-before-target";
            public const string TargetGroup = "target-model";
            public const string OtherManufacturerGroup = "other-manufacturer";

            public int State;
            public string Group;
            public int PerformanceScore = -1;

            public static ActualVehicle Placeholder()
            {
                return new ActualVehicle { State = FH6AutomationConstants.VehicleState.UnknownOrNonTarget, Group = PlaceholderGroup };
            }

            public static ActualVehicle GenericSubaru(string groupSuffix)
            {
                return new ActualVehicle { State = FH6AutomationConstants.VehicleState.UnknownOrNonTarget, Group = GenericSubaruGroup + ":" + groupSuffix };
            }

            public static ActualVehicle AnchorBeforeTarget()
            {
                return new ActualVehicle { State = FH6AutomationConstants.VehicleState.UnknownOrNonTarget, Group = AnchorBeforeTargetGroup };
            }

            public static ActualVehicle Target(int state)
            {
                int score = -1;
                if (state == FH6AutomationConstants.VehicleState.Target ||
                    state == FH6AutomationConstants.VehicleState.ValidNew ||
                    state == FH6AutomationConstants.VehicleState.Deletable)
                {
                    score = 600;
                }
                return new ActualVehicle { State = state, Group = TargetGroup, PerformanceScore = score };
            }

            public static ActualVehicle TargetWithScore(int score)
            {
                return new ActualVehicle { State = FH6AutomationConstants.VehicleState.Target, Group = TargetGroup, PerformanceScore = score };
            }

            public static ActualVehicle OtherManufacturer()
            {
                return new ActualVehicle { State = FH6AutomationConstants.VehicleState.OtherManufacturerOrUnknown, Group = OtherManufacturerGroup };
            }
        }
    }
}
