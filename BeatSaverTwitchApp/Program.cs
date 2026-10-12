using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SimpleJSON;

internal class Program
{
    const string AppName = "BeatSaverTwitchApp";
    const string Version = "1.0.0";
    const string Author = "RiskiVR";

    const string ConfigFile = "config.json";

    static string? _oauth;
    static string? _user;
    static string? _channel;

    public static async Task Main(string[] args)
    {
        Console.Title = AppName;
        Console.WriteLine($"{AppName} v{Version} by {Author}\n");

        if (!File.Exists(ConfigFile))
        {
            await InitConfig();
            Console.WriteLine("No config file was found. One has been created.");
            return;
        }
        await ConnectToTwitch();
    }
    static async Task ConnectToTwitch()
    {
        string twitchIrc = "irc.chat.twitch.tv";
        int port = 6667;
        var twitchInfo = JsonSerializer.Deserialize<Config>(await File.ReadAllTextAsync(ConfigFile), JsonOptions)?.TwitchInfo;
        if (twitchInfo == null) return;

        _oauth = twitchInfo.oauth;
        _user = twitchInfo.user;
        _channel = twitchInfo.channel;

        using TcpClient client = new TcpClient();
        await client.ConnectAsync(twitchIrc, port);
        await using NetworkStream stream = client.GetStream();
        using StreamReader reader = new StreamReader(stream);
        await using StreamWriter writer = new StreamWriter(stream);
        await writer.WriteLineAsync($"PASS {(_oauth.Contains("oauth:") ? "" : "oauth:")}{_oauth}");
        await writer.WriteLineAsync($"NICK {_user}");
        await writer.WriteLineAsync($"JOIN #{_channel}");
        await writer.WriteLineAsync($"PRIVMSG #{_channel} :BeatSaverTwitchApp Initialized!");
        await writer.FlushAsync();
        Console.WriteLine($"Connected to Twitch channel {_channel} as {_user}!\n");

        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null) continue;

            if (line.StartsWith("PING"))
            {
                await writer.WriteLineAsync("PONG :tmi.twitch.tv");
                await writer.FlushAsync();
                continue;
            }

            if (line.Contains("PRIVMSG"))
            {
                int splitPoint = line.IndexOf("PRIVMSG");
                string message = line.Substring(line.IndexOf(" :", splitPoint) + 2);

                _ = HandleBSR(message, writer);
            }
        }
    }

    static async Task HandleBSR(string message, StreamWriter writer)
    {
        if (!message.StartsWith("!bsr")) return;
        var id = message.Substring(5).Trim();

        string? customLevelsPath = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigFile), JsonOptions)?.BeatSaberConfig.CustomLevelsPath;
        
        if (customLevelsPath == null)
        {
            Console.WriteLine("Unable to parse CustomLevelsPath");
            return;
        }

        if (Directory.Exists(Path.Combine(customLevelsPath, id)))
        {
            string existsResponse = $"Song with ID {id} has already been downloaded!";
            Console.WriteLine(existsResponse);
            await writer.WriteLineAsync($"PRIVMSG #{_channel} :{existsResponse}");
            await writer.FlushAsync();
            return;
        }

        Console.WriteLine($"Downloading BeatSaver map with ID {id}...");
        string beatsaverapi = $"https://beatsaver.com/api/maps/id/{id}";

        HttpClient httpClient = new HttpClient();
        WebClient webClient = new WebClient();

        var response = await httpClient.GetAsync(beatsaverapi);
        var content = await response.Content.ReadAsStringAsync();
        var node = JSON.Parse(content);
        var songName = node["metadata"]["songName"];
        var levelAuthor = node["metadata"]["levelAuthorName"];
        var downloadUrl = node["versions"][0]["downloadURL"];

        await webClient.DownloadFileTaskAsync(new Uri(downloadUrl), id);
        await ZipFile.ExtractToDirectoryAsync(id, Path.Combine(customLevelsPath, id));
        File.Delete(id);

        string bsrResponse = $"Downloaded song {songName} mapped by {levelAuthor} with BeatSaver ID {id}";

        await writer.WriteLineAsync($"PRIVMSG #{_channel} :{bsrResponse}");
        await writer.FlushAsync();
        Console.WriteLine(bsrResponse);
    }
    static async Task InitConfig()
    {
        if (!File.Exists(ConfigFile))
        {
            var defaultConfig = new Config
            {
                BeatSaberConfig = new BeatSaberConfig(),
                TwitchInfo = new TwitchInfo()
            };
            await File.WriteAllTextAsync(ConfigFile, JsonSerializer.Serialize(defaultConfig, JsonOptions));
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true
    };
}
[Serializable]
public class TwitchInfo
{
    public string channel = "";
    public string oauth = "";
    public string user = "";
}
[Serializable]
public class BeatSaberConfig
{
    public string CustomLevelsPath = "";
}
[Serializable]
public class Config
{
    public BeatSaberConfig BeatSaberConfig;
    public TwitchInfo TwitchInfo;
}