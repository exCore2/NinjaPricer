using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Cache;
using ExileCore2.Shared.Enums;
using ExileCore2.Shared.Helpers;
using ImGuiNET;
using NinjaPricer.Enums;
using static NinjaPricer.Enums.HaggleTypes.HaggleType;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace NinjaPricer;

public partial class NinjaPricer
{
    public readonly Stopwatch StashUpdateTimer = Stopwatch.StartNew();
    public readonly Stopwatch InventoryUpdateTimer = Stopwatch.StartNew();
    public double StashTabValue { get; set; }
    public double InventoryTabValue { get; set; }
    public List<NormalInventoryItem> ItemList { get; set; } = new List<NormalInventoryItem>();
    public List<CustomItem> FormattedItemList { get; set; } = new List<CustomItem>();

    public List<NormalInventoryItem> InventoryItemList { get; set; } = new List<NormalInventoryItem>();
    public List<CustomItem> FormattedInventoryItemList { get; set; } = new List<CustomItem>();

    public List<CustomItem> ItemsToDrawList { get; set; } = new List<CustomItem>();
    public List<CustomItem> InventoryItemsToDrawList { get; set; } = new List<CustomItem>();
    public StashElement StashPanel { get; set; }
    public InventoryElement InventoryPanel { get; set; }
    public Element HagglePanel { get; set; }

    private CustomItem HoveredItem;
    private RectangleF? HoveredItemTooltipRect;

    private readonly CachedValue<List<ItemOnGround>> _slowGroundItems;
    private readonly CachedValue<List<ItemOnGround>> _groundItems;
    private readonly Dictionary<uint, bool> _soundPlayedTracker = new Dictionary<uint, bool>();

    public NinjaPricer()
    {
        _slowGroundItems = new TimeCache<List<ItemOnGround>>(GetItemsOnGroundSlow, 500);
        _groundItems = new FrameCache<List<ItemOnGround>>(CacheUtils.RememberLastValue(GetItemsOnGround, new List<ItemOnGround>()));
    }

    private List<ItemOnGround> GetItemsOnGround(List<ItemOnGround> previousValue)
    {
        var prevDict = previousValue
            .Where(x => x.Type == GroundItemProcessingType.WorldItem)
            .DistinctBy(x => (x.Item.Element?.Address, x.Item.Entity?.Address))
            .ToDictionary(x => (x.Item.Element?.Address, x.Item.Entity?.Address));
        var labelsOnGround = GameController.IngameState.IngameUi.ItemsOnGroundLabelElement.VisibleGroundItemLabels;
        var result = new List<ItemOnGround>();
        foreach (var description in labelsOnGround)
        {
            if (description.Entity.TryGetComponent<WorldItem>(out var worldItem) &&
                worldItem.ItemEntity is { IsValid: true } groundItemEntity)
            {
                var customItem = prevDict.GetValueOrDefault((description.Label?.Address, groundItemEntity.Address))?.Item;
                if (customItem == null)
                {
                    customItem = new CustomItem(groundItemEntity, description.Label);
                    GetValue(customItem);
                }

                result.Add(new ItemOnGround(customItem, GroundItemProcessingType.WorldItem, description.ClientRect));
            }
        }
        result.AddRange(_slowGroundItems.Value);
        foreach (var id in _soundPlayedTracker.Keys.Except(result.Select(x => x.Item.EntityId)).ToList())
        {
            _soundPlayedTracker.Remove(id);
        }
        return result;
    }

    private List<ItemOnGround> GetItemsOnGroundSlow()
    {
        var labelsOnGround = GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible;
        var result = new List<ItemOnGround>();
        foreach (var labelOnGround in labelsOnGround)
        {
            var item = labelOnGround.ItemOnGround;
            if (item != null &&
                item.TryGetComponent<HeistRewardDisplay>(out var heistReward) &&
                heistReward.RewardItem is { IsValid: true } heistItemEntity)
            {
                result.Add(new ItemOnGround(new CustomItem(heistItemEntity, labelOnGround.Label), GroundItemProcessingType.HeistReward, null));
            }
        }

        GetValue(result.Select(x => x.Item));

        return result;
    }

    // TODO: Get hovered items && items from inventory - Getting hovered item  will become useful later on

    public override void Render()
    {
        #region Reset All Data

        StashTabValue = 0;
        InventoryTabValue = 0;
        HoveredItem = null;
        if (_inspectedItem != null)
        {
            GameController.InspectObject(_inspectedItem, "Ninja pricer hovered item");
        }

        StashPanel = (GameController.Game.IngameState.IngameUi.StashElement, GameController.Game.IngameState.IngameUi.GuildStashElement) switch
        {
            ({ IsVisible: false }, { IsVisible: true, IsValid: true } gs) => gs,
            var (s, _) => s
        };
        InventoryPanel = GameController.Game.IngameState.IngameUi.InventoryPanel;
        HagglePanel = GameController.Game.IngameState.IngameUi.HaggleWindow;

        #endregion

        if (CollectedData == null)
        {
            //nothing loaded yet, don't waste time
            return;
        }

        // only update if the time between last update is more than AutoReloadTimer interval
        if (Settings.DataSourceSettings.AutoReload && Settings.DataSourceSettings.LastUpdateTime.AddMinutes(Settings.DataSourceSettings.ReloadPeriod.Value) < DateTime.Now)
        {
            _downloader.StartDataReload(Settings.DataSourceSettings.League.Value, true);
            Settings.DataSourceSettings.LastUpdateTime = DateTime.Now;
        }

        if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.Loop() is Alive", 5, Color.LawnGreen);

        if (Settings.DebugSettings.EnableDebugLogging)
            LogMessage($"{GetCurrentMethod()}: Selected League: {Settings.DataSourceSettings.League.Value}", 5, Color.White);

        var tabType = StashPanel.VisibleStash?.InvType;

        // Everything is updated, lets check if we should draw
        if (ShouldUpdateValues())
        {
            // Format stash items
            ItemList = StashPanel.IsVisible && tabType != null ? StashPanel.VisibleStash?.VisibleInventoryItems?.ToList() ?? [] : [];
            if (ItemList.Count == 0)
            {
                if (Settings.LeagueSpecificSettings.ShowRitualWindowPrices &&
                    GameController.Game.IngameState.IngameUi.RitualWindow is { IsVisible: true, Items: { Count: > 0 } ritualItems })
                {
                    ItemList = ritualItems;
                }
                else if (Settings.LeagueSpecificSettings.ShowPurchaseWindowPrices &&
                         GameController.Game.IngameState.IngameUi.PurchaseWindow?.TabContainer?.VisibleStash is { IsVisible: true, VisibleInventoryItems: { Count: > 0 } purchaseWindowItems })
                {
                    ItemList = purchaseWindowItems.ToList();
                }
                else if (Settings.LeagueSpecificSettings.ShowPurchaseWindowPrices &&
                         GameController.Game.IngameState.IngameUi.PurchaseWindowHideout?.TabContainer?.VisibleStash is { IsVisible: true, VisibleInventoryItems: { Count: > 0 } hideoutPurchaseWindowItems })
                {
                    ItemList = hideoutPurchaseWindowItems.ToList();
                }
            }

            FormattedItemList = FormatItems(ItemList);

            if (Settings.DebugSettings.EnableDebugLogging)
                LogMessage($"{GetCurrentMethod()}.Render() Looping if (ShouldUpdateValues())", 5,
                    Color.LawnGreen);

            GetValue(FormattedItemList);
        }

        // Gather all information needed before rendering as we only want to iterate through the list once
        ItemsToDrawList = [];
        foreach (var item in FormattedItemList)
        {
            if (item == null || item.Element.Address == 0) continue; // Item is fucked, skip
            if (!item.Element.IsVisible && item.ItemType != ItemTypes.None)
                continue; // Disregard non visible items as that usually means they aren't part of what we want to look at

            StashTabValue += item.PriceData.MinChaosValue;
            ItemsToDrawList.Add(item);
        }
        if (InventoryPanel.IsVisible)
        {
            if (ShouldUpdateValuesInventory())
            {
                // Format Inventory Items
                InventoryItemList = GetInventoryItems();
                FormattedInventoryItemList = FormatItems(InventoryItemList);

                if (Settings.DebugSettings.EnableDebugLogging)
                    LogMessage($"{GetCurrentMethod()}.Render() Looping if (ShouldUpdateValuesInventory())", 5,
                        Color.LawnGreen);

                GetValue(FormattedInventoryItemList);
            }

            // Gather all information needed before rendering as we only want to iterate through the list once
            InventoryItemsToDrawList = new List<CustomItem>();
            foreach (var item in FormattedInventoryItemList)
            {
                if (item == null || item.Element.Address == 0) continue; // Item is fucked, skip
                if (!item.Element.IsVisible && item.ItemType != ItemTypes.None)
                    continue; // Disregard non visible items as that usually means they aren't part of what we want to look at

                InventoryTabValue += item.PriceData.MinChaosValue;
                InventoryItemsToDrawList.Add(item);
            }
        }

        GetHoveredItem(); // Get information for the hovered item
        DrawGraphics();
    }

    public void DrawGraphics()
    {
        ProcessItemsOnGround();
        ProcessTradeWindow();
        ProcessHoveredItem();
        VisibleInventoryValue();
        ProcessExchangeCurrencyPicker();

        if (StashPanel.IsVisible)
        {
            VisibleStashValue();

            var tabType = StashPanel.VisibleStash?.InvType;
            var layout = Settings.StashValueSettings.GetPriceOverlayLayout(tabType);
            if (!Settings.PriceOverlaySettings.Show ||
                Settings.PriceOverlaySettings.DoNotDrawWhileAnItemIsHovered && HoveredItem != null ||
                !layout.Enabled) return;

            foreach (var customItem in ItemsToDrawList.Where(customItem => customItem.ItemType != ItemTypes.None))
            {
                PriceBoxOverItem(customItem, null, null, null, layout);
            }
        }
        else if (Settings.LeagueSpecificSettings.ShowRitualWindowPrices && GameController.IngameState.IngameUi.RitualWindow.IsVisible ||
                 Settings.LeagueSpecificSettings.ShowPurchaseWindowPrices && (GameController.IngameState.IngameUi.PurchaseWindow.IsVisible ||
                                                                              GameController.IngameState.IngameUi.PurchaseWindowHideout.IsVisible))
        {
            if (!Settings.PriceOverlaySettings.Show || Settings.PriceOverlaySettings.DoNotDrawWhileAnItemIsHovered && HoveredItem != null) return;
            foreach (var customItem in ItemsToDrawList.Where(customItem => customItem.ItemType != ItemTypes.None))
            {
                DrawItemPriceInline(customItem);
            }
        }
    }

    private void ProcessExchangeCurrencyPicker()
    {
        if (GameController.IngameState.IngameUi.CurrencyExchangePanel?.CurrencyPicker is { IsValid: true, IsVisible: true } picker)
        {
            var pickerRect = picker.OptionContainer.GetClientRectCache;
            var anyIntersect = false;
            Element tooltip = null;
            if (GameController.Game.IngameState.UIHover is { Address: not 0, IsValid: true } hover &&
                hover.Tooltip is { IsValid: true, IsVisible: true } foundTooltip)
            {
                tooltip = foundTooltip;
            }

            foreach (var currencyOption in picker.Options)
            {
                var optionRect = currencyOption.GetClientRectCache;
                if (pickerRect.Contains(optionRect.TopLeft))
                {
                    anyIntersect = true;
                    if (currencyOption.ItemType is { } itemType)
                    {
                        var item = new CustomItem(itemType);
                        GetValue(item);
                        var topRight = optionRect.TopRight;
                        var bottomRight = optionRect.BottomRight;
                        var typePrice = item.PriceData.MinChaosValue;
                        {
                            var text = typePrice.FormatNumber(Settings.VisualPriceSettings.SignificantDigits.Value);
                            var textSize = Graphics.MeasureText(text);
                            var textRect = new RectangleF(topRight.X - textSize.X, topRight.Y, textSize.X, textSize.Y);
                            if ((HoveredItemTooltipRect?.Intersects(textRect) ?? false) ||
                                (tooltip?.GetClientRectCache.Intersects(textRect) ?? false))
                            {
                                continue;
                            }

                            var color = Settings.VisualPriceSettings.FontColor.Value;
                            Graphics.DrawTextWithBackground(text, topRight, color, FontAlign.Right, Color.Black);
                        }

                        if (currencyOption.Owned is > 0 and var owned)
                        {
                            var totalOwned = typePrice * owned;
                            var text2 = $"Owned: {totalOwned.FormatNumber(Settings.VisualPriceSettings.SignificantDigits.Value)}";
                            var textSize2 = Graphics.MeasureText(text2);
                            var textRect2 = new RectangleF(bottomRight.X - textSize2.X, bottomRight.Y - textSize2.Y, textSize2.X, textSize2.Y);
                            if ((HoveredItemTooltipRect?.Intersects(textRect2) ?? false) ||
                                (tooltip?.GetClientRectCache.Intersects(textRect2) ?? false))
                            {
                                continue;
                            }
                            
                            Graphics.DrawTextWithBackground(text2, textRect2.TopLeft, totalOwned >= Settings.VisualPriceSettings.ValuableColorThreshold
                                ? Settings.VisualPriceSettings.ValuableColor
                                : Settings.VisualPriceSettings.FontColor, Color.Black);
                        }
                    }
                }
                else if (anyIntersect)
                {
                    break;
                }
            }
        }
    }

    private void DrawItemPriceInline(CustomItem customItem)
    {
        var text = customItem.PriceData.MinChaosValue.FormatNumber(Settings.VisualPriceSettings.SignificantDigits.Value);
        var textSize = Graphics.MeasureText(text);
        var topRight = customItem.Element.GetClientRectCache.TopRight;
        if (HoveredItemTooltipRect?.Intersects(new RectangleF(topRight.X - textSize.X, topRight.Y, textSize.X, textSize.Y)) ?? false)
        {
            return;
        }

        var (textColor, backgroundColor) = GetOverlayColors(customItem.PriceData.MinChaosValue);
        var textCenter = new Vector2(topRight.X - textSize.X / 2, topRight.Y);
        Graphics.DrawTextWithBackground(text,
            textCenter,
            textColor, FontAlign.Center, backgroundColor);
    }

    private void ProcessHoveredItem()
    {
        if (!Settings.HoveredItemSettings.Show) return;
        if (HoveredItem == null) return;
        if (HoveredItem.ItemType == ItemTypes.None)
        {
            if (!Settings.DebugSettings.EnableDebugLogging) return;

            if (ImGui.BeginTooltip())
            {
                ImGui.Text($"ItemType: {HoveredItem.ItemType}");
                ImGui.Text($"UniqueName: {HoveredItem.UniqueName}");
                ImGui.Text($"BaseName: {HoveredItem.BaseName}");
                ImGui.Text($"ClassName: {HoveredItem.ClassName}");
                ImGui.Text($"Path: {HoveredItem.Path}");
                ImGui.Text($"Rarity: {HoveredItem.Rarity}");
                ImGui.EndTooltip();
            }

            return;
        }
        var textSections = new List<string> { "" };
        void AddSection() => textSections.Add("");
        void AddText(string text) => textSections[^1] += text;

        var changeText = $"Change in last 7 Days: {HoveredItem.PriceData.ChangeInLast7Days:+#;-#;0}%";
        var changeTextLength = changeText.Length - 1;
        var sectionBreak = $"\n{new string('-', changeTextLength)}\n";
        if (Math.Abs(HoveredItem.PriceData.ChangeInLast7Days) > 0.5)
        {
            AddText(changeText);
        }

        var priceInChaos = HoveredItem.PriceData.MinChaosValue;
        var priceInDivines = priceInChaos / DivinePrice;
        var priceInDivinesText = priceInDivines.FormatNumber(2);
        var minPriceText = priceInChaos.FormatNumber(2, Settings.VisualPriceSettings.MaximalValueForFractionalDisplay);
        AddSection();
        switch (HoveredItem.ItemType)
        {
            case ItemTypes.Currency:
            case ItemTypes.Essence:
            case ItemTypes.Rune:
            case ItemTypes.Fragment:
            case ItemTypes.Catalyst:
            case ItemTypes.Delirium:
            case ItemTypes.DivinationCard:
            case ItemTypes.Ultimatum:
            case ItemTypes.Expedition:
            case ItemTypes.Talisman:
            case ItemTypes.Omen:
            case ItemTypes.Abyss:
            case ItemTypes.Verisium:
            case ItemTypes.Idol:
                if (priceInDivines >= 0.1)
                {
                    var priceInDivinessPerOne = priceInDivines / HoveredItem.CurrencyInfo.StackSize;
                    AddText(priceInDivinessPerOne >= 0.1
                        ? $"\nDivine: {priceInDivinesText}d ({priceInDivinessPerOne.FormatNumber(2)}d per one)"
                        : $"\nDivine: {priceInDivinesText}d");
                }
                AddText($"\nExalt: {minPriceText}ex ({(priceInChaos / HoveredItem.CurrencyInfo.StackSize).FormatNumber(2, Settings.VisualPriceSettings.MaximalValueForFractionalDisplay)}ex per one)");
                break;
            case ItemTypes.UniqueAccessory:
            case ItemTypes.UniqueArmour:
            case ItemTypes.UniqueFlask:
            case ItemTypes.UniqueJewel:
            case ItemTypes.UniqueWeapon:
            case ItemTypes.UniqueCharm:
            case ItemTypes.UniqueTablet:
            case ItemTypes.Relic:
                if (HoveredItem.UniqueNameCandidates.Any())
                {
                    AddText(HoveredItem.UniqueNameCandidates.Count == 1
                        ? $"\nIdentified as: {HoveredItem.UniqueNameCandidates.First()}"
                        : $"\nIdentified as one of:\n{string.Join('\n', HoveredItem.UniqueNameCandidates.Select(x => $"{x}"))}");
                }

                AddSection();
                if (priceInDivines >= 0.1)
                {
                    var maxDivinePriceText = (HoveredItem.PriceData.MaxChaosValue / DivinePrice).FormatNumber(2);
                    AddText(priceInDivinesText != maxDivinePriceText 
                        ? $"\nDivine: {priceInDivinesText}d - {maxDivinePriceText}d" 
                        : $"\nDivine: {priceInDivinesText}d");
                }

                var maxPriceText = HoveredItem.PriceData.MaxChaosValue.FormatNumber(2, Settings.VisualPriceSettings.MaximalValueForFractionalDisplay);
                AddText(minPriceText != maxPriceText 
                    ? $"\nExalt: {minPriceText}ex - {maxPriceText}ex" 
                    : $"\nExalt: {minPriceText}ex");

                break;
            case ItemTypes.SkillGem:
            case ItemTypes.UncutGem:
            case ItemTypes.Tablet:
                if (priceInDivines >= 0.1)
                {
                    AddText($"\nDivine: {priceInDivinesText}d");
                }

                AddText($"\nExalt: {minPriceText}ex");
                break;
        }

        if (Settings.DebugSettings.EnableDebugLogging)
        {
            AddSection();
            AddText($"\nItemType: {HoveredItem.ItemType}"
                    + $"\nDetailsId: {HoveredItem.PriceData.DetailsId}"
                    + $"\nBaseName: {HoveredItem.BaseName}"
                    + $"\nClassName: {HoveredItem.ClassName}"
                    + $"\nPath: {HoveredItem.Path}"
                    + $"\nUniqueName: {HoveredItem.UniqueName}"
                    + $"\nRarity: {HoveredItem.Rarity}"
            );
        } 
                
        if (Settings.LeagueSpecificSettings.ShowArtifactChaosPrices)
        {
            if (TryGetArtifactPrice(HoveredItem, out var amount, out var artifactName))
            {
                AddSection();
                AddText($"\nArtifact price: ({(priceInChaos / amount * 100).FormatNumber(2)}ex per 100 {artifactName})");
            }
        }

        var tooltipText = string.Join(sectionBreak, textSections.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        if (!string.IsNullOrWhiteSpace(tooltipText))
        {
            ImGui.BeginTooltip();
            var hoverTextColor = priceInChaos >= Settings.VisualPriceSettings.ExtraValuableColorThreshold.Value
                ? Settings.VisualPriceSettings.ExtraValuableColor
                : priceInChaos >= Settings.VisualPriceSettings.ValuableColorThreshold.Value
                    ? Settings.VisualPriceSettings.ValuableColor
                    : priceInChaos >= Settings.VisualPriceSettings.SemiValuableColorThreshold.Value
                        ? Settings.VisualPriceSettings.SemiValuableColor
                        : null;
            if (hoverTextColor != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, hoverTextColor.Value.ToImgui());
            }

            ImGui.TextUnformatted(tooltipText);
            if (hoverTextColor != null)
            {
                ImGui.PopStyleColor();
            }

            ImGui.EndTooltip();
        }
    }

    private void VisibleStashValue()
    {
        try
        {
            if (!Settings.StashValueSettings.Show || !StashPanel.IsVisible) return;
            {
                var pos = new Vector2(Settings.StashValueSettings.PositionX.Value, Settings.StashValueSettings.PositionY.Value);
                var chaosValue = StashTabValue;
                var topValueItems = GetTopValueItems(ItemsToDrawList)
                    .Take(Settings.StashValueSettings.TopValuedItemCount.Value)
                    .ToList();

                DrawWorthWidget(chaosValue, pos, Settings.VisualPriceSettings.SignificantDigits.Value, Settings.VisualPriceSettings.FontColor, Settings.StashValueSettings.EnableBackground,
                    topValueItems);
            }
        }
        catch (Exception e)
        {
            // ignored
            if (Settings.DebugSettings.EnableDebugLogging)
            {
                LogMessage("Error in: VisibleStashValue, restart PoEHUD.", 5, Color.Red);
                LogMessage(e.ToString(), 5, Color.Orange);
            }
        }
    }

    private static IEnumerable<CustomItem> GetTopValueItems(List<CustomItem> items)
    {
        return items
            .Where(x => x.PriceData.MinChaosValue != 0)
            .GroupBy(x => (x.PriceData.DetailsId, x.BaseName, x.UniqueName, x.ItemType))
            .Select(group => new CustomItem
            {
                PriceData = { MinChaosValue = group.Sum(i => i.PriceData.MinChaosValue) },
                CurrencyInfo = { StackSize = group.Sum(i => i.CurrencyInfo.StackSize) },
                BaseName = string.IsNullOrWhiteSpace(group.Key.UniqueName) 
                    ? group.Key.BaseName 
                    : group.Key.UniqueName,
            })
            .OrderByDescending(x => x.PriceData.MinChaosValue);
    }

    private void DrawWorthWidget(double chaosValue, Vector2 pos, int significantDigits, Color textColor, bool drawBackground, List<CustomItem> topValueItems) => DrawWorthWidget("", false, chaosValue, pos, significantDigits, textColor, drawBackground, topValueItems);
    private void DrawWorthWidget(string initialString, bool indent, double chaosValue, Vector2 pos, int significantDigits, Color textColor, bool drawBackground, List<CustomItem> topValueItems)
    {
        var text = $"{initialString}{(indent ? "\t" : "")}Exalt: {chaosValue.FormatNumber(significantDigits)}" + (DivinePrice != null
            ? $"\n{(indent ? "\t" : "")}Divine: {(chaosValue / DivinePrice).FormatNumber(significantDigits)}"
            : "");
        if (topValueItems.Count > 0)
        {
            var maxChaosValueLength = topValueItems.Max(x => x.PriceData.MinChaosValue.FormatNumber(2, forceDecimals: true).Length);
            var topValuedTexts = string.Join("\n",
                topValueItems.Select(x => $"{x.PriceData.MinChaosValue.FormatNumber(2, forceDecimals: true).PadLeft(maxChaosValueLength)}: {x}" +
                                          (x.CurrencyInfo.StackSize > 0 ? $" ({x.CurrencyInfo.StackSize})" : null)));
            text += $"\nTop value:\n{topValuedTexts}";
        }

        var box = Graphics.DrawText(text, pos, textColor);
        if (drawBackground)
        {
            Graphics.DrawBox(pos, pos + new Vector2(box.X, box.Y), Color.Black);
        }
    }

    private void VisibleInventoryValue()
    {
        try
        {
            var inventory = GameController.Game.IngameState.IngameUi.InventoryPanel;
            if (!Settings.InventoryValueSettings.Show.Value || !inventory.IsVisible) return;
            {
                var pos = new Vector2(Settings.InventoryValueSettings.PositionX.Value, Settings.InventoryValueSettings.PositionY.Value);
                DrawWorthWidget(InventoryTabValue, pos, Settings.VisualPriceSettings.SignificantDigits.Value, Settings.VisualPriceSettings.FontColor, false, []);
            }
        }
        catch (Exception e)
        {
            // ignored
            if (Settings.DebugSettings.EnableDebugLogging)
            {

                LogMessage("Error in: VisibleInventoryValue, restart PoEHUD.", 5, Color.Red);
                LogMessage(e.ToString(), 5, Color.Orange);
            }
        }
    }

    private (Color TextColor, Color BackgroundColor) GetOverlayColors(double chaosValue)
    {
        if (chaosValue >= Settings.VisualPriceSettings.ExtraValuableColorThreshold.Value)
        {
            return (Settings.VisualPriceSettings.ExtraValuableColor, Settings.VisualPriceSettings.ExtraValuableBackgroundColor);
        }

        if (chaosValue >= Settings.VisualPriceSettings.ValuableColorThreshold.Value)
        {
            return (Settings.VisualPriceSettings.ValuableColor, Settings.VisualPriceSettings.BackgroundColor);
        }

        if (chaosValue >= Settings.VisualPriceSettings.SemiValuableColorThreshold.Value)
        {
            return (Settings.VisualPriceSettings.SemiValuableColor, Settings.VisualPriceSettings.BackgroundColor);
        }

        return (Settings.VisualPriceSettings.FontColor, Settings.VisualPriceSettings.BackgroundColor);
    }

    private void PriceBoxOverItem(CustomItem item, RectangleF? containerBox, Color? textColor = null, Color? backgroundColor = null, StashPriceOverlayLayout layout = null)
    {
        var itemValue = item.PriceData.MinChaosValue;

        if (Settings.PriceOverlaySettings.ShowAboveMinValueOnly && Settings.PriceOverlaySettings.MinValueForDisplay >= itemValue) return;

        layout ??= new StashPriceOverlayLayout();

        var box = item.Element.GetClientRect();
        var h = Math.Abs(Settings.PriceOverlaySettings.BoxHeight.Value);

        const float gap = 2f;
        var barTopY = (layout.Vertical, layout.Edge) switch
        {
            (PriceOverlayVertical.Top, PriceOverlayEdge.Outside) => box.Top - gap - h,
            (PriceOverlayVertical.Top, PriceOverlayEdge.Inside) => box.Top + gap,
            (PriceOverlayVertical.Bottom, PriceOverlayEdge.Outside) => box.Bottom + gap,
            (PriceOverlayVertical.Bottom, PriceOverlayEdge.Inside) => box.Bottom - gap - h,
            _ => throw new ArgumentOutOfRangeException(nameof(layout), $"{layout.Vertical}, {layout.Edge}", "Unexpected price overlay layout.")
        };

        var drawBox = new RectangleF(box.X, barTopY, box.Width, h);

        (containerBox ?? default).Contains(ref drawBox, out var contains);
        if (containerBox != null && !contains || drawBox.Intersects(HoveredItem?.Element?.Tooltip?.GetClientRectCache ?? default)) return;
        var overlayColors = GetOverlayColors(item.PriceData.MinChaosValue);
        Graphics.DrawBox(drawBox, backgroundColor ?? overlayColors.BackgroundColor);
        var textPosition = new Vector2(drawBox.Center.X, drawBox.Center.Y - ImGui.GetTextLineHeight() / 2);

        if (Settings.PriceOverlaySettings.ShowUnitValue)
        {
            itemValue /= item.CurrencyInfo.StackSize;
            if (itemValue < Settings.PriceOverlaySettings.UnitValueHintThreshold) textColor = Color.Red;
        }

        Graphics.DrawText(itemValue.FormatNumber(Settings.VisualPriceSettings.SignificantDigits.Value), textPosition, textColor ?? overlayColors.TextColor, FontAlign.Center);
    }

    private void ProcessTradeWindow()
    {
        if (!Settings.TradeWindowSettings.Show) return;

        var (yourItems, theirItems, element) =
            (GameController.IngameState.IngameUi.TradeWindow,
             GameController.IngameState.IngameUi.SellWindow,
             GameController.IngameState.IngameUi.SellWindowHideout)
                switch
                {
                    ({ IsVisible: true } trade, _, _) => (trade.YourOffer, trade.OtherOffer, trade.SellDialog),
                    (_, { IsVisible: true } sell, _) => (sell.YourOfferItems, sell.OtherOfferItems, sell.SellDialog),
                    (_, _, { IsVisible: true } sellHideout) => (sellHideout.YourOfferItems, sellHideout.OtherOfferItems, sellHideout.SellDialog),
                    (_, _, _) => (null, null, null),
                };
        if (yourItems == null || theirItems == null || element == null || yourItems.Count + theirItems.Count == 0)
        {
            return;
        }

        var yourFormattedItems = GetValue(FormatItems(yourItems));
        var theirFormatterItems = GetValue(FormatItems(theirItems));
        var yourTradeWindowValue = yourFormattedItems.Sum(x => x.PriceData.MinChaosValue);
        var theirTradeWindowValue = theirFormatterItems.Sum(x => x.PriceData.MinChaosValue);
        var textPosition = new Vector2(element.GetClientRectCache.Right, element.GetClientRectCache.Center.Y - ImGui.GetTextLineHeight() * 3) 
                         + new Vector2(Settings.TradeWindowSettings.OffsetX, Settings.TradeWindowSettings.OffsetY);
        DrawWorthWidget("Theirs\n", true, theirTradeWindowValue, textPosition, 2, Settings.VisualPriceSettings.FontColor, true, []);
        textPosition.Y += ImGui.GetTextLineHeight() * 3;
        var diff = theirTradeWindowValue - yourTradeWindowValue;
        DrawWorthWidget("Profit/Loss\n", true, diff, textPosition, 2, diff switch { > 0 => Color.Green, 0 => Settings.VisualPriceSettings.FontColor, < 0 => Color.Red, double.NaN => Color.Purple }, true, []);
        textPosition.Y += ImGui.GetTextLineHeight() * 3;
        DrawWorthWidget("Yours\n", true, yourTradeWindowValue, textPosition, 2, Settings.VisualPriceSettings.FontColor, true, []);
    }

    private void ProcessItemsOnGround()
    {
        if (!Settings.GroundItemSettings.PriceItemsOnGround && !Settings.UniqueIdentificationSettings.ShowRealUniqueNameOnGround && !Settings.GroundItemSettings.PriceHeistRewards) return;
        //this window allows us to change the size of the text we draw to the background list
        //yeah, it's weird
        ImGui.Begin("lmao",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        var drawList = ImGui.GetBackgroundDrawList();
        var tooltipRect = HoveredItem?.Element.AsObject<HoverItemIcon>()?.Tooltip?.GetClientRect() ?? new RectangleF(0, 0, 0, 0);
        var leftPanelRect = GameController.IngameState.IngameUi.OpenLeftPanel.Address != 0
                                ? GameController.IngameState.IngameUi.OpenLeftPanel.GetClientRectCache
                                : RectangleF.Empty;
        var rightPanelRect = GameController.IngameState.IngameUi.OpenRightPanel.Address != 0
                                 ? GameController.IngameState.IngameUi.OpenRightPanel.GetClientRectCache
                                 : RectangleF.Empty;
        foreach (var (item, processingType, clientRect) in _groundItems.Value)
        {
            var box = clientRect ?? item.Element.GetClientRect();
            switch (processingType)
            {
                case GroundItemProcessingType.WorldItem:
                {
                    if (Settings.SoundNotificationSettings.Enabled && 
                        !_soundPlayedTracker.ContainsKey(item.EntityId))
                    {
                        var matchingCustomFile =
                            item.UniqueNameCandidates.Any() ||
                            !string.IsNullOrEmpty(item.UniqueName)
                                ? item.UniqueNameCandidates
                                    .DefaultIfEmpty(item.UniqueName)
                                    .Select(x => _soundFiles.GetValueOrDefault(x))
                                    .FirstOrDefault(x => x != null)
                                : null;
                        if (item.PriceData.MaxChaosValue >= Settings.SoundNotificationSettings.ValueThreshold ||
                            Settings.SoundNotificationSettings.PlayCustomSoundsIfBelowThreshold && matchingCustomFile != null)
                        {
                            if (_soundPlayedTracker.TryAdd(item.EntityId, true))
                            {
                                var defaultFile = Path.Join(ConfigDirectory, "default.wav");
                                if (matchingCustomFile != null && !File.Exists(matchingCustomFile))
                                {
                                    LogError($"Unable to find {matchingCustomFile}. It was probably deleted. Reload the sound list to update your preferences");
                                    matchingCustomFile = null;
                                }

                                var fileToPlay = matchingCustomFile ?? defaultFile;

                                if (File.Exists(fileToPlay))
                                {
                                    if (!GameController.SoundController.HasSound(fileToPlay))
                                    {
                                        GameController.SoundController.PreloadSound(fileToPlay);
                                    }

                                    GameController.SoundController.PlaySound(fileToPlay, Settings.SoundNotificationSettings.Volume);
                                }
                                else if (fileToPlay == defaultFile)
                                {
                                    LogError(
                                        $"Unable to find the default sound file ({defaultFile}) to play. Disable the sound notification feature, reload the sound list to let the plugin create it, or create it yourself");
                                }
                            }
                        }
                    }

                    if (!tooltipRect.Intersects(box) && !leftPanelRect.Intersects(box) && !rightPanelRect.Intersects(box))
                    {
                        var isValuable = item.PriceData.MaxChaosValue >= Settings.VisualPriceSettings.ValuableColorThreshold.Value;

                        if (Settings.GroundItemSettings.PriceItemsOnGround &&
                            (Settings.GroundItemSettings.OnlyPriceItemsAboveThreshold
                                ? item.PriceData.MinChaosValue >= Settings.GroundItemSettings.ValueThreshold
                                : item.PriceData.MinChaosValue > 0) &&
                            (!Settings.GroundItemSettings.OnlyPriceUniquesOnGround || item.Rarity == ItemRarity.Unique))
                        {
                            var s = item.PriceData.MinChaosValue.FormatNumber(2);
                            if (item.PriceData.MaxChaosValue > item.PriceData.MinChaosValue)
                                s += $"-{item.PriceData.MaxChaosValue.FormatNumber(2)}";

                            using (Graphics.SetTextScale(Settings.GroundItemSettings.GroundPriceTextScale))
                            {
                                var textSize = Graphics.MeasureText(s);
                                var textPos = new Vector2(box.Right - textSize.X, box.Top);
                                Graphics.DrawBox(textPos, new Vector2(box.Right, box.Top + textSize.Y), Settings.GroundItemSettings.GroundPriceBackgroundColor);
                                Graphics.DrawText(s, textPos, isValuable ? Settings.VisualPriceSettings.ValuableColor : Settings.VisualPriceSettings.FontColor);
                            }
                        }

                        if (Settings.UniqueIdentificationSettings.ShowRealUniqueNameOnGround && !item.IsIdentified && item.Rarity == ItemRarity.Unique)
                        {
                            float GetRatio(string text)
                            {
                                var textSize = Graphics.MeasureText(text);
                                return Math.Min(box.Width * Settings.UniqueIdentificationSettings.UniqueLabelSize / textSize.X, (box.Height - 2) / textSize.Y);
                            }

                            void DrawOnItemLabel(float scale, string text, Color backgroundColor, Color textColor)
                            {
                                ImGui.SetWindowFontScale(scale);
                                var newTextSize = ImGui.CalcTextSize(text);
                                var textPosition = box.Center - newTextSize / 2;
                                var rectPosition = new Vector2(textPosition.X, box.Top + 1);
                                drawList.AddRectFilled(rectPosition, rectPosition + new Vector2(newTextSize.X, box.Height - 2), backgroundColor.ToImgui());
                                drawList.AddText(textPosition, textColor.ToImgui(), text);
                                ImGui.SetWindowFontScale(1);
                            }

                            if (item.UniqueNameCandidates.Any())
                            {
                                if (Settings.UniqueIdentificationSettings.OnlyShowRealUniqueNameForValuableUniques && !isValuable)
                                {
                                    continue;
                                }

                                var textColor = isValuable ? Settings.UniqueIdentificationSettings.ValuableUniqueItemNameTextColor : Settings.UniqueIdentificationSettings.UniqueItemNameTextColor;
                                var backgroundColor = isValuable
                                    ? Settings.UniqueIdentificationSettings.ValuableUniqueItemNameBackgroundColor
                                    : Settings.UniqueIdentificationSettings.UniqueItemNameBackgroundColor;
                                var (text, ratio) = Enumerable.Range(1, item.UniqueNameCandidates.Count).Select(perOneLine =>
                                        string.Join('\n', MoreLinq.Extensions.BatchExtension.Batch(item.UniqueNameCandidates, perOneLine)
                                            .Select(onLine => string.Join(" / ", onLine))))
                                    .Select(text => (text, ratio: GetRatio(text)))
                                    .MaxBy(x => x.ratio);

                                DrawOnItemLabel(ratio, text, backgroundColor, textColor);
                            }
                            else if (Settings.UniqueIdentificationSettings.ShowWarningTextForUnknownUniques)
                            {
                                const string text = "???";
                                var ratio = GetRatio(text);
                                DrawOnItemLabel(ratio, text, Color.Blue, Color.Red);
                            }
                        }
                    }
                    break;
                }
                case GroundItemProcessingType.HeistReward:
                {
                    if (Settings.GroundItemSettings.PriceHeistRewards && !leftPanelRect.Contains(box.TopRight) && !rightPanelRect.Contains(box.TopRight))
                    {
                        if (item.PriceData.MinChaosValue > 0)
                        {
                            var s = item.PriceData.MinChaosValue.FormatNumber(2);
                            if (item.PriceData.MaxChaosValue > item.PriceData.MinChaosValue)
                            {
                                s += $"-{item.PriceData.MaxChaosValue.FormatNumber(2)}";
                            }

                            using (Graphics.SetTextScale(Settings.GroundItemSettings.GroundPriceTextScale))
                            {
                                var textSize = Graphics.MeasureText(s);
                                var textPos = new Vector2(box.Right - textSize.X, box.Top);
                                Graphics.DrawBox(textPos, textPos + textSize, Settings.GroundItemSettings.GroundPriceBackgroundColor);
                                Graphics.DrawText(s, textPos, Settings.VisualPriceSettings.FontColor);
                            }
                        }
                    }

                    break;
                }
            }
                
        }
        
        ImGui.End();
    }

    private bool TryGetArtifactPrice(CustomItem item, out double amount, out string artifactName)
    {
        amount = 0;
        artifactName = null;
        if (item?.Element == null)
            return false;

        Element GetElementByString(Element element, string str, int maxDepth)
        {
            if (element == null || string.IsNullOrWhiteSpace(str) || !element.IsValid)
                return null;

            if (element.Text?.Trim() == str)
                return element;

            if (maxDepth <= 0)
                return null;

            return element.Children.Select(c => GetElementByString(c, str, maxDepth - 1)).FirstOrDefault(e => e != null);
        }

        var costElement = GetElementByString(item.Element?.AsObject<HoverItemIcon>()?.Tooltip, "Cost:", 15);
        if (costElement?.Parent == null || 
            costElement.Parent.ChildCount < 2 ||
            costElement.Parent.GetChildAtIndex(1).ChildCount < 3)
            return false;
        var amountText = costElement.Parent.GetChildFromIndices(1, 0)?.Text;
        if (amountText == null)
            return false;
        artifactName = costElement.Parent.GetChildFromIndices(1, 2)?.Text;
        if (artifactName == null)
            return false;
        if (costElement.Text.Equals("Cost:")) // Tujen haggling
        {
            if (!int.TryParse(amountText.TrimEnd('x').Replace(".", null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amountInt))
            {
                return false;
            }

            amount = amountInt;
            return true;
        }

        if (costElement.Text.Equals("Cost Per Unit:")) // Artifact stacks (Dannig)
        {
            if (!double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var costPerUnit))
            {
                return false;
            }

            amount = item.CurrencyInfo.StackSize * costPerUnit;
            return true;
        }

        return false;
    }
}

internal record ItemOnGround(CustomItem Item, GroundItemProcessingType Type, RectangleF? ClientRect);
