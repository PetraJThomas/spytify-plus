using System.Collections.Generic;

namespace EspionSpotify.Native
{
    /// <summary>
    /// Natural (numeric-aware) filename comparison: "9" sorts before "10", and "100" follows "99",
    /// even when the leading track number's zero-padding is inconsistent (custom templates, legacy
    /// 2-digit files). Case-insensitive otherwise, with an ordinal tie-break so distinct strings are
    /// never treated as equal (safe as a SortedDictionary comparer). Used to order the .m3u.
    /// </summary>
    public sealed class NaturalFileNameComparer : IComparer<string>
    {
        public static readonly NaturalFileNameComparer Instance = new NaturalFileNameComparer();

        public int Compare(string a, string b)
        {
            a = a ?? "";
            b = b ?? "";
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    var sa = ia;
                    var sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;

                    // Compare the two numeric runs by value: fewer significant digits is smaller,
                    // else lexicographic on the zero-trimmed digits.
                    var na = a.Substring(sa, ia - sa).TrimStart('0');
                    var nb = b.Substring(sb, ib - sb).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    var cmp = string.CompareOrdinal(na, nb);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    var cmp = char.ToLowerInvariant(a[ia]).CompareTo(char.ToLowerInvariant(b[ib]));
                    if (cmp != 0) return cmp;
                    ia++;
                    ib++;
                }
            }

            var lenCmp = (a.Length - ia) - (b.Length - ib);
            return lenCmp != 0 ? lenCmp : string.CompareOrdinal(a, b);
        }
    }
}
