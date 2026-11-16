<?php
/**
 * Tests for how the plugin is put together.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Dewiride\Analytics\WordPress\Connection_Check;
use Dewiride\Analytics\WordPress\Plugin;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tag_Check;

/**
 * Which hooks each side of WordPress gets, and what activation and deactivation do.
 */
final class Plugin_Test extends Test_Case {

	/**
	 * The plugin's basename.
	 */
	private const BASENAME = 'dewiride-analytics/dewiride-analytics.php';

	/**
	 * A page a visitor reads gets the tag, the image and the exclusions, and no settings page.
	 */
	public function test_a_visitors_page_gets_the_tag_and_not_the_settings_page(): void {
		Functions\when( 'is_admin' )->justReturn( false );

		Plugin::boot( self::BASENAME );

		self::assertTrue( has_action( 'wp_enqueue_scripts' ) );
		self::assertTrue( has_filter( 'wp_script_attributes' ) );
		self::assertTrue( has_action( 'wp_body_open' ) );
		self::assertTrue( has_action( 'wp_footer' ) );
		self::assertTrue( has_filter( 'rocket_delay_js_exclusions' ) );
		self::assertTrue( has_action( Tag_Check::EVENT ) );
		self::assertTrue( has_action( Cloudflare_Ranges::EVENT ) );
		self::assertTrue( has_action( 'wp' ) );
		self::assertTrue( has_action( 'shutdown' ) );
		self::assertFalse( has_action( 'admin_menu' ) );
		self::assertFalse( has_action( 'admin_init' ) );
		self::assertFalse( has_action( 'admin_post_' . Connection_Check::ACTION ) );
	}

	/**
	 * The dashboard gets the settings page, the setting and the button, and no tag.
	 */
	public function test_the_dashboard_gets_the_settings_page_and_not_the_tag(): void {
		Functions\when( 'is_admin' )->justReturn( true );

		Plugin::boot( self::BASENAME );

		self::assertTrue( has_action( 'admin_menu' ) );
		self::assertTrue( has_action( 'admin_init' ) );
		self::assertTrue( has_action( 'admin_post_' . Tag_Check::ACTION ) );
		self::assertTrue( has_action( 'admin_post_' . Connection_Check::ACTION ) );
		self::assertTrue( has_filter( 'plugin_action_links_' . self::BASENAME ) );
		self::assertTrue( has_action( Tag_Check::EVENT ) );
		self::assertTrue( has_action( Cloudflare_Ranges::EVENT ) );
		self::assertFalse( has_action( 'wp_enqueue_scripts' ) );
		self::assertFalse( has_filter( 'wp_script_attributes' ) );
		self::assertFalse( has_action( 'send_headers' ) );
		self::assertFalse( has_action( 'shutdown' ) );
	}

	/**
	 * Activation creates the settings and schedules the daily check.
	 */
	public function test_activation_creates_the_settings_and_schedules_the_check(): void {
		Functions\when( 'wp_next_scheduled' )->justReturn( false );
		Functions\expect( 'add_option' )->once()->with( Settings::OPTION, Settings::defaults(), '', true );
		Functions\expect( 'wp_schedule_event' )->once();

		Plugin::activate();
	}

	/**
	 * Deactivation removes the daily check and the weekly fetch, and keeps the settings.
	 */
	public function test_deactivation_removes_the_events_and_keeps_the_settings(): void {
		Functions\expect( 'wp_clear_scheduled_hook' )->once()->with( Tag_Check::EVENT );
		Functions\expect( 'wp_clear_scheduled_hook' )->once()->with( Cloudflare_Ranges::EVENT );
		Functions\expect( 'delete_option' )->never();

		Plugin::deactivate();
	}
}
