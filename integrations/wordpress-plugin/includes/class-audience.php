<?php
/**
 * Who is counted.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Decides whether the person reading the current page is counted.
 *
 * Signed-in people are left out unless one of their roles is ticked in the settings, so the
 * owner's own visits and their team's do not count as readers. A page that is not being read as
 * itself — a preview, the Customizer, an embed somewhere else, an AMP copy — is never counted.
 */
final class Audience {

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * Creates the decision over the given settings.
	 *
	 * @param Settings $settings The plugin's settings.
	 */
	public function __construct( Settings $settings ) {
		$this->settings = $settings;
	}

	/**
	 * Whether the current visitor, on the current page, is counted.
	 */
	public function counts_current_visitor(): bool {
		return ! self::is_copy() && $this->counts_person();
	}

	/**
	 * Whether the person making the current request is counted, whatever the page: anybody who is
	 * not signed in, and somebody signed in with a ticked role.
	 *
	 * This alone decides about a request the site refused. Whoever sends a request writes its
	 * query, and with it whether the page is asked for as a preview, an embed or an AMP copy, so
	 * those cannot be allowed to leave out a probe; who is signed in is the site's own knowledge.
	 */
	public function counts_person(): bool {
		if ( ! is_user_logged_in() ) {
			return true;
		}

		return $this->counts_roles( wp_get_current_user()->roles );
	}

	/**
	 * Whether a signed-in person with these roles is counted.
	 *
	 * Somebody signed in without any role on this site — a network administrator visiting one of
	 * its sites — is left out like everybody else who is signed in.
	 *
	 * @param array<mixed> $roles The person's roles on this site.
	 */
	public function counts_roles( array $roles ): bool {
		return array() !== array_intersect( array_filter( $roles, 'is_string' ), $this->settings->counted_roles() );
	}

	/**
	 * Whether the current page is not being read as itself: a preview, the Customizer, an embed
	 * somewhere else, or an AMP copy.
	 */
	private static function is_copy(): bool {
		return is_preview() || is_customize_preview() || is_embed() || self::is_amp_page();
	}

	/**
	 * Whether the page is the AMP copy made by the AMP plugin, where no script of ours can run.
	 */
	private static function is_amp_page(): bool {
		return function_exists( 'amp_is_request' ) && amp_is_request();
	}
}
