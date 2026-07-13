using System.Linq;
using EspionSpotify.Native;
using Xunit;

namespace EspionSpotify.Tests
{
    public class NaturalFileNameComparerTests
    {
        [Fact]
        public void SortsNumbersByValueNotOrdinal()
        {
            var input = new[] { "100 Z", "10 B", "9 A", "99 Y", "11 C", "1 Aa" };
            var sorted = input.OrderBy(x => x, NaturalFileNameComparer.Instance).ToArray();
            Assert.Equal(new[] { "1 Aa", "9 A", "10 B", "11 C", "99 Y", "100 Z" }, sorted);
        }

        [Fact]
        public void HandlesInconsistentZeroPadding()
        {
            var input = new[] { "010 B", "100 C", "009 A" };
            var sorted = input.OrderBy(x => x, NaturalFileNameComparer.Instance).ToArray();
            Assert.Equal(new[] { "009 A", "010 B", "100 C" }, sorted);
        }

        [Fact]
        public void DistinctStringsNeverCompareEqual()
        {
            // "01 A" and "1 A" are numerically equal but must be distinguishable so a SortedDictionary
            // keyed by filename never drops one of two real files.
            Assert.NotEqual(0, NaturalFileNameComparer.Instance.Compare("01 A", "1 A"));
        }
    }
}
