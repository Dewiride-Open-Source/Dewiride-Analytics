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
 * The settings page exists only in the dashboard, and the tag and the server's reports only on
 * the requests visitors make, so a request loads the hooks for one side and never the other's.
 */
final class Plugin {

	/**
	 * Hooks the plugin into the current request.
	 *
	 * @param string $basename The plugin's basename, as WordPress names it on the Plugins screen.
	 */
	public static function boot( string $basename ): void {
		$settings   = new Settings();
		$tag_check  = new Tag_Check( $settings );
		$cloudflare = new Cloudflare_Ranges();
		$server_key = new Server_Key();
		$status     = new Reporting_Status( $settings );
		$client     = new Collector_Client( $settings, $server_key, $status );
		$audience   = new Audience( $settings );
		$visitor    = new Visitor_Address( $settings, $cloudflare );

		$tag_check->register();
		$cloudflare->register();

		if ( is_admin() ) {
			$connection_check = new Connection_Check( $settings, $server_key, $client, $status, $tag_check );

			$settings->register();
			$tag_check->register_admin();
			$connection_check->register_admin();
			( new Privacy_Policy() )->register();
			( new Settings_Page( $settings, $tag_check, $basename, new Server_Settings( $settings, $server_key, $status, $connection_check, $tag_check, $visitor ) ) )->register();

			return;
		}

		( new Tracker_Tag( $settings, $audience ) )->register();
		( new No_Script_Image( $settings, $audience ) )->register();
		( new Optimiser_Exclusions( $settings ) )->register();
		( new Server_Reporting( $settings, $server_key, $status, new Request_Report( $settings, $audience, $visitor ), $client, $tag_check ) )->register();
	}

	/**
	 * Creates the settings and schedules the daily tag check.
	 */
	public static function activate(): void {
		Settings::create();
		Tag_Check::schedule();
	}

	/**
	 * Removes the daily tag check and the weekly fetch of Cloudflare's addresses. The settings
	 * stay until the plugin is deleted.
	 */
	public static function deactivate(): void {
		Tag_Check::unschedule();
		Cloudflare_Ranges::unschedule();
	}
}
