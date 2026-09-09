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
}
