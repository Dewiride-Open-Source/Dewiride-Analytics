=== Dewiride Analytics ===
Tags: analytics, statistics, visitors, privacy, traffic
Requires at least: 6.4
Tested up to: 7.1
Requires PHP: 7.4
Stable tag: 1.0.1
License: MIT
License URI: https://opensource.org/license/mit

Counts the people who read your site with Dewiride Analytics. No cookies, your own visits left out, and a daily check that the tag reaches readers.

== Description ==

Dewiride Analytics is web analytics that tells you which of your visitors are people. This plugin connects your WordPress site to it: paste two values from Dewiride Analytics, and the people who read your pages are counted.

You need an account on [Dewiride Analytics](https://analytics.dewiride.com/), the hosted service, which has paid plans after a free trial, or an installation of Dewiride Analytics you run yourself, which is free software. Either one shows the two values under its WordPress steps.

* **Counts your readers.** The plugin adds the Dewiride Analytics tag to every page, loaded so that it never holds your pages up.
* **Leaves you out.** People who are signed in aren't counted, so your own visits and your team's don't inflate your numbers. You can choose roles to count, such as subscribers or customers.
* **Leaves out previews and copies.** Previews, the Customizer and your posts embedded on other sites are never counted, and neither are AMP pages, where the tag can't run.
* **Counts readers without JavaScript.** A small image counts the page for a reader whose browser doesn't run scripts.
* **Stays clear of optimisation plugins.** WP Rocket, LiteSpeed Cache, Autoptimize, SiteGround Speed Optimizer, Perfmatters, Breeze, Jetpack Boost and Cloudflare Rocket Loader are asked to leave the tag alone, so it isn't delayed until a visitor moves the mouse.
* **Checks that it works.** Once a day the plugin looks at your home page as a visitor sees it, and tells you in plain words if a page cache, an optimisation plugin or a consent tool is changing the tag, and what to do about it.
* **Warns you about settings that spoil numbers.** Plain permalinks make every page look like your home page, open registration lets anyone sign in and stop being counted, and an address starting with http:// on a site served over https is blocked by browsers.

== Installation ==

1. In Dewiride Analytics, open your website, choose **Tracking code** and then **WordPress**. Keep the **Address** and the **Website ID** it shows.
2. In WordPress, go to **Plugins → Add Plugin** (called **Add New Plugin** before WordPress 6.8), search for **Dewiride Analytics**, select **Install Now**, then **Activate**.
3. Go to **Settings → Dewiride Analytics**, paste the **Address** and the **Website ID**, and select **Save Changes**.
4. If your theme or another plugin already carries Dewiride Analytics code you pasted earlier, remove it. The plugin adds the tag for you.

If you downloaded the plugin's zip file from Dewiride Analytics instead, select **Upload Plugin** on the same screen, choose the file, select **Install Now**, then **Activate Plugin**.

== Frequently Asked Questions ==

= My consent tool holds the tag back. What should I do? =

Some consent tools block every script until a visitor accepts, so only the visitors who accept are counted. The tag sets no cookies and stores nothing on visitors' devices. Whether it may load before a visitor answers is your decision; most consent tools let you allow a script by its address, which is your Address followed by /dw.js.

= Does it work with optimisation plugins? =

Yes. If you use a tool other than those named above that delays or combines JavaScript, exclude dw.js in its settings. The check on **Settings → Dewiride Analytics** tells you if something still changes the tag.

= Does it work with a page cache? =

Yes. The tag is the same for every reader, so cached pages count readers too. After you save your settings for the first time, clear your page cache so that every cached page carries the tag.

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

The plugin also loads your own home page to see whether the tag reaches visitors intact: once a day, shortly after you save a new Address or Website ID, and whenever you select **Check the tag**. That request goes to your own site, not to Dewiride Analytics, and nothing from the page is kept.

The hosted service is provided under its [terms of service](https://analytics.dewiride.com/terms) and [privacy policy](https://analytics.dewiride.com/privacy). An installation you run yourself receives everything at the address you run it on, and nothing reaches the hosted service.

== Changelog ==

= 1.0.1 =
* The settings page says what you need before you start: a Dewiride Analytics account, or an installation of your own.
* An address starting with http:// is accepted only when it is a private one, for a site that only you or your own network open.

= 1.0.0 =
* Counts the people who read your site, leaving out people who are signed in unless you choose their role.
* Counts readers whose browser doesn't run scripts.
* Keeps optimisation plugins from delaying the tag, and checks every day that visitors receive it intact.
