<?php
/**
 * The tag that loads the beacon.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Prints the beacon's script tag on every page a counted visitor reads.
 *
 * The script is enqueued like any other, so WordPress prints it, and the attributes the beacon
 * reads are added through `wp_script_attributes` rather than written into markup by hand.
 */
final class Tracker_Tag {

	/**
	 * The handle the script is enqueued under.
	 */
	public const HANDLE = 'dewiride-analytics';

	/**
	 * The `id` WordPress gives the printed tag: the handle followed by `-js`.
	 */
	public const ELEMENT_ID = 'dewiride-analytics-js';

	/**
	 * Attributes that ask optimisation tools to leave the tag as it is.
	 *
	 * Each is read by a different tool: `data-no-optimize`, `data-no-defer` and `data-no-minify` by
	 * LiteSpeed Cache, `data-noptimize` by Autoptimize and Breeze, `data-jetpack-boost` by Jetpack
	 * Boost, and `nowprocket` by WP Rocket's delayed JavaScript. A tag that is delayed until somebody
	 * moves the mouse never counts the readers who leave without doing so.
	 */
	private const LEFT_ALONE = array(
		'data-no-optimize'   => '1',
		'data-no-defer'      => '1',
		'data-no-minify'     => '1',
		'data-noptimize'     => '1',
		'data-jetpack-boost' => 'ignore',
		'nowprocket'         => true,
	);

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * Who is counted.
	 *
	 * @var Audience
	 */
	private $audience;

	/**
	 * Creates the tag over the given settings and audience.
	 *
	 * @param Settings $settings The plugin's settings.
	 * @param Audience $audience Who is counted.
	 */
	public function __construct( Settings $settings, Audience $audience ) {
		$this->settings = $settings;
		$this->audience = $audience;
	}

	/**
	 * Hooks the tag into the page.
	 */
	public function register(): void {
		add_action( 'wp_enqueue_scripts', array( $this, 'enqueue' ) );
		add_filter( 'wp_script_attributes', array( $this, 'attributes' ) );
	}

	/**
	 * Enqueues the beacon when counting is on and the visitor is counted.
	 *
	 * Loaded `async` in the head, so it starts as early as it can and never holds up the page: an
	 * analytics address that is slow or unreachable must not delay somebody else's site. No version
	 * is added, because the beacon is versioned by the installation that serves it, and the tag
	 * check and the optimisers' exclusions recognise it by its exact address.
	 */
	public function enqueue(): void {
		if ( ! $this->settings->is_complete() || ! $this->audience->counts_current_visitor() ) {
			return;
		}

		wp_enqueue_script(
			self::HANDLE,
			$this->settings->script_url(),
			array(),
			null, // phpcs:ignore WordPress.WP.EnqueuedResourceParameters.MissingVersion -- The installation that serves dw.js versions it; a version query would change the address the tag check and optimiser exclusions match.
			array(
				'strategy'  => 'async',
				'in_footer' => false,
			)
		);
	}

	/**
	 * Adds the beacon's attributes to its own tag, and touches no other.
	 *
	 * `data-cfasync` goes first because Cloudflare's Rocket Loader reads it only when it comes
	 * before `src`. The collector is given as an absolute address so the beacon never has to work
	 * it out from where it was loaded, which an optimiser that copies the file elsewhere would
	 * change.
	 *
	 * @param mixed $attributes The attributes of a script tag about to be printed.
	 * @return mixed The attributes, with the beacon's added when the tag is the beacon's.
	 */
	public function attributes( $attributes ) {
		if ( ! is_array( $attributes ) || self::ELEMENT_ID !== ( $attributes['id'] ?? null ) ) {
			return $attributes;
		}

		return array( 'data-cfasync' => 'false' )
			+ $attributes
			+ array(
				'data-site'      => $this->settings->site_id(),
				'data-collector' => $this->settings->collector_url(),
			)
			+ self::LEFT_ALONE;
	}
}
