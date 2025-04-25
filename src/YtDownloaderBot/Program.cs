// Load environment variables from .env (for local development)
// Requires DotNetEnv.Configuration package

using DotNetEnv.Configuration;
using Microsoft.Extensions.Configuration;
using YtDownloaderBot;
using YtDownloaderBot.Services;
using static System.Console;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddEnvironmentVariables()
    .AddDotNetEnv()
    .Build();

// Read configuration values
var token      = config["BOT_TOKEN"];
var ytDlpPath  = config["YTDLP_PATH"]  ?? "/usr/local/bin/yt-dlp";
var ffmpegPath = config["FFMPEG_PATH"] ?? "/usr/local/bin/ffmpeg";
var connString = config["AZURE_CONNECTION_STRING"];
var container    = config["AZURE_CONTAINER"] ?? "videos";

if (string.IsNullOrWhiteSpace(token))
{
    Error.WriteLine("❌ BOT_TOKEN is not set. Please configure it in .env or environment variables.");
    return;
}
// instantiate the helpers
var blobService = new BlobStorageService(connString ?? string.Empty, container);
var bot = new TelegramBot(token, ytDlpPath, ffmpegPath, blobService);

// Ctrl+C cancellation
using var cts = new CancellationTokenSource();
CancelKeyPress += (_,e) => { e.Cancel = true; cts.Cancel(); };

await bot.StartAsync(cts.Token);
