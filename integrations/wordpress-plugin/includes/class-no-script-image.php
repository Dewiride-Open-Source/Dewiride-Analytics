<?php
/**
 * The image that counts a reader whose browser runs no scripts.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Prints a one-pixel image inside `<noscript>`, which only a browser not running scripts loads.
 *
 * It counts a page view and nothing else: a reader with scripting turned off or blocked, who
 * would otherwise not be counted at all. The page is named in the image's address, because the
 * site knows it exactly, so the browser is asked to say no more than which site it is on.
 */
final class No_Script_Image {

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
	 * Creates the image over the given settings and audience.
	 *
	 * @param Settings $settings The plugin's settings.
	 * @param Audience $audience Who is counted.
	 */
	public function __construct( Settings $settings, Audience $audience ) {
		$this->settings = $settings;
		$this->audience = $audience;
	}

	/**
	 * Hooks the image into the page.
	 *
	 * It goes at the top of the body. A theme too old to call `wp_body_open` gets it in the
	 * footer instead.
	 */
	public function register(): void {
		add_action( 'wp_body_open', array( $this, 'print_in_body' ) );
		add_action( 'wp_footer', array( $this, 'print_in_footer' ) );
	}

	/**
	 * Prints the image at the top of the body.
	 */
	public function print_in_body(): void {
		$this->print_image();
	}

	/**
	 * Prints the image in the footer, for a theme that never opened the body through WordPress.
	 */
	public function print_in_footer(): void {
		if ( did_action( 'wp_body_open' ) ) {
			return;
		}

		$this->print_image();
	}

	/**
	 * The address the image is loaded from: the collector, the Website ID and the page.
	 *
	 * @param string $address The analytics address.
	 * @param string $site_id The Website ID.
	 * @param string $page    The address of the page being read.
	 */
	public static function source( string $address, string $site_id, string $page ): string {
		return add_query_arg(
			array(
				'site' => rawurlencode( $site_id ),
				'u'    => rawurlencode( $page ),
			),
			$address . '/collect/pixel.gif'
		);
	}

	/**
	 * The address of the page being read, without its query or fragment.
	 *
	 * The site's own scheme and host, never the ones the request claimed, followed by the path
	 * that was asked for. A query is left out because it can carry anything — a search, an email
	 * address in a campaign link — and the collector keeps only the path.
	 */
	public static function current_page(): string {
		$home = wp_parse_url( home_url() );
		$home = is_array( $home ) ? $home : array();
		$path = '/';

		if ( isset( $_SERVER['REQUEST_URI'] ) ) {
			$requested = filter_var( wp_unslash( $_SERVER['REQUEST_URI'] ), FILTER_UNSAFE_RAW, FILTER_FLAG_STRIP_LOW );
			$requested = is_string( $requested ) ? wp_parse_url( $requested, PHP_URL_PATH ) : null;
			$path      = is_string( $requested ) && 0 === strpos( $requested, '/' ) ? $requested : '/';
		}

		return ( $home['scheme'] ?? 'http' ) . '://' . ( $home['host'] ?? '' ) . ( isset( $home['port'] ) ? ':' . $home['port'] : '' ) . $path;
	}

	/**
	 * Prints the image when counting is on and the visitor is counted.
	 *
	 * The markup is written out in full rather than built by a script, since a browser that loads
	 * it runs none. `data-no-lazy` and `skip-lazy` keep lazy-loading tools from swapping its address
	 * for one only a script would restore.
	 *
	 * `strict-origin` has the browser send only the site's origin as the referrer, never the page
	 * or its query, which can hold what a reader searched for. The collector takes the page from
	 * `u` in preference to the referrer, and judges where the image was shown from the referrer's
	 * host, so a copy of the page served from another host is still turned away. The snippet the
	 * dashboard hands out asks for the whole address instead, because a line pasted into every
	 * page cannot know which page it is on and has no `u` to name it.
	 */
	private function print_image(): void {
		if ( ! $this->settings->is_complete() || ! $this->audience->counts_current_visitor() ) {
			return;
		}

		printf(
			'<noscript><img src="%s" referrerpolicy="strict-origin" alt="" width="1" height="1" style="position:absolute" data-no-lazy="1" class="skip-lazy"></noscript>' . "\n",
			esc_url( self::source( $this->settings->address(), $this->settings->site_id(), self::current_page() ) )
		);
	}
}
