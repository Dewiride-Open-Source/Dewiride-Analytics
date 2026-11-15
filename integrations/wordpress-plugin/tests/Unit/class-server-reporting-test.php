<?php
/**
 * Tests for the server reporting what it answers.
 *
 * Naming a page writes a header, which PHP's command line cannot send once anything has been
 * printed, so the name reaching the browser is tested by the smoke harness in a real WordPress.
 * What is tested here is when reports are sent at all, and what is sent.
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
	 * The options in each test, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $options = array();

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
				return 'parse_request' === $action ? 1 : 0;
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
				'REQUEST_URI'     => '/posts/hello/',
				'HTTP_USER_AGENT' => 'Mozilla/5.0 (compatible; ExampleBot/1.0)',
			)
		);
	}

	/**
	 * The page is named as its headers are sent, and reported at the very end of the request.
	 */
	public function test_it_hooks_the_naming_and_the_report(): void {
		$reporting = $this->reporting();

		$reporting->register();

		self::assertSame( 10, has_action( 'send_headers', array( $reporting, 'name_page' ) ) );
		self::assertSame( PHP_INT_MAX, has_filter( 'wp_redirect', array( $reporting, 'unname_redirect' ) ) );
		self::assertSame( PHP_INT_MAX, has_action( 'shutdown', array( $reporting, 'report' ) ) );
	}

	/**
	 * A redirect of a page that was never named leaves the address it leads to alone.
	 */
	public function test_an_unnamed_redirect_is_left_alone(): void {
		self::assertSame( 'https://example.com/hello-world/', $this->reporting()->unname_redirect( 'https://example.com/hello-world/' ) );
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
			'counting is off'                            => array( array( 'site_id' => '' ) ),
			'there is no key'                            => array( array( 'key_ending' => '' ) ),
			'reporting is switched off'                  => array( array( 'report_server' => false ) ),
			'holding pages open is not accepted'         => array( array( 'report_unfinished' => false ) ),
			'the address would send the key unencrypted' => array( array( 'address' => 'http://203.0.113.10' ) ),
		);
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
				'REQUEST_URI'           => '/posts/hello/',
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
