<?php
/**
 * What the plugin suggests for the site's privacy policy.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Offers WordPress's privacy policy guide a description of what is collected, for the site's
 * owner to adapt and copy into their own policy.
 *
 * It says what the plugin can make happen on any site, the server's reports included, and the
 * terms on which Dewiride Analytics keeps what it receives. It is a suggestion WordPress shows
 * under Settings → Privacy, never text the plugin puts on a page.
 */
final class Privacy_Policy {

	/**
	 * Hooks the suggestion into the dashboard, where WordPress collects them.
	 */
	public function register(): void {
		add_action( 'admin_init', array( $this, 'suggest' ) );
	}

	/**
	 * Gives WordPress the suggested text.
	 */
	public function suggest(): void {
		wp_add_privacy_policy_content( __( 'Dewiride Analytics', 'dewiride-analytics' ), wp_kses_post( self::text() ) );
	}

	/**
	 * The suggested text, as paragraphs.
	 */
	public static function text(): string {
		$paragraphs = array(
			__( 'This site counts its visits with Dewiride Analytics, to know how many people read it and which visits come from programs such as search engines and AI crawlers.', 'dewiride-analytics' ),
			__( 'When you read a page, a script sends Dewiride Analytics the address of the page, the page you came from, the size of your browser window, your browser\'s language and time zone, whether your browser says it is controlled by automation, how long the page was in front of you, how far down it you read and whether you used a pointer, touch or keyboard — and, if the site has chosen to, which links and buttons you use. Never what you type.', 'dewiride-analytics' ),
			__( 'The site\'s own server also reports the requests it answers, those made by programs included: the address of the page, the page you came from, your IP address, your browser\'s identification and language, and how the site answered. It does this whatever you choose in a consent banner, because it is how the site tells visits by people apart from visits by programs.', 'dewiride-analytics' ),
			__( 'No cookies are set and nothing is stored on your device. Your visits are grouped under a key that changes every day, so one day\'s visits can\'t be linked to the next. If you are signed in to this site, your visits aren\'t counted unless the site counts people with your role.', 'dewiride-analytics' ),
			__( 'Your IP address is kept for 72 hours; after that only the country, region, town and network worked out from it remain. When a visit looks like it comes from a search engine or another crawler, its address is looked up in DNS to confirm who sent it. Reports are kept for twelve months.', 'dewiride-analytics' ),
			sprintf(
				/* translators: %s: Address of the hosted Dewiride Analytics service's privacy policy. */
				__( 'If this site uses the hosted Dewiride Analytics service, Dewiride Analytics processes these reports for the site under its privacy policy: %s. If the site runs Dewiride Analytics itself, they stay on the site\'s own servers.', 'dewiride-analytics' ),
				'https://analytics.dewiride.com/privacy'
			),
		);

		return '<p>' . implode( '</p><p>', array_map( 'esc_html', $paragraphs ) ) . '</p>';
	}
}
