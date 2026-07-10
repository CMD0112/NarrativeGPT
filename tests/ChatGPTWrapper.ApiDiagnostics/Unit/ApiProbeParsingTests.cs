using System.Collections;
using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ApiProbeParsingTests
{
    [Fact]
    public void ParseProbeResult_maps_success_payload()
    {
        var msg = new ApiBridgeMessage(
            """
            {
              "type": "apiResult",
              "ok": true,
              "status": 200,
              "json": {
                "itemCount": 3,
                "jsonKeys": ["items"],
                "hasDeviceId": true,
                "hasAccountId": true,
                "authenticated": true
              }
            }
            """);

        var result = ChatGptProjectApiService.ParseProbeResult(msg);

        Assert.True(result.Ok);
        Assert.Equal(200, result.Status);
        Assert.Equal(3, result.ItemCount);
        Assert.Contains("itemCount", result.JsonKeys);
        Assert.True(result.HasDeviceId);
        Assert.True(result.HasAccountId);
        Assert.True(result.Authenticated);
    }

    [Fact]
    public void ParseProbeResult_maps_api_error()
    {
        var msg = new ApiBridgeMessage(
            """
            {
              "type": "apiError",
              "ok": false,
              "error": "missing_oai_device_id",
              "message": "Sign in and refresh"
            }
            """);

        var result = ChatGptProjectApiService.ParseProbeResult(msg);

        Assert.False(result.Ok);
        Assert.Equal("missing_oai_device_id", result.Error);
        Assert.Empty(result.JsonKeys);
        Assert.Null(result.ItemCount);
    }

    [Fact]
    public void ParseProbeResult_handles_missing_json()
    {
        var msg = new ApiBridgeMessage("""{"type":"apiResult","ok":true,"status":200}""");

        var result = ChatGptProjectApiService.ParseProbeResult(msg);

        Assert.True(result.Ok);
        Assert.Empty(result.JsonKeys);
        Assert.Null(result.ItemCount);
    }

    [Fact]
    public void AdaptPreferredAttachPath_rewrites_stored_project_id()
    {
        var adapted = ChatGptProjectApiService.AdaptPreferredAttachPath(
            "/backend-api/projects/gizmo-old-id/files",
            "gizmo-new-id");

        Assert.Equal("/backend-api/projects/gizmo-new-id/files", adapted);
    }

    [Fact]
    public void AdaptPreferredAttachPath_returns_null_for_non_project_paths()
    {
        var adapted = ChatGptProjectApiService.AdaptPreferredAttachPath(
            "/backend-api/gizmos/gizmo-old-id/files",
            "gizmo-new-id");

        Assert.Null(adapted);
    }

    [Fact]
    public void ResolveUploadUseCase_uses_my_files_for_markdown()
    {
        Assert.Equal("my_files", ChatGptProjectApiService.ResolveUploadUseCase("text/markdown"));
        Assert.Equal("multimodal", ChatGptProjectApiService.ResolveUploadUseCase("image/png"));
    }

    [Fact]
    public void ResolveProjectSourceUploadUseCase_uses_ace_upload_for_markdown()
    {
        Assert.Equal("ace_upload", ChatGptProjectApiService.ResolveProjectSourceUploadUseCase("text/markdown"));
        Assert.Equal("multimodal", ChatGptProjectApiService.ResolveProjectSourceUploadUseCase("image/png"));
    }

    [Fact]
    public void ResolveUploadedProjectFileLocation_prefers_library_sediment()
    {
        using var doc = JsonDocument.Parse("""{"libraryUpload":true,"fileId":"file-abc"}""");
        var msg = new ApiBridgeMessage(doc.RootElement.GetRawText());

        Assert.Equal(
            "sediment",
            ChatGptProjectApiService.ResolveUploadedProjectFileLocation(msg, "ace_upload"));
    }

    [Fact]
    public void BuildUpsertFileEntry_includes_name_and_fs_location_for_markdown()
    {
        var entry = ChatGptProjectApiService.BuildUpsertFileEntry(new GizmoFileRef
        {
            FileId = "file-abc123",
            Name = "sources/world.md",
            Location = ChatGptProjectApiService.ResolveUploadFileLocation("my_files"),
        });

        Assert.Equal("file-abc123", entry["file_id"]);
        Assert.Equal("sources/world.md", entry["name"]);
        Assert.Equal("fs", entry["location"]);
    }

    [Fact]
    public void ResolveUploadFileLocation_uses_sediment_for_ace_upload()
    {
        Assert.Equal("fs", ChatGptProjectApiService.ResolveUploadFileLocation("my_files"));
        Assert.Equal("sediment", ChatGptProjectApiService.ResolveUploadFileLocation("ace_upload"));
        Assert.Equal("sediment", ChatGptProjectApiService.ResolveUploadFileLocation("gizmo"));
    }

    [Fact]
    public void NormalizeUpsertFileLocation_maps_uri_to_storage_enum()
    {
        Assert.Equal("fs", ChatGptProjectApiService.NormalizeUpsertFileLocation("file-service://file-legacy"));
        Assert.Equal("sediment", ChatGptProjectApiService.NormalizeUpsertFileLocation("sediment://file-legacy"));
        Assert.Equal("fs", ChatGptProjectApiService.NormalizeUpsertFileLocation(null));
    }

    [Fact]
    public void IsSnorlaxProjectId_detects_g_p_prefix()
    {
        Assert.True(ChatGptProjectApiService.IsSnorlaxProjectId("g-p-6a220fab2eb48191a75b9d88d85a3d91"));
        Assert.False(ChatGptProjectApiService.IsSnorlaxProjectId("g-abc123"));
    }

    [Fact]
    public void BuildUpsertBody_omits_id_when_gizmoId_null()
    {
        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBody(
            null,
            "My Adventure",
            "",
            null);

        Assert.False(body.ContainsKey("id"));
    }

    [Fact]
    public void BuildUpsertBody_includes_id_for_existing_project()
    {
        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBody(
            "g-p-6a220fab2eb48191a75b9d88d85a3d91",
            "Campaign",
            "",
            null);

        Assert.Equal("g-p-6a220fab2eb48191a75b9d88d85a3d91", body["id"]);
    }

    [Fact]
    public void ClassifyOutcome_marks_id_mismatch_when_response_id_differs()
    {
        Assert.Equal(
            ProjectUpsertOutcome.IdMismatch,
            ProjectUpsertAudit.ClassifyOutcome(
                ProjectUpsertIntent.AttachFiles,
                "g-p-6a220fab2eb48191a75b9d88d85a3d91",
                "g-p-6a23cf7326f0819187f7ebb5f1ec10e2"));
    }

    [Fact]
    public void ClassifyOutcome_empty_attach_response_is_unresolved_not_updated()
    {
        Assert.Equal(
            ProjectUpsertOutcome.Unresolved,
            ProjectUpsertAudit.ClassifyOutcome(
                ProjectUpsertIntent.AttachFiles,
                "g-p-linked",
                null));
    }

    [Fact]
    public void TryExtractUpsertGizmoId_reads_resource_gizmo_id()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "resource": {
                "gizmo": {
                  "id": "g-p-from-resource",
                  "display": { "name": "test" }
                }
              }
            }
            """);

        Assert.Equal("g-p-from-resource", GizmoResponseParser.TryExtractUpsertGizmoId(doc.RootElement));
    }

    [Fact]
    public void TryExtractUpsertGizmoId_reads_nested_resource_gizmo_gizmo_id()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "resource": {
                "gizmo": {
                  "gizmo": {
                    "id": "g-p-nested",
                    "display": { "name": "test" }
                  }
                }
              }
            }
            """);

        Assert.Equal("g-p-nested", GizmoResponseParser.TryExtractUpsertGizmoId(doc.RootElement));
    }

    [Fact]
    public void CanProceedAfterSnorlaxAttachUpsert_accepts_updated_outcome()
    {
        var result = new ProjectUpsertResult
        {
            Outcome = ProjectUpsertOutcome.Updated,
            Message = new ApiBridgeMessage("""{"type":"apiResult","ok":true,"status":200}"""),
        };

        Assert.True(ChatGptProjectApiService.CanProceedAfterSnorlaxAttachUpsert(
            result,
            "g-p-linked",
            [],
            [],
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void CanProceedAfterSnorlaxAttachUpsert_rejects_unresolved_without_sidebar_files()
    {
        var result = new ProjectUpsertResult
        {
            Outcome = ProjectUpsertOutcome.Unresolved,
            Message = new ApiBridgeMessage("""{"type":"apiResult","ok":true,"status":200}"""),
        };

        var baseline = new HashSet<string>(StringComparer.Ordinal) { "g-p-linked" };
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "file-a", Name = "a.md" }],
            },
        };

        Assert.False(ChatGptProjectApiService.CanProceedAfterSnorlaxAttachUpsert(
            result,
            "g-p-linked",
            [
                new GizmoFileRef { FileId = "file-a", Name = "a.md" },
                new GizmoFileRef { FileId = "file-b", Name = "b.md" },
            ],
            sidebar,
            baseline));
    }

    [Fact]
    public void CanProceedAfterSnorlaxAttachUpsert_accepts_unresolved_when_sidebar_has_all_files_and_no_forks()
    {
        var result = new ProjectUpsertResult
        {
            Outcome = ProjectUpsertOutcome.Unresolved,
            Message = new ApiBridgeMessage("""{"type":"apiResult","ok":true,"status":200}"""),
        };

        var baseline = new HashSet<string>(StringComparer.Ordinal) { "g-p-linked" };
        var merged = new List<GizmoFileRef>
        {
            new() { FileId = "file-a", Name = "a.md" },
            new() { FileId = "file-b", Name = "b.md" },
        };
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files = merged,
            },
        };

        Assert.True(ChatGptProjectApiService.CanProceedAfterSnorlaxAttachUpsert(
            result,
            "g-p-linked",
            merged,
            sidebar,
            baseline));
    }

    [Fact]
    public void CanProceedAfterSnorlaxAttachUpsert_rejects_unresolved_when_new_fork_appears()
    {
        var result = new ProjectUpsertResult
        {
            Outcome = ProjectUpsertOutcome.Unresolved,
            Message = new ApiBridgeMessage("""{"type":"apiResult","ok":true,"status":200}"""),
        };

        var baseline = new HashSet<string>(StringComparer.Ordinal) { "g-p-linked" };
        var sidebar = new List<GizmoSummary>
        {
            new() { Id = "g-p-linked", Title = "test", Files = [] },
            new() { Id = "g-p-fork", Title = "test", Files = [] },
        };

        Assert.False(ChatGptProjectApiService.CanProceedAfterSnorlaxAttachUpsert(
            result,
            "g-p-linked",
            [new GizmoFileRef { FileId = "file-a", Name = "a.md" }],
            sidebar,
            baseline));
    }

    [Fact]
    public void CanProceedAfterSnorlaxAttachUpsert_rejects_unresolved_non_200()
    {
        var result = new ProjectUpsertResult
        {
            Outcome = ProjectUpsertOutcome.Unresolved,
            Message = new ApiBridgeMessage("""{"type":"apiResult","ok":false,"status":422}"""),
        };

        Assert.False(ChatGptProjectApiService.CanProceedAfterSnorlaxAttachUpsert(
            result,
            "g-p-linked",
            [],
            [],
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void EvaluatePostAttachSidebarSnapshot_strict_mode_throws_on_any_new_fork()
    {
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "g-p-linked" };
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "file-a", Name = "a.md" }],
            },
            new()
            {
                Id = "g-p-fork",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "file-a", Name = "a.md" }],
            },
        };

        var ex = Assert.Throws<ChatGptApiException>(() =>
            ChatGptProjectApiService.EvaluatePostAttachSidebarSnapshot(
                baseline,
                sidebar,
                "g-p-linked",
                [new GizmoFileRef { FileId = "file-a", Name = "a.md" }],
                strictNoNewProjects: true));

        Assert.Contains("upsert_forked_duplicate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeUpsertDisplay_strips_unknown_keys()
    {
        var sanitized = (Dictionary<string, object?>)ChatGptProjectApiService.SanitizeUpsertDisplay(
            new Dictionary<string, object?>
            {
                ["name"] = "test",
                ["description"] = "desc",
                ["prompt_starters"] = Array.Empty<object>(),
                ["profile_picture_url"] = "https://example.com/p.png",
                ["theme"] = "default",
            });

        Assert.Equal(3, sanitized.Count);
        Assert.Equal("test", sanitized["name"]);
        Assert.False(sanitized.ContainsKey("profile_picture_url"));
        Assert.False(sanitized.ContainsKey("theme"));
    }

    [Fact]
    public void ShouldVerifyAfterApply_skips_when_apply_failed()
    {
        Assert.False(ProjectFileSyncOrchestrator.ShouldVerifyAfterApply(
            new ProjectSourceSyncResult { Success = false, Error = "attach_failed" }));
    }

    [Fact]
    public void ShouldVerifyAfterApply_runs_when_apply_succeeded()
    {
        Assert.True(ProjectFileSyncOrchestrator.ShouldVerifyAfterApply(
            new ProjectSourceSyncResult { Success = true }));
    }

    [Fact]
    public void SidebarProjectContainsAllFiles_true_when_linked_project_has_all_ids()
    {
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files =
                [
                    new GizmoFileRef { FileId = "file-a", Name = "a.md" },
                    new GizmoFileRef { FileId = "file-b", Name = "b.md" },
                ],
            },
        };

        Assert.True(ChatGptProjectApiService.SidebarProjectContainsAllFiles(
            sidebar,
            "g-p-linked",
            [
                new GizmoFileRef { FileId = "file-a", Name = "a.md" },
                new GizmoFileRef { FileId = "file-b", Name = "b.md" },
            ]));
    }

    [Fact]
    public void ApplyUpsertFileLocation_sets_location_on_all_files()
    {
        var merged = new List<GizmoFileRef>
        {
            new() { FileId = "file-a", Name = "a.md", Location = "fs" },
            new() { FileId = "file-b", Name = "b.md", Location = "sediment" },
        };

        var sediment = ChatGptProjectApiService.ApplyUpsertFileLocation(merged, "sediment");

        Assert.All(sediment, f => Assert.Equal("sediment", f.Location));
    }

    [Fact]
    public void SyncAttachFileLocationCandidates_try_fs_before_sediment()
    {
        var candidates = ChatGptProjectApiService.UpsertFileLocationCandidatesForSyncAttach([]);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("fs", candidates[0]);
        Assert.Equal("sediment", candidates[1]);
    }

    [Fact]
    public void MergeDetailFilesWithUploads_preserves_existing_locations()
    {
        var detailFiles = new List<GizmoFileRef>
        {
            new() { FileId = "file-existing", Name = "remote.txt", Location = "sediment" },
        };
        var uploads = new List<GizmoFileRef>
        {
            new() { FileId = "file-new", Name = "scenario.md" },
        };

        var merged = ChatGptProjectApiService.MergeDetailFilesWithUploads(detailFiles, uploads);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, f => f.FileId == "file-existing" && f.Location == "sediment");
        Assert.Contains(merged, f => f.FileId == "file-new" && f.Location == "sediment");
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_uses_detail_file_locations_for_new_files()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "instructions": "keep",
                  "display": { "name": "test", "description": "", "prompt_starters": [] },
                  "sharing": [{ "type": "private", "capabilities": { "can_read": true } }],
                  "tools": [],
                  "files": [
                    { "file_id": "file-existing", "name": "remote.txt", "location": "sediment" }
                  ]
                }
              }
            }
            """);

        var detailFiles = GizmoResponseParser.CollectFileRefsDeep(doc.RootElement);
        var merged = ChatGptProjectApiService.MergeDetailFilesWithUploads(
            detailFiles,
            [new GizmoFileRef { FileId = "file-new", Name = "scenario.md" }]);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            merged);

        var files = Assert.IsAssignableFrom<object[]>(body["files"]);
        Assert.Equal(2, files.Length);
        var newEntry = Assert.IsType<Dictionary<string, object?>>(files[1]);
        Assert.Equal("file-new", newEntry["file_id"]);
        Assert.Equal("sediment", newEntry["location"]);
    }

    [Fact]
    public void FilterDetailFilesExcluding_preserves_locations_and_skips_removed_ids()
    {
        var detailFiles = new List<GizmoFileRef>
        {
            new() { FileId = "keep-1", Name = "story-cards.md", Location = "sediment" },
            new() { FileId = "remove-1", Name = "scenario.md", Location = "sediment" },
            new() { FileId = "keep-2", Name = "plot.md", Location = "context" },
        };

        var remaining = ChatGptProjectApiService.FilterDetailFilesExcluding(
            detailFiles,
            new HashSet<string>(StringComparer.Ordinal) { "remove-1" });

        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, f => f.FileId == "keep-1" && f.Location == "sediment");
        Assert.Contains(remaining, f => f.FileId == "keep-2" && f.Location == "fs");
        Assert.DoesNotContain(remaining, f => f.FileId == "remove-1");
    }

    [Fact]
    public void CanProceedAfterSnorlaxDetachUpsert_rejects_unresolved_when_removed_files_still_on_sidebar()
    {
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "g-p-linked" };
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "Test",
                Files =
                [
                    new GizmoFileRef { FileId = "keep-1", Name = "story-cards.md" },
                    new GizmoFileRef { FileId = "remove-1", Name = "scenario.md" },
                ],
            },
        };

        var result = new ProjectUpsertResult
        {
            Outcome = ProjectUpsertOutcome.Unresolved,
            Message = new ApiBridgeMessage("""{"type":"apiResult","ok":true,"status":200}"""),
        };

        Assert.False(ChatGptProjectApiService.CanProceedAfterSnorlaxDetachUpsert(
            result,
            "g-p-linked",
            new HashSet<string>(StringComparer.Ordinal) { "remove-1" },
            sidebar,
            baseline));
    }

    [Fact]
    public void ClassifyOutcome_marks_create_when_no_request_id()
    {
        Assert.Equal(
            ProjectUpsertOutcome.Created,
            ProjectUpsertAudit.ClassifyOutcome(ProjectUpsertIntent.Create, null, "g-p-new"));
    }

    [Fact]
    public void ClassifyOutcome_marks_updated_when_ids_match()
    {
        Assert.Equal(
            ProjectUpsertOutcome.Updated,
            ProjectUpsertAudit.ClassifyOutcome(
                ProjectUpsertIntent.AttachFiles,
                "g-p-abc",
                "g-p-abc"));
    }

    [Fact]
    public void ClassifyOutcome_marks_id_mismatch_on_attach()
    {
        Assert.Equal(
            ProjectUpsertOutcome.IdMismatch,
            ProjectUpsertAudit.ClassifyOutcome(
                ProjectUpsertIntent.AttachFiles,
                "g-p-abc",
                "g-p-xyz"));
    }

    [Fact]
    public void ShouldRetryUpsertLocationAfterAttempt_false_on_id_mismatch()
    {
        Assert.False(ChatGptProjectApiService.ShouldRetryUpsertLocationAfterAttempt(
            ProjectUpsertOutcome.IdMismatch,
            filesVisibleOnLinkedProject: false,
            hasMoreLocationCandidates: true));
    }

    [Fact]
    public void ShouldRetryUpsertLocationAfterAttempt_true_only_when_updated_and_not_visible()
    {
        Assert.True(ChatGptProjectApiService.ShouldRetryUpsertLocationAfterAttempt(
            ProjectUpsertOutcome.Updated,
            filesVisibleOnLinkedProject: false,
            hasMoreLocationCandidates: true));

        Assert.False(ChatGptProjectApiService.ShouldRetryUpsertLocationAfterAttempt(
            ProjectUpsertOutcome.Updated,
            filesVisibleOnLinkedProject: true,
            hasMoreLocationCandidates: true));

        Assert.False(ChatGptProjectApiService.ShouldRetryUpsertLocationAfterAttempt(
            ProjectUpsertOutcome.Unresolved,
            filesVisibleOnLinkedProject: false,
            hasMoreLocationCandidates: true));
    }

    [Fact]
    public void ClassifySnorlaxCanUpdateCanary_blocks_id_mismatch_and_allows_matching_updated()
    {
        Assert.False(ChatGptProjectApiService.ClassifySnorlaxCanUpdateCanary(
            ProjectUpsertOutcome.IdMismatch,
            "g-p-linked",
            "g-p-fork"));

        Assert.True(ChatGptProjectApiService.ClassifySnorlaxCanUpdateCanary(
            ProjectUpsertOutcome.Updated,
            "g-p-linked",
            "g-p-linked"));

        Assert.False(ChatGptProjectApiService.ClassifySnorlaxCanUpdateCanary(
            ProjectUpsertOutcome.Unresolved,
            "g-p-linked",
            "g-p-linked"));
    }

    [Fact]
    public void TryValidateDetailForAttachReadOnly_accepts_matching_detail()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-linked",
                  "instructions": "keep",
                  "display": { "name": "test", "description": "", "prompt_starters": [] },
                  "sharing": [{ "type": "private", "capabilities": { "can_read": true } }],
                  "tools": [],
                  "files": [
                    { "file_id": "file-existing", "name": "remote.txt", "location": "sediment" }
                  ]
                }
              }
            }
            """);

        Assert.True(ChatGptProjectApiService.TryValidateDetailForAttachReadOnly(
            doc.RootElement,
            "g-p-linked",
            out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryValidateDetailForAttachReadOnly_rejects_detail_id_mismatch()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-other",
                  "instructions": "",
                  "display": { "name": "test", "description": "", "prompt_starters": [] },
                  "sharing": [{ "type": "private", "capabilities": { "can_read": true } }],
                  "tools": []
                }
              }
            }
            """);

        Assert.False(ChatGptProjectApiService.TryValidateDetailForAttachReadOnly(
            doc.RootElement,
            "g-p-linked",
            out var error));
        Assert.Contains("g-p-linked", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProjectFilesAttachBody_uses_files_array_required_by_api()
    {
        var file = new GizmoFileRef
        {
            FileId = "file_abc",
            Name = "scenario.md",
            Location = "fs",
        };

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildProjectFilesAttachBody(file);
        var files = Assert.IsAssignableFrom<object[]>(body["files"]);
        Assert.Single(files);

        var entry = Assert.IsType<Dictionary<string, object?>>(files[0]);
        Assert.Equal("file_abc", entry["file_id"]);
        Assert.Equal("scenario.md", entry["name"]);
        Assert.Equal("fs", entry["location"]);
    }

    [Fact]
    public void BuildProjectFilesAttachBodyCandidates_prefers_files_array_first()
    {
        var file = new GizmoFileRef { FileId = "file_abc", Name = "plot.md" };
        var candidates = ChatGptProjectApiService.BuildProjectFilesAttachBodyCandidates(file);

        Assert.Equal(4, candidates.Count);
        var first = Assert.IsType<Dictionary<string, object?>>(candidates[0]);
        Assert.True(first.ContainsKey("files"));
    }

    [Fact]
    public void BuildProjectFilesAttachBodyCandidates_tries_sediment_before_fs_fallback()
    {
        var file = new GizmoFileRef
        {
            FileId = "file_abc",
            Name = "plot.md",
            Location = "sediment",
        };
        var candidates = ChatGptProjectApiService.BuildProjectFilesAttachBodyCandidates([file]);

        Assert.Equal(4, candidates.Count);
        var sedimentBody = Assert.IsType<Dictionary<string, object?>>(candidates[0]);
        var sedimentFiles = Assert.IsAssignableFrom<object[]>(sedimentBody["files"]);
        var sedimentEntry = Assert.IsType<Dictionary<string, object?>>(sedimentFiles![0]);
        Assert.Equal("sediment", sedimentEntry["location"]);
    }

    [Fact]
    public void ResolvePrimarySnorlaxAttachStrategy_prefers_project_files_api()
    {
        Assert.Equal(
            ChatGptProjectApiService.SnorlaxAttachStrategy.ProjectFilesApi,
            ChatGptProjectApiService.ResolvePrimarySnorlaxAttachStrategy());
    }

    [Fact]
    public void UpsertFileLocationCandidates_prefers_detail_file_locations()
    {
        var merged = new List<GizmoFileRef>
        {
            new() { FileId = "file-a", Name = "a.md", Location = "sediment" },
            new() { FileId = "file-b", Name = "b.md", Location = "fs" },
        };

        var candidates = ChatGptProjectApiService.UpsertFileLocationCandidates(merged).ToList();

        Assert.Equal("sediment", candidates[0]);
        Assert.Equal("fs", candidates[1]);
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void FormatOrphanForkRecoveryMessage_includes_keep_delete_and_cleanup_steps()
    {
        var message = ChatGptProjectApiService.FormatOrphanForkRecoveryMessage(
            "g-p-linked",
            "test",
            ["g-p-fork1", "g-p-fork2"]);

        Assert.Contains("Keep the linked project g-p-linked", message, StringComparison.Ordinal);
        Assert.Contains("g-p-fork1", message, StringComparison.Ordinal);
        Assert.Contains("restart ChatGPT Wrapper", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldPreferSedimentFirstForSnorlaxFallback_when_detail_infers_fs_only()
    {
        var detailFiles = new List<GizmoFileRef>
        {
            new() { FileId = "file-existing", Name = "remote.txt", Location = "fs" },
        };
        var merged = ChatGptProjectApiService.MergeDetailFilesWithUploads(
            detailFiles,
            [new GizmoFileRef { FileId = "file-new", Name = "scenario.md" }]);

        Assert.True(ChatGptProjectApiService.ShouldPreferSedimentFirstForSnorlaxFallback(detailFiles, merged));
    }

    [Fact]
    public void UpsertFileLocationCandidatesForSnorlaxFallback_orders_sediment_before_fs_when_default_fs()
    {
        var detailFiles = new List<GizmoFileRef>
        {
            new() { FileId = "file-existing", Name = "remote.txt" },
        };
        var merged = ChatGptProjectApiService.MergeDetailFilesWithUploads(
            detailFiles,
            [new GizmoFileRef { FileId = "file-new", Name = "scenario.md" }]);

        var candidates = ChatGptProjectApiService.UpsertFileLocationCandidatesForSnorlaxFallback(
            detailFiles,
            merged);

        Assert.Equal("sediment", candidates[0]);
        Assert.Equal("fs", candidates[1]);
    }

    [Fact]
    public void ReadRecentIdMismatchForkIds_returns_fork_response_ids()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), "cgw-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(auditDir);
        var auditPath = Path.Combine(auditDir, "project-upsert-audit.jsonl");
        var at = DateTimeOffset.UtcNow.ToString("O");
        var line =
            "{\"at\":\"" + at
            + "\",\"intent\":\"attachfiles\",\"caller\":\"SyncAttach\",\"requestGizmoId\":\"g-p-linked\",\"responseGizmoId\":\"g-p-fork\",\"outcome\":\"idmismatch\"}";
        File.WriteAllText(auditPath, line);

        var forks = ProjectUpsertAudit.ReadRecentIdMismatchForkIdsFromFile(
            auditPath,
            "g-p-linked",
            DateTimeOffset.UtcNow - TimeSpan.FromHours(1));

        Assert.Single(forks);
        Assert.Equal("g-p-fork", forks[0]);

        Directory.Delete(auditDir, recursive: true);
    }

    [Fact]
    public void MergeDetailFilesWithUploads_incremental_second_file_preserves_first()
    {
        var detailFiles = new List<GizmoFileRef>
        {
            new() { FileId = "file-a", Name = "scenario.md", Location = "sediment" },
        };
        var firstMerge = ChatGptProjectApiService.MergeDetailFilesWithUploads(
            detailFiles,
            [new GizmoFileRef { FileId = "file-b", Name = "plot.md" }]);

        Assert.Equal(2, firstMerge.Count);
        Assert.Contains(firstMerge, f => f.FileId == "file-a");
        Assert.Contains(firstMerge, f => f.FileId == "file-b");

        var simulatedDetail = firstMerge;
        var secondMerge = ChatGptProjectApiService.MergeDetailFilesWithUploads(
            simulatedDetail,
            [new GizmoFileRef { FileId = "file-c", Name = "instructions-snippet.md" }]);

        Assert.Equal(3, secondMerge.Count);
        Assert.Contains(secondMerge, f => f.FileId == "file-a");
        Assert.Contains(secondMerge, f => f.FileId == "file-b");
        Assert.Contains(secondMerge, f => f.FileId == "file-c");
    }

    [Fact]
    public void BlocksCreateWhenAlreadyLinked_blocks_repeat_create()
    {
        Assert.True(AdventureProjectBindingService.BlocksCreateWhenAlreadyLinked("g-p-linked"));
        Assert.False(AdventureProjectBindingService.BlocksCreateWhenAlreadyLinked("g-p-linked", allowRecreate: true));
        Assert.False(AdventureProjectBindingService.BlocksCreateWhenAlreadyLinked(null));
    }

    [Fact]
    public void ParseGizmoNode_handles_null_file_size_and_bytes()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": "g-p-test",
              "name": "Test",
              "files": [
                {
                  "file_id": "file-abc",
                  "name": "scenario.md",
                  "size": null,
                  "bytes": null,
                  "location": "fs"
                }
              ]
            }
            """);

        var summary = GizmoResponseParser.ParseGizmoNode(doc.RootElement, doc.RootElement);

        Assert.NotNull(summary);
        Assert.NotEmpty(summary!.Files);
        Assert.All(summary.Files, f => Assert.Null(f.Size));
        Assert.Contains(summary.Files, f => f.FileId == "file-abc" && f.Name == "scenario.md");
    }

    [Fact]
    public void ParseSidebarItems_skips_malformed_entries_without_throwing()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "items": [
                { "gizmo": { "gizmo": { "id": "g-p-good" }, "files": [ { "file_id": "f1", "name": "a.md", "size": null } ] } },
                { "gizmo": { "gizmo": { "id": null }, "files": [ { "file_id": "f2", "name": "b.md" } ] } }
              ]
            }
            """);

        var items = doc.RootElement.GetProperty("items");
        var parsed = GizmoResponseParser.ParseSidebarItems(items);

        Assert.Single(parsed);
        Assert.Equal("g-p-good", parsed[0].Id);
        Assert.Single(parsed[0].Files);
    }

    [Fact]
    public void GetCursorOrNull_accepts_null_and_numeric_cursors()
    {
        using var nullCursor = JsonDocument.Parse("""{"cursor": null}""");
        Assert.Null(JsonElementParsing.GetCursorOrNull(nullCursor.RootElement));

        using var numericCursor = JsonDocument.Parse("""{"cursor": 42}""");
        Assert.Equal("42", JsonElementParsing.GetCursorOrNull(numericCursor.RootElement));
    }

    [Fact]
    public void BuildUpsertAttachBody_includes_required_metadata_and_linked_id()
    {
        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertAttachBody(
            "g-p-6a220fab2eb48191a75b9d88d85a3d91",
            "test",
            "existing instructions",
            [new GizmoFileRef { FileId = "file-abc", Name = "scenario.md" }]);

        Assert.Equal("g-p-6a220fab2eb48191a75b9d88d85a3d91", body["id"]);
        Assert.Equal("existing instructions", body["instructions"]);
        Assert.True(body.ContainsKey("display"));
        Assert.True(body.ContainsKey("sharing"));
    }

    [Fact]
    public void MergeFileRefsById_deduplicates_by_file_id()
    {
        var merged = ChatGptProjectApiService.MergeFileRefsById(
            [
                new GizmoFileRef { FileId = "f1", Name = "old.md" },
            ],
            [
                new GizmoFileRef { FileId = "f1", Name = "new.md" },
                new GizmoFileRef { FileId = "f2", Name = "b.md" },
            ]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("new.md", merged.Single(f => f.FileId == "f1").Name);
        Assert.Contains(merged, f => f.FileId == "f2");
    }

    [Fact]
    public void MergeFileRefsById_combines_multiple_sources()
    {
        var merged = ChatGptProjectApiService.MergeFileRefsById(
        [
            new GizmoFileRef { FileId = "f1", Name = "a.md" },
            new GizmoFileRef { FileId = "f2", Name = "b.md" },
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void RemoteFilesContainById_requires_matching_file_id()
    {
        var remote = new List<GizmoFileRef>
        {
            new() { FileId = "file-abc", Name = "other.md" },
        };

        Assert.True(ChatGptProjectApiService.RemoteFilesContainById(remote, "file-abc"));
        Assert.False(ChatGptProjectApiService.RemoteFilesContainById(remote, "file-xyz"));
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_preserves_metadata_from_detail_fixture()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "instructions": "keep these instructions",
                  "display": { "name": "test", "description": "", "prompt_starters": [] },
                  "sharing": [{ "type": "private", "capabilities": { "can_read": true } }],
                  "tools": []
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            [new GizmoFileRef { FileId = "f1", Name = "scenario.md" }]);

        Assert.Equal("g-p-test", body["id"]);
        Assert.Equal("keep these instructions", body["instructions"]);
        Assert.True(body.ContainsKey("display"));
        var sharingMeta = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharingMeta);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_wraps_object_sharing_as_array()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "instructions": "",
                  "display": { "name": "test", "description": "", "prompt_starters": [] },
                  "sharing": { "type": "private", "capabilities": { "can_read": true } },
                  "tools": []
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var sharing = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharing);
        var entry = Assert.IsType<Dictionary<string, object?>>(sharing[0]!);
        Assert.Equal("private", entry["type"]);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_preserves_array_sharing_with_valid_private_entry()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "sharing": [
                    { "type": "private", "capabilities": { "can_read": true } }
                  ]
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var sharing = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharing);
        var entry = Assert.IsType<Dictionary<string, object?>>(sharing[0]!);
        Assert.Equal("private", entry["type"]);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_coerces_capabilities_only_sharing_to_private_array()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "sharing": {
                    "capabilities": {
                      "can_read": true,
                      "can_view_config": false,
                      "can_write": false,
                      "can_delete": false,
                      "can_export": false,
                      "can_share": false
                    }
                  }
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var sharing = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharing);
        var entry = Assert.IsType<Dictionary<string, object?>>(sharing[0]!);
        Assert.Equal("private", entry["type"]);
        Assert.IsType<Dictionary<string, object?>>(entry["capabilities"]);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_unknown_sharing_type_falls_back_to_default_private()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "sharing": { "type": "workspace", "capabilities": { "can_read": true } }
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var sharing = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharing);
        var entry = Assert.IsType<Dictionary<string, object?>>(sharing[0]!);
        Assert.Equal("private", entry["type"]);
        var caps = Assert.IsType<Dictionary<string, object?>>(entry["capabilities"]);
        Assert.False((bool)caps["can_share"]!);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_invalid_array_sharing_falls_back_to_default_private()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "sharing": [
                    { "type": "private", "capabilities": { "can_read": true } },
                    { "type": "public", "capabilities": { "can_read": false } }
                  ]
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var sharing = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharing);
        var entry = Assert.IsType<Dictionary<string, object?>>(sharing[0]!);
        Assert.Equal("private", entry["type"]);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_missing_sharing_uses_default_private_array()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "instructions": "",
                  "display": { "name": "test", "description": "", "prompt_starters": [] }
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var sharing = Assert.IsAssignableFrom<IList>(body["sharing"]);
        Assert.Single(sharing);
        var entry = Assert.IsType<Dictionary<string, object?>>(sharing[0]!);
        Assert.Equal("private", entry["type"]);
    }

    [Fact]
    public void BuildUpsertBodyFromDetail_non_array_tools_becomes_empty_array()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "tools": { "invalid": true }
                }
              }
            }
            """);

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildUpsertBodyFromDetail(
            doc.RootElement,
            "g-p-test",
            null);

        var tools = Assert.IsType<object[]>(body["tools"]);
        Assert.Empty(tools);
    }

    [Fact]
    public void FindProjectIdsContainingFile_returns_linked_and_duplicate_owners()
    {
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "f1", Name = "a.md" }],
            },
            new()
            {
                Id = "g-p-dup",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "f2", Name = "b.md" }],
            },
        };

        var owners = ChatGptProjectApiService.FindProjectIdsContainingFile(sidebar, "f2");

        Assert.Single(owners);
        Assert.Equal("g-p-dup", owners[0]);
    }

    [Fact]
    public void BuildFileDownloadPathCandidates_fs_gizmoId_prefers_project_paths()
    {
        var paths = ChatGptApiEndpoints.BuildFileDownloadPathCandidates("file-abc", "g-p-test", "fs");

        Assert.True(paths.Count >= 9);
        Assert.Equal(
            "/backend-api/files/download/file-abc?gizmo_id=g-p-test&download_intent=true",
            paths[0]);
        Assert.Contains("gizmo_id=g-p-test", paths[0], StringComparison.Ordinal);
        Assert.Contains("/backend-api/projects/g-p-test/files/file-abc?download=1", paths);
        Assert.Contains("/backend-api/files/file-abc?download=1", paths[^2]);
        Assert.Contains("/backend-api/files/file-abc", paths[^1]);
        Assert.True(paths.ToList().IndexOf(paths[0]) < paths.ToList().IndexOf(paths[^2]));
    }

    [Fact]
    public void BuildFileDownloadPathCandidates_without_gizmo_uses_generic_only()
    {
        var paths = ChatGptApiEndpoints.BuildFileDownloadPathCandidates("file-abc", null, "sediment");

        Assert.Equal(2, paths.Count);
        Assert.Equal("/backend-api/files/file-abc?download=1", paths[0]);
        Assert.Equal("/backend-api/files/file-abc", paths[1]);
    }

    [Fact]
    public void TryExtractInlineFileContent_reads_base64_from_files_array()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "files": [
                {
                  "file_id": "file_test",
                  "name": "a.md",
                  "base64": "aGVsbG8="
                }
              ]
            }
            """);

        var bytes = GizmoResponseParser.TryExtractInlineFileContent(doc.RootElement, "file_test");

        Assert.NotNull(bytes);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public void TryExtractInlineDownloadPath_normalizes_same_origin_backend_api_url()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "files": [
                {
                  "file_id": "file_test",
                  "download_url": "https://chatgpt.com/backend-api/projects/g-p-test/files/file_test?download=1"
                }
              ]
            }
            """);

        var path = GizmoResponseParser.TryExtractInlineDownloadPath(doc.RootElement, "file_test");

        Assert.Equal("/backend-api/projects/g-p-test/files/file_test?download=1", path);
    }

    [Fact]
    public void IsRemoteFileDownloadUnavailable_detects_all_404_paths()
    {
        var ex = new ChatGptApiException(
            "download_failed 404 paths=6 attempted=/backend-api/files/f1;last=/backend-api/files/f1",
            "/backend-api/files/f1",
            404);

        Assert.True(ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex));
    }

    [Fact]
    public void IsRemoteFileDownloadUnavailable_detects_status_code_404()
    {
        var ex = new ChatGptApiException("not found", "/backend-api/files/f1", 404);
        Assert.True(ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex));
    }

    [Fact]
    public void FormatDownloadFailureMessage_marks_all_404_as_not_available()
    {
        var raw = """
            {
              "attempts": [
                {"status":404,"path":"/backend-api/projects/g-p/files/f1?download=1"},
                {"status":404,"path":"/backend-api/files/f1"}
              ],
              "path":"/backend-api/files/f1",
              "status":404
            }
            """;
        var msg = new ApiBridgeMessage(
            $$"""{"type":"apiError","ok":false,"error":"download_failed","status":404,"message":"download_failed 404 /backend-api/files/f1","detail":{{raw}}}""");

        var formatted = ChatGptProjectApiService.FormatDownloadFailureMessage(
            "download_failed 404 /backend-api/files/f1",
            msg,
            "f1",
            ChatGptApiEndpoints.BuildFileDownloadPathCandidates("f1", "g-p-test", "fs"),
            allAttemptsNotFound: true);

        Assert.StartsWith("download_not_available", formatted, StringComparison.Ordinal);
        Assert.Contains("attempted=", formatted, StringComparison.Ordinal);
        Assert.Contains("paths=10", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDownloadFailureMessage_preserves_download_stub_prefix()
    {
        var raw = """
            {
              "attempts": [
                {"status":200,"path":"/backend-api/files/download/f1?gizmo_id=g-p&inline=false&download_intent=false","stub":true,"byteLength":388},
                {"status":200,"path":"/backend-api/files/download/f1?gizmo_id=g-p&inline=false&download_intent=true","stub":true,"byteLength":388}
              ],
              "path":"/backend-api/files/download/f1?gizmo_id=g-p&inline=false&download_intent=true",
              "stubByteLength":388,
              "status":200
            }
            """;
        var msg = new ApiBridgeMessage(
            $$"""{"type":"apiError","ok":false,"error":"download_stub","status":200,"message":"download_stub 388 /backend-api/files/download/f1","detail":{{raw}}}""");

        var formatted = ChatGptProjectApiService.FormatDownloadFailureMessage(
            "download_stub 388 /backend-api/files/download/f1",
            msg,
            "f1",
            ChatGptApiEndpoints.BuildProjectScopedDownloadPathCandidates("f1", "g-p-test"),
            allAttemptsNotFound: false);

        Assert.StartsWith("download_stub", formatted, StringComparison.Ordinal);
        Assert.Contains("download_intent=true", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_attach_ownership_matches_per_file_resolution()
    {
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "linked",
                Files =
                [
                    new GizmoFileRef { FileId = "f1", Name = "a.md" },
                    new GizmoFileRef { FileId = "f3", Name = "c.md" },
                ],
            },
            new()
            {
                Id = "g-p-other",
                Title = "other",
                Files = [new GizmoFileRef { FileId = "f2", Name = "b.md" }],
            },
        };

        var fileIds = new[] { "f1", "f2", "f3", "f-missing" };
        string? batchWrongOwner = null;
        var batchAllOwns = true;
        foreach (var fileId in fileIds)
        {
            var owners = ChatGptProjectApiService.FindProjectIdsContainingFile(sidebar, fileId);
            var status = ChatGptProjectApiService.ResolveAttachFileOwnership(
                owners,
                "g-p-linked",
                out var wrongOwner);
            if (status == ChatGptProjectApiService.AttachFileOwnershipStatus.LinkedOwns)
                continue;

            batchAllOwns = false;
            if (status == ChatGptProjectApiService.AttachFileOwnershipStatus.WrongOwner)
            {
                batchWrongOwner = wrongOwner;
                break;
            }
        }

        Assert.False(batchAllOwns);
        Assert.Equal("g-p-other", batchWrongOwner);

        foreach (var fileId in fileIds)
        {
            var owners = ChatGptProjectApiService.FindProjectIdsContainingFile(sidebar, fileId);
            var status = ChatGptProjectApiService.ResolveAttachFileOwnership(
                owners,
                "g-p-linked",
                out var wrongOwner);

            if (fileId == "f2")
            {
                Assert.Equal(ChatGptProjectApiService.AttachFileOwnershipStatus.WrongOwner, status);
                Assert.Equal("g-p-other", wrongOwner);
            }
            else if (fileId is "f1" or "f3")
            {
                Assert.Equal(ChatGptProjectApiService.AttachFileOwnershipStatus.LinkedOwns, status);
            }
            else
            {
                Assert.Equal(ChatGptProjectApiService.AttachFileOwnershipStatus.NotVisible, status);
            }
        }
    }

    [Fact]
    public void ProjectSyncPreflightResult_blocked_carries_error_code()
    {
        var blocked = ProjectSyncPreflightResult.Blocked(
            "duplicate_projects_exist",
            "Delete extras",
            ["g-p-a", "g-p-b"]);

        Assert.False(blocked.Allowed);
        Assert.Equal("duplicate_projects_exist", blocked.ErrorCode);
        Assert.Equal(2, blocked.SameTitleProjectIds.Count);
    }

    [Fact]
    public void ResolveAttachFileOwnership_linked_owner_succeeds()
    {
        var status = ChatGptProjectApiService.ResolveAttachFileOwnership(
            ["g-p-linked", "g-p-other"],
            "g-p-linked",
            out var wrongOwner);

        Assert.Equal(ChatGptProjectApiService.AttachFileOwnershipStatus.LinkedOwns, status);
        Assert.Null(wrongOwner);
    }

    [Fact]
    public void ResolveAttachFileOwnership_wrong_owner_only()
    {
        var status = ChatGptProjectApiService.ResolveAttachFileOwnership(
            ["g-p-fork"],
            "g-p-linked",
            out var wrongOwner);

        Assert.Equal(ChatGptProjectApiService.AttachFileOwnershipStatus.WrongOwner, status);
        Assert.Equal("g-p-fork", wrongOwner);
    }

    [Fact]
    public void ResolveAttachFileOwnership_not_visible_when_no_owners()
    {
        var status = ChatGptProjectApiService.ResolveAttachFileOwnership(
            [],
            "g-p-linked",
            out var wrongOwner);

        Assert.Equal(ChatGptProjectApiService.AttachFileOwnershipStatus.NotVisible, status);
        Assert.Null(wrongOwner);
    }

    [Fact]
    public void ValidateAttachFileOwnershipPreflight_blocks_when_file_on_other_project()
    {
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "f1", Name = "a.md" }],
            },
            new()
            {
                Id = "g-p-fork",
                Title = "fork",
                Files = [new GizmoFileRef { FileId = "f-new", Name = "scenario.md" }],
            },
        };

        var result = ChatGptProjectApiService.ValidateAttachFileOwnershipPreflight(
            sidebar,
            "g-p-linked",
            ["f-new"]);

        Assert.False(result.Allowed);
        Assert.Equal("file_owned_by_other_project", result.ErrorCode);
        Assert.Contains("g-p-fork", result.SameTitleProjectIds);
    }

    [Fact]
    public void ValidateAttachFileOwnershipPreflight_allows_file_on_linked_project()
    {
        var sidebar = new List<GizmoSummary>
        {
            new()
            {
                Id = "g-p-linked",
                Title = "test",
                Files = [new GizmoFileRef { FileId = "f1", Name = "a.md" }],
            },
        };

        var result = ChatGptProjectApiService.ValidateAttachFileOwnershipPreflight(
            sidebar,
            "g-p-linked",
            ["f1"]);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void TryMatchRemoteFile_prefers_stored_remote_file_id()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "scenario.md",
            RemoteFileId = "file-sticky",
        };
        var remotes = new List<GizmoFileRef>
        {
            new() { FileId = "file-sticky", Name = "renamed.pdf" },
            new() { FileId = "file-other", Name = "scenario.md" },
        };

        var match = ProjectFileSyncPlanner.TryMatchRemoteFile(entry, remotes);

        Assert.NotNull(match);
        Assert.Equal("file-sticky", match!.FileId);
    }

    [Fact]
    public void TryMatchRemoteFile_matches_case_insensitive_basename()
    {
        var entry = new SourceManifestEntry { RelativePath = "scenario.md" };

        var match = ProjectFileSyncPlanner.TryMatchRemoteFile(
            entry,
            [new GizmoFileRef { FileId = "f1", Name = "folder/Scenario.MD" }]);

        Assert.NotNull(match);
        Assert.Equal("f1", match!.FileId);
    }

    [Fact]
    public void TryMatchRemoteFile_returns_null_for_unrelated_remote_name()
    {
        var entry = new SourceManifestEntry { RelativePath = "scenario.md" };

        var match = ProjectFileSyncPlanner.TryMatchRemoteFile(
            entry,
            [new GizmoFileRef { FileId = "f1", Name = "notes.pdf" }]);

        Assert.Null(match);
    }

    [Fact]
    public void CollectFileRefsDeep_finds_nested_file_objects()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": {
                  "id": "g-p-test",
                  "meta": {
                    "attachment": { "file_id": "file-nested", "name": "plot.md" }
                  }
                }
              }
            }
            """);

        var files = GizmoResponseParser.CollectFileRefsDeep(doc.RootElement);

        Assert.Single(files);
        Assert.Equal("file-nested", files[0].FileId);
        Assert.Equal("plot.md", files[0].Name);
    }

    [Fact]
    public void CollectFileRefsDeep_finds_files_under_gizmo_wrapper()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "gizmo": {
                "gizmo": { "id": "g-p-test" },
                "files": [ { "file_id": "file-wrap", "name": "instructions-snippet.md" } ]
              }
            }
            """);

        var files = GizmoResponseParser.CollectFileRefsDeep(doc.RootElement);

        Assert.Single(files);
        Assert.Equal("file-wrap", files[0].FileId);
    }

    [Fact]
    public void BuildProjectFilesAttachBody_multi_file_includes_all_entries()
    {
        var files = new[]
        {
            new GizmoFileRef { FileId = "file-a", Name = "a.md", Location = "fs" },
            new GizmoFileRef { FileId = "file-b", Name = "b.md", Location = "sediment" },
        };

        var body = (Dictionary<string, object?>)ChatGptProjectApiService.BuildProjectFilesAttachBody(files);
        var entries = Assert.IsAssignableFrom<object[]>(body["files"]);
        Assert.Equal(2, entries.Length);

        var first = Assert.IsType<Dictionary<string, object?>>(entries[0]);
        Assert.Equal("file-a", first["file_id"]);
        var second = Assert.IsType<Dictionary<string, object?>>(entries[1]);
        Assert.Equal("file-b", second["file_id"]);
    }

    [Fact]
    public void BuildProjectFilesAttachBodyCandidates_multi_file_prefers_full_then_minimal()
    {
        var files = new[]
        {
            new GizmoFileRef { FileId = "file-a", Name = "a.md" },
            new GizmoFileRef { FileId = "file-b", Name = "b.md" },
        };

        var candidates = ChatGptProjectApiService.BuildProjectFilesAttachBodyCandidates(files);

        Assert.Equal(4, candidates.Count);
        var full = Assert.IsType<Dictionary<string, object?>>(candidates[0]);
        var fullEntries = Assert.IsAssignableFrom<object[]>(full["files"]);
        Assert.Equal(2, fullEntries.Length);
        var minimal = Assert.IsType<Dictionary<string, object?>>(candidates[1]);
        Assert.True(minimal.ContainsKey("files"));
    }

    [Fact]
    public void ShouldSkipRemoteDownloadWhenBaselineMatchesLocal_skips_when_hashes_match()
    {
        Assert.True(ProjectFileSyncPlanner.ShouldSkipRemoteDownloadWhenBaselineMatchesLocal(
            "abc123",
            "abc123"));
        Assert.False(ProjectFileSyncPlanner.ShouldSkipRemoteDownloadWhenBaselineMatchesLocal(
            "abc123",
            "def456"));
        Assert.False(ProjectFileSyncPlanner.ShouldSkipRemoteDownloadWhenBaselineMatchesLocal("", "abc123"));
        Assert.False(ProjectFileSyncPlanner.ShouldSkipRemoteDownloadWhenBaselineMatchesLocal("abc123", null));
    }

    [Fact]
    public void IsPlanPreflightFresh_requires_matching_gizmo_and_recent_timestamp()
    {
        var plan = new SourceSyncPlan
        {
            PreflightPassedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            PreflightGizmoId = "g-p-test",
        };

        Assert.True(ChatGptProjectApiService.IsPlanPreflightFresh(plan, "g-p-test"));
        Assert.False(ChatGptProjectApiService.IsPlanPreflightFresh(plan, "g-p-other"));
        Assert.False(ChatGptProjectApiService.IsPlanPreflightFresh(
            new SourceSyncPlan { SyncBlocked = true, PreflightPassedAt = DateTimeOffset.UtcNow, PreflightGizmoId = "g-p-test" },
            "g-p-test"));
        Assert.False(ChatGptProjectApiService.IsPlanPreflightFresh(
            new SourceSyncPlan
            {
                PreflightPassedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                PreflightGizmoId = "g-p-test",
            },
            "g-p-test"));
    }

    [Fact]
    public void IsPlanCanaryFresh_requires_canary_flag_and_fresh_preflight()
    {
        var plan = new SourceSyncPlan
        {
            PreflightPassedAt = DateTimeOffset.UtcNow,
            PreflightGizmoId = "g-p-test",
            CanaryPassed = true,
        };

        Assert.True(ChatGptProjectApiService.IsPlanCanaryFresh(plan, "g-p-test"));
        Assert.False(ChatGptProjectApiService.IsPlanCanaryFresh(
            new SourceSyncPlan
            {
                PreflightPassedAt = DateTimeOffset.UtcNow,
                PreflightGizmoId = "g-p-test",
                CanaryPassed = false,
            },
            "g-p-test"));
    }

    [Fact]
    public void ShouldUseSnorlaxFileListFastPath_when_merged_sidebar_detail_non_empty()
    {
        Assert.True(ChatGptProjectApiService.ShouldUseSnorlaxFileListFastPath(2, 1, 3));
        Assert.False(ChatGptProjectApiService.ShouldUseSnorlaxFileListFastPath(2, 1, 0));
        Assert.False(ChatGptProjectApiService.ShouldUseSnorlaxFileListFastPath(0, 0, 1));
    }

    [Fact]
    public void ApplyUserAction_updates_planned_action_for_non_conflict_rows()
    {
        var item = new SourceSyncPlanItem
        {
            Entry = new SourceManifestEntry
            {
                RelativePath = "world.md",
                SyncState = SourceSyncState.LocalNewer,
                PlannedAction = SourceSyncAction.PushReplace,
            },
        };

        Assert.True(ProjectFileSyncPlanner.ApplyUserAction(item, SourceSyncAction.Skip));
        Assert.Equal(SourceSyncAction.Skip, ProjectFileSyncPlanner.ResolveAction(item));
    }

    [Fact]
    public void ApplyUserAction_maps_conflict_choices_to_resolution()
    {
        var item = new SourceSyncPlanItem
        {
            Entry = new SourceManifestEntry
            {
                RelativePath = "world.md",
                SyncState = SourceSyncState.Conflict,
                PlannedAction = SourceSyncAction.NeedsResolution,
            },
        };

        Assert.True(ProjectFileSyncPlanner.ApplyUserAction(item, SourceSyncAction.PushReplace));
        Assert.Equal(SourceSyncAction.PushReplace, ProjectFileSyncPlanner.ResolveAction(item));
        Assert.True(ProjectFileSyncPlanner.IsAutoSafe(item));

        Assert.True(ProjectFileSyncPlanner.ApplyUserAction(item, SourceSyncAction.Skip));
        Assert.Equal(SourceSyncAction.Skip, ProjectFileSyncPlanner.ResolveAction(item));
        Assert.False(ProjectFileSyncPlanner.IsAutoSafe(item));
    }

    [Fact]
    public void GetAvailableActions_limits_missing_remote_to_skip()
    {
        var item = new SourceSyncPlanItem
        {
            Entry = new SourceManifestEntry
            {
                RelativePath = "missing.md",
                SyncState = SourceSyncState.MissingRemote,
            },
        };

        Assert.Equal([SourceSyncAction.Skip], ProjectFileSyncPlanner.GetAvailableActions(item));
    }

    [Fact]
    public void IsLikelyApiErrorJsonPayload_detects_not_found_json()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"detail\":\"Not found.\"}");
        Assert.True(ProjectSourceIntegrityVerifier.IsLikelyApiErrorJsonPayload(bytes));
    }

    [Fact]
    public void IsLikelyApiErrorJsonPayload_ignores_real_image_bytes()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        Assert.False(ProjectSourceIntegrityVerifier.IsLikelyApiErrorJsonPayload(bytes));
    }

    [Fact]
    public void BuildProjectScopedDownloadPathCandidates_prefers_ui_download_path_first()
    {
        var paths = ChatGptApiEndpoints.BuildProjectScopedDownloadPathCandidates("file-1", "g-p-test");
        Assert.Equal(8, paths.Count);
        Assert.Equal(
            "/backend-api/files/download/file-1?gizmo_id=g-p-test&download_intent=true",
            paths[0]);
        Assert.StartsWith("/backend-api/files/download/file-1", paths[1]);
        Assert.Contains("inline=false", paths[1], StringComparison.Ordinal);
        Assert.Contains("download_intent=true", paths[1], StringComparison.Ordinal);
        Assert.Contains("gizmo_id=g-p-test", paths[0], StringComparison.Ordinal);
        Assert.All(paths, p => Assert.Contains("g-p-test", p, StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p == "/backend-api/files/file-1");
    }

    [Fact]
    public void IsLikelyDownloadRedirectEnvelope_distinguishes_estuary_success_json()
    {
        var envelope = System.Text.Encoding.UTF8.GetBytes(
            """
            {"status":"success","download_url":"https://chatgpt.com/backend-api/estuary/content?id=file_x&gizmo_id=g-p-test&ts=1&p=gpp&cid=1&sig=abc&v=0","metadata":null}
            """);
        Assert.True(ProjectSourceIntegrityVerifier.IsLikelyDownloadRedirectEnvelope(envelope));
        Assert.False(ProjectSourceIntegrityVerifier.IsLikelyDownloadMetadataJsonStub(envelope));
        Assert.False(ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(envelope));
        Assert.Equal(
            "/backend-api/estuary/content?id=file_x&gizmo_id=g-p-test&ts=1&p=gpp&cid=1&sig=abc&v=0",
            ProjectSourceIntegrityVerifier.TryExtractDownloadRedirectPath(envelope));
    }

    [Fact]
    public void IsLikelyDownloadStubPayload_detects_small_json_before_blob_ready()
    {
        var stub = System.Text.Encoding.UTF8.GetBytes("""{"file_id":"file_x","name":"test.md","size":16454}""");
        Assert.True(ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(stub, 16454));
        Assert.True(ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(stub, 100));
        Assert.True(ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(stub));
        Assert.True(ProjectSourceIntegrityVerifier.IsLikelyDownloadMetadataJsonStub(stub));
    }

    [Fact]
    public void IsLikelyDownloadMetadataJsonStub_does_not_flag_real_markdown()
    {
        var markdown = System.Text.Encoding.UTF8.GetBytes("# Title\n\nBody text.");
        Assert.False(ProjectSourceIntegrityVerifier.IsLikelyDownloadMetadataJsonStub(markdown));
        Assert.False(ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(markdown));
    }

    [Fact]
    public void ProjectSourceFileSimple_includes_gizmo_query()
    {
        var path = ChatGptApiEndpoints.ProjectSourceFileSimple("g-p-test", "file-abc");
        Assert.Equal("/backend-api/files/file-abc/simple?gizmo_id=g-p-test", path);
    }
}
