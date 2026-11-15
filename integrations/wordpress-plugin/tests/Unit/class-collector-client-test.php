<?php
/**
 * Tests for sending the server's reports.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Actions;
use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Collector_Client;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Server_Key;
use Dewiride\Analytics\WordPress\Settings;
use Mockery;

/**
 * A report is one request with the key, sent nowhere but the saved address, and every answer is
 * recorded as what it means.
 */
final class Collector_Client_Test extends Test_Case {

	/**
	 * A key shaped as Dewiride Analytics issues them; plainly not a real one.
	 */
	private const KEY = 'dwk_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';

	/**
	 * What a fetch that failed outright says went wrong, in these tests.
	 */
	private const FAILED = 'cURL error 7: Failed to connect to analytics.example.com port 443';

	/**
	 * What the reporting state option holds in each test.
	 *
	 * @var mixed
	 */
	private $state = false;

	/**
	 * The sealed key, saved once for every test.
	 *
	 * @var string
	 */
	private $sealed = '';

	/**
	 * Starts each test with a key saved and answers described by plain arrays.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\when( 'update_option' )->alias(
			function ( string $option, $value ): bool {
				if ( Server_Key::OPTION === $option ) {
					$this->sealed = is_string( $value ) ? $value : '';
				} else {
					$this->state = $value;
				}

				return true;
			}
		);
		Functions\when( 'get_option' )->alias(
			function ( string $option, $fallback = false ) {
				$options = array(
					Settings::OPTION         => array(
						'address'    => self::ADDRESS,
						'site_id'    => self::SITE_ID,
						'key_ending' => 'bbbb',
					),
					Server_Key::OPTION       => '' === $this->sealed ? false : $this->sealed,
					Reporting_Status::OPTION => $this->state,
				);

				return $options[ $option ] ?? $fallback;
			}
		);
		Functions\when( 'is_wp_error' )->alias(
			static function ( $thing ): bool {
				return $thing instanceof Fake_Wp_Error;
			}
		);
		$this->given_responses_as_arrays();
		( new Server_Key() )->save( self::KEY );
	}

	/**
	 * A report goes to the collector for server reports, with the key, as JSON, without following
	 * a redirect, within two seconds.
	 */
	public function test_a_report_is_one_request_with_the_key(): void {
		Functions\expect( 'wp_remote_post' )
			->once()
			->with(
				self::ADDRESS . '/collect/server',
				Mockery::on(
					static function ( $arguments ): bool {
						$body = json_decode( self::dig_text( $arguments, 'body' ), true );

						return 2 === self::dig( $arguments, 'timeout' )
							&& 0 === self::dig( $arguments, 'redirection' )
							&& true === self::dig( $arguments, 'sslverify' )
							&& false === self::dig( $arguments, 'reject_unsafe_urls' )
							&& 'Bearer ' . self::KEY === self::dig( $arguments, 'headers', 'Authorization' )
							&& 'application/json' === self::dig( $arguments, 'headers', 'Content-Type' )
							&& 'wordpress-plugin' === self::dig( $body, 'surface' )
							&& array(
								array(
									'kind' => 'pageview',
									'url'  => 'https://example.com/a/b',
								),
							) === self::dig( $body, 'events' )
							&& false !== strpos( self::dig_text( $arguments, 'body' ), '"url":"https://example.com/a/b"' );
					}
				)
			)
			->andReturn( self::answer( 200, array( 'accepted' => 1 ) ) );

		$this->client()->report(
			array(
				'kind' => 'pageview',
				'url'  => 'https://example.com/a/b',
			)
		);
	}

	/**
	 * On a network, WordPress refuses an address it would not fetch for anybody.
	 */
	public function test_on_a_network_unsafe_addresses_are_refused(): void {
		Functions\when( 'is_multisite' )->justReturn( true );
		Functions\expect( 'wp_remote_post' )
			->once()
			->with( Mockery::any(), Mockery::subset( array( 'reject_unsafe_urls' => true ) ) )
			->andReturn( self::answer( 200, array( 'accepted' => 1 ) ) );

		$this->client()->report( array( 'kind' => 'pageview' ) );
	}

	/**
	 * Without a key that opens, nothing is sent.
	 */
	public function test_without_a_key_nothing_is_sent(): void {
		$this->sealed = 'not a seal';
		Functions\expect( 'wp_remote_post' )->never();

		$this->client()->report( array( 'kind' => 'pageview' ) );
	}

	/**
	 * What became of a report, and what each answer is read as.
	 */
	public function test_each_answer_is_read_for_what_it_says(): void {
		Functions\when( 'wp_remote_post' )->justReturn( new Fake_Wp_Error( self::FAILED ) );
		self::assertSame( 0, $this->client()->send( array(), 3 )['status'] );

		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, array( 'accepted' => 0 ) ) );
		self::assertSame( array( 'accepted' => 0 ), $this->client()->send( array(), 3 )['answer'] );

		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, array( 'something' => 'else' ) ) );
		self::assertNull( $this->client()->send( array(), 3 )['answer'] );

		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 302, array(), array( 'location' => 'https://Analytics.Example.org:8443/collect/server?x=1' ) ) );
		self::assertSame( 'https://analytics.example.org:8443', $this->client()->send( array(), 3 )['location'] );

		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 429, array(), array( 'retry-after' => '17' ) ) );
		self::assertSame( 17, $this->client()->send( array(), 3 )['retry_after'] );
	}

	/**
	 * Where redirects lead, written as answers write them, and the address each suggests saving
	 * instead of the one asked, https://analytics.example.com.
	 *
	 * @return array<string, array{string, string}>
	 */
	public function redirects(): array {
		return array(
			'another address'                => array( 'https://Analytics.Example.org:8443/collect/server?x=1', 'https://analytics.example.org:8443' ),
			'another host, the same scheme'  => array( '//analytics.example.org/collect/server', 'https://analytics.example.org' ),
			'another path on the same host'  => array( '/collect/server/', '' ),
			'a path relative to the address' => array( 'server/', '' ),
			'the same address, written out'  => array( 'HTTPS://analytics.example.com/collect/server/', '' ),
			'the same address, its own port' => array( 'https://analytics.example.com:443/collect/server/', '' ),
			'the same host on another port'  => array( 'https://analytics.example.com:8443/collect/server', 'https://analytics.example.com:8443' ),
			'nowhere'                        => array( '', '' ),
		);
	}

	/**
	 * A failure to accept the address's certificate is told apart from no answer at all, since it
	 * is put right somewhere else.
	 */
	public function test_a_certificate_not_accepted_is_told_apart(): void {
		Functions\when( 'wp_remote_post' )->justReturn( new Fake_Wp_Error( 'cURL error 60: SSL certificate problem: unable to get local issuer certificate' ) );
		self::assertTrue( $this->client()->send( array(), 3 )['certificate'] );

		Functions\when( 'wp_remote_post' )->justReturn( new Fake_Wp_Error( self::FAILED ) );
		self::assertFalse( $this->client()->send( array(), 3 )['certificate'] );
	}

	/**
	 * Connecting is given the same bound as the whole report, which WordPress leaves at ten seconds
	 * where PHP has no cURL; the bound is taken off again once the report has gone.
	 */
	public function test_connecting_is_bounded_as_the_whole_is(): void {
		$bound = null;

		Actions\expectAdded( 'requests-requests.before_request' )
			->once()
			->whenHappen(
				static function ( $callback ) use ( &$bound ): void {
					$bound = $callback;
				}
			);
		Actions\expectRemoved( 'requests-requests.before_request' )->once();
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 200, array( 'accepted' => 0 ) ) );

		$this->client()->send( array(), 3 );

		$url     = self::ADDRESS;
		$headers = array();
		$data    = '';
		$type    = 'POST';
		$options = array( 'connect_timeout' => 10 );

		self::assertIsCallable( $bound );
		$bound( $url, $headers, $data, $type, $options );
		self::assertSame( 3, $options['connect_timeout'] );
	}

	/**
	 * A redirect is read for the address it suggests saving: another one, or none when it leads
	 * somewhere on the address that was asked.
	 *
	 * @dataProvider redirects
	 *
	 * @param string $location  Where the answer said to go.
	 * @param string $suggested The address it suggests saving, or an empty string.
	 */
	public function test_a_redirect_is_read_for_the_address_it_suggests( string $location, string $suggested ): void {
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( 301, array(), array( 'location' => $location ) ) );

		self::assertSame( $suggested, $this->client()->send( array(), 3 )['location'] );
	}

	/**
	 * An answer that accepted the report is recorded as a success.
	 */
	public function test_an_accepted_report_is_recorded(): void {
		$this->record( 200, array( 'accepted' => 1 ) );

		self::assertSame( 0, self::dig( $this->state, 'failures' ) );
		self::assertSame( '', self::dig( $this->state, 'stopped' ) );
		self::assertGreaterThan( 0, self::dig( $this->state, 'sent_at' ) );
	}

	/**
	 * A closed account answers that nothing is being kept, and covers no address: the reports
	 * wait for the account, not for the address.
	 */
	public function test_a_closed_account_pauses_the_reports_as_not_measured(): void {
		$this->record(
			200,
			array(
				'accepted'  => 0,
				'rejected'  => 1,
				'hosts'     => array(),
				'measuring' => false,
			)
		);

		self::assertSame( Reporting_Status::NOT_MEASURING, self::dig( $this->state, 'pause' ) );
	}

	/**
	 * Answers that stop the reports until the settings change.
	 *
	 * @return array<string, array{int, array<string, mixed>, string}>
	 */
	public function stopping_answers(): array {
		return array(
			'a refused key'            => array( 401, array(), Reporting_Status::KEY_REFUSED ),
			'a redirect'               => array( 301, array(), Reporting_Status::REDIRECTED ),
			'a key of another website' => array(
				200,
				array(
					'accepted' => 1,
					'site'     => '0197c0de-0000-7000-8000-000000000001',
				),
				Reporting_Status::OTHER_WEBSITE,
			),
		);
	}

	/**
	 * A refused key, a redirect and a key belonging to another website stop the reports.
	 *
	 * @dataProvider stopping_answers
	 *
	 * @param int                  $status The answer's status.
	 * @param array<string, mixed> $body   The answer's body.
	 * @param string               $reason Why the reports stop.
	 */
	public function test_some_answers_stop_the_reports( int $status, array $body, string $reason ): void {
		$this->record( $status, $body );

		self::assertSame( $reason, self::dig( $this->state, 'stopped' ) );
	}

	/**
	 * A redirect stops the reports with the address it suggests saving instead.
	 */
	public function test_a_redirect_is_kept_with_where_it_leads(): void {
		$this->record( 301, array(), array( 'location' => 'https://analytics.example.org/collect/server' ) );

		self::assertSame( Reporting_Status::REDIRECTED, self::dig( $this->state, 'stopped' ) );
		self::assertSame( 'https://analytics.example.org', self::dig( $this->state, 'redirect' ) );
	}

	/**
	 * The website's own identifier, however it is written, is this website.
	 */
	public function test_the_websites_own_identifier_in_capitals_is_this_website(): void {
		$this->record(
			200,
			array(
				'accepted' => 1,
				'site'     => strtoupper( self::SITE_ID ),
			)
		);

		self::assertSame( '', self::dig( $this->state, 'stopped' ) );
		self::assertTrue( self::dig( $this->state, 'names_site' ) );
	}

	/**
	 * A request to slow down pauses the reports for as long as it asks.
	 */
	public function test_a_request_to_slow_down_pauses_the_reports(): void {
		$this->record( 429, array(), array( 'retry-after' => '30' ) );

		self::assertSame( Reporting_Status::BUSY, self::dig( $this->state, 'pause' ) );
		self::assertEqualsWithDelta( time() + 30, self::dig( $this->state, 'paused_until' ), 1 );
	}

	/**
	 * Anything else is a failure, waited out.
	 *
	 * @return array<string, array{mixed}>
	 */
	public function failures(): array {
		return array(
			'no answer'                  => array( new Fake_Wp_Error( self::FAILED ) ),
			'an error'                   => array( self::answer( 503, array() ) ),
			'a refused batch'            => array( self::answer( 400, array() ) ),
			'an answer that is no batch' => array( self::answer( 200, array( 'hello' => 'world' ) ) ),
		);
	}

	/**
	 * Every other answer opens the wait.
	 *
	 * @dataProvider failures
	 *
	 * @param mixed $response The response.
	 */
	public function test_every_other_answer_is_a_failure( $response ): void {
		Functions\when( 'wp_remote_post' )->justReturn( $response );

		$this->client()->report( array( 'kind' => 'pageview' ) );

		self::assertSame( 1, self::dig( $this->state, 'failures' ) );
	}

	/**
	 * Sends a report that is answered as given, and records what came of it.
	 *
	 * @param int                   $status  The answer's status.
	 * @param array<string, mixed>  $body    The answer's body.
	 * @param array<string, string> $headers The answer's headers.
	 */
	private function record( int $status, array $body, array $headers = array() ): void {
		Functions\when( 'wp_remote_post' )->justReturn( self::answer( $status, $body, $headers ) );

		$this->client()->report( array( 'kind' => 'pageview' ) );
	}

	/**
	 * The client, over the settings and key in each test.
	 */
	private function client(): Collector_Client {
		$settings = new Settings();

		return new Collector_Client( $settings, new Server_Key(), new Reporting_Status( $settings ) );
	}


	/**
	 * An answer whose website does not cover this site's address had the report refused, and the
	 * reports wait to ask again rather than counting it a success.
	 */
	public function test_a_website_at_another_address_pauses_the_reports(): void {
		$this->record(
			200,
			array(
				'accepted' => 0,
				'rejected' => 1,
				'hosts'    => array( 'example.org' ),
			)
		);

		self::assertSame( Reporting_Status::UNCOVERED, self::dig( $this->state, 'pause' ) );
		self::assertSame( 0, self::dig( $this->state, 'sent_at' ) );
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
