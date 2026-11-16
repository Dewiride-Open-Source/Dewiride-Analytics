<?php
/**
 * Checks that the server's reports would be accepted.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

use WP_Http;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Sends Dewiride Analytics an empty report, which stores nothing, and says in plain words what
 * its answer means for this site.
 *
 * The answer says whether the key works, which website it belongs to, which addresses that
 * website covers, and whether the website is being measured, so each can be checked against this
 * site before anything is sent. An installation older than the answer's last fields leaves them
 * out, and what cannot be checked is said rather than guessed. Only the outcome is kept, with
 * the status and the address a redirect leads to where they explain it — never anything else an
 * answer held.
 *
 * @phpstan-type Outcome array{outcome: string, status: int, detail: string, older: bool}
 * @phpstan-type Checked array{outcome: string, status: int, detail: string, older: bool, checked_at: int}
 */
final class Connection_Check {

	/**
	 * The option holding the last outcome.
	 */
	public const OPTION = 'dewiride_analytics_connection';

	/**
	 * The admin-post action behind the Check connection button.
	 */
	public const ACTION = 'dewiride_analytics_check_connection';

	/**
	 * Reports would be accepted.
	 */
	public const CONNECTED = 'connected';

	/**
	 * There is no address, Website ID or key to check.
	 */
	public const NOT_SET_UP = 'not-set-up';

	/**
	 * The address is reached over http, which would send the key unencrypted.
	 */
	public const INSECURE = 'insecure';

	/**
	 * WordPress is set to refuse requests to the address.
	 */
	public const BLOCKED = 'blocked';

	/**
	 * Nothing answered.
	 */
	public const UNREACHABLE = 'unreachable';

	/**
	 * The address redirects elsewhere.
	 */
	public const REDIRECTED = 'redirected';

	/**
	 * The key was refused.
	 */
	public const KEY_REFUSED = 'key-refused';

	/**
	 * The installation asked for a pause.
	 */
	public const BUSY = 'busy';

	/**
	 * The installation answered with an error.
	 */
	public const UNEXPECTED = 'unexpected';

	/**
	 * The key belongs to another website.
	 */
	public const OTHER_WEBSITE = 'other-website';

	/**
	 * The website the key belongs to does not cover this site's address.
	 */
	public const UNCOVERED = 'uncovered';

	/**
	 * The website is not being measured.
	 */
	public const NOT_MEASURING = 'not-measuring';

	/**
	 * The address does not serve the tag's script.
	 */
	public const NO_SCRIPT = 'no-script';

	/**
	 * The saved key no longer opens: the site's secret keys changed since it was saved.
	 */
	public const KEY_UNREADABLE = 'key-unreadable';

	/**
	 * The key `wp-config.php` defines is not a whole key.
	 */
	public const KEY_MALFORMED = 'key-malformed';

	/**
	 * Something answered that is not Dewiride Analytics.
	 */
	public const NOT_DEWIRIDE = 'not-dewiride';

	/**
	 * Something at the address turned the site's request away before Dewiride Analytics saw it.
	 */
	public const TURNED_AWAY = 'turned-away';

	/**
	 * The site's server does not accept the address's certificate.
	 */
	public const CERTIFICATE = 'certificate';

	/**
	 * How long the installation is given to answer, in seconds.
	 */
	private const TIMEOUT = 10;

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * The server key.
	 *
	 * @var Server_Key
	 */
	private $server_key;

	/**
	 * Where reports are sent.
	 *
	 * @var Collector_Client
	 */
	private $client;

	/**
	 * How reports have been going.
	 *
	 * @var Reporting_Status
	 */
	private $status;

	/**
	 * The check of the tag visitors receive, run with this one.
	 *
	 * @var Tag_Check
	 */
	private $tag_check;

	/**
	 * Creates the check.
	 *
	 * @param Settings         $settings   The plugin's settings.
	 * @param Server_Key       $server_key The server key.
	 * @param Collector_Client $client     Where reports are sent.
	 * @param Reporting_Status $status     How reports have been going.
	 * @param Tag_Check        $tag_check  The check of the tag visitors receive.
	 */
	public function __construct( Settings $settings, Server_Key $server_key, Collector_Client $client, Reporting_Status $status, Tag_Check $tag_check ) {
		$this->settings   = $settings;
		$this->server_key = $server_key;
		$this->client     = $client;
		$this->status     = $status;
		$this->tag_check  = $tag_check;
	}

	/**
	 * Hooks the Check connection button.
	 */
	public function register_admin(): void {
		add_action( 'admin_post_' . self::ACTION, array( $this, 'handle_request' ) );
	}

	/**
	 * Checks the tag and the connection for the Check connection button, then returns to the
	 * settings page.
	 */
	public function handle_request(): void {
		if ( ! current_user_can( 'manage_options' ) ) {
			wp_die( esc_html__( 'Sorry, you aren\'t allowed to check the connection.', 'dewiride-analytics' ), '', array( 'response' => 403 ) );
		}

		check_admin_referer( self::ACTION );
		$this->tag_check->run();
		$this->run();
		wp_safe_redirect( admin_url( 'options-general.php?page=' . Settings_Page::SLUG ) );
		exit;
	}

	/**
	 * Checks the connection and keeps the outcome. A check that finds everything in order also
	 * ends any wait or stop, so reports start again at once.
	 *
	 * @return Outcome
	 */
	public function run(): array {
		$result = $this->check();

		update_option(
			self::OPTION,
			$result + array(
				'checked_at'  => time(),
				'fingerprint' => $this->status->fingerprint(),
			),
			false
		);

		if ( self::CONNECTED === $result['outcome'] ) {
			$this->status->resume();
		}

		return $result;
	}

	/**
	 * The last outcome, or nothing when the connection has not been checked with the address,
	 * Website ID and key saved now: a check of other settings says nothing about these.
	 *
	 * @return Checked|null
	 */
	public function last_result(): ?array {
		$stored = get_option( self::OPTION );

		if ( ! is_array( $stored ) || ! is_string( $stored['outcome'] ?? null ) || ! is_int( $stored['checked_at'] ?? null ) || ( $stored['fingerprint'] ?? null ) !== $this->status->fingerprint() ) {
			return null;
		}

		return array(
			'outcome'    => $stored['outcome'],
			'status'     => is_int( $stored['status'] ?? null ) ? $stored['status'] : 0,
			'detail'     => is_string( $stored['detail'] ?? null ) ? $stored['detail'] : '',
			'older'      => true === ( $stored['older'] ?? null ),
			'checked_at' => $stored['checked_at'],
		);
	}

	/**
	 * The sentence the settings page shows for an outcome.
	 *
	 * @param array $result The outcome of a check.
	 * @phpstan-param Outcome $result
	 */
	public static function message( array $result ): string {
		switch ( $result['outcome'] ) {
			case self::CONNECTED:
				$message = $result['older']
					? __( 'Connected. Your installation of Dewiride Analytics is an older version, so a reader may be counted twice, once by their browser and once by your server. Update the installation to count each reader once.', 'dewiride-analytics' )
					: __( 'Connected. Dewiride Analytics accepts this site\'s reports.', 'dewiride-analytics' );
				break;
			case self::NOT_SET_UP:
				$message = __( 'Add the address, the Website ID and a server key first.', 'dewiride-analytics' );
				break;
			case self::KEY_UNREADABLE:
				$message = __( 'The saved server key can\'t be read any more, because your site\'s security keys have changed. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and paste it under Server key below.', 'dewiride-analytics' );
				break;
			case self::KEY_MALFORMED:
				$message = __( 'The server key in wp-config.php isn\'t complete. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and put it in wp-config.php in its place.', 'dewiride-analytics' );
				break;
			case self::INSECURE:
				$message = __( 'Reports carry your server key, so the address must start with https://.', 'dewiride-analytics' );
				break;
			case self::BLOCKED:
				$message = __( 'WordPress on this site is set to block requests to other sites. Add the address\'s host name to WP_ACCESSIBLE_HOSTS in wp-config.php, and api.cloudflare.com too if your site is behind Cloudflare.', 'dewiride-analytics' );
				break;
			case self::REDIRECTED:
				$message = '' === $result['detail']
					? __( 'The address answers with a redirect. Check that it\'s exactly the address you open Dewiride Analytics on.', 'dewiride-analytics' )
					/* translators: %s: The address the saved address redirects to. */
					: sprintf( __( 'The address redirects to %s. Save that address instead.', 'dewiride-analytics' ), $result['detail'] );
				break;
			case self::KEY_REFUSED:
				$message = Server_Key::is_in_config()
					? __( 'Dewiride Analytics didn\'t accept the server key in wp-config.php. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and put it in wp-config.php in its place.', 'dewiride-analytics' )
					: __( 'Dewiride Analytics didn\'t accept the server key. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and paste it under Server key below.', 'dewiride-analytics' );
				break;
			case self::BUSY:
				$message = __( 'Dewiride Analytics is busy. Check again in a minute.', 'dewiride-analytics' );
				break;
			case self::OTHER_WEBSITE:
				$message = __( 'The server key and the Website ID come from different websites in Dewiride Analytics. Copy both from the same website, under Tracking code, then WordPress.', 'dewiride-analytics' );
				break;
			case self::UNCOVERED:
				/* translators: 1: The site's host name. 2: The host names the website covers in Dewiride Analytics. */
				$message = sprintf( __( 'This site is at %1$s, but the website in Dewiride Analytics covers only %2$s, so its reports would be refused. Check the website\'s address in Dewiride Analytics.', 'dewiride-analytics' ), self::home_host(), $result['detail'] );
				break;
			case self::NOT_MEASURING:
				$message = __( 'Dewiride Analytics isn\'t counting visits for this website at the moment. Check your account in Dewiride Analytics.', 'dewiride-analytics' );
				break;
			case self::NO_SCRIPT:
				$message = __( 'This address doesn\'t serve the Dewiride Analytics tag, so browsers can\'t load it. Use the address you open Dewiride Analytics on.', 'dewiride-analytics' );
				break;
			case self::NOT_DEWIRIDE:
				$message = __( 'This address doesn\'t answer as Dewiride Analytics. Check that it\'s exactly the address you open Dewiride Analytics on.', 'dewiride-analytics' );
				break;
			case self::TURNED_AWAY:
				$message = __( 'Something at this address turned your site away before Dewiride Analytics could answer, often a firewall. Ask whoever runs the address to let your site through.', 'dewiride-analytics' );
				break;
			case self::CERTIFICATE:
				$message = __( 'Your site doesn\'t accept this address\'s security certificate, so it can\'t send reports there. Ask whoever runs the address to check that its certificate is complete.', 'dewiride-analytics' );
				break;
			case self::UNEXPECTED:
				$message = __( 'Dewiride Analytics answered with an error. Check again in a few minutes.', 'dewiride-analytics' );
				break;
			default:
				$message = __( 'Your site couldn\'t reach Dewiride Analytics at this address. Check the address, and that your host lets your site connect to other sites.', 'dewiride-analytics' );
		}

		return $message;
	}

	/**
	 * Whether a host is one the website covers: one of its hosts, or a name below one.
	 *
	 * @param string $host  The host.
	 * @param array  $hosts The hosts the website covers.
	 * @phpstan-param list<string> $hosts
	 */
	public static function covers( string $host, array $hosts ): bool {
		$host = strtolower( $host );

		foreach ( $hosts as $covered ) {
			$covered = strtolower( $covered );

			if ( $host === $covered || substr( $host, -strlen( '.' . $covered ) ) === '.' . $covered ) {
				return true;
			}
		}

		return false;
	}

	/**
	 * Checks what can be checked before anything is sent, then sends an empty report and reads
	 * the answer.
	 *
	 * @return Outcome
	 */
	private function check(): array {
		$address = $this->settings->server_address();
		$outcome = $this->unready( $address );

		if ( '' !== $outcome ) {
			return self::outcome( $outcome );
		}

		$sent = $this->client->send( array(), self::TIMEOUT );
		$this->client->record( $sent, false );

		return null === $sent['answer'] ? self::refusal( $sent ) : $this->reading( $sent['answer'], $address );
	}

	/**
	 * What keeps a check from being sent at all, or an empty string when nothing does.
	 *
	 * @param string $address The address reports are sent to.
	 */
	private function unready( string $address ): string {
		$source = $this->server_key->source();
		$unread = array(
			Server_Key::UNREADABLE          => self::KEY_UNREADABLE,
			Server_Key::MALFORMED_IN_CONFIG => self::KEY_MALFORMED,
		);

		if ( ! $this->settings->is_complete() || Server_Key::NONE === $source ) {
			$outcome = self::NOT_SET_UP;
		} elseif ( isset( $unread[ $source ] ) ) {
			$outcome = $unread[ $source ];
		} elseif ( ! Private_Host::may_receive_secrets( $address ) ) {
			$outcome = self::INSECURE;
		} elseif ( ( new WP_Http() )->block_request( $this->client->url() ) ) {
			$outcome = self::BLOCKED;
		} else {
			$outcome = '';
		}

		return $outcome;
	}

	/**
	 * What an answer that did not accept the report means.
	 *
	 * An answer that is not a batch, or a page that is not there, is not Dewiride Analytics
	 * answering; a refusal of the site itself is something in front of it.
	 *
	 * @param array $sent What came of the report.
	 * @phpstan-param array{status: int, location: string, certificate: bool} $sent
	 * @return Outcome
	 */
	private static function refusal( array $sent ): array {
		$status = $sent['status'];

		if ( 0 === $status ) {
			return self::outcome( $sent['certificate'] ? self::CERTIFICATE : self::UNREACHABLE );
		}

		if ( $status >= 300 && $status < 400 ) {
			return self::outcome( self::REDIRECTED, $status, $sent['location'] );
		}

		$outcomes = array(
			200 => self::NOT_DEWIRIDE,
			401 => self::KEY_REFUSED,
			403 => self::TURNED_AWAY,
			404 => self::NOT_DEWIRIDE,
			405 => self::NOT_DEWIRIDE,
			429 => self::BUSY,
		);

		return self::outcome( $outcomes[ $status ] ?? self::UNEXPECTED, $status );
	}

	/**
	 * What an answer that accepted the report means for this site, checked field by field, and
	 * whether the address also serves the tag's script.
	 *
	 * @param array<mixed> $answer  The answer.
	 * @param string       $address The address reports are sent to.
	 * @return Outcome
	 */
	private function reading( array $answer, string $address ): array {
		$older = ! array_key_exists( 'site', $answer ) || ! array_key_exists( 'hosts', $answer ) || ! array_key_exists( 'measuring', $answer );
		$hosts = is_array( $answer['hosts'] ?? null ) ? array_values( array_filter( $answer['hosts'], 'is_string' ) ) : null;
		$site  = $answer['site'] ?? null;

		// A closed account covers no address and measures nothing; that it measures nothing is what
		// explains both, so it is read first.
		if ( is_string( $site ) && strtolower( $site ) !== $this->settings->site_id() ) {
			$outcome = self::outcome( self::OTHER_WEBSITE );
		} elseif ( false === ( $answer['measuring'] ?? null ) ) {
			$outcome = self::outcome( self::NOT_MEASURING );
		} elseif ( null !== $hosts && ! self::covers( self::home_host(), $hosts ) ) {
			$outcome = self::outcome( self::UNCOVERED, 0, implode( ', ', $hosts ) );
		} elseif ( $address === $this->settings->address() && ! self::serves_script( $address ) ) {
			$outcome = self::outcome( self::NO_SCRIPT );
		} else {
			$outcome = self::outcome( self::CONNECTED, 0, '', $older );
		}

		return $outcome;
	}

	/**
	 * Whether an address serves the tag's script, which it does when it is where Dewiride
	 * Analytics is opened rather than an address of its engine alone. Asked only where WordPress
	 * reaches the installation at the address browsers use: one given in code for WordPress alone
	 * can be the engine's, which takes reports and serves no script.
	 *
	 * @param string $address The address.
	 */
	private static function serves_script( string $address ): bool {
		$script = wp_remote_get(
			$address . '/dw.js',
			array(
				'timeout'            => self::TIMEOUT,
				'redirection'        => 0,
				'reject_unsafe_urls' => is_multisite(),
				'user-agent'         => 'DewirideAnalytics/' . DEWIRIDE_ANALYTICS_VERSION,
			)
		);

		return ! is_wp_error( $script ) && 200 === (int) wp_remote_retrieve_response_code( $script );
	}

	/**
	 * This site's host name, in lower case.
	 */
	public static function home_host(): string {
		return strtolower( (string) wp_parse_url( home_url(), PHP_URL_HOST ) );
	}

	/**
	 * An outcome.
	 *
	 * @param string $outcome The outcome.
	 * @param int    $status  The status that explains it, or 0.
	 * @param string $detail  What else explains it, or an empty string.
	 * @param bool   $older   Whether the installation left out what it could not yet say.
	 * @return Outcome
	 */
	private static function outcome( string $outcome, int $status = 0, string $detail = '', bool $older = false ): array {
		return array(
			'outcome' => $outcome,
			'status'  => $status,
			'detail'  => $detail,
			'older'   => $older,
		);
	}
}
