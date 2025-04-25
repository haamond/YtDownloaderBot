using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;
using YtDownloaderBot.Services;

namespace YtDownloaderBot;

public class TelegramBot
{
    private readonly BlobStorageService _blobService;
    private readonly TelegramBotClient _botClient;
    private readonly YoutubeDL _downloader;
    private readonly OptionSet _options;

    public TelegramBot(string token, string ytDlpPath, string ffmpegPath, BlobStorageService blobService)
    {
        Console.WriteLine("Initializing TelegramBot...");

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("BOT_TOKEN must be set", nameof(token));
        _blobService = blobService;
        Console.WriteLine($"Using blob service with container: {blobService.Container.Name ?? "unknown"}");

        Console.WriteLine("Creating Telegram bot client...");
        _botClient = new TelegramBotClient(token);

        Console.WriteLine($"Setting up YoutubeDL with paths: yt-dlp={ytDlpPath}, ffmpeg={ffmpegPath}");
        _downloader = new YoutubeDL
        {
            OutputFolder = Environment.CurrentDirectory,
            YoutubeDLPath = ytDlpPath,
            FFmpegPath = ffmpegPath
        };

        _options = new OptionSet
        {
            Format = "bestvideo[ext=mp4]+bestaudio[ext=m4a]"
        };
        Console.WriteLine("TelegramBot initialization complete");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Starting bot...");
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        Console.WriteLine("Starting message receiver...");
        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken
        );

        var me = await _botClient.GetMe(cancellationToken);
        Console.WriteLine($"✅ Bot @{me.Username} is up and running.");
        Console.WriteLine("Waiting for incoming messages...");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Bot shutdown requested via cancellation token");
        }

        Console.WriteLine("Bot has been stopped");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Received update type: {update.Type}");

        if (update.Message?.Type != MessageType.Text)
        {
            Console.WriteLine("Ignoring non-text message");
            return;
        }

        var msg = update.Message;
        var chatId = msg.Chat.Id;
        Console.WriteLine($"Received message from {msg.From?.Username ?? "unknown"} (ID: {chatId}): '{msg.Text}'");

        if (msg.Text != null && !msg.Text.StartsWith("/download "))
        {
            Console.WriteLine("Ignoring message that's not a download command");
            return;
        }

        var url = msg.Text?.Split(' ', 2)[1].Trim();
        Console.WriteLine($"Processing download request for URL: {url}");
        await botClient.SendMessage(chatId, "⏳ Downloading...", cancellationToken: cancellationToken);
        Console.WriteLine("Sent download acknowledgment message to user");

        Console.WriteLine("Starting video download with yt-dlp...");
        var result = await _downloader.RunVideoDownload(url, overrideOptions: _options, ct: cancellationToken);

        if (!result.Success)
        {
            Console.WriteLine($"❌ Download failed: {result.ErrorOutput}");
            await botClient.SendMessage(
                chatId,
                $"❌ Download failed:\n{result.ErrorOutput}",
                cancellationToken: cancellationToken
            );
            Console.WriteLine("Sent error message to user");
            return;
        }

        var filePath = result.Data;
        Console.WriteLine($"✅ Download completed successfully: {filePath}");

        // Generate a blob name based on current UTC time
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var extension = Path.GetExtension(filePath);
        var blobName = $"{timestamp}{extension}";
        Console.WriteLine($"Generated blob name: {blobName}");

        // Upload to Azure Blob Storage using a time-based name
        Console.WriteLine($"Uploading file to blob storage: {filePath} -> {blobName}");
        var link = await _blobService.UploadFileAsync(filePath, blobName);
        Console.WriteLine($"Upload complete, file available at: {link}");

        await botClient.SendMessage(
            chatId,
            $"✅ Here's your video: {link}",
            cancellationToken: cancellationToken
        );
        Console.WriteLine("Sent download link to user");

        // Clean up local file
        Console.WriteLine($"Cleaning up local file: {filePath}");
        try
        {
            File.Delete(filePath);
            Console.WriteLine("Local file deleted successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete local file: {ex.Message}");
            // Ignore cleanup failures
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"❌ [Polling Error] {exception.Message}");
        Console.Error.WriteLine($"Stack trace: {exception.StackTrace}");

        if (exception.InnerException != null)
        {
            Console.Error.WriteLine($"Inner exception: {exception.InnerException.Message}");
        }

        return Task.CompletedTask;
    }
}