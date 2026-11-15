<?php
/**
 * Keeps optimisation plugins from rewriting the beacon's tag.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Adds the beacon to the exclusion lists of the optimisation plugins that offer one as a filter.
 *
 * A beacon that is combined into another file, deferred or delayed until somebody touches the
 * page stops counting the readers who leave quickly — usually most of them — and an optimiser
 * that copies it onto the site's own host breaks it outright. Every callback adds to the list it
 * is given and keeps everything already in it.
 *
 * What each optimiser matches its entries against decides which form of the beacon's identity it
 * is given: its full address, the path of that address, its script handle or its element id.
 */
final class Optimiser_Exclusions {

	/**
	 * Filters whose entries are matched against a script's address or its whole tag, as a
	 * substring or as a regular expression. Each is given the beacon's full address, which reads
	 * the same either way.
	 *
	 * WP Rocket: `rocket_delay_js_exclusions` (Delay JavaScript execution), `rocket_exclude_defer_js`
	 * (Load JavaScript deferred, which also keeps a script out of combining) and
	 * `rocket_minify_excluded_external_js` (minifying and combining scripts from other hosts).
	 * LiteSpeed Cache: `litespeed_optimize_js_excludes` (minify and combine),
	 * `litespeed_optm_js_defer_exc` (deferred or delayed) and `litespeed_optm_gm_js_exc` (guest
	 * mode). Perfmatters: `perfmatters_delay_js_exclusions` and `perfmatters_defer_js_exclusions`.
	 * Breeze: `default_scripts_gnore_from_delay` (Delay JS, spelled as Breeze spells it).
	 */
	private const BY_ADDRESS = array(
		'rocket_delay_js_exclusions',
		'rocket_exclude_defer_js',
		'rocket_minify_excluded_external_js',
		'litespeed_optimize_js_excludes',
		'litespeed_optm_js_defer_exc',
		'litespeed_optm_gm_js_exc',
		'perfmatters_delay_js_exclusions',
		'perfmatters_defer_js_exclusions',
		'default_scripts_gnore_from_delay',
	);

	/**
	 * Filters whose entries are matched against the path of a file on the site's own host: WP
	 * Rocket's `rocket_exclude_js` (minify and combine), which matters when the analytics
	 * installation shares the site's host.
	 */
	private const BY_PATH = array(
		'rocket_exclude_js',
	);

	/**
	 * Filters whose entries are script handles: SiteGround Speed Optimizer's minify, combine and
	 * asynchronous-loading exclusions.
	 */
	private const BY_HANDLE = array(
		'sgo_js_minify_exclude',
		'sgo_javascript_combine_exclude',
		'sgo_js_async_exclude',
	);

	/**
	 * Filters whose entries are element ids: SiteGround Speed Optimizer's combine exclusion by id,
	 * the only one of its combine exclusions that reaches a script from another host.
	 */
	private const BY_ELEMENT_ID = array(
		'sgo_javascript_combine_exclude_ids',
	);

	/**
	 * Autoptimize's JavaScript exclusions: a comma-separated string, or an array keyed by what is
	 * excluded.
	 */
	private const AUTOPTIMIZE = 'autoptimize_filter_js_exclude';

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * Creates the exclusions over the given settings.
	 *
	 * @param Settings $settings The plugin's settings.
	 */
	public function __construct( Settings $settings ) {
		$this->settings = $settings;
	}

	/**
	 * Hooks every exclusion. Each does nothing until the address is saved.
	 */
	public function register(): void {
		foreach ( self::BY_ADDRESS as $hook ) {
			add_filter( $hook, array( $this, 'exclude_address' ) );
		}

		foreach ( self::BY_PATH as $hook ) {
			add_filter( $hook, array( $this, 'exclude_path' ) );
		}

		foreach ( self::BY_HANDLE as $hook ) {
			add_filter( $hook, array( $this, 'exclude_handle' ) );
		}

		foreach ( self::BY_ELEMENT_ID as $hook ) {
			add_filter( $hook, array( $this, 'exclude_element_id' ) );
		}

		add_filter( self::AUTOPTIMIZE, array( $this, 'exclude_from_autoptimize' ) );
	}

	/**
	 * Adds the beacon's full address to a list.
	 *
	 * @param mixed $exclusions The list as the optimiser and other plugins left it.
	 * @return mixed The list with the beacon's address in it.
	 */
	public function exclude_address( $exclusions ) {
		return $this->settings->is_complete() ? self::with( $exclusions, $this->settings->script_url() ) : $exclusions;
	}

	/**
	 * Adds the path of the beacon's address to a list.
	 *
	 * @param mixed $exclusions The list as the optimiser and other plugins left it.
	 * @return mixed The list with the beacon's path in it.
	 */
	public function exclude_path( $exclusions ) {
		$path = wp_parse_url( $this->settings->script_url(), PHP_URL_PATH );

		return $this->settings->is_complete() && is_string( $path ) ? self::with( $exclusions, $path ) : $exclusions;
	}

	/**
	 * Adds the beacon's script handle to a list.
	 *
	 * @param mixed $exclusions The list as the optimiser and other plugins left it.
	 * @return mixed The list with the beacon's handle in it.
	 */
	public function exclude_handle( $exclusions ) {
		return self::with( $exclusions, Tracker_Tag::HANDLE );
	}

	/**
	 * Adds the `id` of the beacon's tag to a list.
	 *
	 * @param mixed $exclusions The list as the optimiser and other plugins left it.
	 * @return mixed The list with the tag's id in it.
	 */
	public function exclude_element_id( $exclusions ) {
		return self::with( $exclusions, Tracker_Tag::ELEMENT_ID );
	}

	/**
	 * Adds the beacon's address to Autoptimize's exclusions, in whichever form they arrive.
	 *
	 * @param mixed $exclusions A comma-separated string, or an array keyed by what is excluded.
	 * @return mixed The exclusions with the beacon's address in them.
	 */
	public function exclude_from_autoptimize( $exclusions ) {
		if ( ! $this->settings->is_complete() ) {
			return $exclusions;
		}

		$address = $this->settings->script_url();

		if ( is_array( $exclusions ) ) {
			return $exclusions + array( $address => '' );
		}

		return is_string( $exclusions ) ? self::listed( $exclusions, $address ) : $exclusions;
	}

	/**
	 * A comma-separated list with one more entry, unless the entry is already there.
	 *
	 * @param string $exclusions The list.
	 * @param string $entry      What to add.
	 * @return string The list with the entry in it.
	 */
	private static function listed( string $exclusions, string $entry ): string {
		if ( false !== strpos( $exclusions, $entry ) ) {
			return $exclusions;
		}

		return '' === trim( $exclusions ) ? $entry : $exclusions . ', ' . $entry;
	}

	/**
	 * A list with one more entry, unless the entry is already there.
	 *
	 * Anything that is not a list is handed back untouched: its shape belongs to whoever made it.
	 *
	 * @param mixed  $exclusions The list.
	 * @param string $entry      What to add.
	 * @return mixed The list with the entry in it.
	 */
	private static function with( $exclusions, string $entry ) {
		if ( is_array( $exclusions ) && ! in_array( $entry, $exclusions, true ) ) {
			$exclusions[] = $entry;
		}

		return $exclusions;
	}
}
