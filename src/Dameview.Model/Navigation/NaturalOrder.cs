namespace Dameview.Navigation;

/// <summary>
/// Orders text the way people read numbers in it, so "2" comes before "10".
/// </summary>
internal static class NaturalOrder
{
    internal static int Compare(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        int leftIndex = 0;
        int rightIndex = 0;
        // Equal numbers written with different leading zeros only decide when nothing else does.
        int zeroTiebreak = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            char leftChar = left[leftIndex];
            char rightChar = right[rightIndex];

            if (char.IsAsciiDigit(leftChar) && char.IsAsciiDigit(rightChar))
            {
                int leftZeros = SkipZeros(left, ref leftIndex);
                int rightZeros = SkipZeros(right, ref rightIndex);
                ReadOnlySpan<char> leftNumber = TakeDigits(left, ref leftIndex);
                ReadOnlySpan<char> rightNumber = TakeDigits(right, ref rightIndex);

                // Without leading zeros, the longer number is the larger one.
                if (leftNumber.Length != rightNumber.Length)
                {
                    return leftNumber.Length.CompareTo(rightNumber.Length);
                }

                int digits = leftNumber.SequenceCompareTo(rightNumber);
                if (digits != 0)
                {
                    return digits;
                }

                if (zeroTiebreak == 0)
                {
                    zeroTiebreak = leftZeros.CompareTo(rightZeros);
                }

                continue;
            }

            if (leftChar != rightChar)
            {
                int letters = ToUpper(leftChar).CompareTo(ToUpper(rightChar));
                if (letters != 0)
                {
                    return letters;
                }
            }

            leftIndex++;
            rightIndex++;
        }

        int remaining = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        if (remaining != 0)
        {
            return remaining;
        }

        return zeroTiebreak != 0 ? zeroTiebreak : left.SequenceCompareTo(right);
    }

    private static int SkipZeros(ReadOnlySpan<char> text, ref int index)
    {
        int start = index;
        while (index < text.Length && text[index] == '0')
        {
            index++;
        }

        return index - start;
    }

    private static ReadOnlySpan<char> TakeDigits(ReadOnlySpan<char> text, ref int index)
    {
        int start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        return text[start..index];
    }

    private static char ToUpper(char value) =>
        char.IsAscii(value)
            ? char.IsAsciiLetterLower(value) ? (char)(value - ('a' - 'A')) : value
            : char.ToUpperInvariant(value);
}
