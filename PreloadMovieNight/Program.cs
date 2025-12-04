// See https://aka.ms/new-console-template for more information

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

try
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddIniFile("settings.ini", optional: false)
        .Build();

    var downloadServerString = configuration["Application:DownloadServer"] ?? string.Empty;
    var playlist = configuration["Application:Playlist"] ?? string.Empty;
    if (string.IsNullOrWhiteSpace(playlist))
        playlist = ".playlist.json";
    
    var downloadServerUrl = new Uri(downloadServerString);
    var downloadBuilder = new UriBuilder(downloadServerUrl);
    var precacheDirectory = configuration["Application:DownloadDirectory"] ?? string.Empty;
    var fullPrecacheDirectory = Path.GetFullPath(precacheDirectory);
    if (!Directory.Exists(fullPrecacheDirectory))
    {
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] Cannot find the '{precacheDirectory}' directory.");
        return 1;
    }

    var httpClient = new HttpClient();
    downloadBuilder.Path += playlist;
    var playlistPath = playlist.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? playlist : downloadBuilder.Uri.ToString();
    var response = await httpClient.GetAsync(playlistPath);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] Unable to find the file at [yellow]{playlistPath}[/]. Unable to continue.");
        return 1;
    }

    var filenames = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.StringArray) ?? [];
    var totalFileNames = filenames.Length;
    var count = 0;
    foreach (var filename in filenames)
    {
        downloadBuilder = new UriBuilder(downloadServerUrl);
        downloadBuilder.Path += filename;
        var fileUri = downloadBuilder.Uri.ToString();
        if (filename.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            fileUri = filename;
        }

        var basename = Path.GetFileName(filename);
        // if (string.IsNullOrEmpty())
        AnsiConsole.MarkupInterpolated($"Downloading '{basename}'.");
        response = await httpClient.GetAsync(fileUri);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            AnsiConsole.MarkupLineInterpolated($" [red]ERROR:[/] Failed to download file.");
            
            continue;
        }

        await using var stream = response.Content.ReadAsStream();
        await using var outStream = new FileStream(Path.Combine(fullPrecacheDirectory, basename), FileMode.Create,
            FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(outStream);
        AnsiConsole.MarkupLineInterpolated($" [green]Complete.[/]");
        count += 1;
    }
    
    AnsiConsole.MarkupLineInterpolated($"[green]Successfully download {count} of {totalFileNames} into cache.[/]");
    PromptForClose();
    return 0;
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"exception of type: '{ex.GetType()}'.");
    AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] Failed to precache items. {ex.Message}");
    AnsiConsole.MarkupLineInterpolated($"[red]ERROR:[/] Ensure your plugin is installed and running from the Application Installation Directory.");
    PromptForClose();
    return 1;
}

void PromptForClose()
{
    Console.WriteLine("Press enter to exit.");
    Console.ReadLine();
}

[JsonSerializable(typeof(string[]))]
internal partial class AppJsonContext : JsonSerializerContext
{
}