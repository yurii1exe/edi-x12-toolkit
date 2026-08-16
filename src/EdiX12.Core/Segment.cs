namespace EdiX12.Core;

/// <summary>
/// One X12 segment: an identifier plus its data elements, in order.
/// </summary>
/// <remarks>
/// <para>
/// Element values are kept exactly as they appeared on the wire, including the trailing
/// spaces that fixed-width ISA elements carry. Trimming is a decision for the typed layer
/// (see <see cref="Interchange"/>), not the tokenizer — a parser that silently rewrites
/// its input cannot be used to diagnose a partner's file.
/// </para>
/// <para>
/// Elements are addressed by their 1-based number, matching how the specification and
/// every implementation guide refer to them: <c>segment[4]</c> is element 04.
/// </para>
/// </remarks>
public sealed class Segment
{
    private readonly string[] _elements;
    private readonly X12Delimiters _delimiters;

    /// <summary>Creates a segment.</summary>
    /// <param name="id">The segment identifier, e.g. <c>ISA</c>, <c>B10</c>, <c>AT7</c>.</param>
    /// <param name="elements">Element values in order, element 01 first.</param>
    /// <param name="delimiters">The delimiters this segment was read with.</param>
    /// <param name="position">1-based ordinal of this segment within the interchange.</param>
    public Segment(string id, string[] elements, X12Delimiters delimiters, int position)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _elements = elements ?? throw new ArgumentNullException(nameof(elements));
        _delimiters = delimiters ?? throw new ArgumentNullException(nameof(delimiters));
        Position = position;
    }

    /// <summary>The segment identifier, e.g. <c>B10</c>.</summary>
    public string Id { get; }

    /// <summary>
    /// 1-based ordinal of this segment within the interchange, counting the ISA as 1.
    /// Used to make error messages actionable: partners quote segment positions back at you.
    /// </summary>
    public int Position { get; }

    /// <summary>Number of elements present. Trailing empty elements are usually omitted by senders.</summary>
    public int ElementCount => _elements.Length;

    /// <summary>All element values, element 01 first.</summary>
    public IReadOnlyList<string> Elements => _elements;

    /// <summary>
    /// The value of element <paramref name="elementNumber"/> (1-based), or an empty string
    /// if the sender omitted it. Absent and empty are the same thing in X12, so this does
    /// not throw for a missing trailing element.
    /// </summary>
    /// <param name="elementNumber">1-based element number, e.g. 4 for element 04.</param>
    public string this[int elementNumber]
    {
        get
        {
            if (elementNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elementNumber), elementNumber, "X12 elements are numbered from 1.");
            }

            return elementNumber <= _elements.Length ? _elements[elementNumber - 1] : string.Empty;
        }
    }

    /// <summary>
    /// Splits a composite element into its components using the interchange's component
    /// separator. Returns an empty array when the element is absent or empty.
    /// </summary>
    /// <param name="elementNumber">1-based element number.</param>
    public string[] Components(int elementNumber)
    {
        string value = this[elementNumber];
        return value.Length == 0
            ? Array.Empty<string>()
            : value.Split(_delimiters.Component);
    }

    /// <summary>
    /// Splits a repeating element on the interchange's repetition separator (ISA11, 5010+).
    /// When the interchange declares no repetition separator the whole value is returned
    /// as a single repetition.
    /// </summary>
    /// <param name="elementNumber">1-based element number.</param>
    public string[] Repetitions(int elementNumber)
    {
        string value = this[elementNumber];
        if (value.Length == 0)
        {
            return Array.Empty<string>();
        }

        return _delimiters.Repetition.HasValue
            ? value.Split(_delimiters.Repetition.Value)
            : new[] { value };
    }

    /// <summary>
    /// The specification's name for an element, e.g. <c>B1004</c> for element 04 of a B10.
    /// This is the form to use in error messages, because it is the form the partner's
    /// implementation guide uses.
    /// </summary>
    /// <param name="elementNumber">1-based element number.</param>
    public string ElementReference(int elementNumber) => $"{Id}{elementNumber:00}";

    /// <summary>Re-renders the segment using its original delimiters, without the terminator.</summary>
    public override string ToString() =>
        _elements.Length == 0 ? Id : Id + _delimiters.Element + string.Join(_delimiters.Element.ToString(), _elements);
}
