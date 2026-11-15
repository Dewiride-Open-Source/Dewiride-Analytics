<?php
/**
 * Tests for what deleting the plugin removes.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tag_Check;

/**
 * Deleting the plugin removes everything it stored, on every site, and sends nothing anywhere.
 */
final class Uninstall_Test extends Test_Case {

	/**
	 * Starts each test with no request leaving the site.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\expect( 'wp_remote_get' )->never();
		Functions\expect( 'wp_remote_post' )->never();
		Functions\expect( 'wp_remote_request' )->never();
	}

	/**
	 * On a single site, both options and the daily event are removed.
	 */
	public function test_a_single_site_forgets_everything(): void {
		Functions\when( 'is_multisite' )->justReturn( false );
		Functions\expect( 'delete_option' )->once()->with( Settings::OPTION );
		Functions\expect( 'delete_option' )->once()->with( Tag_Check::OPTION );
		Functions\expect( 'wp_clear_scheduled_hook' )->once()->with( Tag_Check::EVENT );
		Functions\expect( 'switch_to_blog' )->never();

		$this->uninstall();
	}

	/**
	 * On a network, every site forgets everything, each in turn.
	 */
	public function test_every_site_on_a_network_forgets_everything(): void {
		Functions\when( 'is_multisite' )->justReturn( true );
		Functions\expect( 'get_sites' )
			->once()
			->with(
				array(
					'fields' => 'ids',
					'number' => 0,
				)
			)
			->andReturn( array( 1, 2, 3 ) );
		Functions\expect( 'switch_to_blog' )->times( 3 );
		Functions\expect( 'restore_current_blog' )->times( 3 );
		Functions\expect( 'delete_option' )->times( 3 )->with( Settings::OPTION );
		Functions\expect( 'delete_option' )->times( 3 )->with( Tag_Check::OPTION );
		Functions\expect( 'wp_clear_scheduled_hook' )->times( 3 )->with( Tag_Check::EVENT );

		$this->uninstall();
	}

	/**
	 * Runs the plugin's uninstall file, as WordPress does when the plugin is deleted.
	 */
	private function uninstall(): void {
		include dirname( __DIR__, 2 ) . '/uninstall.php';
	}
}
