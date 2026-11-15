<?php
/**
 * What the site's own server reports about the request it is answering.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Decides whether the request WordPress is answering is reported, and describes it when it is.
 *
 * What is reported is what a visitor asked WordPress for: pages, and with the settings that allow
 * them, feeds, robots.txt and sitemaps, sign-in attempts and XML-RPC. Never the dashboard, its
 * background requests, scheduled events, the REST API or WP-CLI; never people signed in, unless
 * their role is counted; never a sign-in that succeeds, nor anybody signing in in a browser; and
 * never the site's own requests to itself.
 *
 * A visitor writes every header it sends, and chooses its address's query and its method, so what
 * those say is believed only of a request the site answered without an error. Below 400, a request
 * that calls itself a prefetch, asks for something other than a document, is a `HEAD`, or names
 * one of the site's own programs is left out, as is a page asked for with any method but `GET` and
 * `HEAD`, and a feed while feeds are not chosen. From 400 up none of that is enough, or a scanner
 * could hide each probe behind one of them: whatever its method, a feed's query, a preview's,
 * an embed's or an AMP copy's query, or a REST query on an address that is not the REST API's, it
 * is reported, and only a browser's request for a missing image, font, stylesheet or script is
 * left out.
 */
final class Request_Report {

	/**
	 * A page, whatever WordPress answered it with.
	 */
	private const PAGE = 'page';

	/**
	 * A feed, robots.txt or a sitemap.
	 */
	private const FEED = 'feed';

	/**
	 * A request to the sign-in page.
	 */
	private const SIGN_IN = 'sign-in';

	/**
	 * A request to XML-RPC.
	 */
	private const XML_RPC = 'xml-rpc';

	/**
	 * A REST API request on an address that is not the REST API's own: `?rest_route=` added to
	 * any other path, which WordPress answers from the REST API all the same.
	 */
	private const REST_ELSEWHERE = 'rest-elsewhere';

	/**
	 * Anything else, which is never reported.
	 */
	private const NOT_REPORTED = '';

	/**
	 * The files a browser asks for to draw a page. A browser's request for one that is missing is
	 * a broken page, not a visitor looking for a way in.
	 */
	private const PAGE_FILE = '/\.(?:avif|bmp|css|eot|gif|ico|jpe?g|js|map|mjs|otf|png|svg|ttf|webmanifest|webp|woff2?)$/i';

	/**
	 * The longest page address the collector keeps.
	 */
	private const LONGEST_URL = 2048;

	/**
	 * The longest last part of a page's address kept when the address has to be shortened.
	 */
	private const LONGEST_LAST_SEGMENT = 1024;

	/**
	 * The longest referrer the collector keeps.
	 */
	private const LONGEST_REFERRER = 2048;

	/**
	 * The longest client hint forwarded.
	 */
	private const LONGEST_HINT = 256;

	/**
	 * The longest content type the collector keeps.
	 */
	private const LONGEST_CONTENT_TYPE = 128;

	/**
	 * The longest language tag the collector keeps.
	 */
	private const LONGEST_LANGUAGE = 35;

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
	 * Where the visitor is.
	 *
	 * @var Visitor_Address
	 */
	private $visitor_address;

	/**
	 * Creates the decision.
	 *
	 * @param Settings        $settings        The plugin's settings.
	 * @param Audience        $audience        Who is counted.
	 * @param Visitor_Address $visitor_address Where the visitor is.
	 */
	public function __construct( Settings $settings, Audience $audience, Visitor_Address $visitor_address ) {
		$this->settings        = $settings;
		$this->audience        = $audience;
		$this->visitor_address = $visitor_address;
	}

	/**
	 * Whether the page about to be sent is one to name, so the browser's account of it can be
	 * matched to this one: a browser navigating to a page, for a visitor whose request will be
	 * reported.
	 *
	 * Feeds, robots.txt and sitemaps are not named, because no tracker runs in them to send the
	 * name back.
	 *
	 * @param array $sends_from The addresses reports arrive from.
	 * @phpstan-param list<string> $sends_from
	 */
	public function is_nameable( array $sends_from ): bool {
		return self::PAGE === $this->kind()
			&& 'GET' === Request_Headers::method()
			&& self::is_browser_navigation()
			&& $this->admits( self::PAGE, false )
			&& null !== $this->visitor( $sends_from, false );
	}

	/**
	 * What is reported of the request just answered, or nothing when it is not reported.
	 *
	 * @param string $name         The name the page was sent under, or an empty string.
	 * @param array  $sends_from   The addresses reports arrive from.
	 * @param int    $status       The status the site answered with.
	 * @param string $content_type The content type the site answered with, or an empty string.
	 * @phpstan-param list<string> $sends_from
	 * @return array<string, int|string>|null
	 */
	public function observation( string $name, array $sends_from, int $status, string $content_type ): ?array {
		$kind    = $this->kind();
		$refused = $status >= 400;

		if ( self::NOT_REPORTED === $kind || ! $this->admits( $kind, $refused ) || ! self::answer_admits( $status ) ) {
			return null;
		}

		$address = $this->visitor( $sends_from, $refused );

		if ( null === $address ) {
			return null;
		}

		return array_filter(
			array(
				'kind'          => 'pageview',
				'url'           => self::page_url(),
				'referrer'      => self::referrer(),
				'ipAddress'     => $address,
				'userAgent'     => Request_Headers::header( 'User-Agent' ),
				'mobile'        => Request_Headers::header( 'Sec-CH-UA-Mobile', self::LONGEST_HINT ),
				'platform'      => Request_Headers::header( 'Sec-CH-UA-Platform', self::LONGEST_HINT ),
				'brands'        => Request_Headers::header( 'Sec-CH-UA', self::LONGEST_HINT ),
				'statusCode'    => $status,
				'contentType'   => substr( $content_type, 0, self::LONGEST_CONTENT_TYPE ),
				'language'      => self::language(),
				'observedAt'    => self::observed_at(),
				'correlationId' => $name,
			),
			static function ( $value ): bool {
				return '' !== $value && 0 !== $value;
			}
		);
	}

	/**
	 * The address of the page requested: the site's own scheme and host, never the ones the
	 * request claimed, and the path that was asked for, without its query.
	 *
	 * An address too long for the collector is shortened from the middle, keeping the start of
	 * the path and its last part, which is what names the thing asked for.
	 */
	public static function page_url(): string {
		$home   = wp_parse_url( home_url() );
		$home   = is_array( $home ) ? $home : array();
		$origin = ( $home['scheme'] ?? 'http' ) . '://' . ( $home['host'] ?? '' ) . ( isset( $home['port'] ) ? ':' . $home['port'] : '' );
		$path   = Request_Headers::requested_path();

		if ( strlen( $origin . $path ) <= self::LONGEST_URL ) {
			return $origin . $path;
		}

		$last = self::whole_escapes( substr( (string) substr( $path, (int) strrpos( $path, '/' ) ), 0, self::LONGEST_LAST_SEGMENT ) );
		$head = self::whole_escapes( substr( $path, 0, max( 0, self::LONGEST_URL - strlen( $origin ) - strlen( $last ) ) ) );

		return $origin . $head . $last;
	}

	/**
	 * Which kind of request this is.
	 */
	private function kind(): string {
		if ( ( defined( 'WP_CLI' ) && WP_CLI ) || wp_doing_cron() || wp_doing_ajax() || is_admin() ) {
			return self::NOT_REPORTED;
		}

		$kind = self::NOT_REPORTED;

		if ( defined( 'REST_REQUEST' ) && REST_REQUEST ) {
			$kind = self::is_rest_address() ? self::NOT_REPORTED : self::REST_ELSEWHERE;
		} elseif ( defined( 'XMLRPC_REQUEST' ) && XMLRPC_REQUEST ) {
			$kind = self::XML_RPC;
		} elseif ( did_action( 'login_init' ) > 0 ) {
			$kind = self::SIGN_IN;
		} elseif ( did_action( 'parse_request' ) > 0 ) {
			// From the request being parsed rather than from WordPress being set up for it: a feed
			// answered "not modified" ends before then, and SEO plugins serve their sitemaps while
			// the query is still being run.
			$kind = is_feed() || is_robots() || '' !== get_query_var( 'sitemap' ) ? self::FEED : self::PAGE;
		}

		return $kind;
	}

	/**
	 * Whether a request of this kind, from this visitor, is reported at all.
	 *
	 * A page or a feed the site refused is reported whatever the method, whether or not feeds are
	 * chosen, and whatever the query asked it to be shown as — a preview, an embed, an AMP copy —
	 * as is a REST query on an address that is not the REST API's; of a refusal, only who is
	 * signed in can leave it out.
	 *
	 * @param string $kind    The kind of request.
	 * @param bool   $refused Whether the site answered it with an error.
	 */
	private function admits( string $kind, bool $refused ): bool {
		if ( self::PAGE !== $kind && self::FEED !== $kind && self::REST_ELSEWHERE !== $kind ) {
			return $this->admits_attempt( $kind );
		}

		return $refused ? $this->audience->counts_person() : $this->admits_answered( $kind );
	}

	/**
	 * Whether a page or a feed the site answered without an error is reported: one that was read,
	 * not sent a form; a page as itself, and not the favicon; a feed while feeds are chosen. A REST
	 * query is reported only when it was refused.
	 *
	 * @param string $kind The kind of request.
	 */
	private function admits_answered( string $kind ): bool {
		if ( self::REST_ELSEWHERE === $kind || ! in_array( Request_Headers::method(), array( 'GET', 'HEAD' ), true ) ) {
			return false;
		}

		$chosen = self::PAGE === $kind ? ! is_favicon() : $this->settings->reports_feeds();

		return $chosen && $this->audience->counts_current_visitor();
	}

	/**
	 * Whether a request to the sign-in page or to XML-RPC is reported, while sign-in attempts are
	 * chosen and nobody is signed in.
	 *
	 * A sign-in is reported only as an attempt that did not succeed: anything but a browser, and
	 * either asking for the page or sending a sign-in that WordPress did not accept. Whatever was
	 * sent is not read; that WordPress announced nobody signing in is enough.
	 *
	 * @param string $kind The kind of request.
	 */
	private function admits_attempt( string $kind ): bool {
		$method  = Request_Headers::method();
		$attempt = self::XML_RPC === $kind
			|| ( self::SIGN_IN === $kind && ! self::is_browser_navigation() && ( 'GET' === $method || ( 'POST' === $method && 0 === did_action( 'wp_login' ) ) ) );

		return $attempt && $this->settings->reports_sign_in() && ! is_user_logged_in();
	}

	/**
	 * Whether what the site answered, and the headers that came with the request, leave it in.
	 *
	 * @param int $status The status the site answered with.
	 */
	private static function answer_admits( int $status ): bool {
		$destination = strtolower( Request_Headers::header( 'Sec-Fetch-Dest', 32 ) );
		$for_a_page  = '' === $destination || 'document' === $destination;

		if ( $status >= 400 ) {
			return $for_a_page || 1 !== preg_match( self::PAGE_FILE, Request_Headers::requested_path() );
		}

		return $for_a_page
			&& 'HEAD' !== Request_Headers::method()
			&& ! self::is_prefetch()
			&& ! Own_Request::is_own_agent( Request_Headers::header( 'User-Agent' ) )
			&& ! ( $status >= 300 && self::is_browser_navigation() );
	}

	/**
	 * The visitor's address to report, an empty string to report the request without one, or
	 * nothing when the request is not reported: it is the site's own, or its address cannot be
	 * believed and the site answered it without an error.
	 *
	 * Two of the reasons an address cannot be believed can be brought about by whoever sent the
	 * request — Cloudflare's header sent before Cloudflare's addresses are known, or a request
	 * through another Cloudflare customer's zone — so a request the site refused is reported for
	 * them all the same, without an address, or a probe could hide behind either.
	 *
	 * @param array $sends_from The addresses reports arrive from.
	 * @param bool  $refused    Whether the site answered the request with an error.
	 * @phpstan-param list<string> $sends_from
	 */
	private function visitor( array $sends_from, bool $refused ): ?string {
		$resolved = $this->visitor_address->resolve();
		$address  = $resolved['address'];

		if ( Own_Request::is_marked() || ( '' !== $address && Own_Request::is_own_address( $address, $sends_from ) ) ) {
			return null;
		}

		if ( '' !== $address ) {
			return $address;
		}

		return $refused && in_array( $resolved['cause'], array( Visitor_Address::CLOUDFLARE_PENDING, Visitor_Address::FOREIGN_CLOUDFLARE ), true ) ? '' : null;
	}

	/**
	 * Whether the address asked for is the REST API's own: the site's address, or its index, with
	 * `?rest_route=`, or a path under the REST API's prefix.
	 */
	private static function is_rest_address(): bool {
		$home   = rtrim( (string) wp_parse_url( home_url(), PHP_URL_PATH ), '/' );
		$path   = Request_Headers::requested_path();
		$path   = '' !== $home && ( $path === $home || 0 === strpos( $path, $home . '/' ) ) ? (string) substr( $path, strlen( $home ) ) : $path;
		$prefix = preg_quote( trim( rest_get_url_prefix(), '/' ), '#' );

		return 1 === preg_match( '#^/?(?:index\.php)?/?$|^/(?:index\.php/)?' . $prefix . '(?:/|$)#', $path );
	}

	/**
	 * Whether the request is a browser navigating to a page, which no crawler says of itself.
	 */
	private static function is_browser_navigation(): bool {
		return 'document' === strtolower( Request_Headers::header( 'Sec-Fetch-Dest', 32 ) ) && ! self::is_prefetch();
	}

	/**
	 * Whether the request says it is fetched ahead of being needed: a prefetch or a prerender,
	 * which the browser may never show, and which is counted by the tracker if it is shown.
	 */
	private static function is_prefetch(): bool {
		if ( '' !== Request_Headers::header( 'Sec-Purpose', 64 ) ) {
			return true;
		}

		foreach ( array( 'Purpose', 'X-Purpose', 'X-Moz' ) as $header ) {
			if ( 1 === preg_match( '/prefetch|preview/i', Request_Headers::header( $header, 64 ) ) ) {
				return true;
			}
		}

		return false;
	}

	/**
	 * The page the visitor came from, without anything after its path, or an empty string when
	 * the request named none or named something that is not a web address.
	 */
	private static function referrer(): string {
		$referrer = Request_Headers::header( 'Referer', self::LONGEST_REFERRER );
		$referrer = substr( $referrer, 0, strcspn( $referrer, '?#' ) );
		$parts    = wp_parse_url( $referrer );

		return is_array( $parts ) && in_array( strtolower( $parts['scheme'] ?? '' ), array( 'http', 'https' ), true ) && '' !== ( $parts['host'] ?? '' ) ? $referrer : '';
	}

	/**
	 * The content type among the headers a response carries, or an empty string when it has none.
	 *
	 * @param array<mixed> $headers The headers, each written as `Name: value`.
	 */
	public static function content_type_in( array $headers ): string {
		foreach ( $headers as $header ) {
			if ( is_string( $header ) && 0 === stripos( $header, 'content-type:' ) ) {
				return trim( substr( $header, strlen( 'content-type:' ) ) );
			}
		}

		return '';
	}

	/**
	 * The language the visitor's browser asks for first, or an empty string.
	 */
	private static function language(): string {
		$accepted = Request_Headers::header( 'Accept-Language', 256 );
		$first    = trim( substr( $accepted, 0, strcspn( $accepted, ',;' ) ) );

		return strlen( $first ) <= self::LONGEST_LANGUAGE && 1 === preg_match( '/^[A-Za-z]{1,8}(?:-[A-Za-z0-9]{1,8})*$/', $first ) ? $first : '';
	}

	/**
	 * When the request arrived, in milliseconds since 1970, or 0 where PHP's integers are too
	 * small to hold it and it is left out.
	 */
	private static function observed_at(): int {
		$arrived = isset( $_SERVER['REQUEST_TIME_FLOAT'] ) && is_numeric( $_SERVER['REQUEST_TIME_FLOAT'] ) ? (float) $_SERVER['REQUEST_TIME_FLOAT'] : microtime( true );

		return PHP_INT_SIZE >= 8 ? (int) round( 1000 * $arrived ) : 0;
	}

	/**
	 * A part of an address with an escape cut in half at its end removed.
	 *
	 * @param string $part The part.
	 */
	private static function whole_escapes( string $part ): string {
		return (string) preg_replace( '/%[0-9A-Fa-f]?$/', '', $part );
	}
}
