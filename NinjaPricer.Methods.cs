using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Enums;
using NinjaPricer.API.PoeNinja;
using NinjaPricer.API.PoeNinja.Models;
using NinjaPricer.Enums;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace NinjaPricer;

public partial class NinjaPricer
{
    private CustomItem _inspectedItem;

    private static readonly Dictionary<string, string> ShardMapping = new()
    {
        { "Transmutation Shard", "Orb of Transmutation" },
        { "Alteration Shard", "Orb of Alteration" },
        { "Annulment Shard", "Orb of Annulment" },
        { "Exalted Shard", "Exalted Orb" },
        { "Mirror Shard", "Mirror of Kalandra" },
        { "Regal Shard", "Regal Orb" },
        { "Alchemy Shard", "Orb of Alchemy" },
        { "Chaos Shard", "Chaos Orb" },
        { "Ancient Shard", "Ancient Orb" },
        { "Engineer's Shard", "Engineer's Orb" },
        { "Harbinger's Shard", "Harbinger's Orb" },
        { "Horizon Shard", "Orb of Horizons" },
        { "Binding Shard", "Orb of Binding" },
        { "Scroll Fragment", "Scroll of Wisdom" },
        { "Ritual Splinter", "Ritual Vessel" },
        { "Crescent Splinter", "The Maven's Writ" },
        { "Timeless Vaal Splinter", "Timeless Vaal Emblem" },
        { "Timeless Templar Splinter", "Timeless Templar Emblem" },
        { "Timeless Eternal Empire Splinter", "Timeless Eternal Emblem" },
        { "Timeless Maraketh Splinter", "Timeless Maraketh Emblem" },
        { "Timeless Karui Splinter", "Timeless Karui Emblem" },
        { "Splinter of Xoph", "Xoph's Breachstone" },
        { "Splinter of Tul", "Tul's Breachstone" },
        { "Splinter of Esh", "Esh's Breachstone" },
        { "Splinter of Uul-Netol", "Uul-Netol's Breachstone" },
        { "Splinter of Chayula", "Chayula's Breachstone" },
        //{ "Simulacrum Splinter", "Simulacrum" },
        { "Chance Shard", "Orb of Chance" },
    };

    private double DivinePrice => _downloader.CollectedData?.DivineToExaltedRate ?? 0;
    private double ChaosPrice => _downloader.CollectedData?.ChaosToExaltedRate ?? 0;

    private List<NormalInventoryItem> GetInventoryItems()
    {
        var inventory = GameController.Game.IngameState.IngameUi.InventoryPanel;
        return !inventory.IsVisible ? null : inventory[InventoryIndex.PlayerInventory].VisibleInventoryItems.ToList();
    }

    private static List<CustomItem> FormatItems(IEnumerable<NormalInventoryItem> itemList)
    {
        return itemList?.Where(x => x?.Item?.IsValid == true).Select(inventoryItem => new CustomItem(inventoryItem)).ToList() ?? [];
    }

    private static bool TryGetShardParent(string shardBaseName, out string shardParent)
    {
        return ShardMapping.TryGetValue(shardBaseName ?? "<unknown>", out shardParent);
    }

    private void GetHoveredItem()
    {
        try
        {
            var uiHover = GameController.Game.IngameState.UIHover;
            if (uiHover.Address != 0 && uiHover.AsObject<HoverItemIcon>().ToolTipType != ToolTipType.ItemInChat)
            {
                var inventoryItemIcon = uiHover.AsObject<NormalInventoryItem>();
                var tooltip = inventoryItemIcon.Tooltip;
                var poeEntity = inventoryItemIcon.Item;
                if (tooltip != null && poeEntity.Address != 0 && poeEntity.IsValid)
                {
                    var item = inventoryItemIcon.Item;
                    var baseItemType = GameController.Files.BaseItemTypes.Translate(item.Path);
                    if (baseItemType != null)
                    {
                        HoveredItem = new CustomItem(inventoryItemIcon);
                        if (Settings.DebugSettings.InspectHoverHotkey.PressedOnce())
                        {
                            _inspectedItem = HoveredItem;
                        }
                        if (HoveredItem.ItemType != ItemTypes.None)
                            GetValue(HoveredItem);
                    }
                }
            }

            HoveredItemTooltipRect = HoveredItem?.Element?.Tooltip?.GetClientRectCache;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Failed to get the hovered item: {ex}");
        }
    }

    private void GetValue(IEnumerable<CustomItem> items)
    {
        if (items == null)
        {
            return;
        }

        foreach (var customItem in items)
        {
            GetValue(customItem);
        }
    }

    private T GetValue<T>(T items) where T : IReadOnlyCollection<CustomItem>
    {
        if (items == null)
        {
            return default;
        }

        foreach (var customItem in items)
        {
            GetValue(customItem);
        }

        return items;
    }

    private StashOverview GetMatchingUniqueData(CollectiveApiData root, ItemTypes type)
    {
        return type switch
        {
            ItemTypes.UniqueAccessory => root.Accessories,
            ItemTypes.UniqueArmour => root.Armour,
            ItemTypes.UniqueFlask => root.Flasks,
            ItemTypes.UniqueJewel => root.Jewels,
            ItemTypes.UniqueWeapon => root.Weapons,
            ItemTypes.UniqueCharm => root.Charms,
            ItemTypes.Relic => root.SanctumRelics,
            ItemTypes.UniqueTablet => root.UniqueTablets,
            _ => null,
        };
    }

    private ExchangeOverview GetMatchingExchangeData(CollectiveApiData root, ItemTypes type)
    {
        return type switch
        {
            ItemTypes.None => null,
            ItemTypes.Currency => root.Currency,
            ItemTypes.Essence => root.Essences,
            ItemTypes.Fragment => root.Fragments,
            ItemTypes.SkillGem => root.LineageSupportGems,
            ItemTypes.UncutGem => root.UncutGems,
            ItemTypes.Omen => root.Ritual,
            ItemTypes.Catalyst => root.Breach,
            ItemTypes.Delirium => root.Delirium,
            ItemTypes.Rune => root.Runes,
            ItemTypes.Ultimatum => root.SoulCores,
            ItemTypes.Idol => root.Idols,
            ItemTypes.Expedition => root.Expedition,
            ItemTypes.Abyss => root.Abyss,
            ItemTypes.Verisium => root.Verisium,
            _ => null,
        };
    }

    private void GetValue(CustomItem item)
    {
        try
        {
            if (!Settings.ValuationDisablingSettings.IsValuationDisabled(item.ItemType))
            {
                switch (item.ItemType) // easier to get data for each item type and handle logic based on that
                {
                    case ItemTypes.Currency:
                    {
                        if (item.BaseName == "Exalted Orb")
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize;
                            break;
                        }

                        var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                            ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                            : (1, item.BaseName);
                        var currencySearch = CollectedData.Currency?.LinesByName.GetValueOrDefault(pricedItem);
                        if (currencySearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * currencySearch.Value.Line.PrimaryValue * CollectedData.Currency.PrimaryToExaltedRate / pricedStack;
                            item.PriceData.ChangeInLast7Days = currencySearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = currencySearch.Value.Item.DetailsId;
                        }

                        break;
                    }
                    case ItemTypes.Fragment:
                    {
                        var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                            ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                            : (1, item.BaseName);
                        var fragmentSearch = CollectedData.Fragments?.LinesByName.GetValueOrDefault(pricedItem);
                        if (fragmentSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * fragmentSearch.Value.Line.PrimaryValue * CollectedData.Fragments.PrimaryToExaltedRate / pricedStack;
                            item.PriceData.ChangeInLast7Days = fragmentSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = fragmentSearch.Value.Item.DetailsId;
                        }

                        break;
                    }
                    case ItemTypes.Tablet:
                    {
                        var match = CollectedData.Tablets.Lines
                            .FirstOrDefault(x => x.BaseType == item.BaseName && x.Variant == item.Rarity.ToString());
                        if (match != null)
                        {
                            item.PriceData.MinChaosValue = match.PrimaryValue * CollectedData.Tablets.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = match.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = match.DetailsId;
                        }

                        break;
                    }
                    case var v when GetMatchingExchangeData(CollectedData, v) is { } data:
                        var genericCurrencySearch = data.LinesByName.GetValueOrDefault(item.BaseName);
                        if (genericCurrencySearch != default)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * genericCurrencySearch.Line.PrimaryValue * data.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = genericCurrencySearch.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = genericCurrencySearch.Item.DetailsId;
                        }

                        break;
                    case var v when GetMatchingUniqueData(CollectedData, v) is { } stashData:
                    {
                        var matches = stashData.Lines
                            .Where(x => x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name))
                            .ToList();

                        if (matches.Count == 1)
                        {
                            item.PriceData.MinChaosValue = matches[0].PrimaryValue * stashData.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = matches[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = matches[0].DetailsId;
                        }
                        else if (matches.Count > 1)
                        {
                            item.PriceData.MinChaosValue = matches.Min(x => x.PrimaryValue) * stashData.PrimaryToExaltedRate;
                            item.PriceData.MaxChaosValue = matches.Max(x => x.PrimaryValue) * stashData.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = matches[0].DetailsId;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()}.GetValue()", 5, Color.Red); }
        }
        finally
        {
            item.PriceData.MinChaosValue = Math.Max(0, item.PriceData.MinChaosValue);
            item.PriceData.MaxChaosValue = Math.Max(item.PriceData.MaxChaosValue, item.PriceData.MinChaosValue);
        }
    }

    private bool ShouldUpdateValues()
    {
        if (StashUpdateTimer.ElapsedMilliseconds > Settings.DataSourceSettings.ItemUpdatePeriodMs)
        {
            StashUpdateTimer.Restart();
            if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()} ValueUpdateTimer.Restart()", 5, Color.DarkGray); }
        }
        else
        {
            return false;
        }
        // TODO: Get inventory items and not just stash tab items, this will be done at a later date
        try
        {
            if (!Settings.StashValueSettings.Show)
            {
                if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()}.ShouldUpdateValues() Stash is not visible", 5, Color.DarkGray); }
                return false;
            }
        }
        catch (Exception)
        {
            if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValues()", 5, Color.DarkGray);
            return false;
        }

        if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValues() == True", 5, Color.LimeGreen);
        return true;
    }

    private bool ShouldUpdateValuesInventory()
    {
        if (InventoryUpdateTimer.ElapsedMilliseconds > Settings.DataSourceSettings.ItemUpdatePeriodMs)
        {
            InventoryUpdateTimer.Restart();
            if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()} ValueUpdateTimer.Restart()", 5, Color.DarkGray); }
        }
        else
        {
            return false;
        }
        // TODO: Get inventory items and not just stash tab items, this will be done at a later date
        try
        {
            if (!Settings.InventoryValueSettings.Show.Value || !GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible)
            {
                if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory() Inventory is not visible", 5, Color.DarkGray); }
                return false;
            }

            // Dont continue if the stash page isnt even open
            if (GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory].VisibleInventoryItems == null)
            {
                if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory() Items == null", 5, Color.DarkGray);
                return false;
            }
        }
        catch (Exception)
        {
            if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory()", 5, Color.DarkGray);
            return false;
        }

        if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory() == True", 5, Color.LimeGreen);
        return true;
    }
}