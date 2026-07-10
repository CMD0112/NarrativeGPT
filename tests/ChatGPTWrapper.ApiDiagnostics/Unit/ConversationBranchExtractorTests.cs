using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ConversationBranchExtractorTests
{
    [Fact]
    public void ExtractActiveBranch_follows_current_node_path()
    {
        const string json = """
            {
              "current_node": "a2",
              "mapping": {
                "root": { "parent": null, "children": ["u1"] },
                "u1": {
                  "parent": "root",
                  "children": ["a1"],
                  "message": {
                    "id": "msg-u1",
                    "author": { "role": "user" },
                    "content": { "parts": ["I look around"] },
                    "create_time": 3
                  }
                },
                "a1": {
                  "parent": "u1",
                  "children": ["u2"],
                  "message": {
                    "id": "msg-a1",
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Opening scene."] },
                    "create_time": 2
                  }
                },
                "u2": {
                  "parent": "a1",
                  "children": ["a2"],
                  "message": {
                    "id": "msg-u2",
                    "author": { "role": "user" },
                    "content": { "parts": ["Go north"] },
                    "create_time": 10
                  }
                },
                "a2": {
                  "parent": "u2",
                  "children": [],
                  "message": {
                    "id": "msg-a2",
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Live path."] },
                    "create_time": 30
                  }
                },
                "a-alt": {
                  "parent": "u2",
                  "children": [],
                  "message": {
                    "id": "msg-alt",
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Discarded regenerate."] },
                    "create_time": 20
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var branch = ConversationBranchExtractor.ExtractActiveBranch(doc.RootElement);

        Assert.Equal(4, branch.Count);
        Assert.Equal("u1", branch[0].NodeId);
        Assert.Equal("msg-u1", branch[0].MessageId);
        Assert.Equal(0, branch[0].BranchIndex);
        Assert.Equal("a2", branch[^1].NodeId);
        Assert.Equal("Live path.", branch[^1].DisplayText);
        Assert.DoesNotContain(branch, m => m.NodeId == "a-alt");
    }

    [Fact]
    public void ExtractActiveBranch_flags_utility_and_injected_context()
    {
        const string json = """
            {
              "current_node": "a1",
              "mapping": {
                "u1": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["[[cgw:utility job=\\\"propose_memories\\\"]]packet[[/cgw:utility]]"] }
                  }
                },
                "a1": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["[[cgw:utility-response job=\\\"propose_memories\\\"]]done[[/cgw:utility-response]]"] }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var branch = ConversationBranchExtractor.ExtractActiveBranch(doc.RootElement);

        Assert.Equal(2, branch.Count);
        Assert.True(branch[0].IsUtility);
        Assert.True(branch[1].IsUtility);
    }

    [Fact]
    public void ExtractActiveBranch_skips_injected_context_user_without_player_line()
    {
        const string json = """
            {
              "current_node": "a1",
              "mapping": {
                "u1": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["[[cgw:sources]]scenario[[/cgw:sources]]"] }
                  }
                },
                "a1": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Ready."] }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var branch = ConversationBranchExtractor.ExtractActiveBranch(doc.RootElement);

        Assert.Equal(2, branch.Count);
        Assert.True(branch[0].IsInjectedContext);
        Assert.Equal("Ready.", branch[1].DisplayText);
    }
}
