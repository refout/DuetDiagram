# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 246 | 180 | 38 | 28 |
| DuetDiagram.App | 39 | 9240 | 5198 | 2525 | 1517 |
| DuetDiagram.Benchmarks | 6 | 588 | 349 | 149 | 90 |
| DuetDiagram.Core | 90 | 12538 | 7231 | 3445 | 1862 |
| DuetDiagram.Core.Tests | 34 | 8848 | 6263 | 890 | 1695 |
| DuetDiagram.Dsl | 9 | 2553 | 1523 | 654 | 376 |
| DuetDiagram.Dsl.Tests | 7 | 2340 | 1707 | 243 | 390 |
| DuetDiagram.E2E.Tests | 13 | 3372 | 2175 | 522 | 675 |
| DuetDiagram.Layout | 19 | 2941 | 1582 | 968 | 391 |
| DuetDiagram.Layout.Tests | 6 | 1676 | 1197 | 169 | 310 |
| DuetDiagram.Llm | 16 | 3013 | 1708 | 881 | 424 |
| DuetDiagram.Llm.Tests | 8 | 2592 | 1961 | 85 | 546 |
| DuetDiagram.Mermaid | 12 | 2682 | 1561 | 704 | 417 |
| DuetDiagram.Mermaid.Tests | 9 | 2741 | 1991 | 290 | 460 |
| DuetDiagram.Render | 20 | 3101 | 1594 | 1052 | 455 |
| DuetDiagram.Render.Tests | 12 | 2744 | 1959 | 259 | 526 |
| tools | 40 | 9436 | 6420 | 1484 | 1532 |
| **合计** | **341** | **70651** | **44599** | **14358** | **11694** |

## 明细

### DuetDiagram.AotSmokeTest

| 文件 | 代码 |
|---|---:|
| Program.cs | 180 |

### DuetDiagram.App

| 文件 | 代码 |
|---|---:|
| App.axaml.cs | 18 |
| DiagnosticsPanel.axaml.cs | 12 |
| DiagramCanvas.cs | 814 |
| DiffSidebar.axaml.cs | 105 |
| HighlightOverlay.cs | 17 |
| LayoutFailureDialog.axaml.cs | 29 |
| PropertyPanel.axaml.cs | 80 |
| SidecarRecoveryDialog.axaml.cs | 25 |
| StatusBar.axaml.cs | 72 |
| FrameBenchmark.cs | 375 |
| ConnectSession.cs | 19 |
| ConstraintGestures.cs | 97 |
| DragController.cs | 58 |
| DragSession.cs | 23 |
| EdgeAdorner.cs | 57 |
| EdgeHandleHitTest.cs | 118 |
| HighlightTracker.cs | 167 |
| SelectionSet.cs | 33 |
| MainWindow.axaml.cs | 232 |
| Program.cs | 78 |
| SampleDiagram.cs | 100 |
| SelfTest.cs | 148 |
| ConstraintEditorBinder.cs | 117 |
| DiagramSession.cs | 659 |
| DocumentFile.cs | 43 |
| DocumentLaunch.cs | 67 |
| ErrorPresenter.cs | 41 |
| ErrorPresenterTable.cs | 69 |
| FieldEditBinder.cs | 257 |
| WorkspaceRegistry.cs | 94 |
| CanvasViewModel.cs | 285 |
| ConstraintEditorViewModel.cs | 165 |
| DiagnosticsViewModel.cs | 155 |
| PropertyFieldCatalog.cs | 123 |
| PropertyFieldViewModel.cs | 93 |
| PropertyPanelViewModel.cs | 204 |
| PropertySectionViewModel.cs | 35 |
| RenderModeViewModel.cs | 33 |
| StatusBarViewModel.cs | 81 |

### DuetDiagram.Benchmarks

| 文件 | 代码 |
|---|---:|
| Benchmarks.cs | 73 |
| ConstraintBenchmarks.cs | 97 |
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
| AddActionCommand.cs | 71 |
| AddLayoutConstraintCommand.cs | 198 |
| AddNodeCommand.cs | 77 |
| AddTagCommand.cs | 76 |
| ConnectEdgeCommand.cs | 82 |
| CreateCompositeCommand.cs | 150 |
| CreateLayerCommand.cs | 68 |
| CreatePageCommand.cs | 68 |
| DefinePaletteEntryCommand.cs | 88 |
| DeletePageCommand.cs | 77 |
| DisconnectEdgeCommand.cs | 81 |
| DissolveCompositeCommand.cs | 106 |
| MoveIntoCompositeCommand.cs | 93 |
| ReconnectEdgeCommand.cs | 130 |
| RemoveActionCommand.cs | 69 |
| RemoveLayoutConstraintCommand.cs | 107 |
| RemoveNodeCommand.cs | 126 |
| RemovePaletteEntryCommand.cs | 105 |
| RemoveTagCommand.cs | 69 |
| RenameLayerCommand.cs | 73 |
| ReorderLayerCommand.cs | 77 |
| SetCanvasSettingsCommand.cs | 152 |
| SetDirectionCommand.cs | 65 |
| SetEdgeFieldCommand.cs | 110 |
| SetKindCommand.cs | 65 |
| SetNodeFieldCommand.cs | 108 |
| SetPlaceCommand.cs | 113 |
| SetSpacingCommand.cs | 89 |
| UpdatePaletteEntryCommand.cs | 74 |
| ChangeContext.cs | 20 |
| ChangeSource.cs | 11 |
| CommandError.cs | 7 |
| CommandMemento.cs | 111 |
| CommandResult.cs | 58 |
| CompositeMembership.cs | 159 |
| DiagramCommandBase.cs | 35 |
| EdgeFieldValue.cs | 93 |
| ErrorCodes.cs | 43 |
| FieldChange.cs | 17 |
| IDiagramCommand.cs | 12 |
| ISessionProvider.cs | 16 |
| LayerAccess.cs | 46 |
| NodeFieldValue.cs | 361 |
| PageAccess.cs | 36 |
| PaletteFieldValue.cs | 73 |
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
| Composites.cs | 49 |
| DefinitionCollection.cs | 36 |
| DiagramDocument.cs | 275 |
| DiagramEnums.cs | 99 |
| DiagramValidator.cs | 348 |
| EdgeDef.cs | 14 |
| FieldRegistry.cs | 190 |
| IDefinition.cs | 5 |
| LayoutConstraintSpec.cs | 44 |
| LayoutHints.cs | 91 |
| NodeDef.cs | 59 |
| PageAndLayer.cs | 15 |
| Palette.cs | 48 |
| Styles.cs | 53 |
| SupportingDefs.cs | 62 |
| ValidationIssue.cs | 10 |
| DiagramHashing.cs | 172 |
| DiagramJsonContext.cs | 67 |
| DiagramSerializer.cs | 54 |
| LayoutSidecar.cs | 44 |
| SidecarBackup.cs | 132 |
| SidecarPaths.cs | 55 |
| SidecarStore.cs | 123 |
| UserSidecar.cs | 48 |
| ITimeProvider.cs | 22 |
| DiagramWorkspace.cs | 47 |
| DocumentLock.cs | 136 |
| Heartbeat.cs | 62 |

### DuetDiagram.Core.Tests

| 文件 | 代码 |
|---|---:|
| AtomicityTests.cs | 126 |
| BroadcasterTests.cs | 89 |
| CommandBusTests.cs | 242 |
| CommandListTests.cs | 154 |
| CommentDisciplineTests.cs | 99 |
| CompositeCommandTests.cs | 405 |
| ConflictTests.cs | 234 |
| CorePurityTests.cs | 57 |
| DisconnectEdgeCommandTests.cs | 176 |
| DocumentFactoryTests.cs | 66 |
| DocumentLockTests.cs | 177 |
| DocumentSettingCommandTests.cs | 219 |
| EdgeCommandTests.cs | 276 |
| Harness.cs | 215 |
| IrExtensionTests.cs | 202 |
| IrFixtures.cs | 176 |
| LayerCommandTests.cs | 180 |
| LayoutCommandTests.cs | 318 |
| LayoutConstraintTests.cs | 307 |
| MementoRegistrationTests.cs | 66 |
| NestedExecuteTests.cs | 81 |
| NodeFieldTests.cs | 303 |
| PageTagActionCommandTests.cs | 298 |
| PaletteCommandTests.cs | 256 |
| RoundTripTests.cs | 139 |
| SessionIdResolutionTests.cs | 57 |
| SidecarBackupTests.cs | 245 |
| SidecarTests.cs | 301 |
| TempDirectory.cs | 22 |
| TestCommands.cs | 109 |
| UndoStressTests.cs | 77 |
| ValidatorTests.cs | 320 |
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

### DuetDiagram.E2E.Tests

| 文件 | 代码 |
|---|---:|
| CanvasSmokeTests.cs | 202 |
| ConnectTests.cs | 86 |
| ConstraintEditorTests.cs | 299 |
| DiagnosticsPanelTests.cs | 151 |
| DragTests.cs | 121 |
| EdgeEditTests.cs | 96 |
| ErrorPresentationTests.cs | 208 |
| HeadlessFixture.cs | 124 |
| HighlightTests.cs | 70 |
| LayoutFailureTests.cs | 217 |
| ModeSwitchTests.cs | 198 |
| MultiWindowTests.cs | 259 |
| PropertyPanelTests.cs | 144 |

### DuetDiagram.Layout

| 文件 | 代码 |
|---|---:|
| ConstraintLayoutEngine.cs | 210 |
| EngineLayoutResult.cs | 73 |
| Fallback.cs | 78 |
| FallbackPlan.cs | 115 |
| ILayoutEngine.cs | 5 |
| AlignSolver.cs | 70 |
| AnchorRestorer.cs | 35 |
| CompositeOutline.cs | 59 |
| EdgeRouter.cs | 251 |
| EngineAdapter.cs | 41 |
| NodeGrid.cs | 70 |
| OrderSolver.cs | 128 |
| RowPacker.cs | 40 |
| RowReflow.cs | 62 |
| SameRankContraction.cs | 144 |
| LayoutBudgets.cs | 21 |
| LayoutCoordinator.cs | 103 |
| LayoutModels.cs | 32 |
| LayoutRequestFactory.cs | 45 |

### DuetDiagram.Layout.Tests

| 文件 | 代码 |
|---|---:|
| AlignTests.cs | 129 |
| FallbackTests.cs | 351 |
| Graphs.cs | 69 |
| LayoutRequestFactoryTests.cs | 176 |
| LayoutTests.cs | 327 |
| OrderTests.cs | 145 |

### DuetDiagram.Llm

| 文件 | 代码 |
|---|---:|
| DiagramSummary.cs | 46 |
| SummaryBuilder.cs | 73 |
| SummaryFormatter.cs | 118 |
| ActionDispatch.cs | 111 |
| CompositeTool.cs | 64 |
| DiagramToolContext.cs | 20 |
| DiagramToolset.cs | 174 |
| EditTool.cs | 242 |
| LayoutTool.cs | 190 |
| PortParser.cs | 16 |
| SchemaBuilder.cs | 127 |
| StyleResolver.cs | 137 |
| StyleTool.cs | 123 |
| ToolDescriptor.cs | 99 |
| ToolRegistry.cs | 76 |
| ToolResult.cs | 92 |

### DuetDiagram.Llm.Tests

| 文件 | 代码 |
|---|---:|
| CompositeToolTests.cs | 226 |
| EditToolTests.cs | 311 |
| Harness.cs | 71 |
| LayoutToolTests.cs | 304 |
| SchemaTests.cs | 181 |
| StyleToolTests.cs | 236 |
| SummaryTests.cs | 427 |
| ToolRegistryTests.cs | 205 |

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
| CullingIndex.cs | 72 |
| CullingPolicy.cs | 19 |
| DiagnosticsFrame.cs | 14 |
| DiagnosticsSampler.cs | 107 |
| DrawCommand.cs | 80 |
| DrawList.cs | 54 |
| Highlight.cs | 181 |
| HitTester.cs | 59 |
| ITextMeasurer.cs | 6 |
| ModeSwitch.cs | 44 |
| QuadTree.cs | 260 |
| RenderMode.cs | 6 |
| SceneBuilder.cs | 371 |
| SkiaTextMeasurer.cs | 70 |
| SpatialRect.cs | 29 |
| TextLayout.cs | 22 |
| Theme.cs | 113 |
| Viewport.cs | 49 |
| ViewportCulling.cs | 6 |
| ViewportTransform.cs | 32 |

### DuetDiagram.Render.Tests

| 文件 | 代码 |
|---|---:|
| CullingPolicyTests.cs | 216 |
| DiagnosticsSamplerTests.cs | 199 |
| DrawListTests.cs | 237 |
| FakeTextMeasurer.cs | 17 |
| HighlightTests.cs | 143 |
| HitTesterTests.cs | 104 |
| Layouts.cs | 59 |
| ModeSwitchTests.cs | 113 |
| QuadTreeTests.cs | 285 |
| SceneSnapshotTests.cs | 251 |
| Snapshot.cs | 57 |
| ViewportTests.cs | 278 |

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
| Analysis.cs | 381 |
| Checks.cs | 181 |
| Program.cs | 75 |
| Statistics.cs | 428 |
| Study.cs | 111 |
| Template.cs | 132 |

