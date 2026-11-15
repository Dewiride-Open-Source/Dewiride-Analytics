<?php
/**
 * Tests for checking the connection.
 *
 * WordPress's HTTP class exists only inside WordPress, so a test that reaches it stands one in,
 * which PHP allows once per process; those tests each run in a process of their own.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Collector_Client;
use Dewiride\Analytics\WordPress\Connection_Check;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Server_Key;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Settings_Page;
use Dewiride\Analytics\WordPress\Tag_Check;
use Mockery;
use RuntimeException;

/**
 * An empty report is sent, and its answer is read against this site field by field; only the
 * outcome is kept.
 */
final class Connection_Check_Test extends Test_Case {

	/**
	 * A key shaped as Dewiride Analytics issues them; plainly not a real one.
	 */
	private const KEY = 'dwk_ddddddddddddddddddddddddddddddddddddddddddd';

	/**
	 * An address other than the one saved, where a redirect leads.
	 */
	private const ELSEWHERE = 'https://analytics.example.org';

	/**
	 * The options in each test, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $options = array();

	/**
	 * Starts each test set up, with answers described by plain arrays.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->options = array(
			Settings::OPTION => array(
				'address'    => self::ADDRESS,
				'site_id'    => self::SITE_ID,
				'key_ending' => 'dddd',
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
		Functions\when( 'is_wp_error' )->justReturn( false );
		$this->given_responses_as_arrays();
		( new Server_Key() )->save( self::KEY );
	}

	/**
	 * The button is hooked.
	 */
	public function test_the_button_is_hooked(): void {
		$check = $this->check();

		$check->register_admin();

		self::assertSame( 10, has_action( 'admin_post_' . Connection_Check::ACTION, array( $check, 'handle_request' ) ) );
	}

	/**
	 * Without a key, nothing is sent and the outcome says what to add.
	 */
	public function test_without_a_key_nothing_is_sent(): void {
		unset( $this->options[ Server_Key::OPTION ] );
		Functions\expect( 'wp_remote_post' )->never();

		self::assertSame( Connection_Check::NOT_SET_UP, $this->check()->run()['outcome'] );
		self::assertSame( Connection_Check::NOT_SET_UP, self::dig( $this->options, Connection_Check::OPTION, 'outcome' ) );
	}

	/**
	 * An address that would send the key unencrypted is refused before anything is sent.
	 */
	public function test_an_unencrypted_address_is_refused_before_sending(): void {
		$this->options[ Settings::OPTION ] = array( 'address' => 'http://203.0.113.10' ) + self::as_array( $this->options[ Settings::OPTION ] );
		Functions\expect( 'wp_remote_post' )->never();

		self::assertSame( Connection_Check::INSECURE, $this->check()->run()['outcome'] );
	}

	/**
	 * WordPress set to block requests to other sites is said to be so.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_wordpress_blocking_requests_is_said_to(): void {
		Mockery::mock( 'overload:WP_Http' )->shouldReceive( 'block_request' )->andReturn( true );
		Functions\expect( 'wp_remote_post' )->never();

		self::assertSame( Connection_Check::BLOCKED, $this->check()->run()['outcome'] );
	}

	/**
	 * Answers that refused the report, and what each means.
	 *
	 * @return array<string, array{int, array<string, string>, string, string}>
	 */
	public function refusals(): array {
		return array(
			'no answer'          => array( 0, array(), Connection_Check::UNREACHABLE, '' ),
			'a certificate'      => array( -1, array(), Connection_Check::CERTIFICATE, '' ),
			'a refused key'      => array( 401, array(), Connection_Check::KEY_REFUSED, '' ),
			'a redirect'         => array( 308, array( 'location' => self::ELSEWHERE . '/collect/server' ), Connection_Check::REDIRECTED, self::ELSEWHERE ),
			'a path moved'       => array( 301, array( 'location' => '/collect/server/' ), Connection_Check::REDIRECTED, '' ),
			'a pause'            => array( 429, array(), Connection_Check::BUSY, '' ),
			'an error'           => array( 502, array(), Connection_Check::UNEXPECTED, '' ),
			'a page, no batch'   => array( 200, array(), Connection_Check::NOT_DEWIRIDE, '' ),
			'nothing there'      => array( 404, array(), Connection_Check::NOT_DEWIRIDE, '' ),
			'a method not taken' => array( 405, array(), Connection_Check::NOT_DEWIRIDE, '' ),
			'turned away'        => array( 403, array(), Connection_Check::TURNED_AWAY, '' ),
		);
	}

	/**
	 * Each refusal is read as what it means, with the address a redirect leads to.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 * @dataProvider refusals
	 *
	 * @param int                   $status  The answer's status, or 0 for none and -1 for none
	 *                                       because the certificate was not accepted.
	 * @param array<string, string> $headers The answer's headers.
	 * @param string                $outcome What it means.
	 * @param string                $detail  What else is kept.
	 */
	public function test_each_refusal_is_read_for_what_it_means( int $status, array $headers, string $outcome, string $detail ): void {
		$failures = array(
			0  => 'cURL error 7: Failed to connect to analytics.example.com port 443',
			-1 => 'cURL error 60: SSL certificate problem: unable to get local issuer certificate',
		);

		$this->given_http_allowed();
		Functions\when( 'is_wp_error' )->alias(
			static function ( $thing ): bool {
				return $thing instanceof Fake_Wp_Error;
			}
		);
		Functions\when( 'wp_remote_post' )->justReturn( isset( $failures[ $status ] ) ? new Fake_Wp_Error( $failures[ $status ] ) : self::answer( $status, array(), $headers ) );

		$result = $this->check()->run();

		self::assertSame( $outcome, $result['outcome'] );
		self::assertSame( $detail, $result['detail'] );
	}

	/**
	 * An answer from an installation that names the website, covers this site, measures it and
	 * serves the tag is a connection, and ends any stop.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_full_answer_in_order_is_a_connection(): void {
		$this->given_http_allowed();
		( new Reporting_Status( new Settings() ) )->stop( Reporting_Status::KEY_REFUSED );
		Functions\expect( 'wp_remote_post' )
			->once()
			->with( self::ADDRESS . '/collect/server', Mockery::on( self::empty_batch() ) )
			->andReturn( self::answer( 200, self::full_answer() ) );
		Functions\expect( 'wp_remote_get' )->once()->with( self::ADDRESS . '/dw.js', Mockery::type( 'array' ) )->andReturn( self::answer( 200, array() ) );

		$result = $this->check()->run();

		self::assertSame( Connection_Check::CONNECTED, $result['outcome'] );
		self::assertFalse( $result['older'] );
		self::assertSame( '', self::dig( $this->options, Reporting_Status::OPTION, 'stopped' ) );
		self::assertTrue( self::dig( $this->options, Reporting_Status::OPTION, 'names_site' ) );
	}

	/**
	 * Answers that accepted the report but say something is wrong for this site.
	 *
	 * @return array<string, array{array<string, mixed>, string, string}>
	 */
	public function answers_wrong_for_this_site(): array {
		return array(
			'another website\'s key'       => array( array( 'site' => '0197c0de-0000-7000-8000-000000000001' ), Connection_Check::OTHER_WEBSITE, '' ),
			'a website at another address' => array( array( 'hosts' => array( 'example.org', 'blog.example.net' ) ), Connection_Check::UNCOVERED, 'example.org, blog.example.net' ),
			'a website not being measured' => array( array( 'measuring' => false ), Connection_Check::NOT_MEASURING, '' ),
			'a closed account'             => array(
				array(
					'hosts'     => array(),
					'measuring' => false,
				),
				Connection_Check::NOT_MEASURING,
				'',
			),
		);
	}

	/**
	 * Each field is read against this site.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 * @dataProvider answers_wrong_for_this_site
	 *
	 * @param array<string, mixed> $differences What differs from an answer in order.
	 * @param string               $outcome     What it means.
	 * @param string               $detail      What else is kept.
	 */
	public function test_each_field_is_read_against_this_site( array $differences, string $outcome, string $detail ): void {
		$this->given_http_allowed();
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, $differences + self::full_answer() ) );
		Functions\when( 'wp_remote_get' )->justReturn( self::answer( 200, array() ) );

		$result = $this->check()->run();

		self::assertSame( $outcome, $result['outcome'] );
		self::assertSame( $detail, $result['detail'] );
	}

	/**
	 * An installation older than the answer's last fields is still a connection, said to be one
	 * that could not be checked in full.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_an_older_installation_is_a_connection_checked_in_part(): void {
		$this->given_http_allowed();
		Functions\when( 'wp_remote_post' )->justReturn(
			self::answer(
				200,
				array(
					'accepted' => 0,
					'rejected' => 0,
				)
			)
		);
		Functions\when( 'wp_remote_get' )->justReturn( self::answer( 200, array() ) );

		$result = $this->check()->run();

		self::assertSame( Connection_Check::CONNECTED, $result['outcome'] );
		self::assertTrue( $result['older'] );
		self::assertStringContainsString( 'older version', Connection_Check::message( $result ) );
	}

	/**
	 * An address that does not serve the tag's script is an engine's alone, which browsers cannot
	 * load the tag from.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_an_address_without_the_script_is_said_to_be_wrong(): void {
		$this->given_http_allowed();
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, self::full_answer() ) );
		Functions\when( 'wp_remote_get' )->justReturn( self::answer( 404, array() ) );

		self::assertSame( Connection_Check::NO_SCRIPT, $this->check()->run()['outcome'] );
	}

	/**
	 * Hosts and the names below them are covered, and nothing else.
	 */
	public function test_a_host_and_the_names_below_it_are_covered(): void {
		self::assertTrue( Connection_Check::covers( 'example.com', array( 'Example.com' ) ) );
		self::assertTrue( Connection_Check::covers( 'blog.example.com', array( 'example.com' ) ) );
		self::assertFalse( Connection_Check::covers( 'badexample.com', array( 'example.com' ) ) );
		self::assertFalse( Connection_Check::covers( 'example.com', array( 'blog.example.com' ) ) );
	}

	/**
	 * Every outcome has its own sentence.
	 */
	public function test_every_outcome_has_its_own_sentence(): void {
		$outcomes = array(
			Connection_Check::CONNECTED,
			Connection_Check::NOT_SET_UP,
			Connection_Check::INSECURE,
			Connection_Check::BLOCKED,
			Connection_Check::UNREACHABLE,
			Connection_Check::REDIRECTED,
			Connection_Check::KEY_REFUSED,
			Connection_Check::BUSY,
			Connection_Check::UNEXPECTED,
			Connection_Check::OTHER_WEBSITE,
			Connection_Check::UNCOVERED,
			Connection_Check::NOT_MEASURING,
			Connection_Check::NO_SCRIPT,
			Connection_Check::KEY_UNREADABLE,
			Connection_Check::KEY_MALFORMED,
			Connection_Check::NOT_DEWIRIDE,
			Connection_Check::TURNED_AWAY,
			Connection_Check::CERTIFICATE,
		);

		$sentences = array_combine(
			$outcomes,
			array_map(
				static function ( string $outcome ): string {
					return Connection_Check::message(
						array(
							'outcome' => $outcome,
							'status'  => 502,
							'detail'  => self::ELSEWHERE,
							'older'   => false,
						)
					);
				},
				$outcomes
			)
		);

		self::assertSame( count( $outcomes ), count( array_unique( $sentences ) ) );
		self::assertStringContainsString( self::ELSEWHERE, $sentences[ Connection_Check::REDIRECTED ] );
		self::assertStringNotContainsString( '502', $sentences[ Connection_Check::UNEXPECTED ] );
	}

	/**
	 * An installation too old to name the website is said to count a reader twice until it is
	 * updated, which is what it means for the site.
	 */
	public function test_an_older_installation_is_said_to_count_readers_twice(): void {
		$sentence = Connection_Check::message(
			array(
				'outcome' => Connection_Check::CONNECTED,
				'status'  => 0,
				'detail'  => '',
				'older'   => true,
			)
		);

		self::assertStringContainsString( 'counted twice', $sentence );
	}

	/**
	 * A redirect that suggests no other address is told as one to check, not one to save.
	 */
	public function test_a_redirect_in_place_asks_for_the_address_to_be_checked(): void {
		$sentence = Connection_Check::message(
			array(
				'outcome' => Connection_Check::REDIRECTED,
				'status'  => 301,
				'detail'  => '',
				'older'   => false,
			)
		);

		self::assertStringStartsWith( 'The address answers with a redirect. Check that it', $sentence );
		self::assertStringNotContainsString( 'Save that address', $sentence );
	}

	/**
	 * The last outcome is read back, and nothing is when nothing was kept.
	 */
	public function test_the_last_outcome_is_read_back(): void {
		self::assertNull( $this->check()->last_result() );

		$this->options[ Connection_Check::OPTION ] = array(
			'outcome'     => Connection_Check::BUSY,
			'status'      => 429,
			'checked_at'  => 1763200000,
			'fingerprint' => ( new Reporting_Status( new Settings() ) )->fingerprint(),
		);

		self::assertSame(
			array(
				'outcome'    => Connection_Check::BUSY,
				'status'     => 429,
				'detail'     => '',
				'older'      => false,
				'checked_at' => 1763200000,
			),
			$this->check()->last_result()
		);
	}

	/**
	 * A check of other settings says nothing about these: once the address, the Website ID or the
	 * key changes, the connection is said not to have been checked.
	 */
	public function test_a_check_of_other_settings_is_not_read_back(): void {
		$this->options[ Connection_Check::OPTION ] = array(
			'outcome'     => Connection_Check::KEY_REFUSED,
			'status'      => 401,
			'checked_at'  => 1763200000,
			'fingerprint' => ( new Reporting_Status( new Settings() ) )->fingerprint(),
		);
		$this->options[ Settings::OPTION ]         = array( 'key_ending' => 'eeee' ) + self::as_array( $this->options[ Settings::OPTION ] );

		self::assertNull( $this->check()->last_result() );
	}

	/**
	 * A saved key that no longer opens is said to, rather than said to be missing.
	 */
	public function test_a_key_that_no_longer_opens_is_said_to(): void {
		Functions\when( 'wp_salt' )->justReturn( 'new secret keys' );
		Functions\expect( 'wp_remote_post' )->never();

		self::assertSame( Connection_Check::KEY_UNREADABLE, $this->check()->run()['outcome'] );
	}

	/**
	 * A key in wp-config.php that is not a whole key is said to be so, and one the installation
	 * refused is said to need replacing there.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_key_in_wp_config_is_said_to_be_there(): void {
		define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', 'dwk_short' );
		Functions\expect( 'wp_remote_post' )->never();

		self::assertSame( Connection_Check::KEY_MALFORMED, $this->check()->run()['outcome'] );
		self::assertStringContainsString(
			'put it in wp-config.php in its place',
			Connection_Check::message(
				array(
					'outcome' => Connection_Check::KEY_REFUSED,
					'status'  => 401,
					'detail'  => '',
					'older'   => false,
				)
			)
		);
	}

	/**
	 * Checking the connection is not a report: the time of the last report is not changed by it.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_check_is_not_counted_as_a_report(): void {
		$this->given_http_allowed();
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, self::full_answer() ) );
		Functions\when( 'wp_remote_get' )->justReturn( self::answer( 200, array() ) );

		$this->check()->run();

		self::assertSame( 0, self::dig( $this->options, Reporting_Status::OPTION, 'sent_at' ) );
	}

	/**
	 * Only somebody who manages the site's settings may check, with a nonce, and returns to the
	 * settings page.
	 */
	public function test_the_button_needs_permission_and_returns_to_the_page(): void {
		Functions\when( 'current_user_can' )->justReturn( false );
		Functions\expect( 'wp_die' )->once()->andThrow( new RuntimeException( 'refused' ) );
		$this->expectExceptionMessage( 'refused' );

		$this->check()->handle_request();
	}

	/**
	 * The button checks the tag and the connection, then goes back to the settings page.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_the_button_checks_both_and_returns(): void {
		$this->given_http_allowed();
		Functions\when( 'current_user_can' )->justReturn( true );
		Functions\when( 'admin_url' )->alias(
			static function ( string $path ): string {
				return 'https://example.com/wp-admin/' . $path;
			}
		);
		Functions\when( 'home_url' )->justReturn( 'https://example.com' );
		Functions\expect( 'check_admin_referer' )->once()->with( Connection_Check::ACTION );
		Functions\when( 'wp_remote_get' )->justReturn( self::answer( 503, array() ) );
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 401, array() ) );
		Functions\expect( 'wp_safe_redirect' )
			->once()
			->with( 'https://example.com/wp-admin/options-general.php?page=' . Settings_Page::SLUG )
			->andThrow( new RuntimeException( 'redirected' ) );
		$this->expectExceptionMessage( 'redirected' );

		try {
			$this->check()->handle_request();
		} finally {
			self::assertSame( Tag_Check::UNREACHABLE, self::dig( $this->options, Tag_Check::OPTION, 'outcome' ) );
			self::assertSame( Connection_Check::KEY_REFUSED, self::dig( $this->options, Connection_Check::OPTION, 'outcome' ) );
		}
	}

	/**
	 * Stands in WordPress's HTTP class, letting every request through.
	 */
	private function given_http_allowed(): void {
		Mockery::mock( 'overload:WP_Http' )->shouldReceive( 'block_request' )->andReturn( false );
	}

	/**
	 * The check, over the options in each test.
	 */
	private function check(): Connection_Check {
		$settings   = new Settings();
		$server_key = new Server_Key();
		$status     = new Reporting_Status( $settings );

		return new Connection_Check( $settings, $server_key, new Collector_Client( $settings, $server_key, $status ), $status, new Tag_Check( $settings ) );
	}

	/**
	 * Whether a request's arguments carry an empty batch with the key.
	 *
	 * @return \Closure(mixed): bool
	 */
	private static function empty_batch(): \Closure {
		return static function ( $arguments ): bool {
			return '{"surface":"wordpress-plugin","events":[]}' === self::dig_text( $arguments, 'body' )
				&& 'Bearer ' . self::KEY === self::dig( $arguments, 'headers', 'Authorization' );
		};
	}

	/**
	 * An answer from an installation that is in order for this site.
	 *
	 * @return array<string, mixed>
	 */
	private static function full_answer(): array {
		return array(
			'accepted'  => 0,
			'rejected'  => 0,
			'site'      => self::SITE_ID,
			'hosts'     => array( 'example.com' ),
			'measuring' => true,
			'from'      => '198.51.100.20',
		);
	}


	/**
	 * Where code sends the server's reports to another address than the browsers', that address
	 * need not serve the tag, and is not asked for it.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_an_address_given_in_code_is_not_asked_for_the_tag(): void {
		$this->given_http_allowed();
		\Brain\Monkey\Filters\expectApplied( 'dewiride_analytics_server_address' )->andReturn( 'https://engine.example.net' );
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, self::full_answer() ) );
		Functions\expect( 'wp_remote_get' )->never();

		self::assertSame( Connection_Check::CONNECTED, $this->check()->run()['outcome'] );
	}

	/**
	 * An answer as these tests describe them.
	 *
	 * @param int                   $status  The status.
	 * @param array<string, mixed>  $body    The body, before it is encoded.
	 * @param array<string, string> $headers The headers.
	 * @return array{response: array{code: int}, body: string, headers: array<string, string>}
	 */
	private static function answer( int $status, array $body, array $headers = array() ): array {
		return array(
			'response' => array( 'code' => $status ),
			'body'     => (string) json_encode( $body ), // phpcs:ignore WordPress.WP.AlternativeFunctions.json_encode_json_encode -- Also built by data providers, which run before WordPress's functions are stood in.
			'headers'  => $headers,
		);
	}
}
