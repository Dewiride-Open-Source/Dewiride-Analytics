<?php
/**
 * Sends the server's reports to the smoke harness's stand-in collector, refuses a request the way
 * a security plugin would when the harness asks it to, and adds another plugin's timing to a page.
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

/**
 * Answers 403 and ends the request when the harness asks a firewall to refuse it at this moment.
 *
 * A request asked for with `smoke_firewall` names the moment: `plugin_loaded`, once the plugin
 * under test has loaded and before WordPress's pluggable functions have, as a firewall that runs
 * while its own plugin loads refuses; or `init`, as WordPress starts.
 *
 * @param string $moment The moment the request has reached.
 */
function dewiride_analytics_smoke_firewall( string $moment ): void {
	$asked = filter_input( INPUT_GET, 'smoke_firewall' );

	if ( is_string( $asked ) && sanitize_key( $asked ) === $moment ) {
		status_header( 403 );
		exit;
	}
}

add_action(
	'plugin_loaded',
	static function ( string $plugin ): void {
		if ( 'dewiride-analytics.php' === basename( $plugin ) ) {
			dewiride_analytics_smoke_firewall( 'plugin_loaded' );
		}
	}
);

add_action(
	'init',
	static function (): void {
		dewiride_analytics_smoke_firewall( 'init' );
	},
	0
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

// A request carrying `X-Smoke-Timing` is answered with a timing of another plugin's, as a
// performance plugin adds one, so the harness can check that a redirect keeps it.
add_action(
	'send_headers',
	static function (): void {
		if ( isset( $_SERVER['HTTP_X_SMOKE_TIMING'] ) ) {
			header( 'Server-Timing: other;dur=1', false );
		}
	}
);
