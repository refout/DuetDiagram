# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 320 | 217 | 65 | 38 |
| DuetDiagram.App | 66 | 17065 | 9747 | 4465 | 2853 |
| DuetDiagram.Benchmarks | 6 | 588 | 349 | 149 | 90 |
| DuetDiagram.Core | 121 | 18158 | 10233 | 5286 | 2639 |
| DuetDiagram.Core.Tests | 44 | 12614 | 8880 | 1321 | 2413 |
| DuetDiagram.Dsl | 9 | 2553 | 1523 | 654 | 376 |
| DuetDiagram.Dsl.Tests | 7 | 2340 | 1707 | 243 | 390 |
| DuetDiagram.E2E.Tests | 26 | 7632 | 4947 | 1073 | 1612 |
| DuetDiagram.Layout | 19 | 2974 | 1604 | 977 | 393 |
| DuetDiagram.Layout.Tests | 6 | 1737 | 1244 | 173 | 320 |
| DuetDiagram.Llm | 25 | 4816 | 2618 | 1524 | 674 |
| DuetDiagram.Llm.Tests | 17 | 4836 | 3472 | 384 | 980 |
| DuetDiagram.Mcp | 15 | 2498 | 1292 | 840 | 366 |
| DuetDiagram.Mcp.Tests | 11 | 3288 | 2082 | 546 | 660 |
| DuetDiagram.Mermaid | 12 | 2677 | 1564 | 696 | 417 |
| DuetDiagram.Mermaid.Tests | 9 | 2780 | 2021 | 293 | 466 |
| DuetDiagram.Render | 33 | 6500 | 3451 | 2112 | 937 |
| DuetDiagram.Render.Tests | 24 | 6233 | 4291 | 815 | 1127 |
| tools | 37 | 9817 | 6675 | 1507 | 1635 |
| **合计** | **488** | **109426** | **67917** | **23123** | **18386** |

## 明细

### DuetDiagram.AotSmokeTest

| 文件 | 代码 |
|---|---:|
| Program.cs | 217 |

### DuetDiagram.App

| 文件 | 代码 |
|---|---:|
| App.axaml.cs | 18 |
| ContextMenuBuilder.cs | 33 |
| DiagnosticsPanel.axaml.cs | 12 |
| DiagramCanvas.cs | 869 |
| DiagramMenuBar.axaml.cs | 93 |
| DiagramToolBar.axaml.cs | 105 |
| DiffSidebar.axaml.cs | 105 |
| HighlightOverlay.cs | 17 |
| LayerPanel.axaml.cs | 271 |
| LayoutFailureDialog.axaml.cs | 29 |
| PageTabs.axaml.cs | 133 |
| PalettePanel.axaml.cs | 246 |
| PropertyPanel.axaml.cs | 80 |
| RichTextEditor.axaml.cs | 181 |
| ShapeLibraryPanel.axaml.cs | 88 |
| ShapePreview.cs | 44 |
| SidecarRecoveryDialog.axaml.cs | 25 |
| StatusBar.axaml.cs | 72 |
| TemplatePanel.axaml.cs | 123 |
| TextPresetPanel.axaml.cs | 233 |
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
| TextEditSession.cs | 145 |
| MainWindow.axaml.cs | 351 |
| Program.cs | 78 |
| ShapeGeometryRenderer.cs | 88 |
| SampleDiagram.cs | 80 |
| SelfTest.cs | 224 |
| ConstraintEditorBinder.cs | 117 |
| ContextEntries.cs | 53 |
| DiagramSession.cs | 1330 |
| DocumentFile.cs | 43 |
| DocumentLaunch.cs | 76 |
| ErrorPresenter.cs | 41 |
| ErrorPresenterTable.cs | 80 |
| FieldEditBinder.cs | 271 |
| MenuEntries.cs | 185 |
| MenuRegistry.cs | 101 |
| TemplateCatalog.cs | 73 |
| WorkspaceRegistry.cs | 94 |
| CanvasViewModel.cs | 296 |
| ConstraintEditorViewModel.cs | 165 |
| DiagnosticsViewModel.cs | 155 |
| LayerPanelViewModel.cs | 226 |
| LayerRowViewModel.cs | 86 |
| PageTabViewModel.cs | 41 |
| PageTabsViewModel.cs | 99 |
| PalettePanelViewModel.cs | 210 |
| PropertyFieldCatalog.cs | 132 |
| PropertyFieldViewModel.cs | 104 |
| PropertyPanelViewModel.cs | 232 |
| PropertySectionViewModel.cs | 35 |
| RenderModeViewModel.cs | 33 |
| RichTextEditorViewModel.cs | 184 |
| ShapeLibraryPanelViewModel.cs | 123 |
| StatusBarViewModel.cs | 81 |
| TemplatePanelViewModel.cs | 130 |
| TextPresetPanelViewModel.cs | 210 |

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
| ApplyTextPresetCommand.cs | 177 |
| AssignLayerCommand.cs | 149 |
| ConnectEdgeCommand.cs | 82 |
| CreateCompositeCommand.cs | 150 |
| CreateLayerCommand.cs | 68 |
| CreatePageCommand.cs | 68 |
| DefinePaletteEntryCommand.cs | 88 |
| DefineTextPresetCommand.cs | 97 |
| DeletePageCommand.cs | 77 |
| DisconnectEdgeCommand.cs | 81 |
| DissolveCompositeCommand.cs | 106 |
| InsertTemplateCommand.cs | 130 |
| MoveIntoCompositeCommand.cs | 93 |
| ReconnectEdgeCommand.cs | 130 |
| RemoveActionCommand.cs | 69 |
| RemoveLayoutConstraintCommand.cs | 107 |
| RemoveNodeCommand.cs | 126 |
| RemovePaletteEntryCommand.cs | 108 |
| RemoveTagCommand.cs | 69 |
| RemoveTextPresetCommand.cs | 94 |
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
| SetRichLabelCommand.cs | 95 |
| SetSpacingCommand.cs | 89 |
| UpdatePaletteEntryCommand.cs | 74 |
| UpdateTextPresetCommand.cs | 105 |
| ChangeContext.cs | 20 |
| ChangeSource.cs | 11 |
| CommandError.cs | 7 |
| CommandMemento.cs | 132 |
| CommandResult.cs | 58 |
| CompositeMembership.cs | 159 |
| DiagramCommandBase.cs | 35 |
| EdgeFieldValue.cs | 97 |
| ErrorCodes.cs | 54 |
| FieldChange.cs | 17 |
| IDiagramCommand.cs | 12 |
| ISessionProvider.cs | 16 |
| LayerAccess.cs | 92 |
| NodeFieldValue.cs | 385 |
| PageAccess.cs | 36 |
| PaletteFieldValue.cs | 73 |
| SessionIds.cs | 8 |
| TextPresetFieldValue.cs | 109 |
| TextPresetMemento.cs | 11 |
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
| BitmapExport.cs | 6 |
| ChangeConflict.cs | 72 |
| CollectionEquality.cs | 104 |
| Composites.cs | 49 |
| DefinitionCollection.cs | 36 |
| DiagramDocument.cs | 332 |
| DiagramEnums.cs | 99 |
| DiagramValidator.cs | 440 |
| DroppedFeature.cs | 2 |
| EdgeDef.cs | 15 |
| FieldRegistry.cs | 215 |
| IDefinition.cs | 5 |
| LayoutConstraintSpec.cs | 44 |
| LayoutHints.cs | 91 |
| MathSyntax.cs | 345 |
| NodeDef.cs | 68 |
| PageAndLayer.cs | 15 |
| PageMembership.cs | 91 |
| Palette.cs | 48 |
| PdfExport.cs | 5 |
| RichTextContent.cs | 193 |
| Styles.cs | 53 |
| SupportingDefs.cs | 62 |
| SvgExport.cs | 2 |
| ValidationIssue.cs | 10 |
| DiagramHashing.cs | 176 |
| DiagramJsonContext.cs | 74 |
| DiagramSerializer.cs | 54 |
| BuiltinShapeProvider.cs | 46 |
| IShapeProvider.cs | 5 |
| PathParser.cs | 282 |
| PathShape.cs | 17 |
| ShapeDefinition.cs | 6 |
| ShapeGeometry.cs | 73 |
| ShapeRegistry.cs | 41 |
| LayoutSidecar.cs | 44 |
| SidecarBackup.cs | 132 |
| SidecarPaths.cs | 55 |
| SidecarStore.cs | 123 |
| UserSidecar.cs | 48 |
| TemplateDocument.cs | 159 |
| TemplateInstantiator.cs | 252 |
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
| Harness.cs | 278 |
| IrExtensionTests.cs | 202 |
| IrFixtures.cs | 176 |
| LayerAssignmentTests.cs | 162 |
| LayerCommandTests.cs | 180 |
| LayerVisibilityTests.cs | 191 |
| LayoutCommandTests.cs | 318 |
| LayoutConstraintTests.cs | 307 |
| MementoRegistrationTests.cs | 66 |
| NestedExecuteTests.cs | 81 |
| NodeFieldTests.cs | 318 |
| PageMembershipTests.cs | 228 |
| PageTagActionCommandTests.cs | 298 |
| PaletteCommandTests.cs | 256 |
| PathShapeTests.cs | 257 |
| PermissionTests.cs | 105 |
| RichTextTests.cs | 396 |
| RoundTripTests.cs | 186 |
| SessionIdResolutionTests.cs | 57 |
| ShapeRegistryTests.cs | 122 |
| SidecarBackupTests.cs | 245 |
| SidecarTests.cs | 301 |
| SoftLockTests.cs | 174 |
| TempDirectory.cs | 22 |
| TemplateTests.cs | 432 |
| TestCommands.cs | 109 |
| TextPresetCommandTests.cs | 368 |
| UndoStressTests.cs | 77 |
| ValidatorTests.cs | 377 |
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
| CompositeDragTests.cs | 158 |
| ConnectTests.cs | 86 |
| ConstraintEditorTests.cs | 299 |
| ContextMenuTests.cs | 249 |
| DiagnosticsPanelTests.cs | 151 |
| DragTests.cs | 121 |
| EdgeEditTests.cs | 96 |
| ErrorPresentationTests.cs | 208 |
| HeadlessFixture.cs | 180 |
| HighlightTests.cs | 70 |
| LayerPanelTests.cs | 357 |
| LayerVisibilityTests.cs | 154 |
| LayoutFailureTests.cs | 217 |
| MarqueeTests.cs | 269 |
| MenuBarTests.cs | 131 |
| ModeSwitchTests.cs | 209 |
| MultiWindowTests.cs | 259 |
| PageTabsTests.cs | 233 |
| PalettePanelTests.cs | 171 |
| PropertyPanelTests.cs | 167 |
| RichTextEditorTests.cs | 344 |
| ShapeLibraryTests.cs | 112 |
| TemplateTests.cs | 197 |
| TextPresetTests.cs | 189 |
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
| DiagramSummary.cs | 51 |
| SummaryBuilder.cs | 91 |
| SummaryFormatter.cs | 119 |
| ErrorEnvelope.cs | 53 |
| ErrorLoop.cs | 68 |
| RepairHints.cs | 240 |
| ActionDispatch.cs | 175 |
| CompositeTool.cs | 64 |
| DiagramToolContext.cs | 27 |
| DiagramToolset.cs | 169 |
| EditTool.cs | 290 |
| ExportTool.cs | 121 |
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
| ExportToolTests.cs | 432 |
| Harness.cs | 92 |
| HistoryToolTests.cs | 133 |
| LayerPermissionTests.cs | 98 |
| LayoutToolTests.cs | 304 |
| RepairHintTests.cs | 147 |
| RichTextSummaryTests.cs | 147 |
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
| DiagramMcpServer.cs | 94 |
| DocumentRendering.cs | 51 |
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
| StdioTests.cs | 234 |

### DuetDiagram.Mermaid

| 文件 | 代码 |
|---|---:|
| ExportOptions.cs | 5 |
| ExportReport.cs | 7 |
| MermaidExporter.cs | 371 |
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
| ExporterTests.cs | 317 |
| ImporterTests.cs | 329 |
| LexerTests.cs | 294 |
| ParserTests.cs | 407 |
| RoundTripTests.cs | 194 |

### DuetDiagram.Render

| 文件 | 代码 |
|---|---:|
| CompositeFrame.cs | 217 |
| CullingIndex.cs | 72 |
| CullingPolicy.cs | 19 |
| DiagnosticsFrame.cs | 14 |
| DiagnosticsSampler.cs | 107 |
| DrawCommand.cs | 91 |
| DrawList.cs | 64 |
| BitmapExporter.cs | 60 |
| BitmapOptions.cs | 15 |
| CanvasPainter.cs | 361 |
| PdfExporter.cs | 77 |
| PdfOptions.cs | 12 |
| SvgExporter.cs | 266 |
| SvgOptions.cs | 6 |
| Highlight.cs | 181 |
| HitTester.cs | 113 |
| ITextMeasurer.cs | 6 |
| LayerPlan.cs | 87 |
| MathLayout.cs | 183 |
| MathTypesetter.cs | 45 |
| ModeSwitch.cs | 44 |
| QuadTree.cs | 260 |
| RenderMode.cs | 6 |
| RichTextLayout.cs | 190 |
| SceneBuilder.cs | 490 |
| SceneComposer.cs | 55 |
| SkiaTextMeasurer.cs | 77 |
| SpatialRect.cs | 29 |
| TextLayout.cs | 78 |
| Theme.cs | 139 |
| Viewport.cs | 49 |
| ViewportCulling.cs | 6 |
| ViewportTransform.cs | 32 |

### DuetDiagram.Render.Tests

| 文件 | 代码 |
|---|---:|
| BitmapExportTests.cs | 359 |
| CompositeFrameTests.cs | 106 |
| CullingPolicyTests.cs | 216 |
| CustomShapeTests.cs | 98 |
| DiagnosticsSamplerTests.cs | 199 |
| DrawListTests.cs | 237 |
| FakeTextMeasurer.cs | 22 |
| HighlightTests.cs | 143 |
| HitTesterTests.cs | 104 |
| LayerRenderTests.cs | 201 |
| Layouts.cs | 59 |
| MathSnapshotTests.cs | 85 |
| MathTypesettingTests.cs | 258 |
| ModeSwitchTests.cs | 113 |
| PageRenderTests.cs | 117 |
| PdfExportTests.cs | 355 |
| QuadTreeTests.cs | 285 |
| RichTextLayoutTests.cs | 297 |
| RichTextRenderTests.cs | 117 |
| SceneSnapshotTests.cs | 251 |
| ShapeProviderTests.cs | 86 |
| Snapshot.cs | 57 |
| SvgExportTests.cs | 248 |
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
| Candidates.cs | 137 |
| Facts.cs | 68 |
| Program.cs | 12 |
| Analysis.cs | 381 |
| Checks.cs | 181 |
| Program.cs | 75 |
| Statistics.cs | 428 |
| Study.cs | 111 |
| Template.cs | 132 |

