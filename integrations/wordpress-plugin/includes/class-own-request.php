<?php
/**
 * Requests the site makes to itself.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Recognises the requests a site makes to itself, which are not visits and are never reported.
 *
 * WordPress fetches its own pages — Site Health checks its page cache, scheduled events start
 * themselves — and so do caching plugins warming their copies, and this plugin when it checks the
 * tag. Each arrives from the site's own address or names itself in its user agent, and this
 * plugin's own requests also carry a header nobody without the site's secret keys can write.
 */
final class Own_Request {

	/**
	 * The header the plugin's own requests to its site carry.
	 */
	public const HEADER = 'X-Dewiride-Analytics-Own';

	/**
	 * How long one value of the header is good for, in seconds. The value before it is accepted
	 * too, so a request made just before the value changes is still recognised.
	 */
	private const PERIOD = 600;

	/**
	 * What the user agents of this plugin and of the installation's own sitemap reader begin with.
	 */
	private const DEWIRIDE_AGENT = 'DewirideAnalytics/';

	/**
	 * What the user agent of WP Rocket's cache warming begins with.
	 */
	private const WP_ROCKET_AGENT = 'WP Rocket/';

	/**
	 * What the user agent of WordPress's own requests begins with; it ends with the site's address.
	 */
	private const WORDPRESS_AGENT = 'WordPress/';

	/**
	 * The value the plugin's own requests carry now.
	 */
	public static function marker(): string {
		return self::marker_for( intdiv( time(), self::PERIOD ) );
	}

	/**
	 * Whether the request being answered is one of the plugin's own, by the header it carries.
	 */
	public static function is_marked(): bool {
		$sent = Request_Headers::header( self::HEADER, 128 );

		if ( '' === $sent ) {
			return false;
		}

		$period = intdiv( time(), self::PERIOD );

		return hash_equals( self::marker_for( $period ), $sent ) || hash_equals( self::marker_for( $period - 1 ), $sent );
	}

	/**
	 * Whether an address is the site's own: this computer, the address its web server answers on,
	 * or one its reports have been seen reaching Dewiride Analytics from.
	 *
	 * @param string $address    A visitor's address, as {@see Address_Range::normalise()} writes it.
	 * @param array  $sends_from The addresses reports have been seen arriving from.
	 * @phpstan-param list<string> $sends_from
	 */
	public static function is_own_address( string $address, array $sends_from ): bool {
		return Address_Range::is_loopback( $address )
			|| Address_Range::normalise( Request_Headers::server( 'SERVER_ADDR', 64 ) ) === $address
			|| in_array( $address, $sends_from, true );
	}

	/**
	 * Whether a user agent names a program the site itself runs: WordPress fetching one of this
	 * site's pages, WP Rocket warming its cache, or this plugin.
	 *
	 * Anybody can send these names, so they are believed only of a request the site answered
	 * without an error; see {@see Request_Report}.
	 *
	 * @param string $user_agent The request's user agent.
	 */
	public static function is_own_agent( string $user_agent ): bool {
		$this_site = '; ' . home_url();

		return 0 === strpos( $user_agent, self::DEWIRIDE_AGENT )
			|| 0 === strpos( $user_agent, self::WP_ROCKET_AGENT )
			|| ( 0 === strpos( $user_agent, self::WORDPRESS_AGENT ) && substr( $user_agent, -strlen( $this_site ) ) === $this_site );
	}

	/**
	 * The header's value for one period.
	 *
	 * @param int $period The number of the period since 1970.
	 */
	private static function marker_for( int $period ): string {
		return hash_hmac( 'sha256', 'dewiride-analytics-own-request:' . $period, wp_salt( 'nonce' ) );
	}
}
