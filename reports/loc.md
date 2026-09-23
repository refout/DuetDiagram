# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 246 | 180 | 38 | 28 |
| DuetDiagram.App | 52 | 12754 | 7216 | 3442 | 2096 |
| DuetDiagram.Benchmarks | 6 | 588 | 349 | 149 | 90 |
| DuetDiagram.Core | 98 | 13636 | 7751 | 3874 | 2011 |
| DuetDiagram.Core.Tests | 39 | 10140 | 7157 | 1028 | 1955 |
| DuetDiagram.Dsl | 9 | 2553 | 1523 | 654 | 376 |
| DuetDiagram.Dsl.Tests | 7 | 2340 | 1707 | 243 | 390 |
| DuetDiagram.E2E.Tests | 20 | 5788 | 3773 | 804 | 1211 |
| DuetDiagram.Layout | 19 | 2974 | 1604 | 977 | 393 |
| DuetDiagram.Layout.Tests | 6 | 1737 | 1244 | 173 | 320 |
| DuetDiagram.Llm | 25 | 4533 | 2509 | 1379 | 645 |
| DuetDiagram.Llm.Tests | 16 | 4182 | 3013 | 323 | 846 |
| DuetDiagram.Mcp | 14 | 2356 | 1238 | 766 | 352 |
| DuetDiagram.Mcp.Tests | 11 | 3137 | 1998 | 506 | 633 |
| DuetDiagram.Mermaid | 12 | 2682 | 1561 | 704 | 417 |
| DuetDiagram.Mermaid.Tests | 9 | 2741 | 1991 | 290 | 460 |
| DuetDiagram.Render | 21 | 3540 | 1817 | 1209 | 514 |
| DuetDiagram.Render.Tests | 14 | 3202 | 2277 | 314 | 611 |
| tools | 34 | 9460 | 6458 | 1442 | 1560 |
| **合计** | **413** | **88589** | **55366** | **18315** | **14908** |

## 明细

### DuetDiagram.AotSmokeTest

| 文件 | 代码 |
|---|---:|
| Program.cs | 180 |

### DuetDiagram.App

| 文件 | 代码 |
|---|---:|
| App.axaml.cs | 18 |
| ContextMenuBuilder.cs | 33 |
| DiagnosticsPanel.axaml.cs | 12 |
| DiagramCanvas.cs | 904 |
| DiagramMenuBar.axaml.cs | 93 |
| DiagramToolBar.axaml.cs | 105 |
| DiffSidebar.axaml.cs | 105 |
| HighlightOverlay.cs | 17 |
| LayerPanel.axaml.cs | 271 |
| LayoutFailureDialog.axaml.cs | 29 |
| PageTabs.axaml.cs | 133 |
| PropertyPanel.axaml.cs | 80 |
| SidecarRecoveryDialog.axaml.cs | 25 |
| StatusBar.axaml.cs | 72 |
| FrameBenchmark.cs | 375 |
| ConnectSession.cs | 19 |
| ConstraintGestures.cs | 97 |
| DragController.cs | 58 |
| DragSession.cs | 23 |
| EdgeAdorner.cs | 76 |
| EdgeHandleHitTest.cs | 118 |
| HighlightTracker.cs | 167 |
| MarqueeSession.cs | 32 |
| SelectionSet.cs | 33 |
| MainWindow.axaml.cs | 295 |
| Program.cs | 78 |
| SampleDiagram.cs | 103 |
| SelfTest.cs | 148 |
| ConstraintEditorBinder.cs | 117 |
| ContextEntries.cs | 53 |
| DiagramSession.cs | 1027 |
| DocumentFile.cs | 43 |
| DocumentLaunch.cs | 67 |
| ErrorPresenter.cs | 41 |
| ErrorPresenterTable.cs | 74 |
| FieldEditBinder.cs | 257 |
| MenuEntries.cs | 177 |
| MenuRegistry.cs | 101 |
| WorkspaceRegistry.cs | 94 |
| CanvasViewModel.cs | 285 |
| ConstraintEditorViewModel.cs | 165 |
| DiagnosticsViewModel.cs | 155 |
| LayerPanelViewModel.cs | 226 |
| LayerRowViewModel.cs | 86 |
| PageTabViewModel.cs | 41 |
| PageTabsViewModel.cs | 99 |
| PropertyFieldCatalog.cs | 125 |
| PropertyFieldViewModel.cs | 93 |
| PropertyPanelViewModel.cs | 222 |
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
| AssignLayerCommand.cs | 149 |
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
| SetLayerLockedCommand.cs | 39 |
| SetLayerVisibleCommand.cs | 39 |
| SetNodeFieldCommand.cs | 108 |
| SetPlaceCommand.cs | 113 |
| SetSpacingCommand.cs | 89 |
| UpdatePaletteEntryCommand.cs | 74 |
| ChangeContext.cs | 20 |
| ChangeSource.cs | 11 |
| CommandError.cs | 7 |
| CommandMemento.cs | 117 |
| CommandResult.cs | 58 |
| CompositeMembership.cs | 159 |
| DiagramCommandBase.cs | 35 |
| EdgeFieldValue.cs | 97 |
| ErrorCodes.cs | 48 |
| FieldChange.cs | 17 |
| IDiagramCommand.cs | 12 |
| ISessionProvider.cs | 16 |
| LayerAccess.cs | 92 |
| NodeFieldValue.cs | 365 |
| PageAccess.cs | 36 |
| PaletteFieldValue.cs | 73 |
| SessionIds.cs | 8 |
| ValidationResult.cs | 8 |
| LayerAcl.cs | 25 |
| PermissionSet.cs | 11 |
| SoftLock.cs | 83 |
| SoftLockOptions.cs | 6 |
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
| EdgeDef.cs | 15 |
| FieldRegistry.cs | 193 |
| IDefinition.cs | 5 |
| LayoutConstraintSpec.cs | 44 |
| LayoutHints.cs | 91 |
| NodeDef.cs | 62 |
| PageAndLayer.cs | 15 |
| PageMembership.cs | 91 |
| Palette.cs | 48 |
| Styles.cs | 53 |
| SupportingDefs.cs | 62 |
| ValidationIssue.cs | 10 |
| DiagramHashing.cs | 174 |
| DiagramJsonContext.cs | 67 |
| DiagramSerializer.cs | 54 |
| LayoutSidecar.cs | 44 |
| SidecarBackup.cs | 132 |
| SidecarPaths.cs | 55 |
| SidecarStore.cs | 123 |
| UserSidecar.cs | 48 |
| ITimeProvider.cs | 22 |
| DiagramWorkspace.cs | 50 |
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
| Harness.cs | 248 |
| IrExtensionTests.cs | 202 |
| IrFixtures.cs | 176 |
| LayerAssignmentTests.cs | 162 |
| LayerCommandTests.cs | 180 |
| LayerVisibilityTests.cs | 191 |
| LayoutCommandTests.cs | 318 |
| LayoutConstraintTests.cs | 307 |
| MementoRegistrationTests.cs | 66 |
| NestedExecuteTests.cs | 81 |
| NodeFieldTests.cs | 304 |
| PageMembershipTests.cs | 228 |
| PageTagActionCommandTests.cs | 298 |
| PaletteCommandTests.cs | 256 |
| PermissionTests.cs | 105 |
| RoundTripTests.cs | 139 |
| SessionIdResolutionTests.cs | 57 |
| SidecarBackupTests.cs | 245 |
| SidecarTests.cs | 301 |
| SoftLockTests.cs | 174 |
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
| ContextMenuTests.cs | 249 |
| DiagnosticsPanelTests.cs | 151 |
| DragTests.cs | 121 |
| EdgeEditTests.cs | 96 |
| ErrorPresentationTests.cs | 208 |
| HeadlessFixture.cs | 177 |
| HighlightTests.cs | 70 |
| LayerPanelTests.cs | 357 |
| LayerVisibilityTests.cs | 154 |
| LayoutFailureTests.cs | 217 |
| MarqueeTests.cs | 269 |
| MenuBarTests.cs | 131 |
| ModeSwitchTests.cs | 209 |
| MultiWindowTests.cs | 259 |
| PageTabsTests.cs | 233 |
| PropertyPanelTests.cs | 167 |
| ToolBarTests.cs | 118 |

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
| LayoutRequestFactory.cs | 67 |

### DuetDiagram.Layout.Tests

| 文件 | 代码 |
|---|---:|
| AlignTests.cs | 129 |
| FallbackTests.cs | 351 |
| Graphs.cs | 69 |
| LayoutRequestFactoryTests.cs | 223 |
| LayoutTests.cs | 327 |
| OrderTests.cs | 145 |

### DuetDiagram.Llm

| 文件 | 代码 |
|---|---:|
| ChatOptionsFactory.cs | 27 |
| DiagramChatClient.cs | 86 |
| ScriptedChatClient.cs | 52 |
| DiagramSummary.cs | 46 |
| SummaryBuilder.cs | 86 |
| SummaryFormatter.cs | 118 |
| ErrorEnvelope.cs | 53 |
| ErrorLoop.cs | 68 |
| RepairHints.cs | 216 |
| ActionDispatch.cs | 175 |
| CompositeTool.cs | 64 |
| DiagramToolContext.cs | 24 |
| DiagramToolset.cs | 169 |
| EditTool.cs | 290 |
| ExportTool.cs | 50 |
| HistoryTool.cs | 85 |
| LayoutTool.cs | 190 |
| PortParser.cs | 16 |
| SchemaBuilder.cs | 127 |
| StyleResolver.cs | 137 |
| StyleTool.cs | 123 |
| ToolDescriptor.cs | 99 |
| ToolRegistry.cs | 76 |
| ToolResult.cs | 104 |
| ValidateTool.cs | 28 |

### DuetDiagram.Llm.Tests

| 文件 | 代码 |
|---|---:|
| ChatClientTests.cs | 200 |
| CompositeToolTests.cs | 226 |
| EditToolTests.cs | 311 |
| ErrorLoopTests.cs | 145 |
| ExportToolTests.cs | 130 |
| Harness.cs | 82 |
| HistoryToolTests.cs | 133 |
| LayerPermissionTests.cs | 98 |
| LayoutToolTests.cs | 304 |
| RepairHintTests.cs | 147 |
| SchemaTests.cs | 181 |
| StyleToolTests.cs | 236 |
| SummaryTests.cs | 472 |
| ToolParityTests.cs | 53 |
| ToolRegistryTests.cs | 222 |
| ValidateToolTests.cs | 73 |

### DuetDiagram.Mcp

| 文件 | 代码 |
|---|---:|
| Program.cs | 78 |
| BearerAuth.cs | 87 |
| ChangeFeed.cs | 86 |
| ConflictResponder.cs | 40 |
| DiagramMcpServer.cs | 91 |
| HttpHost.cs | 280 |
| RateLimiter.cs | 43 |
| SessionCore.cs | 71 |
| SessionState.cs | 110 |
| StandardErrorDiagnostics.cs | 11 |
| StdioLogging.cs | 11 |
| WorkspaceGuard.cs | 60 |
| SkillCatalog.cs | 252 |
| SkillResource.cs | 18 |

### DuetDiagram.Mcp.Tests

| 文件 | 代码 |
|---|---:|
| ChangeFeedTests.cs | 149 |
| ConcurrencyTests.cs | 100 |
| ConflictResponseTests.cs | 205 |
| Harness.cs | 323 |
| HttpTransportTests.cs | 210 |
| ProgressiveDisclosureTests.cs | 131 |
| ProtocolTests.cs | 91 |
| SecurityTests.cs | 321 |
| SessionStateTests.cs | 175 |
| SkillTests.cs | 143 |
| StdioTests.cs | 150 |

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
| DrawList.cs | 64 |
| Highlight.cs | 181 |
| HitTester.cs | 113 |
| ITextMeasurer.cs | 6 |
| LayerPlan.cs | 87 |
| ModeSwitch.cs | 44 |
| QuadTree.cs | 260 |
| RenderMode.cs | 6 |
| SceneBuilder.cs | 443 |
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
| LayerRenderTests.cs | 201 |
| Layouts.cs | 59 |
| ModeSwitchTests.cs | 113 |
| PageRenderTests.cs | 117 |
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
| Agents.cs | 165 |
| Program.cs | 78 |
| Scenarios.cs | 321 |
| Session.cs | 355 |
| ApiDump.cs | 58 |
| CheckRunner.cs | 246 |
| DagreCandidate.cs | 258 |
| GraphShapes.cs | 95 |
| LayoutModels.cs | 41 |
| Program.cs | 36 |
| SugiyamaCandidate.cs | 85 |
| Analysis.cs | 381 |
| Checks.cs | 181 |
| Program.cs | 75 |
| Statistics.cs | 428 |
| Study.cs | 111 |
| Template.cs | 132 |

