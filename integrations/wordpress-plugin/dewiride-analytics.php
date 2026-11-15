<?php
/**
 * Plugin Name:       Dewiride Analytics
 * Plugin URI:        https://analytics.dewiride.com/docs/install#wordpress
 * Description:       Counts the people who read your site with Dewiride Analytics, and reports the search engines, AI crawlers and other robots that visit it.
 * Version:           1.1.0
 * Requires at least: 6.4
 * Requires PHP:      7.4
 * Author:            Dewiride Technologies
 * Author URI:        https://analytics.dewiride.com/
 * License:           MIT
 * License URI:       https://opensource.org/license/mit
 * Text Domain:       dewiride-analytics
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

define( 'DEWIRIDE_ANALYTICS_VERSION', '1.1.0' );

require_once __DIR__ . '/includes/class-private-host.php';
require_once __DIR__ . '/includes/class-request-headers.php';
require_once __DIR__ . '/includes/class-address-range.php';
require_once __DIR__ . '/includes/class-own-request.php';
require_once __DIR__ . '/includes/class-server-key.php';
require_once __DIR__ . '/includes/class-settings.php';
require_once __DIR__ . '/includes/class-audience.php';
require_once __DIR__ . '/includes/class-tracker-tag.php';
require_once __DIR__ . '/includes/class-no-script-image.php';
require_once __DIR__ . '/includes/class-optimiser-exclusions.php';
require_once __DIR__ . '/includes/class-tag-check.php';
require_once __DIR__ . '/includes/class-cloudflare-ranges.php';
require_once __DIR__ . '/includes/class-visitor-address.php';
require_once __DIR__ . '/includes/class-stored-option.php';
require_once __DIR__ . '/includes/class-reporting-status.php';
require_once __DIR__ . '/includes/class-collector-client.php';
require_once __DIR__ . '/includes/class-request-report.php';
require_once __DIR__ . '/includes/class-server-reporting.php';
require_once __DIR__ . '/includes/class-connection-check.php';
require_once __DIR__ . '/includes/class-privacy-policy.php';
require_once __DIR__ . '/includes/class-server-settings.php';
require_once __DIR__ . '/includes/class-settings-page.php';
require_once __DIR__ . '/includes/class-plugin.php';

register_activation_hook( __FILE__, array( Plugin::class, 'activate' ) );
register_deactivation_hook( __FILE__, array( Plugin::class, 'deactivate' ) );

Plugin::boot( plugin_basename( __FILE__ ) );
