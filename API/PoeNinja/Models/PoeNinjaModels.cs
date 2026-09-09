using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace NinjaPricer.API.PoeNinja.Models;

public class ExchangeOverview
{
    private Dictionary<string, (ExchangeLine Line, ExchangeItem Item)> _linesByName;

    [JsonProperty("core")]
    public CoreData Core { get; set; }

    [JsonProperty("lines")]
    public List<ExchangeLine> Lines { get; set; }

    [JsonProperty("items")]
    public List<ExchangeItem> Items { get; set; }

    public Dictionary<string, (ExchangeLine Line, ExchangeItem Item)> LinesByName
    {
        get
        {
            if (_linesByName == null)
            {
                _linesByName = Items.Join(Lines, i => i.Id, l => l.Id, (i, l) => (i, l))
                    .ToDictionary(p => p.i.Name, p => (p.l, p.i));
            }

            return _linesByName;
        }
    }

    [JsonIgnore]
    public double PrimaryToExaltedRate => Core.Primary == "exalted" ? 1 : Core.Rates.Exalted.Value;
}

public class StashOverview
{
    [JsonProperty("core")]
    public CoreData Core { get; set; }

    [JsonProperty("lines")]
    public List<StashLine> Lines { get; set; }

    [JsonIgnore]
    public double PrimaryToExaltedRate => Core.Primary == "exalted" ? 1 : Core.Rates.Exalted.Value;
}

public class CoreData
{
    [JsonProperty("items")]
    public List<ExchangeItem> Items { get; set; }

    [JsonProperty("rates")]
    public Rates Rates { get; set; }

    [JsonProperty("primary")]
    public string Primary { get; set; }

    [JsonProperty("secondary")]
    public string Secondary { get; set; }
}

public class Rates
{
    [JsonProperty("exalted")]
    public double? Exalted { get; set; }

    [JsonProperty("chaos")]
    public double? Chaos { get; set; }
}

public class ExchangeItem
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("image")]
    public string Image { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("detailsId")]
    public string DetailsId { get; set; }
}

// Line from exchange overview - currency-type items
public class ExchangeLine
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("primaryValue")]
    public double PrimaryValue { get; set; }

    [JsonProperty("volumePrimaryValue")]
    public double VolumePrimaryValue { get; set; }

    [JsonProperty("maxVolumeCurrency")]
    public string MaxVolumeCurrency { get; set; }

    [JsonProperty("maxVolumeRate")]
    public double MaxVolumeRate { get; set; }

    [JsonProperty("sparkline")]
    public Sparkline Sparkline { get; set; }
}

// Line from stash overview - unique items
public class StashLine
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("itemId")]
    public string ItemId { get; set; }

    [JsonProperty("detailsId")]
    public string DetailsId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("baseType")]
    public string BaseType { get; set; }

    [JsonProperty("variant")]
    public string Variant { get; set; }

    [JsonProperty("icon")]
    public string Icon { get; set; }

    [JsonProperty("flavourText")]
    public string FlavourText { get; set; }

    [JsonProperty("levelRequired")]
    public long LevelRequired { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("primaryValue")]
    public double PrimaryValue { get; set; }

    [JsonProperty("listingCount")]
    public long ListingCount { get; set; }

    [JsonProperty("corrupted")]
    public bool Corrupted { get; set; }

    [JsonProperty("sparkLine")]
    public Sparkline Sparkline { get; set; }

    [JsonProperty("implicitModifiers")]
    public List<Modifier> ImplicitModifiers { get; set; }

    [JsonProperty("explicitModifiers")]
    public List<Modifier> ExplicitModifiers { get; set; }

    [JsonProperty("propertyModifiers")]
    public List<Modifier> PropertyModifiers { get; set; }

    [JsonProperty("requirementModifiers")]
    public List<Modifier> RequirementModifiers { get; set; }

    [JsonProperty("utilityModifiers")]
    public List<Modifier> UtilityModifiers { get; set; }

    [JsonProperty("grantedSkillModifiers")]
    public List<Modifier> GrantedSkillModifiers { get; set; }
}

public class Sparkline
{
    [JsonProperty("totalChange")]
    public double TotalChange { get; set; }

    [JsonProperty("data")]
    public List<double?> Data { get; set; }
}

public class Modifier
{
    [JsonProperty("text")]
    public string Text { get; set; }

    [JsonProperty("optional")]
    public bool Optional { get; set; }
}
