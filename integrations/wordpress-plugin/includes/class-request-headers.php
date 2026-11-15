<?php
/**
 * What the request WordPress is answering says about itself.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Reads the request's headers, and what the web server says about the request, one way for all
 * of them.
 *
 * Whoever sent the request wrote every header in it, so what is read is evidence, not truth, and
 * it is kept as it was written: control characters are dropped, a value that is not valid UTF-8
 * is emptied, and a value is cut to a length. Nothing else is rewritten, because what a crawler or
 * a scanner sends is what tells it apart.
 */
final class Request_Headers {

	/**
	 * The most characters kept of a value, unless a shorter limit is asked for.
	 */
	public const LONGEST = 1024;

	/**
	 * A request header by its name, or an empty string when the request did not send it.
	 *
	 * @param string $name    The header's name, such as `Sec-Fetch-Dest`.
	 * @param int    $longest The most characters kept.
	 */
	public static function header( string $name, int $longest = self::LONGEST ): string {
		return self::server( 'HTTP_' . strtoupper( str_replace( '-', '_', $name ) ), $longest );
	}

	/**
	 * A value the web server put in `$_SERVER`, or an empty string when there is none.
	 *
	 * WordPress adds slashes to `$_SERVER` as it does to a form's fields, so they are taken off
	 * before anything else.
	 *
	 * @param string $key     The key, such as `REMOTE_ADDR`.
	 * @param int    $longest The most characters kept.
	 */
	public static function server( string $key, int $longest = self::LONGEST ): string {
		if ( ! isset( $_SERVER[ $key ] ) || ! is_string( $_SERVER[ $key ] ) ) {
			return '';
		}

		$value = filter_var( wp_unslash( $_SERVER[ $key ] ), FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );

		return is_string( $value ) ? mb_substr( wp_check_invalid_utf8( $value ), 0, $longest ) : '';
	}

	/**
	 * The end of a request header that lists addresses, or an empty string when the request did
	 * not send it.
	 *
	 * Each proxy appends what it saw to the end of the list it was handed, so the end is the part
	 * that can be believed: cutting a long list from its start, as other headers are cut, would keep
	 * what the visitor wrote and drop what the proxies added. A list longer than the limit loses its
	 * first entries, the cut one among them. Only control characters are dropped, and the value is
	 * not tested as UTF-8: what is read from it is addresses, and an entry holding anything else is
	 * simply not one, where emptying the whole list would let any visitor empty it.
	 *
	 * @param string $name    The header's name, such as `X-Forwarded-For`.
	 * @param int    $longest The most bytes kept, counted from the end.
	 */
	public static function list_end( string $name, int $longest ): string {
		$key = 'HTTP_' . strtoupper( str_replace( '-', '_', $name ) );

		if ( ! isset( $_SERVER[ $key ] ) || ! is_string( $_SERVER[ $key ] ) ) {
			return '';
		}

		$value = (string) filter_var( wp_unslash( $_SERVER[ $key ] ), FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );

		if ( strlen( $value ) > $longest ) {
			$value = (string) substr( $value, -$longest );
			$value = (string) substr( $value, strcspn( $value, ',' ) + 1 );
		}

		return $value;
	}

	/**
	 * The request's method, in capitals, or an empty string when the server named none.
	 */
	public static function method(): string {
		return strtoupper( self::server( 'REQUEST_METHOD', 16 ) );
	}

	/**
	 * The path the request asked for, without its query or fragment, as a web address may hold
	 * it.
	 *
	 * Kept as it was sent, because a probe for `/.env` or `/wp-config.php.bak` is told apart by
	 * exactly that. Control characters are dropped, and every character a path may not hold —
	 * bytes that are not ASCII among them — is written as a percent escape, as is a `%` that does
	 * not start one, so the address is one any reader accepts. A request naming a whole address
	 * rather than a path, as one sent to a proxy does, is read for its path.
	 */
	public static function requested_path(): string {
		if ( ! isset( $_SERVER['REQUEST_URI'] ) || ! is_string( $_SERVER['REQUEST_URI'] ) ) {
			return '/';
		}

		$requested = (string) filter_var( wp_unslash( $_SERVER['REQUEST_URI'] ), FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );

		if ( 1 === preg_match( '~^[a-z][a-z0-9+.-]*://[^/?#]*~i', $requested, $authority ) ) {
			$requested = substr( $requested, strlen( $authority[0] ) );
		}

		$path = (string) preg_replace_callback(
			'~[^A-Za-z0-9\-._\~!$&\'()*+,;=:@/%]|%(?![0-9A-Fa-f]{2})~',
			static function ( array $character ): string {
				return rawurlencode( $character[0] );
			},
			substr( $requested, 0, strcspn( $requested, '?#' ) )
		);

		return 0 === strpos( $path, '/' ) ? $path : '/' . $path;
	}
}
