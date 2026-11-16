<?php
/**
 * The key the site's own server reports with.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

use Exception;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Keeps the server key, and gives it back only to whatever sends a report.
 *
 * A key lets whoever holds it report visits to the website it belongs to, so it is kept out of
 * reach of anything that reads the database. On a single site it can live in `wp-config.php`,
 * where it wins over a saved one. Otherwise it is saved sealed under the site's own secret keys —
 * `AUTH_KEY` and `AUTH_SALT` in `wp-config.php` — so a copy of the database alone does not give
 * it away, and is never loaded with every page. A site that never set those keys has WordPress keep
 * them in the database instead, where the seal guards no more than the database does. A network's
 * sites share one `wp-config.php`, so there only a saved key counts.
 *
 * @phpstan-type Found array{key: string, source: string}
 */
final class Server_Key {

	/**
	 * The option holding the sealed key.
	 */
	public const OPTION = 'dewiride_analytics_server_key';

	/**
	 * The constant `wp-config.php` may define the key in.
	 */
	public const CONSTANT = 'DEWIRIDE_ANALYTICS_SERVER_KEY';

	/**
	 * The key is the one `wp-config.php` defines.
	 */
	public const FROM_CONFIG = 'config';

	/**
	 * The key is the one saved on the settings page.
	 */
	public const SAVED = 'saved';

	/**
	 * A key is saved, but it no longer opens: the site's secret keys have changed since.
	 */
	public const UNREADABLE = 'unreadable';

	/**
	 * `wp-config.php` defines the key, but not as a key Dewiride Analytics issues.
	 */
	public const MALFORMED_IN_CONFIG = 'malformed-in-config';

	/**
	 * There is no key.
	 */
	public const NONE = 'none';

	/**
	 * A key as Dewiride Analytics issues it: `dwk_` and 43 characters of URL-safe base64.
	 */
	private const PATTERN = '/^dwk_[A-Za-z0-9_-]{43}$/';

	/**
	 * How many of the key's last characters are shown, as the dashboard shows them.
	 */
	private const ENDING = 4;

	/**
	 * The key and where it came from, once read.
	 *
	 * @var Found|null
	 */
	private $found;

	/**
	 * Whether a text is shaped like a key Dewiride Analytics issues.
	 *
	 * @param string $key The text.
	 */
	public static function looks_valid( string $key ): bool {
		return 1 === preg_match( self::PATTERN, $key );
	}

	/**
	 * Whether the key is the one `wp-config.php` defines, which it is whenever the constant is
	 * defined on a single site.
	 */
	public static function is_in_config(): bool {
		return ! is_multisite() && defined( self::CONSTANT );
	}

	/**
	 * The last characters of a key, which tell keys apart without giving one away.
	 *
	 * @param string $key The key.
	 */
	public static function ending_of( string $key ): string {
		return substr( $key, -self::ENDING );
	}

	/**
	 * The key reports are sent with, or an empty string when there is none to send.
	 */
	public function key(): string {
		return $this->found()['key'];
	}

	/**
	 * Where the key came from: one of the constants above.
	 */
	public function source(): string {
		return $this->found()['source'];
	}

	/**
	 * Saves a key, sealed, replacing any saved before.
	 *
	 * @param string $key A key shaped like one Dewiride Analytics issues.
	 * @return bool Whether it was saved: sealing needs randomness, which a server can fail to give.
	 */
	public function save( string $key ): bool {
		try {
			$sealed = self::seal( $key );
		} catch ( Exception $failure ) {
			return false;
		}

		update_option( self::OPTION, $sealed, false );
		$this->found = null;

		return true;
	}

	/**
	 * Removes the saved key.
	 */
	public function forget(): void {
		delete_option( self::OPTION );
		$this->found = null;
	}

	/**
	 * The key and where it came from, read once.
	 *
	 * @return Found
	 */
	private function found(): array {
		if ( null === $this->found ) {
			$this->found = self::look_up();
		}

		return $this->found;
	}

	/**
	 * Reads the key from `wp-config.php` or, failing that, opens the saved one.
	 *
	 * @return Found
	 */
	private static function look_up(): array {
		if ( self::is_in_config() ) {
			$key = constant( self::CONSTANT );

			return is_string( $key ) && self::looks_valid( $key )
				? self::found_as( $key, self::FROM_CONFIG )
				: self::found_as( '', self::MALFORMED_IN_CONFIG );
		}

		$sealed = get_option( self::OPTION, '' );

		if ( ! is_string( $sealed ) || '' === $sealed ) {
			return self::found_as( '', self::NONE );
		}

		$key = self::open( $sealed );

		return self::looks_valid( $key ) ? self::found_as( $key, self::SAVED ) : self::found_as( '', self::UNREADABLE );
	}

	/**
	 * A key and where it came from.
	 *
	 * @param string $key    The key, or an empty string.
	 * @param string $source Where it came from.
	 * @return Found
	 */
	private static function found_as( string $key, string $source ): array {
		return array(
			'key'    => $key,
			'source' => $source,
		);
	}

	/**
	 * Seals a key under the site's secret keys, with a nonce of its own, as hexadecimal text.
	 *
	 * @param string $key The key.
	 * @throws Exception When the server has no randomness to give.
	 */
	private static function seal( string $key ): string {
		$nonce = random_bytes( SODIUM_CRYPTO_SECRETBOX_NONCEBYTES );

		return bin2hex( $nonce . sodium_crypto_secretbox( $key, $nonce, self::sealing_key() ) );
	}

	/**
	 * Opens a sealed key, or gives an empty string when it does not open: it was sealed under
	 * secret keys the site no longer has, or it is not a sealed key at all.
	 *
	 * @param string $sealed The sealed key, as saved.
	 */
	private static function open( string $sealed ): string {
		$bytes = 1 === preg_match( '/^(?:[0-9a-f]{2})+$/', $sealed ) ? (string) hex2bin( $sealed ) : '';

		if ( strlen( $bytes ) <= SODIUM_CRYPTO_SECRETBOX_NONCEBYTES ) {
			return '';
		}

		try {
			$opened = sodium_crypto_secretbox_open(
				substr( $bytes, SODIUM_CRYPTO_SECRETBOX_NONCEBYTES ),
				substr( $bytes, 0, SODIUM_CRYPTO_SECRETBOX_NONCEBYTES ),
				self::sealing_key()
			);
		} catch ( Exception $failure ) {
			return '';
		}

		return is_string( $opened ) ? $opened : '';
	}

	/**
	 * The key a server key is sealed under: derived from the site's own secret keys, so it is
	 * written nowhere and changes when they do.
	 */
	private static function sealing_key(): string {
		return hash_hmac( 'sha256', 'dewiride-analytics-server-key', wp_salt( 'auth' ), true );
	}
}
