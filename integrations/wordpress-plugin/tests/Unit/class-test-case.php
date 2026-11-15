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
	 * Starts Brain Monkey and stubs translation, escaping and the URL helpers.
	 */
	protected function setUp(): void {
		parent::setUp();
		Monkey\setUp();
		Functions\stubTranslationFunctions();
		Functions\stubEscapeFunctions();
		Functions\when( 'wp_parse_url' )->alias( 'parse_url' );
		Functions\when( 'wp_unslash' )->alias( 'stripslashes' );
		Functions\when( 'home_url' )->justReturn( 'https://example.com' );
		Functions\when( 'is_multisite' )->justReturn( false );
	}

	/**
	 * Stops Brain Monkey, checking every expectation the test set.
	 */
	protected function tearDown(): void {
		Monkey\tearDown();
		parent::tearDown();
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
