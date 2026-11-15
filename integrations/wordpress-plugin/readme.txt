=== Dewiride Analytics ===
Tags: analytics, statistics, visitors, privacy, traffic
Requires at least: 6.4
Tested up to: 7.1
Requires PHP: 7.4
Stable tag: 1.1.0
License: MIT
License URI: https://opensource.org/license/mit

Counts the people who read your site with Dewiride Analytics, and reports the search engines, AI crawlers and other robots that visit it.

== Description ==

Dewiride Analytics is web analytics that tells you which of your visitors are people. This plugin connects your WordPress site to it: paste two values from Dewiride Analytics, and the people who read your pages are counted. Add a server key as well, and your server reports the robots that read your site without ever running the tag.

You need an account on [Dewiride Analytics](https://analytics.dewiride.com/), the hosted service, which has paid plans after a free trial, or an installation of Dewiride Analytics you run yourself, which is free software. Either one shows the two values under its WordPress steps.

* **Counts your readers.** The plugin adds the Dewiride Analytics tag to every page, loaded so that it never holds your pages up.
* **Leaves you out.** People who are signed in aren't counted, so your own visits and your team's don't inflate your numbers. You can choose roles to count, such as subscribers or customers.
* **Leaves out previews and copies.** Previews, the Customizer and your posts embedded on other sites are never counted, and neither are AMP pages, where the tag can't run.
* **Counts readers without JavaScript.** A small image counts the page for a reader whose browser doesn't run scripts.
* **Stays clear of optimisation plugins.** WP Rocket, LiteSpeed Cache, Autoptimize, SiteGround Speed Optimizer, Perfmatters, Breeze, Jetpack Boost and Cloudflare Rocket Loader are asked to leave the tag alone, so it isn't delayed until a visitor moves the mouse.
* **Reports robots too.** Search engines, AI crawlers, feed readers and scanners don't run the tag, so only your server sees them. With a server key saved, the plugin reports each one from your server: what it asked for, how your site answered, and where it came from. Pages your readers open are matched to what their browser reports, so a reader is counted once, except in older browsers, which don't say when they're opening a page.
* **Reports after the page has gone.** On most hosts the plugin sends each report once the page has reached the visitor. If Dewiride Analytics can't be reached, the plugin stops trying for a while, from 30 seconds up to 15 minutes, rather than trying on every page.
* **Recognises Cloudflare.** Visits that reach your site through Cloudflare are reported with the visitor's own address. Behind another proxy or load balancer, you name it in the settings.
* **Checks that it works.** Once a day the plugin looks at your home page as a visitor sees it, and tells you in plain words if a page cache, an optimisation plugin or a consent tool is changing the tag, and what to do about it. **Check connection** says whether your server key works, belongs to this website, and whether the Address is the right one.
* **Warns you about settings that spoil numbers.** Plain permalinks make every page look like your home page, open registration lets anyone sign in and stop being counted, and an address starting with http:// on a site served over https is blocked by browsers.

== Installation ==

1. In Dewiride Analytics, open your website, choose **Tracking code** and then **WordPress**. Keep the **Address** and the **Website ID** it shows.
2. In WordPress, go to **Plugins → Add Plugin** (called **Add New Plugin** before WordPress 6.8), search for **Dewiride Analytics**, select **Install Now**, then **Activate**.
3. Go to **Settings → Dewiride Analytics**, paste the **Address** and the **Website ID**, and select **Save Changes**.
4. If your theme or another plugin already carries Dewiride Analytics code you pasted earlier, remove it. The plugin adds the tag for you.
5. To report robots too, go back to **Tracking code** → **WordPress** in Dewiride Analytics and select **Create a key for WordPress**. Paste the key into **Server key** in **Settings → Dewiride Analytics**, select **Save Changes**, then **Check connection**.

If you downloaded the plugin's zip file from Dewiride Analytics instead, select **Upload Plugin** on the same screen, choose the file, select **Install Now**, then **Activate Plugin**.

== Frequently Asked Questions ==

= My consent tool holds the tag back. What should I do? =

Some consent tools block every script until a visitor accepts, so only the visitors who accept are counted. The tag sets no cookies and stores nothing on visitors' devices. Whether it may load before a visitor answers is your decision; most consent tools let you allow a script by its address, which is your Address followed by /dw.js.

= Does it work with optimisation plugins? =

Yes. If you use a tool other than those named above that delays or combines JavaScript, exclude dw.js in its settings. The check on **Settings → Dewiride Analytics** tells you if something still changes the tag.

= Does it work with a page cache? =

Yes. The tag is the same for every reader, so cached pages count readers too. After you save your settings for the first time, clear your page cache so that every cached page carries the tag. A visit the cache answers on its own never reaches WordPress, so a robot that receives a cached page isn't reported by your server.

= Will reporting robots slow my site down? =

Not where your host runs PHP with PHP-FPM or LiteSpeed, which most hosts do: the plugin sends each report after the page has reached the visitor. Each report still keeps one of your server's PHP workers busy while it is sent, usually for about a tenth of a second and never more than two seconds. If Dewiride Analytics stops answering, the reports already on their way take those two seconds, and on a server with few workers a page can wait for one to come free; after that the plugin stops trying for a while. Where PHP runs inside Apache, the page can't be sent before its report, so reporting stays off unless you also tick **Report anyway**: every page a robot reads would then arrive a little later. The settings page says which kind of server yours is.

= Which visits does my server report? =

Pages, and, unless you untick them, feeds, robots.txt and sitemaps. Sign-in attempts and XML-RPC requests only if you tick them: attacks on those can arrive by the thousand, and each counts towards your page views. Never the dashboard, the REST API, people who are signed in (unless you count their role), anybody signing in successfully, or your site's own requests, such as WordPress checking itself or a cache plugin warming its pages.

A request your site turns away with an error, such as a scanner looking for a file that isn't there, is reported even when it's dressed up as something that isn't reported, such as a page loaded ahead of time, a preview, an embed, or a REST API request sent to an ordinary page, so a scanner can't hide that way. Only who is signed in can still leave it out.

= My site is behind Cloudflare or a load balancer. =

Cloudflare is recognised without any setting. For another proxy or load balancer, choose **Through another proxy** under **How visitors reach your site**, enter the header it puts the visitor's address in and its own addresses, and save. The settings page shows the address WordPress sees for you, so you can tell straight away whether it's right.

= Why does my server stop reporting when the tag check finds a problem? =

While a consent tool, an optimisation plugin or a missing tag keeps the tag from your readers, their browsers report nothing. Your server would then report them as programs that don't run scripts, so it pauses until the tag check finds the tag in place again.

= Where is the server key kept? =

In your site's database, sealed with your site's secret keys, and never shown again after you save it. On a single site you can keep it in wp-config.php instead: `define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', 'your key' );`

= Why does it warn me about plain permalinks? =

With plain permalinks, every page's address is your home page followed by something like ?p=123. Dewiride Analytics doesn't keep what follows the ? in an address, because it can hold searches and email addresses, so every visit would be recorded as your home page. Choose any other option in **Settings → Permalinks**.

= Who can change the Address? =

Only someone WordPress allows to add code to the site: its administrators on a single site, and network administrators on a network. The Address decides which script your pages load, so it gets the same protection as adding code yourself. Anyone who manages the site's settings can still paste the Website ID and choose who is counted.

= Why must the Address start with https://? =

Your visitors' browsers send what they report to it, so it is reached only over an encrypted connection. An address starting with http:// is accepted only when it is a private one — http://localhost, an address like http://192.168.1.20, or a name ending in .local, .internal, .home.arpa or .test — for a site that only you or your own network open, such as while you build it.

= Why aren't my own visits counted? =

People who are signed in are left out, so you and your team don't inflate your numbers. To count signed-in people with a particular role, such as subscribers or customers, tick it under **Who is counted** in **Settings → Dewiride Analytics**.

== Screenshots ==

1. Settings → Dewiride Analytics: the Address and the Website ID from Dewiride Analytics, who is counted, and a check that the tag reaches your visitors.

== External services ==

This plugin connects your site to Dewiride Analytics, a web analytics service, so that the visits your pages receive are counted and each is judged to come from a person or not. The service is either the hosted [Dewiride Analytics](https://analytics.dewiride.com/), or an installation of Dewiride Analytics run by whoever runs your site. Nothing is sent anywhere until you save an Address and a Website ID in **Settings → Dewiride Analytics**.

Once you have, every page a counted visitor reads loads the script dw.js from the Address you saved, in the visitor's browser. The script reports to /collect at the same Address, sending your Website ID, the time, and:

* the address of the page, of which the service keeps only the path;
* the page the visitor came from, without anything after its path;
* the size of the browser window, the browser's language and its time zone's offset from UTC;
* whether the browser says it is being controlled by automation;
* how long the page was in front of the reader, how far down it they got, and whether there was any pointer or keyboard activity;
* when you switch on **Record what people click** in your website's settings in Dewiride Analytics, which links, buttons and fields are used: the kind of control, the words on it and where it leads. Never what anybody types.

The script sets no cookies and stores nothing on the visitor's device. Like every request a browser makes, each report also reaches the service with the visitor's IP address and browser identification.

For a visitor whose browser doesn't run scripts, a small image loaded from /collect/pixel.gif at the same Address records a page view, naming your Website ID and the page's address without anything after its path.

Once you save a server key, your server also reports to /collect/server at the same Address, with the key, each request it answers that the plugin reports — pages, and the feeds, robots.txt, sitemaps, sign-in attempts and XML-RPC requests you choose — whether a person or a program made it. Each report holds:

* the address of the page, without anything after its path;
* the page the visitor came from, without anything after its path;
* the visitor's IP address, their browser's identification, the language it asks for first, and the brief description of the device a Chromium browser sends with every request;
* the status and content type your site answered with, and when the request arrived;
* for a browser opening a page, a random name, also put in that page's Server-Timing header, which lets the browser's report and the server's be recognised as one visit.

Selecting **Check connection** sends an empty report, which stores nothing, and loads dw.js from the same Address.

Once your server is reporting robots, the plugin fetches the list of Cloudflare's own addresses from [Cloudflare's API](https://api.cloudflare.com/client/v4/ips) the first time a request arrives carrying Cloudflare's visitor header, and then once a week for as long as the plugin is active, to know that the visitor's address Cloudflare passes on is genuine. That request sends nothing about your site or its visitors. It is provided under Cloudflare's [website terms](https://www.cloudflare.com/website-terms/) and [privacy policy](https://www.cloudflare.com/privacypolicy/).

The plugin also loads your own home page to see whether the tag reaches visitors intact: once a day, shortly after you save a new Address or Website ID, and whenever you select **Check the tag** or **Check connection**. That request goes to your own site, not to Dewiride Analytics, and nothing from the page is kept.

The hosted service is provided under its [terms of service](https://analytics.dewiride.com/terms) and [privacy policy](https://analytics.dewiride.com/privacy). An installation you run yourself receives everything at the address you run it on, and nothing reaches the hosted service.

== Changelog ==

= 1.1.0 =
* Reports the search engines, AI crawlers, feed readers and other robots that never run the tag, from your server, once you save a server key.
* Check connection says whether your server key works, belongs to this website, and whether the Address is right.
* Recognises visits through Cloudflare, and lets you name another proxy.
* Suggests text for your privacy policy, under Settings → Privacy.

= 1.0.1 =
* The settings page says what you need before you start: a Dewiride Analytics account, or an installation of your own.
* An address starting with http:// is accepted only when it is a private one, for a site that only you or your own network open.

= 1.0.0 =
* Counts the people who read your site, leaving out people who are signed in unless you choose their role.
* Counts readers whose browser doesn't run scripts.
* Keeps optimisation plugins from delaying the tag, and checks every day that visitors receive it intact.
