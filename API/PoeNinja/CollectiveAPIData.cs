using System;
using System.Linq;
using NinjaPricer.API.PoeNinja.Models;

namespace NinjaPricer.API.PoeNinja;

public class CollectiveApiData
{
    public ExchangeOverview Currency { get; set; }
    public ExchangeOverview Breach { get; set; }
    public ExchangeOverview Delirium { get; set; }
    public ExchangeOverview Essences { get; set; }
    public ExchangeOverview Runes { get; set; }
    public ExchangeOverview Ritual { get; set; }
    public ExchangeOverview Fragments { get; set; }
    public ExchangeOverview UncutGems { get; set; }
    public ExchangeOverview Abyss { get; set; }
    public ExchangeOverview Expedition { get; set; }
    public ExchangeOverview Verisium { get; set; }
    public ExchangeOverview LineageSupportGems { get; set; }
    public ExchangeOverview SoulCores { get; set; }
    public ExchangeOverview Idols { get; set; }

    public StashOverview Weapons { get; set; }
    public StashOverview Armour { get; set; }
    public StashOverview Accessories { get; set; }
    public StashOverview Flasks { get; set; }
    public StashOverview Jewels { get; set; }
    public StashOverview Charms { get; set; }
    public StashOverview SanctumRelics { get; set; }
    public StashOverview Tablets { get; set; }
    public StashOverview UniqueTablets { get; set; }

    public double DivineToExaltedRate { get; set; }
    public double ChaosToExaltedRate { get; set; }

    public double DivineToExaltedRateRaw
    {
        get
        {
            if (Currency.Core.Primary == "divine")
            {
                return Currency.Core.Rates.Exalted.Value;
            }

            if (Currency.Core.Primary == "exalted")
            {
                return Currency.Lines.First(x => x.Id == "divine").PrimaryValue;
            }

            throw new Exception($"Unknown primary {Currency.Core.Primary}");
        }
    }

    public double ChaosToExaltedRateRaw
    {
        get
        {
            if (Currency.Core.Primary == "divine")
            {
                return Currency.Lines.First(x => x.Id == "chaos").PrimaryValue * Currency.Core.Rates.Exalted.Value;
            }

            if (Currency.Core.Primary == "exalted")
            {
                return Currency.Lines.First(x => x.Id == "chaos").PrimaryValue;
            }

            throw new Exception($"Unknown primary {Currency.Core.Primary}");
        }
    }
}
