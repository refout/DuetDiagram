# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 246 | 180 | 38 | 28 |
| DuetDiagram.App | 6 | 562 | 352 | 116 | 94 |
| DuetDiagram.Benchmarks | 4 | 309 | 189 | 73 | 47 |
| DuetDiagram.Core | 55 | 6500 | 3452 | 2059 | 989 |
| DuetDiagram.Core.Tests | 22 | 4127 | 2952 | 456 | 719 |
| DuetDiagram.Dsl | 9 | 2553 | 1523 | 654 | 376 |
| DuetDiagram.Dsl.Tests | 7 | 2340 | 1707 | 243 | 390 |
| DuetDiagram.Layout | 15 | 2073 | 1134 | 660 | 279 |
| DuetDiagram.Layout.Tests | 4 | 954 | 710 | 59 | 185 |
| DuetDiagram.Mermaid | 6 | 1488 | 886 | 366 | 236 |
| DuetDiagram.Mermaid.Tests | 5 | 1428 | 1045 | 147 | 236 |
| DuetDiagram.Render | 3 | 501 | 296 | 117 | 88 |
| DuetDiagram.Render.Tests | 1 | 439 | 307 | 27 | 105 |
| tools | 24 | 4270 | 2876 | 708 | 686 |
| **合计** | **162** | **27790** | **17609** | **5723** | **4458** |

## 明细

### DuetDiagram.AotSmokeTest

| 文件 | 代码 |
|---|---:|
| Program.cs | 180 |

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
| QuadTreeBenchmarks.cs | 45 |
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
| ErrorCodes.cs | 27 |
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
| DiagramDocument.cs | 275 |
| DiagramEnums.cs | 99 |
| DiagramValidator.cs | 249 |
| EdgeDef.cs | 14 |
| FieldRegistry.cs | 132 |
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
| SidecarStore.cs | 123 |
| UserSidecar.cs | 48 |
| ITimeProvider.cs | 22 |
| DiagramWorkspace.cs | 47 |

### DuetDiagram.Core.Tests

| 文件 | 代码 |
|---|---:|
| AtomicityTests.cs | 126 |
| BroadcasterTests.cs | 89 |
| CommandBusTests.cs | 242 |
| CommentDisciplineTests.cs | 99 |
| ConflictTests.cs | 229 |
| CorePurityTests.cs | 57 |
| DocumentFactoryTests.cs | 66 |
| Harness.cs | 73 |
| IrExtensionTests.cs | 202 |
| IrFixtures.cs | 176 |
| MementoRegistrationTests.cs | 66 |
| NestedExecuteTests.cs | 81 |
| RoundTripTests.cs | 137 |
| SessionIdResolutionTests.cs | 57 |
| SidecarBackupTests.cs | 245 |
| SidecarTests.cs | 301 |
| TempDirectory.cs | 22 |
| TestCommands.cs | 109 |
| UndoStressTests.cs | 77 |
| ValidatorTests.cs | 227 |
| VersionLogTests.cs | 177 |
| WorkspaceTests.cs | 94 |

### DuetDiagram.Dsl

| 文件 | 代码 |
|---|---:|
| DslLexer.cs | 228 |
| DslSource.cs | 23 |
| DslToken.cs | 26 |
| DslMapper.cs | 370 |
| MappingOptions.cs | 10 |
| MappingReport.cs | 13 |
| SidecarMerge.cs | 20 |
| DslAst.cs | 68 |
| DslParser.cs | 765 |

### DuetDiagram.Dsl.Tests

| 文件 | 代码 |
|---|---:|
| AstProjection.cs | 25 |
| Corpus.cs | 45 |
| CorpusParsingTests.cs | 203 |
| LayoutIntentTests.cs | 365 |
| LexerTests.cs | 195 |
| MapperTests.cs | 366 |
| ParserTests.cs | 508 |

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
| FallbackTests.cs | 275 |
| Graphs.cs | 53 |
| LayoutRequestFactoryTests.cs | 154 |
| LayoutTests.cs | 228 |

### DuetDiagram.Mermaid

| 文件 | 代码 |
|---|---:|
| MermaidDiagramKind.cs | 45 |
| MermaidLexer.cs | 273 |
| MermaidSource.cs | 23 |
| MermaidToken.cs | 38 |
| MermaidAst.cs | 24 |
| MermaidParser.cs | 483 |

### DuetDiagram.Mermaid.Tests

| 文件 | 代码 |
|---|---:|
| Corpus.cs | 53 |
| CorpusLexingTests.cs | 118 |
| CorpusParsingTests.cs | 194 |
| LexerTests.cs | 294 |
| ParserTests.cs | 386 |

### DuetDiagram.Render

| 文件 | 代码 |
|---|---:|
| QuadTree.cs | 260 |
| SpatialRect.cs | 28 |
| ViewportCulling.cs | 8 |

### DuetDiagram.Render.Tests

| 文件 | 代码 |
|---|---:|
| QuadTreeTests.cs | 307 |

### tools

| 文件 | 代码 |
|---|---:|
| Corpus.cs | 65 |
| Generator.cs | 143 |
| Listings.cs | 470 |
| Program.cs | 86 |
| Structure.cs | 169 |
| Support.cs | 88 |
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

