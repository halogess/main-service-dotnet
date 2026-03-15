# Test Code Audit 2026-03-15

## Summary
- Baseline before cleanup: `dotnet test Tests\Tests.csproj --no-restore` passed `32/32`.
- Stage 1 cleanup only removes dead-certain local artifacts. No active tests, test data, runtime endpoints, or manual debug harnesses were deleted.
- `Tests/Tests.csproj` is active but not integrated into `main-service-dotnet.sln`. That is a workflow gap, not dead code.
- No `orphan/dead` test code was removed in this pass beyond generated artifacts.

## Active assets kept
| Path | Label | Evidence | Risk if removed | Recommendation |
| --- | --- | --- | --- | --- |
| `Tests/Tests.csproj` | aktif | `dotnet test` discovered and ran 32 tests; project references `main-service.csproj`; project is not in solution | High | Keep; consider adding to solution or CI separately |
| `Tests/TestData/Docx/bab2.docx` | aktif | Referenced by `Tests/DocxExtractionServiceTests.cs` and `Tests/SectionExtractionTests.cs`; copied by `Tests/Tests.csproj` content rule | High | Keep |

## Artifacts removed in stage 1
| Path | Label | Evidence | Risk if removed | Recommendation |
| --- | --- | --- | --- | --- |
| `TestResults/` | artefak | Contains tracked `.trx` test output only | Low | Removed and ignored |
| `Tests/TestResults/` | artefak | Contains tracked `.trx` test output only | Low | Removed and ignored |
| `test_output.txt` | artefak | Captured `dotnet test` output; no code references | Low | Removed and ignored |
| `build_output.txt` | artefak | Captured build output; no code references | Low | Removed and ignored |
| `build_log.txt` | artefak | Captured build output; no code references | Low | Removed and ignored |
| `DebugElementCount/consistency_check.txt` | artefak | Generated debug output; no code references | Low | Removed and ignored |
| `DebugElementCount/identity_check.txt` | artefak | Generated debug output; no code references | Low | Removed and ignored |
| `DebugElementCount/output.txt` | artefak | Generated debug output; no code references | Low | Removed and ignored |
| `DebugSections/test_output.txt` | artefak | Generated debug output; no code references | Low | Removed and ignored |

## Manual harnesses retained for verification
| Path | Label | Evidence | Risk if removed | Recommendation |
| --- | --- | --- | --- | --- |
| `Controllers/TestingController.cs` | harness manual | Public runtime endpoint surface under `/api/Testing/*`; no source-level callers required for ASP.NET route reachability | Medium to High | Keep for now; gate or remove only with explicit confirmation |
| `DebugElementCount/` | harness manual | Standalone console project, excluded from `main-service.csproj`, not in solution | Medium | Keep pending manual verification |
| `DebugFontResolution/` | harness manual | Standalone console project, excluded from `main-service.csproj`, not in solution | Medium | Keep pending manual verification |
| `DebugSections/` | harness manual | Standalone console project, excluded from `main-service.csproj`, not in solution | Medium | Keep pending manual verification |
| `DebugShapes/` | harness manual | Standalone console project, excluded from `main-service.csproj`, not in solution | Medium | Keep pending manual verification |
| `DebugTableExtraction/` | harness manual | Standalone console project, excluded from `main-service.csproj`, not in solution | Medium | Keep pending manual verification |
| `tmp_check_bab_detail_counts/` | harness manual | Standalone console project outside normal build/solution flow | Medium | Keep pending manual verification |
| `tmp_db_inspect/` | harness manual | Standalone console project outside normal build/solution flow | Medium | Keep pending manual verification |
| `tmp_doc602_loccheck/` | harness manual | Standalone console project outside normal build/solution flow | Medium | Keep pending manual verification |
| `tmp_page601_check/` | harness manual | Standalone console project outside normal build/solution flow | Medium | Keep pending manual verification |
| `tmp_querydoc/` | harness manual | Standalone console project outside normal build/solution flow; currently modified in workspace | High | Do not auto-clean; verify ownership and usage first |

## Workspace items retained without automatic cleanup
| Path | Label | Evidence | Risk if removed | Recommendation |
| --- | --- | --- | --- | --- |
| `Tests/ParagraphExtractorNumberingContinuationTests.cs` | aktif | Tracked xUnit test file; currently modified; test suite still passes | High | Keep untouched |
| `Tests/AturanControllerExportTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `Tests/AturanControllerNormalizationTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `Tests/AturanDetailJsonNormalizerTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `Tests/AturanExcelExportBuilderTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `Tests/ControllerTestHelpers.cs` | aktif | Untracked helper referenced by active controller-related tests | High | Keep untouched |
| `Tests/NomorHalamanNormalizationTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `Tests/RulesControllerNormalizationTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `Tests/TitleParagraphIndentationRuleTests.cs` | aktif | Untracked xUnit test file contributing to current 32-test baseline | High | Keep untouched |
| `tmp_build_verify/` | harness manual | Untracked workspace utility directory | Medium | Keep untouched pending explicit decision |
| `tmp_doc690_chartcheck/` | harness manual | Untracked workspace utility directory | Medium | Keep untouched pending explicit decision |
| `tmp_prompt_eval_20260302/` | harness manual | Untracked workspace utility directory with current IDE activity | High | Keep untouched pending explicit decision |

## Follow-up outside stage 1
- Decide whether `Tests/Tests.csproj` should be added to `main-service-dotnet.sln` or CI.
- Decide whether `/api/Testing/*` should be gated by environment or removed.
- Review manual harness directories one by one before any stage 2 deletion.
