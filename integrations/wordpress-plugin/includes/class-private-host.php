<?php
/**
 * Addresses nobody on the public internet can reach.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Decides whether an address names this computer or a private network.
 *
 * What is sent to such an address never crosses the public internet, so an installation there may
 * be reached over plain http: one on the same computer while a site is built, or one on the
 * network a site is served from. Every other address is reached over https only.
 */
final class Private_Host {

	/**
	 * Names that never resolve on the public internet, each on its own or as the ending of a
	 * longer name: this computer's own and names kept for testing (RFC 6761), a local network's
	 * own names (RFC 6762), home networks' (RFC 8375), and names kept for private networks by ICANN.
	 */
	private const PRIVATE_NAMES = array( 'localhost', 'test', 'local', 'home.arpa', 'internal' );

	/**
	 * Whether the host of an address is this computer or on a private network.
	 *
	 * An IPv4 address is when it is not publicly routable: loopback, the private ranges, and the
	 * other ranges kept from public use. A name is when it is one of the names above or ends with
	 * one. A name that only resolves to a private address is not, because where it resolves to is
	 * for whoever controls the name to change.
	 *
	 * @param string $address An address, as the settings hold it.
	 */
	public static function matches( string $address ): bool {
		$host = strtolower( (string) wp_parse_url( $address, PHP_URL_HOST ) );

		if ( false !== filter_var( $host, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4 ) ) {
			return false === filter_var( $host, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4 | FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE );
		}

		foreach ( self::PRIVATE_NAMES as $name ) {
			$ending = '.' . $name;

			if ( $name === $host || substr( $host, -strlen( $ending ) ) === $ending ) {
				return true;
			}
		}

		return false;
	}
}
