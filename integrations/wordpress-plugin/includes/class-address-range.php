<?php
/**
 * IP addresses and the ranges they fall in.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Reads IP addresses and ranges the way forwarding headers and settings write them, and says
 * whether an address falls in a range.
 *
 * @phpstan-type Range array{0: string, 1: int}
 */
final class Address_Range {

	/**
	 * The addresses carriers share among their customers (RFC 6598), which PHP does not count as
	 * private although nobody can be reached at one from the internet.
	 */
	private const SHARED = '100.64.0.0/10';

	/**
	 * The addresses of this computer.
	 */
	private const LOOPBACK = array( '127.0.0.0/8', '::1/128' );

	/**
	 * The prefix of an IPv6 address that carries an IPv4 one (RFC 4291).
	 */
	private const MAPPED_PREFIX = "\0\0\0\0\0\0\0\0\0\0\xff\xff";

	/**
	 * An address as the plugin compares it, or an empty string when the text holds none.
	 *
	 * Takes what forwarding headers write: an IPv4 address with a port after it, and an IPv6
	 * address in brackets, with or without a port. An IPv6 address that carries an IPv4 one is
	 * written as the IPv4 address, so that the two spellings of one connection are one address.
	 *
	 * @param string $text The text that may hold an address.
	 */
	public static function normalise( string $text ): string {
		$text = trim( $text );

		if ( 1 === preg_match( '/^\[([^\]]+)\](?::[0-9]{1,5})?$/', $text, $parts ) ) {
			$text = $parts[1];
		} elseif ( 1 === preg_match( '/^([0-9.]+):[0-9]{1,5}$/', $text, $parts ) ) {
			$text = $parts[1];
		}

		if ( false === filter_var( $text, FILTER_VALIDATE_IP ) ) {
			return '';
		}

		$packed = (string) inet_pton( $text );

		if ( 16 === strlen( $packed ) && self::MAPPED_PREFIX === substr( $packed, 0, 12 ) ) {
			$packed = substr( $packed, 12 );
		}

		return (string) inet_ntop( $packed );
	}

	/**
	 * A range, written as an address and the length of its prefix such as `173.245.48.0/20`, or
	 * as one address; nothing when the text is not one.
	 *
	 * @param string $text The range as written.
	 * @return Range|null The range's first address, packed, and the length of its prefix.
	 */
	public static function parse( string $text ): ?array {
		$parts = explode( '/', trim( $text ), 2 );

		if ( false === filter_var( $parts[0], FILTER_VALIDATE_IP ) ) {
			return null;
		}

		$packed = (string) inet_pton( self::normalise( $parts[0] ) );
		$bits   = 8 * strlen( $packed );

		if ( ! isset( $parts[1] ) ) {
			return array( $packed, $bits );
		}

		if ( 1 !== preg_match( '/^[0-9]{1,3}$/', $parts[1] ) ) {
			return null;
		}

		// A range of IPv6 addresses carrying IPv4 ones is the same range of IPv4 addresses, whose
		// prefix is 96 bits shorter.
		$length = (int) $parts[1] - ( false !== strpos( $parts[0], ':' ) && 32 === $bits ? 96 : 0 );

		return $length >= 0 && $length <= $bits ? array( $packed, $length ) : null;
	}

	/**
	 * Whether an address falls in a range.
	 *
	 * @param array  $range   The range, as {@see self::parse()} reads it.
	 * @param string $address The address, as {@see self::normalise()} writes it.
	 * @phpstan-param Range $range
	 */
	public static function contains( array $range, string $address ): bool {
		if ( '' === $address ) {
			return false;
		}

		$packed           = (string) inet_pton( $address );
		$network          = $range[0];
		$length           = $range[1];
		$whole_bytes      = intdiv( $length, 8 );
		$bits_of_the_next = $length % 8;

		if ( strlen( $packed ) !== strlen( $network ) || substr( $packed, 0, $whole_bytes ) !== substr( $network, 0, $whole_bytes ) ) {
			return false;
		}

		if ( 0 === $bits_of_the_next ) {
			return true;
		}

		$mask = ( 0xff << ( 8 - $bits_of_the_next ) ) & 0xff;

		return ( ord( $packed[ $whole_bytes ] ) & $mask ) === ( ord( $network[ $whole_bytes ] ) & $mask );
	}

	/**
	 * Whether an address falls in any of the ranges, each written as {@see self::parse()} reads
	 * it. A range that cannot be read holds no address.
	 *
	 * @param array<mixed> $ranges  The ranges, as written.
	 * @param string       $address The address, as {@see self::normalise()} writes it.
	 */
	public static function in_any( array $ranges, string $address ): bool {
		foreach ( $ranges as $text ) {
			$range = is_string( $text ) ? self::parse( $text ) : null;

			if ( null !== $range && self::contains( $range, $address ) ) {
				return true;
			}
		}

		return false;
	}

	/**
	 * Whether anybody on the internet can be reached at an address: not a private one, not this
	 * computer, not a range kept from public use and not one carriers share among customers.
	 *
	 * @param string $address The address, as {@see self::normalise()} writes it.
	 */
	public static function is_public( string $address ): bool {
		return false !== filter_var( $address, FILTER_VALIDATE_IP, FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE )
			&& ! self::in_any( array( self::SHARED ), $address );
	}

	/**
	 * Whether an address is this computer's own.
	 *
	 * @param string $address The address, as {@see self::normalise()} writes it.
	 */
	public static function is_loopback( string $address ): bool {
		return self::in_any( self::LOOPBACK, $address );
	}
}
