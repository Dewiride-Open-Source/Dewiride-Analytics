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
	 * @param string $key     The key, such as `REMOTE_ADDR`.
	 * @param int    $longest The most characters kept.
	 */
	public static function server( string $key, int $longest = self::LONGEST ): string {
		$raw = self::raw( $key );

		if ( null === $raw ) {
			return '';
		}

		$value = filter_var( $raw, FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );

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
		$raw = self::raw( 'HTTP_' . strtoupper( str_replace( '-', '_', $name ) ) );

		if ( null === $raw ) {
			return '';
		}

		$value = (string) filter_var( $raw, FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );

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
	 * not start one, so the address is one any reader accepts. Square brackets are the exception:
	 * browsers send them as they are and the tracker reports them so, and both halves must name a
	 * page alike to count it once. A request naming a whole address rather than a path, as one
	 * sent to a proxy does, is read for its path.
	 */
	public static function requested_path(): string {
		$raw = self::raw( 'REQUEST_URI' );

		if ( null === $raw ) {
			return '/';
		}

		$requested = (string) filter_var( $raw, FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );

		if ( 1 === preg_match( '~^[a-z][a-z0-9+.-]*://[^/?#]*~i', $requested, $authority ) ) {
			$requested = substr( $requested, strlen( $authority[0] ) );
		}

		$path = (string) preg_replace_callback(
			'~[^A-Za-z0-9\-._\~!$&\'()*+,;=:@/%\[\]]|%(?![0-9A-Fa-f]{2})~',
			static function ( array $character ): string {
				return rawurlencode( $character[0] );
			},
			substr( $requested, 0, strcspn( $requested, '?#' ) )
		);

		return 0 === strpos( $path, '/' ) ? $path : '/' . $path;
	}

	/**
	 * What `$_SERVER` holds under a key as the web server put it there, or nothing when it holds
	 * no text there.
	 *
	 * WordPress adds slashes to `$_SERVER`, as it does to a form's fields, once plugins have
	 * loaded (`wp_magic_quotes()`, just before `sanitize_comment_cookies`), so from then on they are
	 * taken off. A request a security plugin refused while plugins were still loading never got
	 * that far, and is read as it is, or a backslash its sender wrote would be lost.
	 *
	 * @param string $key The key, such as `REMOTE_ADDR`.
	 */
	private static function raw( string $key ): ?string {
		if ( ! isset( $_SERVER[ $key ] ) || ! is_string( $_SERVER[ $key ] ) ) {
			return null;
		}

		// phpcs:ignore WordPress.Security.ValidatedSanitizedInput.MissingUnslash, WordPress.Security.ValidatedSanitizedInput.InputNotSanitized -- Unslashed below once WordPress has slashed it; every caller then strips control characters and keeps the rest as evidence.
		$value = $_SERVER[ $key ];

		return did_action( 'sanitize_comment_cookies' ) > 0 ? (string) wp_unslash( $value ) : $value;
	}
}
