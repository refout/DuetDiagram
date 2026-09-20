# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 104 | 72 | 17 | 15 |
| DuetDiagram.App | 5 | 402 | 250 | 90 | 62 |
| DuetDiagram.Core | 37 | 3102 | 1656 | 953 | 493 |
| DuetDiagram.Core.Tests | 13 | 1925 | 1363 | 217 | 345 |
| tools | 16 | 2412 | 1621 | 413 | 378 |
| **合计** | **72** | **7945** | **4962** | **1690** | **1293** |

## 明细

### DuetDiagram.AotSmokeTest

| 文件 | 代码 |
|---|---:|
| Program.cs | 72 |

### DuetDiagram.App

| 文件 | 代码 |
|---|---:|
| App.axaml.cs | 16 |
| DiagramPreview.cs | 128 |
| MainWindow.axaml.cs | 11 |
| Program.cs | 20 |
| SelfTest.cs | 75 |

### DuetDiagram.Core

| 文件 | 代码 |
|---|---:|
| IChangeBroadcaster.cs | 15 |
| InProcessBroadcaster.cs | 86 |
| NullChangeBroadcaster.cs | 23 |
| DiagramCommandBus.cs | 340 |
| DiagramCommandBusContext.cs | 48 |
| DiagramCommandBusOptions.cs | 9 |
| VersionCheckRequest.cs | 6 |
| AddNodeCommand.cs | 77 |
| ConnectEdgeCommand.cs | 82 |
| RemoveNodeCommand.cs | 126 |
| ChangeContext.cs | 20 |
| ChangeSource.cs | 11 |
| CommandError.cs | 7 |
| CommandMemento.cs | 29 |
| CommandResult.cs | 48 |
| DiagramCommandBase.cs | 35 |
| ErrorCodes.cs | 19 |
| FieldChange.cs | 17 |
| IDiagramCommand.cs | 12 |
| ISessionProvider.cs | 16 |
| SessionIds.cs | 8 |
| ValidationResult.cs | 8 |
| IDiagnosticsSink.cs | 28 |
| HistoryStack.cs | 64 |
| AuditLog.cs | 52 |
| DiffResult.cs | 27 |
| VersionEntry.cs | 23 |
| VersionLog.cs | 93 |
| DiagramDocument.cs | 55 |
| DiagramEnums.cs | 40 |
| EdgeDef.cs | 13 |
| NodeDef.cs | 11 |
| DiagramHashing.cs | 57 |
| DiagramJsonContext.cs | 28 |
| DiagramSerializer.cs | 54 |
| ITimeProvider.cs | 22 |
| DiagramWorkspace.cs | 47 |

### DuetDiagram.Core.Tests

| 文件 | 代码 |
|---|---:|
| AtomicityTests.cs | 126 |
| BroadcasterTests.cs | 89 |
| CommandBusTests.cs | 242 |
| CorePurityTests.cs | 57 |
| Harness.cs | 73 |
| MementoRegistrationTests.cs | 66 |
| NestedExecuteTests.cs | 81 |
| RoundTripTests.cs | 115 |
| SessionIdResolutionTests.cs | 57 |
| TestCommands.cs | 109 |
| UndoStressTests.cs | 77 |
| VersionLogTests.cs | 177 |
| WorkspaceTests.cs | 94 |

### tools

| 文件 | 代码 |
|---|---:|
| Program.cs | 155 |
| ApiDump.cs | 58 |
| CheckRunner.cs | 246 |
| DagreCandidate.cs | 258 |
| GraphShapes.cs | 95 |
| LayoutModels.cs | 41 |
| Program.cs | 36 |
| SugiyamaCandidate.cs | 85 |
| AnchorRestore.cs | 35 |
| ConstraintLayoutPipeline.cs | 45 |
| DagreEngine.cs | 40 |
| InvariantChecks.cs | 275 |
| LayoutModels.cs | 44 |
| Program.cs | 10 |
| RowReflow.cs | 67 |
| SameRankContraction.cs | 131 |

