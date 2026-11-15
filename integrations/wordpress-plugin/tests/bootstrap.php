<?php
/**
 * Loads the plugin's classes for unit tests, which run without WordPress.
 *
 * WordPress's functions are replaced test by test with Brain Monkey. The constants the plugin
 * reads from WordPress are defined here, with the values WordPress gives them.
 *
 * @package Dewiride_Analytics
 */

// phpcs:disable WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedConstantFound -- WordPress's own constants, defined as WordPress defines them.
define( 'ABSPATH', dirname( __DIR__ ) . '/' );
define( 'HOUR_IN_SECONDS', 3600 );
define( 'WP_UNINSTALL_PLUGIN', 'dewiride-analytics/dewiride-analytics.php' );
// phpcs:enable

define( 'DEWIRIDE_ANALYTICS_VERSION', 'test' );

require_once dirname( __DIR__ ) . '/vendor/autoload.php';
require_once dirname( __DIR__ ) . '/includes/class-private-host.php';
require_once dirname( __DIR__ ) . '/includes/class-settings.php';
require_once dirname( __DIR__ ) . '/includes/class-audience.php';
require_once dirname( __DIR__ ) . '/includes/class-tracker-tag.php';
require_once dirname( __DIR__ ) . '/includes/class-no-script-image.php';
require_once dirname( __DIR__ ) . '/includes/class-optimiser-exclusions.php';
require_once dirname( __DIR__ ) . '/includes/class-tag-check.php';
require_once dirname( __DIR__ ) . '/includes/class-settings-page.php';
require_once dirname( __DIR__ ) . '/includes/class-plugin.php';
require_once __DIR__ . '/Unit/class-fake-roles.php';
require_once __DIR__ . '/Unit/class-test-case.php';
