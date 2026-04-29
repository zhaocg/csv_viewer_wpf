using System;
using System.IO;
using System.Text.Json;
using CsvViewer.Models;

namespace CsvViewer.Services;

public sealed class AppStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _stateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CsvViewer",
        "app-state.json");

    public AppState Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new AppState();
            }

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        var directoryPath = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_stateFilePath, json);
    }
}
