<?php
/**
 * The part of the settings page about the server's own reports.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Settings → Dewiride Analytics, for robots and other programs: the server key, what is reported,
 * how visitors reach the site, and how the reports are going.
 *
 * Every state the reports can be in is said in a sentence that names what to do about it, so an
 * owner never has to work out from a status code why robots stopped appearing.
 */
final class Server_Settings {

	/**
	 * The section the fields are in.
	 */
	private const SECTION = 'dewiride_analytics_server';

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * The server key.
	 *
	 * @var Server_Key
	 */
	private $server_key;

	/**
	 * How reports have been going.
	 *
	 * @var Reporting_Status
	 */
	private $status;

	/**
	 * The check of the connection.
	 *
	 * @var Connection_Check
	 */
	private $connection_check;

	/**
	 * The check of the tag visitors receive.
	 *
	 * @var Tag_Check
	 */
	private $tag_check;

	/**
	 * Where the visitor is.
	 *
	 * @var Visitor_Address
	 */
	private $visitor_address;

	/**
	 * Creates the part of the page.
	 *
	 * @param Settings         $settings         The plugin's settings.
	 * @param Server_Key       $server_key       The server key.
	 * @param Reporting_Status $status           How reports have been going.
	 * @param Connection_Check $connection_check The check of the connection.
	 * @param Tag_Check        $tag_check        The check of the tag visitors receive.
	 * @param Visitor_Address  $visitor_address  Where the visitor is.
	 */
	public function __construct( Settings $settings, Server_Key $server_key, Reporting_Status $status, Connection_Check $connection_check, Tag_Check $tag_check, Visitor_Address $visitor_address ) {
		$this->settings         = $settings;
		$this->server_key       = $server_key;
		$this->status           = $status;
		$this->connection_check = $connection_check;
		$this->tag_check        = $tag_check;
		$this->visitor_address  = $visitor_address;
	}

	/**
	 * Declares the section and its fields on the page.
	 *
	 * @param string $page The page's slug.
	 */
	public function add_fields( string $page ): void {
		add_settings_section( self::SECTION, __( 'Robots and other programs', 'dewiride-analytics' ), array( $this, 'describe' ), $page );
		add_settings_field( 'dewiride_analytics_server_key', __( 'Server key', 'dewiride-analytics' ), array( $this, 'render_key' ), $page, self::SECTION, array( 'label_for' => 'dewiride-analytics-server-key' ) );
		add_settings_field( 'dewiride_analytics_report_server', __( 'Reporting', 'dewiride-analytics' ), array( $this, 'render_reporting' ), $page, self::SECTION );
		add_settings_field( 'dewiride_analytics_report_also', __( 'Also report', 'dewiride-analytics' ), array( $this, 'render_categories' ), $page, self::SECTION );
		add_settings_field( 'dewiride_analytics_proxy', __( 'How visitors reach your site', 'dewiride-analytics' ), array( $this, 'render_proxy' ), $page, self::SECTION );
	}

	/**
	 * Says what the section is for, and what a page cache in front of WordPress means for it.
	 */
	public function describe(): void {
		printf( '<p>%s</p>', esc_html__( 'Search engines, AI crawlers, feed readers and scanners don\'t run the tag, so only your server sees them. Save a server key and your server reports them too.', 'dewiride-analytics' ) );

		if ( self::has_page_cache() ) {
			printf( '<p>%s</p>', esc_html__( 'Your site uses a page cache. Visits it answers from the cache don\'t reach WordPress, so your server can\'t report them; people whose browser runs the tag are still counted.', 'dewiride-analytics' ) );
		}
	}

	/**
	 * Prints the server key field, which never shows a key.
	 *
	 * A key defined in `wp-config.php` is shown as that, by its ending. A saved one is shown by its
	 * ending too, with a box that removes it; pasting a new key replaces it.
	 */
	public function render_key(): void {
		$source = $this->server_key->source();
		$ending = Server_Key::ending_of( $this->server_key->key() );

		if ( Server_Key::FROM_CONFIG === $source || Server_Key::MALFORMED_IN_CONFIG === $source ) {
			printf(
				'<p id="dewiride-analytics-server-key">%s</p>',
				esc_html(
					Server_Key::FROM_CONFIG === $source
						/* translators: %s: The last four characters of the server key. */
						? sprintf( __( 'Set in wp-config.php, ending in %s.', 'dewiride-analytics' ), $ending )
						: __( 'The key set in wp-config.php isn\'t complete. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and put it in wp-config.php in its place.', 'dewiride-analytics' )
				)
			);

			return;
		}

		printf(
			'<input type="password" id="dewiride-analytics-server-key" name="%s" value="" class="regular-text code" autocomplete="new-password" spellcheck="false" aria-describedby="dewiride-analytics-server-key-note">',
			esc_attr( Settings::OPTION . '[server_key]' )
		);
		printf( '<p class="description" id="dewiride-analytics-server-key-note">%s</p>', esc_html( $this->key_note( $source, $ending ) ) );

		if ( Server_Key::SAVED === $source || Server_Key::UNREADABLE === $source ) {
			printf(
				'<p><label><input type="checkbox" name="%s" value="1"> %s</label></p>',
				esc_attr( Settings::OPTION . '[forget_server_key]' ),
				esc_html__( 'Remove the saved key', 'dewiride-analytics' )
			);
		}
	}

	/**
	 * Prints the switch, and where the server cannot finish a page before reporting it, the
	 * second switch that accepts what that costs.
	 */
	public function render_reporting(): void {
		$saved = $this->settings->all();

		echo '<fieldset>';
		self::print_checkbox( 'report_server', $saved['report_server'], __( 'Report visits from robots and other programs', 'dewiride-analytics' ) );

		if ( ! Server_Reporting::finishes_pages_first() ) {
			printf( '<p class="description">%s</p>', esc_html__( 'This server can\'t send a page before reporting it, so every page opened by anyone who isn\'t signed in, readers and robots alike, would arrive a little later. Reports are sent only if you tick the box below as well.', 'dewiride-analytics' ) );
			self::print_checkbox( 'report_unfinished', $saved['report_unfinished'], __( 'Report anyway, even though pages arrive a little later', 'dewiride-analytics' ) );
		}

		echo '</fieldset>';
	}

	/**
	 * Prints the two kinds of request reported only when they are chosen.
	 */
	public function render_categories(): void {
		$saved = $this->settings->all();

		echo '<fieldset>';
		self::print_checkbox( 'report_feeds', $saved['report_feeds'], __( 'Feeds, robots.txt and sitemaps', 'dewiride-analytics' ) );
		self::print_checkbox( 'report_sign_in', $saved['report_sign_in'], __( 'Sign-in attempts and XML-RPC', 'dewiride-analytics' ) );
		printf( '<p class="description">%s</p>', esc_html__( 'Attacks on the sign-in page and XML-RPC can arrive by the thousand, and each one counts towards your page views.', 'dewiride-analytics' ) );
		echo '</fieldset>';
	}

	/**
	 * Prints how visitors reach the site, with the address WordPress sees for whoever is reading
	 * the page, which is how a wrong choice shows.
	 */
	public function render_proxy(): void {
		$proxy = $this->settings->proxy();
		$name  = Settings::OPTION . '[proxy_mode]';

		printf( '<fieldset><legend class="screen-reader-text">%s</legend>', esc_html__( 'How visitors reach your site', 'dewiride-analytics' ) );
		printf(
			'<label><input type="radio" name="%1$s" value="%2$s"%3$s> %4$s</label><br>',
			esc_attr( $name ),
			esc_attr( Settings::AUTOMATIC ),
			checked( Settings::AUTOMATIC, $proxy['mode'], false ),
			esc_html__( 'Directly, or through Cloudflare', 'dewiride-analytics' )
		);
		printf(
			'<label><input type="radio" name="%1$s" value="%2$s"%3$s> %4$s</label>',
			esc_attr( $name ),
			esc_attr( Settings::THROUGH_PROXY ),
			checked( Settings::THROUGH_PROXY, $proxy['mode'], false ),
			esc_html__( 'Through another proxy or load balancer', 'dewiride-analytics' )
		);
		printf(
			'<p><label for="dewiride-analytics-proxy-header">%s</label><br><input type="text" id="dewiride-analytics-proxy-header" name="%s" value="%s" class="regular-text code" autocomplete="off" spellcheck="false"></p>',
			esc_html__( 'The header your proxy puts the visitor\'s address in', 'dewiride-analytics' ),
			esc_attr( Settings::OPTION . '[proxy_header]' ),
			esc_attr( $proxy['header'] )
		);
		printf(
			'<p><label for="dewiride-analytics-proxy-trusted">%s</label><br><textarea id="dewiride-analytics-proxy-trusted" name="%s" rows="3" class="large-text code" spellcheck="false">%s</textarea></p>',
			esc_html__( 'Your proxies\' addresses, one to a line', 'dewiride-analytics' ),
			esc_attr( Settings::OPTION . '[proxy_trusted]' ),
			esc_textarea( implode( "\n", $proxy['trusted'] ) )
		);
		printf( '<p class="description">%s</p>', esc_html( $this->address_sentence() ) );
		echo '</fieldset>';
	}

	/**
	 * Prints the status of the reports and of the last connection check, as rows of the page's
	 * status table, with the button that checks the connection.
	 */
	public function print_status_rows(): void {
		printf(
			'<tr><th scope="row">%s</th><td><p>%s</p></td></tr>',
			esc_html__( 'Robots and other programs', 'dewiride-analytics' ),
			esc_html( $this->reporting_sentence() )
		);

		if ( Server_Key::NONE === $this->server_key->source() ) {
			return;
		}

		printf( '<tr><th scope="row">%s</th><td>', esc_html__( 'Connection', 'dewiride-analytics' ) );
		$this->print_connection();
		echo '</td></tr>';
	}

	/**
	 * What the reports are doing, and what to do when they are not being sent.
	 */
	public function reporting_sentence(): string {
		$problem = $this->setup_problem();

		return '' === $problem ? $this->delivery_sentence() : $problem;
	}

	/**
	 * What in the setup keeps the reports from being sent, or an empty string when nothing does.
	 */
	private function setup_problem(): string {
		$source  = $this->server_key->source();
		$problem = '';

		if ( Server_Key::NONE === $source ) {
			$problem = __( 'Off. Save a server key below to report robots and other programs too.', 'dewiride-analytics' );
		} elseif ( ! $this->settings->all()['report_server'] ) {
			$problem = __( 'Off. Tick Report visits from robots and other programs below to start.', 'dewiride-analytics' );
		} elseif ( Server_Key::UNREADABLE === $source ) {
			$problem = __( 'Off. The saved server key can\'t be read any more, because your site\'s security keys have changed. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and paste it below.', 'dewiride-analytics' );
		} elseif ( Server_Key::MALFORMED_IN_CONFIG === $source ) {
			$problem = __( 'Off. The server key in wp-config.php isn\'t complete. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and put it in wp-config.php in its place.', 'dewiride-analytics' );
		} elseif ( '' === $this->settings->key_ending() ) {
			// A key saved some other way than through this page — imported, or written from WP-CLI —
			// left the settings without its ending, which is what every request asks, and saving
			// the page writes it.
			$problem = __( 'Off. Press Save Changes below so that reporting can start.', 'dewiride-analytics' );
		} elseif ( ! $this->settings->reports_server( Server_Reporting::finishes_pages_first() ) ) {
			$problem = __( 'Off. This server can\'t send a page before reporting it. Tick the second box under Reporting below if pages may arrive a little later.', 'dewiride-analytics' );
		} elseif ( ! Private_Host::may_receive_secrets( $this->settings->server_address() ) ) {
			$problem = __( 'Off. Reports carry your server key, so the address must start with https://.', 'dewiride-analytics' );
		} elseif ( Tag_Check::holds_back( $this->tag_check->last_result() ) ) {
			$problem = __( 'Paused while the tag doesn\'t reach your readers, as the tag check says. Until it does, readers whose tag never ran would be reported as robots.', 'dewiride-analytics' );
		}

		return $problem;
	}

	/**
	 * How the reports are going, once the setup lets them be sent.
	 *
	 * Before anything has been sent, the address WordPress sees for whoever reads the page says
	 * whether visitors' addresses can be read at all: behind a proxy WordPress was not told about,
	 * none can, and nothing will ever be sent until that is put right.
	 */
	private function delivery_sentence(): string {
		$state = $this->status->state();
		$now   = time();

		if ( '' !== $state['stopped'] ) {
			$sentence = self::stop_sentence( $state['stopped'], $state['redirect'] );
		} elseif ( $state['paused_until'] > $now ) {
			$sentence = self::pause_sentence( $state['pause'], human_time_diff( $now, $state['paused_until'] ) );
		} elseif ( $state['failures'] > 0 ) {
			$sentence = self::failure_sentence( $state['failure'], human_time_diff( $now, max( $now + 1, $state['retry_at'] ) ) );
		} elseif ( $state['sent_at'] > 0 ) {
			/* translators: 1: How long ago, such as "3 mins". 2: How long sending took, in milliseconds. */
			$sentence = sprintf( __( 'On. A report was sent %1$s ago and took %2$d ms.', 'dewiride-analytics' ), human_time_diff( $state['sent_at'], $now ), $state['send_ms'] );
		} elseif ( in_array( $this->visitor_address->resolve()['cause'], array( Visitor_Address::UNKNOWN_PROXY, Visitor_Address::UNTRUSTED_PROXY, Visitor_Address::NO_VISITOR ), true ) ) {
			$sentence = __( 'Not reporting yet. Visitors\' addresses can\'t be read, so they aren\'t reported; How visitors reach your site below says why.', 'dewiride-analytics' );
		} else {
			$sentence = __( 'On. Robots and other programs are reported as they arrive.', 'dewiride-analytics' );
		}

		return $sentence;
	}

	/**
	 * What went wrong with the last report, and when the plugin tries again.
	 *
	 * @param int    $failure The status the last report was answered with, or 0 for no answer.
	 * @param string $wait    How long until the next try, such as "30 secs".
	 */
	private static function failure_sentence( int $failure, string $wait ): string {
		if ( 0 === $failure ) {
			/* translators: %s: How long until the next try, such as "30 secs". */
			$sentence = __( 'Waiting. The last report got no answer; the plugin tries again in %s.', 'dewiride-analytics' );
		} elseif ( in_array( $failure, array( 200, 404, 405 ), true ) ) {
			/* translators: %s: How long until the next try, such as "30 secs". */
			$sentence = __( 'Waiting. The address doesn\'t answer as Dewiride Analytics; check that it\'s exactly the address you open Dewiride Analytics on. The plugin tries again in %s.', 'dewiride-analytics' );
		} elseif ( 403 === $failure ) {
			/* translators: %s: How long until the next try, such as "30 secs". */
			$sentence = __( 'Waiting. Something at the address turned the last report away, often a firewall; the plugin tries again in %s.', 'dewiride-analytics' );
		} else {
			/* translators: %s: How long until the next try, such as "30 secs". */
			$sentence = __( 'Waiting. Dewiride Analytics answered the last report with an error; the plugin tries again in %s.', 'dewiride-analytics' );
		}

		return sprintf( $sentence, $wait );
	}

	/**
	 * Why the reports are waiting, and for how long.
	 *
	 * @param string $pause Why: one of the pauses {@see Reporting_Status} keeps.
	 * @param string $wait  How long is left, such as "1 min".
	 */
	private static function pause_sentence( string $pause, string $wait ): string {
		$sentences = array(
			/* translators: %s: How long until the plugin asks again, such as "15 mins". */
			Reporting_Status::NOT_MEASURING => __( 'Waiting. Dewiride Analytics isn\'t counting visits for this website at the moment; the plugin asks again in %s.', 'dewiride-analytics' ),
			/* translators: %s: How long until the plugin asks again, such as "15 mins". */
			Reporting_Status::UNCOVERED     => __( 'Waiting. The website in Dewiride Analytics doesn\'t cover this site\'s address, so its reports are refused. Check the website\'s address in Dewiride Analytics; the plugin asks again in %s.', 'dewiride-analytics' ),
		);

		/* translators: %s: How long until reports start again, such as "1 min". */
		return sprintf( $sentences[ $pause ] ?? __( 'Waiting. Dewiride Analytics asked for a pause; reports start again in %s.', 'dewiride-analytics' ), $wait );
	}

	/**
	 * What stopped the reports, and what to do about it.
	 *
	 * @param string $reason   Why the reports stopped.
	 * @param string $redirect Where the address redirects, for a redirect.
	 */
	private static function stop_sentence( string $reason, string $redirect ): string {
		if ( Reporting_Status::REDIRECTED === $reason ) {
			return '' === $redirect
				? __( 'Stopped. The address answers with a redirect. Check that it\'s exactly the address you open Dewiride Analytics on.', 'dewiride-analytics' )
				/* translators: %s: The address the saved address redirects to. */
				: sprintf( __( 'Stopped. The address redirects to %s. Save that address instead.', 'dewiride-analytics' ), $redirect );
		}

		$sentences = array(
			Reporting_Status::OTHER_WEBSITE  => __( 'Stopped. The server key and the Website ID come from different websites in Dewiride Analytics. Copy both from the same website, under Tracking code, then WordPress.', 'dewiride-analytics' ),
			Reporting_Status::KEY_UNREADABLE => __( 'Stopped. The server key couldn\'t be read on your site\'s pages. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and paste it below.', 'dewiride-analytics' ),
		);

		$refused = Server_Key::is_in_config()
			? __( 'Stopped. Dewiride Analytics didn\'t accept the server key in wp-config.php. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and put it in wp-config.php in its place.', 'dewiride-analytics' )
			: __( 'Stopped. Dewiride Analytics didn\'t accept the server key. Create a new key in Dewiride Analytics, under Tracking code, then WordPress, and paste it below.', 'dewiride-analytics' );

		return $sentences[ $reason ] ?? $refused;
	}

	/**
	 * The note under the key field.
	 *
	 * @param string $source Where the key comes from.
	 * @param string $ending The last characters of the saved key.
	 */
	private function key_note( string $source, string $ending ): string {
		if ( Server_Key::SAVED === $source ) {
			/* translators: %s: The last four characters of the saved server key. */
			return sprintf( __( 'Saved, ending in %s. Paste a new key to replace it.', 'dewiride-analytics' ), $ending );
		}

		return Server_Key::UNREADABLE === $source
			? __( 'The saved key can\'t be read any more, because your site\'s security keys have changed. Create a new key in Dewiride Analytics and paste it here.', 'dewiride-analytics' )
			: __( 'In Dewiride Analytics, open your website, choose Tracking code, then WordPress, and create a key.', 'dewiride-analytics' );
	}

	/**
	 * Whose address WordPress sees for the person reading the page, or why it cannot be used.
	 */
	private function address_sentence(): string {
		$resolved = $this->visitor_address->resolve();

		if ( Visitor_Address::FOUND === $resolved['cause'] ) {
			/* translators: %s: An IP address. */
			return sprintf( __( 'WordPress sees your address as %s.', 'dewiride-analytics' ), $resolved['address'] );
		}

		// Cloudflare's list is fetched only once the server reports and somebody arrives through
		// Cloudflare; until then nothing is fetching it, and there is nothing to put right.
		if ( Visitor_Address::CLOUDFLARE_PENDING === $resolved['cause'] ) {
			return false !== wp_next_scheduled( Cloudflare_Ranges::EVENT )
				? __( 'Your site is reached through Cloudflare. The plugin is fetching Cloudflare\'s list of addresses, and reports visitors once it has it. If this stays, make sure your site can reach api.cloudflare.com.', 'dewiride-analytics' )
				: __( 'Your site is reached through Cloudflare. Once your server reports robots, the plugin fetches Cloudflare\'s list of addresses and reads visitors\' addresses through it.', 'dewiride-analytics' );
		}

		$sentences = array(
			Visitor_Address::UNKNOWN_PROXY      => __( 'Your site is behind a proxy WordPress doesn\'t know about, so visitors\' addresses can\'t be read and they aren\'t reported. Choose Through another proxy and enter its addresses.', 'dewiride-analytics' ),
			Visitor_Address::FOREIGN_CLOUDFLARE => __( 'Your request came through Cloudflare for a name that isn\'t this site\'s, so its address isn\'t believed.', 'dewiride-analytics' ),
			Visitor_Address::UNTRUSTED_PROXY    => __( 'Your request didn\'t come from one of the proxies entered above, so its address isn\'t believed.', 'dewiride-analytics' ),
			Visitor_Address::NO_VISITOR         => __( 'Your proxy didn\'t say whose request it passed on.', 'dewiride-analytics' ),
			Visitor_Address::PRIVATE_ADDRESS    => __( 'Your address is on a private network, so visits from it aren\'t reported.', 'dewiride-analytics' ),
		);

		return $sentences[ $resolved['cause'] ] ?? __( 'WordPress didn\'t receive an address for your request.', 'dewiride-analytics' );
	}

	/**
	 * Prints the last connection check and the Check connection button.
	 */
	private function print_connection(): void {
		$result = $this->connection_check->last_result();

		if ( null === $result ) {
			printf( '<p>%s</p>', esc_html__( 'Not checked yet.', 'dewiride-analytics' ) );
		} else {
			printf(
				'<p>%s</p><p class="description">%s</p>',
				esc_html( Connection_Check::message( $result ) ),
				/* translators: %s: How long ago, such as "3 hours". */
				esc_html( sprintf( __( 'Checked %s ago.', 'dewiride-analytics' ), human_time_diff( $result['checked_at'] ) ) )
			);
		}

		printf( '<form action="%s" method="post">', esc_url( admin_url( 'admin-post.php' ) ) );
		printf( '<input type="hidden" name="action" value="%s">', esc_attr( Connection_Check::ACTION ) );
		wp_nonce_field( Connection_Check::ACTION );
		submit_button( __( 'Check connection', 'dewiride-analytics' ), 'secondary', 'dewiride-analytics-check-connection', false );
		echo '</form>';
	}

	/**
	 * Prints one checkbox of the settings form, on a line of its own.
	 *
	 * @param string $name    The setting it sets.
	 * @param bool   $ticked  Whether it is ticked.
	 * @param string $label   What it says.
	 */
	private static function print_checkbox( string $name, bool $ticked, string $label ): void {
		printf(
			'<label><input type="checkbox" name="%s" value="1"%s> %s</label><br>',
			esc_attr( Settings::OPTION . '[' . $name . ']' ),
			checked( $ticked, true, false ),
			esc_html( $label )
		);
	}

	/**
	 * Whether a page cache answers some visits before WordPress runs: one of the caching plugins
	 * that install WordPress's page-cache drop-in, and switch it on.
	 */
	private static function has_page_cache(): bool {
		return defined( 'WP_CACHE' ) && WP_CACHE && file_exists( WP_CONTENT_DIR . '/advanced-cache.php' );
	}
}
