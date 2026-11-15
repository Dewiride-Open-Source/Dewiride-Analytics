<?php
/**
 * Checks that visitors receive the tag the plugin prints.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

use WP_HTML_Tag_Processor;
use WP_Http;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Fetches the home page as an anonymous visitor would and records what became of the tag.
 *
 * What WordPress prints is not always what a visitor receives: page caches serve copies made
 * before the plugin was set up, optimisation plugins delay or combine scripts, and consent tools
 * hold them back until somebody accepts. Each of those quietly stops counting some or all
 * readers, so the plugin looks once a day, and whenever the owner asks, and says plainly what it
 * found. Only the outcome and the time are kept — never anything from the page — with the options
 * loaded for every page, since each report the server sends first asks whether the tag reaches
 * readers.
 *
 * @phpstan-type Outcome array{outcome: string, tool: string}
 * @phpstan-type Checked array{outcome: string, tool: string, checked_at: int}
 * @phpstan-type Beacon_Tag array{id: string, type: string, sources: array<string, string>, site: string|null, collector: string|null}
 */
final class Tag_Check {

	/**
	 * The WP-Cron event that runs the check once a day.
	 */
	public const EVENT = 'dewiride_analytics_tag_check';

	/**
	 * The option holding the last outcome.
	 */
	public const OPTION = 'dewiride_analytics_tag_check';

	/**
	 * The admin-post action behind the Check the tag button.
	 */
	public const ACTION = 'dewiride_analytics_check_tag';

	/**
	 * Exactly one tag, loading from the saved address and carrying the saved Website ID.
	 */
	public const INTACT = 'intact';

	/**
	 * No tag on the page.
	 */
	public const MISSING = 'missing';

	/**
	 * The plugin's tag is there, but changed so that it no longer loads as printed.
	 */
	public const ALTERED = 'altered';

	/**
	 * More than one tag, such as a snippet pasted into the theme before the plugin was installed.
	 */
	public const DUPLICATE = 'duplicate';

	/**
	 * The page could not be fetched.
	 */
	public const UNREACHABLE = 'unreachable';

	/**
	 * The tag is as printed, but loads over http on a page served over https, which browsers
	 * refuse.
	 */
	public const INSECURE = 'insecure';

	/**
	 * WP Rocket's delayed JavaScript changed the tag.
	 */
	public const WP_ROCKET = 'wp-rocket';

	/**
	 * LiteSpeed Cache's delayed JavaScript changed the tag.
	 */
	public const LITESPEED_CACHE = 'litespeed-cache';

	/**
	 * A consent tool is holding the tag back.
	 */
	public const CONSENT_TOOL = 'consent-tool';

	/**
	 * An optimiser combined the beacon with other scripts or copied it to another address.
	 */
	public const OPTIMISER = 'optimiser';

	/**
	 * Script types that mean a tool has taken the tag over, and which tool.
	 *
	 * WP Rocket writes `rocketlazyloadscript` with or without `text/` in front; LiteSpeed Cache
	 * writes `litespeed/javascript`; consent tools write `text/plain` until somebody accepts.
	 */
	private const TOOLS_BY_TYPE = array(
		'rocketlazyloadscript'      => self::WP_ROCKET,
		'text/rocketlazyloadscript' => self::WP_ROCKET,
		'litespeed/javascript'      => self::LITESPEED_CACHE,
		'text/plain'                => self::CONSENT_TOOL,
	);

	/**
	 * How many redirects within the site are followed to reach the home page.
	 */
	private const REDIRECTS = 3;

	/**
	 * How long the home page is given to arrive, in seconds.
	 */
	private const TIMEOUT = 10;

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * Creates the check over the given settings.
	 *
	 * @param Settings $settings The plugin's settings.
	 */
	public function __construct( Settings $settings ) {
		$this->settings = $settings;
	}

	/**
	 * Hooks the daily check, and forgets the last outcome when the address or Website ID change.
	 */
	public function register(): void {
		add_action( self::EVENT, array( $this, 'run' ) );
		add_action( 'add_option_' . Settings::OPTION, array( $this, 'settings_added' ), 10, 0 );
		add_action( 'update_option_' . Settings::OPTION, array( $this, 'settings_changed' ), 10, 2 );
	}

	/**
	 * Hooks the Check the tag button, and makes sure the daily check is scheduled.
	 *
	 * A plugin activated across a network is activated on one site only, so the other sites get
	 * their daily check the first time somebody opens their dashboard.
	 */
	public function register_admin(): void {
		add_action( 'admin_post_' . self::ACTION, array( $this, 'handle_request' ) );
		add_action( 'admin_init', array( self::class, 'schedule' ) );
	}

	/**
	 * Schedules the daily check, unless it already is.
	 */
	public static function schedule(): void {
		if ( false === wp_next_scheduled( self::EVENT ) ) {
			wp_schedule_event( time() + HOUR_IN_SECONDS, 'daily', self::EVENT );
		}
	}

	/**
	 * Removes the daily check.
	 */
	public static function unschedule(): void {
		wp_clear_scheduled_hook( self::EVENT );
	}

	/**
	 * Checks the home page and keeps the outcome, when counting is on.
	 */
	public function run(): void {
		if ( ! $this->settings->is_complete() ) {
			return;
		}

		$page   = $this->fetch_home_page();
		$result = null === $page
			? self::outcome( self::UNREACHABLE )
			: self::inspect( $page['html'], $this->settings->address(), $this->settings->site_id(), $page['url'] );

		update_option( self::OPTION, $result + array( 'checked_at' => time() ), true );
	}

	/**
	 * Whether an outcome keeps the tag from readers, which pauses the server's reports: no tag on
	 * the page, a tag a tool has taken over, or one browsers refuse to load.
	 *
	 * A page that could not be fetched pauses nothing, since nothing is known about it, and
	 * neither does a second copy of the beacon, which runs once whichever copy loads first.
	 *
	 * @param array|null $result The outcome of a check, or nothing when none has run.
	 * @phpstan-param Checked|null $result
	 */
	public static function holds_back( ?array $result ): bool {
		return null !== $result && in_array( $result['outcome'], array( self::MISSING, self::ALTERED, self::INSECURE ), true );
	}

	/**
	 * The last outcome, or nothing when the home page has not been checked since the address or
	 * Website ID were saved.
	 *
	 * @return Checked|null
	 */
	public function last_result(): ?array {
		$stored   = get_option( self::OPTION );
		$outcomes = array( self::INTACT, self::MISSING, self::ALTERED, self::DUPLICATE, self::UNREACHABLE, self::INSECURE );

		if ( ! is_array( $stored ) || ! in_array( $stored['outcome'] ?? null, $outcomes, true ) || ! is_int( $stored['checked_at'] ?? null ) ) {
			return null;
		}

		return array(
			'outcome'    => $stored['outcome'],
			'tool'       => is_string( $stored['tool'] ?? null ) ? $stored['tool'] : '',
			'checked_at' => $stored['checked_at'],
		);
	}

	/**
	 * Checks soon after the settings are saved for the first time.
	 *
	 * WordPress saves an option whose stored value is still its registered default by adding it
	 * rather than updating it, so the first save of the settings page announces an option added
	 * and never one updated. What was added is read back rather than taken from the hook, which
	 * WordPress announces only once it can be read. Activation adds the empty defaults the same
	 * way, and those name no address and no Website ID, so nothing is checked for them.
	 */
	public function settings_added(): void {
		$this->settings_changed( array(), $this->settings->all() );
	}

	/**
	 * Forgets the last outcome when the address or Website ID change, and checks again soon.
	 *
	 * @param mixed $previous The settings before the change.
	 * @param mixed $saved    The settings after it.
	 */
	public function settings_changed( $previous, $saved ): void {
		$previous = is_array( $previous ) ? $previous : array();
		$saved    = is_array( $saved ) ? $saved : array();

		if ( ( $previous['address'] ?? '' ) === ( $saved['address'] ?? '' ) && ( $previous['site_id'] ?? '' ) === ( $saved['site_id'] ?? '' ) ) {
			return;
		}

		delete_option( self::OPTION );
		wp_schedule_single_event( time(), self::EVENT );
	}

	/**
	 * Runs the check for the Check the tag button, then returns to the settings page.
	 */
	public function handle_request(): void {
		if ( ! current_user_can( 'manage_options' ) ) {
			wp_die( esc_html__( 'Sorry, you aren\'t allowed to check the tag.', 'dewiride-analytics' ), '', array( 'response' => 403 ) );
		}

		check_admin_referer( self::ACTION );
		$this->run();
		wp_safe_redirect( admin_url( 'options-general.php?page=' . Settings_Page::SLUG ) );
		exit;
	}

	/**
	 * What became of the tag in a page's markup.
	 *
	 * A tag is the plugin's own when it carries the `id` WordPress gives it, and it is another copy
	 * of the beacon when it loads a `dw.js` and names a website. The plugin's tag is intact when it
	 * is the only one, loads from the saved address as an ordinary script, names the saved Website
	 * ID and sends to an absolute address, and the page is not served over https while the tag
	 * loads over http.
	 *
	 * @param string $html    The page as a visitor receives it.
	 * @param string $address The saved analytics address.
	 * @param string $site_id The saved Website ID.
	 * @param string $page    The address the page was served from.
	 * @return Outcome
	 */
	public static function inspect( string $html, string $address, string $site_id, string $page ): array {
		$tags = self::beacon_tags( $html );

		if ( count( $tags ) > 1 ) {
			return self::outcome( self::DUPLICATE );
		}

		$tag = $tags[0] ?? null;

		if ( null === $tag || Tracker_Tag::ELEMENT_ID !== $tag['id'] ) {
			return self::outcome( self::MISSING );
		}

		$secure_page = 'https' === strtolower( (string) wp_parse_url( $page, PHP_URL_SCHEME ) );

		return self::judge( $tag, $address . '/dw.js', $site_id, $secure_page );
	}

	/**
	 * The sentence the settings page shows for an outcome: what was found and, when something
	 * needs doing, what to do.
	 *
	 * @param array $result The outcome of a check.
	 * @phpstan-param Outcome $result
	 */
	public static function message( array $result ): string {
		$messages = array(
			self::INTACT                                => __( 'The tag is in place on your home page.', 'dewiride-analytics' ),
			self::MISSING                               => __( 'Your home page doesn\'t carry the tag. If your site uses a page cache, clear it, then check again.', 'dewiride-analytics' ),
			self::DUPLICATE                             => __( 'Your home page loads the tag more than once. Remove any Dewiride Analytics code added to your theme or another plugin; this plugin adds the tag for you.', 'dewiride-analytics' ),
			self::UNREACHABLE                           => __( 'Your site couldn\'t open its own home page to check the tag. Some hosts don\'t allow this, and it doesn\'t affect counting.', 'dewiride-analytics' ),
			self::INSECURE                              => __( 'Your home page is served over https, but the tag loads over http, so browsers block it. The address must start with https:// too.', 'dewiride-analytics' ),
			self::ALTERED                               => __( 'Something on your site changes the tag before visitors receive it. Clear any page cache, then check again.', 'dewiride-analytics' ),
			self::ALTERED . ':' . self::WP_ROCKET       => __( 'WP Rocket is delaying the tag, so visitors who leave quickly aren\'t counted. Clear WP Rocket\'s cache, then check again.', 'dewiride-analytics' ),
			self::ALTERED . ':' . self::LITESPEED_CACHE => __( 'LiteSpeed Cache is delaying the tag, so visitors who leave quickly aren\'t counted. Purge LiteSpeed Cache, then check again.', 'dewiride-analytics' ),
			self::ALTERED . ':' . self::CONSENT_TOOL    => __( 'A consent tool is holding the tag back, so only visitors who accept are counted. The tag sets no cookies and stores nothing on visitors\' devices.', 'dewiride-analytics' ),
			self::ALTERED . ':' . self::OPTIMISER       => __( 'An optimisation plugin has combined the tag with other files or moved it. Exclude dw.js from JavaScript combining in that plugin, then clear its cache.', 'dewiride-analytics' ),
		);

		$key = '' === $result['tool'] ? $result['outcome'] : $result['outcome'] . ':' . $result['tool'];

		return $messages[ $key ] ?? $messages[ $result['outcome'] ] ?? $messages[ self::ALTERED ];
	}

	/**
	 * Every tag on the page that is the plugin's or another copy of the beacon.
	 *
	 * @param string $html The page.
	 * @return list<Beacon_Tag>
	 */
	private static function beacon_tags( string $html ): array {
		$processor = new WP_HTML_Tag_Processor( $html );
		$tags      = array();

		while ( $processor->next_tag( array( 'tag_name' => 'script' ) ) ) {
			$tag = self::describe( $processor );

			if ( Tracker_Tag::ELEMENT_ID === $tag['id'] || ( null !== $tag['site'] && self::loads_beacon( $tag['sources'] ) ) ) {
				$tags[] = $tag;
			}
		}

		return $tags;
	}

	/**
	 * The attributes of the script tag the processor stands on that say what it loads and how.
	 *
	 * Its sources are `src` and every `data-…-src`, since tools that hold a script back move its
	 * address into an attribute of their own.
	 *
	 * @param WP_HTML_Tag_Processor $processor A processor standing on a script tag.
	 * @return Beacon_Tag
	 */
	private static function describe( WP_HTML_Tag_Processor $processor ): array {
		$sources = array();

		foreach ( array_merge( array( 'src' ), $processor->get_attribute_names_with_prefix( 'data-' ) ?? array() ) as $name ) {
			$value = $processor->get_attribute( $name );

			if ( ( 'src' === $name || '-src' === substr( $name, -4 ) ) && is_string( $value ) && '' !== $value ) {
				$sources[ $name ] = $value;
			}
		}

		return array(
			'id'        => self::text( $processor->get_attribute( 'id' ) ) ?? '',
			'type'      => strtolower( trim( self::text( $processor->get_attribute( 'type' ) ) ?? '' ) ),
			'sources'   => $sources,
			'site'      => self::text( $processor->get_attribute( 'data-site' ) ),
			'collector' => self::text( $processor->get_attribute( 'data-collector' ) ),
		);
	}

	/**
	 * Whether any of a tag's sources is a `dw.js`.
	 *
	 * @param array<string, string> $sources The tag's sources.
	 */
	private static function loads_beacon( array $sources ): bool {
		foreach ( $sources as $source ) {
			$path = wp_parse_url( $source, PHP_URL_PATH );

			if ( is_string( $path ) && '/dw.js' === substr( $path, -6 ) ) {
				return true;
			}
		}

		return false;
	}

	/**
	 * Whether the plugin's tag reaches visitors as it was printed, and if not, what changed it.
	 *
	 * The collector has to be named by an absolute address, so the beacon sends exactly there. A
	 * tag printed as it should be is still refused by browsers when it loads over http on a page
	 * served over https, which happens when a site moves to https after the address was saved.
	 *
	 * @param array  $tag         The plugin's tag.
	 * @param string $script_url  The address it should load from.
	 * @param string $site_id     The Website ID it should name.
	 * @param bool   $secure_page Whether the page was served over https.
	 * @phpstan-param Beacon_Tag $tag
	 * @return Outcome
	 */
	private static function judge( array $tag, string $script_url, string $site_id, bool $secure_page ): array {
		$tool = isset( $tag['sources']['data-rocket-src'] ) ? self::WP_ROCKET : ( self::TOOLS_BY_TYPE[ $tag['type'] ] ?? null );

		if ( null === $tool && '' !== $tag['type'] && 'text/javascript' !== $tag['type'] ) {
			$tool = '';
		} elseif ( null === $tool && ( $tag['sources']['src'] ?? '' ) !== $script_url ) {
			$tool = self::OPTIMISER;
		} elseif ( null === $tool && ( $site_id !== $tag['site'] || 1 !== preg_match( '~^https?://[^/]~i', (string) $tag['collector'] ) ) ) {
			$tool = '';
		}

		if ( null !== $tool ) {
			return self::outcome( self::ALTERED, $tool );
		}

		return $secure_page && 0 === stripos( $script_url, 'http:' ) ? self::outcome( self::INSECURE ) : self::outcome( self::INTACT );
	}

	/**
	 * An attribute's value when it has text, or nothing when it is absent or written without one.
	 *
	 * @param mixed $value What the processor returned for the attribute.
	 */
	private static function text( $value ): ?string {
		return is_string( $value ) ? $value : null;
	}

	/**
	 * An outcome, with the tool that caused it when one was recognised.
	 *
	 * @param string $outcome The outcome.
	 * @param string $tool    The tool, or an empty string.
	 * @return Outcome
	 */
	private static function outcome( string $outcome, string $tool = '' ): array {
		return array(
			'outcome' => $outcome,
			'tool'    => $tool,
		);
	}

	/**
	 * The home page as an anonymous visitor receives it, and the address that served it, or
	 * nothing when it could not be fetched.
	 *
	 * Redirects are followed only within the site's own host, so a page that sends visitors
	 * elsewhere is reported as one that could not be checked rather than as somebody else's page.
	 *
	 * @return array{url: string, html: string}|null
	 */
	private function fetch_home_page(): ?array {
		$url  = home_url( '/' );
		$host = strtolower( (string) wp_parse_url( $url, PHP_URL_HOST ) );

		for ( $redirects = 0; $redirects <= self::REDIRECTS && '' !== $url; $redirects++ ) {
			$response = wp_remote_get(
				$url,
				array(
					'timeout'     => self::TIMEOUT,
					'redirection' => 0,
					'user-agent'  => 'DewirideAnalytics/' . DEWIRIDE_ANALYTICS_VERSION,
					'headers'     => array( Own_Request::HEADER => Own_Request::marker( $url ) ),
				)
			);

			if ( is_wp_error( $response ) ) {
				return null;
			}

			if ( 200 === (int) wp_remote_retrieve_response_code( $response ) ) {
				return array(
					'url'  => $url,
					'html' => wp_remote_retrieve_body( $response ),
				);
			}

			$url = self::redirect_within( $response, $url, $host );
		}

		return null;
	}

	/**
	 * Where a redirect leads, when it stays on the site's host.
	 *
	 * @param array<string, mixed> $response The response.
	 * @param string               $from     The address that was fetched.
	 * @param string               $host     The site's host.
	 * @return string The address to fetch next, or an empty string to stop.
	 */
	private static function redirect_within( array $response, string $from, string $host ): string {
		$status   = (int) wp_remote_retrieve_response_code( $response );
		$location = wp_remote_retrieve_header( $response, 'location' );

		if ( $status < 300 || $status > 399 || ! is_string( $location ) || '' === $location ) {
			return '';
		}

		$next = WP_Http::make_absolute_url( $location, $from );

		return strtolower( (string) wp_parse_url( $next, PHP_URL_HOST ) ) === $host ? $next : '';
	}
}
