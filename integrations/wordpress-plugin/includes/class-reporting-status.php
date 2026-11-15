<?php
/**
 * How the site's own server's reports have been going.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Remembers what became of the server's reports, so that every request knows whether to send one
 * and the settings page can say what happened.
 *
 * When Dewiride Analytics cannot be reached, or answers with an error, nothing is sent for 30
 * seconds, then for twice as long after each further failure, up to 15 minutes; after each wait
 * one request tries again, and the first that succeeds ends the waiting. A request to slow down
 * is waited out for as long as it asks. A key that is refused, an address that redirects and a
 * key that belongs to another website stop the reports until the settings change, because sending
 * again would only be refused again.
 *
 * It is one option, loaded with every page, and written only when something changes: a failure,
 * a recovery, a stop. The time of the last report, how long it took and any new address the
 * reports came from are kept only every 15 minutes, so even the busiest site — one sending through
 * a pool of addresses among them — writes it a few times an hour rather than once a page. What is
 * kept is counts, times, statuses and fixed reasons — nothing a report carried.
 *
 * Everything kept belongs to one address, website and key. Change any of them and the next read
 * starts afresh.
 *
 * @phpstan-type State array{fingerprint: string, stopped: string, redirect: string, failures: int, retry_at: int, probing_until: int, paused_until: int, pause: string, failure: int, failed_at: int, from: array<mixed>, names_site: bool, sent_at: int, send_ms: int}
 */
final class Reporting_Status {

	/**
	 * The option holding the state.
	 */
	public const OPTION = 'dewiride_analytics_reporting';

	/**
	 * Dewiride Analytics refused the key.
	 */
	public const KEY_REFUSED = 'key-refused';

	/**
	 * The address answered with a redirect.
	 */
	public const REDIRECTED = 'redirected';

	/**
	 * The key belongs to another website.
	 */
	public const OTHER_WEBSITE = 'other-website';

	/**
	 * The saved key does not open.
	 */
	public const KEY_UNREADABLE = 'key-unreadable';

	/**
	 * The website does not cover this site's address, so its reports are refused.
	 */
	public const UNCOVERED = 'uncovered';

	/**
	 * Dewiride Analytics asked for reports to slow down.
	 */
	public const BUSY = 'busy';

	/**
	 * Dewiride Analytics is not keeping what this website sends.
	 */
	public const NOT_MEASURING = 'not-measuring';

	/**
	 * How long the first failure is waited out, in seconds.
	 */
	private const FIRST_WAIT = 30;

	/**
	 * The longest any run of failures is waited out, in seconds.
	 */
	private const LONGEST_WAIT = 900;

	/**
	 * How long a request to slow down is waited out when it does not say, in seconds.
	 */
	private const BUSY_WAIT = 60;

	/**
	 * The longest any pause asked for lasts, in seconds.
	 */
	private const LONGEST_PAUSE = 3600;

	/**
	 * How long reports wait while Dewiride Analytics is not keeping them, in seconds, before one
	 * asks again.
	 */
	private const NOT_MEASURING_WAIT = 900;

	/**
	 * How often the time of the last report, and a new address reports come from, are kept, in
	 * seconds.
	 */
	private const KEPT_EVERY = 900;

	/**
	 * The most addresses reports are remembered arriving from, newest first.
	 */
	private const MOST_ADDRESSES = 8;

	/**
	 * How long reports wait when the website does not cover this site's address, in seconds,
	 * before one asks again.
	 */
	private const UNCOVERED_WAIT = 900;

	/**
	 * How long the one request trying again after a wait holds that turn, in seconds, which is
	 * longer than the request can take.
	 */
	private const TURN = 10;

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * The state as read at the start of this request, with what this request changed.
	 *
	 * @var State|null
	 */
	private $state;

	/**
	 * Creates the state over the given settings.
	 *
	 * @param Settings $settings The plugin's settings.
	 */
	public function __construct( Settings $settings ) {
		$this->settings = $settings;
	}

	/**
	 * The state.
	 *
	 * @return State
	 */
	public function state(): array {
		if ( null === $this->state ) {
			$this->state = $this->read();
		}

		return $this->state;
	}

	/**
	 * Whether a report may be sent now: nothing has stopped the reports, no pause is under way,
	 * and either nothing has failed or the wait after a failure is over and no other request is
	 * already trying again.
	 */
	public function may_send(): bool {
		$state = $this->state();
		$now   = time();

		if ( '' !== $state['stopped'] || $state['paused_until'] > $now ) {
			return false;
		}

		return 0 === $state['failures'] || ( $state['retry_at'] <= $now && $state['probing_until'] <= $now );
	}

	/**
	 * Takes the turn to send now, and says whether this request has it.
	 *
	 * After a failure, the request that finds the wait over takes the turn to try again, and the
	 * others wait for its answer.
	 */
	public function claim(): bool {
		if ( ! $this->may_send() ) {
			return false;
		}

		return 0 === $this->state()['failures'] || $this->take_turn();
	}

	/**
	 * Takes the one turn to try again after a wait, and says whether this request has it.
	 *
	 * Each request reads the state as it was when the request began, so every request already
	 * under way when a wait ends finds it over. The turn is therefore taken in the database: the
	 * stored state is read as it is now and changed only while it still is, so that of the
	 * requests that find the wait over together, exactly one takes the turn and the others leave
	 * the trying to it. Only a request that finds a wait over does this, and only after its page
	 * has gone.
	 */
	private function take_turn(): bool {
		$stored = Stored_Option::now( self::OPTION );

		if ( null === $stored ) {
			return false;
		}

		// Another request may have had its answer since this one began: a success leaves nothing
		// to wait for, and a failure starts a new wait.
		$this->state            = $this->from_stored( maybe_unserialize( $stored ) );
		$taken                  = $this->state;
		$taken['probing_until'] = time() + self::TURN;
		$turn                   = $this->may_send()
			&& ( 0 === $this->state['failures'] || Stored_Option::replace( self::OPTION, $stored, $taken ) );

		if ( $turn && $this->state['failures'] > 0 ) {
			$this->state = $taken;
		}

		return $turn;
	}

	/**
	 * Whether Dewiride Analytics has named the website in an answer, which it does from the
	 * version that joins a page's two halves only when they can be one person. Until it has, no
	 * page is named for it.
	 */
	public function names_site(): bool {
		return $this->state()['names_site'];
	}

	/**
	 * The addresses the server's reports have been seen arriving from, newest first.
	 *
	 * @return list<string>
	 */
	public function sends_from(): array {
		return array_values( array_filter( $this->state()['from'], 'is_string' ) );
	}

	/**
	 * Records an answer that accepted the report.
	 *
	 * @param array<mixed> $answer      The answer, decoded.
	 * @param int          $duration_ms How long sending took, in milliseconds.
	 */
	public function succeeded( array $answer, int $duration_ms ): void {
		$now     = time();
		$from    = is_string( $answer['from'] ?? null ) ? Address_Range::normalise( $answer['from'] ) : '';
		$known   = $this->sends_from();
		$due     = $now - $this->state()['sent_at'] >= self::KEPT_EVERY;
		$changes = array(
			'failures'      => 0,
			'retry_at'      => 0,
			'probing_until' => 0,
		);

		// A new address is learnt at once only when none is known yet, then at the reports' own
		// pace, so a server sending through a pool of addresses does not write on every report.
		if ( '' !== $from && ! in_array( $from, $known, true ) && ( array() === $known || $due ) ) {
			$changes['from'] = array_slice( array_merge( array( $from ), $known ), 0, self::MOST_ADDRESSES );
		}

		if ( array_key_exists( 'site', $answer ) ) {
			$changes['names_site'] = true;
		}

		if ( false === ( $answer['measuring'] ?? null ) ) {
			$changes['paused_until'] = $now + self::NOT_MEASURING_WAIT;
			$changes['pause']        = self::NOT_MEASURING;
		}

		if ( $due ) {
			$changes['sent_at'] = $now;
			$changes['send_ms'] = $duration_ms;
		}

		$this->change( $changes );
	}

	/**
	 * Records a report that failed: no answer, or an answer that was an error.
	 *
	 * @param int $status The status of the answer, or 0 when there was none.
	 */
	public function failed( int $status ): void {
		$failures = $this->state()['failures'] + 1;
		$now      = time();

		$this->change(
			array(
				'failures'      => $failures,
				'retry_at'      => $now + (int) min( self::LONGEST_WAIT, self::FIRST_WAIT * ( 2 ** min( $failures - 1, 10 ) ) ),
				'probing_until' => 0,
				'failure'       => $status,
				'failed_at'     => $now,
			)
		);
	}

	/**
	 * Records a request to slow down, and waits it out.
	 *
	 * @param int $seconds How long the answer asked for, or 0 when it did not say.
	 */
	public function slowed( int $seconds ): void {
		$wait = $seconds > 0 ? min( $seconds, self::LONGEST_PAUSE ) : self::BUSY_WAIT;

		$this->change(
			array(
				'paused_until' => time() + $wait,
				'pause'        => self::BUSY,
			)
		);
	}

	/**
	 * Records that the website does not cover this site's address, and asks again in 15 minutes:
	 * the website's addresses are changed in Dewiride Analytics, not here, so nothing here would
	 * say when to try again sooner.
	 */
	public function uncovered(): void {
		$this->change(
			array(
				'paused_until' => time() + self::UNCOVERED_WAIT,
				'pause'        => self::UNCOVERED,
			)
		);
	}

	/**
	 * Stops the reports until the settings change.
	 *
	 * @param string $reason   Why: one of the reasons above.
	 * @param string $redirect Where the address redirects, for a redirect.
	 */
	public function stop( string $reason, string $redirect = '' ): void {
		$this->change(
			array(
				'stopped'  => $reason,
				'redirect' => $redirect,
			)
		);
	}

	/**
	 * Starts afresh, keeping only what has been learnt about the installation, after a check of
	 * the connection that succeeded.
	 */
	public function resume(): void {
		$state = $this->state();
		$fresh = self::fresh( $state['fingerprint'] );

		$fresh['from']       = $state['from'];
		$fresh['names_site'] = $state['names_site'];
		$fresh['sent_at']    = $state['sent_at'];
		$fresh['send_ms']    = $state['send_ms'];

		$this->write( $fresh );
	}

	/**
	 * Applies changes, and writes them when they change anything.
	 *
	 * @param array<string, int|string|bool|array<mixed>> $changes The changes.
	 */
	private function change( array $changes ): void {
		$state   = $this->state();
		$changed = $state;

		foreach ( $changes as $name => $value ) {
			if ( array_key_exists( $name, $state ) && gettype( $value ) === gettype( $state[ $name ] ) ) {
				$changed[ $name ] = $value;
			}
		}

		if ( $changed !== $state ) {
			/**
			 * Each value replaced one of the same type.
			 *
			 * @var State $changed
			 */
			$this->write( $changed );
		}
	}

	/**
	 * Writes the state, loaded with every page from then on.
	 *
	 * @param array $state The state.
	 * @phpstan-param State $state
	 */
	private function write( array $state ): void {
		$this->state = $state;
		update_option( self::OPTION, $state, true );
	}

	/**
	 * The state as saved, or a fresh one when nothing is saved for the address, website and key
	 * the reports are sent to and with now.
	 *
	 * @return State
	 */
	private function read(): array {
		return $this->from_stored( get_option( self::OPTION ) );
	}

	/**
	 * The state a stored value holds, or a fresh one when it holds none for the address, website
	 * and key the reports are sent to and with now.
	 *
	 * @param mixed $stored What the option holds.
	 * @return State
	 */
	private function from_stored( $stored ): array {
		$fresh = self::fresh( $this->fingerprint() );

		if ( ! is_array( $stored ) || ( $stored['fingerprint'] ?? null ) !== $fresh['fingerprint'] ) {
			return $fresh;
		}

		$state = $fresh;

		foreach ( $fresh as $name => $default ) {
			$value = $stored[ $name ] ?? null;

			if ( gettype( $value ) === gettype( $default ) ) {
				$state[ $name ] = $value;
			}
		}

		/**
		 * Each value was checked against the type of the default it replaced.
		 *
		 * @var State $state
		 */
		return $state;
	}

	/**
	 * What the reports are sent to and with now, as a short digest: the address, the website and
	 * the end of the key.
	 */
	private function fingerprint(): string {
		return substr( hash( 'sha256', $this->settings->server_address() . '|' . $this->settings->site_id() . '|' . $this->settings->key_ending() ), 0, 16 );
	}

	/**
	 * The state of reports nothing has happened to yet.
	 *
	 * @param string $fingerprint What the reports are sent to and with.
	 * @return State
	 */
	private static function fresh( string $fingerprint ): array {
		return array(
			'fingerprint'   => $fingerprint,
			'stopped'       => '',
			'redirect'      => '',
			'failures'      => 0,
			'retry_at'      => 0,
			'probing_until' => 0,
			'paused_until'  => 0,
			'pause'         => '',
			'failure'       => 0,
			'failed_at'     => 0,
			'from'          => array(),
			'names_site'    => false,
			'sent_at'       => 0,
			'send_ms'       => 0,
		);
	}
}
