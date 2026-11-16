<?php
/**
 * Sends the server's reports to Dewiride Analytics.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Sends reports to the installation's collector for server reports, with the server key, and
 * reads what it answers.
 *
 * Each report is one request, sent as the page it describes is finished, and given two seconds at
 * most: a report usually takes a tenth of that, and every second it may take is a second a PHP
 * worker can be kept from the next page when the installation stops answering. It is never queued:
 * a report that waits is a report that says the page was slower to arrive than it was, which is
 * part of how crawlers are told apart.
 *
 * @phpstan-type Sent array{status: int, answer: array<mixed>|null, location: string, retry_after: int, duration_ms: int, certificate: bool}
 */
final class Collector_Client {

	/**
	 * How the reports name where they come from.
	 */
	public const SURFACE = 'wordpress-plugin';

	/**
	 * Where server reports go, after the installation's address.
	 */
	private const PATH = '/collect/server';

	/**
	 * How long a report may take, in seconds.
	 */
	private const TIMEOUT = 2;

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
	 * How reports have been going.
	 *
	 * @var Reporting_Status
	 */
	private $status;

	/**
	 * Creates the client.
	 *
	 * @param Settings         $settings   The plugin's settings.
	 * @param Server_Key       $server_key The server key.
	 * @param Reporting_Status $status     How reports have been going.
	 */
	public function __construct( Settings $settings, Server_Key $server_key, Reporting_Status $status ) {
		$this->settings   = $settings;
		$this->server_key = $server_key;
		$this->status     = $status;
	}

	/**
	 * Where server reports are sent.
	 */
	public function url(): string {
		return $this->settings->server_address() . self::PATH;
	}

	/**
	 * Sends one report and records how it went.
	 *
	 * @param array<string, mixed> $observation What was seen of the request.
	 */
	public function report( array $observation ): void {
		if ( '' === $this->server_key->key() ) {
			return;
		}

		$this->record( $this->send( array( $observation ), self::TIMEOUT ) );
	}

	/**
	 * Sends a batch of observations, which may be empty, and gives back what came of it.
	 *
	 * Redirects are not followed: the key goes only to the address that was saved. On a network,
	 * where the address was chosen by somebody who need not be trusted with the network's servers,
	 * WordPress refuses an address it would not fetch for anybody.
	 *
	 * WordPress bounds a request's whole time only where PHP has cURL; without it, connecting alone
	 * may take ten seconds, so for this request the connection is given the same bound as the
	 * whole.
	 *
	 * @param list<array<string, mixed>> $events  The observations.
	 * @param int                        $timeout How long it may take, in seconds.
	 * @return Sent
	 */
	public function send( array $events, int $timeout ): array {
		$body = wp_json_encode(
			array(
				'surface' => self::SURFACE,
				'events'  => $events,
			),
			JSON_UNESCAPED_SLASHES | JSON_INVALID_UTF8_SUBSTITUTE
		);

		$bound = static function ( &$url, &$headers, &$data, &$type, &$options ) use ( $timeout ): void {
			if ( is_array( $options ) ) {
				$options['connect_timeout'] = $timeout;
			}
		};

		add_action( 'requests-requests.before_request', $bound, 10, 5 );

		$started  = microtime( true );
		$response = false === $body ? null : wp_remote_post(
			$this->url(),
			array(
				'timeout'            => $timeout,
				'redirection'        => 0,
				'sslverify'          => true,
				'reject_unsafe_urls' => is_multisite(),
				'user-agent'         => 'DewirideAnalytics/' . DEWIRIDE_ANALYTICS_VERSION,
				'headers'            => array(
					'Authorization' => 'Bearer ' . $this->server_key->key(),
					'Content-Type'  => 'application/json',
				),
				'body'               => $body,
			)
		);
		$duration = (int) round( 1000 * ( microtime( true ) - $started ) );

		remove_action( 'requests-requests.before_request', $bound, 10 );

		if ( ! is_array( $response ) ) {
			return self::sent( 0, null, '', 0, $duration, is_wp_error( $response ) && 1 === preg_match( '/certificate|\bSSL\b/i', $response->get_error_message() ) );
		}

		$status = (int) wp_remote_retrieve_response_code( $response );
		$answer = 200 === $status ? json_decode( wp_remote_retrieve_body( $response ), true ) : null;

		return self::sent(
			$status,
			is_array( $answer ) && is_int( $answer['accepted'] ?? null ) ? $answer : null,
			self::elsewhere( self::header( $response, 'location' ), $this->url() ),
			(int) self::header( $response, 'retry-after' ),
			$duration
		);
	}

	/**
	 * Records what came of a report.
	 *
	 * An answer that accepted it but names another website is a key from that website, which
	 * stops the reports as a refused key does; one whose website does not cover this site's address
	 * had every report refused, and is asked again later. Anything that is neither a success nor one
	 * of the answers that say what to do is a failure, waited out.
	 *
	 * @param array $sent   What came of the report.
	 * @param bool  $report Whether the batch was a report, rather than the empty batch that checks
	 *                      the connection.
	 * @phpstan-param Sent $sent
	 */
	public function record( array $sent, bool $report = true ): void {
		$status = $sent['status'];
		$answer = $sent['answer'];

		if ( null !== $answer ) {
			$site = $answer['site'] ?? null;

			$hosts = is_array( $answer['hosts'] ?? null ) ? array_values( array_filter( $answer['hosts'], 'is_string' ) ) : null;

			if ( is_string( $site ) && strtolower( $site ) !== $this->settings->site_id() ) {
				$this->status->stop( Reporting_Status::OTHER_WEBSITE );
			} elseif ( null !== $hosts && array() !== $hosts && ! Connection_Check::covers( Connection_Check::home_host(), $hosts ) ) {
				$this->status->uncovered();
			} else {
				$this->status->succeeded( $answer, $sent['duration_ms'], $report );
			}
		} elseif ( 401 === $status ) {
			$this->status->stop( Reporting_Status::KEY_REFUSED );
		} elseif ( $status >= 300 && $status < 400 ) {
			$this->status->stop( Reporting_Status::REDIRECTED, $sent['location'] );
		} elseif ( 429 === $status ) {
			$this->status->slowed( $sent['retry_after'] );
		} else {
			$this->status->failed( $status );
		}
	}

	/**
	 * What came of a report.
	 *
	 * @param int               $status      The answer's status, or 0 when there was none.
	 * @param array<mixed>|null $answer      The answer, when it accepted the batch.
	 * @param string            $location    Where a redirect leads, as a scheme and a host.
	 * @param int               $retry_after How long an answer asked for a pause, in seconds.
	 * @param int               $duration_ms How long it took, in milliseconds.
	 * @param bool              $certificate Whether there was no answer because the address's
	 *                                       certificate was not accepted.
	 * @return Sent
	 */
	private static function sent( int $status, ?array $answer, string $location, int $retry_after, int $duration_ms, bool $certificate = false ): array {
		return array(
			'status'      => $status,
			'answer'      => $answer,
			'location'    => $location,
			'retry_after' => $retry_after,
			'duration_ms' => $duration_ms,
			'certificate' => $certificate,
		);
	}

	/**
	 * A header of the answer, as text.
	 *
	 * @param array<mixed> $response The answer.
	 * @param string       $name     The header's name.
	 */
	private static function header( array $response, string $name ): string {
		$value = wp_remote_retrieve_header( $response, $name );
		$value = is_array( $value ) ? reset( $value ) : $value;

		return is_string( $value ) ? trim( $value ) : '';
	}

	/**
	 * The scheme, host and port a redirect leads to, which is all the settings page shows of it
	 * and the address it suggests saving instead; or an empty string when it leads nowhere else
	 * to save: it names no location, or one on the address that was asked.
	 *
	 * A location may be written relative to the address asked — a path alone keeps its scheme
	 * and host, a host without a scheme keeps its scheme — and is read that way.
	 *
	 * @param string $location The redirect's location.
	 * @param string $asked    The address that was asked.
	 */
	private static function elsewhere( string $location, string $asked ): string {
		$parts = '' === $location ? false : wp_parse_url( $location );
		$base  = wp_parse_url( $asked );

		if ( ! is_array( $parts ) || ! is_array( $base ) || ! isset( $parts['host'] ) ) {
			return '';
		}

		$origin = self::origin( $parts + array( 'scheme' => $base['scheme'] ?? '' ) );

		return self::origin( $base ) === $origin ? '' : $origin;
	}

	/**
	 * The scheme, host and port of an address's parts, in lower case, without the port its scheme
	 * uses anyway.
	 *
	 * @param array<string, int|string> $parts The parts, as wp_parse_url() gives them.
	 */
	private static function origin( array $parts ): string {
		$scheme   = strtolower( (string) ( $parts['scheme'] ?? '' ) );
		$port     = (int) ( $parts['port'] ?? 0 );
		$implicit = array(
			'http'  => 80,
			'https' => 443,
		);

		return $scheme . '://' . strtolower( (string) ( $parts['host'] ?? '' ) ) . ( 0 === $port || ( $implicit[ $scheme ] ?? 0 ) === $port ? '' : ':' . $port );
	}
}
