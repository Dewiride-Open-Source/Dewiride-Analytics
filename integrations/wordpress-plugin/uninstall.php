<?php
/**
 * Removes what the plugin stored, when it is deleted from the Plugins screen.
 *
 * Its settings, the server key, how the server's reports were going, the last checks of the tag
 * and of the connection, Cloudflare's addresses, and the events that refresh them, on every site
 * where the plugin could have been used. Nothing is sent anywhere.
 *
 * @package Dewiride_Analytics
 */

use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Dewiride\Analytics\WordPress\Connection_Check;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Server_Key;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tag_Check;

if ( ! defined( 'WP_UNINSTALL_PLUGIN' ) ) {
	exit;
}

require_once __DIR__ . '/includes/class-settings.php';
require_once __DIR__ . '/includes/class-tag-check.php';
require_once __DIR__ . '/includes/class-server-key.php';
require_once __DIR__ . '/includes/class-reporting-status.php';
require_once __DIR__ . '/includes/class-connection-check.php';
require_once __DIR__ . '/includes/class-cloudflare-ranges.php';

$dewiride_analytics_forget = static function (): void {
	delete_option( Settings::OPTION );
	delete_option( Tag_Check::OPTION );
	delete_option( Server_Key::OPTION );
	delete_option( Reporting_Status::OPTION );
	delete_option( Connection_Check::OPTION );
	delete_option( Cloudflare_Ranges::OPTION );
	wp_clear_scheduled_hook( Tag_Check::EVENT );
	wp_clear_scheduled_hook( Cloudflare_Ranges::EVENT );
};

if ( ! is_multisite() ) {
	$dewiride_analytics_forget();

	return;
}

// Every site on the network is visited in turn, which is heavy on a very large network.
$dewiride_analytics_site_ids = get_sites(
	array(
		'fields' => 'ids',
		'number' => 0,
	)
);

foreach ( $dewiride_analytics_site_ids as $dewiride_analytics_site_id ) {
	switch_to_blog( (int) $dewiride_analytics_site_id );
	$dewiride_analytics_forget();
	restore_current_blog();
}
