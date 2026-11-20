using System.Numerics;

namespace Moneta.Core;

public static class RateConverter
{
    public static decimal ConvertAmount(decimal amount, string sourceCode, Currency target, OfficialRateSet rates)
    {
        ValidateTarget(target);
        ValidateCode(sourceCode, nameof(sourceCode));
        RateRules.Validate(rates);
        var source = FindQuote(sourceCode, rates);
        var destination = FindQuote(target.Code, rates);
        if (sourceCode == target.Code) return decimal.Round(amount, target.DecimalPlaces, MidpointRounding.ToEven);
        return (Fraction.FromDecimal(amount) * Factor(source) / Factor(destination)).Round(target.DecimalPlaces);
    }

    public static decimal ConvertTotal(IEnumerable<CurrencyAmount> amounts, Currency target, OfficialRateSet rates)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        ValidateTarget(target);
        RateRules.Validate(rates);
        var destination = Factor(FindQuote(target.Code, rates));
        var quotes = new Dictionary<string, OfficialRateQuote>(rates.Quotes.Count, StringComparer.Ordinal);
        foreach (var quote in rates.Quotes) quotes.Add(quote.CurrencyCode, quote);
        var total = Fraction.Zero;
        foreach (var amount in amounts)
        {
            if (amount is null) throw new ArgumentException("Un montant natif est absent.", nameof(amounts));
            ValidateCode(amount.CurrencyCode, nameof(amounts));
            OfficialRateQuote? source = null;
            if (amount.CurrencyCode != rates.ReferenceCurrency && !quotes.TryGetValue(amount.CurrencyCode, out source))
                throw Missing(amount.CurrencyCode);
            // Keep reference units exact; divide by the destination only after aggregation.
            total += Fraction.FromDecimal(amount.Amount) * Factor(source);
        }
        return (total / destination).Round(target.DecimalPlaces);
    }

    internal static decimal ConvertQuoted(decimal amount, Currency target, OfficialRateQuote? source, OfficialRateQuote? destination)
    {
        ValidateTarget(target);
        return (Fraction.FromDecimal(amount) * Factor(source) / Factor(destination)).Round(target.DecimalPlaces);
    }

    internal static decimal SumRounded(IEnumerable<decimal> amounts, Currency target)
    {
        ValidateTarget(target);
        var total = Fraction.Zero;
        foreach (var amount in amounts) total += Fraction.FromDecimal(amount);
        return total.Round(target.DecimalPlaces);
    }

    internal static decimal Difference(decimal first, decimal second)
    {
        static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xff;
        return (Fraction.FromDecimal(first) + Fraction.FromDecimal(-second)).Round(Math.Max(Scale(first), Scale(second)));
    }

    internal static decimal Percentage(decimal amount, decimal planned) =>
        (Fraction.FromDecimal(amount) * Fraction.FromDecimal(100) / Fraction.FromDecimal(planned)).Round(1);

    private static OfficialRateQuote? FindQuote(string code, OfficialRateSet rates)
    {
        if (code == rates.ReferenceCurrency) return null;
        foreach (var quote in rates.Quotes)
            if (quote.CurrencyCode == code) return quote;
        throw Missing(code);
    }

    private static Fraction Factor(OfficialRateQuote? quote) => quote is null
        ? Fraction.One
        : Fraction.FromDecimal(quote.OfficialValue) / new Fraction(quote.Nominal, BigInteger.One);

    private static void ValidateTarget(Currency target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateCode(target.Code, nameof(target));
        ArgumentOutOfRangeException.ThrowIfNegative(target.DecimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(target.DecimalPlaces, 28);
    }

    private static void ValidateCode(string code, string parameter)
    {
        if (!RateRules.IsCurrencyCode(code))
            throw new ArgumentException("Le code de devise doit contenir trois lettres ASCII majuscules.", parameter);
    }

    private static RateException Missing(string code) => new(RateFailure.Unavailable, $"Aucun cours officiel de cette publication pour {code}.");

    // Every decimal enters as its exact base-10 coefficient. Only Round materializes money.
    private readonly struct Fraction
    {
        private static readonly BigInteger[] PowersOfTen = CreatePowersOfTen();
        private static readonly BigInteger MaximumCoefficient = (BigInteger.One << 96) - 1;
        public static readonly Fraction Zero = new(BigInteger.Zero, BigInteger.One);
        public static readonly Fraction One = new(BigInteger.One, BigInteger.One);
        private readonly BigInteger _numerator;
        private readonly BigInteger _denominator;

        public Fraction(BigInteger numerator, BigInteger denominator, bool reduced = false)
        {
            if (numerator.IsZero)
            {
                _numerator = BigInteger.Zero;
                _denominator = BigInteger.One;
                return;
            }
            var divisor = reduced ? BigInteger.One : BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            _numerator = numerator / divisor;
            _denominator = denominator / divisor;
        }

        public static Fraction FromDecimal(decimal value)
        {
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value, bits);
            var coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            if (bits[3] < 0) coefficient = -coefficient;
            return new Fraction(coefficient, PowersOfTen[(bits[3] >> 16) & 0xff]);
        }

        public static Fraction operator *(Fraction left, Fraction right)
        {
            if (left._numerator.IsZero || right._numerator.IsZero) return Zero;
            // Cancel before multiplication to keep large balances and nominal scales cheap.
            var first = BigInteger.GreatestCommonDivisor(BigInteger.Abs(left._numerator), right._denominator);
            var second = BigInteger.GreatestCommonDivisor(BigInteger.Abs(right._numerator), left._denominator);
            return new Fraction((left._numerator / first) * (right._numerator / second),
                (left._denominator / second) * (right._denominator / first), reduced: true);
        }

        public static Fraction operator /(Fraction left, Fraction right)
        {
            if (right._numerator.Sign <= 0) throw new InvalidOperationException("Le facteur de change doit être positif.");
            return left * new Fraction(right._denominator, right._numerator, reduced: true);
        }

        public static Fraction operator +(Fraction left, Fraction right)
        {
            if (left._numerator.IsZero) return right;
            if (right._numerator.IsZero) return left;
            var common = BigInteger.GreatestCommonDivisor(left._denominator, right._denominator);
            var leftScale = right._denominator / common;
            var rightScale = left._denominator / common;
            return new Fraction(left._numerator * leftScale + right._numerator * rightScale,
                left._denominator * leftScale);
        }

        public decimal Round(int scale)
        {
            var coefficient = BigInteger.DivRem(BigInteger.Abs(_numerator) * PowersOfTen[scale], _denominator, out var remainder);
            var midpoint = (remainder << 1).CompareTo(_denominator);
            if (midpoint > 0 || (midpoint == 0 && !coefficient.IsEven)) coefficient++;
            // Trailing zeros may be removed without an additional rounding (e.g. MaxValue at precision 28).
            while (coefficient > MaximumCoefficient && scale > 0 && coefficient % 10 == 0)
            {
                coefficient /= 10;
                scale--;
            }
            if (coefficient > MaximumCoefficient)
                throw new OverflowException("Le résultat arrondi n’est pas représentable exactement en décimal.");
            return new decimal((int)(uint)(coefficient & uint.MaxValue),
                (int)(uint)((coefficient >> 32) & uint.MaxValue),
                (int)(uint)(coefficient >> 64), _numerator.Sign < 0 && !coefficient.IsZero, (byte)scale);
        }

        private static BigInteger[] CreatePowersOfTen()
        {
            var powers = new BigInteger[29];
            powers[0] = BigInteger.One;
            for (var index = 1; index < powers.Length; index++) powers[index] = powers[index - 1] * 10;
            return powers;
        }
    }
}
