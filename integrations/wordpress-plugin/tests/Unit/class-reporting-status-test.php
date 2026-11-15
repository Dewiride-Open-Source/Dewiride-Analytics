<?php
/**
 * Tests for remembering how the server's reports have gone.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Settings;

/**
 * Failures are waited out for longer each time, a request to slow down is waited out as asked,
 * stops last until the settings change, and nothing is written unless something changed.
 */
final class Reporting_Status_Test extends Test_Case {

	/**
	 * The address the site's server sends its reports from.
	 */
	private const SERVER = '198.51.100.20';

	/**
	 * Another address the site's server sends from, when it has a pool of them.
	 */
	private const OTHER_SERVER = '198.51.100.21';

	/**
	 * What the option holds in each test.
	 *
	 * @var mixed
	 */
	private $saved = false;

	/**
	 * How many times the option was written.
	 *
	 * @var int
	 */
	private $writes = 0;

	/**
	 * The end of the server key in the settings.
	 *
	 * @var string
	 */
	private $key_ending = 'abcd';

	/**
	 * The database, holding the option the test keeps.
	 *
	 * @var Fake_Database
	 */
	private $database;

	/**
	 * Starts each test with nothing kept, a key saved, and every write kept.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\when( 'get_option' )->alias(
			function ( string $option ) {
				if ( Settings::OPTION === $option ) {
					return array(
						'address'    => self::ADDRESS,
						'site_id'    => self::SITE_ID,
						'key_ending' => $this->key_ending,
					);
				}

				return Reporting_Status::OPTION === $option ? $this->saved : false;
			}
		);
		Functions\when( 'update_option' )->alias(
			function ( string $option, $value, $autoload ): bool {
				self::assertSame( Reporting_Status::OPTION, $option );
				self::assertTrue( $autoload );
				$this->saved = $value;
				++$this->writes;

				return true;
			}
		);
		$this->database = new Fake_Database(
			function ( string $option ) {
				return Reporting_Status::OPTION === $option ? $this->saved : false;
			},
			function ( string $option, $value ): void {
				self::assertSame( Reporting_Status::OPTION, $option );
				$this->saved = $value;
				++$this->writes;
			}
		);
		self::given_database( $this->database );
	}

	/**
	 * Reports nothing has happened to may be sent, and nothing is written to find that out.
	 */
	public function test_reports_start_free_to_send(): void {
		$status = $this->status();

		self::assertTrue( $status->may_send() );
		self::assertTrue( $status->claim() );
		self::assertFalse( $status->names_site() );
		self::assertSame( array(), $status->sends_from() );
		self::assertSame( 0, $this->writes );
	}

	/**
	 * Each failure in a row is waited out twice as long as the one before, up to 15 minutes.
	 */
	public function test_failures_are_waited_out_for_longer_each_time(): void {
		$waits = array();

		for ( $failure = 1; $failure <= 7; $failure++ ) {
			$status = $this->status();
			$before = time();
			$status->failed( 503 );
			$waits[] = $status->state()['retry_at'] - $before;
		}

		self::assertSame( array( 30, 60, 120, 240, 480, 900, 900 ), $waits );
		self::assertSame( 503, $this->status()->state()['failure'] );
		self::assertFalse( $this->status()->may_send() );
	}

	/**
	 * Once a wait is over, one request takes the turn to try again and the others wait for it.
	 */
	public function test_after_a_wait_one_request_tries_again(): void {
		$this->given_state(
			array(
				'failures' => 2,
				'retry_at' => time() - 1,
			)
		);

		self::assertTrue( $this->status()->claim() );
		self::assertFalse( $this->status()->may_send() );
		self::assertGreaterThan( time(), self::dig( $this->saved, 'probing_until' ) );
	}

	/**
	 * Of the requests already under way when a wait ends, each of which began with the wait not
	 * yet over, only the first to claim the turn has it.
	 */
	public function test_of_requests_finding_a_wait_over_together_one_tries_again(): void {
		$this->given_state(
			array(
				'failures' => 2,
				'retry_at' => time() - 1,
			)
		);
		$first  = $this->status();
		$second = $this->status();

		self::assertTrue( $first->may_send() );
		self::assertTrue( $second->may_send() );

		self::assertTrue( $first->claim() );
		self::assertFalse( $second->claim() );
	}

	/**
	 * A request that read the state just before another took the turn finds it taken, and
	 * leaves the trying to that one.
	 */
	public function test_a_request_beaten_to_the_turn_leaves_it(): void {
		$this->given_state(
			array(
				'failures' => 2,
				'retry_at' => time() - 1,
			)
		);
		$this->database->before_update = function (): void {
			$this->saved = array( 'probing_until' => time() + 10 ) + self::as_array( $this->saved );
		};

		self::assertFalse( $this->status()->claim() );
	}

	/**
	 * A report another request had accepted since this one began leaves nothing to wait for: this
	 * request sends, and takes no turn.
	 */
	public function test_a_wait_another_request_ended_lets_this_one_send(): void {
		$this->given_state(
			array(
				'failures' => 2,
				'retry_at' => time() - 1,
			)
		);
		$status = $this->status();
		$status->may_send();
		$this->saved  = array( 'failures' => 0 ) + self::as_array( $this->saved );
		$this->writes = 0;

		self::assertTrue( $status->claim() );
		self::assertSame( 0, $this->writes );
	}

	/**
	 * A failure another request had since this one began starts a new wait, which this request
	 * keeps to.
	 */
	public function test_a_failure_meanwhile_starts_a_new_wait(): void {
		$this->given_state(
			array(
				'failures' => 2,
				'retry_at' => time() - 1,
			)
		);
		$status = $this->status();
		$status->may_send();
		$this->saved = array(
			'failures' => 3,
			'retry_at' => time() + 120,
		) + self::as_array( $this->saved );

		self::assertFalse( $status->claim() );
		self::assertFalse( $status->may_send() );
	}

	/**
	 * The first report accepted after failures ends the waiting.
	 */
	public function test_an_accepted_report_ends_the_waiting(): void {
		$this->given_state(
			array(
				'failures'      => 3,
				'retry_at'      => time() - 1,
				'probing_until' => time() + 5,
			)
		);

		$this->status()->succeeded( array( 'accepted' => 1 ), 120 );

		self::assertSame( 0, self::dig( $this->saved, 'failures' ) );
		self::assertTrue( $this->status()->may_send() );
	}

	/**
	 * What an answer says about the installation is learnt: where reports arrive from, and that
	 * it names the website, which is when pages start being named.
	 */
	public function test_an_answer_teaches_where_reports_arrive_from(): void {
		$this->status()->succeeded(
			array(
				'accepted' => 1,
				'site'     => self::SITE_ID,
				'from'     => '::ffff:' . self::SERVER,
			),
			80
		);

		$status = $this->status();

		self::assertSame( array( self::SERVER ), $status->sends_from() );
		self::assertTrue( $status->names_site() );
	}

	/**
	 * An installation that stops naming the website — rolled back to a version that joins names
	 * unsafely — stops the pages being named, from its next answer.
	 */
	public function test_an_answer_without_the_website_stops_the_naming(): void {
		$this->status()->succeeded(
			array(
				'accepted' => 1,
				'site'     => self::SITE_ID,
			),
			80
		);
		self::assertTrue( $this->status()->names_site() );

		$this->status()->succeeded( array( 'accepted' => 1 ), 80 );

		self::assertFalse( $this->status()->names_site() );
	}

	/**
	 * The empty batch that checks the connection is not a report: it keeps no time of a report
	 * sent, though it teaches where reports arrive from.
	 */
	public function test_a_check_of_the_connection_is_not_a_report_sent(): void {
		$this->status()->succeeded(
			array(
				'accepted' => 0,
				'from'     => self::SERVER,
			),
			120,
			false
		);

		self::assertSame( 0, self::dig( $this->saved, 'sent_at' ) );
		self::assertSame( array( self::SERVER ), $this->status()->sends_from() );
	}

	/**
	 * Reports that change nothing write nothing, and the time of the last report is kept only
	 * every 15 minutes.
	 */
	public function test_reports_that_change_nothing_write_nothing(): void {
		$this->status()->succeeded( array( 'accepted' => 1 ), 90 );
		self::assertSame( 1, $this->writes );
		self::assertSame( 90, self::dig( $this->saved, 'send_ms' ) );

		$this->status()->succeeded( array( 'accepted' => 1 ), 95 );
		$this->status()->succeeded( array( 'accepted' => 1 ), 99 );

		self::assertSame( 1, $this->writes );
		self::assertSame( 90, self::dig( $this->saved, 'send_ms' ) );

		$this->given_state( array( 'sent_at' => time() - 901 ) );
		$this->status()->succeeded( array( 'accepted' => 1 ), 70 );

		self::assertSame( 70, self::dig( $this->saved, 'send_ms' ) );
	}

	/**
	 * A website that is not being measured is asked about again in 15 minutes, not on every page.
	 */
	public function test_a_website_not_measured_is_asked_about_again_later(): void {
		$this->status()->succeeded(
			array(
				'accepted'  => 0,
				'measuring' => false,
			),
			60
		);

		$state = $this->status()->state();

		self::assertSame( Reporting_Status::NOT_MEASURING, $state['pause'] );
		self::assertGreaterThanOrEqual( time() + 899, $state['paused_until'] );
		self::assertFalse( $this->status()->may_send() );
	}

	/**
	 * A request to slow down is waited out for as long as it asks, a minute when it does not say,
	 * and an hour at most.
	 */
	public function test_a_request_to_slow_down_is_waited_out(): void {
		$cases = array(
			array( 42, 42 ),
			array( 0, 60 ),
			array( 86400, 3600 ),
		);

		foreach ( $cases as list( $asked, $waited ) ) {
			$this->saved = false;
			$status      = $this->status();
			$before      = time();
			$status->slowed( $asked );
			$state = $status->state();

			self::assertSame( Reporting_Status::BUSY, $state['pause'] );
			self::assertEqualsWithDelta( $waited, $state['paused_until'] - $before, 1 );
		}
	}

	/**
	 * A stop lasts until the address, the website or the key changes.
	 */
	public function test_a_stop_lasts_until_the_settings_change(): void {
		$this->status()->stop( Reporting_Status::REDIRECTED, 'https://analytics.example.org' );

		self::assertFalse( $this->status()->may_send() );
		self::assertSame( 'https://analytics.example.org', $this->status()->state()['redirect'] );

		$this->key_ending = 'wxyz';

		self::assertTrue( $this->status()->may_send() );
		self::assertSame( '', $this->status()->state()['stopped'] );
	}

	/**
	 * A check that found the connection in order starts afresh, keeping what was learnt.
	 */
	public function test_resuming_keeps_what_was_learnt(): void {
		$this->given_state(
			array(
				'stopped'    => Reporting_Status::KEY_REFUSED,
				'failures'   => 4,
				'from'       => array( self::SERVER ),
				'names_site' => true,
			)
		);

		$this->status()->resume();

		$status = $this->status();

		self::assertTrue( $status->may_send() );
		self::assertSame( array( self::SERVER ), $status->sends_from() );
		self::assertTrue( $status->names_site() );
	}

	/**
	 * A value of the wrong type in what is kept reads as nothing having happened.
	 */
	public function test_a_damaged_value_reads_as_its_default(): void {
		$this->given_state( array( 'failures' => 'many' ) );

		self::assertSame( 0, $this->status()->state()['failures'] );
	}

	/**
	 * A server sending through several addresses has them learnt one at a time, at the reports'
	 * own pace, so reports from a pool of addresses do not each write the state.
	 */
	public function test_a_pool_of_addresses_is_learnt_at_the_reports_own_pace(): void {
		$this->status()->succeeded( array( 'from' => self::SERVER ), 90 );
		$writes = $this->writes;

		$this->status()->succeeded( array( 'from' => self::OTHER_SERVER ), 90 );
		$this->status()->succeeded( array( 'from' => '198.51.100.22' ), 90 );

		self::assertSame( $writes, $this->writes );
		self::assertSame( array( self::SERVER ), $this->status()->sends_from() );

		$this->saved = array( 'sent_at' => time() - 901 ) + self::as_array( $this->saved );
		$this->status()->succeeded( array( 'from' => self::OTHER_SERVER ), 90 );

		self::assertSame( array( self::OTHER_SERVER, self::SERVER ), $this->status()->sends_from() );
	}

	/**
	 * Makes the kept state read as given, for the address, website and key in the settings.
	 *
	 * @param array<string, mixed> $values The values that differ from nothing having happened.
	 */
	private function given_state( array $values ): void {
		$this->saved = false;
		$fresh       = $this->status()->state();
		$this->saved = $values + $fresh;
	}

	/**
	 * The state, read afresh as a new request reads it.
	 */
	private function status(): Reporting_Status {
		return new Reporting_Status( new Settings() );
	}
}
