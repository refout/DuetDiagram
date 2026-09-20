# LOC 统计

> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。
> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。

| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |
|---|---:|---:|---:|---:|---:|
| DuetDiagram.AotSmokeTest | 1 | 95 | 72 | 8 | 15 |
| DuetDiagram.Core | 37 | 2424 | 1657 | 307 | 460 |
| DuetDiagram.Core.Tests | 13 | 1734 | 1350 | 61 | 323 |
| tools | 1 | 207 | 155 | 9 | 43 |
| **合计** | **52** | **4460** | **3234** | **385** | **841** |

## 明细

### DuetDiagram.AotSmokeTest

| 文件 | 代码 |
|---|---:|
| Program.cs | 72 |

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
| RemoveNodeCommand.cs | 127 |
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
| CommandBusTests.cs | 229 |
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

