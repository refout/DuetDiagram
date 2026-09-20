# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 104 | 72 | 17 | 15 |
| DuetDiagram.App | 6 | 562 | 352 | 116 | 94 |
| DuetDiagram.Benchmarks | 3 | 238 | 144 | 60 | 34 |
| DuetDiagram.Core | 47 | 4733 | 2546 | 1443 | 744 |
| DuetDiagram.Core.Tests | 13 | 1932 | 1370 | 217 | 345 |
| tools | 30 | 4822 | 3174 | 880 | 768 |
| **合计** | **100** | **12391** | **7658** | **2733** | **2000** |

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
| FrameBenchmark.cs | 88 |
| MainWindow.axaml.cs | 11 |
| Program.cs | 34 |
| SelfTest.cs | 75 |

### DuetDiagram.Benchmarks

| 文件 | 代码 |
|---|---:|
| Benchmarks.cs | 73 |
| Program.cs | 9 |
| TestGraphs.cs | 62 |

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
| CollectionEquality.cs | 70 |
| Composites.cs | 45 |
| DefinitionCollection.cs | 34 |
| DiagramDocument.cs | 221 |
| DiagramEnums.cs | 99 |
| DiagramValidator.cs | 182 |
| EdgeDef.cs | 14 |
| IDefinition.cs | 5 |
| LayoutHints.cs | 91 |
| NodeDef.cs | 59 |
| PageAndLayer.cs | 15 |
| Palette.cs | 29 |
| Styles.cs | 53 |
| SupportingDefs.cs | 62 |
| DiagramHashing.cs | 57 |
| DiagramJsonContext.cs | 58 |
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
| RoundTripTests.cs | 122 |
| SessionIdResolutionTests.cs | 57 |
| TestCommands.cs | 109 |
| UndoStressTests.cs | 77 |
| VersionLogTests.cs | 177 |
| WorkspaceTests.cs | 94 |

### tools

| 文件 | 代码 |
|---|---:|
| Generator.cs | 143 |
| Program.cs | 69 |
| Support.cs | 86 |
| Program.cs | 155 |
| ApiDump.cs | 58 |
| CheckRunner.cs | 246 |
| DagreCandidate.cs | 258 |
| GraphShapes.cs | 95 |
| LayoutModels.cs | 41 |
| Program.cs | 36 |
| SugiyamaCandidate.cs | 85 |
| AnchorRestore.cs | 35 |
| ConstraintLayoutPipeline.cs | 57 |
| DagreEngine.cs | 40 |
| EdgeRouter.cs | 241 |
| InvariantChecks.cs | 364 |
| LayoutModels.cs | 64 |
| Program.cs | 10 |
| RowReflow.cs | 79 |
| SameRankContraction.cs | 131 |
| ApiDump.cs | 58 |
| DiagramTools.cs | 116 |
| HttpRoundtrip.cs | 47 |
| Program.cs | 72 |
| RecordingReadStream.cs | 49 |
| Roundtrips.cs | 206 |
| ServerHost.cs | 150 |
| ApiDump.cs | 59 |
| Checks.cs | 91 |
| Program.cs | 33 |

