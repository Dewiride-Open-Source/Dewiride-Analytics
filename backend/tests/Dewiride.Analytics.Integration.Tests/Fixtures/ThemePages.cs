namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// The pages publishing software makes, down to the classes that matter here.
/// </summary>
/// <remarks>
/// Written from each publisher's own source: wordpress-develop's default themes and the post date block's
/// renderer, Yoast's presentations and Hugo's embedded Open Graph template. They were not captured from
/// running websites, so what they prove is that the reader reads what that source writes.
/// </remarks>
internal static class ThemePages
{
    /// <summary>
    /// A post on Ipsum, the default theme of the WordPress release after Twenty Twenty-Five: its own date
    /// under its title, and others in a list under the comments.
    /// </summary>
    /// <param name="own">The post's date.</param>
    /// <param name="others">How many other posts it lists.</param>
    /// <returns>The page.</returns>
    public static string Ipsum(string own, int others) =>
        $$"""
        <!DOCTYPE html><html lang="en-US"><head><title>Mine &#8211; My WordPress</title></head>
        <body class="wp-singular post-template-default single single-post postid-1 wp-theme-ipsum">
        <main class="wp-block-group is-layout-constrained">
          <div class="wp-block-group is-layout-constrained"><h1 class="wp-block-post-title">Mine</h1>
            <div class="wp-block-group is-nowrap is-layout-flex">
              <div class="wp-block-post-date"><time datetime="{{own}}">January 5, 2026</time></div>
              <div class="taxonomy-category wp-block-post-terms"><a href="/c/">Uncategorized</a></div>
            </div>
          </div>
          <div class="entry-content wp-block-post-content is-layout-constrained"><p>Words.</p></div>
          <div class="wp-block-group"><h2 class="wp-block-heading">Keep Reading</h2>{{ListOfPosts(others)}}</div>
        </main></body></html>
        """;

    /// <summary>
    /// A post on Twenty Twenty-Five: no date of its own anywhere, and "More posts", which can list the post
    /// itself.
    /// </summary>
    /// <param name="listed">How many posts it lists.</param>
    /// <returns>The page.</returns>
    public static string TwentyTwentyFive(int listed) =>
        $$"""
        <!DOCTYPE html><html lang="en-US"><head><title>Mine &#8211; My WordPress</title></head>
        <body class="wp-singular post-template-default single single-post wp-theme-twentytwentyfive">
        <main class="wp-block-group is-layout-constrained">
          <div class="wp-block-group alignfull is-layout-constrained"><h1 class="wp-block-post-title">Mine</h1>
            <div class="wp-block-group is-layout-flex"><p>Written by </p>
              <div class="wp-block-post-author-name"><a href="/a/">admin</a></div>
            </div>
            <div class="entry-content wp-block-post-content is-layout-constrained"><p>Words.</p></div>
          </div>
          <div class="wp-block-group alignwide"><h2 class="wp-block-heading">More posts</h2>
            {{ListOfPosts(listed)}}
          </div>
        </main></body></html>
        """;

    /// <summary>A post on Twenty Twenty-Four: one date, and no list.</summary>
    /// <param name="own">The post's date.</param>
    /// <returns>The page.</returns>
    public static string TwentyTwentyFour(string own) =>
        $"""
        <!DOCTYPE html><html lang="en-US"><head><title>Mine &#8211; My WordPress</title></head>
        <body class="wp-singular post-template-default single single-post wp-theme-twentytwentyfour">
        <main class="wp-block-group"><h1 class="wp-block-post-title">Mine</h1>
          <div class="wp-block-post-date"><time datetime="{own}">January 5, 2026</time></div>
          <div class="entry-content wp-block-post-content"><p>Words.</p></div></main>
        </body></html>
        """;

    /// <summary>The list a block theme prints posts in, each with its date.</summary>
    /// <param name="count">How many.</param>
    /// <returns>The markup, which is nothing where there are none.</returns>
    public static string ListOfPosts(int count)
    {
        var items = Enumerable.Range(1, count).Select(number =>
            $"""
            <li class="wp-block-post post-{number} post type-post status-publish hentry category-uncategorized">
              <h3 class="wp-block-post-title"><a href="/p{number}/">Post {number}</a></h3>
              <div class="wp-block-post-date">
                <time datetime="2026-01-{number + 10:00}T09:00:00+00:00">A date</time>
              </div>
            </li>
            """);

        return count == 0
            ? string.Empty
            : $"""<div class="wp-block-query"><ul class="wp-block-post-template">{string.Concat(items)}</ul></div>""";
    }

    /// <summary>
    /// The entries a classic theme prints on a page: each marked as an entry, with its date marked as
    /// published.
    /// </summary>
    /// <param name="dates">The date of each.</param>
    /// <returns>The markup.</returns>
    public static string ClassicEntries(params string[] dates) =>
        string.Concat(dates.Select((date, number) =>
            $"""
            <article id="post-{number}" class="post-{number} post type-post status-publish hentry category-news">
              <header class="entry-header"><h2 class="entry-title"><a href="/p{number}/">Post {number}</a></h2>
                <div class="entry-meta"><span class="posted-on"><a href="/p{number}/">
                  <time class="entry-date published updated" datetime="{date}">A date</time>
                </a></span></div>
              </header>
            </article>
            """));

    /// <summary>
    /// What Yoast makes of the page WordPress lists posts on: an article to Open Graph with no publish time,
    /// and a collection to its structured data.
    /// </summary>
    /// <param name="body">What the theme prints in the body, with the body's class.</param>
    /// <returns>The page.</returns>
    public static string YoastPostsPage(string body) =>
        $$"""
        <!DOCTYPE html><html><head><title>Blog - My site</title>
        <meta property="og:type" content="article" />
        <meta property="og:title" content="Blog - My site" />
        <script type="application/ld+json" class="yoast-schema-graph">
        {"@context":"https://schema.org","@graph":[
        {"@type":["WebPage","CollectionPage"],"@id":"https://blog.example.test/blog/","name":"Blog - My site"},
        {"@type":"WebSite","name":"My site"}]}
        </script>
        </head><body class="blog wp-theme-whichever hfeed"><main>{{body}}</main></body></html>
        """;

    /// <summary>
    /// What Yoast makes of a post: an article to Open Graph, with its publish time, and to its structured
    /// data.
    /// </summary>
    /// <param name="title">The post's title.</param>
    /// <param name="date">The post's date.</param>
    /// <returns>The page.</returns>
    public static string YoastPost(string title, string date) =>
        $$"""
        <!DOCTYPE html><html><head><title>{{title}} - My site</title>
        <meta property="og:type" content="article" />
        <meta property="og:title" content="{{title}} - My site" />
        <meta property="article:published_time" content="{{date}}" />
        <script type="application/ld+json" class="yoast-schema-graph">
        {"@context":"https://schema.org","@graph":[
        {"@type":"Article","headline":"{{title}}","datePublished":"{{date}}"},
        {"@type":"WebPage","name":"{{title}}"}]}
        </script>
        </head><body class="post-template-default single single-post"><main>{{ClassicEntries(date)}}</main>
        </body></html>
        """;

    /// <summary>
    /// What Yoast makes of a plain page: an article to Open Graph, with no publish time, and a web page to
    /// its structured data.
    /// </summary>
    /// <param name="title">The page's title.</param>
    /// <param name="more">Anything another plugin adds to the head.</param>
    /// <returns>The page.</returns>
    public static string YoastPage(string title, string more = "") =>
        $$"""
        <!DOCTYPE html><html><head><title>{{title}} - My site</title>
        <meta property="og:type" content="article" />
        <meta property="og:title" content="{{title}} - My site" />
        <script type="application/ld+json" class="yoast-schema-graph">
        {"@context":"https://schema.org","@graph":[{"@type":"WebPage","name":"{{title}}"},
        {"@type":"WebSite","name":"My site"}]}
        </script>
        {{more}}
        </head><body class="page-template-default page"></body></html>
        """;

    /// <summary>
    /// What Hugo's own template makes of a page in no section of posts, such as its About page: an article,
    /// and no date where it was given none.
    /// </summary>
    /// <param name="title">The page's title.</param>
    /// <returns>The page.</returns>
    public static string HugoPage(string title) =>
        $"""
        <!DOCTYPE html><html><head><title>{title}</title>
        <meta property="og:title" content="{title}">
        <meta property="og:type" content="article">
        </head><body></body></html>
        """;
}
