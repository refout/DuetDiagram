# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 104 | 72 | 17 | 15 |
| DuetDiagram.App | 6 | 562 | 352 | 116 | 94 |
| DuetDiagram.Benchmarks | 3 | 238 | 144 | 60 | 34 |
| DuetDiagram.Core | 55 | 6308 | 3374 | 1967 | 967 |
| DuetDiagram.Core.Tests | 20 | 3769 | 2731 | 390 | 648 |
| DuetDiagram.Layout | 15 | 2073 | 1134 | 660 | 279 |
| DuetDiagram.Layout.Tests | 4 | 929 | 686 | 71 | 172 |
| tools | 21 | 3213 | 2153 | 528 | 532 |
| **合计** | **125** | **17196** | **10646** | **3809** | **2741** |

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
| CommandResult.cs | 58 |
| DiagramCommandBase.cs | 35 |
| ErrorCodes.cs | 24 |
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
| ChangeConflict.cs | 72 |
| CollectionEquality.cs | 104 |
| Composites.cs | 45 |
| DefinitionCollection.cs | 36 |
| DiagramDocument.cs | 221 |
| DiagramEnums.cs | 99 |
| DiagramValidator.cs | 238 |
| EdgeDef.cs | 14 |
| FieldRegistry.cs | 128 |
| IDefinition.cs | 5 |
| LayoutHints.cs | 91 |
| NodeDef.cs | 59 |
| PageAndLayer.cs | 15 |
| Palette.cs | 29 |
| Styles.cs | 53 |
| SupportingDefs.cs | 62 |
| ValidationIssue.cs | 10 |
| DiagramHashing.cs | 172 |
| DiagramJsonContext.cs | 58 |
| DiagramSerializer.cs | 54 |
| LayoutSidecar.cs | 44 |
| SidecarBackup.cs | 132 |
| SidecarPaths.cs | 55 |
| SidecarStore.cs | 117 |
| UserSidecar.cs | 48 |
| ITimeProvider.cs | 22 |
| DiagramWorkspace.cs | 47 |

### DuetDiagram.Core.Tests

| 文件 | 代码 |
|---|---:|
| AtomicityTests.cs | 126 |
| BroadcasterTests.cs | 89 |
| CommandBusTests.cs | 242 |
| ConflictTests.cs | 223 |
| CorePurityTests.cs | 57 |
| Harness.cs | 73 |
| IrExtensionTests.cs | 194 |
| IrFixtures.cs | 218 |
| MementoRegistrationTests.cs | 66 |
| NestedExecuteTests.cs | 81 |
| RoundTripTests.cs | 122 |
| SessionIdResolutionTests.cs | 57 |
| SidecarBackupTests.cs | 235 |
| SidecarTests.cs | 294 |
| TempDirectory.cs | 22 |
| TestCommands.cs | 109 |
| UndoStressTests.cs | 77 |
| ValidatorTests.cs | 175 |
| VersionLogTests.cs | 177 |
| WorkspaceTests.cs | 94 |

### DuetDiagram.Layout

| 文件 | 代码 |
|---|---:|
| ConstraintLayoutEngine.cs | 100 |
| EngineLayoutResult.cs | 66 |
| Fallback.cs | 78 |
| FallbackPlan.cs | 81 |
| ILayoutEngine.cs | 5 |
| AnchorRestorer.cs | 35 |
| EdgeRouter.cs | 240 |
| EngineAdapter.cs | 41 |
| NodeGrid.cs | 70 |
| RowReflow.cs | 82 |
| SameRankContraction.cs | 144 |
| LayoutBudgets.cs | 21 |
| LayoutCoordinator.cs | 103 |
| LayoutModels.cs | 29 |
| LayoutRequestFactory.cs | 39 |

### DuetDiagram.Layout.Tests

| 文件 | 代码 |
|---|---:|
| FallbackTests.cs | 263 |
| Graphs.cs | 53 |
| LayoutRequestFactoryTests.cs | 154 |
| LayoutTests.cs | 216 |

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

