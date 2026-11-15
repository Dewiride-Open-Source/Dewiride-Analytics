<?php
/**
 * Sends the server's reports to the smoke harness's stand-in collector, and refuses a page the way
 * a security plugin would when the harness asks it to.
 *
 * Browsers would load the tag from the saved address; WordPress reaches the stand-in by a name only
 * the harness's network knows, which is what the plugin's filter for the server's address is for.
 * Mounted into the WordPress under test as a must-use plugin, so it runs before anything else.
 *
 * @package Dewiride_Analytics
 */

add_filter(
	'dewiride_analytics_server_address',
	static function (): string {
		return 'http://collector:8080';
	}
);

// A page asked for with `smoke_refuse` in its query is answered 403 once WordPress has run the
// query, as a security plugin refuses a request, so the harness can check that what the site
// refused is reported however the page was asked for.
add_action(
	'template_redirect',
	static function (): void {
		if ( isset( $_GET['smoke_refuse'] ) ) { // phpcs:ignore WordPress.Security.NonceVerification.Recommended -- The harness's own switch, standing in for a security plugin's rule.
			status_header( 403 );
			exit;
		}
	},
	0
);
