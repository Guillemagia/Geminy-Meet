using MarketIntel.Api.Indicators;
using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>Turns raw candles into an <see cref="IndicatorSnapshot"/> and into display DTOs.</summary>
public sealed class IndicatorEngine
{
    public IndicatorSnapshot Compute(IReadOnlyList<CandleDto> candles)
    {
        // Guard against empty/insufficient data (can happen with real providers off-hours).
        if (candles.Count == 0)
            return new IndicatorSnapshot
            {
                Price = double.NaN, Ema9 = double.NaN, Ema20 = double.NaN, Ema50 = double.NaN,
                Ema200 = double.NaN, Sma20 = double.NaN, Sma50 = double.NaN, Rsi = double.NaN,
                MacdLine = double.NaN, MacdSignal = double.NaN, MacdHistogram = double.NaN,
                Atr = double.NaN, Vwap = double.NaN, BollingerUpper = double.NaN,
                BollingerMiddle = double.NaN, BollingerLower = double.NaN,
                RelativeVolume = double.NaN, Adx = double.NaN, Completeness = 0
            };

        var close = candles.Select(c => (double)c.Close).ToArray();
        var high = candles.Select(c => (double)c.High).ToArray();
        var low = candles.Select(c => (double)c.Low).ToArray();
        var vol = candles.Select(c => (double)c.Volume).ToArray();

        var (macd, signal, hist) = IndicatorMath.Macd(close);
        var (bbU, bbM, bbL) = IndicatorMath.Bollinger(close);

        var snap = new IndicatorSnapshot
        {
            Price = close[^1],
            Ema9 = IndicatorMath.Ema(close, 9),
            Ema20 = IndicatorMath.Ema(close, 20),
            Ema50 = IndicatorMath.Ema(close, 50),
            Ema200 = IndicatorMath.Ema(close, 200),
            Sma20 = IndicatorMath.Sma(close, 20),
            Sma50 = IndicatorMath.Sma(close, 50),
            Rsi = IndicatorMath.Rsi(close),
            MacdLine = macd,
            MacdSignal = signal,
            MacdHistogram = hist,
            Atr = IndicatorMath.Atr(high, low, close),
            Vwap = IndicatorMath.Vwap(high, low, close, vol),
            BollingerUpper = bbU,
            BollingerMiddle = bbM,
            BollingerLower = bbL,
            RelativeVolume = IndicatorMath.RelativeVolume(vol),
            Adx = IndicatorMath.Adx(high, low, close),
            Completeness = 0 // set below
        };

        // Completeness = share of indicators that produced a finite number.
        double[] all =
        [
            snap.Ema9, snap.Ema20, snap.Ema50, snap.Rsi, snap.MacdLine, snap.Atr,
            snap.Vwap, snap.BollingerUpper, snap.RelativeVolume, snap.Adx
        ];
        double finite = all.Count(v => !double.IsNaN(v) && !double.IsInfinity(v));
        snap = snap with { Completeness = finite / all.Length };
        return snap;
    }

    /// <summary>Build the display list of indicators shown on a symbol page.</summary>
    public IReadOnlyList<IndicatorDto> ToDisplay(IndicatorSnapshot s)
    {
        var list = new List<IndicatorDto>();

        FactorBias vwapBias = s.Price > s.Vwap ? FactorBias.Bullish : s.Price < s.Vwap ? FactorBias.Bearish : FactorBias.Neutral;
        list.Add(new IndicatorDto("VWAP", s.Vwap.ToString("F2"), vwapBias,
            s.Price > s.Vwap ? Loc.T("Vwap.Above") : Loc.T("Vwap.Below")));

        list.Add(new IndicatorDto("EMA 9", s.Ema9.ToString("F2"),
            s.Price > s.Ema9 ? FactorBias.Bullish : FactorBias.Bearish));
        list.Add(new IndicatorDto("EMA 20", s.Ema20.ToString("F2"),
            s.Price > s.Ema20 ? FactorBias.Bullish : FactorBias.Bearish));
        list.Add(new IndicatorDto("EMA 50", s.Ema50.ToString("F2"),
            s.Price > s.Ema50 ? FactorBias.Bullish : FactorBias.Bearish));

        FactorBias rsiBias = s.Rsi > 55 ? FactorBias.Bullish : s.Rsi < 45 ? FactorBias.Bearish : FactorBias.Neutral;
        list.Add(new IndicatorDto("RSI", s.Rsi.ToString("F0"), rsiBias,
            s.Rsi > 70 ? Loc.T("Ind.Overbought") : s.Rsi < 30 ? Loc.T("Ind.Oversold") : Loc.T("Ind.Neutral")));

        FactorBias macdBias = s.MacdHistogram > 0 ? FactorBias.Bullish : FactorBias.Bearish;
        list.Add(new IndicatorDto("MACD", s.MacdHistogram.ToString("F2"), macdBias,
            s.MacdHistogram > 0 ? Loc.T("Ind.MACD.Positive") : Loc.T("Ind.MACD.Negative")));

        FactorBias volBias = s.RelativeVolume >= 1.5 ? FactorBias.Bullish : FactorBias.Neutral;
        list.Add(new IndicatorDto(Loc.T("Ind.Vol.Name"), $"{s.RelativeVolume:F1}x", volBias,
            s.RelativeVolume >= 1.5 ? Loc.T("Ind.Vol.High") : Loc.T("Ind.Vol.Normal")));

        FactorBias adxBias = s.Adx > 25 ? FactorBias.Bullish : FactorBias.Neutral;
        list.Add(new IndicatorDto("ADX", s.Adx.ToString("F0"), adxBias,
            s.Adx > 25 ? Loc.T("Ind.ADX.Strong") : Loc.T("Ind.ADX.Weak")));

        list.Add(new IndicatorDto("ATR", s.Atr.ToString("F2"), FactorBias.Neutral, Loc.T("Ind.ATR.Detail")));

        return list;
    }
}
