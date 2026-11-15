<?php
/**
 * The addresses Cloudflare reaches websites from.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Keeps Cloudflare's list of the addresses its servers reach websites from, fetched from
 * Cloudflare itself.
 *
 * A site behind Cloudflare sees every request arrive from one of Cloudflare's addresses, with the
 * visitor's own in a header anybody could write. The header is believed only of a request that
 * came from one of these addresses, so the list is what makes it trustworthy. Cloudflare publishes
 * it at an address that needs no account; it is fetched the first time a request arrives through
 * Cloudflare while the server's reports are on, then once a week, and the last good copy is kept
 * whenever a fetch fails.
 *
 * Kept with the options WordPress loads for every page, because on a site behind Cloudflare
 * every report needs it, and it is a few hundred characters.
 */
final class Cloudflare_Ranges {

	/**
	 * The option holding the list.
	 */
	public const OPTION = 'dewiride_analytics_cloudflare';

	/**
	 * The WP-Cron event that fetches it.
	 */
	public const EVENT = 'dewiride_analytics_cloudflare';

	/**
	 * Where Cloudflare publishes the list.
	 */
	private const SOURCE = 'https://api.cloudflare.com/client/v4/ips';

	/**
	 * How long the list is given to arrive, in seconds.
	 */
	private const TIMEOUT = 10;

	/**
	 * The most ranges a list may hold before it is refused as not Cloudflare's.
	 */
	private const MOST_RANGES = 100;

	/**
	 * The shortest IPv4 prefix a range may have, and the shortest IPv6 one, before the list is
	 * refused. Cloudflare's are /22 to /13 and /32 to /29, so an answer naming far more of the
	 * internet than that is not one to trust visitors' addresses on.
	 */
	private const SHORTEST_IPV4_PREFIX = 10;

	/**
	 * See {@see self::SHORTEST_IPV4_PREFIX}.
	 */
	private const SHORTEST_IPV6_PREFIX = 20;

	/**
	 * How long to wait before trying again when no list has ever arrived, in seconds.
	 */
	private const RETRY = 3600;

	/**
	 * Hooks the fetch.
	 */
	public function register(): void {
		add_action( self::EVENT, array( $this, 'refresh' ) );
	}

	/**
	 * The ranges, or nothing when none has been fetched yet.
	 *
	 * Reading them asks for nothing: the fetch is scheduled by whatever sends reports, so that
	 * nothing reaches Cloudflare before the site's owner has switched reporting on.
	 *
	 * @return list<string>|null
	 */
	public function ranges(): ?array {
		return self::stored();
	}

	/**
	 * Schedules the weekly fetch, the first one at once, unless it is scheduled already.
	 */
	public static function schedule(): void {
		if ( false === wp_next_scheduled( self::EVENT ) ) {
			wp_schedule_event( time(), 'weekly', self::EVENT );
		}
	}

	/**
	 * Removes the weekly fetch.
	 */
	public static function unschedule(): void {
		wp_clear_scheduled_hook( self::EVENT );
	}

	/**
	 * Fetches the list and keeps it. When the fetch fails the last good copy stays, and while
	 * there is none another fetch is scheduled within the hour.
	 */
	public function refresh(): void {
		$ranges = self::fetch();

		if ( null !== $ranges ) {
			update_option(
				self::OPTION,
				array(
					'ranges'     => $ranges,
					'fetched_at' => time(),
				),
				true
			);

			return;
		}

		if ( null === self::stored() ) {
			wp_schedule_single_event( time() + self::RETRY, self::EVENT );
		}
	}

	/**
	 * Reads the ranges from Cloudflare's answer, or nothing when it is not a list of Cloudflare's
	 * ranges: not a successful answer, empty, too long, or holding a range that is not one or is
	 * far wider than any of Cloudflare's.
	 *
	 * @param mixed $answer Cloudflare's answer, decoded.
	 * @return list<string>|null
	 */
	public static function read( $answer ): ?array {
		$result = is_array( $answer ) && true === ( $answer['success'] ?? null ) && is_array( $answer['result'] ?? null ) ? $answer['result'] : array();
		$ranges = array_merge(
			is_array( $result['ipv4_cidrs'] ?? null ) ? $result['ipv4_cidrs'] : array(),
			is_array( $result['ipv6_cidrs'] ?? null ) ? $result['ipv6_cidrs'] : array()
		);

		if ( array() === $ranges || count( $ranges ) > self::MOST_RANGES ) {
			return null;
		}

		$read = array();

		foreach ( $ranges as $text ) {
			$range = is_string( $text ) ? Address_Range::parse( $text ) : null;

			if ( null === $range || $range[1] < ( 4 === strlen( $range[0] ) ? self::SHORTEST_IPV4_PREFIX : self::SHORTEST_IPV6_PREFIX ) ) {
				return null;
			}

			$read[] = (string) $text;
		}

		return $read;
	}

	/**
	 * The list as kept, or nothing when there is none.
	 *
	 * @return list<string>|null
	 */
	private static function stored(): ?array {
		$stored = get_option( self::OPTION );
		$ranges = is_array( $stored ) && is_array( $stored['ranges'] ?? null ) ? array_values( array_filter( $stored['ranges'], 'is_string' ) ) : array();

		return array() === $ranges ? null : $ranges;
	}

	/**
	 * The list as Cloudflare publishes it today, or nothing when it could not be fetched or read.
	 *
	 * @return list<string>|null
	 */
	private static function fetch(): ?array {
		$response = wp_remote_get(
			self::SOURCE,
			array(
				'timeout'     => self::TIMEOUT,
				'redirection' => 0,
				'user-agent'  => 'DewirideAnalytics/' . DEWIRIDE_ANALYTICS_VERSION,
			)
		);

		if ( is_wp_error( $response ) || 200 !== (int) wp_remote_retrieve_response_code( $response ) ) {
			return null;
		}

		return self::read( json_decode( wp_remote_retrieve_body( $response ), true ) );
	}
}
