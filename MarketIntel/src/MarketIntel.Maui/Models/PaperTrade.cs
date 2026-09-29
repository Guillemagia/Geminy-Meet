namespace MarketIntel.Maui.Models;

/// <summary>An open simulated position.</summary>
public sealed class PaperPosition
{
    public string Symbol { get; set; } = "";
    public bool IsLong { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public decimal Shares { get; set; }
    public DateTime OpenedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A closed simulated trade with its realized P&amp;L.</summary>
public sealed class ClosedTrade
{
    public string Symbol { get; set; } = "";
    public bool IsLong { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Shares { get; set; }
    public DateTime ClosedUtc { get; set; } = DateTime.UtcNow;

    public decimal Pnl => (IsLong ? ExitPrice - EntryPrice : EntryPrice - ExitPrice) * Shares;
    public double PnlPercent => EntryPrice > 0
        ? (double)((IsLong ? ExitPrice - EntryPrice : EntryPrice - ExitPrice) / EntryPrice) * 100
        : 0;
}
