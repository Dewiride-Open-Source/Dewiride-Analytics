<?php
/**
 * Tests for what the server reports about the request it answered.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Audience;
use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Dewiride\Analytics\WordPress\Own_Request;
use Dewiride\Analytics\WordPress\Request_Report;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Visitor_Address;

/**
 * What a visitor asked WordPress for is reported; the dashboard, the site's own requests, people
 * signed in and successful sign-ins never are; and what a visitor's headers say is believed only
 * of a request the site answered without an error.
 */
final class Request_Report_Test extends Test_Case {

	/**
	 * A crawler's user agent.
	 */
	private const CRAWLER = 'Mozilla/5.0 (compatible; ExampleBot/1.0; +https://bot.example/)';

	/**
	 * A browser's user agent.
	 */
	private const BROWSER = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36';

	/**
	 * The crawler's address.
	 */
	private const VISITOR = '203.0.113.7';

	/**
	 * What the REST API answers with.
	 */
	private const JSON = 'application/json';

	/**
	 * The actions WordPress has run, by name, in each test.
	 *
	 * @var array<string, int>
	 */
	private $actions = array( 'parse_request' => 1 );

	/**
	 * What the plugin's settings hold in each test, beyond the address and the Website ID.
	 *
	 * @var array<string, mixed>
	 */
	private $settings = array();

	/**
	 * The plugin's other options in each test, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $options_given = array();

	/**
	 * Starts each test on a page a crawler asked for, answered by WordPress.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\when( 'get_option' )->alias(
			function ( string $option ) {
				return Settings::OPTION === $option
					? $this->settings + array(
						'address' => self::ADDRESS,
						'site_id' => self::SITE_ID,
					)
					: $this->options_given[ $option ] ?? false;
			}
		);
		Functions\when( 'did_action' )->alias(
			function ( string $action ): int {
				return $this->actions[ $action ] ?? ( 'sanitize_comment_cookies' === $action ? 1 : 0 );
			}
		);
		Functions\when( 'doing_action' )->justReturn( false );
		Functions\when( 'wp_doing_cron' )->justReturn( false );
		Functions\when( 'wp_doing_ajax' )->justReturn( false );
		Functions\when( 'is_admin' )->justReturn( false );
		Functions\when( 'is_feed' )->justReturn( false );
		Functions\when( 'is_robots' )->justReturn( false );
		Functions\when( 'is_favicon' )->justReturn( false );
		Functions\when( 'get_query_var' )->justReturn( '' );
		Functions\when( 'is_preview' )->justReturn( false );
		Functions\when( 'is_customize_preview' )->justReturn( false );
		Functions\when( 'is_embed' )->justReturn( false );
		Functions\when( 'is_user_logged_in' )->justReturn( false );
		Functions\when( 'rest_get_url_prefix' )->justReturn( 'wp-json' );
		Functions\when( 'wp_next_scheduled' )->justReturn( 1 );
		$this->given_visit();
	}

	/**
	 * A crawler's page is reported with what the server saw of it.
	 */
	public function test_a_crawlers_page_is_reported_with_what_was_seen(): void {
		$observation = $this->observe( 200, 'text/html; charset=UTF-8' );

		self::assertNotNull( $observation );
		self::assertSame( 'pageview', $observation['kind'] );
		self::assertSame( 'https://example.com/posts/hello/', $observation['url'] );
		self::assertSame( 'https://news.example/front', $observation['referrer'] );
		self::assertSame( self::VISITOR, $observation['ipAddress'] );
		self::assertSame( self::CRAWLER, $observation['userAgent'] );
		self::assertSame( 200, $observation['statusCode'] );
		self::assertSame( 'text/html; charset=UTF-8', $observation['contentType'] );
		self::assertSame( 'en-GB', $observation['language'] );
		self::assertIsInt( $observation['observedAt'] );
		self::assertArrayNotHasKey( 'correlationId', $observation );
		self::assertArrayNotHasKey( 'mobile', $observation );
	}

	/**
	 * A named page carries its name, and a browser's hints are forwarded as sent.
	 */
	public function test_a_named_page_carries_its_name_and_hints(): void {
		$this->given_visit(
			array(
				'HTTP_USER_AGENT'         => self::BROWSER,
				'HTTP_SEC_FETCH_DEST'     => 'document',
				'HTTP_SEC_CH_UA_MOBILE'   => '?0',
				'HTTP_SEC_CH_UA_PLATFORM' => '"Windows"',
				'HTTP_SEC_CH_UA'          => '"Chromium";v="142", "Not_A Brand";v="99"',
			)
		);

		$observation = $this->observe( 200, 'text/html', str_repeat( 'a1', 16 ) );

		self::assertNotNull( $observation );
		self::assertSame( str_repeat( 'a1', 16 ), $observation['correlationId'] );
		self::assertSame( '?0', $observation['mobile'] );
		self::assertSame( '"Windows"', $observation['platform'] );
		self::assertSame( '"Chromium";v="142", "Not_A Brand";v="99"', $observation['brands'] );
	}

	/**
	 * Requests that are never reported, whoever made them.
	 *
	 * @return array<string, array{string}>
	 */
	public function background_requests(): array {
		return array(
			'the dashboard'                        => array( 'is_admin' ),
			'a scheduled event'                    => array( 'wp_doing_cron' ),
			'the dashboard\'s background requests' => array( 'wp_doing_ajax' ),
		);
	}

	/**
	 * The dashboard, its background requests and scheduled events are never reported.
	 *
	 * @dataProvider background_requests
	 *
	 * @param string $stand_in The function that says the request is one of them.
	 */
	public function test_background_requests_are_never_reported( string $stand_in ): void {
		Functions\when( $stand_in )->justReturn( true );

		self::assertNull( $this->observe( 200 ) );
	}

	/**
	 * The REST API at its own addresses is not reported, answered or refused.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_the_rest_api_is_not_reported(): void {
		define( 'REST_REQUEST', true ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedConstantFound -- WordPress's own constant, defined as WordPress defines it.
		$this->actions = array();
		$this->given_visit( array( 'REQUEST_URI' => '/wp-json/wp/v2/users/me' ) );

		self::assertNull( $this->observe( 200, self::JSON ) );
		self::assertNull( $this->observe( 401, self::JSON ) );
	}

	/**
	 * On a site in a folder of its host, the REST API's own addresses are found in that folder.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_the_rest_api_of_a_site_in_a_folder_is_not_reported(): void {
		define( 'REST_REQUEST', true ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedConstantFound -- WordPress's own constant, defined as WordPress defines it.
		Functions\when( 'home_url' )->justReturn( 'https://example.com/blog' );
		$this->actions = array();

		foreach ( array( '/blog/wp-json/wp/v2/posts', '/blog/?rest_route=/x', '/blog/index.php?rest_route=/x' ) as $own ) {
			$this->given_visit( array( 'REQUEST_URI' => $own ) );
			self::assertNull( $this->observe( 404, self::JSON ), $own );
		}

		$this->given_visit( array( 'REQUEST_URI' => '/blog/.env?rest_route=/x' ) );
		self::assertNotNull( $this->observe( 404, self::JSON ), 'a probe with a REST query' );
	}

	/**
	 * A request that never went through WordPress's front end is not reported.
	 */
	public function test_a_request_outside_the_front_end_is_not_reported(): void {
		$this->actions = array();

		self::assertNull( $this->observe( 200 ) );
	}

	/**
	 * People signed in are left out unless their role is counted, as the tag leaves them out.
	 */
	public function test_people_signed_in_are_left_out_unless_counted(): void {
		Functions\when( 'is_user_logged_in' )->justReturn( true );
		Functions\when( 'wp_get_current_user' )->justReturn( (object) array( 'roles' => array( 'editor' ) ) );

		self::assertNull( $this->observe( 200 ) );

		$this->settings = array( 'counted_roles' => array( 'editor' ) );

		self::assertNotNull( $this->observe( 200 ) );
	}

	/**
	 * A preview, the favicon and a form sent to a page are not reported.
	 */
	public function test_previews_the_favicon_and_forms_are_not_reported(): void {
		Functions\when( 'is_preview' )->justReturn( true );
		self::assertNull( $this->observe( 200 ) );
		Functions\when( 'is_preview' )->justReturn( false );

		Functions\when( 'is_favicon' )->justReturn( true );
		self::assertNull( $this->observe( 200 ) );
		Functions\when( 'is_favicon' )->justReturn( false );

		$this->given_visit( array( 'REQUEST_METHOD' => 'POST' ) );
		self::assertNull( $this->observe( 200 ) );
	}

	/**
	 * What a visitor's headers can say about a request.
	 *
	 * @return array<string, array{array<string, string>}>
	 */
	public function headers_a_visitor_writes(): array {
		return array(
			'a HEAD request'               => array( array( 'REQUEST_METHOD' => 'HEAD' ) ),
			'a prefetch'                   => array( array( 'HTTP_SEC_PURPOSE' => 'prefetch' ) ),
			'a prerender'                  => array( array( 'HTTP_SEC_PURPOSE' => 'prefetch;prerender' ) ),
			'an older browser\'s prefetch' => array( array( 'HTTP_PURPOSE' => 'prefetch' ) ),
			'a proxy\'s prefetch'          => array( array( 'HTTP_X_PURPOSE' => 'preview' ) ),
			'Firefox\'s old prefetch'      => array( array( 'HTTP_X_MOZ' => 'prefetch' ) ),
			'a request for an image'       => array( array( 'HTTP_SEC_FETCH_DEST' => 'image' ) ),
			'this plugin'                  => array( array( 'HTTP_USER_AGENT' => 'DewirideAnalytics/1.1.0' ) ),
			'WordPress fetching this site' => array( array( 'HTTP_USER_AGENT' => 'WordPress/7.1.4; https://example.com' ) ),
		);
	}

	/**
	 * A visitor's headers leave a request out only when the site answered it without an error, or
	 * a scanner could hide every probe behind one of them.
	 *
	 * @dataProvider headers_a_visitor_writes
	 *
	 * @param array<string, string> $headers What the request carried.
	 */
	public function test_a_visitors_headers_leave_out_only_what_was_answered( array $headers ): void {
		$this->given_visit( $headers + array( 'REQUEST_URI' => '/.env' ) );

		self::assertNull( $this->observe( 200 ) );
		self::assertNull( $this->observe( 399 ), 'the last status below a refusal' );
		self::assertNotNull( $this->observe( 400 ), 'the first refusal' );
		self::assertNotNull( $this->observe( 404 ) );
	}

	/**
	 * A browser's request for a missing image, font, stylesheet or script is a broken page, and
	 * left out; what a probe asks for is not.
	 */
	public function test_a_browsers_missing_files_are_left_out_and_probes_are_not(): void {
		foreach ( array( '/wp-content/uploads/2026/11/photo.jpg', '/fonts/inter.woff2', '/style.css', '/app.js.map' ) as $file ) {
			$this->given_visit(
				array(
					'HTTP_SEC_FETCH_DEST' => 'image',
					'REQUEST_URI'         => $file,
				)
			);

			self::assertNull( $this->observe( 404 ), $file );
		}

		foreach ( array( '/.env', '/wp-config.php.bak', '/xmlrpc.php' ) as $probe ) {
			$this->given_visit(
				array(
					'HTTP_SEC_FETCH_DEST' => 'image',
					'REQUEST_URI'         => $probe,
				)
			);

			self::assertNotNull( $this->observe( 404 ), $probe );
		}
	}

	/**
	 * A browser that is redirected is not reported, since the page it lands on is; a crawler that
	 * is redirected is.
	 */
	public function test_a_redirected_browser_is_left_out_and_a_redirected_crawler_is_not(): void {
		self::assertNotNull( $this->observe( 301 ) );

		$this->given_visit(
			array(
				'HTTP_USER_AGENT'     => self::BROWSER,
				'HTTP_SEC_FETCH_DEST' => 'document',
			)
		);

		self::assertNull( $this->observe( 301 ) );
		self::assertNotNull( $this->observe( 200 ) );
	}

	/**
	 * Feeds, robots.txt and sitemaps are reported while that is chosen, which it is to begin with.
	 */
	public function test_feeds_robots_and_sitemaps_follow_their_setting(): void {
		foreach ( array( 'is_feed', 'is_robots' ) as $function ) {
			Functions\when( $function )->justReturn( true );
			$this->settings = array();
			self::assertNotNull( $this->observe( 200 ), $function );

			$this->settings = array( 'report_feeds' => false );
			self::assertNull( $this->observe( 200 ), $function );
			Functions\when( $function )->justReturn( false );
		}

		Functions\when( 'get_query_var' )->justReturn( 'index' );
		self::assertNull( $this->observe( 200 ) );
	}

	/**
	 * Sign-in attempts are reported only while that is chosen, which it is not to begin with; a
	 * sign-in that succeeds never is, and nor is anybody signing in in a browser.
	 */
	public function test_only_failed_sign_ins_by_programs_are_reported_when_chosen(): void {
		$this->actions = array( 'login_init' => 1 );
		$this->given_visit( array( 'REQUEST_URI' => '/wp-login.php' ) );

		self::assertNull( $this->observe( 200 ) );

		$this->settings = array( 'report_sign_in' => true );
		self::assertNotNull( $this->observe( 200 ), 'a program asking for the sign-in page' );

		$this->given_visit(
			array(
				'REQUEST_URI'    => '/wp-login.php',
				'REQUEST_METHOD' => 'POST',
			)
		);
		self::assertNotNull( $this->observe( 200 ), 'a program\'s sign-in that WordPress did not accept' );

		$this->actions['wp_login'] = 1;
		self::assertNull( $this->observe( 302 ), 'a sign-in that succeeded' );
		unset( $this->actions['wp_login'] );

		$this->given_visit(
			array(
				'REQUEST_URI'         => '/wp-login.php',
				'REQUEST_METHOD'      => 'POST',
				'HTTP_USER_AGENT'     => self::BROWSER,
				'HTTP_SEC_FETCH_DEST' => 'document',
			)
		);
		self::assertNull( $this->observe( 200 ), 'somebody signing in in a browser' );
	}

	/**
	 * XML-RPC is reported only while sign-in attempts are, and only for somebody not signed in.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_xml_rpc_follows_the_sign_in_setting(): void {
		define( 'XMLRPC_REQUEST', true ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedConstantFound -- WordPress's own constant, defined as WordPress defines it.
		$this->actions = array();
		$this->given_visit( array( 'REQUEST_URI' => '/xmlrpc.php' ) );

		self::assertNull( $this->observe( 405 ) );

		$this->settings = array( 'report_sign_in' => true );
		self::assertNotNull( $this->observe( 405 ) );

		Functions\when( 'is_user_logged_in' )->justReturn( true );
		self::assertNull( $this->observe( 200 ) );
	}

	/**
	 * Without an address that can be sent, or from the site itself, nothing is reported.
	 */
	public function test_nothing_is_reported_without_an_address_or_from_the_site_itself(): void {
		$this->given_visit( array( 'REMOTE_ADDR' => '10.0.0.5' ) );
		self::assertNull( $this->observe( 200 ), 'a private address' );

		$this->given_visit( array( 'SERVER_ADDR' => self::VISITOR ) );
		self::assertNull( $this->observe( 200 ), 'the site\'s own address' );

		$this->given_visit();
		self::assertNull( $this->observe( 200, '', '', array( self::VISITOR ) ), 'where its reports come from' );

		$this->given_visit( array( 'HTTP_X_DEWIRIDE_ANALYTICS_OWN' => Own_Request::marker( 'https://example.com/posts/hello/' ) ) );
		self::assertNull( $this->observe( 500 ), 'the plugin\'s own request' );
	}

	/**
	 * A browser navigating to a page is named; nothing else is.
	 */
	public function test_only_a_browser_navigating_to_a_page_is_named(): void {
		self::assertFalse( $this->report()->is_nameable( array() ), 'a crawler' );

		$browser = array(
			'HTTP_USER_AGENT'     => self::BROWSER,
			'HTTP_SEC_FETCH_DEST' => 'document',
		);

		$this->given_visit( $browser );
		self::assertTrue( $this->report()->is_nameable( array() ), 'a browser' );

		$this->given_visit( $browser + array( 'HTTP_SEC_PURPOSE' => 'prefetch' ) );
		self::assertFalse( $this->report()->is_nameable( array() ), 'a prefetch' );

		$this->given_visit( $browser + array( 'REQUEST_METHOD' => 'HEAD' ) );
		self::assertFalse( $this->report()->is_nameable( array() ), 'a HEAD request' );

		$this->given_visit( $browser );
		Functions\when( 'is_feed' )->justReturn( true );
		self::assertFalse( $this->report()->is_nameable( array() ), 'a feed' );
	}

	/**
	 * An address too long for the collector keeps its start and its last part.
	 */
	public function test_an_address_too_long_keeps_its_start_and_last_part(): void {
		$this->given_visit( array( 'REQUEST_URI' => '/archive/' . str_repeat( 'x', 3000 ) . '/the-post-asked-for' ) );

		$url = Request_Report::page_url();

		self::assertLessThanOrEqual( 2048, strlen( $url ) );
		self::assertStringStartsWith( 'https://example.com/archive/xxx', $url );
		self::assertStringEndsWith( '/the-post-asked-for', $url );
	}

	/**
	 * A shortened address never ends on half an escape.
	 */
	public function test_a_shortened_address_ends_on_whole_escapes(): void {
		$this->given_visit( array( 'REQUEST_URI' => '/' . str_repeat( '%C3%A9', 500 ) . '/end' ) );

		$url  = Request_Report::page_url();
		$head = substr( $url, 0, -strlen( '/end' ) );

		self::assertMatchesRegularExpression( '/(?:%C3%A9)+$/', $head );
	}

	/**
	 * The content type is found among the headers sent, whatever their capitals.
	 */
	public function test_the_content_type_is_found_among_the_headers(): void {
		self::assertSame( 'application/rss+xml; charset=UTF-8', Request_Report::content_type_in( array( 'X-Powered-By: PHP', 'content-TYPE:  application/rss+xml; charset=UTF-8 ' ) ) );
		self::assertSame( '', Request_Report::content_type_in( array( 'X-Powered-By: PHP' ) ) );
	}

	/**
	 * Only a language tag is kept of what the browser asks for, and nothing that is not one.
	 */
	public function test_only_a_language_tag_is_kept(): void {
		$this->given_visit( array( 'HTTP_ACCEPT_LANGUAGE' => '*' ) );
		self::assertArrayNotHasKey( 'language', (array) $this->observe( 200 ) );

		$this->given_visit( array( 'HTTP_ACCEPT_LANGUAGE' => 'pt-BR;q=0.9' ) );
		self::assertSame( 'pt-BR', ( (array) $this->observe( 200 ) )['language'] );
	}

	/**
	 * A referrer that is not a web address is not sent.
	 */
	public function test_a_referrer_that_is_not_a_web_address_is_not_sent(): void {
		$this->given_visit( array( 'HTTP_REFERER' => 'android-app://com.google.android.gm/' ) );

		self::assertArrayNotHasKey( 'referrer', (array) $this->observe( 200 ) );
	}

	/**
	 * A REST query added to any other address is reported when the site refused it, or a probe
	 * could hide behind one; the REST API's own addresses are not reported.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_rest_query_elsewhere_is_reported_when_refused(): void {
		define( 'REST_REQUEST', true ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedConstantFound -- WordPress's own constant, defined as WordPress defines it.
		$this->actions = array();

		$this->given_visit( array( 'REQUEST_URI' => '/.env?rest_route=/probe' ) );
		self::assertNotNull( $this->observe( 404, self::JSON ), 'a probe with a REST query' );
		self::assertNull( $this->observe( 200, self::JSON ), 'a REST answer on another address' );

		foreach ( array( '/wp-json/wp/v2/posts', '/?rest_route=/wp/v2/posts', '/index.php?rest_route=/x', '/wp-json' ) as $own ) {
			$this->given_visit( array( 'REQUEST_URI' => $own ) );
			self::assertNull( $this->observe( 404, self::JSON ), $own );
		}
	}

	/**
	 * A page the site refused is reported whatever the method asked for it, and a feed whether or
	 * not feeds are chosen.
	 */
	public function test_a_refusal_is_reported_whatever_the_method_or_the_feeds_switch(): void {
		$this->given_visit( array( 'REQUEST_METHOD' => 'POST' ) );
		self::assertNull( $this->observe( 200 ) );
		self::assertNotNull( $this->observe( 404 ) );

		$this->given_visit();
		$this->settings = array( 'report_feeds' => false );
		Functions\when( 'is_feed' )->justReturn( true );
		self::assertNull( $this->observe( 200 ) );
		self::assertNotNull( $this->observe( 404 ) );
	}

	/**
	 * Cloudflare's header sent before Cloudflare's addresses are known makes the address one that
	 * cannot be believed: a page answered is not reported, and a refusal is, without an address.
	 */
	public function test_a_refusal_is_reported_without_an_address_a_visitor_spoiled(): void {
		$this->given_visit( array( 'HTTP_CF_CONNECTING_IP' => '1' ) );

		self::assertNull( $this->observe( 200 ) );

		$observation = $this->observe( 404 );

		self::assertNotNull( $observation );
		self::assertArrayNotHasKey( 'ipAddress', $observation );
		self::assertSame( 404, $observation['statusCode'] );
	}

	/**
	 * A feed reader told the feed has not changed is reported: WordPress answers it before it sets
	 * itself up for the page, so it is recognised by the request having been parsed.
	 */
	public function test_a_feed_answered_not_modified_is_reported(): void {
		Functions\when( 'is_feed' )->justReturn( true );

		$observation = $this->observe( 304, '' );

		self::assertNotNull( $observation );
		self::assertSame( 304, $observation['statusCode'] );
	}

	/**
	 * An address that cannot be believed for a reason no visitor can bring about — a proxy nobody
	 * named — is not reported, refused or not.
	 */
	public function test_a_refusal_behind_an_unnamed_proxy_is_not_reported(): void {
		$this->given_visit(
			array(
				'REMOTE_ADDR'          => '10.0.0.5',
				'HTTP_X_FORWARDED_FOR' => self::VISITOR,
			)
		);

		self::assertNull( $this->observe( 404 ) );
	}

	/**
	 * Ways of asking for a page as a copy of itself, each chosen by the query, which whoever sends
	 * the request writes.
	 *
	 * @return array<string, array{string}>
	 */
	public function copies(): array {
		return array(
			'a preview'      => array( 'is_preview' ),
			'the Customizer' => array( 'is_customize_preview' ),
			'an embed'       => array( 'is_embed' ),
		);
	}

	/**
	 * A page asked for as a copy is left out when it was answered, and reported when it was
	 * refused, so a scanner cannot hide a probe by asking for an embed or a preview of it.
	 *
	 * @dataProvider copies
	 *
	 * @param string $conditional The conditional tag that is true for such a request.
	 */
	public function test_a_copy_leaves_out_only_what_was_answered( string $conditional ): void {
		Functions\when( $conditional )->justReturn( true );

		self::assertNull( $this->observe( 200 ) );
		self::assertNotNull( $this->observe( 404 ) );
		self::assertNotNull( $this->observe( 403 ) );
	}

	/**
	 * An AMP copy is left out when it was answered and reported when it was refused.
	 *
	 * In a process of its own: stubbing `amp_is_request()` declares it, and a function once
	 * declared stays declared for every test that runs after it.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_an_amp_copy_leaves_out_only_what_was_answered(): void {
		Functions\when( 'amp_is_request' )->justReturn( true );

		self::assertNull( $this->observe( 200 ) );
		self::assertNotNull( $this->observe( 404 ) );
	}

	/**
	 * Somebody signed in whose role is not counted is left out of what the site refused too:
	 * who is signed in is the site's own knowledge, not the visitor's say.
	 */
	public function test_a_refusal_for_somebody_signed_in_is_left_out(): void {
		Functions\when( 'is_user_logged_in' )->justReturn( true );
		Functions\when( 'wp_get_current_user' )->justReturn( (object) array( 'roles' => array( 'editor' ) ) );

		self::assertNull( $this->observe( 404 ) );

		$this->settings = array( 'counted_roles' => array( 'editor' ) );

		self::assertNotNull( $this->observe( 404 ) );
	}

	/**
	 * A request a security plugin refused before WordPress parsed it — while plugins were still
	 * loading — is reported as the page it asked for, as it was sent; one it let through, which
	 * never reached WordPress's front end, is not.
	 */
	public function test_a_request_refused_before_it_was_parsed_is_reported(): void {
		$this->actions = array( 'sanitize_comment_cookies' => 0 );
		$this->given_visit(
			array(
				'REQUEST_URI'     => '/?id=1%27%20UNION%20SELECT%201',
				'QUERY_STRING'    => 'id=1%27%20UNION%20SELECT%201',
				'HTTP_USER_AGENT' => 'sqlmap/1.8 (https://sqlmap.org) \\ probe',
			),
			false
		);

		$observation = $this->observe( 403 );

		self::assertNotNull( $observation );
		self::assertSame( 'https://example.com/', $observation['url'] );
		self::assertSame( 403, $observation['statusCode'] );
		self::assertSame( 'sqlmap/1.8 (https://sqlmap.org) \\ probe', $observation['userAgent'] );
		self::assertNull( $this->observe( 200 ) );
	}

	/**
	 * Refused before it was parsed, a request to the REST API's own address stays out, as it does
	 * once parsed; a REST query added to any other address is a probe like any other.
	 */
	public function test_the_rest_api_refused_before_it_was_parsed_is_not_reported(): void {
		$this->actions = array();

		$this->given_visit(
			array(
				'REQUEST_URI'  => '/wp-json/wp/v2/users',
				'QUERY_STRING' => '',
			)
		);
		self::assertNull( $this->observe( 403 ), 'the REST API under its prefix' );

		$this->given_visit(
			array(
				'REQUEST_URI'  => '/?rest_route=/wp/v2/users',
				'QUERY_STRING' => 'rest_route=/wp/v2/users',
			)
		);
		self::assertNull( $this->observe( 403 ), 'the REST API by its query' );

		$this->given_visit(
			array(
				'REQUEST_URI'  => '/.env?rest_route=/',
				'QUERY_STRING' => 'rest_route=/',
			)
		);
		self::assertNotNull( $this->observe( 403 ), 'a probe with a REST query' );
	}

	/**
	 * Refused before WordPress reached its sign-in page — a plugin hiding that page answers it with
	 * a 404 — a request for it follows the sign-in attempts setting.
	 */
	public function test_the_sign_in_page_refused_before_it_was_parsed_follows_its_setting(): void {
		$this->actions      = array();
		$GLOBALS['pagenow'] = 'wp-login.php'; // phpcs:ignore WordPress.WP.GlobalVariablesOverride.Prohibited -- WordPress's own record of the script asked for, set as WordPress sets it.
		$this->given_visit(
			array(
				'REQUEST_URI'  => '/wp-login.php',
				'QUERY_STRING' => '',
			)
		);

		self::assertNull( $this->observe( 404 ) );

		$this->settings = array( 'report_sign_in' => true );

		self::assertNotNull( $this->observe( 404 ) );

		unset( $GLOBALS['pagenow'] );
	}

	/**
	 * Signing out leaves nobody signed in by the end of the request, and is not a sign-in attempt.
	 */
	public function test_signing_out_is_not_a_sign_in_attempt(): void {
		$this->settings = array( 'report_sign_in' => true );
		$this->actions  = array(
			'login_init' => 1,
			'wp_logout'  => 1,
		);
		$this->given_visit( array( 'REQUEST_URI' => '/wp-login.php?action=logout' ) );

		self::assertNull( $this->observe( 302 ) );
	}

	/**
	 * A user name and password written into the page somebody came from are never sent.
	 */
	public function test_a_referrer_is_sent_without_a_user_name_or_password(): void {
		$this->given_visit( array( 'HTTP_REFERER' => 'https://alice:s3cret@staging.example.com:8443/reports/q3?x=1' ) );

		self::assertSame( 'https://staging.example.com:8443/reports/q3', self::dig( $this->observe( 200 ), 'referrer' ) );
	}

	/**
	 * On a site served over http, browsers say nothing of where a request goes, and are known by
	 * asking for https instead: a browser redirected there is left to the page it lands on.
	 */
	public function test_a_browser_redirected_on_an_http_site_is_left_out(): void {
		$this->given_visit(
			array(
				'HTTP_USER_AGENT'                => self::BROWSER,
				'HTTP_UPGRADE_INSECURE_REQUESTS' => '1',
			)
		);

		self::assertNull( $this->observe( 301 ) );
		self::assertNotNull( $this->observe( 200 ) );
	}

	/**
	 * A probe sent through another Cloudflare customer's zone is reported when it was refused,
	 * without the address it claimed, and left out when it was answered.
	 */
	public function test_a_refusal_through_another_cloudflare_zone_is_reported_without_an_address(): void {
		$this->options_given[ Cloudflare_Ranges::OPTION ] = array( 'ranges' => array( '173.245.48.0/20' ) );
		$this->given_visit(
			array(
				'REMOTE_ADDR'           => '173.245.48.20',
				'HTTP_HOST'             => 'attacker.example',
				'HTTP_CF_CONNECTING_IP' => '66.249.66.1',
				'REQUEST_URI'           => '/.env',
			)
		);

		self::assertNull( $this->observe( 200 ) );

		$observation = $this->observe( 404 );

		self::assertNotNull( $observation );
		self::assertArrayNotHasKey( 'ipAddress', $observation );
	}

	/**
	 * When the request arrived is sent in milliseconds since 1970.
	 */
	public function test_when_the_request_arrived_is_sent_in_milliseconds(): void {
		self::assertSame( 1763200000250, self::dig( $this->observe( 200 ), 'observedAt' ) );
	}

	/**
	 * A browser on the site's own address is not reported, so its page is not named either.
	 */
	public function test_a_browser_on_the_sites_own_address_is_not_named(): void {
		$this->given_visit(
			array(
				'SERVER_ADDR'         => self::VISITOR,
				'HTTP_USER_AGENT'     => self::BROWSER,
				'HTTP_SEC_FETCH_DEST' => 'document',
			)
		);

		self::assertFalse( $this->report()->is_nameable( array() ) );
	}

	/**
	 * Makes the request a crawler's, with what the test changes.
	 *
	 * @param array<string, mixed> $changes What differs from the crawler's request.
	 * @param bool                 $slashed Whether WordPress has slashed it yet, which it has not
	 *                                      for a request refused while plugins load.
	 */
	private function given_visit( array $changes = array(), bool $slashed = true ): void {
		$this->given_request(
			$changes + array(
				'REMOTE_ADDR'          => self::VISITOR,
				'SERVER_ADDR'          => '198.51.100.20',
				'REQUEST_METHOD'       => 'GET',
				'REQUEST_URI'          => '/posts/hello/?utm_source=mail',
				'QUERY_STRING'         => 'utm_source=mail',
				'HTTP_USER_AGENT'      => self::CRAWLER,
				'HTTP_REFERER'         => 'https://news.example/front?id=7#comments',
				'HTTP_ACCEPT_LANGUAGE' => 'en-GB,en;q=0.9',
				'REQUEST_TIME_FLOAT'   => 1763200000.25,
			),
			$slashed
		);
	}

	/**
	 * What is reported of the request, answered as given.
	 *
	 * @param int    $status       The status the site answered with.
	 * @param string $content_type The content type it answered with.
	 * @param string $name         The name the page was sent under.
	 * @param array  $sends_from   The addresses reports arrive from.
	 * @phpstan-param list<string> $sends_from
	 * @return array<string, int|string>|null
	 */
	private function observe( int $status, string $content_type = 'text/html', string $name = '', array $sends_from = array() ): ?array {
		return $this->report()->observation( $name, $sends_from, $status, $content_type );
	}

	/**
	 * The report, over the settings in each test.
	 */
	private function report(): Request_Report {
		$settings = new Settings();

		return new Request_Report( $settings, new Audience( $settings ), new Visitor_Address( $settings, new Cloudflare_Ranges() ) );
	}
}
