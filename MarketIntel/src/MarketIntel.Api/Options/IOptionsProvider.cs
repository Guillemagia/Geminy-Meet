using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Options;

/// <summary>Abstraction over an options-chain source. Swap the mock for a real provider later.</summary>
public interface IOptionsProvider
{
    bool IsSynthetic { get; }

    /// <summary>Option contracts around the money for a symbol at the given spot price.</summary>
    Task<IReadOnlyList<OptionContractDto>> GetChainAsync(string symbol, decimal spot, CancellationToken ct = default);
}
