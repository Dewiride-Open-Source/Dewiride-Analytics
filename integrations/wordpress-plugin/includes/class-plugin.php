<?php
/**
 * Puts the plugin together.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Creates the plugin's parts and hooks each into the requests it belongs to.
 *
 * The settings page exists only in the dashboard and the tag only on the pages visitors read, so
 * a request loads the hooks for one side and never the other's.
 */
final class Plugin {

	/**
	 * Hooks the plugin into the current request.
	 *
	 * @param string $basename The plugin's basename, as WordPress names it on the Plugins screen.
	 */
	public static function boot( string $basename ): void {
		$settings  = new Settings();
		$tag_check = new Tag_Check( $settings );

		$tag_check->register();

		if ( is_admin() ) {
			$settings->register();
			$tag_check->register_admin();
			( new Settings_Page( $settings, $tag_check, $basename ) )->register();

			return;
		}

		$audience = new Audience( $settings );

		( new Tracker_Tag( $settings, $audience ) )->register();
		( new No_Script_Image( $settings, $audience ) )->register();
		( new Optimiser_Exclusions( $settings ) )->register();
	}

	/**
	 * Creates the settings and schedules the daily tag check.
	 */
	public static function activate(): void {
		Settings::create();
		Tag_Check::schedule();
	}

	/**
	 * Removes the daily tag check. The settings stay until the plugin is deleted.
	 */
	public static function deactivate(): void {
		Tag_Check::unschedule();
	}
}
