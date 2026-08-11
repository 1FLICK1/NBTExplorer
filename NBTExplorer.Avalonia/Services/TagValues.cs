using System.Globalization;
using NBTExplorer.Model;
using Substrate.Nbt;

namespace NBTExplorer.Avalonia.Services;

/// <summary>
/// Text ↔ tag conversion for the value editors.
///
/// Everything here is InvariantCulture on purpose. NBT is a binary interchange format: a float
/// written as "1,5" on a Russian-locale machine and "1.5" elsewhere would be the same file
/// behaving differently depending on who opened it. (The existing WildcardRule in NBTModel uses
/// culture-sensitive double.Parse and has exactly that latent bug — not fixed here, but do not
/// copy the pattern.)
/// </summary>
public static class TagValues
{
    public static string Format(TagNode tag) => tag.GetTagType() switch {
        TagType.TAG_BYTE => tag.ToTagByte().Data.ToString(CultureInfo.InvariantCulture),
        TagType.TAG_SHORT => tag.ToTagShort().Data.ToString(CultureInfo.InvariantCulture),
        TagType.TAG_INT => tag.ToTagInt().Data.ToString(CultureInfo.InvariantCulture),
        TagType.TAG_LONG => tag.ToTagLong().Data.ToString(CultureInfo.InvariantCulture),
        TagType.TAG_FLOAT => tag.ToTagFloat().Data.ToString("R", CultureInfo.InvariantCulture),
        TagType.TAG_DOUBLE => tag.ToTagDouble().Data.ToString("R", CultureInfo.InvariantCulture),
        TagType.TAG_STRING => tag.ToTagString().Data,
        _ => tag.ToString() ?? "",
    };

    /// <summary>Null when the text is a valid value for this tag type, otherwise the reason.</summary>
    public static string? Validate(TagType type, string text) => type switch {
        TagType.TAG_BYTE => Range<byte>(text, byte.TryParse, "0", "255"),
        TagType.TAG_SHORT => Range<short>(text, short.TryParse, "-32768", "32767"),
        TagType.TAG_INT => Range<int>(text, int.TryParse, "-2147483648", "2147483647"),
        TagType.TAG_LONG => Range<long>(text, long.TryParse,
                                        "-9223372036854775808", "9223372036854775807"),
        TagType.TAG_FLOAT => Real(text, static (string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)),
        TagType.TAG_DOUBLE => Real(text, static (string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)),
        TagType.TAG_STRING => null,
        _ => null,
    };

    /// <summary>
    /// Writes the parsed text into the tag IN PLACE. That is deliberate and load-bearing:
    /// FormRegistry hands the model's own TagNode to the dialog and expects it mutated, because
    /// the DataNode holds no other reference to the value it displays.
    /// </summary>
    public static bool TryApply(TagNode tag, string text)
    {
        if (Validate(tag.GetTagType(), text) is not null)
            return false;

        switch (tag) {
            case TagNodeByte t: t.Data = byte.Parse(text, CultureInfo.InvariantCulture); break;
            case TagNodeShort t: t.Data = short.Parse(text, CultureInfo.InvariantCulture); break;
            case TagNodeInt t: t.Data = int.Parse(text, CultureInfo.InvariantCulture); break;
            case TagNodeLong t: t.Data = long.Parse(text, CultureInfo.InvariantCulture); break;
            case TagNodeFloat t:
                t.Data = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); break;
            case TagNodeDouble t:
                t.Data = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); break;
            case TagNodeString t: t.Data = text; break;
            default: return false;
        }

        return true;
    }

    /// <summary>Default value for a newly created tag of the given type.</summary>
    public static TagNode Default(TagType type) => TagDataNode.DefaultTag(type);

    // ---- byte-array text form (interim hex editor) -------------------------------------------

    /// <summary>
    /// Renders a raw byte payload as grouped hex. The real hex editor (a port of the 4300-line
    /// vendored HexBox) is a separate project; this covers the common case of small
    /// TAG_Byte_Array / TAG_Int_Array values honestly rather than blocking the operation.
    /// </summary>
    public static string FormatHex(byte[] data, int bytesPerElement)
    {
        int group = Math.Max(1, bytesPerElement);
        var sb = new System.Text.StringBuilder(data.Length * 3);

        for (int i = 0; i < data.Length; i++) {
            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            if (i == data.Length - 1)
                break;
            // Space between elements, newline every 16 bytes, so long payloads stay scannable.
            if ((i + 1) % 16 == 0)
                sb.Append('\n');
            else if ((i + 1) % group == 0)
                sb.Append(' ');
        }

        return sb.ToString();
    }

    public static bool TryParseHex(string text, int expectedLength, out byte[] data, out string? error)
    {
        data = [];
        var bytes = new List<byte>(expectedLength);

        foreach (string token in text.Split([' ', '\t', '\r', '\n', ','],
                                            StringSplitOptions.RemoveEmptyEntries)) {
            if (token.Length % 2 != 0) {
                error = $"'{token}' is not a whole number of bytes.";
                return false;
            }

            for (int i = 0; i < token.Length; i += 2) {
                if (!byte.TryParse(token.AsSpan(i, 2), NumberStyles.HexNumber,
                                   CultureInfo.InvariantCulture, out byte b)) {
                    error = $"'{token.Substring(i, 2)}' is not valid hex.";
                    return false;
                }
                bytes.Add(b);
            }
        }

        // The array length is fixed by the tag; growing or shrinking it here would silently
        // change the structure the game expects.
        if (bytes.Count != expectedLength) {
            error = $"Expected {expectedLength} bytes, got {bytes.Count}.";
            return false;
        }

        data = [.. bytes];
        error = null;
        return true;
    }

    // ---- helpers -----------------------------------------------------------------------------

    private delegate bool TryParse<T>(string s, out T value);

    private static string? Range<T>(string text, TryParse<T> parse, string min, string max)
        => parse(text.Trim(), out _) ? null : $"Enter a whole number between {min} and {max}.";

    private static string? Real<T>(string text, TryParse<T> parse)
        => parse(text.Trim(), out _) ? null : "Enter a number, using '.' as the decimal separator.";
}
