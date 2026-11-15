<?php
/**
 * What every unit test starts from.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey;
use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Settings;
use Mockery\Adapter\Phpunit\MockeryPHPUnitIntegration;
use PHPUnit\Framework\TestCase;

/**
 * Sets Brain Monkey up and down around each test, and stubs the WordPress functions whose
 * behaviour no test is about.
 */
abstract class Test_Case extends TestCase {

	use MockeryPHPUnitIntegration;

	/**
	 * A Website ID as the dashboard shows it.
	 */
	protected const SITE_ID = '3f2b8c1e-4d5a-4b6c-9e7f-0a1b2c3d4e5f';

	/**
	 * An analytics address as the settings page saves it.
	 */
	protected const ADDRESS = 'https://analytics.example.com';

	/**
	 * What the web server said about the request before the test described its own, or nothing
	 * when the test described none.
	 *
	 * @var array<mixed>|null
	 */
	private $server_before;

	/**
	 * Starts Brain Monkey and stubs translation, escaping and the URL helpers.
	 */
	protected function setUp(): void {
		parent::setUp();
		Monkey\setUp();
		Functions\stubTranslationFunctions();
		Functions\stubEscapeFunctions();
		Functions\when( 'wp_parse_url' )->alias( 'parse_url' );
		Functions\when( 'wp_unslash' )->alias( 'stripslashes' );
		Functions\when( 'wp_json_encode' )->alias( 'json_encode' );
		Functions\when( 'wp_check_invalid_utf8' )->alias(
			static function ( string $text ): string {
				return mb_check_encoding( $text, 'UTF-8' ) ? $text : '';
			}
		);
		Functions\when( 'home_url' )->justReturn( 'https://example.com' );
		Functions\when( 'is_multisite' )->justReturn( false );
		Functions\when( 'wp_salt' )->justReturn( 'the site\'s secret keys' );
		Functions\when( 'wp_get_environment_type' )->justReturn( 'production' );
		Functions\when( 'wp_validate_boolean' )->alias(
			static function ( $value ): bool {
				if ( is_bool( $value ) ) {
					return $value;
				}

				return ! ( is_string( $value ) && 'false' === strtolower( $value ) ) && (bool) $value;
			}
		);
		Functions\when( 'maybe_serialize' )->alias(
			static function ( $value ) {
				// phpcs:ignore WordPress.PHP.DiscouragedPHPFunctions.serialize_serialize -- As WordPress's maybe_serialize() stores arrays.
				return is_array( $value ) || is_object( $value ) ? serialize( $value ) : $value;
			}
		);
		Functions\when( 'maybe_unserialize' )->alias(
			static function ( $value ) {
				// phpcs:ignore WordPress.PHP.DiscouragedPHPFunctions.serialize_unserialize -- As WordPress's maybe_unserialize() reads what maybe_serialize() stored; only the test's own values.
				return is_string( $value ) && 1 === preg_match( '/^[aO]:\d+:/', $value ) ? unserialize( $value ) : $value;
			}
		);
	}

	/**
	 * Stops Brain Monkey, checking every expectation the test set, and puts back what the web
	 * server said about the request.
	 */
	protected function tearDown(): void {
		if ( null !== $this->server_before ) {
			$_SERVER = $this->server_before;
		}

		unset( $GLOBALS['wpdb'] );
		Monkey\tearDown();
		parent::tearDown();
	}

	/**
	 * Makes a stand-in WordPress's database for the rest of the test.
	 *
	 * @param Fake_Database $database The stand-in.
	 */
	protected static function given_database( Fake_Database $database ): void {
		$GLOBALS['wpdb'] = $database; // phpcs:ignore WordPress.WP.GlobalVariablesOverride.Prohibited -- A test's stand-in, in place of the connection WordPress would make.
	}

	/**
	 * What nested arrays hold under the given keys, or nothing when any of them is missing.
	 *
	 * @param mixed      $value   The outermost value.
	 * @param int|string ...$keys The keys, outermost first.
	 * @return mixed
	 */
	protected static function dig( $value, ...$keys ) {
		foreach ( $keys as $key ) {
			if ( ! is_array( $value ) || ! array_key_exists( $key, $value ) ) {
				return null;
			}

			$value = $value[ $key ];
		}

		return $value;
	}

	/**
	 * The text nested arrays hold under the given keys, or an empty string.
	 *
	 * @param mixed      $value   The outermost value.
	 * @param int|string ...$keys The keys, outermost first.
	 */
	protected static function dig_text( $value, ...$keys ): string {
		$found = self::dig( $value, ...$keys );

		return is_string( $found ) ? $found : '';
	}

	/**
	 * A value that should be an array, or an empty one when it is not.
	 *
	 * @param mixed $value The value.
	 * @return array<mixed>
	 */
	protected static function as_array( $value ): array {
		return is_array( $value ) ? $value : array();
	}

	/**
	 * Stands in WordPress's readers of a response, for responses a test describes as plain arrays
	 * holding `response.code`, `body` and `headers`.
	 */
	protected function given_responses_as_arrays(): void {
		Functions\when( 'wp_remote_retrieve_response_code' )->alias(
			static function ( $response ): int {
				$code = self::dig( $response, 'response', 'code' );

				return is_int( $code ) ? $code : 0;
			}
		);
		Functions\when( 'wp_remote_retrieve_body' )->alias(
			static function ( $response ): string {
				return self::dig_text( $response, 'body' );
			}
		);
		Functions\when( 'wp_remote_retrieve_header' )->alias(
			static function ( $response, string $name ): string {
				return self::dig_text( $response, 'headers', $name );
			}
		);
	}

	/**
	 * Makes the request WordPress is answering read as given: what the web server says about it
	 * and the headers it carried, by their names in `$_SERVER`. Whatever the test does not name is
	 * left out, as a request that did not send it.
	 *
	 * Text is slashed as WordPress slashes `$_SERVER` once plugins have loaded, and WordPress is
	 * said to have done so, so that a quotation mark or a backslash a test sends reaches the plugin
	 * exactly as a visitor's would. A request refused while plugins were still loading never got
	 * that far, and is given as it is.
	 *
	 * @param array<string, mixed> $server  What the web server says, such as `REMOTE_ADDR`.
	 * @param bool                 $slashed Whether WordPress has slashed it yet.
	 */
	protected function given_request( array $server, bool $slashed = true ): void {
		if ( null === $this->server_before ) {
			$this->server_before = $_SERVER; // phpcs:ignore WordPress.Security.ValidatedSanitizedInput.InputNotSanitized -- Kept whole only to be put back after the test.
		}

		foreach ( array_keys( $_SERVER ) as $key ) {
			if ( is_string( $key ) && ( 0 === strpos( $key, 'HTTP_' ) || in_array( $key, array( 'REMOTE_ADDR', 'REQUEST_METHOD', 'REQUEST_URI', 'SERVER_ADDR', 'QUERY_STRING' ), true ) ) ) {
				unset( $_SERVER[ $key ] );
			}
		}

		foreach ( $server as $key => $value ) {
			$_SERVER[ $key ] = $slashed && is_string( $value ) ? addslashes( $value ) : $value;
		}

		if ( $slashed && 0 === did_action( 'sanitize_comment_cookies' ) ) {
			do_action( 'sanitize_comment_cookies' ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedHooknameFound -- WordPress's own action, fired as WordPress fires it once the request is slashed.
		}
	}

	/**
	 * Makes the saved settings read as given.
	 *
	 * @param string   $address       The analytics address.
	 * @param string   $site_id       The Website ID.
	 * @param string[] $counted_roles The roles whose signed-in members are counted.
	 */
	protected function given_settings( string $address = self::ADDRESS, string $site_id = self::SITE_ID, array $counted_roles = array() ): void {
		Functions\when( 'get_option' )->alias(
			static function ( $option ) use ( $address, $site_id, $counted_roles ) {
				return Settings::OPTION === $option
					? array(
						'address'       => $address,
						'site_id'       => $site_id,
						'counted_roles' => $counted_roles,
					)
					: false;
			}
		);
	}

	/**
	 * Makes the site's roles the given ones.
	 *
	 * @param array<string, string> $names Each role's slug and display name.
	 */
	protected function given_roles( array $names ): void {
		Functions\when( 'wp_roles' )->justReturn( new Fake_Roles( $names ) );
	}
}
