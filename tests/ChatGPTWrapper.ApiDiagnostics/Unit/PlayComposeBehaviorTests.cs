using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[CollectionDefinition("PlayComposeWebView", DisableParallelization = true)]
public sealed class PlayComposeWebViewCollection : ICollectionFixture<PlayComposeTestHost>;

[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayComposeMountTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Wrapper_mounts_inside_composer_anchor()
    {
        var mountedInComposer = await host.EvalBoolAsync(
            "(function(){var root=document.getElementById('cgw-play-composer-root');return !!(root&&root.closest('[data-testid=\"composer\"]'));})()");
        Assert.True(mountedInComposer);
    }

    [Fact]
    public async Task Wrapper_sets_document_attribute_when_enabled()
    {
        var attr = await host.EvalStringAsync(
            "document.documentElement.getAttribute('data-cgw-wrapper-composer')");
        Assert.Equal("1", attr);
    }

    [Fact]
    public async Task Unmount_removes_root_and_clears_attribute()
    {
        await host.EvalVoidAsync("globalThis.__cgwSetWrapperComposer(false);");
        var rootExists = await host.EvalBoolAsync("!!document.getElementById('cgw-play-composer-root')");
        var attr = await host.EvalStringAsync(
            "document.documentElement.getAttribute('data-cgw-wrapper-composer')");

        Assert.False(rootExists);
        Assert.True(string.IsNullOrEmpty(attr));
    }

    [Fact]
    public async Task Composer_exposes_expected_controls()
    {
        var hasControls = await host.EvalBoolAsync(
            """
            (function(){
              var root=document.getElementById('cgw-play-composer-root');
              if(!root)return false;
              return !!(root.querySelector('.cgw-compose-input')
                && root.querySelector('.cgw-compose-send')
                && root.querySelector('.cgw-compose-attach')
                && root.querySelector('.cgw-compose-file-input')
                && root.querySelector('.cgw-compose-footer'));
            })()
            """);
        Assert.True(hasControls);
    }
}

[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayComposeSendTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Send_posts_input_and_send_messages_to_host()
    {
        await host.SetInputValueAsync("hello world");
        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        var messages = await host.DrainMessagesAsync();
        Assert.Contains(messages, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeInput");
        Assert.Contains(messages, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal("hello world", messages.Last(m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend")
            .RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Send_clears_input_immediately()
    {
        await host.SetInputValueAsync("gone after send");
        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        Assert.Equal("", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Send_keeps_input_editable_for_native_parity()
    {
        await host.SetInputValueAsync("first");
        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        Assert.False(await host.IsInputReadOnlyAsync());
        await host.SetInputValueAsync("second while in flight");
        Assert.Equal("second while in flight", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Send_disables_send_button_until_busy_released()
    {
        await host.SetInputValueAsync("x");
        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        Assert.True(await host.IsSendDisabledAsync());

        await host.ApplyStateAsync(new PlayComposeUiState { Busy = false, Focus = true });
        Assert.True(await host.IsSendDisabledAsync());

        await host.SetInputValueAsync("next");
        Assert.False(await host.IsSendDisabledAsync());
    }

    [Fact]
    public async Task Enter_sends_shift_enter_does_not()
    {
        await host.SetInputValueAsync("enter send");
        await host.PressEnterAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        var afterEnter = await host.DrainMessagesAsync();
        Assert.Contains(afterEnter, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");

        await host.SetInputValueAsync("shift enter");
        await host.PressEnterAsync(shift: true);
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        var afterShift = await host.DrainMessagesAsync();
        Assert.DoesNotContain(afterShift, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal("shift enter", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Double_send_is_blocked_while_in_flight()
    {
        await host.SetInputValueAsync("once");
        await host.TriggerSendClickAsync();
        await host.SetInputValueAsync("twice");
        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        var sends = (await host.DrainMessagesAsync())
            .Count(m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Enter_during_busy_does_not_send()
    {
        await host.SetInputValueAsync("while busy");
        await host.ApplyStateAsync(new PlayComposeUiState { Busy = true, Status = "Sending…" });
        await host.PressEnterAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(100));

        var messages = await host.DrainMessagesAsync();
        Assert.DoesNotContain(messages, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal("while busy", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Wrapper_is_first_child_of_composer_anchor()
    {
        var isFirst = await host.EvalBoolAsync(
            """
            (function(){
              var root=document.getElementById('cgw-play-composer-root');
              var anchor=root&&root.closest('[data-testid="composer"]');
              return !!(anchor&&anchor.firstElementChild===root);
            })()
            """);
        Assert.True(isFirst);
    }

    [Fact]
    public async Task Native_composer_dom_relocated_to_offscreen_bucket()
    {
        var relocated = await host.EvalBoolAsync(
            """
            (function(){
              var bucket=document.getElementById('cgw-native-composer-offscreen');
              var anchor=document.querySelector('[data-testid="composer"]:has(#cgw-play-composer-root)');
              if(!bucket||!anchor)return false;
              if(anchor.querySelector('#prompt-textarea'))return false;
              return !!(bucket.querySelector('#prompt-textarea')&&bucket.querySelector('[data-testid="composer-submit-button"]'));
            })()
            """);
        Assert.True(relocated);
    }

    [Fact]
    public async Task Stale_composer_is_hidden_when_anchor_replaced()
    {
        await host.SimulateComposerAnchorReplacementAsync();
        await host.WaitUntilAsync("!!document.getElementById('cgw-play-composer-root')", TimeSpan.FromSeconds(3));
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(300));

        var staleHidden = await host.EvalBoolAsync(
            """
            (function(){
              var composers=document.querySelectorAll('[data-testid="composer"]');
              var active=null;
              for(var i=0;i<composers.length;i++){
                if(composers[i].querySelector('#cgw-play-composer-root')){active=composers[i];break;}
              }
              for(var j=0;j<composers.length;j++){
                if(composers[j]===active)continue;
                var style=window.getComputedStyle(composers[j]);
                if(style.display!=='none')return false;
              }
              return true;
            })()
            """);
        Assert.True(staleHidden);
    }

    [Fact]
    public async Task Attachment_only_send_posts_attachments_and_clears_chips()
    {
        await host.StageAttachmentAsync("notes.txt", "text/plain", "hello file");
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, await host.GetAttachmentCountAsync());

        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        var messages = await host.DrainMessagesAsync();
        var send = messages.Last(m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.True(send.RootElement.TryGetProperty("attachments", out var attachments));
        Assert.Equal(1, attachments.GetArrayLength());
        Assert.Equal("notes.txt", attachments[0].GetProperty("name").GetString());
        Assert.Equal("", send.RootElement.GetProperty("text").GetString());
        Assert.Equal(0, await host.GetAttachmentCountAsync());
    }

    [Fact]
    public async Task Send_with_text_and_attachment_includes_both()
    {
        await host.SetInputValueAsync("see attached");
        await host.StageAttachmentAsync("data.json", "application/json", "{}");
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(400));
        await host.TriggerSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(50));

        var messages = await host.DrainMessagesAsync();
        var send = messages.Last(m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal("see attached", send.RootElement.GetProperty("text").GetString());
        Assert.Equal(1, send.RootElement.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task Attachment_enables_send_without_text()
    {
        await host.StageAttachmentAsync("pic.txt", "text/plain", "x");
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(400));
        Assert.False(await host.IsSendDisabledAsync());
    }
}

[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayComposeStateTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Busy_true_disables_send()
    {
        await host.ApplyStateAsync(new PlayComposeUiState { Busy = true, Status = "Sending…" });

        var sendDisabled = await host.IsSendDisabledAsync();
        var footer = await host.EvalStringAsync(
            "(function(){var f=document.querySelector('#cgw-play-composer-root .cgw-compose-footer');return f?f.textContent:'';})()");

        Assert.True(sendDisabled);
        Assert.Equal("Sending…", footer);
    }

    [Fact]
    public async Task Status_only_patch_does_not_clear_user_typed_text()
    {
        await host.SetInputValueAsync("draft line");
        await host.ApplyStateAsync(new PlayComposeUiState { Status = "Preparing…" });
        Assert.Equal("draft line", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Success_idle_patch_without_clear_preserves_text_typed_while_waiting()
    {
        await host.SetInputValueAsync("first");
        await host.TriggerSendClickAsync();
        await host.SetInputValueAsync("next line while waiting");

        await host.ApplyStateAsync(new PlayComposeUiState
        {
            Busy = false,
            Focus = true,
            Status = "Sent.",
        });

        Assert.Equal("next line while waiting", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Clear_patch_clears_input()
    {
        await host.SetInputValueAsync("to clear");
        await host.ApplyStateAsync(new PlayComposeUiState { Clear = true });
        Assert.Equal("", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Text_patch_restores_input_value()
    {
        await host.ApplyStateAsync(new PlayComposeUiState
        {
            Text = "restored prompt",
            Busy = false,
            Focus = true,
        });
        Assert.Equal("restored prompt", await host.GetInputValueAsync());
    }

    [Fact]
    public async Task Focus_patch_moves_active_element_to_wrapper_input()
    {
        await host.EvalVoidAsync("document.body.focus();");
        await host.ApplyStateAsync(new PlayComposeUiState { Focus = true });
        await host.WaitUntilAsync(
            "document.activeElement === document.querySelector('#cgw-play-composer-root .cgw-compose-input')",
            TimeSpan.FromSeconds(3));
        Assert.True(await host.IsInputFocusedAsync());
    }

    [Fact]
    public async Task Wrapper_composer_does_not_render_accept_button()
    {
        await host.WaitUntilAsync(
            "!!document.querySelector('#cgw-play-composer-root .cgw-compose-send')",
            TimeSpan.FromSeconds(3));
        Assert.False(await host.HasAcceptButtonAsync());
    }
}

[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayComposeFocusTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Native_textarea_is_marked_non_tab_focusable()
    {
        Assert.Equal(-1, await host.GetNativeTextareaTabIndexAsync());
    }

    [Fact]
    public async Task Native_focus_is_redirected_to_wrapper_when_idle()
    {
        await host.FocusNativeTextareaAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(600));
        var focused = await host.IsInputFocusedAsync();
        if (!focused)
        {
            await host.ApplyStateAsync(new PlayComposeUiState { Focus = true });
            await host.WaitUntilAsync(
                "document.activeElement === document.querySelector('#cgw-play-composer-root .cgw-compose-input')",
                TimeSpan.FromSeconds(3));
        }

        Assert.True(await host.IsInputFocusedAsync());
    }

    [Fact]
    public async Task Send_requests_focus_after_clearing_input()
    {
        await host.SetInputValueAsync("focus me after");
        await host.TriggerSendClickAsync();
        await host.WaitUntilAsync(
            "document.activeElement === document.querySelector('#cgw-play-composer-root .cgw-compose-input')",
            TimeSpan.FromSeconds(3));
        Assert.True(await host.IsInputFocusedAsync());
    }
}

[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayComposeNativeTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync(enableWrapper: false);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Native_mode_keeps_chatgpt_composer_visible()
    {
        Assert.False(await host.EvalBoolAsync("!!document.getElementById('cgw-play-composer-root')"));
        var attr = await host.EvalStringAsync(
            "document.documentElement.getAttribute('data-cgw-wrapper-composer')");
        Assert.True(string.IsNullOrEmpty(attr));
        Assert.True(await host.EvalBoolAsync(
            "!!document.querySelector('[data-testid=\"composer\"] #prompt-textarea')"));
    }

    [Fact]
    public async Task Native_send_click_blocked_while_host_busy()
    {
        await host.SetNativeInputValueAsync("raw bypass attempt");
        await host.ApplyStateAsync(new PlayComposeUiState { Busy = true, Status = "Logging response…" });
        await host.TriggerNativeSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(80));

        var messages = await host.DrainMessagesAsync();
        Assert.DoesNotContain(messages, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal("raw bypass attempt", await host.GetNativeInputValueAsync());
    }

    [Fact]
    public async Task Native_send_click_posts_compose_send()
    {
        await host.SetNativeInputValueAsync("native line");
        await host.TriggerNativeSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(80));

        var messages = await host.DrainMessagesAsync();
        Assert.Contains(messages, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal(
            "native line",
            messages.Last(m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend")
                .RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Native_enter_posts_compose_send()
    {
        await host.SetNativeInputValueAsync("enter native");
        await host.PressNativeEnterAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(80));

        var messages = await host.DrainMessagesAsync();
        Assert.Contains(messages, m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
    }
}

[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayComposeStabilityTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anchor_replacement_reparents_same_textarea_element()
    {
        await host.SetInputValueAsync("persist across churn");
        await host.RememberTextareaAsync();
        await host.SimulateComposerAnchorReplacementAsync();
        await host.WaitUntilAsync("!!document.getElementById('cgw-play-composer-root')", TimeSpan.FromSeconds(3));
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(300));

        Assert.True(await host.IsSameTextareaAsync());
        Assert.Equal("persist across churn", await host.GetInputValueAsync());
    }
}