# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 246 | 180 | 38 | 28 |
| DuetDiagram.App | 6 | 562 | 352 | 116 | 94 |
| DuetDiagram.Benchmarks | 5 | 414 | 252 | 100 | 62 |
| DuetDiagram.Core | 55 | 6597 | 3517 | 2078 | 1002 |
| DuetDiagram.Core.Tests | 22 | 4214 | 3017 | 466 | 731 |
| DuetDiagram.Dsl | 9 | 2553 | 1523 | 654 | 376 |
| DuetDiagram.Dsl.Tests | 7 | 2340 | 1707 | 243 | 390 |
| DuetDiagram.Layout | 16 | 2288 | 1225 | 764 | 299 |
| DuetDiagram.Layout.Tests | 4 | 1040 | 773 | 71 | 196 |
| DuetDiagram.Mermaid | 12 | 2682 | 1561 | 704 | 417 |
| DuetDiagram.Mermaid.Tests | 9 | 2741 | 1991 | 290 | 460 |
| DuetDiagram.Render | 3 | 501 | 296 | 117 | 88 |
| DuetDiagram.Render.Tests | 1 | 439 | 307 | 27 | 105 |
| tools | 34 | 7468 | 5112 | 1201 | 1155 |
| **合计** | **184** | **34085** | **21813** | **6869** | **5403** |

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
| MermaidBenchmarks.cs | 63 |
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
| ErrorCodes.cs | 29 |
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
| DiagramValidator.cs | 312 |
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
| ValidatorTests.cs | 292 |
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
| ConstraintLayoutEngine.cs | 106 |
| EngineLayoutResult.cs | 70 |
| Fallback.cs | 78 |
| FallbackPlan.cs | 85 |
| ILayoutEngine.cs | 5 |
| AnchorRestorer.cs | 35 |
| CompositeOutline.cs | 59 |
| EdgeRouter.cs | 251 |
| EngineAdapter.cs | 41 |
| NodeGrid.cs | 70 |
| RowReflow.cs | 82 |
| SameRankContraction.cs | 144 |
| LayoutBudgets.cs | 21 |
| LayoutCoordinator.cs | 103 |
| LayoutModels.cs | 30 |
| LayoutRequestFactory.cs | 45 |

### DuetDiagram.Layout.Tests

| 文件 | 代码 |
|---|---:|
| FallbackTests.cs | 275 |
| Graphs.cs | 53 |
| LayoutRequestFactoryTests.cs | 176 |
| LayoutTests.cs | 269 |

### DuetDiagram.Mermaid

| 文件 | 代码 |
|---|---:|
| ExportOptions.cs | 5 |
| ExportReport.cs | 7 |
| MermaidExporter.cs | 368 |
| ImportOptions.cs | 7 |
| ImportReport.cs | 10 |
| MermaidImporter.cs | 234 |
| MermaidDiagramKind.cs | 45 |
| MermaidLexer.cs | 308 |
| MermaidSource.cs | 23 |
| MermaidToken.cs | 38 |
| MermaidAst.cs | 24 |
| MermaidParser.cs | 492 |

### DuetDiagram.Mermaid.Tests

| 文件 | 代码 |
|---|---:|
| Corpus.cs | 53 |
| CorpusImportTests.cs | 118 |
| CorpusLexingTests.cs | 118 |
| CorpusParsingTests.cs | 191 |
| ExporterTests.cs | 287 |
| ImporterTests.cs | 329 |
| LexerTests.cs | 294 |
| ParserTests.cs | 407 |
| RoundTripTests.cs | 194 |

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
| Agreement.cs | 392 |
| Blind.cs | 207 |
| Corpus.cs | 89 |
| Digest.cs | 15 |
| Generator.cs | 143 |
| Judging.cs | 40 |
| Listings.cs | 300 |
| Predicate.cs | 527 |
| Program.cs | 160 |
| Ratings.cs | 108 |
| Sample.cs | 261 |
| Scoring.cs | 271 |
| Semantic.cs | 201 |
| Structure.cs | 170 |
| Support.cs | 134 |
| Verify.cs | 239 |
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

