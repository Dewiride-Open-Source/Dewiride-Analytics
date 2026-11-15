<?php
/**
 * Tests for keeping the server key.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Server_Key;

/**
 * The key is kept sealed under the site's secret keys, out of the options loaded with every page,
 * and given back only as it was saved.
 */
final class Server_Key_Test extends Test_Case {

	/**
	 * A key shaped as Dewiride Analytics issues them. Made of one repeated character, so it is
	 * plainly not a real one.
	 */
	private const KEY = 'dwk_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';

	/**
	 * What the option holds in each test.
	 *
	 * @var mixed
	 */
	private $saved = false;

	/**
	 * Starts each test with nothing saved, and keeps what a test saves.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\when( 'get_option' )->alias(
			function ( string $option, $fallback = false ) {
				return Server_Key::OPTION === $option && false !== $this->saved ? $this->saved : $fallback;
			}
		);
		Functions\when( 'update_option' )->alias(
			function ( string $option, $value, $autoload ): bool {
				self::assertSame( Server_Key::OPTION, $option );
				self::assertFalse( $autoload );
				$this->saved = $value;

				return true;
			}
		);
		Functions\when( 'delete_option' )->alias(
			function (): bool {
				$this->saved = false;

				return true;
			}
		);
	}

	/**
	 * Only the shape Dewiride Analytics issues is a key.
	 */
	public function test_only_the_shape_dewiride_analytics_issues_is_a_key(): void {
		self::assertTrue( Server_Key::looks_valid( self::KEY ) );
		self::assertFalse( Server_Key::looks_valid( substr( self::KEY, 0, -1 ) ) );
		self::assertFalse( Server_Key::looks_valid( self::KEY . 'a' ) );
		self::assertFalse( Server_Key::looks_valid( 'dwk_' . str_repeat( '+', 43 ) ) );
		self::assertFalse( Server_Key::looks_valid( ' ' . self::KEY ) );
		self::assertSame( 'aaaa', Server_Key::ending_of( self::KEY ) );
	}

	/**
	 * Without a key, there is none to send.
	 */
	public function test_nothing_saved_is_no_key(): void {
		$key = new Server_Key();

		self::assertSame( '', $key->key() );
		self::assertSame( Server_Key::NONE, $key->source() );
	}

	/**
	 * A saved key is sealed, never kept as written, and opens to the key again.
	 */
	public function test_a_saved_key_is_sealed_and_opens_again(): void {
		self::assertTrue( ( new Server_Key() )->save( self::KEY ) );

		self::assertIsString( $this->saved );
		self::assertMatchesRegularExpression( '/^[0-9a-f]+$/', (string) $this->saved );

		$key = new Server_Key();

		self::assertSame( self::KEY, $key->key() );
		self::assertSame( Server_Key::SAVED, $key->source() );
	}

	/**
	 * Sealing the same key twice gives two different seals.
	 */
	public function test_each_seal_is_different(): void {
		$key = new Server_Key();

		$key->save( self::KEY );
		$first = $this->saved;
		$key->save( self::KEY );

		self::assertNotSame( $first, $this->saved );
	}

	/**
	 * Once the site's secret keys change, a saved key no longer opens.
	 */
	public function test_a_key_sealed_under_other_secret_keys_does_not_open(): void {
		( new Server_Key() )->save( self::KEY );
		Functions\when( 'wp_salt' )->justReturn( 'new secret keys' );

		$key = new Server_Key();

		self::assertSame( '', $key->key() );
		self::assertSame( Server_Key::UNREADABLE, $key->source() );
	}

	/**
	 * Whatever else the option holds is no key.
	 *
	 * @return array<string, array{mixed}>
	 */
	public function damaged(): array {
		return array(
			'text that is not hexadecimal' => array( 'not a seal' ),
			'too short to hold a nonce'    => array( bin2hex( 'short' ) ),
			'a nonce and no key'           => array( str_repeat( '00', 40 ) ),
			'something that is not text'   => array( array( self::KEY ) ),
		);
	}

	/**
	 * A damaged option reads as a key that does not open, never as a key.
	 *
	 * @dataProvider damaged
	 *
	 * @param mixed $stored What the option holds.
	 */
	public function test_a_damaged_option_is_no_key( $stored ): void {
		$this->saved = $stored;

		self::assertSame( '', ( new Server_Key() )->key() );
	}

	/**
	 * Removing the key forgets it.
	 */
	public function test_a_removed_key_is_gone(): void {
		$key = new Server_Key();

		$key->save( self::KEY );
		$key->forget();

		self::assertSame( Server_Key::NONE, $key->source() );
	}

	/**
	 * A key in `wp-config.php` wins on a single site.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_key_in_wp_config_wins_on_a_single_site(): void {
		define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', self::KEY );
		$this->saved = 'a seal of another key';

		$key = new Server_Key();

		self::assertTrue( Server_Key::is_in_config() );
		self::assertSame( self::KEY, $key->key() );
		self::assertSame( Server_Key::FROM_CONFIG, $key->source() );
	}

	/**
	 * A key in `wp-config.php` that is not one is said to be so, and sent nowhere.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_malformed_key_in_wp_config_is_no_key(): void {
		define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', 'not a key' );

		$key = new Server_Key();

		self::assertSame( '', $key->key() );
		self::assertSame( Server_Key::MALFORMED_IN_CONFIG, $key->source() );
	}

	/**
	 * On a network, whose sites share one `wp-config.php`, only a saved key counts.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_on_a_network_only_a_saved_key_counts(): void {
		define( 'DEWIRIDE_ANALYTICS_SERVER_KEY', self::KEY );
		Functions\when( 'is_multisite' )->justReturn( true );

		self::assertFalse( Server_Key::is_in_config() );
		self::assertSame( Server_Key::NONE, ( new Server_Key() )->source() );
	}
}
