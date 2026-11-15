<?php
/**
 * The plugin's page under Settings.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Settings → Dewiride Analytics: connecting the site, choosing who is counted, and seeing whether
 * counting works.
 *
 * Built with the Settings API and WordPress's own admin markup, so it looks and behaves like
 * every other settings page in the dashboard.
 */
final class Settings_Page {

	/**
	 * The page's slug, under Settings.
	 */
	public const SLUG = 'dewiride-analytics';

	/**
	 * The capability that opens the page and saves it.
	 */
	private const CAPABILITY = 'manage_options';

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * The check of the tag visitors receive.
	 *
	 * @var Tag_Check
	 */
	private $tag_check;

	/**
	 * The plugin's basename, which names it on the Plugins screen.
	 *
	 * @var string
	 */
	private $basename;

	/**
	 * Creates the page.
	 *
	 * @param Settings  $settings  The plugin's settings.
	 * @param Tag_Check $tag_check The check of the tag visitors receive.
	 * @param string    $basename  The plugin's basename.
	 */
	public function __construct( Settings $settings, Tag_Check $tag_check, string $basename ) {
		$this->settings  = $settings;
		$this->tag_check = $tag_check;
		$this->basename  = $basename;
	}

	/**
	 * Hooks the page, its fields, the Settings link and the reminder on the Plugins screen.
	 */
	public function register(): void {
		add_action( 'admin_menu', array( $this, 'add_page' ) );
		add_action( 'admin_init', array( $this, 'add_fields' ) );
		add_action( 'admin_notices', array( $this, 'print_setup_notice' ) );
		add_filter( 'plugin_action_links_' . $this->basename, array( $this, 'action_links' ) );
	}

	/**
	 * Adds the page under Settings.
	 */
	public function add_page(): void {
		add_options_page(
			__( 'Dewiride Analytics', 'dewiride-analytics' ),
			__( 'Dewiride Analytics', 'dewiride-analytics' ),
			self::CAPABILITY,
			self::SLUG,
			array( $this, 'render' )
		);
	}

	/**
	 * Declares the page's two sections and their fields.
	 */
	public function add_fields(): void {
		add_settings_section(
			'dewiride_analytics_connect',
			__( 'Connect', 'dewiride-analytics' ),
			array( $this, 'describe_connect' ),
			self::SLUG
		);

		add_settings_field(
			'dewiride_analytics_address',
			__( 'Address', 'dewiride-analytics' ),
			array( $this, 'render_address' ),
			self::SLUG,
			'dewiride_analytics_connect',
			array( 'label_for' => 'dewiride-analytics-address' )
		);

		add_settings_field(
			'dewiride_analytics_site_id',
			__( 'Website ID', 'dewiride-analytics' ),
			array( $this, 'render_site_id' ),
			self::SLUG,
			'dewiride_analytics_connect',
			array( 'label_for' => 'dewiride-analytics-site-id' )
		);

		add_settings_section(
			'dewiride_analytics_audience',
			__( 'Who is counted', 'dewiride-analytics' ),
			array( $this, 'describe_audience' ),
			self::SLUG
		);

		add_settings_field(
			'dewiride_analytics_counted_roles',
			__( 'Roles to count', 'dewiride-analytics' ),
			array( $this, 'render_counted_roles' ),
			self::SLUG,
			'dewiride_analytics_audience'
		);
	}

	/**
	 * Prints the page.
	 */
	public function render(): void {
		if ( ! current_user_can( self::CAPABILITY ) ) {
			return;
		}

		echo '<div class="wrap">';
		printf( '<h1>%s</h1>', esc_html( get_admin_page_title() ) );
		$this->print_warnings();
		$this->print_status();
		echo '<form action="options.php" method="post">';
		settings_fields( Settings::GROUP );
		do_settings_sections( self::SLUG );
		submit_button();
		echo '</form>';
		printf(
			'<p class="description">%s</p>',
			/* translators: %s: The plugin's version number. */
			esc_html( sprintf( __( 'Dewiride Analytics %s', 'dewiride-analytics' ), DEWIRIDE_ANALYTICS_VERSION ) )
		);
		echo '</div>';
	}

	/**
	 * Says where the two values come from.
	 */
	public function describe_connect(): void {
		printf( '<p>%s</p>', esc_html__( 'Copy both from the WordPress steps in Dewiride Analytics.', 'dewiride-analytics' ) );
	}

	/**
	 * Says who is left out without being asked.
	 */
	public function describe_audience(): void {
		printf( '<p>%s</p>', esc_html__( 'People who are signed in aren\'t counted, so your own visits don\'t count.', 'dewiride-analytics' ) );
	}

	/**
	 * Prints the address field, which only somebody allowed to add code to the site can change.
	 *
	 * For anybody else it is read-only, with a note saying who can change it. Read-only rather
	 * than disabled: a disabled field is left out of what the form sends, so every save would
	 * read as emptying it.
	 */
	public function render_address(): void {
		$locked = ! Settings::may_change_address();

		printf(
			'<input type="url" id="dewiride-analytics-address" name="%s" value="%s" class="regular-text code" placeholder="https://analytics.example.com" autocomplete="off" spellcheck="false"%s>',
			esc_attr( Settings::OPTION . '[address]' ),
			esc_attr( $this->settings->address() ),
			$locked ? ' readonly aria-describedby="dewiride-analytics-address-note"' : ''
		);

		if ( $locked ) {
			printf( '<p class="description" id="dewiride-analytics-address-note">%s</p>', esc_html( Settings::address_lock_message() ) );
		}
	}

	/**
	 * Prints the Website ID field.
	 */
	public function render_site_id(): void {
		printf(
			'<input type="text" id="dewiride-analytics-site-id" name="%s" value="%s" class="regular-text code" autocomplete="off" spellcheck="false">',
			esc_attr( Settings::OPTION . '[site_id]' ),
			esc_attr( $this->settings->site_id() )
		);
	}

	/**
	 * Prints a checkbox for every role on the site, none ticked until somebody ticks one.
	 */
	public function render_counted_roles(): void {
		$counted = $this->settings->counted_roles();

		printf( '<fieldset><legend class="screen-reader-text">%s</legend>', esc_html__( 'Roles to count', 'dewiride-analytics' ) );

		foreach ( wp_roles()->get_names() as $role => $name ) {
			printf(
				'<label><input type="checkbox" name="%s" value="%s"%s> %s</label><br>',
				esc_attr( Settings::OPTION . '[counted_roles][]' ),
				esc_attr( (string) $role ),
				checked( in_array( (string) $role, $counted, true ), true, false ),
				esc_html( translate_user_role( $name ) )
			);
		}

		echo '</fieldset>';
	}

	/**
	 * Puts a Settings link beside the plugin on the Plugins screen.
	 *
	 * @param mixed $links The plugin's action links.
	 * @return mixed The links, with Settings first.
	 */
	public function action_links( $links ) {
		if ( ! is_array( $links ) ) {
			return $links;
		}

		return array_merge(
			array(
				'settings' => sprintf(
					'<a href="%s">%s</a>',
					esc_url( $this->page_url() ),
					esc_html__( 'Settings', 'dewiride-analytics' )
				),
			),
			$links
		);
	}

	/**
	 * Reminds whoever can finish the setup, on the Plugins screen only, until it is finished.
	 */
	public function print_setup_notice(): void {
		$screen   = get_current_screen();
		$reminder = null !== $screen && 'plugins' === $screen->id && current_user_can( self::CAPABILITY ) ? $this->setup_reminder() : '';

		if ( '' === $reminder ) {
			return;
		}

		printf(
			'<div class="notice notice-info is-dismissible"><p>%s</p></div>',
			wp_kses(
				sprintf( $reminder, esc_url( $this->page_url() ) ),
				array( 'a' => array( 'href' => array() ) )
			)
		);
	}

	/**
	 * What the reminder asks of whoever reads it, with a placeholder for the settings page's
	 * address, or an empty string when nothing is left that they can add.
	 *
	 * It asks for exactly what is missing. The address is asked only of somebody who may change
	 * it, so a site's administrator on a network, who cannot, is reminded only once the address is
	 * saved, and then of the Website ID, which anybody who manages the site's options can add.
	 */
	private function setup_reminder(): string {
		$settings   = $this->settings->all();
		$no_site_id = '' === $settings['site_id'];

		if ( '' !== $settings['address'] ) {
			return $no_site_id
				/* translators: %s: Address of the plugin's settings page. */
				? __( 'Dewiride Analytics is almost ready. <a href="%s">Add your Website ID</a> to start counting visitors.', 'dewiride-analytics' )
				: '';
		}

		if ( ! Settings::may_change_address() ) {
			return '';
		}

		return $no_site_id
			/* translators: %s: Address of the plugin's settings page. */
			? __( 'Dewiride Analytics is almost ready. <a href="%s">Add your address and Website ID</a> to start counting visitors.', 'dewiride-analytics' )
			/* translators: %s: Address of the plugin's settings page. */
			: __( 'Dewiride Analytics is almost ready. <a href="%s">Add your address</a> to start counting visitors.', 'dewiride-analytics' );
	}

	/**
	 * Which warnings apply to this site's setup.
	 *
	 * An address starting with http on a site that has since moved to https loads a script
	 * browsers refuse. Plain permalinks put every page at the same address with only a query to
	 * tell them apart, and the query is not kept, so every visit would read as the home page.
	 * Open registration lets anybody sign in, and signed-in people are not counted unless their
	 * role is ticked.
	 *
	 * @return array{insecure_address: bool, plain_permalinks: bool, open_registration: string}
	 *         Whether browsers refuse the saved address, whether permalinks are plain, and the
	 *         role new registrations get when they are left out of the count, or an empty string.
	 */
	public function warnings(): array {
		$default_role = get_option( 'default_role' );
		$default_role = is_string( $default_role ) && '' !== $default_role ? $default_role : 'subscriber';
		$registering  = (bool) get_option( 'users_can_register' ) && ! in_array( $default_role, $this->settings->counted_roles(), true );

		return array(
			'insecure_address'  => Settings::is_mixed_content( $this->settings->address() ),
			'plain_permalinks'  => '' === get_option( 'permalink_structure' ),
			'open_registration' => $registering ? $default_role : '',
		);
	}

	/**
	 * Prints the warnings that apply.
	 */
	private function print_warnings(): void {
		$warnings = $this->warnings();

		if ( $warnings['insecure_address'] ) {
			$this->print_warning(
				__( 'Your site is served over https, but the address starts with http://, so browsers block the tag.', 'dewiride-analytics' )
				. ' '
				. ( Settings::may_change_address() ? __( 'Change the address to start with https:// and save.', 'dewiride-analytics' ) : Settings::address_lock_message() )
			);
		}

		if ( $warnings['plain_permalinks'] ) {
			$this->print_warning(
				sprintf(
					/* translators: %s: Address of the Permalink Settings page. */
					__( 'Your site\'s links look like ?p=123, so every visit would be recorded as your home page. Choose another option in <a href="%s">Permalink Settings</a>.', 'dewiride-analytics' ),
					esc_url( admin_url( 'options-permalink.php' ) )
				)
			);
		}

		if ( '' !== $warnings['open_registration'] ) {
			$names = wp_roles()->get_names();

			$this->print_warning(
				sprintf(
					/* translators: %s: Name of the role people get when they register. */
					__( 'Anyone can register on your site, and people who are signed in aren\'t counted. To count people who register, tick %s under Roles to count.', 'dewiride-analytics' ),
					'<strong>' . esc_html( translate_user_role( $names[ $warnings['open_registration'] ] ?? $warnings['open_registration'] ) ) . '</strong>'
				)
			);
		}
	}

	/**
	 * Prints one warning.
	 *
	 * @param string $message The warning, which may hold a link or emphasis.
	 */
	private function print_warning( string $message ): void {
		printf(
			'<div class="notice notice-warning inline"><p>%s</p></div>',
			wp_kses(
				$message,
				array(
					'a'      => array( 'href' => array() ),
					'strong' => array(),
				)
			)
		);
	}

	/**
	 * Prints whether counting is on, the last tag check, and the button that runs one.
	 *
	 * "On" says what the plugin does, which is all it can see: whether the tag runs and its
	 * reports arrive is for the tag check and the dashboard to show.
	 */
	private function print_status(): void {
		$complete = $this->settings->is_complete();

		printf( '<h2>%s</h2>', esc_html__( 'Status', 'dewiride-analytics' ) );
		echo '<table class="form-table" role="presentation"><tbody>';
		printf(
			'<tr><th scope="row">%s</th><td>%s</td></tr>',
			esc_html__( 'Counting', 'dewiride-analytics' ),
			esc_html(
				$complete
					? __( 'On. The tag is added to your pages for every visitor who is counted.', 'dewiride-analytics' )
					: $this->counting_off()
			)
		);

		if ( $complete ) {
			printf( '<tr><th scope="row">%s</th><td>', esc_html__( 'Tag check', 'dewiride-analytics' ) );
			$this->print_tag_check();
			echo '</td></tr>';
		}

		echo '</tbody></table>';
	}

	/**
	 * What the status says while counting is off.
	 *
	 * It names exactly what is missing, as the reminder does. An address somebody here may not
	 * change is not theirs to add, so they are told who can instead.
	 */
	private function counting_off(): string {
		$settings = $this->settings->all();

		if ( '' !== $settings['address'] ) {
			return __( 'Off. Add your Website ID below to start counting.', 'dewiride-analytics' );
		}

		if ( ! Settings::may_change_address() ) {
			/* translators: %s: Who can change the address, and what to ask them. */
			return sprintf( __( 'Off. %s', 'dewiride-analytics' ), Settings::address_lock_message() );
		}

		return '' === $settings['site_id']
			? __( 'Off. Add your address and Website ID below to start counting.', 'dewiride-analytics' )
			: __( 'Off. Add your address below to start counting.', 'dewiride-analytics' );
	}

	/**
	 * Prints the last tag check and the Check the tag button.
	 */
	private function print_tag_check(): void {
		$result = $this->tag_check->last_result();

		if ( null === $result ) {
			printf( '<p>%s</p>', esc_html__( 'Not checked yet.', 'dewiride-analytics' ) );
		} else {
			printf(
				'<p>%s</p><p class="description">%s</p>',
				esc_html( Tag_Check::message( $result ) ),
				/* translators: %s: How long ago, such as "3 hours". */
				esc_html( sprintf( __( 'Checked %s ago.', 'dewiride-analytics' ), human_time_diff( $result['checked_at'] ) ) )
			);
		}

		printf( '<form action="%s" method="post">', esc_url( admin_url( 'admin-post.php' ) ) );
		printf( '<input type="hidden" name="action" value="%s">', esc_attr( Tag_Check::ACTION ) );
		wp_nonce_field( Tag_Check::ACTION );
		submit_button( __( 'Check the tag', 'dewiride-analytics' ), 'secondary', 'dewiride-analytics-check', false );
		echo '</form>';
	}

	/**
	 * The address of this page.
	 */
	private function page_url(): string {
		return admin_url( 'options-general.php?page=' . self::SLUG );
	}
}
