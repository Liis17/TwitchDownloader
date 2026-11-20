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
        Assert.False(Keyboards.TryParseDeleteChannelCallback(callbacks[1]!.Replace("delete_channel:", "stop_download:"), out _));
        Assert.False(Keyboards.TryParseDeleteChannelCallback("delete_channel:", out _));
    }

    [Theory]
    [InlineData("https://www.twitch.tv/videos/2345678901?t=1h2m", true, "2345678901")]
    [InlineData("twitch.tv/somechannel/video/42", true, "42")]
    [InlineData("2345678901", true, "2345678901")]
    [InlineData("https://www.twitch.tv/Some_Channel", false, "some_channel")]
    [InlineData("somechannel", false, "somechannel")]
    public void TryParseDownloadTarget_DistinguishesVodAndChannel(string input, bool expectedVod, string expectedTarget)
    {
        Assert.True(TelegramService.TryParseDownloadTarget(input, out var isVod, out var target));
        Assert.Equal(expectedVod, isVod);
        Assert.Equal(expectedTarget, target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://www.twitch.tv/")]
    [InlineData("привет мир")]
    public void TryParseDownloadTarget_RejectsGarbage(string input)
    {
        Assert.False(TelegramService.TryParseDownloadTarget(input, out _, out _));
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
