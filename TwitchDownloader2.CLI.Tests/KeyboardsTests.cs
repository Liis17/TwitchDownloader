using Telegram.Bot.Types.ReplyMarkups;
using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class KeyboardsTests
{
    [Fact]
    public void StopDownloadsKeyboard_UsesSessionIdInsteadOfChannelInCallback()
    {
        var active = new[]
        {
            new ActiveDownloadInfo(
                "session-123",
                "alpha",
                DateTimeOffset.UtcNow,
                "/downloads/alpha.recording",
                ActiveDownloadState.Recording)
        };

        InlineKeyboardMarkup keyboard = Keyboards.GetStopDownloadsKeyboard(active);
        var button = Assert.Single(Assert.Single(keyboard.InlineKeyboard));

        Assert.Equal("stop_download:session-123", button.CallbackData);
        Assert.True(Keyboards.TryParseStopDownloadCallback(button.CallbackData, out var sessionId));
        Assert.Equal("session-123", sessionId);
    }

    [Fact]
    public void DeleteChannelsKeyboard_PutsEachChannelIntoParsableCallback()
    {
        InlineKeyboardMarkup keyboard = Keyboards.GetDeleteChannelsKeyboard(["alpha", "beta_2"]);
        var callbacks = keyboard.InlineKeyboard.Select(row => Assert.Single(row).CallbackData).ToArray();

        Assert.Equal(2, callbacks.Length);
        Assert.True(Keyboards.TryParseDeleteChannelCallback(callbacks[1], out var channel));
        Assert.Equal("beta_2", channel);
        Assert.False(Keyboards.TryParseDeleteChannelCallback(callbacks[1].Replace("delete_channel:", "stop_download:"), out _));
        Assert.False(Keyboards.TryParseDeleteChannelCallback("delete_channel:", out _));
    }

    [Fact]
    public void MainKeyboard_MovesChannelEditingIntoChannelsMenu()
    {
        var mainButtons = Keyboards.GetMainKeyboard().Keyboard.SelectMany(row => row).Select(button => button.Text).ToArray();
        var channelButtons = Keyboards.GetChannelsKeyboard().Keyboard.SelectMany(row => row).Select(button => button.Text).ToArray();

        Assert.DoesNotContain("➕ Добавить", mainButtons);
        Assert.DoesNotContain("🗑️ Удалить", mainButtons);
        Assert.Contains("📺 Каналы", mainButtons);
        Assert.Equal(["➕ Добавить", "🗑️ Удалить", "🏠 Вернуться на главную"], channelButtons);
    }
}
