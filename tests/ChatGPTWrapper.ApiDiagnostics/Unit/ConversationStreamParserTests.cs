using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ConversationStreamParserTests
{
    [Fact]
    public void Parse_accumulates_append_deltas_from_fixture()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "conversation-stream-sample.txt");
        fixturePath = Path.GetFullPath(fixturePath);
        Assert.True(File.Exists(fixturePath), $"Missing fixture: {fixturePath}");

        var sse = File.ReadAllText(fixturePath);
        var result = ConversationStreamParser.Parse(sse);

        Assert.Equal("Hello world", result.AssistantText);
        Assert.Equal("asst-1", result.AssistantMessageId);
        Assert.Equal("conv-test-1", result.ConversationId);
        Assert.True(result.StreamComplete);
    }

    [Fact]
    public void ParseChunks_handles_incremental_reads()
    {
        ConversationCaptureCache.ClearForTests();

        const string part1 = "data: {\"p\":\"/message/content/parts/0\",\"o\":\"append\",\"v\":\"Hel\"}\n\n";
        const string part2 = "data: {\"p\":\"/message/content/parts/0\",\"o\":\"append\",\"v\":\"lo\"}\n\n";
        const string part3 = "data: [DONE]\n\n";

        var result = ConversationStreamParser.ParseChunks([part1, part2, part3]);

        Assert.Equal("Hello", result.AssistantText);
        Assert.True(result.StreamComplete);
    }

    [Fact]
    public void ExtractTranscriptTurns_skips_injected_context_user_messages()
    {
        const string json = """
            {
              "mapping": {
                "u1": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["[[cgw:sources]]scenario[[/cgw:sources]]"] },
                    "create_time": 1
                  }
                },
                "a1": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Opening scene."] },
                    "create_time": 2
                  }
                },
                "u2": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["I look around"] },
                    "create_time": 3
                  }
                },
                "a2": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Dust swirls."] },
                    "create_time": 4
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("", pairs[0].PlayerText);
        Assert.Equal("Opening scene.", pairs[0].NarratorText);
        Assert.Equal("I look around", pairs[1].PlayerText);
        Assert.Equal("Dust swirls.", pairs[1].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_follows_active_branch_not_regenerated_sibling()
    {
        const string json = """
            {
              "current_node": "a-live",
              "mapping": {
                "root": { "parent": null, "children": ["u1"] },
                "u1": {
                  "parent": "root",
                  "children": ["a-live", "a-alt"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["Go north"] },
                    "create_time": 10
                  }
                },
                "a-live": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Live path."] },
                    "create_time": 30
                  }
                },
                "a-alt": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Discarded regenerate."] },
                    "create_time": 20
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Single(pairs);
        Assert.Equal("Go north", pairs[0].PlayerText);
        Assert.Equal("Live path.", pairs[0].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_skips_null_message_nodes()
    {
        const string json = """
            {
              "current_node": "a1",
              "mapping": {
                "root": { "parent": null, "children": ["placeholder", "u1"] },
                "placeholder": {
                  "parent": "root",
                  "children": [],
                  "message": null
                },
                "u1": {
                  "parent": "root",
                  "children": ["a1"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["look around"] },
                    "create_time": 1
                  }
                },
                "a1": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["The room is dark."] },
                    "create_time": 2
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Single(pairs);
        Assert.Equal("look around", pairs[0].PlayerText);
        Assert.Equal("The room is dark.", pairs[0].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_interleaves_when_assistant_create_time_missing()
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
                    "author": { "role": "user" },
                    "content": { "parts": ["six"] },
                    "create_time": 1
                  }
                },
                "a1": {
                  "parent": "u1",
                  "children": ["u2"],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Seven."] }
                  }
                },
                "u2": {
                  "parent": "a1",
                  "children": ["a2"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["seven"] },
                    "create_time": 3
                  }
                },
                "a2": {
                  "parent": "u2",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Eight."] }
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("six", pairs[0].PlayerText);
        Assert.Equal("Seven.", pairs[0].NarratorText);
        Assert.Equal("seven", pairs[1].PlayerText);
        Assert.Equal("Eight.", pairs[1].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_orders_user_assistant_pairs()
    {
        const string json = """
            {
              "mapping": {
                "u1": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["I look around"] },
                    "create_time": 1
                  }
                },
                "a1": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Dust swirls."] },
                    "create_time": 2
                  }
                },
                "u2": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["I listen"] },
                    "create_time": 3
                  }
                },
                "a2": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Silence."] },
                    "create_time": 4
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("I look around", pairs[0].PlayerText);
        Assert.Equal("Dust swirls.", pairs[0].NarratorText);
        Assert.Equal("I listen", pairs[1].PlayerText);
        Assert.Equal("Silence.", pairs[1].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_extracts_player_from_context_tagged_packet()
    {
        const string json = """
            {
              "current_node": "a1",
              "mapping": {
                "root": { "parent": null, "children": ["u1"] },
                "u1": {
                  "parent": "root",
                  "children": ["a1"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["[[cgw:meta mode=\"fat\" turn=\"1\"]] [[/cgw:meta]]\n\n[[cgw:summary]]room[[/cgw:summary]]\n\neight"] },
                    "create_time": 1
                  }
                },
                "a1": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Eight."] },
                    "create_time": 2
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Single(pairs);
        Assert.Equal("eight", pairs[0].PlayerText);
        Assert.Equal("Eight.", pairs[0].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_extracts_player_from_player_turn_section()
    {
        const string json = """
            {
              "current_node": "a1",
              "mapping": {
                "root": { "parent": null, "children": ["u1"] },
                "u1": {
                  "parent": "root",
                  "children": ["a1"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["=== ROLLING SUMMARY ===\nroom\n\n=== PLAYER TURN ===\nnine"] },
                    "create_time": 1
                  }
                },
                "a1": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Nine."] },
                    "create_time": 2
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Single(pairs);
        Assert.Equal("nine", pairs[0].PlayerText);
        Assert.Equal("Nine.", pairs[0].NarratorText);
    }

    [Fact]
    public void ExtractAssistantChildOfUserMessage_returns_null_when_user_child_not_ready()
    {
        const string json = """
            {
              "current_node": "a-prev",
              "mapping": {
                "root": { "parent": null, "children": ["u-prev"] },
                "u-prev": {
                  "parent": "root",
                  "children": ["a-prev"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["fourteen"] },
                    "create_time": 1
                  }
                },
                "a-prev": {
                  "parent": "u-prev",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Fourteen."] },
                    "create_time": 2
                  }
                },
                "u-new": {
                  "parent": "a-prev",
                  "children": [],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["fifteen"] },
                    "create_time": 3
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var text = ConversationStreamParser.ExtractAssistantChildOfUserMessage(doc.RootElement, "u-new");

        Assert.Null(text);
    }

    [Fact]
    public void ExtractAssistantChildOfUserMessage_prefers_active_branch_child()
    {
        const string json = """
            {
              "current_node": "a-live",
              "mapping": {
                "root": { "parent": null, "children": ["u1"] },
                "u1": {
                  "parent": "root",
                  "children": ["a-live", "a-alt"],
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["job packet"] },
                    "create_time": 1
                  }
                },
                "a-live": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["[{\"text\":\"live memory\",\"tags\":[],\"pinned\":false}]"] },
                    "create_time": 30
                  }
                },
                "a-alt": {
                  "parent": "u1",
                  "children": [],
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["[]"] },
                    "create_time": 20
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var text = ConversationStreamParser.ExtractAssistantChildOfUserMessage(doc.RootElement, "u1");

        Assert.Contains("live memory", text);
    }

    [Fact]
    public void ExtractLastAssistantFromConversation_reads_mapping_leaf()
    {
        const string json = """
            {
              "current_node": "leaf",
              "mapping": {
                "user-node": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["hi"] }
                  }
                },
                "leaf": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Narrator reply"] }
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var text = ConversationStreamParser.ExtractLastAssistantFromConversation(doc.RootElement);

        Assert.Equal("Narrator reply", text);
    }

    [Fact]
    public void ExtractTranscriptTurns_pairs_flat_mapping_without_parent_chain()
    {
        const string json = """
            {
              "mapping": {
                "u-ctx": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["[[cgw:sources]]hidden[[/cgw:sources]]"] },
                    "create_time": 1
                  }
                },
                "a-open": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Scene opens."] },
                    "create_time": 2
                  }
                },
                "u-play": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["=== PLAYER TURN ===\nenter the cave"] },
                    "create_time": 3
                  }
                },
                "a-play": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Darkness swallows you."] },
                    "create_time": 4
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("", pairs[0].PlayerText);
        Assert.Equal("Scene opens.", pairs[0].NarratorText);
        Assert.Equal("enter the cave", pairs[1].PlayerText);
        Assert.Equal("Darkness swallows you.", pairs[1].NarratorText);
    }

    [Fact]
    public void ExtractTranscriptTurns_interleaves_orphan_assistant_before_first_user()
    {
        const string json = """
            {
              "mapping": {
                "a0": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["Prologue."] },
                    "create_time": 1
                  }
                },
                "u1": {
                  "message": {
                    "author": { "role": "user" },
                    "content": { "parts": ["step forward"] },
                    "create_time": 2
                  }
                },
                "a1": {
                  "message": {
                    "author": { "role": "assistant" },
                    "content": { "parts": ["You step."] },
                    "create_time": 3
                  }
                }
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var pairs = ConversationStreamParser.ExtractTranscriptTurns(doc.RootElement);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("", pairs[0].PlayerText);
        Assert.Equal("Prologue.", pairs[0].NarratorText);
        Assert.Equal("step forward", pairs[1].PlayerText);
        Assert.Equal("You step.", pairs[1].NarratorText);
    }
}

[Trait("Category", "Unit")]
public sealed class ConversationCaptureCacheTests
{
    [Fact]
    public void Store_and_TryGet_round_trip()
    {
        ConversationCaptureCache.ClearForTests();
        ConversationCaptureCache.Store("conv-1", "user-1", "assistant text", "asst-1", streamComplete: true);

        Assert.True(ConversationCaptureCache.TryGet("conv-1", "user-1", out var capture));
        Assert.Equal("assistant text", capture.AssistantText);
        Assert.True(ConversationCaptureCache.TryGetLast(out var last));
        Assert.Equal("assistant text", last.AssistantText);
    }
}
