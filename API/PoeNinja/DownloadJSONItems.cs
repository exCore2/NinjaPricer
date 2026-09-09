using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExileCore2;
using Newtonsoft.Json;
using NinjaPricer.API.PoeNinja.Models;

namespace NinjaPricer.API.PoeNinja;

public class DataDownloader
{
    private const string BaseUrl = "https://poe.ninja";

    private static string GetExchangeLink(string league, string type)
        => $"{BaseUrl}/poe2/api/economy/exchange/current/overview?league={league}&type={type}";

    private static string GetStashLink(string league, string type)
        => $"{BaseUrl}/poe2/api/economy/stash/current/item/overview?league={league}&type={type}";

    private int _updating;
    public CollectiveApiData CollectedData { get; set; }

    private class LeagueMetadata
    {
        public DateTime LastLoadTime { get; set; }
    }

    public Action<string> log { get; set; }
    public NinjaPricerSettings Settings { get; set; }
    public string DataDirectory { get; set; }

    static readonly List<(string Name, string ApiName, Action<CollectiveApiData, ExchangeOverview> Setter)> ExchangeCategories = new()
    {
        ("Currency", "Currency", (d, v) => d.Currency = v),
        ("Breach", "Breach", (d, v) => d.Breach = v),
        ("Delirium", "Delirium", (d, v) => d.Delirium = v),
        ("Essences", "Essences", (d, v) => d.Essences = v),
        ("Runes", "Runes", (d, v) => d.Runes = v),
        ("Ritual", "Ritual", (d, v) => d.Ritual = v),
        ("Fragments", "Fragments", (d, v) => d.Fragments = v),
        ("UncutGems", "UncutGems", (d, v) => d.UncutGems = v),
        ("Abyss", "Abyss", (d, v) => d.Abyss = v),
        ("Expedition", "Expedition", (d, v) => d.Expedition = v),
        ("Verisium", "Verisium", (d, v) => d.Verisium = v),
        ("LineageSupportGems", "LineageSupportGems", (d, v) => d.LineageSupportGems = v),
        ("SoulCores", "SoulCores", (d, v) => d.SoulCores = v),
        ("Idols", "Idols", (d, v) => d.Idols = v),
    };

    static readonly List<(string Name, string ApiName, Action<CollectiveApiData, StashOverview> Setter)> StashCategories = new()
    {
        ("Weapons", "UniqueWeapons", (d, v) => d.Weapons = v),
        ("Armour", "UniqueArmours", (d, v) => d.Armour = v),
        ("Accessories", "UniqueAccessories", (d, v) => d.Accessories = v),
        ("Flasks", "UniqueFlasks", (d, v) => d.Flasks = v),
        ("Jewels", "UniqueJewels", (d, v) => d.Jewels = v),
        ("Charms", "UniqueCharms", (d, v) => d.Charms = v),
        ("SanctumRelics", "UniqueSanctumRelics", (d, v) => d.SanctumRelics = v),
        ("Tablets", "PrecursorTablets", (d, v) => d.Tablets = v),
        ("UniqueTablets", "UniqueTablets", (d, v) => d.UniqueTablets = v),
    };

    public void StartDataReload(string league, bool forceRefresh)
    {
        log($"Getting data for {league}");

        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
        {
            log("Update is already in progress");
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                log("Gathering Data from Poe.Ninja.");

                var newData = new CollectiveApiData();
                var tryWebFirst = forceRefresh;
                var metadataPath = Path.Join(DataDirectory, league, "meta.json");
                if (!tryWebFirst && Settings.DataSourceSettings.AutoReload)
                {
                    tryWebFirst = await IsLocalCacheStale(metadataPath);
                }

                // Load exchange (currency) categories
                foreach (var (name, apiName, setter) in ExchangeCategories)
                {
                    var fileName = $"{name}.json";
                    var url = GetExchangeLink(league, apiName);

                    var data = await LoadFromWebOrBackup<ExchangeOverview>(fileName, url, tryWebFirst);
                    if (data != null)
                    {
                        setter(newData, data);
                    }
                }

                // Load stash (unique) categories
                foreach (var (name, apiName, setter) in StashCategories)
                {
                    var fileName = $"{name}.json";
                    var url = GetStashLink(league, apiName);

                    var data = await LoadFromWebOrBackup<StashOverview>(fileName, url, tryWebFirst);
                    if (data != null)
                    {
                        setter(newData, data);
                    }
                }

                newData.DivineToExaltedRate = newData.DivineToExaltedRateRaw;

                new FileInfo(metadataPath).Directory?.Create();
                await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(new LeagueMetadata { LastLoadTime = DateTime.UtcNow }));

                log("Finished Gathering Data from Poe.Ninja.");
                CollectedData = newData;
                log("Updated CollectedData.");
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"Ninja pricer failed to reload data: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _updating, 0);
            }
        });
    }

    private async Task<bool> IsLocalCacheStale(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return true;

        try
        {
            var metadata = JsonConvert.DeserializeObject<LeagueMetadata>(await File.ReadAllTextAsync(metadataPath));
            return DateTime.UtcNow - metadata.LastLoadTime > TimeSpan.FromMinutes(Settings.DataSourceSettings.ReloadPeriod);
        }
        catch (Exception ex)
        {
            if (Settings.DebugSettings.EnableDebugLogging)
                log($"Metadata loading failed: {ex}");
            return true;
        }
    }

    private async Task<T> LoadFromWebOrBackup<T>(string fileName, string url, bool tryWebFirst) where T : class
    {
        var backupFile = Path.Join(DataDirectory, Settings.DataSourceSettings.League.Value, fileName);

        if (tryWebFirst)
        {
            var webData = await LoadFromWeb<T>(fileName, url, backupFile);
            if (webData != null) return webData;
        }

        var backupData = await LoadFromBackup<T>(fileName, backupFile);
        if (backupData != null) return backupData;

        if (!tryWebFirst)
        {
            return await LoadFromWeb<T>(fileName, url, backupFile);
        }

        return null;
    }

    private async Task<T> LoadFromWeb<T>(string fileName, string url, string backupFile) where T : class
    {
        try
        {
            if (Settings.DebugSettings.EnableDebugLogging)
                log($"Downloading {fileName}");

            var json = await Utils.DownloadFromUrl(url);
            var data = JsonConvert.DeserializeObject<T>(json);

            if (Settings.DebugSettings.EnableDebugLogging)
                log($"{fileName} downloaded");

            try
            {
                new FileInfo(backupFile).Directory.Create();
                await File.WriteAllTextAsync(backupFile, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                var errorPath = backupFile + ".error";
                new FileInfo(errorPath).Directory.Create();
                await File.WriteAllTextAsync(errorPath, ex.ToString());
                if (Settings.DebugSettings.EnableDebugLogging)
                    log($"{fileName} save failed: {ex}");
            }

            return data;
        }
        catch (Exception ex)
        {
            if (Settings.DebugSettings.EnableDebugLogging)
                log($"{fileName} fresh data download failed: {ex}");
            return null;
        }
    }

    private async Task<T> LoadFromBackup<T>(string fileName, string backupFile) where T : class
    {
        if (File.Exists(backupFile))
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(await File.ReadAllTextAsync(backupFile));
            }
            catch (Exception backupEx)
            {
                if (Settings.DebugSettings.EnableDebugLogging)
                    log($"{fileName} backup data load failed: {backupEx}");
            }
        }
        else if (Settings.DebugSettings.EnableDebugLogging)
        {
            log($"No backup for {fileName}");
        }

        return null;
    }
}
