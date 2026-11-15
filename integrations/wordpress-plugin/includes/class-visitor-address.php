<?php
/**
 * Where the visitor WordPress is answering is.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Works out the visitor's IP address, believing a forwarding header only of a proxy known to
 * write it.
 *
 * The address is what most of a visit's judgement rests on — whose network it is, and whether a
 * crawler is the one it says it is — so an address that cannot be trusted is not sent at all, and
 * the reason is kept for the settings page to explain. Two ways of reaching a site are known:
 *
 * - **Automatically**: the address the connection came from, unless it came from Cloudflare, for
 *   one of the site's own names, when Cloudflare's header names the visitor. A connection from a
 *   private address that forwards somebody else's is from a proxy WordPress was not told about.
 * - **Through another proxy**: the proxies' addresses and the header they write are given in the
 *   settings, and the header is believed only of a request that arrives from one of them. A list
 *   of addresses is read from its right-hand end, past the proxies, to the first address that is
 *   not one. A request from a public address that is not a proxy's reached the site directly, and
 *   that address is the visitor's.
 *
 * Either way an address that is not on the public internet is not sent, except on a site whose
 * environment is `local` or `development`, where the visitors are the developer's own.
 *
 * @phpstan-type Resolved array{address: string, cause: string}
 */
final class Visitor_Address {

	/**
	 * The address was found.
	 */
	public const FOUND = '';

	/**
	 * The connection came from a private address and forwarded somebody else's, so a proxy stands
	 * in front of the site that WordPress was not told about.
	 */
	public const UNKNOWN_PROXY = 'unknown-proxy';

	/**
	 * The connection came from Cloudflare, but for a name that is not the site's own, so another
	 * Cloudflare customer could have written the visitor's address.
	 */
	public const FOREIGN_CLOUDFLARE = 'foreign-cloudflare';

	/**
	 * The request came through Cloudflare, and Cloudflare's addresses have not been fetched yet.
	 */
	public const CLOUDFLARE_PENDING = 'cloudflare-pending';

	/**
	 * The request did not come from one of the proxies named in the settings.
	 */
	public const UNTRUSTED_PROXY = 'untrusted-proxy';

	/**
	 * The proxy did not say who the visitor was.
	 */
	public const NO_VISITOR = 'no-visitor';

	/**
	 * The visitor's address is not on the public internet.
	 */
	public const PRIVATE_ADDRESS = 'private-address';

	/**
	 * The web server gave no address for the connection.
	 */
	public const NO_ADDRESS = 'no-address';

	/**
	 * The header Cloudflare names the visitor in.
	 */
	private const CLOUDFLARE_HEADER = 'CF-Connecting-IP';

	/**
	 * The most of a proxy's list of addresses read, in bytes from its end: as much as web servers
	 * accept in one header.
	 */
	private const LONGEST_LIST = 8192;

	/**
	 * Headers a proxy names the visitor in. A private connection carrying one is a proxy's.
	 */
	private const FORWARDING_HEADERS = array(
		'Forwarded',
		'X-Forwarded-For',
		'X-Real-IP',
		'True-Client-IP',
		'X-Client-IP',
		'X-Cluster-Client-IP',
		'Fastly-Client-IP',
		self::CLOUDFLARE_HEADER,
	);

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * Cloudflare's addresses.
	 *
	 * @var Cloudflare_Ranges
	 */
	private $cloudflare;

	/**
	 * The answer for this request, once worked out.
	 *
	 * @var Resolved|null
	 */
	private $resolved;

	/**
	 * Creates the reading over the given settings.
	 *
	 * @param Settings          $settings   The plugin's settings.
	 * @param Cloudflare_Ranges $cloudflare Cloudflare's addresses.
	 */
	public function __construct( Settings $settings, Cloudflare_Ranges $cloudflare ) {
		$this->settings   = $settings;
		$this->cloudflare = $cloudflare;
	}

	/**
	 * The visitor's address and, when it cannot be sent, an empty address and the reason.
	 *
	 * @return Resolved
	 */
	public function resolve(): array {
		if ( null === $this->resolved ) {
			$this->resolved = $this->work_out();
		}

		return $this->resolved;
	}

	/**
	 * Works the address out.
	 *
	 * @return Resolved
	 */
	private function work_out(): array {
		$connection = Address_Range::normalise( Request_Headers::server( 'REMOTE_ADDR', 64 ) );

		if ( '' === $connection ) {
			return self::refused( self::NO_ADDRESS );
		}

		$proxy    = $this->settings->proxy();
		$resolved = Settings::THROUGH_PROXY === $proxy['mode']
			? self::through_proxy( $connection, $proxy['header'], $proxy['trusted'] )
			: $this->automatically( $connection );

		if ( self::FOUND !== $resolved['cause'] || Address_Range::is_public( $resolved['address'] ) || self::is_development() ) {
			return $resolved;
		}

		return self::refused( self::PRIVATE_ADDRESS );
	}

	/**
	 * The address when nothing is configured: the connection's, or Cloudflare's header for it.
	 *
	 * @param string $connection The address the connection came from.
	 * @return Resolved
	 */
	private function automatically( string $connection ): array {
		$cloudflare_header = Request_Headers::header( self::CLOUDFLARE_HEADER, 64 );
		$through           = '' === $cloudflare_header ? null : $this->through_cloudflare( $connection, $cloudflare_header );

		if ( null !== $through ) {
			return $through;
		}

		return ! Address_Range::is_public( $connection ) && self::forwards_an_address()
			? self::refused( self::UNKNOWN_PROXY )
			: self::found( $connection );
	}

	/**
	 * The address Cloudflare names for a request that came through Cloudflare, or nothing when the
	 * connection is not one of Cloudflare's and the header was written by somebody else.
	 *
	 * @param string $connection The address the connection came from.
	 * @param string $header     What Cloudflare's header holds.
	 * @return Resolved|null
	 */
	private function through_cloudflare( string $connection, string $header ): ?array {
		$ranges = $this->cloudflare->ranges();
		$named  = Address_Range::normalise( $header );

		if ( null === $ranges ) {
			$resolved = self::refused( self::CLOUDFLARE_PENDING );
		} elseif ( ! Address_Range::in_any( $ranges, $connection ) ) {
			$resolved = null;
		} elseif ( ! self::is_for_this_site() ) {
			$resolved = self::refused( self::FOREIGN_CLOUDFLARE );
		} else {
			$resolved = '' === $named ? self::refused( self::NO_VISITOR ) : self::found( $named );
		}

		return $resolved;
	}

	/**
	 * The address a proxy named in the settings says the visitor has.
	 *
	 * @param string $connection The address the connection came from.
	 * @param string $header     The header the proxies name the visitor in.
	 * @param array  $trusted    The proxies' addresses.
	 * @phpstan-param list<string> $trusted
	 * @return Resolved
	 */
	private static function through_proxy( string $connection, string $header, array $trusted ): array {
		// A request from anywhere but the proxies reached the site directly, so the connection's
		// own address is the visitor's, whatever its headers say. One from a private network is
		// most likely a proxy whose address the settings do not name.
		if ( ! Address_Range::in_any( $trusted, $connection ) ) {
			return Address_Range::is_public( $connection ) ? self::found( $connection ) : self::refused( self::UNTRUSTED_PROXY );
		}

		$value = Request_Headers::list_end( $header, self::LONGEST_LIST );

		return self::first_past_the_proxies(
			0 === strcasecmp( 'Forwarded', $header ) ? self::forwarded_for( $value ) : self::listed_in( $value ),
			$trusted
		);
	}

	/**
	 * The first address in a list, read from its right-hand end, that is not a proxy's.
	 *
	 * The last proxy appended the address it was reached from; anything to its left was written by
	 * whoever reached it, so the walk stops at the first address that is not a proxy's, and nothing
	 * further left is read. An entry that is not an address, met before then, names nobody.
	 *
	 * @param array $named   The addresses the list names, left to right.
	 * @param array $trusted The proxies' addresses.
	 * @phpstan-param list<string> $named
	 * @phpstan-param list<string> $trusted
	 * @return Resolved
	 */
	private static function first_past_the_proxies( array $named, array $trusted ): array {
		$resolved = self::refused( self::NO_VISITOR );

		for ( $at = count( $named ) - 1; $at >= 0 && '' !== $named[ $at ]; $at-- ) {
			if ( ! Address_Range::in_any( $trusted, $named[ $at ] ) ) {
				$resolved = self::found( $named[ $at ] );
				break;
			}
		}

		return $resolved;
	}

	/**
	 * The addresses a comma-separated list names, left to right, each as an address or an empty
	 * string where an entry is not one. A single address is a list of one.
	 *
	 * @param string $value The header's value.
	 * @return list<string>
	 */
	private static function listed_in( string $value ): array {
		if ( '' === trim( $value ) ) {
			return array();
		}

		return array_map( array( Address_Range::class, 'normalise' ), explode( ',', $value ) );
	}

	/**
	 * The addresses a `Forwarded` header (RFC 7239) names, left to right: each element's `for=`
	 * parameter, perhaps quoted and in brackets, as an address, or an empty string where it is not
	 * one.
	 *
	 * A comma or a semicolon inside a quoted string is part of a value, not a separator, so a
	 * visitor's `Host` that a proxy copies into a `host=` parameter cannot add an element of its
	 * own. The header is split from its right-hand end, where the proxies wrote: read that way, each
	 * quoted string a proxy wrote opens and closes inside that proxy's element, so a quotation mark
	 * the visitor left open further left cannot move where the proxies' elements begin.
	 *
	 * @param string $value The header's value.
	 * @return list<string>
	 */
	private static function forwarded_for( string $value ): array {
		if ( '' === trim( $value ) ) {
			return array();
		}

		$named = array();

		foreach ( self::split_leftwards( $value, ',' ) as $element ) {
			$named[] = Address_Range::normalise( self::for_parameter( $element ) );
		}

		return array_reverse( $named );
	}

	/**
	 * The `for=` parameter of one element of a `Forwarded` header, without the quotation marks
	 * around it and with what they escape restored, or an empty string when it has none.
	 *
	 * @param string $element The element.
	 */
	private static function for_parameter( string $element ): string {
		foreach ( self::split_leftwards( $element, ';' ) as $pair ) {
			if ( 1 === preg_match( '/^\s*for\s*=\s*(.*?)\s*$/i', $pair, $parameter ) ) {
				$for = $parameter[1];

				return strlen( $for ) >= 2 && '"' === $for[0] && '"' === substr( $for, -1 )
					? (string) preg_replace( '/\\\\(.)/', '$1', substr( $for, 1, -1 ) )
					: $for;
			}
		}

		return '';
	}

	/**
	 * A value split at a separator, read from its right-hand end, except where the separator is
	 * inside a quoted string.
	 *
	 * Read from the end, a quotation mark after an odd number of backslashes is one a quoted string
	 * holds, and any other ends or begins one.
	 *
	 * @param string $value     The value.
	 * @param string $separator The separator, one character.
	 * @return list<string> The parts, right to left.
	 */
	private static function split_leftwards( string $value, string $separator ): array {
		$parts  = array();
		$quoted = false;
		$end    = strlen( $value );

		for ( $at = $end - 1; $at >= 0; $at-- ) {
			if ( '"' === $value[ $at ] && ! self::is_escaped( $value, $at ) ) {
				$quoted = ! $quoted;
			} elseif ( $separator === $value[ $at ] && ! $quoted ) {
				$parts[] = (string) substr( $value, $at + 1, $end - $at - 1 );
				$end     = $at;
			}
		}

		$parts[] = (string) substr( $value, 0, $end );

		return $parts;
	}

	/**
	 * Whether the character at a place comes after an odd number of backslashes.
	 *
	 * @param string $value The value.
	 * @param int    $at    The place.
	 */
	private static function is_escaped( string $value, int $at ): bool {
		$backslashes = 0;

		while ( $at > $backslashes && '\\' === $value[ $at - $backslashes - 1 ] ) {
			++$backslashes;
		}

		return 1 === $backslashes % 2;
	}

	/**
	 * Whether the request was for one of the site's own names: its host, or the same host with or
	 * without `www.` in front.
	 *
	 * A site's own Cloudflare zone sends its own names. Another Cloudflare customer can point a
	 * name of theirs at the same server, and write any address they like into the header, but the
	 * name they asked for is theirs.
	 */
	private static function is_for_this_site(): bool {
		$requested = strtolower( (string) preg_replace( '/:\d+$/', '', Request_Headers::header( 'Host', 255 ) ) );
		$home      = strtolower( (string) wp_parse_url( home_url(), PHP_URL_HOST ) );

		return '' !== $home && ( $requested === $home || 'www.' . $requested === $home || 'www.' . $home === $requested );
	}

	/**
	 * Whether the request carries any header a proxy names the visitor in.
	 */
	private static function forwards_an_address(): bool {
		foreach ( self::FORWARDING_HEADERS as $header ) {
			if ( '' !== Request_Headers::header( $header, 64 ) ) {
				return true;
			}
		}

		return false;
	}

	/**
	 * Whether the site is somebody's own copy, being built or tried out, where every visitor is
	 * on a private network.
	 */
	private static function is_development(): bool {
		return in_array( wp_get_environment_type(), array( 'local', 'development' ), true );
	}

	/**
	 * An address found.
	 *
	 * @param string $address The address.
	 * @return Resolved
	 */
	private static function found( string $address ): array {
		return array(
			'address' => $address,
			'cause'   => self::FOUND,
		);
	}

	/**
	 * No address, for a reason.
	 *
	 * @param string $cause The reason.
	 * @return Resolved
	 */
	private static function refused( string $cause ): array {
		return array(
			'address' => '',
			'cause'   => $cause,
		);
	}
}
