using System.Runtime.InteropServices;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// The elements of a page still open at the point it has been read to, and what is known of being inside them.
/// </summary>
/// <remarks>
/// <para>
/// An end tag closes the nearest open element of its name and everything opened since. One with nothing of
/// its name open is ignored, found out from a count of each name, so a page of them costs nothing. What an end
/// tag closes was opened once and is closed once, so however a page nests, this costs what it takes to read it.
/// </para>
/// <para>
/// There is a limit on how many are held, because what a page says about itself does not depend on how deep
/// down the thousands it says it. Past that, nothing more is held, and whatever is deepest stays open.
/// </para>
/// </remarks>
internal sealed class OpenElements
{
    private const int MostHeld = 4096;

    private readonly List<(string Name, ElementMarks Marks)> _elements = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>How many are open.</summary>
    public int Depth => _elements.Count;

    /// <summary>What being inside the deepest of them means for whatever comes next.</summary>
    public ElementMarks Marks => _elements.Count == 0 ? ElementMarks.None : _elements[^1].Marks;

    /// <summary>
    /// Opens an element.
    /// </summary>
    /// <param name="name">Its name.</param>
    /// <param name="marks">What being inside it means.</param>
    /// <returns>Whether it is held.</returns>
    public bool Open(string name, ElementMarks marks)
    {
        if (_elements.Count == MostHeld)
        {
            return false;
        }

        _elements.Add((name, marks));
        CollectionsMarshal.GetValueRefOrAddDefault(_counts, name, out _)++;

        return true;
    }

    /// <summary>
    /// Closes the nearest open element of a name, and everything opened since.
    /// </summary>
    /// <param name="name">The name an end tag gave.</param>
    public void Close(string name)
    {
        if (!_counts.ContainsKey(name))
        {
            return;
        }

        string closed;

        do
        {
            closed = _elements[^1].Name;
            _elements.RemoveAt(_elements.Count - 1);
            Forget(closed);
        }
        while (closed != name);
    }

    private void Forget(string name)
    {
        var remaining = _counts[name] - 1;

        if (remaining == 0)
        {
            _counts.Remove(name);
        }
        else
        {
            _counts[name] = remaining;
        }
    }
}

/// <summary>
/// What an element says it is, in the few terms a page's description of itself is read in.
/// </summary>
[Flags]
internal enum ElementMarks
{
    /// <summary>Nothing that matters here.</summary>
    None = 0,

    /// <summary>Holds one entry, in the newer microformat.</summary>
    NewEntry = 1 << 0,

    /// <summary>Holds one entry, in the older microformat.</summary>
    OldEntry = 1 << 1,

    /// <summary>Holds one entry, in either microformat or in microdata.</summary>
    Entry = 1 << 2,

    /// <summary>Is WordPress's block for a post's publish date.</summary>
    PostDate = 1 << 3,

    /// <summary>Is WordPress's block for a list of posts.</summary>
    PostList = 1 << 4,

    /// <summary>Is vector graphics or mathematics, where markup follows rules of its own.</summary>
    Foreign = 1 << 5,

    /// <summary>Holds markup for a script to use, which is no part of the page until one does.</summary>
    Inert = 1 << 6,

    /// <summary>All of the above, which is to say what goes for everything inside an element as well.</summary>
    PassedDown = NewEntry | OldEntry | Entry | PostDate | PostList | Foreign | Inert,

    /// <summary>Is a publish date, in the newer microformat.</summary>
    NewPublished = 1 << 7,

    /// <summary>Is a publish date, in the older microformat.</summary>
    OldPublished = 1 << 8,

    /// <summary>Is WordPress's block printing when a post was last changed instead.</summary>
    ModifiedDate = 1 << 9,

    /// <summary>On the page's body: WordPress says this page is one post.</summary>
    OnePost = 1 << 10,

    /// <summary>On the page's body: WordPress says this page lists posts.</summary>
    ListOfPosts = 1 << 11,
}
