namespace TNLorry.Utils
{
    /// <summary>
    /// Indian digit grouping: 12,34,567 — not 1,234,567. Players notice immediately when a
    /// rupee figure is grouped the Western way, and `ToString("N0")` with an Indian culture is
    /// not reliable across IL2CPP stripping levels, so this is done by hand.
    ///
    /// Extracted from the HUD so it is testable and so the result screen, shop, job board and
    /// bank all format money identically.
    /// </summary>
    public static class CurrencyFormat
    {
        public const string RupeeSign = "₹";

        /// <summary>₹45,20,000 — the lakh/crore grouping, with sign.</summary>
        public static string Rupees(long amount)
        {
            // System.Math.Abs, not Mathf.Abs: the latter coerces to float and large balances
            // would render in scientific notation.
            string digits = Group(System.Math.Abs(amount)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
            return (amount < 0 ? "-" + RupeeSign : RupeeSign) + digits;
        }

        /// <summary>"45.2 L" / "1.2 Cr" for tight spaces like the truck dealer tiles.</summary>
        public static string RupeesShort(long amount)
        {
            long magnitude = System.Math.Abs(amount);
            string sign = amount < 0 ? "-" + RupeeSign : RupeeSign;

            // InvariantCulture explicitly: on a device set to a locale that uses a comma as the
            // decimal separator this would otherwise render "45,2K", which reads as a grouping
            // error rather than a decimal.
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            if (magnitude >= 10000000) return sign + (magnitude / 10000000.0).ToString("0.##", culture) + " Cr";
            if (magnitude >= 100000)   return sign + (magnitude / 100000.0).ToString("0.#", culture) + " L";
            if (magnitude >= 1000)     return sign + (magnitude / 1000.0).ToString("0.#", culture) + "K";
            return sign + magnitude.ToString(culture);
        }

        /// <summary>
        /// Groups a digit string the Indian way: last three digits, then pairs.
        /// Expects digits only — no sign, no separators.
        /// </summary>
        public static string Group(string digits)
        {
            if (string.IsNullOrEmpty(digits) || digits.Length <= 3) return digits;

            string last3 = digits.Substring(digits.Length - 3);
            string rest  = digits.Substring(0, digits.Length - 3);

            var grouped = new System.Text.StringBuilder(digits.Length + 4);
            for (int i = rest.Length; i > 0; i -= 2)
            {
                int start = System.Math.Max(0, i - 2);
                if (grouped.Length > 0) grouped.Insert(0, ",");
                grouped.Insert(0, rest.Substring(start, i - start));
            }

            return grouped + "," + last3;
        }
    }
}
