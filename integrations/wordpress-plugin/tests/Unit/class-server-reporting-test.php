<?php
/**
 * Tests for the server reporting what it answers.
 *
 * Naming a page writes a header, which PHP's command line cannot send once anything has been
 * printed, so a test of the naming replaces the header functions in the plugin's own namespace,
 * in a process of its own: a function once declared stays declared for every test after it. The
 * name reaching a browser is tested by the smoke harness in a real WordPress.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Audience;
use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Dewiride\Analytics\WordPress\Collector_Client;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Request_Report;
use Dewiride\Analytics\WordPress\Server_Key;
use Dewiride\Analytics\WordPress\Server_Reporting;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tag_Check;
use Dewiride\Analytics\WordPress\Visitor_Address;
use Mockery;

/**
 * Reports are sent at the end of the request only when everything they need is in place, and
 * stay off on a server that would hold the page open for them unless that is accepted.
 */
final class Server_Reporting_Test extends Test_Case {

	/**
	 * A key shaped as Dewiride Analytics issues them; plainly not a real one.
	 */
	private const KEY = 'dwk_ccccccccccccccccccccccccccccccccccccccccccc';

	/**
	 * The crawler's address.
	 */
	private const VISITOR = '203.0.113.7';

	/**
	 * The page asked for.
	 */
	private const PAGE = '/posts/hello/';

	/**
	 * Where a redirect leads.
	 */
	private const REDIRECT = 'https://example.com/hello-world/';

	/**
	 * A key set in `wp-config.php`; plainly not a real one either.
	 */
	private const CONFIG_KEY = 'dwk_ddddddddddddddddddddddddddddddddddddddddddd';

	/**
	 * The plugin's namespace, in which a test replaces a function PHP itself defines.
	 */
	private const PLUGIN = 'Dewiride\\Analytics\\WordPress\\';

	/**
	 * The options in each test, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $options = array();

	/**
	 * The headers of the response, where a test replaces the functions that write them.
	 *
	 * @var list<string>
	 */
	private $headers = array();

	/**
	 * What the request did, in order, where a test follows it.
	 *
	 * @var list<string>
	 */
	private $calls = array();

	/**
	 * Starts each test with a key saved, reports accepted even though this server holds pages
	 * open, and a crawler's page being answered.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->options = array(
			Settings::OPTION => array(
				'address'           => self::ADDRESS,
				'site_id'           => self::SITE_ID,
				'key_ending'        => 'cccc',
				'report_unfinished' => true,
			),
		);
		Functions\when( 'get_option' )->alias(
			function ( string $option, $fallback = false ) {
				return $this->options[ $option ] ?? $fallback;
			}
		);
		Functions\when( 'update_option' )->alias(
			function ( string $option, $value ): bool {
				$this->options[ $option ] = $value;

				return true;
			}
		);
		self::given_database(
			new Fake_Database(
				function ( string $option ) {
					return $this->options[ $option ] ?? false;
				},
				function ( string $option, $value ): void {
					$this->options[ $option ] = $value;
				}
			)
		);
		Functions\when( 'did_action' )->alias(
			static function ( string $action ): int {
				return in_array( $action, array( 'parse_request', 'sanitize_comment_cookies' ), true ) ? 1 : 0;
			}
		);
		foreach ( array( 'doing_action', 'wp_doing_cron', 'wp_doing_ajax', 'is_admin', 'is_feed', 'is_robots', 'is_favicon', 'is_preview', 'is_customize_preview', 'is_embed', 'is_user_logged_in' ) as $function ) {
			Functions\when( $function )->justReturn( false );
		}
		Functions\when( 'get_query_var' )->justReturn( '' );
		Functions\when( 'is_wp_error' )->justReturn( false );
		Functions\when( 'wp_remote_retrieve_response_code' )->justReturn( 200 );
		Functions\when( 'wp_remote_retrieve_body' )->justReturn( '{"accepted":1,"rejected":0}' );
		Functions\when( 'wp_remote_retrieve_header' )->justReturn( '' );
		( new Server_Key() )->save( self::KEY );
		$this->given_request(
			array(
				'REMOTE_ADDR'     => self::VISITOR,
				'REQUEST_METHOD'  => 'GET',
				'REQUEST_URI'     => self::PAGE,
				'HTTP_USER_AGENT' => 'Mozilla/5.0 (compatible; ExampleBot/1.0)',
			)
		);
	}

	/**
	 * The page is named once the query has run, the request is told to carry on before anything
	 * else at its end, and the report is sent at the very end.
	 */
	public function test_it_hooks_the_naming_and_the_report(): void {
		$reporting = $this->reporting();

		$reporting->register();

		self::assertSame( 10, has_action( 'wp', array( $reporting, 'name_page' ) ) );
		self::assertSame( PHP_INT_MAX, has_filter( 'wp_redirect', array( $reporting, 'unname_redirect' ) ) );
		self::assertSame( PHP_INT_MIN, has_action( 'shutdown', array( $reporting, 'keep_going' ) ) );
		self::assertSame( PHP_INT_MAX, has_action( 'shutdown', array( $reporting, 'report' ) ) );
	}

	/**
	 * The request is told to carry on should the visitor hang up, only while reports are sent.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_the_request_carries_on_after_a_hang_up_only_while_reporting(): void {
		Functions\expect( self::PLUGIN . 'ignore_user_abort' )->once()->with( true );

		$this->reporting()->keep_going();

		$this->options[ Settings::OPTION ] = array( 'report_server' => false ) + self::as_array( $this->options[ Settings::OPTION ] );
		$this->reporting()->keep_going();
	}

	/**
	 * A browser's page is named only once the installation has named the website in an answer,
	 * which shows it joins names safely: one that does not would hand whoever echoed a name first
	 * every visit the crawler that filled a cache made that day.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_page_is_named_only_once_the_installation_names_the_website(): void {
		$this->given_browser_navigation();
		$this->given_response_headers();

		$this->reporting()->name_page();

		self::assertSame( array(), $this->headers );

		$this->given_installation_naming_the_website();
		$this->reporting()->name_page();

		self::assertCount( 1, $this->headers );
		self::assertMatchesRegularExpression( '/^Server-Timing: dw;desc="[0-9a-f]{32}"$/', $this->headers[0] );
	}

	/**
	 * A page that turns out to be a redirect loses its name, and keeps every other timing the
	 * response carries, another plugin's measurements among them.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_redirect_loses_its_name_and_keeps_every_other_timing(): void {
		$this->given_browser_navigation();
		$this->given_installation_naming_the_website();
		$this->given_response_headers( 'Server-Timing: other;dur=1', 'Content-Type: text/html; charset=UTF-8' );
		$reporting = $this->reporting();
		$reporting->name_page();

		self::assertCount( 3, $this->headers );
		self::assertSame( self::REDIRECT, $reporting->unname_redirect( self::REDIRECT ) );
		self::assertSame( array( 'Content-Type: text/html; charset=UTF-8', 'Server-Timing: other;dur=1' ), $this->headers );
	}

	/**
	 * Servers that can hand a page over before the request ends, by the function they do it with.
	 *
	 * @return array<string, array{string}>
	 */
	public function servers_finishing_pages_first(): array {
		return array(
			'PHP-FPM'   => array( 'fastcgi_finish_request' ),
			'LiteSpeed' => array( 'litespeed_finish_request' ),
		);
	}

	/**
	 * Where the server can hand the page over first, reports are on without held pages being
	 * accepted, and the page is handed over before the key is opened or the report sent.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 * @dataProvider servers_finishing_pages_first
	 *
	 * @param string $finish The function the server hands a page over with.
	 */
	public function test_a_server_finishing_pages_first_hands_the_page_over_before_reporting( string $finish ): void {
		$this->options[ Settings::OPTION ] = array( 'report_unfinished' => false ) + self::as_array( $this->options[ Settings::OPTION ] );
		Functions\when( $finish )->alias(
			function (): bool {
				$this->calls[] = 'page handed over';

				return true;
			}
		);
		Functions\when( 'get_option' )->alias(
			function ( string $option, $fallback = false ) {
				if ( Server_Key::OPTION === $option ) {
					$this->calls[] = 'key opened';
				}

				return $this->options[ $option ] ?? $fallback;
			}
		);
		Functions\when( 'wp_remote_post' )->alias(
			function (): array {
				$this->calls[] = 'report sent';

				return array();
			}
		);
		$reporting = $this->reporting();

		self::assertTrue( Server_Reporting::finishes_pages_first() );
		self::assertTrue( $reporting->is_sending() );

		$reporting->report();

		self::assertSame( 'page handed over', $this->calls[0] ?? null );
		self::assertContains( 'report sent', $this->calls );
	}

	/**
	 * A key set in `wp-config.php` reports as a saved one does, under that key.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_key_in_wp_config_reports_under_that_key(): void {
		define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', self::CONFIG_KEY );
		$this->options[ Settings::OPTION ] = array( 'key_ending' => '' ) + self::as_array( $this->options[ Settings::OPTION ] );
		unset( $this->options[ Server_Key::OPTION ] );
		Functions\expect( 'wp_remote_post' )
			->once()
			->with(
				self::ADDRESS . '/collect/server',
				Mockery::on(
					static function ( $arguments ): bool {
						return 'Bearer ' . self::CONFIG_KEY === self::dig( $arguments, 'headers', 'Authorization' );
					}
				)
			)
			->andReturn( array() );

		self::assertSame( 'dddd', ( new Settings() )->key_ending() );

		$this->reporting()->report();
	}

	/**
	 * A key in `wp-config.php` that is not a whole key sends nothing, and the key saved here does
	 * not stand in for it.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_malformed_key_in_wp_config_sends_nothing(): void {
		define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', 'dwk_short' );
		Functions\expect( 'wp_remote_post' )->never();

		$reporting = $this->reporting();

		self::assertFalse( $reporting->is_sending() );
		$reporting->report();
	}

	/**
	 * A redirect of a page that was never named leaves the address it leads to alone.
	 */
	public function test_an_unnamed_redirect_is_left_alone(): void {
		self::assertSame( self::REDIRECT, $this->reporting()->unname_redirect( self::REDIRECT ) );
	}

	/**
	 * PHP's command line, like mod_php, cannot finish a page before the request ends.
	 */
	public function test_the_command_line_cannot_finish_a_page_first(): void {
		self::assertFalse( Server_Reporting::finishes_pages_first() );
	}

	/**
	 * A crawler's page is reported at the end of the request, in the two seconds a report gets.
	 */
	public function test_a_crawlers_page_is_reported(): void {
		Functions\expect( 'wp_remote_post' )
			->once()
			->with(
				self::ADDRESS . '/collect/server',
				Mockery::on(
					static function ( $arguments ): bool {
						$body = json_decode( self::dig_text( $arguments, 'body' ), true );

						return 2 === self::dig( $arguments, 'timeout' )
							&& 'https://example.com/posts/hello/' === self::dig( $body, 'events', 0, 'url' )
							&& self::VISITOR === self::dig( $body, 'events', 0, 'ipAddress' )
							&& null === self::dig( $body, 'events', 0, 'correlationId' );
					}
				)
			)
			->andReturn( array() );

		$this->reporting()->report();
	}

	/**
	 * Nothing a visitor sent is kept: the options hold counts, times and reasons, never an address,
	 * a page or a user agent.
	 */
	public function test_nothing_a_visitor_sent_is_kept(): void {
		Functions\when( 'wp_remote_post' )->justReturn( array() );
		Functions\when( 'wp_remote_retrieve_body' )->justReturn( '{"accepted":1,"rejected":0,"site":"' . self::SITE_ID . '","from":"198.51.100.20"}' );
		$this->options[ Reporting_Status::OPTION ] = array( 'sent_at' => 1 ) + ( new Reporting_Status( new Settings() ) )->state();

		$this->reporting()->report();

		$kept = (string) wp_json_encode( $this->options );

		self::assertStringContainsString( '198.51.100.20', $kept );
		self::assertStringNotContainsString( self::VISITOR, $kept );
		self::assertStringNotContainsString( 'hello', $kept );
		self::assertStringNotContainsString( 'ExampleBot', $kept );
	}

	/**
	 * What keeps reports from being sent at all.
	 *
	 * @return array<string, array{array<string, mixed>}>
	 */
	public function unready_settings(): array {
		return array(
			'counting is off'                    => array( array( 'site_id' => '' ) ),
			'there is no key'                    => array( array( 'key_ending' => '' ) ),
			'reporting is switched off'          => array( array( 'report_server' => false ) ),
			'holding pages open is not accepted' => array( array( 'report_unfinished' => false ) ),
		);
	}

	/**
	 * An address that would carry the key unencrypted is sent nothing. Whether it may be sent the
	 * key can take a lookup of its name, so it is asked only once a report is about to go, never
	 * while a page is being answered.
	 */
	public function test_an_address_that_would_send_the_key_unencrypted_is_sent_nothing(): void {
		$this->options[ Settings::OPTION ] = array( 'address' => 'http://203.0.113.10' ) + self::as_array( $this->options[ Settings::OPTION ] );
		Functions\expect( 'wp_remote_post' )->never();

		$reporting = $this->reporting();

		self::assertTrue( $reporting->is_sending() );
		$reporting->report();
	}

	/**
	 * Without everything in place, nothing is sent.
	 *
	 * @dataProvider unready_settings
	 *
	 * @param array<string, mixed> $settings What differs from settings ready to report.
	 */
	public function test_without_everything_in_place_nothing_is_sent( array $settings ): void {
		$this->options[ Settings::OPTION ] = $settings + self::as_array( $this->options[ Settings::OPTION ] );
		Functions\expect( 'wp_remote_post' )->never();

		$reporting = $this->reporting();

		self::assertFalse( $reporting->is_sending() );
		$reporting->report();
	}

	/**
	 * While the tag does not reach readers, nothing is sent; when the page could not be checked,
	 * reports go on.
	 */
	public function test_reports_pause_while_the_tag_is_kept_from_readers(): void {
		$this->options[ Tag_Check::OPTION ] = array(
			'outcome'    => Tag_Check::ALTERED,
			'tool'       => Tag_Check::CONSENT_TOOL,
			'checked_at' => time(),
		);

		self::assertFalse( $this->reporting()->is_sending() );

		$this->options[ Tag_Check::OPTION ] = array( 'outcome' => Tag_Check::UNREACHABLE ) + self::as_array( $this->options[ Tag_Check::OPTION ] );

		self::assertTrue( $this->reporting()->is_sending() );
	}

	/**
	 * Stopped reports stay stopped.
	 */
	public function test_stopped_reports_send_nothing(): void {
		( new Reporting_Status( new Settings() ) )->stop( Reporting_Status::KEY_REFUSED );
		Functions\expect( 'wp_remote_post' )->never();

		$this->reporting()->report();
	}

	/**
	 * A request that is not reported sends nothing, however ready the reports are.
	 */
	public function test_a_request_not_reported_sends_nothing(): void {
		Functions\when( 'is_user_logged_in' )->justReturn( true );
		Functions\when( 'wp_get_current_user' )->justReturn( (object) array( 'roles' => array( 'administrator' ) ) );
		Functions\expect( 'wp_remote_post' )->never();

		$this->reporting()->report();
	}

	/**
	 * Once a wait after failures is over, the report that tries again takes the turn first.
	 */
	public function test_the_report_trying_again_takes_the_turn(): void {
		$status = new Reporting_Status( new Settings() );
		$status->failed( 503 );
		$this->options[ Reporting_Status::OPTION ] = array( 'retry_at' => time() - 1 ) + self::as_array( $this->options[ Reporting_Status::OPTION ] );
		Functions\expect( 'wp_remote_post' )->once()->andReturn( array() );

		$this->reporting()->report();

		self::assertSame( 0, self::dig( $this->options, Reporting_Status::OPTION, 'failures' ) );
	}


	/**
	 * A key that no longer opens stops the reports once, rather than leaving every page finished
	 * early for a report that is never sent.
	 */
	public function test_a_key_that_no_longer_opens_stops_the_reports(): void {
		Functions\when( 'wp_salt' )->justReturn( 'new secret keys' );
		Functions\expect( 'wp_remote_post' )->never();

		$this->reporting()->report();

		self::assertSame( Reporting_Status::KEY_UNREADABLE, self::dig( $this->options, Reporting_Status::OPTION, 'stopped' ) );
		self::assertFalse( $this->reporting()->is_sending() );
	}

	/**
	 * A request through Cloudflare has Cloudflare's list fetched, once reports are being sent.
	 */
	public function test_cloudflares_list_is_asked_for_only_while_sending(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'           => self::VISITOR,
				'REQUEST_METHOD'        => 'GET',
				'REQUEST_URI'           => self::PAGE,
				'HTTP_CF_CONNECTING_IP' => '198.51.100.7',
			)
		);
		Functions\when( 'wp_next_scheduled' )->justReturn( false );
		Functions\when( 'wp_remote_post' )->justReturn( array() );
		Functions\expect( 'wp_schedule_event' )->once()->with( Mockery::type( 'int' ), 'weekly', Cloudflare_Ranges::EVENT );

		$this->reporting()->report();

		$this->options[ Settings::OPTION ] = array( 'report_server' => false ) + self::as_array( $this->options[ Settings::OPTION ] );
		$this->reporting()->report();
	}

	/**
	 * A redirect an earlier filter cancelled is left as it is.
	 */
	public function test_a_cancelled_redirect_is_left_alone(): void {
		self::assertFalse( $this->reporting()->unname_redirect( false ) );
	}

	/**
	 * A browser navigating to the crawler's page.
	 */
	private function given_browser_navigation(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'         => self::VISITOR,
				'REQUEST_METHOD'      => 'GET',
				'REQUEST_URI'         => self::PAGE,
				'HTTP_USER_AGENT'     => 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36',
				'HTTP_SEC_FETCH_DEST' => 'document',
			)
		);
	}

	/**
	 * An installation whose answers name the website, which shows it joins names safely.
	 */
	private function given_installation_naming_the_website(): void {
		$this->options[ Reporting_Status::OPTION ] = array( 'names_site' => true ) + ( new Reporting_Status( new Settings() ) )->state();
	}

	/**
	 * Replaces the functions the plugin writes headers with by a list of the response's headers.
	 *
	 * @param string ...$headers The headers the response already carries.
	 */
	private function given_response_headers( string ...$headers ): void {
		$this->headers = $headers;
		Functions\when( self::PLUGIN . 'headers_sent' )->justReturn( false );
		Functions\when( self::PLUGIN . 'headers_list' )->alias(
			function (): array {
				return $this->headers;
			}
		);
		Functions\when( self::PLUGIN . 'header' )->alias(
			function ( string $header ): void {
				$this->headers[] = $header;
			}
		);
		Functions\when( self::PLUGIN . 'header_remove' )->alias(
			function ( string $name ): void {
				$this->headers = array_values(
					array_filter(
						$this->headers,
						static function ( string $header ) use ( $name ): bool {
							return 0 !== stripos( $header, $name . ':' );
						}
					)
				);
			}
		);
	}

	/**
	 * The reporting over the options in each test.
	 */
	private function reporting(): Server_Reporting {
		$settings = new Settings();
		$status   = new Reporting_Status( $settings );

		return new Server_Reporting(
			$settings,
			new Server_Key(),
			$status,
			new Request_Report( $settings, new Audience( $settings ), new Visitor_Address( $settings, new Cloudflare_Ranges() ) ),
			new Collector_Client( $settings, new Server_Key(), $status ),
			new Tag_Check( $settings )
		);
	}
}
