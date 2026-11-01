namespace Moneta.Core;

public static class MoneyInput
{
    public static decimal ParseFrench(string text, Currency currency, bool allowNegative = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(currency);
        var input = text.AsSpan().Trim();
        const string invalid = "Montant invalide : saisissez un nombre avec une virgule décimale (exemple : 1 234,56).";
        var negative = false;
        if (!input.IsEmpty && input[0] is '-' or '+')
        {
            if (!allowNegative) throw new ArgumentException(invalid);
            negative = input[0] == '-';
            input = input[1..];
        }
        if (input.IsEmpty) throw new ArgumentException(invalid);
        var comma = input.IndexOf(',');
        var whole = comma < 0 ? input : input[..comma];
        var fraction = comma < 0 ? ReadOnlySpan<char>.Empty : input[(comma + 1)..];
        if (whole.IsEmpty || (comma >= 0 && fraction.IsEmpty)) throw new ArgumentException(invalid);
        // Accumulate exact minor units, retaining the symmetric bounds of ExactMoney.
        long minor = 0;
        var digitsInGroup = 0;
        var grouped = false;
        try
        {
            foreach (var character in whole)
            {
                if (character is ' ' or '\u00a0' or '\u202f')
                {
                    if (digitsInGroup == 0 || (!grouped && digitsInGroup > 3) || (grouped && digitsInGroup != 3))
                        throw new ArgumentException(invalid);
                    grouped = true;
                    digitsInGroup = 0;
                    continue;
                }
                if (character is < '0' or > '9') throw new ArgumentException(invalid);
                digitsInGroup++;
                minor = checked(minor * 10 + (negative ? -(character - '0') : character - '0'));
            }
            if (digitsInGroup == 0 || (grouped && digitsInGroup != 3)) throw new ArgumentException(invalid);
            for (var index = 0; index < fraction.Length; index++)
            {
                var character = fraction[index];
                if (character is < '0' or > '9') throw new ArgumentException(invalid);
                if (index < currency.DecimalPlaces)
                    minor = checked(minor * 10 + (negative ? -(character - '0') : character - '0'));
                else if (character != '0')
                    throw new ArgumentException($"Ce montant dépasse la précision de {currency.DecimalPlaces} décimale(s) de {currency.Code}. Aucun arrondi n’est effectué.");
            }
            for (var index = fraction.Length; index < currency.DecimalPlaces; index++) minor = checked(minor * 10);
            if (minor == long.MinValue) throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw new ArgumentException($"Montant hors limites pour {currency.Code} : le nombre d’unités mineures doit être compris entre {-long.MaxValue} et {long.MaxValue}.");
        }
        return ExactMoney.FromMinorUnits(minor, currency);
    }
}
